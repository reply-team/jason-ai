import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { DesktopReplyAccount, DesktopReplyChannel, DesktopSetup } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { setupFound, chatDesktop, madeUpLinks, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { listen } from './actions';
import { chatPlace, dropLinks, heldLink, replyAccountsAddress, replyCardFor, resetLinks } from './connect';
import { Painted } from './painted';
import { Console } from './console';
import { attention } from './needs';
import { bind, chats, lines, setup } from './state';

// The Connect panel's Reply card, its accounts grouped by channel with the state Reply reports, and Disconnect on the
// cards of the notification plugins.

const hostile = '<img src=x onerror="window.hit=1">';

function renderConnect(status: DesktopSetup): StubDesktop {
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

    it('opens Reply for everything else with one plain https link, and no button per account', async () => {
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
        for (const row of card.querySelectorAll('.acct')) expect(row.querySelector('button, a')).toBeNull();
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

describe('the Reply card, connecting an account by a link', () => {
    const clipboard = { writeText: vi.fn((text: string) => Promise.resolve(text.length)) };
    beforeEach(() => {
        resetLinks();
        clipboard.writeText.mockClear();
        Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });
    });
    afterEach(() => {
        Reflect.deleteProperty(navigator, 'clipboard');
        vi.restoreAllMocks();
    });

    const linkCalls = (desktop: StubDesktop) =>
        desktop.calls.filter((call) => call.name.startsWith('setup.reply')).map((call) => [call.name, ...call.args]);
    const dialogOf = (card: () => HTMLElement) => within(card()).getByRole('dialog');
    const addAccount = async (card: () => HTMLElement, choice: string) => {
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        fireEvent.click(await waitFor(() => within(card()).getByRole('button', { name: choice })));
    };
    const mailboxWords =
        "No Reply sign-in is needed, so you can send this link to whoever owns the mailbox. Anyone who has it can add and manage this Reply account's mailboxes, and it does not expire: send it only to someone you trust.";
    const linkedinWords =
        'No Reply sign-in is needed, so you can send this link to whoever owns the account. Anyone who has it can connect an account to this Reply account: send it only to them.';
    const limited = 'Valid for a limited time; if it stopped working, make a new one.';
    const stored = () => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage });

    it('offers Gmail, Outlook, another mailbox and LinkedIn, and no form that takes a password', async () => {
        renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        const offered = await waitFor(() => card().querySelector<HTMLElement>('.add-account')!);
        expect(within(offered).getAllByRole('button').map(words)).toEqual([
            'Gmail',
            'Outlook',
            'Another mailbox',
            'LinkedIn',
            'Cancel',
        ]);
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

    it('has no Add account before the CLI is signed in', async () => {
        renderConnect({ ...setupFound, reply: { ...setupFound.reply, signedIn: false, user: null, accounts: [] } });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(within(card()).queryByRole('button', { name: 'Add account' })).toBeNull();
    });

    it('asks Reply for Gmail’s page on the click, and shows it with its host, Copy link, Open in the browser and Check now', async () => {
        const desktop = renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(linkCalls(desktop)).toEqual([]);
        await addAccount(card, 'Gmail');
        await waitFor(() => expect(within(dialogOf(card)).getByRole('textbox', { name: 'Link' })).toBeTruthy());
        expect(linkCalls(desktop)).toEqual([['setup.replyMailboxLink', 'gmail']]);
        const dialog = dialogOf(card);
        expect((within(dialog).getByRole('textbox', { name: 'Link' }) as HTMLInputElement).value).toBe(
            madeUpLinks.gmail,
        );
        expect(words(dialog.querySelector('.link-host')!)).toBe('run.reply.io');
        expect(words(dialog)).toContain(mailboxWords);
        expect(words(dialog)).not.toContain(limited);
        const open = within(dialog).getByRole('link', { name: 'Open in the browser' }) as HTMLAnchorElement;
        expect(open.getAttribute('href')).toBe(madeUpLinks.gmail);
        expect(open.getAttribute('target')).toBe('_blank');
        expect(open.getAttribute('rel')).toBe('noreferrer');

        fireEvent.click(within(dialog).getByRole('button', { name: 'Copy link' }));
        await waitFor(() => expect(clipboard.writeText).toHaveBeenCalledWith(madeUpLinks.gmail));
        await waitFor(() => expect(words(dialogOf(card))).toContain('Copied.'));

        const before = statusReads(desktop);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Check now' }));
        await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
    });

    it('asks Outlook’s page and the hosted page for another mailbox, each by its own name', async () => {
        const desktop = renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Outlook');
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect((within(dialogOf(card)).getByRole('textbox', { name: 'Link' }) as HTMLInputElement).value).toBe(
            madeUpLinks.outlook,
        );
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
        await addAccount(card, 'Another mailbox');
        await waitFor(() =>
            expect((within(dialogOf(card)).getByRole('textbox', { name: 'Link' }) as HTMLInputElement).value).toBe(
                madeUpLinks.hosted,
            ),
        );
        expect(linkCalls(desktop)).toEqual([
            ['setup.replyMailboxLink', 'outlook'],
            ['setup.replyMailboxLink', 'hosted'],
        ]);
    });

    it('asks a LinkedIn account’s name, then a link for it, says it is valid for a limited time, and reads the list again', async () => {
        const desktop = renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'LinkedIn');
        const name = await waitFor(() =>
            within(card()).getByRole('textbox', { name: 'Name for the LinkedIn account' }),
        );
        fireEvent.click(within(card()).getByRole('button', { name: 'Make the link' }));
        expect(linkCalls(desktop)).toEqual([]);
        fireEvent.input(name, { target: { value: '  Made-up Sales  ' } });
        const before = statusReads(desktop);
        fireEvent.click(within(card()).getByRole('button', { name: 'Make the link' }));
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect(linkCalls(desktop)).toEqual([['setup.replyLinkedInConnect', 'Made-up Sales']]);
        const dialog = dialogOf(card);
        expect(words(dialog)).toContain(linkedinWords);
        expect(words(dialog)).toContain(limited);
        expect(words(dialog.querySelector('.link-host')!)).toBe('run.reply.io');
        await waitFor(() => expect(statusReads(desktop)).toBe(before + 1));
    });

    it('forgets the link when the dialog closes: nothing of it stays on the page or in the page’s storage', async () => {
        const desktop = renderConnect(withAccounts([]));
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Gmail');
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect(document.body.innerHTML).toContain('madeUpToken_ForTests');
        expect(heldLink()).toBe(madeUpLinks.gmail);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(within(card()).queryByRole('dialog')).toBeNull());
        expect(heldLink()).toBeNull();
        expect(document.body.innerHTML).not.toContain('madeUpToken_ForTests');
        expect(stored()).not.toContain('madeUpToken_ForTests');
        // A read of the list afterwards brings nothing of it back.
        desktop.changed();
        await waitFor(() => expect(statusReads(desktop)).toBeGreaterThan(0));
        expect(document.body.innerHTML).not.toContain('madeUpToken_ForTests');
    });

    it('takes the link with it when the console goes away', async () => {
        renderConnect(withAccounts([]));
        const connect = await openConnect();
        await addAccount(() => replyCard(connect), 'Gmail');
        await waitFor(() => expect(heldLink()).toBe(madeUpLinks.gmail));
        cleanup();
        expect(heldLink()).toBeNull();
    });

    it('drops an answer that comes after its dialog was closed', async () => {
        const desktop = renderConnect(withAccounts([]));
        let release: () => void = () => undefined;
        desktop.setup.replyMailboxLink = () =>
            new Promise((resolve) => {
                release = () =>
                    resolve({ kind: 'mailbox', link: madeUpLinks.gmail, host: 'run.reply.io', expiresAtUtc: null });
            });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Gmail');
        const dialog = await waitFor(() => dialogOf(card));
        expect(words(dialog)).toContain('Asking Reply for the link');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
        release();
        await new Promise((resolve) => setTimeout(resolve, 0));
        expect(within(card()).queryByRole('dialog')).toBeNull();
        expect(document.body.innerHTML).not.toContain('madeUpToken_ForTests');
    });

    it('says a refusal as the desktop’s sentence, inert, in the dialog', async () => {
        const desktop = renderConnect(withAccounts([]));
        desktop.linkAnswers.set('setup.replyMailboxLink', { refused: `Not on for this plan. ${hostile}` });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        await addAccount(card, 'Gmail');
        const alert = await waitFor(() => within(dialogOf(card)).getByRole('alert'));
        expect(words(alert)).toBe(`Not on for this plan. ${hostile}`);
        expect(card().querySelector('img')).toBeNull();
        expect(within(dialogOf(card)).queryByRole('link')).toBeNull();
    });

    it('offers Reconnect on a row that is not in a good state, and none on one that is', async () => {
        renderConnect(
            withAccounts([
                account('1', 'email', 'connected'),
                account('2', 'email', 'disconnected'),
                account('3', 'email', 'unknown'),
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
        const reconnectable = Array.from(card().querySelectorAll<HTMLElement>('.acct'))
            .filter((row) => within(row).queryByRole('button', { name: 'Reconnect' }) !== null)
            .map((row) => words(row.querySelector('b')!));
        expect(reconnectable).toEqual(['Account 2', 'Account 3', 'Account 14', 'Account 15']);
    });

    it('reconnects a LinkedIn account by its id, and a mailbox through the page of the provider the person picks', async () => {
        const desktop = renderConnect(
            withAccounts([account('2', 'email', 'disconnected'), account('14', 'linkedin', 'cookieInvalid')]),
        );
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const row = (name: string) =>
            Array.from(card().querySelectorAll<HTMLElement>('.acct')).find(
                (each) => words(each.querySelector('b')!) === name,
            )!;
        fireEvent.click(within(row('Account 14')).getByRole('button', { name: 'Reconnect' }));
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect(linkCalls(desktop)).toEqual([['setup.replyLinkedInReconnect', '14']]);
        fireEvent.click(within(dialogOf(card)).getByRole('button', { name: 'Close' }));

        fireEvent.click(within(row('Account 2')).getByRole('button', { name: 'Reconnect' }));
        const choosing = await waitFor(() => dialogOf(card));
        expect(linkCalls(desktop)).toHaveLength(1);
        expect(words(choosing)).toContain('makes no second one');
        fireEvent.click(within(choosing).getByRole('button', { name: 'Outlook' }));
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect(linkCalls(desktop)[1]).toEqual(['setup.replyMailboxLink', 'outlook']);
        expect(words(dialogOf(card))).toContain(mailboxWords);
    });

    it('turns the LinkedIn acts off below the Reply CLI that reads a body from standard input, saying so once', async () => {
        const floor = 'Connecting LinkedIn accounts here needs the Reply CLI 0.20.0 or later: update the Reply CLI.';
        renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [account('14', 'linkedin', 'cookieInvalid'), account('2', 'email', 'disconnected')],
                linkedinPending: [{ id: '41', name: 'Made-up one', expiresAtUtc: null, isExpired: false }],
                linkedinBelowFloor: floor,
            },
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        expect(Array.from(card().querySelectorAll('.link-floor'), words)).toEqual([floor]);
        const button = (row: Element, name: string) =>
            within(row as HTMLElement).getByRole('button', { name }) as HTMLButtonElement;
        const linkedinRow = Array.from(card().querySelectorAll('.acct')).find((row) =>
            words(row).includes('Account 14'),
        )!;
        const emailRow = Array.from(card().querySelectorAll('.acct')).find((row) => words(row).includes('Account 2'))!;
        expect(button(linkedinRow, 'Reconnect').disabled).toBe(true);
        expect(button(emailRow, 'Reconnect').disabled).toBe(false);
        const pending = card().querySelector('.acct-pending')!;
        expect(button(pending, 'Copy link').disabled).toBe(true);
        expect(button(pending, 'Open').disabled).toBe(true);
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        const offered = await waitFor(() => card().querySelector<HTMLElement>('.add-account')!);
        expect(button(offered, 'LinkedIn').disabled).toBe(true);
        expect(button(offered, 'Gmail').disabled).toBe(false);
    });

    it('turns every ask off while a link is being asked for, and on again once it comes', async () => {
        const desktop = renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [account('14', 'linkedin', 'cookieInvalid'), account('2', 'email', 'disconnected')],
                linkedinPending: [{ id: '41', name: 'Made-up one', expiresAtUtc: null, isExpired: false }],
            },
        });
        let release: () => void = () => undefined;
        desktop.setup.replyLinkedInReconnect = () =>
            new Promise((resolve) => {
                release = () =>
                    resolve({ kind: 'linkedin', link: madeUpLinks.linkedin, host: 'run.reply.io', expiresAtUtc: null });
            });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const asks = () =>
            Array.from(card().querySelectorAll<HTMLButtonElement>('button')).filter(
                (each) =>
                    ['Reconnect', 'Copy link', 'Open', 'Add account'].includes(words(each)) &&
                    !each.closest('[role="dialog"]'),
            );
        const linkedinRow = Array.from(card().querySelectorAll<HTMLElement>('.acct')).find((row) =>
            words(row).includes('Account 14'),
        )!;
        fireEvent.click(within(linkedinRow).getByRole('button', { name: 'Reconnect' }));
        await waitFor(() => expect(words(dialogOf(card))).toContain('Asking Reply for the link'));
        expect(asks().length).toBe(5);
        expect(asks().every((each) => each.disabled)).toBe(true);
        release();
        await waitFor(() => within(dialogOf(card)).getByRole('textbox', { name: 'Link' }));
        expect(asks().every((each) => !each.disabled)).toBe(true);
    });

    it('keeps a link to the card it was asked from: another chat’s card never shows it, and a card no longer drawn drops it', async () => {
        const desktop = stubDesktop({ ...setupFound, reply: { ...setupFound.reply, accounts: [] } });
        setup.value = desktop.status;
        const unbind = bind({ api: new FakeApi(), desktop, dock: {} as never });
        const one = replyCardFor('chat_one');
        const two = replyCardFor('chat_two');
        expect(replyCardFor('chat_one')).toBe(one);
        const { container, rerender } = render(
            <>
                <div className="one">
                    <Painted view={one} onGone={() => dropLinks(chatPlace('chat_one'))} />
                </div>
                <div className="two">
                    <Painted view={two} onGone={() => dropLinks(chatPlace('chat_two'))} />
                </div>
            </>,
        );
        const stop = listen(container);
        const cardIn = (which: string) => () => container.querySelector<HTMLElement>(`.${which} .card`)!;
        await addAccount(cardIn('one'), 'Gmail');
        await waitFor(() => expect(heldLink()).toBe(madeUpLinks.gmail));
        expect(within(cardIn('one')()).queryByRole('dialog')).not.toBeNull();
        expect(within(cardIn('two')()).queryByRole('dialog')).toBeNull();
        rerender(
            <>
                <div className="one" />
                <div className="two">
                    <Painted view={two} onGone={() => dropLinks(chatPlace('chat_two'))} />
                </div>
            </>,
        );
        expect(heldLink()).toBeNull();
        stop();
        unbind();
    });

    it('lists the connections that wait under LinkedIn, by name, state and expiry, and asks the link again on a click', async () => {
        const desktop = renderConnect({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [],
                linkedinPending: [
                    { id: '41', name: 'Made-up one', expiresAtUtc: '2026-11-19T04:59:00Z', isExpired: false },
                    { id: '42', name: hostile, expiresAtUtc: null, isExpired: true },
                ],
            },
        });
        const connect = await openConnect();
        const card = () => replyCard(connect);
        const pending = Array.from(card().querySelectorAll<HTMLElement>('.acct-pending'));
        expect(pending.map((row) => words(row.closest('.acct-group')!.querySelector('.acct-hd')!))).toEqual([
            'LinkedIn',
            'LinkedIn',
        ]);
        expect(words(pending[0]!)).toContain('Made-up one');
        expect(words(pending[0]!)).toContain('Waiting');
        expect(words(pending[0]!)).toContain('2026-11-19 04:59 UTC');
        expect(words(pending[1]!)).toContain('Expired');
        expect(card().querySelector('img')).toBeNull();
        expect(card().innerHTML).not.toContain('linkedin-custom-connect');

        desktop.linkAnswers.set('setup.replyLinkedInPendingLink', {
            kind: 'linkedin',
            link: madeUpLinks.linkedin,
            host: 'run.reply.io',
            expiresAtUtc: '2026-11-19T04:59:00Z',
        });
        fireEvent.click(within(pending[0]!).getByRole('button', { name: 'Copy link' }));
        await waitFor(() => expect(clipboard.writeText).toHaveBeenCalledWith(madeUpLinks.linkedin));
        expect(linkCalls(desktop)).toEqual([['setup.replyLinkedInPendingLink', '41']]);
        const dialog = dialogOf(card);
        expect(words(dialog)).toContain('Valid until 2026-11-19 04:59 UTC');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));

        const opened = vi.spyOn(window, 'open').mockReturnValue(null);
        fireEvent.click(
            within(card().querySelectorAll<HTMLElement>('.acct-pending')[1]!).getByRole('button', { name: 'Open' }),
        );
        await waitFor(() => expect(opened).toHaveBeenCalledWith(madeUpLinks.linkedin, '_blank', 'noopener,noreferrer'));
        expect(linkCalls(desktop)[1]).toEqual(['setup.replyLinkedInPendingLink', '42']);
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
