import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import type { NotificationChannel, NotificationsInfo } from '../../api/types';
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
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import * as classicCampaigns from '../campaigns/store';
import * as store from './store';

// The Accounts page: the provider's accounts by the Reply card's own code inside the desktop, nothing of them in a
// browser, and the notification channels as system.info says them, read only in both.

const channel = (fields: Partial<NotificationChannel> & { channel: string }): NotificationChannel => ({
    kind: 'plugin',
    on: true,
    outcome: null,
    reason: null,
    code: null,
    class: null,
    at: null,
    ...fields,
});

const report = (channels: NotificationChannel[], enabled = true, lastAt: string | null = null): NotificationsInfo => ({
    enabled,
    channels,
    last_notification_id: null,
    last_at: lastAt,
});

/** The recorded system.info with the notification section a test states; the recordings hold the toast alone. */
class Reporting extends FakeApi {
    constructor(private readonly notifications: NotificationsInfo | null) {
        super();
    }

    override async call<T>(operation: string, given: object = {}, kind: 'read' | 'write' = 'read'): Promise<T> {
        if (operation !== 'system.info') return super.call<T>(operation, given, kind);
        this.calls.push({ operation, body: given, kind });
        return { ...fixtures['system.info/with-ui'].response, notifications: this.notifications } as T;
    }
}

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

beforeEach(() => {
    store.info.value = null;
    store.failure.value = null;
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

function inBrowser(notifications: NotificationsInfo | null): Reporting {
    const fake = new Reporting(notifications)
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty');
    renderApp(fake);
    route.value = { screen: 'accounts' };
    return fake;
}

const notifications = () => screen.getByRole('heading', { name: /^Notifications/ }).closest<HTMLElement>('section')!;

describe('Accounts in a browser', () => {
    it('is in the sidebar, and says in one sentence that the provider acts are the desktops, with none offered', async () => {
        inBrowser(report([]));
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

    it('shows a channel that delivered, one that failed with its code and class, and one that is off', async () => {
        const at = new Date(Date.now() - 5 * 60_000).toISOString();
        inBrowser(
            report(
                [
                    channel({ channel: 'toast', kind: 'toast', outcome: 'delivered', at, presence: 'available' }),
                    {
                        ...channel({
                            channel: 'ntfy',
                            outcome: 'failed',
                            code: 'notification_unreachable',
                            class: 'transient',
                            at,
                        }),
                        detail: 'the server said something private',
                    } as NotificationChannel,
                    channel({ channel: 'slack', on: false, outcome: 'not_attempted', reason: 'off' }),
                ],
                true,
                at,
            ),
        );
        await waitFor(() => screen.getByText('Desktop toast'));
        const rows = Array.from(notifications().querySelectorAll('tbody tr')).map((row) =>
            Array.from(row.querySelectorAll('td')).map(words),
        );
        expect(rows).toEqual([
            ['Desktop toast', 'on', 'Delivered 5 min ago.'],
            ['ntfy', 'on', 'Failed 5 min ago (notification_unreachable, transient).'],
            ['slack', 'off', 'Switched off.'],
        ]);
        expect(words(notifications())).toContain('The last notification was sent 5 min ago.');
        // What the runtime keeps for itself in a failure is not drawn.
        expect(words(notifications())).not.toContain('private');
        expect(notifications().querySelector('h2 .badge')!.textContent).toBe('on');
    });

    it('says all notifications are off, and that no channel is set up where the runtime reports none', async () => {
        inBrowser(report([], false));
        await waitFor(() => screen.getByText(/No channel is set up/));
        expect(notifications().querySelector('h2 .badge')!.textContent).toBe('off');
    });

    it('says a channel nothing was sent to yet, and a toast with no desktop, in words', async () => {
        inBrowser(
            report([
                channel({ channel: 'toast', kind: 'toast', presence: 'no_desktop' }),
                channel({ channel: 'ntfy' }),
            ]),
        );
        await waitFor(() => screen.getByText('Desktop toast'));
        const rows = Array.from(notifications().querySelectorAll('tbody tr')).map((row) =>
            words(row.lastElementChild!),
        );
        expect(rows).toEqual(['This machine has no desktop to show it on.', 'Nothing has been sent yet.']);
    });

    it('says a runtime that reports no section does not report its channels', async () => {
        inBrowser(null);
        await waitFor(() => screen.getByText('This runtime does not report its notification channels.'));
    });

    it('says where the channels are changed, names Notifications:* and the skill, and writes nothing', async () => {
        const fake = inBrowser(report([channel({ channel: 'ntfy' })]));
        await waitFor(() => screen.getByText('ntfy'));
        const text = words(notifications());
        expect(text).toContain('Notifications:*');
        expect(text).toContain('settings file');
        expect(text).toContain('operating-the-installation');
        expect(within(notifications()).queryAllByRole('button')).toEqual([]);
        expect(within(notifications()).queryAllByRole('textbox')).toEqual([]);
        expect(fake.calls.filter((call) => call.kind === 'write')).toEqual([]);
    });
});

function inDesktop(info: NotificationsInfo | null, status: DesktopSetup = setupFound): StubDesktop {
    const fake = new Reporting(info)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two');
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

const side = () => within(document.querySelector<HTMLElement>('.side')!);

async function openAccounts(): Promise<HTMLElement> {
    await waitFor(() => side().getByRole('button', { name: 'Accounts' }));
    fireEvent.click(side().getByRole('button', { name: 'Accounts' }));
    return waitFor(() => {
        const page = screen.getByRole('heading', { name: 'Accounts', level: 1 }).closest<HTMLElement>('.main')!;
        expect(page.querySelector('.card .acct')).not.toBeNull();
        return page;
    });
}

describe('Accounts inside the desktop', () => {
    it('draws the Reply card with its accounts, and not the browsers sentence', async () => {
        inDesktop(report([channel({ channel: 'ntfy', outcome: 'delivered', at: new Date().toISOString() })]));
        const page = await openAccounts();
        const card = within(page).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        expect(words(card.querySelector('.acct')!)).toContain('Sales inbox');
        expect(words(page)).not.toContain('a browser cannot do them');
        // The notification half is the same as in a browser.
        await waitFor(() => expect(words(notifications())).toContain('Delivered just now.'));
    });

    it('asks the desktop for a Pause by the account id, and the desktop answers it', async () => {
        const desktop = inDesktop(report([]), {
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
        const desktop = inDesktop(report([]), {
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
        fireEvent.click(side().getByRole('button', { name: 'Connect' }));
        const connect = await waitFor(() => {
            const panel = screen.getByRole('heading', { name: 'Connect', level: 1 }).closest<HTMLElement>('.painted')!;
            expect(panel.querySelector('.acct')).not.toBeNull();
            return panel;
        });
        expect(within(cardIn(connect)).queryByRole('dialog')).toBeNull();
        // The other way round: an act asked on Connect shows its dialog there and not on Accounts.
        fireEvent.click(within(row(connect)).getByRole('button', { name: 'Delete' }));
        await waitFor(() => expect(within(cardIn(connect)).queryByRole('dialog')).not.toBeNull());
        expect(within(cardIn(page)).queryByRole('dialog')).toBeNull();
        expect(desktop.calls.filter((call) => call.name.startsWith('setup.reply')).length).toBeGreaterThan(0);
    });

    it('drops a choice the card held open when the page stops drawing it', async () => {
        inDesktop(report([]));
        const page = await openAccounts();
        const card = () => within(page).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        fireEvent.click(within(card()).getByRole('button', { name: 'Add account' }));
        await waitFor(() => within(card()).getByRole('button', { name: 'Gmail' }));
        fireEvent.click(side().getByRole('button', { name: 'System' }));
        await waitFor(() => expect(screen.queryByRole('heading', { name: 'Accounts', level: 1 })).toBeNull());
        fireEvent.click(side().getByRole('button', { name: 'Accounts' }));
        const again = await openAccounts();
        expect(within(again).queryByRole('button', { name: 'Gmail' })).toBeNull();
    });
});
