import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { DesktopReplyAccount, DesktopReplyChannel, DesktopReplyRefusal, DesktopSetup } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { setupFound, chatDesktop, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { listen } from './actions';
import { chatPlace, dropDialogs, heldDialog, replyAccountsAddress, replyCardFor, resetDialogs } from './connect';
import { Painted } from './painted';
import { Console } from './console';
import { attention } from './needs';
import { bind, chats, lines, setup, setupError } from './state';

// The Connect panel's Reply card, its accounts grouped by channel with the state Reply reports, and Disconnect on the
// cards of the notification plugins.

const hostile = '<img src=x onerror="window.hit=1">';

function renderConnect(status: DesktopSetup, firstReadMs = 0): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    const desktop = chatDesktop(status);
    if (firstReadMs > 0) {
        // A first read of the setup that answers late, as one does on a slow or busy machine.
        const read = desktop.setup.status;
        desktop.setup.status = async () => {
            await new Promise((resolve) => setTimeout(resolve, firstReadMs));
            return read();
        };
    }
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

async function openConnect(): Promise<HTMLElement> {
    await waitFor(() => screen.getByRole('button', { name: 'Send' }));
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Connect' }));
    return waitFor(() => {
        const panel = screen.getByRole('heading', { name: 'Connect', level: 1 }).closest<HTMLElement>('.painted')!;
        expect(panel.querySelector('.plugin')).not.toBeNull();
        return panel;
    });
}

const replyCard = (connect: HTMLElement) =>
    within(connect).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

const account = (id: string, channel: string, status: string, name = `Account ${id}`): DesktopReplyAccount => ({
    id,
    channel,
    name,
    address: `${id}@example.test`,
    status,
});

const withAccounts = (accounts: DesktopReplyAccount[]): DesktopSetup => ({
    ...setupFound,
    reply: { ...setupFound.reply, accounts },
});

const statusReads = (desktop: StubDesktop) => desktop.calls.filter((call) => call.name === 'setup.status').length;

const focusBack = () => fireEvent.focus(window);

/** Each account row as a person reads it: the heading over it, its name and its state. */
function rows(card: HTMLElement): [string, string, string][] {
    return Array.from(card.querySelectorAll<HTMLElement>('.acct')).map((row) => [
        words(row.closest('.acct-group')!.querySelector('.acct-hd')!),
        words(row.querySelector('b')!),
        words(row.querySelector('.pillc')!),
    ]);
}

beforeEach(() => {
    // The setup is module state: one test's reading must not stand in for the next test's before its own arrives.
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('the Reply card', () => {
    it('groups the accounts by channel: email, then LinkedIn, then a channel it does not know, by its own name', async () => {
        renderConnect(
            withAccounts([
                account('li1', 'linkedin', 'enabled'),
                account('x1', 'whatsapp', 'connected'),
                account('em1', 'email', 'connected'),
                account('li2', 'linkedin', 'disabled'),
                account('em2', 'email', 'disconnected'),
            ]),
        );
        const card = replyCard(await openConnect());
        expect(Array.from(card.querySelectorAll('.acct-hd'), words)).toEqual(['Email', 'LinkedIn', 'whatsapp']);
        expect(rows(card).map(([heading, name]) => [heading, name])).toEqual([
            ['Email', 'Account em1'],
            ['Email', 'Account em2'],
            ['LinkedIn', 'Account li1'],
            ['LinkedIn', 'Account li2'],
            ['whatsapp', 'Account x1'],
        ]);
    });

    it('shows the accounts however late the first read of the setup answers', async () => {
        renderConnect(
            withAccounts([account('em1', 'email', 'connected'), account('li1', 'linkedin', 'enabled')]),
            1500,
        );
        const card = replyCard(await openConnect());
        expect(Array.from(card.querySelectorAll('.acct-hd'), words)).toEqual(['Email', 'LinkedIn']);
    });

    it('draws no heading for a channel with no account', async () => {
        renderConnect(withAccounts([account('li1', 'linkedin', 'enabled')]));
        const card = replyCard(await openConnect());
        expect(Array.from(card.querySelectorAll('.acct-hd'), words)).toEqual(['LinkedIn']);
    });

    it('draws nothing of the accounts for an empty list', async () => {
        renderConnect(withAccounts([]));
        const card = replyCard(await openConnect());
        expect(card.querySelector('.acct-group')).toBeNull();
        expect(card.querySelector('.acct')).toBeNull();
    });

    it('says each state Reply reports in plain words, an unknown one as its own text, and an absent one as unknown', async () => {
        const long = `pausedForReview${'x'.repeat(45)}`;
        renderConnect(
            withAccounts([
                account('e1', 'email', 'connected'),
                account('e2', 'email', 'disconnected'),
                account('e3', 'email', 'unknown'),
                account('l1', 'linkedin', 'enabled'),
                account('l2', 'linkedin', 'disabled'),
                account('l3', 'linkedin', 'cookieInvalid'),
                account('l4', 'linkedin', 'dailyLimitReached'),
                account('e4', 'email', hostile),
                account('e5', 'email', long),
                account('e6', 'email', ''),
                account('l5', 'linkedin', 'connected'),
            ]),
        );
        const card = replyCard(await openConnect());
        const states = new Map(rows(card).map(([, name, state]) => [name, state]));
        expect(Object.fromEntries(states)).toEqual({
            'Account e1': 'Connected',
            'Account e2': 'Disconnected',
            'Account e3': 'Unknown',
            'Account l1': 'Enabled',
            'Account l2': 'Disabled',
            'Account l3': 'Signed out',
            'Account l4': 'Daily limit reached',
            'Account e4': hostile,
            'Account e5': `${long.slice(0, 40)}…`,
            'Account e6': 'State unknown',
            // A word of the other channel's is not this channel's state: drawn as Reply wrote it.
            'Account l5': 'connected',
        });
        const pill = (name: string) =>
            Array.from(card.querySelectorAll<HTMLElement>('.acct'))
                .find((row) => words(row.querySelector('b')!) === name)!
                .querySelector('.pillc')!.className;
        expect(pill('Account e1')).toBe('pillc green');
        expect(pill('Account e2')).toBe('pillc red');
        expect(pill('Account e3')).toBe('pillc gray');
        expect(pill('Account l1')).toBe('pillc green');
        expect(pill('Account l2')).toBe('pillc gray');
        expect(pill('Account l3')).toBe('pillc red');
        expect(pill('Account l4')).toBe('pillc amber');
        expect(pill('Account e4')).toBe('pillc gray');
        expect(pill('Account e6')).toBe('pillc gray');
        expect(card.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('cuts by characters, never inside one, and leaves forty characters whole', async () => {
        const forty = `${'x'.repeat(39)}😀`;
        renderConnect(withAccounts([account('e1', 'email', `${forty}yy`), account('e2', 'email', forty)]));
        const states = new Map(rows(replyCard(await openConnect())).map(([, name, state]) => [name, state]));
        expect(states.get('Account e1')).toBe(`${forty}…`);
        expect(states.get('Account e2')).toBe(forty);
    });

    it('draws a channel it does not know as inert text, cut to forty characters', async () => {
        const channel = `${hostile}${'y'.repeat(30)}`;
        renderConnect(withAccounts([account('z1', channel, 'connected')]));
        const card = replyCard(await openConnect());
        expect(Array.from(card.querySelectorAll('.acct-hd'), (each) => each.textContent!.trim())).toEqual([
            `${channel.slice(0, 40)}…`,
        ]);
        expect(card.querySelector('img')).toBeNull();
    });

    it('opens Reply for what the card does not do with one plain https link', async () => {
        renderConnect(withAccounts([account('em1', 'email', 'connected'), account('li1', 'linkedin', 'enabled')]));
        const card = replyCard(await openConnect());
        const links = within(card).getAllByRole('link');
        expect(links).toHaveLength(1);
        const link = links[0]! as HTMLAnchorElement;
        expect(words(link)).toBe('Manage accounts in Reply');
        expect(link.getAttribute('href')).toBe(replyAccountsAddress);
        expect(new URL(link.href).protocol).toBe('https:');
        expect(link.tagName).toBe('A');
        expect(link.getAttribute('target')).toBe('_blank');
        expect(link.getAttribute('rel')).toBe('noreferrer');
        expect(link.getAttribute('data-act')).toBeNull();
        expect(words(link.parentElement!)).toContain('Signatures and a LinkedIn account’s limits are set in Reply.');
        for (const row of card.querySelectorAll('.acct')) expect(row.querySelector('a')).toBeNull();
    });
});

describe('the Reply card, a channel it did not list', () => {
    const with_ = (absent: DesktopReplyChannel[], unread: DesktopReplyChannel[]): DesktopSetup => ({
        ...setupFound,
        reply: { ...setupFound.reply, accounts: [account('em1', 'email', 'connected')], absent, unread },
    });
    const noted = (card: HTMLElement) => Array.from(card.querySelectorAll('.acct-none'), words);

    it('says a channel the CLI named as not in the plan or role in one fixed line, beside the other rows, as no error', async () => {
        const card = replyCard((renderConnect(with_(['linkedin'], [])), await openConnect()));
        expect(rows(card)).toEqual([['Email', 'Account em1', 'Connected']]);
        expect(noted(card)).toEqual(['This Reply account has no LinkedIn.']);
        expect(within(card).queryByRole('alert')).toBeNull();
    });

    it('says the same of email', async () => {
        const card = replyCard((renderConnect(with_(['email'], [])), await openConnect()));
        expect(noted(card)).toEqual(['This Reply account has no email.']);
    });

    it('says a channel that could not be read in its own fixed sentence, never as an absent one', async () => {
        const card = replyCard((renderConnect(with_([], ['linkedin'])), await openConnect()));
        expect(rows(card)).toEqual([['Email', 'Account em1', 'Connected']]);
        expect(noted(card)).toEqual(['LinkedIn accounts could not be read. Check again.']);
        expect(card.textContent).not.toContain('has no LinkedIn');
    });

    it('says the unread sentence for email too, and one line for each channel when both ended so', async () => {
        const card = replyCard((renderConnect(with_(['linkedin'], ['email'])), await openConnect()));
        expect(noted(card)).toEqual([
            'This Reply account has no LinkedIn.',
            'Email accounts could not be read. Check again.',
        ]);
    });

    it('says nothing when both channels were listed', async () => {
        renderConnect(withAccounts([account('em1', 'email', 'connected')]));
        expect((await openConnect()).querySelector('.acct-none')).toBeNull();
    });
});

describe('the Reply card, reading again', () => {
    afterEach(() => vi.useRealTimers());

    it('has a Check again button that reads the list again', async () => {
        const desktop = renderConnect(withAccounts([account('em1', 'email', 'connected')]));
        const card = replyCard(await openConnect());
        const before = statusReads(desktop);
        fireEvent.click(within(card).getByRole('button', { name: 'Check again' }));
        await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
    });

    it('reads again when the window takes the focus back, not more often than once in thirty seconds', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-11-12T10:00:00Z'));
        const desktop = renderConnect(withAccounts([account('em1', 'email', 'connected')]));
        await openConnect();
        await waitFor(() => expect(statusReads(desktop)).toBeGreaterThan(0));
        const first = statusReads(desktop);
        vi.setSystemTime(new Date('2026-11-12T10:00:29Z'));
        focusBack();
        await Promise.resolve();
        expect(statusReads(desktop)).toBe(first);
        vi.setSystemTime(new Date('2026-11-12T10:00:31Z'));
        focusBack();
        await waitFor(() => expect(statusReads(desktop)).toBe(first + 1));
        vi.setSystemTime(new Date('2026-11-12T10:00:50Z'));
        focusBack();
        await Promise.resolve();
        expect(statusReads(desktop)).toBe(first + 1);
    });

    it('does not start a read on the focus while one is going', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-11-12T10:00:00Z'));
        const desktop = renderConnect(withAccounts([account('em1', 'email', 'connected')]));
        await openConnect();
        const first = statusReads(desktop);
        let release: () => void = () => undefined;
        const slow = new Promise<DesktopSetup>((resolve) => (release = () => resolve(desktop.status)));
        desktop.setup.status = () => {
            desktop.calls.push({ name: 'setup.status', args: [] });
            return slow;
        };
        vi.setSystemTime(new Date('2026-11-12T10:01:00Z'));
        focusBack();
        vi.setSystemTime(new Date('2026-11-12T10:02:00Z'));
        focusBack();
        expect(statusReads(desktop)).toBe(first + 1);
        release();
    });
});

describe('the Reply card, its acts', () => {
    beforeEach(() => resetDialogs());
    afterEach(() => vi.restoreAllMocks());

    const actCalls = (desktop: StubDesktop) =>
        desktop.calls.filter((call) => call.name.startsWith('setup.reply')).map((call) => [call.name, ...call.args]);
    const dialogOf = (card: () => HTMLElement) => within(card()).getByRole('dialog');
    const addAccount = async (card: () => HTMLElement, choice: string) => {
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        fireEvent.click(await waitFor(() => within(card()).getByRole('button', { name: choice })));
    };
    const rowOf = (card: () => HTMLElement, name: string) =>
        Array.from(card().querySelectorAll<HTMLElement>('.acct')).find(
            (each) => words(each.querySelector('b')!) === name,
        )!;
    const buttonsOf = (row: HTMLElement) => within(row).queryAllByRole('button').map(words);
    const opened = 'The link opened in your browser.';
    const refusal = (
        refused: string,
        code: string | null,
        offer: DesktopReplyRefusal['offer'] = null,
        reread = false,
    ) => ({ refused, code, offer, reread }) satisfies DesktopReplyRefusal;

    it('offers Gmail, Outlook and LinkedIn, and no form that takes a password', async () => {
        renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        const offered = await waitFor(() => card().querySelector<HTMLElement>('.add-account')!);
        expect(within(offered).getAllByRole('button').map(words)).toEqual(['Gmail', 'Outlook', 'LinkedIn', 'Cancel']);
        expect(card().querySelector('input[type="password"]:not([aria-label="Reply API key"])')).toBeNull();
    });

    it('offers no channel the CLI says is not in the plan or role, and nothing before a sign-in', async () => {
        renderConnect({ ...setupFound, reply: { ...setupFound.reply, accounts: [], absent: ['email'] } });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        const offered = await waitFor(() => card().querySelector<HTMLElement>('.add-account')!);
        expect(within(offered).getAllByRole('button').map(words)).toEqual(['LinkedIn', 'Cancel']);
    });

    it('has no Add account and no act on a row before the CLI is signed in', async () => {
        renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                signedIn: false,
                user: null,
                accounts: [account('2', 'email', 'disconnected')],
            },
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(within(card()).queryByRole('button', { name: 'Add account' })).toBeNull();
        expect(buttonsOf(rowOf(card, 'Account 2'))).toEqual([]);
    });

    it('connects a mailbox for its provider: the desktop opens the link, and the card says so and never holds it', async () => {
        const desktop = renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(actCalls(desktop)).toEqual([]);
        await addAccount(card, 'Gmail');
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)).toEqual([['setup.replyConnect', 'email', { provider: 'gmail' }]]);
        const dialog = dialogOf(card);
        expect(words(dialog)).toContain('It works once and connects one account.');
        expect(within(dialog).queryByRole('textbox')).toBeNull();
        expect(within(dialog).queryByRole('link')).toBeNull();
        expect(within(dialog).queryByRole('button', { name: 'Copy link' })).toBeNull();
        const before = statusReads(desktop);
        fireEvent.click(within(dialog).getByRole('button', { name: 'Check now' }));
        await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
        await addAccount(card, 'Outlook');
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)[1]).toEqual(['setup.replyConnect', 'email', { provider: 'office365' }]);
    });

    it('asks a LinkedIn account’s name, then its link, says until when it works, and reads the list again', async () => {
        const desktop = renderConnect(withAccounts([]));
        desktop.actAnswers.set('setup.replyConnect', {
            opened: true,
            channel: 'linkedin',
            kind: 'connect',
            linkKind: 'connection_link',
            expiresAt: '2026-11-19T04:59:00Z',
            pendingId: 51,
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'LinkedIn');
        const name = await waitFor(() =>
            within(card()).getByRole('textbox', { name: 'Name for the LinkedIn account' }),
        );
        fireEvent.click(within(card()).getByRole('button', { name: 'Make the link' }));
        expect(actCalls(desktop)).toEqual([]);
        fireEvent.input(name, { target: { value: '  Made-up Sales  ' } });
        const before = statusReads(desktop);
        fireEvent.click(within(card()).getByRole('button', { name: 'Make the link' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)).toEqual([['setup.replyConnect', 'linkedin', { name: 'Made-up Sales' }]]);
        expect(words(dialogOf(card))).toContain('Valid until 2026-11-19 04:59 UTC');
        await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
    });

    it('offers the shared mailbox page only after the desktop said one-mailbox links are not available, and asks it on a click', async () => {
        const desktop = renderConnect(withAccounts([]));
        const sentence =
            'This Reply does not make a link for one mailbox yet. Its shared mailbox page can be opened instead.';
        desktop.actAnswers.set('setup.replyConnect', [
            refusal(sentence, 'email_link.not_available', 'shared_page'),
            {
                opened: true,
                channel: 'email',
                kind: 'connect',
                linkKind: 'shared_page',
                expiresAt: null,
                pendingId: null,
            },
        ]);
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Gmail');
        const alert = await waitFor(() => within(dialogOf(card)).getByRole('alert'));
        expect(words(alert)).toBe(sentence);
        expect(actCalls(desktop)).toEqual([['setup.replyConnect', 'email', { provider: 'gmail' }]]);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Open the shared mailbox page…' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)[1]).toEqual(['setup.replyConnect', 'email', { provider: 'gmail', sharedPage: true }]);
        expect(words(dialogOf(card))).toContain('shared mailbox page, which does not expire');
    });

    it('copies a new link instead of opening it where the person ticks so, and says until when it works', async () => {
        const desktop = renderConnect(withAccounts([account('14', 'linkedin', 'cookieInvalid')]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const box = within(card()).getByRole('checkbox', {
            name: 'Copy a new link to hand on to whoever owns the account, instead of opening it here',
        }) as HTMLInputElement;
        expect(box.checked).toBe(false);
        fireEvent.click(box);
        desktop.actAnswers.set('setup.replyConnect', {
            copied: true,
            channel: 'email',
            kind: 'connect',
            linkKind: 'connection_link',
            expiresAt: '2026-11-18T12:00:00Z',
            pendingId: 50,
        });
        await addAccount(card, 'Gmail');
        await waitFor(() => expect(words(dialogOf(card))).toContain('The link was copied.'));
        expect(words(dialogOf(card))).toContain('Valid until 2026-11-18 12:00 UTC');
        expect(words(dialogOf(card))).not.toContain(opened);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
        fireEvent.click(within(rowOf(card, 'Account 14')).getByRole('button', { name: 'Reconnect' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain('The link was copied.'));
        expect(actCalls(desktop)).toEqual([
            ['setup.replyConnect', 'email', { provider: 'gmail', copy: true }],
            ['setup.replyReconnect', 'linkedin', '14', { copy: true }],
        ]);
    });

    it('opens the shared mailbox page even where a copy was ticked, and never asks the desktop to copy it', async () => {
        const desktop = renderConnect(withAccounts([]));
        desktop.actAnswers.set('setup.replyConnect', [
            refusal('Its shared mailbox page can be opened instead.', 'email_link.not_available', 'shared_page'),
            {
                opened: true,
                channel: 'email',
                kind: 'connect',
                linkKind: 'shared_page',
                expiresAt: null,
                pendingId: null,
            },
        ]);
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(card()).getByRole('checkbox'));
        await addAccount(card, 'Outlook');
        fireEvent.click(
            await waitFor(() => within(dialogOf(card)).getByRole('button', { name: 'Open the shared mailbox page…' })),
        );
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)).toEqual([
            ['setup.replyConnect', 'email', { provider: 'office365', copy: true }],
            ['setup.replyConnect', 'email', { provider: 'office365', sharedPage: true }],
        ]);
    });

    it('offers each row the acts its channel and state take, and Reconnect only where the state is not good', async () => {
        renderConnect(
            withAccounts([
                account('1', 'email', 'connected'),
                account('2', 'email', 'disconnected'),
                account('11', 'linkedin', 'enabled'),
                account('12', 'linkedin', 'disabled'),
                account('13', 'linkedin', 'dailyLimitReached'),
                account('14', 'linkedin', 'cookieInvalid'),
                account('15', 'linkedin', ''),
                account('21', 'whatsapp', 'broken'),
            ]),
        );
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(
            ['1', '2', '11', '12', '13', '14', '15', '21'].map((id) => [id, buttonsOf(rowOf(card, `Account ${id}`))]),
        ).toEqual([
            ['1', ['Limits', 'Unblock', 'Delete']],
            ['2', ['Reconnect', 'Limits', 'Unblock', 'Delete']],
            ['11', ['Pause', 'Delete']],
            ['12', ['Resume', 'Delete']],
            ['13', ['Pause', 'Delete']],
            ['14', ['Reconnect', 'Delete']],
            ['15', ['Reconnect', 'Delete']],
            ['21', []],
        ]);
    });

    it('asks the desktop for each act by the account’s channel and id, and reads the list again once it is done', async () => {
        const desktop = renderConnect(
            withAccounts([
                account('2', 'email', 'disconnected'),
                account('11', 'linkedin', 'enabled'),
                account('12', 'linkedin', 'disabled'),
            ]),
        );
        const connect = await openConnect();
        const card = () => replyCard(connect);
        for (const [name, label] of [
            ['Account 2', 'Reconnect'],
            ['Account 2', 'Unblock'],
            ['Account 11', 'Pause'],
            ['Account 12', 'Resume'],
            ['Account 11', 'Delete'],
        ] as const) {
            const before = statusReads(desktop);
            fireEvent.click(within(rowOf(card, name)).getByRole('button', { name: label }));
            await waitFor(() => expect(words(dialogOf(card))).toMatch(/Done\.|opened in your browser/));
            await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
            fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
        }
        expect(actCalls(desktop)).toEqual([
            ['setup.replyReconnect', 'email', '2', undefined],
            ['setup.replyUnblock', '2'],
            ['setup.replyPause', '11'],
            ['setup.replyResume', '12'],
            ['setup.replyDelete', 'linkedin', '11'],
        ]);
    });

    it('offers the forced delete only after the desktop said sequences use the account, and never forces first', async () => {
        const desktop = renderConnect(withAccounts([account('11', 'linkedin', 'enabled')]));
        desktop.actAnswers.set('setup.replyDelete', [
            refusal('Sequences use this LinkedIn account, so it was not deleted.', 'linkedin.account_in_use', 'force'),
            { done: true },
        ]);
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(rowOf(card, 'Account 11')).getByRole('button', { name: 'Delete' }));
        await waitFor(() => within(dialogOf(card)).getByRole('alert'));
        expect(actCalls(desktop)).toEqual([['setup.replyDelete', 'linkedin', '11']]);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Delete anyway…' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain('Done.'));
        expect(actCalls(desktop)[1]).toEqual(['setup.replyDelete', 'linkedin', '11', { force: true }]);
    });

    it('offers Reconnect on a signed-out answer, and no Pause or Resume for that account while it stands', async () => {
        const desktop = renderConnect(withAccounts([account('11', 'linkedin', 'enabled')]));
        desktop.actAnswers.set(
            'setup.replyPause',
            refusal('The LinkedIn account is signed out of LinkedIn.', 'linkedin.invalid_cookie', 'reconnect'),
        );
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(rowOf(card, 'Account 11')).getByRole('button', { name: 'Pause' }));
        await waitFor(() => within(dialogOf(card)).getByRole('alert'));
        expect(buttonsOf(rowOf(card, 'Account 11'))).toEqual(['Delete']);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Reconnect' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(actCalls(desktop)[1]).toEqual(['setup.replyReconnect', 'linkedin', '11', undefined]);
    });

    it('reads the list again where the answer says so, offers nothing for a plan’s limit, and says any code inert', async () => {
        const desktop = renderConnect(withAccounts([account('11', 'linkedin', 'enabled')]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const ask = async (answer: DesktopReplyRefusal) => {
            desktop.actAnswers.set('setup.replyPause', answer);
            const before = statusReads(desktop);
            fireEvent.click(within(rowOf(card, 'Account 11')).getByRole('button', { name: 'Pause' }));
            const alert = await waitFor(() => within(dialogOf(card)).getByRole('alert'));
            await new Promise((resolve) => setTimeout(resolve, 0));
            const shown = {
                words: words(alert),
                buttons: buttonsOf(dialogOf(card)),
                reads: statusReads(desktop) - before,
            };
            fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
            return shown;
        };
        expect(
            await ask(refusal('The account’s state changed meanwhile.', 'account.status_changed', null, true)),
        ).toEqual({
            words: 'The account’s state changed meanwhile.',
            buttons: ['Close'],
            reads: 1,
        });
        expect(
            await ask(
                refusal('This Reply account’s plan or role does not include that.', 'access.feature_unavailable'),
            ),
        ).toEqual({
            words: 'This Reply account’s plan or role does not include that.',
            buttons: ['Close'],
            reads: 0,
        });
        const odd = `Reply did not do it: the Reply CLI answered linkedin.new_code ${hostile}.`;
        expect((await ask(refusal(odd, 'linkedin.new_code'))).words).toBe(odd);
        expect(card().querySelector('img')).toBeNull();
        // One ask per click: a reread is a read of the list, never the act again.
        expect(desktop.calls.filter((call) => call.name === 'setup.replyPause')).toHaveLength(3);
    });

    it('reads a mailbox’s daily limit into a field, and sets it to what is typed on Set', async () => {
        const desktop = renderConnect(withAccounts([account('2', 'email', 'connected')]));
        desktop.actAnswers.set('setup.replyLimits', [{ daily: 500 }, { daily: 200 }]);
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(rowOf(card, 'Account 2')).getByRole('button', { name: 'Limits' }));
        const field = await waitFor(
            () => within(card()).getByRole('textbox', { name: 'Daily limit' }) as HTMLInputElement,
        );
        expect(field.value).toBe('500');
        expect(words(card().querySelector('.acct-limit')!)).toContain('now 500');
        expect(within(card()).queryByRole('dialog')).toBeNull();
        fireEvent.input(field, { target: { value: '200' } });
        fireEvent.click(within(card()).getByRole('button', { name: 'Set' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain('Done.'));
        expect(actCalls(desktop)).toEqual([
            ['setup.replyLimits', '2'],
            ['setup.replyLimits', '2', 200],
        ]);
    });

    it('below the Reply CLI the acts need, says so once, offers the desktop’s update and no act', async () => {
        const below = 'Managing Reply accounts here needs the Reply CLI 0.21.0 or later: update the Reply CLI.';
        const desktop = renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                cli: { found: true, version: '0.20.0', install: 'reply' },
                accounts: [account('2', 'email', 'disconnected')],
                belowMinimum: below,
            },
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(Array.from(card().querySelectorAll('.below-minimum'), words)).toEqual([`${below} Update`]);
        expect(within(card()).queryByRole('button', { name: 'Add account' })).toBeNull();
        expect(buttonsOf(rowOf(card, 'Account 2'))).toEqual([]);
        fireEvent.click(within(card()).getByRole('button', { name: 'Update' }));
        await waitFor(() =>
            expect(desktop.calls.filter((call) => call.name === 'setup.installProgram')).toEqual([
                { name: 'setup.installProgram', args: ['reply'] },
            ]),
        );
    });

    it('below the minimum with no npm to update with, says so and offers no button', async () => {
        renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                cli: { found: true, version: '0.20.0', install: null },
                accounts: [],
                belowMinimum: 'Managing Reply accounts here needs the Reply CLI 0.21.0 or later: update the Reply CLI.',
            },
        });
        const card = replyCard(await openConnect());
        expect(within(card.querySelector<HTMLElement>('.below-minimum')!).queryAllByRole('button')).toEqual([]);
    });

    it('turns every act off while one is being asked for, and on again once it answers', async () => {
        const desktop = renderConnect(
            withAccounts([account('14', 'linkedin', 'cookieInvalid'), account('2', 'email', 'disconnected')]),
        );
        let release: () => void = () => undefined;
        desktop.setup.replyReconnect = () =>
            new Promise((resolve) => {
                release = () =>
                    resolve({
                        opened: true,
                        channel: 'linkedin',
                        kind: 'reconnect',
                        linkKind: 'connection_link',
                        expiresAt: null,
                        pendingId: null,
                    });
            });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const acts = () =>
            Array.from(card().querySelectorAll<HTMLButtonElement>('button')).filter(
                (each) =>
                    ['Reconnect', 'Limits', 'Unblock', 'Delete', 'Add account'].includes(words(each)) &&
                    !each.closest('[role="dialog"]'),
            );
        fireEvent.click(within(rowOf(card, 'Account 14')).getByRole('button', { name: 'Reconnect' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain('Asking Reply'));
        expect(acts().length).toBe(7);
        expect(acts().every((each) => each.disabled)).toBe(true);
        release();
        await waitFor(() => expect(words(dialogOf(card))).toContain(opened));
        expect(acts().every((each) => !each.disabled)).toBe(true);
    });

    it('takes its dialog with it when the console goes away', async () => {
        renderConnect(withAccounts([]));
        const connect = await openConnect();
        await addAccount(() => replyCard(connect), 'Gmail');
        await waitFor(() => expect(heldDialog()).toBe('connect'));
        cleanup();
        expect(heldDialog()).toBeNull();
    });

    it('drops an answer that comes after its dialog was closed', async () => {
        const desktop = renderConnect(withAccounts([]));
        let release: () => void = () => undefined;
        desktop.setup.replyConnect = () =>
            new Promise((resolve) => {
                release = () => resolve(refusal(`Late. ${hostile}`, null));
            });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Gmail');
        const dialog = await waitFor(() => dialogOf(card));
        expect(words(dialog)).toContain('Asking Reply');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
        release();
        await new Promise((resolve) => setTimeout(resolve, 0));
        expect(within(card()).queryByRole('dialog')).toBeNull();
        expect(card().textContent).not.toContain('Late.');
    });

    it('reads the list again when an act answers done or asks for a read after its dialog was closed', async () => {
        const desktop = renderConnect(withAccounts([account('11', 'linkedin', 'enabled')]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        for (const late of [
            { done: true } as const,
            refusal('Changed meanwhile.', 'account.status_changed', null, true),
        ]) {
            let release: () => void = () => undefined;
            desktop.setup.replyPause = () => new Promise((resolve) => (release = () => resolve(late)));
            fireEvent.click(within(rowOf(card, 'Account 11')).getByRole('button', { name: 'Pause' }));
            fireEvent.click(within(await waitFor(() => dialogOf(card))).getByRole('button', { name: 'Close' }));
            const before = statusReads(desktop);
            release();
            await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
            expect(within(card()).queryByRole('dialog')).toBeNull();
        }
    });

    it('keeps a dialog to the card it was asked from: another chat’s card never shows it, and a card no longer drawn drops it', async () => {
        const desktop = stubDesktop({ ...setupFound, reply: { ...setupFound.reply, accounts: [] } });
        setup.value = desktop.status;
        const unbind = bind({ api: new FakeApi(), desktop, dock: {} as never });
        const one = replyCardFor('chat_one');
        const two = replyCardFor('chat_two');
        expect(replyCardFor('chat_one')).toBe(one);
        const { container, rerender } = render(
            <>
                <div className="one">
                    <Painted view={one} onGone={() => dropDialogs(chatPlace('chat_one'))} />
                </div>
                <div className="two">
                    <Painted view={two} onGone={() => dropDialogs(chatPlace('chat_two'))} />
                </div>
            </>,
        );
        const stop = listen(container);
        const cardIn = (which: string) => () => container.querySelector<HTMLElement>(`.${which} .card`)!;
        await addAccount(cardIn('one'), 'Gmail');
        await waitFor(() => expect(heldDialog()).toBe(chatPlace('chat_one')));
        expect(within(cardIn('one')()).queryByRole('dialog')).not.toBeNull();
        expect(within(cardIn('two')()).queryByRole('dialog')).toBeNull();
        rerender(
            <>
                <div className="one" />
                <div className="two">
                    <Painted view={two} onGone={() => dropDialogs(chatPlace('chat_two'))} />
                </div>
            </>,
        );
        expect(heldDialog()).toBeNull();
        stop();
        unbind();
    });

    it('lists the links made and not used yet under their channel, by name, state and expiry, with no act', async () => {
        renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [],
                pending: [
                    {
                        channel: 'linkedin',
                        id: '41',
                        name: 'Made-up one',
                        kind: 'connect',
                        expiresAt: '2026-11-19T04:59:00Z',
                        expired: false,
                    },
                    { channel: 'linkedin', id: '42', name: hostile, kind: 'connect', expiresAt: null, expired: true },
                    { channel: 'email', id: '50', name: '', kind: 'reconnect', expiresAt: null, expired: false },
                ],
            },
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const pending = Array.from(card().querySelectorAll<HTMLElement>('.acct-pending'));
        expect(pending.map((row) => words(row.closest('.acct-group')!.querySelector('.acct-hd')!))).toEqual([
            'Email',
            'LinkedIn',
            'LinkedIn',
        ]);
        expect(words(pending[0]!)).toContain('A reconnect');
        expect(words(pending[1]!)).toContain('Made-up one');
        expect(words(pending[1]!)).toContain('Waiting');
        expect(words(pending[1]!)).toContain('2026-11-19 04:59 UTC');
        expect(words(pending[2]!)).toContain('Expired');
        expect(card().querySelector('img')).toBeNull();
        for (const row of pending) expect(within(row).queryAllByRole('button')).toEqual([]);
        // Each group with a waiting link says how to make a new one, once.
        expect(Array.from(card().querySelectorAll('.acct-pending-hint'), words)).toEqual([
            'A link here is not shown again: add the account again to make a new link.',
            'A link here is not shown again: add the account again to make a new link.',
        ]);
    });
});

describe('Disconnect', () => {
    const allConfigured: DesktopSetup = {
        ...setupFound,
        plugins: [
            { ...setupFound.plugins[0]!, id: 'slack', label: 'Slack', configured: true, binding: true },
            { ...setupFound.plugins[0]!, configured: true, binding: true },
            ...setupFound.plugins.slice(1),
        ],
    };
    const card = (connect: HTMLElement, id: string) => connect.querySelector<HTMLElement>(`.plugin[data-id="${id}"]`)!;

    it('is offered on a configured slack, telegram and ntfy, and never on the journal forwarder', async () => {
        renderConnect(allConfigured);
        const connect = await openConnect();
        for (const id of ['slack', 'telegram', 'ntfy'])
            expect(within(card(connect, id)).getAllByRole('button', { name: 'Disconnect' })).toHaveLength(1);
        expect(allConfigured.plugins.find((each) => each.id === 'journal-forwarder')!.configured).toBe(true);
        expect(within(card(connect, 'journal-forwarder')).queryByRole('button', { name: 'Disconnect' })).toBeNull();
    });

    it('is not offered on a plugin that is not configured', async () => {
        renderConnect(setupFound);
        const connect = await openConnect();
        expect(within(card(connect, 'telegram')).queryByRole('button', { name: 'Disconnect' })).toBeNull();
    });

    it('asks once, saying what goes and what stays, and a cancel changes nothing', async () => {
        const desktop = renderConnect(allConfigured);
        const connect = await openConnect();
        fireEvent.click(within(card(connect, 'ntfy')).getByRole('button', { name: 'Disconnect' }));
        const asking = await waitFor(() => within(card(connect, 'ntfy')).getByRole('alertdialog'));
        expect(words(asking)).toContain(
            'Every value under its configuration in the settings file and its notification target are removed. Jason stops sending notifications there. The plugin stays installed.',
        );
        expect(words(asking)).not.toContain('granted');
        expect(desktop.calls.some((each) => each.name === 'setup.disconnectPlugin')).toBe(false);
        fireEvent.click(within(asking).getByRole('button', { name: 'Cancel' }));
        await waitFor(() => expect(within(card(connect, 'ntfy')).queryByRole('alertdialog')).toBeNull());
        expect(desktop.calls.some((each) => each.name === 'setup.disconnectPlugin')).toBe(false);
    });

    it('disconnects through the desktop on the confirmation, then reads the cards again', async () => {
        const desktop = renderConnect(allConfigured);
        const connect = await openConnect();
        const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
        fireEvent.click(within(card(connect, 'slack')).getByRole('button', { name: 'Disconnect' }));
        const asking = await waitFor(() => within(card(connect, 'slack')).getByRole('alertdialog'));
        fireEvent.click(within(asking).getByRole('button', { name: 'Disconnect Slack' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.disconnectPlugin')).toBe(true));
        expect(desktop.calls.filter((each) => each.name === 'setup.disconnectPlugin').map((each) => each.args)).toEqual(
            [['slack']],
        );
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(read + 1));
        expect(within(card(connect, 'slack')).queryByRole('alertdialog')).toBeNull();
    });

    it('forgets a confirmation left open once its plugin is no longer configured, so configuring it again asks afresh', async () => {
        const desktop = renderConnect(allConfigured);
        const connect = await openConnect();
        fireEvent.click(within(card(connect, 'telegram')).getByRole('button', { name: 'Disconnect' }));
        await waitFor(() => within(card(connect, 'telegram')).getByRole('alertdialog'));
        const unconfigured = (configured: boolean): DesktopSetup => ({
            ...allConfigured,
            plugins: allConfigured.plugins.map((each) => (each.id === 'telegram' ? { ...each, configured } : each)),
        });
        desktop.status = unconfigured(false);
        desktop.changed();
        await waitFor(() => expect(within(card(connect, 'telegram')).getByText('Not configured')).toBeTruthy());
        desktop.status = unconfigured(true);
        desktop.changed();
        await waitFor(() => expect(within(card(connect, 'telegram')).getByText('Configured')).toBeTruthy());
        expect(within(card(connect, 'telegram')).queryByRole('alertdialog')).toBeNull();
        expect(within(card(connect, 'telegram')).getAllByRole('button', { name: 'Disconnect' })).toHaveLength(1);
    });

    it('says a Disconnect the desktop refuses as its words, inert, and leaves the card as it stood', async () => {
        const desktop = renderConnect(allConfigured);
        const reason = `The settings file has Notifications:Plugins:slack as something other than an object, so it was left as it is. Edit it by hand, then press Reload. ${hostile}`;
        desktop.refuse.set('setup.disconnectPlugin', reason);
        const connect = await openConnect();
        const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
        fireEvent.click(within(card(connect, 'slack')).getByRole('button', { name: 'Disconnect' }));
        const asking = await waitFor(() => within(card(connect, 'slack')).getByRole('alertdialog'));
        fireEvent.click(within(asking).getByRole('button', { name: 'Disconnect Slack' }));
        const alert = await waitFor(() => within(connect).getByRole('alert'));
        expect(words(alert)).toBe(reason);
        expect(connect.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(read + 1));
        const slack = card(connect, 'slack');
        expect(within(slack).getByText('Configured')).toBeTruthy();
        expect(within(slack).queryByRole('alertdialog')).toBeNull();
        expect(within(slack).getAllByRole('button', { name: 'Disconnect' })).toHaveLength(1);
    });

    it('is not offered while the settings file cannot be kept, as Configure is not', async () => {
        renderConnect({ ...allConfigured, settingsRefused: 'The settings file is not plain JSON.' });
        const connect = await openConnect();
        expect(within(connect).queryByRole('button', { name: 'Disconnect' })).toBeNull();
        expect(within(connect).queryByRole('button', { name: 'Configure' })).toBeNull();
    });
});
