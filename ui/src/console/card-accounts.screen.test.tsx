import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import type { DesktopConversationRecord, DesktopKeptEntry, DesktopSetup } from '../platform/desktop';
import { loadMode, mode } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { journalLine, setupFound, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { callsBeyondStart, FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { clock } from '../time/format';
import { acts } from './card-acts';
import { cardReads } from './card-frame';
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
    if (!fake.offers('plugin.list')) fake.answer('plugin.list', 'plugin.list/card-accounts-reply');
    if (!fake.offers('route.list')) fake.answer('route.list', 'route.list/card-accounts-reply');
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui');
    watch(agentHeads(fake));
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
        expect(within(shown).queryByText('Reading...')).toBeNull();
        return shown;
    });
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
/** Reply's part of a card: the facts read from Reply now, under their own label. */
const fromReply = (shown: HTMLElement): HTMLElement => {
    const part = shown.querySelector<HTMLElement>('.card-facts.card-provider');
    expect(part).not.toBeNull();
    return part!;
};
/** The runtime's part of a card: its facts under "On record now". */
const onRecord = (shown: HTMLElement): HTMLElement => {
    const part = shown.querySelector<HTMLElement>('.card-facts:not(.card-provider)');
    expect(part).not.toBeNull();
    return part!;
};
const count = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name).length;
// The console's own read at its start, for the mode strip, is not the card's.
const asked = (fake: FakeApi, operation: string) => callsBeyondStart(fake, operation).length;
const rows = (shown: HTMLElement) =>
    [...shown.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map((cell) => words(cell)));

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    cardReads.value = {};
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
    mode.value = 'live';
});

/** Opens the Feed from the sidebar's menu and returns its pane. */
async function openFeed(): Promise<HTMLElement> {
    await waitFor(() => {
        const menu = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Menu' });
        fireEvent.click(within(menu).getByRole('button', { name: /^Feed/ }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

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
        const provider = fromReply(shown);
        expect(words(provider)).toContain('Signed in to Reply as owner@example.test');
        expect(words(provider)).toContain('The runtime’s Reply plugin uses this sign-in');
        expect(words(provider)).toContain('9 accounts');
        expect([...provider.querySelectorAll('th')].map((each) => words(each))).toEqual([
            'Account at Reply',
            'Channel',
            'Status at Reply',
        ]);
        expect(rows(provider)).toEqual([
            ['Sales inbox sales@example.test', 'Email', 'connected'],
            ['Old inbox old@example.test', 'Email', 'disconnected'],
            ['Shared inbox team@example.test', 'Email', 'unknown'],
            ['Owner Example owner-li', 'LinkedIn', 'enabled'],
            ['Second Seat second-li', 'LinkedIn', 'disabled'],
            ['Third Seat third-li', 'LinkedIn', 'cookieInvalid'],
            ['Fourth Seat fourth-li', 'LinkedIn', 'dailyLimitReached'],
            ['New inbox new@example.test', 'Email', 'warmingUp'],
            ['Quiet inbox quiet@example.test', 'Email', 'none given'],
        ]);
        // The account is its name over its address, the channel has a glyph, the status is a chip in Reply's own word, and
        // only the two words Reply uses for a usable account are green.
        const chips = [...provider.querySelectorAll('.acct-chip')];
        expect(chips.map((each) => [each.textContent, each.classList.contains('ready')])).toEqual([
            ['connected', true],
            ['disconnected', false],
            ['unknown', false],
            ['enabled', true],
            ['disabled', false],
            ['cookieInvalid', false],
            ['dailyLimitReached', false],
            ['warmingUp', false],
            ['none given', false],
        ]);
        expect(provider.querySelectorAll('tbody .acct-chan svg')).toHaveLength(9);
        expect(provider.querySelector('tbody tr .acct-name')?.textContent).toBe('Sales inbox');
        expect(provider.querySelector('tbody tr .acct-sub')?.textContent).toBe('sales@example.test');
        const record = onRecord(shown);
        expect(words(record)).toContain('Plugin that sends messages by default Reply (reply)');
        expect(words(record)).toContain('Its state: valid');
        // Nothing of the provider's stands under the runtime's facts, and nothing of the runtime's under Reply's.
        expect(words(record)).not.toContain('example.test');
        expect(words(provider)).not.toContain('Its state');
    });

    it('draws its captions at plain weight: its header and the label of each part', async () => {
        renderConsole([setUp(turn())]);
        const pane = await openChat('Sending');
        const shown = await read(pane);
        const captions = [...shown.querySelectorAll('.card-head, .card-rec')];
        expect(captions.map((each) => words(each))).toEqual(['Accounts', 'Read from Reply now', 'On record now']);
        for (const caption of captions) {
            expect(caption.closest('b, strong')).toBeNull();
            expect(caption.querySelector('b, strong')).toBeNull();
            expect(caption.classList.contains('card-who')).toBe(false);
        }
    });

    it('says when Reply is not signed in, and has no table of accounts', async () => {
        const out: DesktopSetup = {
            ...setupFound,
            reply: { ...setupFound.reply, signedIn: false, user: null, wired: false, accounts: [] },
        };
        renderConsole([setUp(turn())], stubDesktop(out));
        const pane = await openChat('Sending');
        const provider = fromReply(await read(pane));
        expect(words(provider)).toContain('Not signed in to Reply');
        expect(words(provider)).toContain('The runtime’s Reply plugin does not use a Reply sign-in yet');
        expect(words(provider)).toContain('No accounts');
        expect(provider.querySelector('table')).toBeNull();
    });

    it('says which channels Reply did not list, and why', async () => {
        const partly: DesktopSetup = {
            ...setupFound,
            reply: {
                ...setupFound.reply,
                absent: ['linkedin'],
                unread: ['email'],
                accounts: [
                    { id: 'a4', channel: 'linkedin', name: 'Owner Example', address: 'owner-li', status: 'enabled' },
                ],
            },
        };
        renderConsole([setUp(turn())], stubDesktop(partly));
        const pane = await openChat('Sending');
        const provider = fromReply(await read(pane));
        expect(
            within(provider).getByText('LinkedIn is not in this Reply account’s plan or role, so Reply lists none.'),
        ).toBeTruthy();
        expect(within(provider).getByText('Email accounts could not be read from Reply.')).toBeTruthy();
        // The count is of what Reply listed, and says which channel was not read.
        expect(words(provider)).toContain('1 account listed; Email not read');
    });

    it('is drawn in the Feed and reads once there', async () => {
        // The Feed's row is in the demo's sidebar.
        mode.value = 'demo';
        const { fake } = renderConsole([
            { id: 'conv_01JDACCOUNTS00000000000000F', campaignId: null, title: 'Feed', main: true, entries: turn() },
        ]);
        const feed = await openFeed();
        const shown = await read(feed);
        expect(within(shown).getByText('sales@example.test')).toBeTruthy();
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([1, 1]);
    });

    it('reads again on Refresh in one chat and leaves another chat’s card as it was read', async () => {
        const at = new Date('2026-10-16T09:05:00.000Z').getTime();
        const now = vi.spyOn(Date, 'now').mockReturnValue(at);
        const { fake } = renderConsole([
            setUp(turn()),
            { id: 'conv_01JDACCOUNTS00000000000000B', campaignId: null, title: 'Second', entries: turn() },
        ]);
        await read(await openChat('Sending'));
        now.mockReturnValue(at + 60_000);
        const second = await openChat('Second');
        expect(within(await read(second)).getByText(`As of ${clock(new Date(at + 60_000), zone)}`)).toBeTruthy();
        expect(asked(fake, 'route.list')).toBe(2);
        now.mockReturnValue(at + 5 * 60_000);
        const again = await openChat('Sending');
        fireEvent.click(within(card(again)).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(again)).getByText(`As of ${clock(new Date(at + 5 * 60_000), zone)}`));
        expect(asked(fake, 'route.list')).toBe(3);
        const back = await openChat('Second');
        expect(within(await read(back)).getByText(`As of ${clock(new Date(at + 60_000), zone)}`)).toBeTruthy();
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([3, 3]);
    });

    it('says when no route sends messages', async () => {
        const fake = new FakeApi().answer('route.list', 'route.list/empty');
        renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        expect(words(onRecord(await read(pane)))).toBe(
            'On record now No route sends messages: the runtime routes no plugin to send them. Open Accounts',
        );
    });

    it('says when the runtime does not list the plugin a route names, and does not say it sends', async () => {
        const fake = new FakeApi().answer('plugin.list', 'plugin.list/none');
        renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        const record = words(onRecord(await read(pane)));
        expect(record).toContain('reply');
        expect(record).toContain('The runtime does not list this plugin.');
        expect(record).not.toContain('sends messages');
    });

    it('does not read again when Plan only marks its words as the plan, or takes the mark off', async () => {
        const desktop = stubDesktop(everyState);
        const { fake } = renderConsole([{ ...setUp(turn()), mode: 'plan_only' }], desktop);
        const pane = await openChat('Sending');
        await read(pane);
        flush(() => desktop.emit(setupChat, { kind: 'done', error: null, plan: true }));
        flush(() => desktop.emit(setupChat, { kind: 'done', error: null }));
        await read(pane);
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([1, 1]);
    });

    it('names no speaker among its facts: the word Jason is nowhere on it', async () => {
        renderConsole([setUp(turn())]);
        const pane = await openChat('Sending');
        expect(card(pane).textContent).not.toMatch(/Jason/);
        expect((await read(pane)).textContent).not.toMatch(/Jason/);
    });

    it('takes a desktop read that throws as a read that failed, and reads again on Refresh', async () => {
        const desktop = stubDesktop(everyState);
        const { fake } = renderConsole([setUp(turn())], desktop);
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.status')).toBe(true));
        const status = desktop.setup.status;
        desktop.setup.status = () => {
            throw new Error('The desktop could not answer.');
        };
        const pane = await openChat('Sending');
        const shown = await read(pane);
        expect(words(fromReply(shown))).toContain('Reply could not be read: The desktop could not answer.');
        desktop.setup.status = status;
        fireEvent.click(within(shown).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane)).getByText('sales@example.test'));
        expect(asked(fake, 'route.list')).toBe(2);
    });

    it('says each runtime read that failed, not the first alone', async () => {
        const fake = new FakeApi();
        fake.unreachable('route.list');
        fake.unreachable('plugin.list');
        renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        const record = words(onRecord(await read(pane)));
        expect(record).toContain('The runtime’s routes could not be read: Failed to fetch');
        expect(record).toContain('The runtime’s plugins could not be read: Failed to fetch');
    });

    it('opened afresh, reads only its last card that reads; an earlier one stands as a stub until Show now', async () => {
        const { fake } = renderConsole([
            setUp([...turn(), { kind: 'user', text: 'And now?' }, asking(`Again.\n\n${fenced({ kind: 'accounts' })}`)]),
        ]);
        const pane = await openChat('Sending');
        await waitFor(() => expect(within(pane).getAllByRole('group', { name: 'Accounts' })).toHaveLength(2));
        await waitFor(() => {
            const [earlier, later] = within(pane).getAllByRole('group', { name: 'Accounts' });
            expect(within(later!).getByText(/^As of /)).toBeTruthy();
            expect(within(earlier!).getByRole('button', { name: 'Show now' })).toBeTruthy();
        });
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([1, 1]);
        const [earlier] = within(pane).getAllByRole('group', { name: 'Accounts' });
        fireEvent.click(within(earlier!).getByRole('button', { name: 'Show now' }));
        await waitFor(() =>
            expect(
                within(within(pane).getAllByRole('group', { name: 'Accounts' })[0]!).getByText(/^As of /),
            ).toBeTruthy(),
        );
        expect([asked(fake, 'route.list'), asked(fake, 'plugin.list')]).toEqual([2, 2]);
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

    it('says it is reading while a read is under way, and when it read once it has', async () => {
        const fake = new FakeApi();
        const release = fake.hold('route.list');
        renderConsole([setUp(turn())], stubDesktop(everyState), fake);
        const pane = await openChat('Sending');
        const shown = await waitFor(() => {
            const each = card(pane);
            expect(within(each).getByText('Reading...')).toBeTruthy();
            return each;
        });
        expect(within(shown).queryByText(/^As of /)).toBeNull();
        flush(() => release());
        expect(within(await read(pane)).getByText('sales@example.test')).toBeTruthy();
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
        expect(words(onRecord(shown))).toContain('The runtime’s routes could not be read: Failed to fetch');
        expect(within(shown).getByText('sales@example.test')).toBeTruthy();
        // The desktop's read refused: the runtime's part stands, Reply's says it could not be read.
        back();
        desktop.refuse.set('setup.status', 'The Reply CLI did not answer.');
        fireEvent.click(within(shown).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => within(card(pane)).getByText(/^Reply could not be read/));
        const again = await read(pane);
        expect(words(fromReply(again))).toContain('Reply could not be read: The Reply CLI did not answer.');
        expect(within(again).queryByText('sales@example.test')).toBeNull();
        expect(words(onRecord(again))).toContain('Plugin that sends messages by default Reply (reply)');
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
        watch(agentHeads(fake));
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
            expect(within(shown).queryByText('Read from Reply now')).toBeNull();
            expect(within(shown).queryByText('sales@example.test')).toBeNull();
            expect(words(onRecord(shown))).toContain('Plugin that sends messages by default Reply (reply)');
        } finally {
            cleanup();
            setPlace('window');
        }
    });
});
