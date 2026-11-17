import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import type { NotificationChannel, NotificationsInfo } from '../../api/types';
import { Console } from '../../console/console';
import { attention } from '../../console/needs';
import * as consoleCampaigns from '../../console/campaigns';
import { chats, conversations } from '../../console/state';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { route } from '../../router';
import { setupFound, stubDesktop, stubStream } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import * as shell from '../../shell/shell';
import { chosenSection } from './chosen';

// The Notifications section of Settings: the notification channels as system.info says them, read only, in a browser
// and inside the desktop's window.

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
    shell.info.value = null;
    chosenSection.value = 'appearance';
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});
afterEach(() => {
    cleanup();
    mode.value = 'live';
});

function inBrowser(notifications: NotificationsInfo | null): Reporting {
    const fake = new Reporting(notifications).answer('preference.list', 'preference.list/none');
    renderApp(fake);
    route.value = { screen: 'settings' };
    return fake;
}

const notifications = () => screen.getByRole('heading', { name: /^Notifications/ }).closest<HTMLElement>('section')!;

describe('Notifications in Settings in a browser', () => {
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

    it('says what the channels are for and how they are turned on or off, in a browser by asking the AI, and writes nothing', async () => {
        const fake = inBrowser(report([channel({ channel: 'ntfy' })]));
        await waitFor(() => screen.getByText('ntfy'));
        const text = words(notifications());
        expect(text).toContain(
            'This is how Jason tells you that an approval or a question is waiting for you. To turn a channel on or off, ask your AI.',
        );
        expect(text).not.toContain('Connect panel');
        expect(text).not.toContain('Notifications:*');
        expect(within(notifications()).queryAllByRole('button')).toEqual([]);
        expect(within(notifications()).queryAllByRole('textbox')).toEqual([]);
        expect(fake.calls.filter((call) => call.kind === 'write')).toEqual([]);
    });
});

describe('Notifications in Settings inside the desktop', () => {
    it('is a section of its own, listed before Models, with the Connect panel clause in its caption', async () => {
        mode.value = 'live';
        const fake = new Reporting(
            report([channel({ channel: 'ntfy', outcome: 'delivered', at: new Date().toISOString() })]),
        )
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/none')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all');
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
        render(<Console api={fake} desktop={stubDesktop(setupFound)} stream={stubStream().stream} />);
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Settings' }),
        );
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        const list = within(screen.getByRole('navigation', { name: 'Settings' }));
        const names = list.getAllByRole('button').map((each) => words(each));
        expect(names.indexOf('Notifications')).toBe(names.indexOf('Models') - 1);
        fireEvent.click(list.getByRole('button', { name: 'Notifications' }));
        await waitFor(() => expect(words(notifications())).toContain('Delivered just now.'));
        expect(words(notifications())).toContain(
            'To turn a channel on or off, ask your AI; a provider’s channel can also be connected or disconnected on the Connect panel.',
        );
    });
});
