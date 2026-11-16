import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import type { DesktopConversationRecord, DesktopKeptEntry, DesktopSetup } from '../platform/desktop';
import { loadMode } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { journalLine, setupFound, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import { clock } from '../time/format';
import { acts } from './card-acts';
import { accountsHeld } from './card-accounts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The accounts card the chat's AI asks for, as a person meets it: under its header, when it was read and a Refresh, the
// provider's part as the desktop reads Reply now (the sign-in, the accounts with Reply's own words for their state, the
// channels Reply did not list and why) and, under "On record now", the plugin the runtime routes sends to and its state
// as the runtime answers it. It reads once when drawn and again only on its Refresh, never on a journal line or a change
// the desktop reports; it acts on nothing, and in a browser tab it draws the runtime's part and says where connecting is
// done.

const setupChat = 'conv_01JDACCOUNTS00000000000000S';
const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const asking = (text = `Here is where your accounts stand.\n\n${fenced({ kind: 'accounts' })}`): DesktopKeptEntry => ({
    kind: 'text',
    text,
});
const setUp = (entries: DesktopKeptEntry[]) => ({ id: setupChat, campaignId: null, title: 'Sending', entries });
const turn = (): DesktopKeptEntry[] => [{ kind: 'user', text: 'Which accounts can send?' }, asking()];

/** Reply as the desktop reads it: signed in and used by Jason, with an account in each of Reply's words for a state. */
const everyState: DesktopSetup = {
    ...setupFound,
    reply: {
        ...setupFound.reply,
        user: 'owner@example.test',
        accounts: [
            { id: 'a1', channel: 'email', name: 'Sales inbox', address: 'sales@example.test', status: 'connected' },
            { id: 'a2', channel: 'email', name: 'Old inbox', address: 'old@example.test', status: 'disconnected' },
            { id: 'a3', channel: 'email', name: 'Shared inbox', address: 'team@example.test', status: 'unknown' },
            { id: 'a4', channel: 'linkedin', name: 'Owner Example', address: 'owner-li', status: 'enabled' },
            { id: 'a5', channel: 'linkedin', name: 'Second Seat', address: 'second-li', status: 'disabled' },
            { id: 'a6', channel: 'linkedin', name: 'Third Seat', address: 'third-li', status: 'cookieInvalid' },
            { id: 'a7', channel: 'linkedin', name: 'Fourth Seat', address: 'fourth-li', status: 'dailyLimitReached' },
            { id: 'a8', channel: 'email', name: 'New inbox', address: 'new@example.test', status: 'warmingUp' },
            { id: 'a9', channel: 'email', name: 'Quiet inbox', address: 'quiet@example.test', status: '' },
        ],
    },
};

function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    desktop: StubDesktop = stubDesktop(everyState),
    fake: FakeApi = new FakeApi(),
    journal: StubStream = stubStream(),
): { fake: FakeApi; desktop: StubDesktop; journal: StubStream } {
    fake.answer('plugin.list', 'plugin.list/card-accounts-reply').answer(
        'route.list',
        'route.list/card-accounts-reply',
    );
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
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
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { fake, desktop, journal };
}

async function openChat(title: string): Promise<HTMLElement> {
    await waitFor(() => {
        const list = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', {
            name: 'Conversations',
        });
        fireEvent.click(within(list).getByRole('button', { name: title }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

const card = (pane: HTMLElement) => within(pane).getByRole('group', { name: 'Accounts' });
/** The card once it has read: its "As of" is there and nothing is being read. */
const read = (pane: HTMLElement) =>
    waitFor(() => {
        const shown = card(pane);
        expect(within(shown).getByText(/^As of /)).toBeTruthy();
        expect(within(shown).queryByText('Reading…')).toBeNull();
        return shown;
    });
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const count = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name).length;
const asked = (fake: FakeApi, operation: string) => fake.calls.filter((each) => each.operation === operation).length;
const rows = (shown: HTMLElement) =>
    [...shown.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map((cell) => words(cell)));

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    accountsHeld.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.loaded.value = false;
    attention.detail.value = null;
    forgetWaiting();
    feedId.value = null;
    feedUnseen.value = false;
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('the accounts card', () => {
    it('draws Reply as the desktop read it now, each state in Reply’s own word, and the route the runtime holds', async () => {
        renderConsole([setUp(turn())]);
        const pane = await openChat('Sending');
        const shown = await read(pane);
        expect(words(shown).startsWith('Accounts')).toBe(true);
        // The block itself never reaches the page; the words before it keep the speaker's name, the card has none.
        expect(pane.textContent).not.toContain('"kind"');
        expect(within(pane).getByText('Here is where your accounts stand.')).toBeTruthy();
        expect(shown.querySelector('.who')).toBeNull();
        const provider = within(shown).getByRole('region', { name: 'Read from Reply now' });
        expect(words(provider)).toContain('Signed in to Reply as owner@example.test');
        expect(words(provider)).toContain('Jason uses this sign-in');
        expect(words(provider)).toContain('9 accounts');
        expect([...provider.querySelectorAll('th')].map((each) => words(each))).toEqual([
            'Address',
            'Name at Reply',
            'Channel',
            'Status at Reply',
        ]);
        expect(rows(provider)).toEqual([
            ['sales@example.test', 'Sales inbox', 'Email', 'connected'],
            ['old@example.test', 'Old inbox', 'Email', 'disconnected'],
            ['team@example.test', 'Shared inbox', 'Email', 'unknown'],
            ['owner-li', 'Owner Example', 'LinkedIn', 'enabled'],
            ['second-li', 'Second Seat', 'LinkedIn', 'disabled'],
            ['third-li', 'Third Seat', 'LinkedIn', 'cookieInvalid'],
            ['fourth-li', 'Fourth Seat', 'LinkedIn', 'dailyLimitReached'],
            ['new@example.test', 'New inbox', 'Email', 'warmingUp'],
            ['quiet@example.test', 'Quiet inbox', 'Email', 'none given'],
        ]);
        const record = within(shown).getByRole('region', { name: 'On record now' });
        expect(words(record)).toContain('Plugin that sends messages by default Reply (reply)');
        expect(words(record)).toContain('Its state: valid');
        // Nothing of the provider's stands under the runtime's facts, and nothing of the runtime's under Reply's.
        expect(words(record)).not.toContain('example.test');
        expect(words(provider)).not.toContain('Its state');
    });

    it('says when Reply is not signed in, and has no table of accounts', async () => {
        const out: DesktopSetup = {
            ...setupFound,
            reply: { ...setupFound.reply, signedIn: false, user: null, wired: false, accounts: [] },
        };
        renderConsole([setUp(turn())], stubDesktop(out));
        const pane = await openChat('Sending');
        const provider = within(await read(pane)).getByRole('region', { name: 'Read from Reply now' });
        expect(words(provider)).toContain('Not signed in to Reply');
        expect(words(provider)).toContain('Jason does not use a Reply sign-in yet');
        expect(words(provider)).toContain('No accounts');
        expect(provider.querySelector('table')).toBeNull();
    });

    it('says which channels Reply did not list, and why', async () => {
        const partly: DesktopSetup = {
            ...setupFound,
            reply: { ...setupFound.reply, absent: ['linkedin'], unread: ['email'], accounts: [] },
        };
        renderConsole([setUp(turn())], stubDesktop(partly));
        const pane = await openChat('Sending');
        const provider = within(await read(pane)).getByRole('region', { name: 'Read from Reply now' });
        expect(
            within(provider).getByText('LinkedIn is not in this Reply account’s plan or role, so Reply lists none.'),
        ).toBeTruthy();
        expect(within(provider).getByText('Email accounts could not be read from Reply.')).toBeTruthy();
    });

    it('is drawn in an agent’s chat as in a set-up chat, its turn’s words after it named as the speaker’s', async () => {
        const after = `${fenced({ kind: 'accounts' })}\n\nThe first one sends.`;
        renderConsole([
            {
                id: 'conv_01JDACCOUNTS00000000000000A',
                campaignId: 'cmp_1',
                title: 'Spring',
                entries: [{ kind: 'user', text: 'Which accounts can send?' }, asking(after)],
            },
        ]);
        const pane = await openChat('Spring');
        const shown = await read(pane);
        expect(within(shown).getByText('sales@example.test')).toBeTruthy();
        expect(shown.querySelector('.who')).toBeNull();
        expect(words(pane.querySelector('.m.ai')!)).toBe('Jason The first one sends.');
    });

    it('reads once when drawn and again only on its Refresh: never on a journal line or a change the desktop reports', async () => {
        const at = new Date('2026-10-16T09:05:00.000Z').getTime();
        const now = vi.spyOn(Date, 'now').mockReturnValue(at);
        const { desktop, fake, journal } = renderConsole([setUp(turn())]);
        // What the console itself reads of the desktop as it starts is counted before the card is drawn.
        await waitFor(() => expect(count(desktop, 'setup.status')).toBeGreaterThan(0));
        await new Promise((resolve) => setTimeout(resolve, 50));
        const before = count(desktop, 'setup.status');
        const pane = await openChat('Sending');
        let shown = await read(pane);
        expect(count(desktop, 'setup.status')).toBe(before + 1);
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([1, 1]);
        expect(within(shown).getByText(`As of ${clock(new Date(at), zone)}`)).toBeTruthy();

        // A journal line and a change the desktop reports leave the card as it was read.
        desktop.status = {
            ...everyState,
            reply: {
                ...everyState.reply,
                accounts: [
                    {
                        id: 'b1',
                        channel: 'email',
                        name: 'Later inbox',
                        address: 'later@example.test',
                        status: 'connected',
                    },
                ],
            },
        };
        flush(() => {
            journal.open();
            journal.send(
                journalLine({
                    id: 'jrn_31',
                    kind: 'campaign_paused',
                    campaign_id: 'cmp_1',
                    ts: '2026-10-16T09:06:00.000Z',
                    actor: { type: 'human', id: 'ada' },
                }),
            );
        });
        const console = count(desktop, 'setup.status');
        flush(() => desktop.changed());
        await new Promise((resolve) => setTimeout(resolve, 50));
        shown = card(pane);
        expect(within(shown).queryByText('later@example.test')).toBeNull();
        expect(within(shown).getByText('sales@example.test')).toBeTruthy();
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([1, 1]);
        // The console reads the desktop again on its change; the card adds no read of its own.
        const afterChange = count(desktop, 'setup.status');
        expect(afterChange - console).toBeLessThanOrEqual(1);

        // Refresh reads all three again, once, and draws the new answer at the new moment.
        now.mockReturnValue(at + 3 * 60_000);
        fireEvent.click(within(shown).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane)).getByText('later@example.test'));
        expect(count(desktop, 'setup.status')).toBe(afterChange + 1);
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([2, 2]);
        expect(within(card(pane)).getByText(`As of ${clock(new Date(at + 3 * 60_000), zone)}`)).toBeTruthy();
    });

    it('says it is reading while a read is under way, with its Refresh off', async () => {
        const fake = new FakeApi();
        const release = fake.hold('route.list');
        renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        const shown = await waitFor(() => {
            const each = card(pane);
            expect(within(each).getByText('Reading…')).toBeTruthy();
            return each;
        });
        expect(within(shown).getByRole('button', { name: 'Refresh' }).hasAttribute('disabled')).toBe(true);
        expect(within(shown).queryByText(/^As of /)).toBeNull();
        flush(() => release());
        const done = await read(pane);
        expect(within(done).getByRole('button', { name: 'Refresh' }).hasAttribute('disabled')).toBe(false);
    });

    it('acts on nothing: its only buttons are Refresh and Open Accounts, which opens the Accounts page', async () => {
        const { desktop } = renderConsole([setUp(turn())]);
        const pane = await openChat('Sending');
        const shown = await read(pane);
        expect(
            within(shown)
                .getAllByRole('button')
                .map((each) => words(each)),
        ).toEqual(['Refresh', 'Open Accounts']);
        expect(shown.querySelector('a, input, select, textarea, form')).toBeNull();
        const acted = desktop.calls.filter((each) => /^setup\.(reply|connect|configure|disconnect)/.test(each.name));
        fireEvent.click(within(shown).getByRole('button', { name: 'Open Accounts' }));
        await waitFor(() => expect(route.value).toEqual({ screen: 'accounts' }));
        expect(desktop.calls.filter((each) => /^setup\.(reply|connect|configure|disconnect)/.test(each.name))).toEqual(
            acted,
        );
    });

    it('draws each part it could read when the other’s read failed, with the reason in the reader’s own words', async () => {
        const fake = new FakeApi();
        const back = fake.unreachable('route.list');
        const { desktop } = renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        const shown = await read(pane);
        expect(words(within(shown).getByRole('region', { name: 'On record now' }))).toContain(
            'The runtime could not be read: Failed to fetch',
        );
        expect(within(shown).getByText('sales@example.test')).toBeTruthy();
        // The desktop's read refused: the runtime's part stands, Reply's says it could not be read.
        back();
        desktop.refuse.set('setup.status', 'The Reply CLI did not answer.');
        fireEvent.click(within(shown).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane)).getByText(/^Reply could not be read/));
        const again = await read(pane);
        expect(words(within(again).getByRole('region', { name: 'Read from Reply now' }))).toContain(
            'Reply could not be read: The Reply CLI did not answer.',
        );
        expect(within(again).queryByText('sales@example.test')).toBeNull();
        expect(words(within(again).getByRole('region', { name: 'On record now' }))).toContain(
            'Plugin that sends messages by default Reply (reply)',
        );
    });
});

describe('the accounts card in a browser tab', () => {
    it('draws the runtime’s part and the Accounts page’s sentence that connecting is done in Jason Desktop', async () => {
        const desktop = stubDesktop(everyState);
        const stand = standInBridge(desktop);
        const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
        setPlace('tab', tab);
        await loadMode(tab);
        desktop.keep({ ...setUp(turn()), mode: 'ask_first' });
        const fake = new FakeApi()
            .answer('plugin.list', 'plugin.list/card-accounts-reply')
            .answer('route.list', 'route.list/card-accounts-reply')
            .answer('ui.tokens', 'ui.tokens/two');
        nothingWaitsInChats(fake)
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/set')
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
        await session.bootstrap();
        try {
            render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
            const pane = await openChat('Sending');
            const shown = await read(pane);
            expect(
                within(shown).getByText(
                    'Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason Desktop; a browser cannot do them.',
                ),
            ).toBeTruthy();
            expect(within(shown).queryByRole('region', { name: 'Read from Reply now' })).toBeNull();
            expect(within(shown).queryByText('sales@example.test')).toBeNull();
            expect(words(within(shown).getByRole('region', { name: 'On record now' }))).toContain(
                'Plugin that sends messages by default Reply (reply)',
            );
        } finally {
            cleanup();
            setPlace('window');
        }
    });
});
