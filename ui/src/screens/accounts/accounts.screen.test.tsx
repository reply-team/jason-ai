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
import type { DesktopSetup } from '../../platform/desktop';
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
        replyAccount('outbound', { team: '9', signedIn: true, user: 'bea@example.test', senders: [linkedIn] }),
        replyAccount('routed-only', { held: false }),
    ]);
    const cardOf = (page: HTMLElement, label: string) => within(page).getByRole('region', { name: label });
    const cards = (page: HTMLElement) =>
        Array.from(page.querySelectorAll<HTMLElement>('.reply-account')).map((card) =>
            words(card.querySelector('.acct-head')!),
        );
    const section = (page: HTMLElement) => within(page).getByRole('region', { name: 'Reply' });
    const refusal = (refused: string) => ({ refused, code: null, offer: null, reread: false });

    it('draws one Reply section, then one card per account in the desktop’s order, each named as the Reply CLI names it with its team beside it, the default marked and Make default on the others', async () => {
        inDesktop(three);
        const page = await openAccounts('.reply-account');
        expect(words(section(page).querySelector('.reply-name')!)).toBe('Reply Recommended');
        expect(cards(page)).toEqual([
            'Reply default Default',
            'Reply outbound team 9 Make default',
            'Reply routed-only Make default',
        ]);
        const ada = cardOf(page, 'Reply account default');
        expect(words(ada.querySelector('.reply-state')!)).toBe('Signed in as ada@example.test');
        expect(words(ada.querySelector('.acct')!)).toContain('Sales inbox');
        expect(within(ada).queryByRole('button', { name: 'Make default' })).toBeNull();
        const bea = cardOf(page, 'Reply account outbound, team 9');
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
        const desktop = inDesktop(three);
        const page = await openAccounts('.reply-account');
        const bea = () => cardOf(page, 'Reply account outbound, team 9');
        const outbound = { type: 'reply', name: 'outbound', team: '9' };
        const row = bea().querySelector<HTMLElement>('.acct')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Pause' }));
        await waitFor(() => expect(words(within(bea()).getByRole('dialog'))).toMatch(/Done\./));
        expect(within(cardOf(page, 'Reply account default')).queryByRole('dialog')).toBeNull();
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
            ['setup.connectReply', outbound],
            ['setup.connectReplyWithKey', 'made-up-key', { type: 'reply', name: 'routed-only', team: null }],
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
            [{ type: 'reply', name: 'routed-only', team: null }],
            [{ type: 'reply', name: 'routed-only', team: null }],
        ]);
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
        expect(words(section(page))).not.toMatch(/could not be read|no account is the default/i);
        expect(cards(page)).toEqual(['Reply default Make default']);
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

    it('asks a desktop that lacks Make default for none: no card offers it', async () => {
        const desktop = chatDesktop(three);
        delete (desktop.setup as Partial<typeof desktop.setup>).makeDefault;
        inDesktop(three, desktop);
        const page = await openAccounts('.reply-account');
        expect(cards(page)).toEqual(['Reply default Default', 'Reply outbound team 9', 'Reply routed-only']);
    });
});
