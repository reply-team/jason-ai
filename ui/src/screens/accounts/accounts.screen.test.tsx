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
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../../test/desktop';
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

function inDesktop(status: DesktopSetup = setupFound): StubDesktop {
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
    const desktop = chatDesktop(status);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);

async function openAccounts(): Promise<HTMLElement> {
    await waitFor(() => side().getByRole('button', { name: /^Needs you/ }));
    bringForward('Accounts');
    return waitFor(() => {
        const page = screen.getByRole('heading', { name: 'Accounts', level: 1 }).closest<HTMLElement>('.main')!;
        expect(page.querySelector('.card .acct')).not.toBeNull();
        return page;
    });
}

describe('Accounts inside the desktop', () => {
    it('draws the Reply card with its accounts, and not the browsers sentence', async () => {
        inDesktop();
        const page = await openAccounts();
        const card = within(page).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
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
        const card = () => within(page).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
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
        const card = () => within(page).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        await waitFor(() => within(card()).getByRole('button', { name: 'Gmail' }));
        bringForward('System');
        await waitFor(() => expect(screen.queryByRole('heading', { name: 'Accounts', level: 1 })).toBeNull());
        bringForward('Accounts');
        const again = await openAccounts();
        expect(within(again).queryByRole('button', { name: 'Gmail' })).toBeNull();
    });
});
