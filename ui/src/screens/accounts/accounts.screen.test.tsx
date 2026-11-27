import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import { resetDialogs } from '../../console/connect';
import { attention } from '../../console/needs';
import * as consoleCampaigns from '../../console/campaigns';
import { chats, lines, setup, setupError } from '../../console/state';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { route } from '../../router';
import type { DesktopAccount, DesktopReplyDone, DesktopSetup } from '../../platform/desktop';
import {
    chatDesktop,
    replyAccount,
    setupFound,
    stubStream,
    withReplyAccounts,
    type StubDesktop,
} from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import { agentHeads } from '../../test/agent-head';
import { renderApp } from '../../test/render-app';
import * as classicCampaigns from '../campaigns/store';
import { bringForward } from '../../test/area';

// The Accounts page: the provider's accounts by the Reply card's own code inside the desktop, nothing of them in a
// browser. The notification channels are Settings', not this page's.

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

beforeEach(() => {
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
    resetDialogs();
});

afterEach(() => cleanup());

function inBrowser(): FakeApi {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty');
    renderApp(fake);
    route.value = { screen: 'accounts' };
    return fake;
}

describe('Accounts in a browser', () => {
    it('is in the sidebar, and says in one sentence that the provider acts are the desktops, with none offered', async () => {
        inBrowser();
        await waitFor(() => screen.getByRole('heading', { name: 'Accounts', level: 1 }));
        expect(
            within(screen.getByRole('navigation', { name: 'Screens' })).getByRole('link', { name: 'Accounts' }),
        ).toBeTruthy();
        const provider = screen.getByRole('heading', { name: 'Provider' }).closest<HTMLElement>('section')!;
        expect(words(provider)).toContain(
            'Connecting, reconnecting, pausing and removing a provider’s accounts are done in Jason Desktop; a browser cannot do them.',
        );
        expect(within(provider).queryAllByRole('button')).toEqual([]);
    });

    it('draws nothing of the notification channels: they are Settings’', async () => {
        inBrowser();
        await waitFor(() => screen.getByRole('heading', { name: 'Provider' }));
        expect(screen.queryByRole('heading', { name: /^Notifications/ })).toBeNull();
        expect(screen.queryByText(/notification/i)).toBeNull();
    });
});

function inDesktop(status: DesktopSetup = setupFound, given?: StubDesktop): StubDesktop {
    const fake = new FakeApi()
        .answer('system.info', 'system.info/with-ui')
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two');
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
    const desktop = given ?? chatDesktop(status);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);

async function openAccounts(drawn = '.card .acct'): Promise<HTMLElement> {
    await waitFor(() => side().getByRole('button', { name: /^Needs you/ }));
    bringForward('Accounts');
    return waitFor(() => {
        const page = screen.getByRole('heading', { name: 'Accounts', level: 1 }).closest<HTMLElement>('.main')!;
        expect(page.querySelector(drawn)).not.toBeNull();
        return page;
    });
}

/** The card of the one account a desktop of an earlier version draws, whose name it cannot give. */
const earlierCard = (where: HTMLElement) => within(where).getByRole('region', { name: 'Reply account' });

describe('Accounts inside a desktop of an earlier version, which sends one Reply state and no accounts', () => {
    it('draws that state as one account’s card with its senders, and not the browser’s sentence', async () => {
        inDesktop();
        const page = await openAccounts();
        const card = earlierCard(page);
        expect(words(card.querySelector('.acct')!)).toContain('Sales inbox');
        expect(words(page)).not.toContain('a browser cannot do them');
        expect(screen.queryByRole('heading', { name: /^Notifications/ })).toBeNull();
    });

    it('asks the desktop for a Pause by the sender’s id, naming no account, and the desktop answers it', async () => {
        const desktop = inDesktop({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [
                    {
                        id: 'li_1',
                        channel: 'linkedin',
                        name: 'Outreach profile',
                        address: 'p@example.test',
                        status: 'enabled',
                    },
                ],
            },
        });
        const page = await openAccounts();
        const card = () => earlierCard(page);
        const row = Array.from(card().querySelectorAll<HTMLElement>('.acct')).find((each) =>
            words(each).includes('Outreach profile'),
        )!;
        fireEvent.click(within(row).getByRole('button', { name: 'Pause' }));
        await waitFor(() => expect(words(within(card()).getByRole('dialog'))).toMatch(/Done\./));
        expect(
            desktop.calls
                .filter((call) => call.name.startsWith('setup.reply'))
                .map((call) => [call.name, ...call.args]),
        ).toEqual([['setup.replyPause', 'li_1']]);
    });

    it('keeps an act asked on Accounts off the Connect panel, and one asked there off Accounts', async () => {
        const desktop = inDesktop({
            ...setupFound,
            reply: {
                ...setupFound.reply,
                accounts: [
                    {
                        id: 'li_1',
                        channel: 'linkedin',
                        name: 'Outreach profile',
                        address: 'p@example.test',
                        status: 'enabled',
                    },
                ],
            },
        });
        const page = await openAccounts();
        const cardIn = (where: HTMLElement) =>
            where.querySelector<HTMLElement>('.reply-account') ??
            within(where).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        const row = (where: HTMLElement) =>
            Array.from(cardIn(where).querySelectorAll<HTMLElement>('.acct')).find((each) =>
                words(each).includes('Outreach profile'),
            )!;
        fireEvent.click(within(row(page)).getByRole('button', { name: 'Pause' }));
        await waitFor(() => expect(words(within(cardIn(page)).getByRole('dialog'))).toMatch(/Done\./));
        // Connect is under More.
        bringForward('Connect');
        const connect = await waitFor(() => {
            const panel = screen.getByRole('heading', { name: 'Connect', level: 1 }).closest<HTMLElement>('.painted')!;
            expect(panel.querySelector('.acct')).not.toBeNull();
            return panel;
        });
        expect(within(cardIn(connect)).queryByRole('dialog')).toBeNull();
        // The other way round: an act asked on Connect shows its dialog there and not on Accounts.
        fireEvent.click(within(row(connect)).getByRole('button', { name: 'Delete' }));
        await waitFor(() => expect(within(cardIn(connect)).queryByRole('dialog')).not.toBeNull());
        // Leaving Accounts for the panel put the chat's address after Accounts' own; Back draws Accounts again, without it.
        history.back();
        const again = await waitFor(() => {
            const shown = screen.getByRole('heading', { name: 'Accounts', level: 1 }).closest<HTMLElement>('.main')!;
            expect(shown.querySelector('.card .acct')).not.toBeNull();
            return shown;
        });
        expect(within(cardIn(again)).queryByRole('dialog')).toBeNull();
        expect(desktop.calls.filter((call) => call.name.startsWith('setup.reply')).length).toBeGreaterThan(0);
    });

    it('drops a choice the card held open when the page stops drawing it', async () => {
        inDesktop();
        const page = await openAccounts();
        const card = () => earlierCard(page);
        fireEvent.click(within(card()).getByRole('button', { name: 'Connect a mailbox or LinkedIn' }));
        await waitFor(() => within(card()).getByRole('button', { name: 'Gmail' }));
        bringForward('System');
        await waitFor(() => expect(screen.queryByRole('heading', { name: 'Accounts', level: 1 })).toBeNull());
        bringForward('Accounts');
        const again = await openAccounts();
        expect(within(again).queryByRole('button', { name: 'Gmail' })).toBeNull();
    });
});

describe('Reply accounts, one card per profile', () => {
    const mailbox = {
        id: 'm_1',
        channel: 'email',
        name: 'Sales inbox',
        address: 'sales@example.test',
        status: 'connected',
    };
    const linkedIn = { id: 'l_1', channel: 'linkedin', name: 'Made-up Bea', address: '', status: 'enabled' };
    const three = withReplyAccounts(setupFound, [
        replyAccount('default', {
            default: true,
            signedIn: true,
            user: 'ada@example.test',
            team: { id: '3', name: 'Made-up Three' },
            senders: [mailbox],
        }),
        replyAccount('outbound', {
            signedIn: true,
            user: 'bea@example.test',
            team: { id: '9', name: '' },
            notRead: ['teams'],
            senders: [linkedIn],
        }),
        replyAccount('routed-only', { held: false }),
    ]);
    const cardOf = (page: HTMLElement, label: string) => within(page).getByRole('region', { name: label });
    const cards = (page: HTMLElement) =>
        Array.from(page.querySelectorAll<HTMLElement>('.reply-account')).map((card) =>
            words(card.querySelector('.acct-head')!),
        );
    const section = (page: HTMLElement) => within(page).getByRole('region', { name: 'Reply' });
    const refusal = (refused: string) => ({ refused, code: null, offer: null, reread: false });

    it('draws one Reply section, then one card per account in the desktop’s order, each named as the Reply CLI names its profile and nothing beside it, the default marked, its sign-in with the team it is bound to, and no Make default once a default is set', async () => {
        inDesktop(three);
        const page = await openAccounts('.reply-account');
        expect(words(section(page).querySelector('.reply-name')!)).toBe('Reply Recommended');
        expect(cards(page)).toEqual(['Reply default Default', 'Reply outbound', 'Reply routed-only']);
        const ada = cardOf(page, 'Reply account default');
        expect(words(ada.querySelector('.reply-state')!)).toBe('Signed in as ada@example.test in Made-up Three');
        expect(words(ada.querySelector('.acct')!)).toContain('Sales inbox');
        expect(within(ada).queryByRole('button', { name: 'Make default' })).toBeNull();
        expect(words(ada)).not.toContain('could not be read');
        // A team Reply gives no name, or whose name could not be read, said as that, never by its id; teams that could not
        // be read said as that, the account's mailboxes and LinkedIn accounts standing.
        const bea = cardOf(page, 'Reply account outbound');
        expect(words(bea.querySelector('.reply-state')!)).toBe(
            'Signed in as bea@example.test in a team whose name could not be read',
        );
        expect(words(bea.querySelector('.acct')!)).toContain('Made-up Bea');
        expect(Array.from(bea.querySelectorAll('.acct-none')).map((line) => words(line))).toEqual([
            'The teams of this sign-in could not be read. Check again.',
        ]);
        // A profile only a route names, which the Reply CLI does not hold: not signed in, with the sign-in that makes it.
        const routed = cardOf(page, 'Reply account routed-only');
        expect(words(routed.querySelector('.reply-state')!)).toBe('Not signed in');
        expect(within(routed).getByRole('button', { name: 'Sign in with Reply' }).classList.contains('p')).toBe(true);
        expect(within(routed).getByRole('button', { name: 'Use key' })).toBeTruthy();
        expect(within(routed).queryByRole('button', { name: 'Connect a mailbox or LinkedIn' })).toBeNull();
        // No card says who uses it.
        expect(words(page)).not.toMatch(/uses this sign-in|does not use it yet|whether jason uses/i);
        cleanup();
        // Every other global route to Reply is a mark of the card of the account it names, in the words the page gives
        // the desktop's closed data: a channel's sends by the channel, any other work by its operation's name.
        const marked = withReplyAccounts(setupFound, [
            {
                ...three.accounts!.accounts[0]!,
                defaultFor: [{ kind: 'channel', operation: 'message.send', channel: 'linkedin' }],
            },
            {
                ...three.accounts!.accounts[1]!,
                defaultFor: [
                    { kind: 'operation', operation: 'contact.find' },
                    { kind: 'channel', operation: 'message.send', channel: 'email' },
                    { kind: 'channel', operation: 'conversation.reply', channel: 'linkedin' },
                ],
            },
            three.accounts!.accounts[2]!,
        ]);
        inDesktop(marked);
        expect(cards(await openAccounts('.reply-account'))).toEqual([
            'Reply default Default Default for LinkedIn',
            'Reply outbound Default for contact.find Default for email Default for conversation.reply on LinkedIn',
            'Reply routed-only',
        ]);
    });

    it('asks every act on a card as that card’s account, and a sign-in or a key for that account alone', async () => {
        const disconnected = {
            id: 'm_2',
            channel: 'email',
            name: 'Made-up Ops',
            address: 'ops@example.test',
            status: 'disconnected',
        };
        const disabled = { id: 'l_2', channel: 'linkedin', name: 'Made-up Cy', address: '', status: 'disabled' };
        const desktop = inDesktop(
            withReplyAccounts(setupFound, [
                replyAccount('default', {
                    default: true,
                    signedIn: true,
                    user: 'ada@example.test',
                    senders: [mailbox],
                }),
                replyAccount('outbound', {
                    signedIn: true,
                    user: 'bea@example.test',
                    senders: [disconnected, linkedIn, disabled],
                }),
                replyAccount('routed-only', { held: false }),
            ]),
        );
        const page = await openAccounts('.reply-account');
        const bea = () => cardOf(page, 'Reply account outbound');
        const outbound = { type: 'reply', name: 'outbound' };
        const rowOf = (name: string) =>
            Array.from(bea().querySelectorAll<HTMLElement>('.acct')).find(
                (each) => words(each.querySelector('b')!) === name,
            )!;
        const answered = async (text: RegExp) =>
            waitFor(() => expect(words(within(bea()).getByRole('dialog'))).toMatch(text));
        fireEvent.click(within(rowOf('Made-up Bea')).getByRole('button', { name: 'Pause' }));
        await answered(/Done\./);
        expect(within(cardOf(page, 'Reply account default')).queryByRole('dialog')).toBeNull();
        fireEvent.click(within(rowOf('Made-up Cy')).getByRole('button', { name: 'Resume' }));
        await answered(/Done\./);
        fireEvent.click(within(rowOf('Made-up Ops')).getByRole('button', { name: 'Reconnect' }));
        await answered(/The link opened in your browser\./);
        fireEvent.click(within(rowOf('Made-up Ops')).getByRole('button', { name: 'Limits' }));
        const field = await waitFor(() => within(bea()).getByRole('textbox', { name: 'Daily limit' }));
        fireEvent.input(field, { target: { value: '40' } });
        fireEvent.click(within(bea()).getByRole('button', { name: 'Set' }));
        await answered(/Done\./);
        fireEvent.click(within(rowOf('Made-up Ops')).getByRole('button', { name: 'Delete' }));
        await answered(/Done\./);
        fireEvent.click(within(bea()).getByRole('button', { name: 'Connect a mailbox or LinkedIn' }));
        fireEvent.click(await waitFor(() => within(bea()).getByRole('button', { name: 'Gmail' })));
        await answered(/The link opened in your browser\./);
        fireEvent.click(within(bea()).getByRole('button', { name: 'Connect a mailbox or LinkedIn' }));
        fireEvent.click(await waitFor(() => within(bea()).getByRole('button', { name: 'LinkedIn' })));
        fireEvent.input(
            await waitFor(() => within(bea()).getByRole('textbox', { name: 'Name for the LinkedIn account' })),
            {
                target: { value: 'Made-up Dee' },
            },
        );
        fireEvent.click(within(bea()).getByRole('button', { name: 'Make the link' }));
        await answered(/The link opened in your browser\./);
        // A signed-in card offers no sign-in, by the browser or by a key: another login is another Reply account.
        expect(within(bea()).queryByRole('button', { name: 'Sign in with Reply' })).toBeNull();
        expect(within(bea()).queryByPlaceholderText('Or paste a Reply API key')).toBeNull();
        expect(within(bea()).queryByRole('button', { name: 'Use key' })).toBeNull();
        fireEvent.click(
            within(cardOf(page, 'Reply account routed-only')).getByRole('button', { name: 'Sign in with Reply' }),
        );
        await waitFor(() => expect(desktop.calls.some((call) => call.name === 'setup.connectReply')).toBe(true));
        fireEvent.input(
            within(cardOf(page, 'Reply account routed-only')).getByPlaceholderText('Or paste a Reply API key'),
            {
                target: { value: 'made-up-key' },
            },
        );
        fireEvent.click(within(cardOf(page, 'Reply account routed-only')).getByRole('button', { name: 'Use key' }));
        await waitFor(() => expect(desktop.calls.some((call) => call.name === 'setup.connectReplyWithKey')).toBe(true));
        expect(
            desktop.calls
                .filter((call) => /^setup\.(reply|connectReply)/.test(call.name))
                .map((call) => [call.name, ...call.args]),
        ).toEqual([
            ['setup.replyPause', 'l_1', outbound],
            ['setup.replyResume', 'l_2', outbound],
            ['setup.replyReconnect', 'email', 'm_2', undefined, outbound],
            ['setup.replyLimits', 'm_2', undefined, outbound],
            ['setup.replyLimits', 'm_2', 40, outbound],
            ['setup.replyDelete', 'email', 'm_2', undefined, outbound],
            ['setup.replyConnect', 'email', { provider: 'gmail' }, outbound],
            ['setup.replyConnect', 'linkedin', { name: 'Made-up Dee' }, outbound],
            ['setup.connectReply', { type: 'reply', name: 'routed-only' }],
            ['setup.connectReplyWithKey', 'made-up-key', { type: 'reply', name: 'routed-only' }],
        ]);
    });

    it('offers Make default on every card while no account is the default, asks the desktop by that account, says on its card what the desktop answered, and waits on the default being made', async () => {
        const none = withReplyAccounts(
            setupFound,
            three.accounts!.accounts.map((each) => ({ ...each, default: false })),
            { noDefault: true },
        );
        const desktop = inDesktop(none);
        desktop.actAnswers.set('setup.makeDefault', [
            refusal('Jason is not running, so nothing was changed. Start it, then try again.'),
            { done: true },
        ]);
        const page = await openAccounts('.reply-account');
        expect(cards(page)).toEqual([
            'Reply default Make default',
            'Reply outbound Make default',
            'Reply routed-only Make default',
        ]);
        const routed = () => cardOf(page, 'Reply account routed-only');
        fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
        await waitFor(() => expect(words(within(routed()).getByRole('alert'))).toMatch(/^Jason is not running/));
        expect(within(cardOf(page, 'Reply account default')).queryByRole('dialog')).toBeNull();
        const reads = desktop.calls.filter((call) => call.name === 'setup.status').length;
        fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
        await waitFor(() => expect(words(within(routed()).getByRole('dialog'))).toMatch(/Done\./));
        await waitFor(() =>
            expect(desktop.calls.filter((call) => call.name === 'setup.status').length).toBeGreaterThan(reads),
        );
        expect(desktop.calls.filter((call) => call.name === 'setup.makeDefault').map((call) => call.args)).toEqual([
            [{ type: 'reply', name: 'routed-only' }],
            [{ type: 'reply', name: 'routed-only' }],
        ]);
        // While the desktop has not answered, the card says the default is being made, never that Reply is asked, which
        // Make default never asks.
        let held: (answer: DesktopReplyDone) => void = () => undefined;
        desktop.setup.makeDefault = () => new Promise((resolve) => (held = resolve));
        fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
        await waitFor(() => expect(words(within(routed()).getByRole('dialog'))).toContain('Making it the default…'));
        expect(words(routed())).not.toContain('Asking Reply');
        // Meanwhile Make default is off, on that card and on the others, as every act is while one is asked.
        for (const name of ['routed-only', 'default'])
            expect(
                (
                    within(cardOf(page, `Reply account ${name}`)).getByRole('button', {
                        name: 'Make default',
                    }) as HTMLButtonElement
                ).disabled,
                name,
            ).toBe(true);
        // Done is said once the accounts are read again: until then the act is still asked and Make default stays off, so
        // no card offers it under Done.
        let reading: (state: DesktopSetup) => void = () => undefined;
        desktop.setup.status = () => new Promise((resolve) => (reading = resolve));
        held({ done: true });
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(words(within(routed()).getByRole('dialog'))).not.toMatch(/Done\./);
        expect((within(routed()).getByRole('button', { name: 'Make default' }) as HTMLButtonElement).disabled).toBe(
            true,
        );
        reading(
            withReplyAccounts(
                setupFound,
                three.accounts!.accounts.map((each) => ({ ...each, default: each.name === 'routed-only' })),
            ),
        );
        await waitFor(() => expect(words(within(routed()).getByRole('dialog'))).toMatch(/Done\./));
        expect(cards(page).filter((card) => card.includes('Make default'))).toEqual([]);
    });

    it('says what of the accounts could not be read in the section, never as a state: no account where the profiles were not, no mark and no Make default where the routes were not; and that no account is the default yet', async () => {
        inDesktop(withReplyAccounts(setupFound, [], { notRead: ['profiles'] }));
        let page = await openAccounts('.reply-section');
        expect(words(section(page))).toContain('The Reply accounts could not be read.');
        expect(within(section(page)).getByRole('button', { name: 'Check again' })).toBeTruthy();
        expect(page.querySelector('.reply-account')).toBeNull();
        cleanup();

        inDesktop(
            withReplyAccounts(
                setupFound,
                [replyAccount('default', { signedIn: true, user: 'ada@example.test' }), replyAccount('outbound')],
                { notRead: ['routes'] },
            ),
        );
        page = await openAccounts('.reply-account');
        expect(words(section(page))).toContain('Which Reply account is the default could not be read.');
        expect(words(section(page))).not.toContain('No Reply account is the default yet');
        expect(cards(page)).toEqual(['Reply default', 'Reply outbound']);
        expect(within(page).queryByRole('button', { name: 'Make default' })).toBeNull();
        cleanup();

        inDesktop(
            withReplyAccounts(setupFound, [replyAccount('default', { signedIn: true, user: 'ada@example.test' })], {
                noDefault: true,
            }),
        );
        page = await openAccounts('.reply-account');
        expect(words(section(page))).toContain(
            'No Reply account is the default yet. Campaigns have nothing to send through until you make one the default.',
        );
        expect(cards(page)).toEqual(['Reply default Make default']);
        cleanup();

        // A default of another plugin: no Reply account is marked, none offers Make default, and nothing is missing.
        inDesktop(
            withReplyAccounts(setupFound, [replyAccount('default', { signedIn: true, user: 'ada@example.test' })]),
        );
        page = await openAccounts('.reply-account');
        expect(words(section(page))).not.toMatch(/could not be read|no account is the default/i);
        expect(cards(page)).toEqual(['Reply default']);
    });

    it('with no usable Reply CLI draws the section alone, saying what it says of the CLI', async () => {
        const missing = withReplyAccounts(
            { ...setupFound, reply: { ...setupFound.reply, cli: { found: false, version: null, install: 'reply' } } },
            [],
        );
        inDesktop(missing);
        const page = await openAccounts('.reply-section');
        expect(words(section(page))).toContain('The Reply CLI was not found.');
        expect(within(section(page)).getByRole('button', { name: 'Install' })).toBeTruthy();
        expect(page.querySelector('.reply-account')).toBeNull();
    });

    it('says a sign-in’s or a key’s refusal on the card of the account it was asked for, in the desktop’s words without the window’s wrapper, and keeps it until a read shows that account’s sign-in changed, or the next act', async () => {
        // `outbound` not signed in, so its card offers the key.
        const [ada, signedIn, routedOnly] = three.accounts!.accounts as [
            DesktopAccount,
            DesktopAccount,
            DesktopAccount,
        ];
        const bea: DesktopAccount = { ...signedIn, signedIn: false, user: null, team: null, notRead: [], senders: [] };
        const start = withReplyAccounts(setupFound, [ada, bea, routedOnly]);
        const desktop = inDesktop(start);
        const wrapped = (member: string, words: string) =>
            `Error invoking remote method 'jason:setup.${member}': Error: ${words}`;
        const keyRefused = 'Reply did not accept that key. Authentication credentials are missing or invalid.';
        const notPlaced =
            'Signed in, but Jason’s Reply plugin could not be placed or loaded. The update feed at http://127.0.0.1:9/manifest.json could not be reached.';
        desktop.refuse.set('setup.connectReplyWithKey', wrapped('connectReplyWithKey', keyRefused));
        desktop.refuse.set('setup.connectReply', wrapped('connectReply', notPlaced));
        const page = await openAccounts('.reply-account');
        const card = (name: string) => cardOf(page, `Reply account ${name}`);
        const useKey = (name: string) => {
            fireEvent.input(within(card(name)).getByPlaceholderText('Or paste a Reply API key'), {
                target: { value: 'made-up-key' },
            });
            fireEvent.click(within(card(name)).getByRole('button', { name: 'Use key' }));
        };
        const checkAgain = () =>
            fireEvent.click(within(card('default')).getAllByRole('button', { name: 'Check again' })[0]!);
        const refused = (name: string) => {
            const line = within(card(name)).queryByRole('alert');
            return line === null ? null : words(line);
        };
        let landEarlier: (state: DesktopSetup) => void = () => undefined;
        let earlierState = start;
        // Each step, then the refusal each card says; the section says none.
        const steps: [string, () => Promise<void>, Record<'default' | 'outbound' | 'routed-only', string | null>][] = [
            [
                'a key refused on one card',
                async () => useKey('outbound'),
                { default: null, outbound: keyRefused, 'routed-only': null },
            ],
            [
                'a read that shows another account’s sign-in changed',
                async () => {
                    desktop.status = withReplyAccounts(setupFound, [
                        { ...ada, user: 'cy@example.test' },
                        bea,
                        routedOnly,
                    ]);
                    checkAgain();
                    await waitFor(() => expect(words(card('default'))).toContain('cy@example.test'));
                },
                { default: null, outbound: keyRefused, 'routed-only': null },
            ],
            [
                'a sign-in on another card, which signs it in, then cannot place the plugin',
                async () => {
                    const status = desktop.setup.status;
                    desktop.setup.status = () => new Promise((resolve) => (landEarlier = resolve));
                    checkAgain();
                    desktop.setup.status = status;
                    earlierState = desktop.status;
                    desktop.status = withReplyAccounts(setupFound, [
                        ada,
                        bea,
                        { ...routedOnly, signedIn: true, user: 'bea@example.test' },
                    ]);
                    fireEvent.click(within(card('routed-only')).getByRole('button', { name: 'Sign in with Reply' }));
                },
                { default: null, outbound: null, 'routed-only': notPlaced },
            ],
            [
                'the read after it, which shows that account signed in: the sign-in’s own change',
                async () => {
                    await waitFor(() => expect(words(card('routed-only'))).toContain('Signed in as bea@example.test'));
                },
                { default: null, outbound: null, 'routed-only': notPlaced },
            ],
            [
                'a read begun before the sign-in that lands after it, which the page drops: the card stays as the later read drew it',
                async () => {
                    const reads = desktop.calls.filter((call) => call.name === 'setup.status').length;
                    landEarlier(earlierState);
                    await new Promise((resolve) => setTimeout(resolve, 50));
                    expect(desktop.calls.filter((call) => call.name === 'setup.status').length).toBe(reads);
                    expect(words(card('routed-only'))).toContain('Signed in as bea@example.test');
                },
                { default: null, outbound: null, 'routed-only': notPlaced },
            ],
            [
                'a read that shows that account’s sign-in changed: signed out in a terminal',
                async () => {
                    desktop.status = start;
                    checkAgain();
                },
                { default: null, outbound: null, 'routed-only': null },
            ],
            [
                'a key refused again, then the next act of any kind: an Unblock on another card',
                async () => {
                    useKey('outbound');
                    await waitFor(() => expect(refused('outbound')).toBe(keyRefused));
                    fireEvent.click(within(card('default')).getByRole('button', { name: 'Unblock' }));
                    await waitFor(() =>
                        expect(desktop.calls.some((call) => call.name === 'setup.replyUnblock')).toBe(true),
                    );
                },
                { default: null, outbound: null, 'routed-only': null },
            ],
        ];
        for (const [step, act, expected] of steps) {
            await act();
            await waitFor(() =>
                expect(
                    {
                        default: refused('default'),
                        outbound: refused('outbound'),
                        'routed-only': refused('routed-only'),
                    },
                    step,
                ).toEqual(expected),
            );
            expect(within(section(page)).queryByRole('alert'), step).toBeNull();
        }
        expect(words(document.body)).not.toContain('Error invoking remote method');
    });

    it('says an act with no card of its own, and a read that failed, in the section without the window’s wrapper; a good read takes the read’s line away', async () => {
        const missing = withReplyAccounts(
            { ...setupFound, reply: { ...setupFound.reply, cli: { found: false, version: null, install: 'reply' } } },
            [],
        );
        const desktop = inDesktop(missing);
        desktop.refuse.set(
            'setup.installProgram',
            "Error invoking remote method 'jason:setup.installProgram': Error: npm ended with code 1.",
        );
        let page = await openAccounts('.reply-section');
        fireEvent.click(within(section(page)).getByRole('button', { name: 'Install' }));
        await waitFor(() => expect(words(within(section(page)).getByRole('alert'))).toBe('npm ended with code 1.'));
        cleanup();

        const reading = inDesktop(three);
        page = await openAccounts('.reply-account');
        reading.refuse.set(
            'setup.status',
            "Error invoking remote method 'jason:setup.status': Error: This window may not ask that.",
        );
        fireEvent.click(
            within(cardOf(page, 'Reply account default')).getAllByRole('button', { name: 'Check again' })[0]!,
        );
        await waitFor(() =>
            expect(words(within(section(page)).getByRole('alert'))).toBe('This window may not ask that.'),
        );
        reading.refuse.delete('setup.status');
        fireEvent.click(
            within(cardOf(page, 'Reply account default')).getAllByRole('button', { name: 'Check again' })[0]!,
        );
        await waitFor(() => expect(within(section(page)).queryByRole('alert')).toBeNull());
        expect(words(page)).not.toContain('Error invoking remote method');
    });

    it('offers Add Reply account in the Reply section wherever the accounts were read, asks the desktop under the name typed, an empty one too, and says its refusal under the field', async () => {
        const desktop = inDesktop(three);
        const taken = 'The Reply CLI holds a Reply account of that name already; choose another name.';
        const badName = 'Name the Reply account in one line of up to 64 characters that does not begin with a hyphen.';
        desktop.actAnswers.set('setup.addAccount', [
            refusal(badName),
            { ...refusal(taken), reread: true },
            { done: true },
        ]);
        const page = await openAccounts('.reply-account');
        const adding = () => section(page).querySelector<HTMLElement>('.reply-add')!;
        const field = () => within(adding()).getByRole('textbox', { name: 'Name for a new Reply account' });
        const reads = () => desktop.calls.filter((call) => call.name === 'setup.status').length;
        // An empty field is asked too, and refused as every name the desktop refuses is, under the field.
        fireEvent.click(within(adding()).getByRole('button', { name: 'Add Reply account' }));
        await waitFor(() => expect(words(within(adding()).getByRole('alert'))).toBe(badName));
        fireEvent.input(field(), { target: { value: '  outbound ' } });
        fireEvent.click(within(adding()).getByRole('button', { name: 'Add Reply account' }));
        await waitFor(() => expect(words(within(adding()).getByRole('alert'))).toBe(taken));
        expect(within(page).getAllByRole('alert')).toHaveLength(1);
        const before = reads();
        fireEvent.input(field(), { target: { value: 'Made-up Ops' } });
        fireEvent.click(within(adding()).getByRole('button', { name: 'Add Reply account' }));
        await waitFor(() => expect(within(adding()).queryByRole('alert')).toBeNull());
        await waitFor(() => expect(reads()).toBeGreaterThan(before));
        expect((field() as HTMLInputElement).value).toBe('');
        // The name typed is asked for trimmed.
        expect(desktop.calls.filter((call) => call.name === 'setup.addAccount').map((call) => call.args)).toEqual([
            [''],
            ['outbound'],
            ['Made-up Ops'],
        ]);
        cleanup();
        // Not where the accounts were not read, nor below the Reply CLI the acts need, nor from a desktop without it.
        inDesktop(withReplyAccounts(setupFound, [], { notRead: ['profiles'] }));
        expect((await openAccounts('.reply-section')).querySelector('.reply-add')).toBeNull();
        cleanup();
        const older = chatDesktop(three);
        delete (older.setup as Partial<typeof older.setup>).addAccount;
        inDesktop(three, older);
        expect((await openAccounts('.reply-account')).querySelector('.reply-add')).toBeNull();
    });

    it('offers Sign out on a signed-in card and Remove on a card whose profile the Reply CLI holds, never default’s, asks the desktop as that card’s account, and says on its card how it ended', async () => {
        const four = withReplyAccounts(setupFound, [
            ...three.accounts!.accounts,
            replyAccount('spare'),
            // The Reply CLI's current profile, which a route naming no profile follows: Remove would move that route.
            replyAccount('quiet', { signedIn: true, user: 'cy@example.test', followed: true }),
        ]);
        const desktop = inDesktop(four);
        const notRemoved = 'The Reply CLI did not remove the profile. Could not write the configuration.';
        desktop.actAnswers.set('setup.removeAccount', [{ ...refusal(notRemoved), reread: true }, { done: true }]);
        const page = await openAccounts('.reply-account');
        const card = (name: string) => cardOf(page, `Reply account ${name}`);
        const topActs = (name: string) => Array.from(card(name).querySelectorAll('.reply-top button'), words);
        expect([
            topActs('default'),
            topActs('outbound'),
            topActs('routed-only'),
            topActs('spare'),
            topActs('quiet'),
        ]).toEqual([
            ['Sign out'],
            ['Sign out', 'Remove'],
            ['Sign in with Reply'],
            ['Sign in with Reply', 'Remove'],
            ['Sign out'],
        ]);
        const outbound = { type: 'reply', name: 'outbound' };
        fireEvent.click(within(card('outbound')).getByRole('button', { name: 'Sign out' }));
        await waitFor(() => expect(words(within(card('outbound')).getByRole('dialog'))).toMatch(/Done\./));
        fireEvent.click(within(card('outbound')).getByRole('button', { name: 'Remove' }));
        await waitFor(() => expect(words(within(card('outbound')).getByRole('alert'))).toBe(notRemoved));
        fireEvent.click(within(card('outbound')).getByRole('button', { name: 'Remove' }));
        await waitFor(() => expect(words(within(card('outbound')).getByRole('dialog'))).toMatch(/Done\./));
        expect(within(card('default')).queryByRole('dialog')).toBeNull();
        // While the card's act is asked, its Sign out and Remove are off, as its other acts are.
        let held: (answer: DesktopReplyDone) => void = () => undefined;
        desktop.setup.signOut = () => new Promise((resolve) => (held = resolve));
        fireEvent.click(within(card('outbound')).getByRole('button', { name: 'Sign out' }));
        await waitFor(() => expect(words(within(card('outbound')).getByRole('dialog'))).toContain('Signing it out…'));
        const off = (name: string) =>
            (within(card('outbound')).getByRole('button', { name }) as HTMLButtonElement).disabled;
        expect([off('Sign out'), off('Remove')]).toEqual([true, true]);
        held({ done: true });
        await waitFor(() => expect([off('Sign out'), off('Remove')]).toEqual([false, false]));
        expect(
            desktop.calls
                .filter((call) => call.name === 'setup.signOut' || call.name === 'setup.removeAccount')
                .map((call) => [call.name, ...call.args]),
        ).toEqual([
            ['setup.signOut', outbound],
            ['setup.removeAccount', outbound],
            ['setup.removeAccount', outbound],
        ]);
        cleanup();
        // Where the routes could not be read, the desktop takes the Reply CLI's current profile as followed: its card
        // offers no Remove, and every other card whose profile the Reply CLI holds still does.
        inDesktop(
            withReplyAccounts(
                setupFound,
                [
                    replyAccount('default', { signedIn: true, user: 'ada@example.test' }),
                    replyAccount('outbound', { signedIn: true, user: 'bea@example.test', followed: true }),
                    replyAccount('spare'),
                ],
                { notRead: ['routes'] },
            ),
        );
        const unread = await openAccounts('.reply-account');
        expect(
            ['default', 'outbound', 'spare'].map((name) =>
                Array.from(cardOf(unread, `Reply account ${name}`).querySelectorAll('.reply-top button'), words),
            ),
        ).toEqual([['Sign out'], ['Sign out'], ['Sign in with Reply', 'Remove']]);
        cleanup();
        // A desktop of an earlier version, without them, is asked for neither.
        const older = chatDesktop(four);
        delete (older.setup as Partial<typeof older.setup>).signOut;
        delete (older.setup as Partial<typeof older.setup>).removeAccount;
        inDesktop(four, older);
        const drawn = await openAccounts('.reply-account');
        expect(Array.from(drawn.querySelectorAll('.reply-top button'), words)).toEqual([
            'Sign in with Reply',
            'Sign in with Reply',
        ]);
    });

    it('says who a card not signed in was last signed in as, and where the last read found another login, a team the sign-in is no longer in, or another of its teams, what the account was and is now, with Keep it and Sign out; a team only by its name', async () => {
        const ada3 = { user: 'ada@example.test', team: { id: '3', name: 'Made-up Three' } };
        const desktop = inDesktop(
            withReplyAccounts(setupFound, [
                replyAccount('default', {
                    default: true,
                    signedIn: true,
                    user: 'cy@example.test',
                    team: { id: '4', name: '' },
                    lastSignIn: ada3,
                    change: 'team',
                    senders: [mailbox],
                }),
                replyAccount('outbound', { lastSignIn: ada3 }),
                // Signed in as the note says: no change to say.
                replyAccount('quiet', {
                    signedIn: true,
                    user: 'bea@example.test',
                    team: { id: '9', name: '' },
                    lastSignIn: { user: 'bea@example.test', team: { id: '9', name: '' } },
                }),
                replyAccount('spare'),
                // Another login: no team and nothing listed until Keep it.
                replyAccount('moved', { signedIn: true, user: 'dee@example.test', lastSignIn: ada3, change: 'login' }),
                // A sign-in that can no longer work in the noted team, whose login Reply does not say.
                replyAccount('left', { signedIn: true, lastSignIn: ada3, change: 'unknown' }),
                // Bound, and its login could not be read.
                replyAccount('unnamed', {
                    signedIn: true,
                    team: { id: '6', name: 'Made-up Six' },
                    notRead: ['login'],
                }),
            ]),
        );
        const page = await openAccounts('.reply-account');
        const card = (name: string) => cardOf(page, `Reply account ${name}`);
        const line = (name: string) => words(card(name).querySelector('.reply-state')!);
        expect([line('moved'), line('left'), line('unnamed')]).toEqual([
            'Signed in as dee@example.test',
            'Signed in',
            'Signed in, in Made-up Six',
        ]);
        expect(words(card('unnamed'))).toContain('The login of this sign-in could not be read. Check again.');
        for (const [name, said] of [
            [
                'moved',
                'This Reply account was ada@example.test in Made-up Three, and is now dee@example.test: campaigns sending through it now send as the new one. Keep it Sign out',
            ],
            [
                'left',
                'This Reply account was in Made-up Three, and this sign-in can no longer work in it. Keep it Sign out',
            ],
        ] as const) {
            expect(words(card(name).querySelector('.acct-changed')!), name).toBe(said);
            expect(card(name).querySelector('.acct'), name).toBeNull();
        }
        expect(words(card('outbound').querySelector('.reply-state')!)).toBe('Not signed in');
        expect(words(card('outbound').querySelector('.reply-state + .acct-last')!)).toBe(
            'Last signed in as ada@example.test in Made-up Three.',
        );
        // Nothing noted, or signed in as noted: no line and no notice.
        for (const name of ['spare', 'quiet'])
            expect([card(name).querySelector('.acct-last'), card(name).querySelector('.acct-changed')]).toEqual([
                null,
                null,
            ]);
        // The notice stands while the desktop says the change, and the lists are the account's as it now is.
        const notice = () => card('default').querySelector<HTMLElement>('.acct-changed')!;
        expect(words(notice())).toBe(
            'This Reply account was ada@example.test in Made-up Three, and is now cy@example.test in a team whose name could not be read: campaigns sending through it now send as the new one. Keep it Sign out',
        );
        // A team is named, never numbered.
        expect(words(page)).not.toMatch(/team \d/);
        expect(words(card('default').querySelector('.acct')!)).toContain('Sales inbox');
        fireEvent.click(within(notice()).getByRole('button', { name: 'Keep it' }));
        await waitFor(() => expect(desktop.calls.some((call) => call.name === 'setup.keepChange')).toBe(true));
        fireEvent.click(within(notice()).getByRole('button', { name: 'Sign out' }));
        await waitFor(() => expect(desktop.calls.some((call) => call.name === 'setup.signOut')).toBe(true));
        expect(
            desktop.calls
                .filter((call) => call.name === 'setup.keepChange' || call.name === 'setup.signOut')
                .map((call) => [call.name, ...call.args]),
        ).toEqual([
            ['setup.keepChange', { type: 'reply', name: 'default' }],
            ['setup.signOut', { type: 'reply', name: 'default' }],
        ]);
    });

    it('asks a desktop that lacks Make default for none: no card offers it, even with no default set', async () => {
        const none = withReplyAccounts(
            setupFound,
            three.accounts!.accounts.map((each) => ({ ...each, default: false })),
            { noDefault: true },
        );
        const desktop = chatDesktop(none);
        delete (desktop.setup as Partial<typeof desktop.setup>).makeDefault;
        inDesktop(none, desktop);
        const page = await openAccounts('.reply-account');
        expect(cards(page)).toEqual(['Reply default', 'Reply outbound', 'Reply routed-only']);
    });

    it('offers the teams of a sign-in in more than one in place of its mailboxes and LinkedIn accounts, from the read right after a sign-in on the card, and pins the one picked', async () => {
        const teams = [
            { id: '5', name: 'Made-up Five' },
            { id: '9', name: 'Made-up Nine' },
        ];
        const ada = replyAccount('default', {
            default: true,
            signedIn: true,
            user: 'ada@example.test',
            senders: [mailbox],
        });
        const signedIn = { signedIn: true, user: 'bea@example.test' };
        const teamless = withReplyAccounts(setupFound, [
            ada,
            replyAccount('outbound', { ...signedIn, unread: ['email', 'linkedin'], teams }),
        ]);
        const desktop = inDesktop(withReplyAccounts(setupFound, [ada, replyAccount('outbound')]));
        const page = await openAccounts('.reply-account');
        const bea = () => cardOf(page, 'Reply account outbound');
        const why = 'This sign-in belongs to more than one Reply team. Choose the one this profile works in.';
        const teamRows = () => Array.from(bea().querySelectorAll<HTMLElement>('.acct-team')).map(words);
        // A key whose login is in two teams: the read that follows the sign-in offers them at once, and no line says
        // that something could not be read.
        desktop.status = teamless;
        fireEvent.input(within(bea()).getByPlaceholderText('Or paste a Reply API key'), {
            target: { value: 'made-up-key' },
        });
        fireEvent.click(within(bea()).getByRole('button', { name: 'Use key' }));
        await waitFor(() => expect(words(bea())).toContain(why));
        expect(words(bea())).not.toMatch(/could not be read/);
        expect(teamRows()).toEqual(['Made-up Five Choose', 'Made-up Nine Choose']);
        expect(within(bea()).queryByRole('button', { name: 'Connect a mailbox or LinkedIn' })).toBeNull();
        // A pick asks the desktop by the account and the team's id, with no question, and the card reads as that team.
        desktop.status = withReplyAccounts(setupFound, [
            ada,
            replyAccount('outbound', { ...signedIn, senders: [linkedIn] }),
        ]);
        const row = (name: string) =>
            bea().querySelectorAll<HTMLElement>('.acct-team')[teams.findIndex((each) => each.name === name)]!;
        fireEvent.click(within(row('Made-up Nine')).getByRole('button', { name: 'Choose' }));
        await waitFor(() => expect(words(bea())).toContain('Made-up Bea'));
        expect(words(bea())).not.toContain(why);
        expect(desktop.calls.filter((call) => call.name === 'setup.chooseTeam').map((call) => call.args)).toEqual([
            [{ type: 'reply', name: 'outbound' }, '9'],
        ]);
        // Its refusal is said on the card.
        desktop.status = teamless;
        desktop.changed();
        await waitFor(() => expect(teamRows()).toHaveLength(2));
        desktop.actAnswers.set(
            'setup.chooseTeam',
            refusal("The team was not chosen. Team 5 isn't one you can act in."),
        );
        fireEvent.click(within(row('Made-up Five')).getByRole('button', { name: 'Choose' }));
        await waitFor(() =>
            expect(words(within(bea()).getByRole('alert'))).toBe(
                "The team was not chosen. Team 5 isn't one you can act in.",
            ),
        );
    });
});
