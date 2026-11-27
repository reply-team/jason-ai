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

describe('Accounts inside the desktop', () => {
    it('draws the Reply card with its accounts, and not the browsers sentence', async () => {
        inDesktop();
        const page = await openAccounts();
        const card = earlierCard(page);
        expect(words(card.querySelector('.acct')!)).toContain('Sales inbox');
        expect(words(page)).not.toContain('a browser cannot do them');
        expect(screen.queryByRole('heading', { name: /^Notifications/ })).toBeNull();
    });

    it('asks the desktop for a Pause by the account id, and the desktop answers it', async () => {
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
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
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
        replyAccount('default', { default: true, signedIn: true, user: 'ada@example.test', senders: [mailbox] }),
        replyAccount('outbound', { signedIn: true, user: 'bea@example.test', senders: [linkedIn] }),
        replyAccount('routed-only', { held: false }),
    ]);
    const cardOf = (page: HTMLElement, label: string) => within(page).getByRole('region', { name: label });
    const cards = (page: HTMLElement) =>
        Array.from(page.querySelectorAll<HTMLElement>('.reply-account')).map((card) =>
            words(card.querySelector('.acct-head')!),
        );
    const section = (page: HTMLElement) => within(page).getByRole('region', { name: 'Reply' });
    const refusal = (refused: string) => ({ refused, code: null, offer: null, reread: false });

    it('draws one Reply section, then one card per account in the desktop’s order, each named as the Reply CLI names its profile and nothing beside it, the default marked and Make default on every other', async () => {
        inDesktop(three);
        const page = await openAccounts('.reply-account');
        expect(words(section(page).querySelector('.reply-name')!)).toBe('Reply Recommended');
        expect(cards(page)).toEqual([
            'Reply default Default',
            'Reply outbound Make default',
            'Reply routed-only Make default',
        ]);
        const ada = cardOf(page, 'Reply account default');
        expect(words(ada.querySelector('.reply-state')!)).toBe('Signed in as ada@example.test');
        expect(words(ada.querySelector('.acct')!)).toContain('Sales inbox');
        expect(within(ada).queryByRole('button', { name: 'Make default' })).toBeNull();
        const bea = cardOf(page, 'Reply account outbound');
        expect(words(bea.querySelector('.reply-state')!)).toBe('Signed in as bea@example.test');
        expect(words(bea.querySelector('.acct')!)).toContain('Made-up Bea');
        // A profile only a route names, which the Reply CLI does not hold: not signed in, with the sign-in that makes it.
        const routed = cardOf(page, 'Reply account routed-only');
        expect(words(routed.querySelector('.reply-state')!)).toBe('Not signed in');
        expect(within(routed).getByRole('button', { name: 'Sign in with Reply' }).classList.contains('p')).toBe(true);
        expect(within(routed).getByRole('button', { name: 'Use key' })).toBeTruthy();
        expect(within(routed).queryByRole('button', { name: 'Add account' })).toBeNull();
        // No card says who uses it.
        expect(words(page)).not.toMatch(/uses this sign-in|does not use it yet|whether jason uses/i);
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
        fireEvent.click(within(bea()).getByRole('button', { name: 'Add account' }));
        fireEvent.click(await waitFor(() => within(bea()).getByRole('button', { name: 'Gmail' })));
        await answered(/The link opened in your browser\./);
        fireEvent.click(within(bea()).getByRole('button', { name: 'Add account' }));
        fireEvent.click(await waitFor(() => within(bea()).getByRole('button', { name: 'LinkedIn' })));
        fireEvent.input(
            await waitFor(() => within(bea()).getByRole('textbox', { name: 'Name for the LinkedIn account' })),
            {
                target: { value: 'Made-up Dee' },
            },
        );
        fireEvent.click(within(bea()).getByRole('button', { name: 'Make the link' }));
        await answered(/The link opened in your browser\./);
        fireEvent.click(within(bea()).getByRole('button', { name: 'Sign in with Reply' }));
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
            ['setup.connectReply', outbound],
            ['setup.connectReplyWithKey', 'made-up-key', { type: 'reply', name: 'routed-only' }],
        ]);
    });

    it('asks the desktop to make an account the default, by that account, and says on its card what the desktop answered', async () => {
        const desktop = inDesktop(three);
        desktop.actAnswers.set('setup.makeDefault', [
            refusal(
                'A search still followed runs on the account that is the default now, so nothing was changed: stop it, or let it end, then try again.',
            ),
            { done: true },
        ]);
        const page = await openAccounts('.reply-account');
        const routed = () => cardOf(page, 'Reply account routed-only');
        fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
        await waitFor(() =>
            expect(words(within(routed()).getByRole('alert'))).toMatch(/^A search still followed runs on the account/),
        );
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
        // While the desktop has not answered, the card says what it waits on, never that Reply is asked, which Make default
        // never asks: where a default is set, the person's answer in the desktop's box; where none is, the default made.
        const held: ((answer: DesktopReplyDone) => void)[] = [];
        desktop.setup.makeDefault = () => new Promise((resolve) => held.push(resolve));
        const waiting = async (line: string) => {
            fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
            await waitFor(() => expect(words(within(routed()).getByRole('dialog'))).toContain(line));
            expect(words(routed())).not.toContain('Asking Reply');
            held.shift()!({ done: true });
            await waitFor(() => expect(words(within(routed()).getByRole('dialog'))).toMatch(/Done\./));
        };
        await waiting('Answer the question Jason Desktop asks.');
        desktop.status = withReplyAccounts(
            setupFound,
            three.accounts!.accounts.map((each) => ({ ...each, default: false })),
            { noDefault: true },
        );
        desktop.changed();
        await waitFor(() => expect(words(section(page))).toContain('No account is the default yet'));
        await waiting('Making it the default…');
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
        expect(words(section(page))).toContain('Which account is the default could not be read.');
        expect(words(section(page))).not.toContain('No account is the default yet');
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
            'No account is the default yet. Campaigns have nothing to send through until you make one the default.',
        );
        expect(cards(page)).toEqual(['Reply default Make default']);
        cleanup();

        // A default of another plugin: no Reply account is marked, every card offers Make default, and nothing is missing.
        inDesktop(
            withReplyAccounts(setupFound, [replyAccount('default', { signedIn: true, user: 'ada@example.test' })]),
        );
        page = await openAccounts('.reply-account');
        expect(words(section(page))).not.toMatch(/could not be read|no account is the default|names a Reply team/i);
        expect(cards(page)).toEqual(['Reply default Make default']);
        cleanup();

        // A campaign's route that names a Reply team beside its profile still works there, and is said, with the way off it.
        const accounts = [replyAccount('default', { signedIn: true, user: 'ada@example.test' }), replyAccount('quiet')];
        inDesktop(withReplyAccounts(setupFound, accounts, { campaignsWithTeam: 1 }));
        page = await openAccounts('.reply-account');
        const teamLine = () => Array.from(section(page).querySelectorAll<HTMLElement>('.team-routes')).map(words);
        expect(teamLine()).toEqual([
            '1 campaign has a route that names a Reply team beside its profile. Its work goes to that team, and the profile’s card shows the profile’s own. Route it to a profile pinned to that team instead.',
        ]);
        cleanup();
        inDesktop(withReplyAccounts(setupFound, accounts, { campaignsWithTeam: 2 }));
        page = await openAccounts('.reply-account');
        expect(teamLine()).toEqual([
            '2 campaigns have a route that names a Reply team beside its profile. Their work goes to that team, and the profile’s card shows the profile’s own. Route each to a profile pinned to that team instead.',
        ]);
    });

    it('with no usable Reply CLI draws the section alone, saying what it says today of the CLI', async () => {
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

    it('says a sign-in’s or a key’s refusal on the card it was clicked on, in the desktop’s words without the window’s wrapper, and nothing of it in the section', async () => {
        const desktop = inDesktop(three);
        const wrapped = (member: string, words: string) =>
            `Error invoking remote method 'jason:setup.${member}': Error: ${words}`;
        const keyRefused = 'Reply did not accept that key. Authentication credentials are missing or invalid.';
        const notPlaced =
            'Signed in, but Jason’s Reply plugin could not be placed: The update feed at http://127.0.0.1:9/manifest.json could not be reached.';
        desktop.refuse.set('setup.connectReplyWithKey', wrapped('connectReplyWithKey', keyRefused));
        desktop.refuse.set('setup.connectReply', wrapped('connectReply', notPlaced));
        const [ada, bea, routedOnly] = three.accounts!.accounts as [DesktopAccount, DesktopAccount, DesktopAccount];
        const page = await openAccounts('.reply-account');
        const second = () => cardOf(page, 'Reply account outbound');
        const routed = () => cardOf(page, 'Reply account routed-only');
        const useKey = (card: HTMLElement) => {
            fireEvent.input(within(card).getByPlaceholderText('Or paste a Reply API key'), {
                target: { value: 'made-up-key' },
            });
            fireEvent.click(within(card).getByRole('button', { name: 'Use key' }));
        };
        const checkAgain = () =>
            fireEvent.click(
                within(cardOf(page, 'Reply account default')).getAllByRole('button', { name: 'Check again' })[0]!,
            );
        useKey(second());
        await waitFor(() => expect(words(within(second()).getByRole('alert'))).toBe(keyRefused));
        expect(within(section(page)).queryByRole('alert')).toBeNull();
        expect(within(cardOf(page, 'Reply account default')).queryByRole('alert')).toBeNull();
        // A later read that shows another account's sign-in changed, and this one's as it was, keeps the line.
        desktop.status = withReplyAccounts(setupFound, [{ ...ada, user: 'cy@example.test' }, bea, routedOnly]);
        checkAgain();
        await waitFor(() => expect(words(cardOf(page, 'Reply account default'))).toContain('cy@example.test'));
        expect(words(within(second()).getByRole('alert'))).toBe(keyRefused);
        // The next sign-in, on another card, says its own refusal there, and the first card's goes. That sign-in signed
        // in before the plugin could not be placed: the read after it shows the account signed in, the act's own change,
        // and the line stays; so it does where a read begun before the sign-in lands after it.
        const status = desktop.setup.status;
        let landEarlier: (state: DesktopSetup) => void = () => undefined;
        desktop.setup.status = () => new Promise((resolve) => (landEarlier = resolve));
        checkAgain();
        desktop.setup.status = status;
        const earlierState = desktop.status;
        desktop.status = withReplyAccounts(setupFound, [
            ada,
            bea,
            { ...routedOnly, signedIn: true, user: 'bea@example.test' },
        ]);
        fireEvent.click(within(routed()).getByRole('button', { name: 'Sign in with Reply' }));
        await waitFor(() => expect(words(within(routed()).getByRole('alert'))).toBe(notPlaced));
        expect(within(second()).queryByRole('alert')).toBeNull();
        await waitFor(() => expect(words(routed())).toContain('Signed in as bea@example.test'));
        expect(words(within(routed()).getByRole('alert'))).toBe(notPlaced);
        landEarlier(earlierState);
        await waitFor(() => expect(words(routed())).toContain('Not signed in'));
        expect(words(within(routed()).getByRole('alert'))).toBe(notPlaced);
        // A later read that shows this account's sign-in changed, signed out in a terminal, takes it away.
        desktop.status = three;
        checkAgain();
        await waitFor(() => expect(within(routed()).queryByRole('alert')).toBeNull());
        // The next act of any kind takes it away too.
        useKey(second());
        await waitFor(() => expect(words(within(second()).getByRole('alert'))).toBe(keyRefused));
        fireEvent.click(within(routed()).getByRole('button', { name: 'Make default' }));
        await waitFor(() => expect(desktop.calls.some((call) => call.name === 'setup.makeDefault')).toBe(true));
        await waitFor(() => expect(within(second()).queryByRole('alert')).toBeNull());
        // The Connect panel's card is the default's, whichever that is. A refusal said there stays with its account: when
        // the default moves to another, here signed in as the same person, that one's card says nothing of it, nor does
        // the first account's when it is back.
        bringForward('Connect');
        const connect = () =>
            screen
                .getByRole('heading', { name: 'Connect', level: 1 })
                .closest<HTMLElement>('.painted')!
                .querySelector<HTMLElement>('.reply-card')!;
        const drawn = () => connect().querySelector('.acct-name')?.textContent;
        await waitFor(() => expect(drawn()).toBe('default'));
        useKey(connect());
        await waitFor(() => expect(words(within(connect()).getByRole('alert'))).toBe(keyRefused));
        const movedDefault = withReplyAccounts(setupFound, [
            { ...ada, default: false },
            bea,
            { ...routedOnly, default: true, signedIn: true, user: 'ada@example.test' },
        ]);
        desktop.status = movedDefault;
        desktop.changed();
        await waitFor(() => expect(drawn()).toBe('routed-only'));
        expect(within(connect()).queryByRole('alert')).toBeNull();
        desktop.status = three;
        desktop.changed();
        await waitFor(() => expect(drawn()).toBe('default'));
        expect(within(connect()).queryByRole('alert')).toBeNull();
        // Nor is it said on another account's card where the default moved while the key was asked and no read came after.
        let refuseKey: (error: Error) => void = () => undefined;
        desktop.setup.connectReplyWithKey = () => new Promise((_, reject) => (refuseKey = reject));
        useKey(connect());
        desktop.status = movedDefault;
        desktop.changed();
        await waitFor(() => expect(drawn()).toBe('routed-only'));
        desktop.refuse.set('setup.status', wrapped('status', 'This window may not ask that.'));
        refuseKey(new Error(wrapped('connectReplyWithKey', keyRefused)));
        const panel = () => connect().closest<HTMLElement>('.painted')!;
        await waitFor(() => expect(words(panel())).toContain('This window may not ask that.'));
        expect(words(connect())).not.toContain(keyRefused);
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

    it('asks a desktop that lacks Make default for none: no card offers it', async () => {
        const desktop = chatDesktop(three);
        delete (desktop.setup as Partial<typeof desktop.setup>).makeDefault;
        inDesktop(three, desktop);
        const page = await openAccounts('.reply-account');
        expect(cards(page)).toEqual(['Reply default Default', 'Reply outbound', 'Reply routed-only']);
    });

    it('offers the teams of a sign-in in more than one in place of its accounts, from the read right after a sign-in on the card, and pins the one picked; a desktop without the pick says it cannot choose', async () => {
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
        expect(teamRows()).toEqual(['Made-up Five team 5 Choose', 'Made-up Nine team 9 Choose']);
        expect(within(bea()).queryByRole('button', { name: 'Add account' })).toBeNull();
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
        cleanup();
        // A desktop without the pick: the card says so, and offers no choice it cannot make.
        const earlier = chatDesktop(teamless);
        delete (earlier.setup as Partial<typeof earlier.setup>).chooseTeam;
        inDesktop(teamless, earlier);
        const again = await openAccounts('.reply-account');
        const card = cardOf(again, 'Reply account outbound');
        expect(words(card)).toContain(
            'This sign-in belongs to more than one Reply team, and this version of Jason Desktop cannot choose one.',
        );
        expect(within(card).queryByRole('button', { name: 'Choose' })).toBeNull();
        expect(words(card)).not.toMatch(/could not be read/);
    });
});
