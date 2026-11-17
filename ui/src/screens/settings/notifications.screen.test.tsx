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

// The Notifications section of Settings: the notification channels as system.info says them, in a browser and inside the
// desktop's window, with a switch per channel that asks the runtime to turn that one channel on or off.

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

    it('says what the channels are for and how one is switched, and writes nothing until a switch is pressed', async () => {
        const fake = inBrowser(report([channel({ channel: 'toast', kind: 'toast' }), channel({ channel: 'ntfy' })]));
        await waitFor(() => screen.getByText('ntfy'));
        const text = words(notifications());
        expect(text).toContain(
            'This is how Jason tells you that an approval or a question is waiting for you. Turn one channel on or off with its switch, or with jason notification enable or disable. A switch is made in your name. Every notification at once is switched in the settings file.',
        );
        expect(text).not.toContain('Connect panel');
        const switches = within(notifications()).getAllByRole('switch');
        expect(switches.map((each) => [each.getAttribute('aria-label'), each.getAttribute('aria-checked')])).toEqual([
            ['Desktop toast', 'true'],
            ['ntfy', 'true'],
        ]);
        expect(switches.every((each) => !(each as HTMLButtonElement).disabled)).toBe(true);
        expect(within(notifications()).queryAllByRole('textbox')).toEqual([]);
        expect(fake.calls.filter((call) => call.kind === 'write')).toEqual([]);
    });

    it('turns one channel off as the paired person and shows the channels as the runtime answers', async () => {
        const fake = inBrowser(report([channel({ channel: 'toast', kind: 'toast', presence: 'available' })]));
        fake.answerAnyRequest('notification.disable', 'notification.disable/toast');
        await waitFor(() => screen.getByText('Desktop toast'));

        fireEvent.click(within(notifications()).getByRole('switch', { name: 'Desktop toast' }));

        await waitFor(() =>
            expect(
                within(notifications()).getByRole('switch', { name: 'Desktop toast' }).getAttribute('aria-checked'),
            ).toBe('false'),
        );
        expect(fake.calls.filter((call) => call.kind === 'write')).toEqual([
            { operation: 'notification.disable', body: { channel: 'toast' }, kind: 'write' },
        ]);
        // The answer is the runtime's whole section: the plugin channels it names come with it.
        expect(within(notifications()).getByRole('switch', { name: 'pager' }).getAttribute('aria-checked')).toBe(
            'true',
        );
    });

    it('turns a channel that is off on with enable', async () => {
        const fake = inBrowser(
            report([channel({ channel: 'toast', kind: 'toast', on: false, presence: 'available' })]),
        );
        fake.answerAnyRequest('notification.enable', 'notification.enable/toast');
        await waitFor(() => screen.getByText('Desktop toast'));

        fireEvent.click(within(notifications()).getByRole('switch', { name: 'Desktop toast' }));

        await waitFor(() =>
            expect(
                within(notifications()).getByRole('switch', { name: 'Desktop toast' }).getAttribute('aria-checked'),
            ).toBe('true'),
        );
        expect(fake.sent('notification.enable')).toEqual({ channel: 'toast' });
    });

    it('while a switch waits for its answer, keeps it where it was and holds every switch, then shows the answer', async () => {
        const fake = inBrowser(
            report([
                channel({ channel: 'toast', kind: 'toast', presence: 'available' }),
                channel({ channel: 'pager', readiness: 'ready' }),
            ]),
        );
        fake.answerAnyRequest('notification.disable', 'notification.disable/toast');
        const release = fake.hold('notification.disable');
        await waitFor(() => screen.getByText('Desktop toast'));

        fireEvent.click(within(notifications()).getByRole('switch', { name: 'Desktop toast' }));

        await waitFor(() => expect(fake.sent('notification.disable')).toEqual({ channel: 'toast' }));
        const waiting = within(notifications()).getAllByRole('switch') as HTMLButtonElement[];
        // Nothing is shown as switched before the runtime says so, and no second switch is pressed meanwhile.
        expect(waiting.map((each) => [each.getAttribute('aria-label'), each.getAttribute('aria-checked')])).toEqual([
            ['Desktop toast', 'true'],
            ['pager', 'true'],
        ]);
        expect(waiting.every((each) => each.disabled)).toBe(true);
        fireEvent.click(within(notifications()).getByRole('switch', { name: 'pager' }));
        expect(fake.calls.filter((call) => call.kind === 'write')).toHaveLength(1);

        release();

        await waitFor(() =>
            expect(
                within(notifications()).getByRole('switch', { name: 'Desktop toast' }).getAttribute('aria-checked'),
            ).toBe('false'),
        );
        const answered = within(notifications()).getAllByRole('switch') as HTMLButtonElement[];
        expect(answered.find((each) => each.getAttribute('aria-label') === 'pager')!.disabled).toBe(false);
    });

    it('turns a plugin channel off by its id', async () => {
        const fake = inBrowser(
            report([
                channel({ channel: 'toast', kind: 'toast', presence: 'available' }),
                channel({ channel: 'pager', readiness: 'ready' }),
            ]),
        );
        fake.answerAnyRequest('notification.disable', 'notification.disable/plugin');
        await waitFor(() => screen.getByText('pager'));

        fireEvent.click(within(notifications()).getByRole('switch', { name: 'pager' }));

        await waitFor(() =>
            expect(within(notifications()).getByRole('switch', { name: 'pager' }).getAttribute('aria-checked')).toBe(
                'false',
            ),
        );
        expect(fake.sent('notification.disable')).toEqual({ channel: 'pager' });
        expect(
            within(notifications()).getByRole('switch', { name: 'Desktop toast' }).getAttribute('aria-checked'),
        ).toBe('true');
    });

    // A report read before the settings file stopped naming a channel, or before its plugin was taken away: the switch
    // is pressed, and the runtime's sentence is what the page says.
    it.each([
        ['pager', 'notification.enable/not-named'],
        ['nobody-installed-this', 'notification.enable/unknown-channel'],
    ] as const)('shows the runtime’s sentence for %s refused as %s', async (name, exchange) => {
        const fake = inBrowser(report([channel({ channel: name, on: false, readiness: 'ready' })]));
        fake.answerAnyRequest('notification.enable', exchange);
        await waitFor(() => screen.getByText(name));

        fireEvent.click(within(notifications()).getByRole('switch', { name }));

        const alert = await within(notifications()).findByRole('alert');
        expect(words(alert)).toBe((fixtures[exchange].response as { error: { message: string } }).error.message);
        expect(fake.sent('notification.enable')).toEqual({ channel: name });
        expect(within(notifications()).getByRole('switch', { name }).getAttribute('aria-checked')).toBe('false');
    });

    it('shows the sentence of the runtime for a refused switch, and the switch stays where it was', async () => {
        const fake = inBrowser(report([channel({ channel: 'chat-room', on: false, readiness: 'ready' })]));
        fake.answerAnyRequest('notification.enable', 'notification.enable/not-configured');
        await waitFor(() => screen.getByText('chat-room'));

        fireEvent.click(within(notifications()).getByRole('switch', { name: 'chat-room' }));

        const alert = await within(notifications()).findByRole('alert');
        expect(words(alert)).toBe(
            (fixtures['notification.enable/not-configured'].response as { error: { message: string } }).error.message,
        );
        const after = within(notifications()).getByRole('switch', { name: 'chat-room' }) as HTMLButtonElement;
        expect(after.getAttribute('aria-checked')).toBe('false');
        expect(after.disabled).toBe(false);
    });

    it('draws a plugin channel that could not deliver with what is missing, its switch inert while it is off', async () => {
        inBrowser(
            report([
                channel({
                    channel: 'chat-room',
                    on: false,
                    readiness: 'binding_invalid',
                    readiness_detail: '/chat required',
                }),
                channel({ channel: 'pager', on: true, readiness: 'unavailable', readiness_detail: 'not loaded' }),
            ]),
        );
        await waitFor(() => screen.getByText('chat-room'));
        const rows = Array.from(notifications().querySelectorAll('tbody tr'));
        expect(words(rows[0]!.children[1]!)).toBe(
            'off Cannot be turned on: its binding is not valid (/chat required).',
        );
        expect((within(rows[0] as HTMLElement).getByRole('switch') as HTMLButtonElement).disabled).toBe(true);
        // One that is on says it cannot deliver, by the same detail, and can always be turned off.
        expect(words(rows[1]!.children[1]!)).toBe('on Cannot deliver: the plugin is unavailable (not loaded).');
        expect((within(rows[1] as HTMLElement).getByRole('switch') as HTMLButtonElement).disabled).toBe(false);
    });

    it('says a plugin of another kind, and a reason this page has no words for, by the runtime’s own word', async () => {
        inBrowser(
            report([
                channel({
                    channel: 'crm',
                    on: false,
                    readiness: 'not_declared',
                    readiness_detail: 'a provider plugin declaring no version 1 of the delivery contract',
                }),
                channel({ channel: 'later', on: false, readiness: 'a_new_reason' }),
            ]),
        );
        await waitFor(() => screen.getByText('crm'));
        const rows = Array.from(notifications().querySelectorAll('tbody tr')).map((row) => words(row.children[1]!));
        expect(rows).toEqual([
            'off Cannot be turned on: the plugin does not declare notifications (a provider plugin declaring no version 1 of the delivery contract).',
            'off Cannot be turned on: a_new_reason.',
        ]);
    });

    it('while every notification is off, says the one line to change and every switch is off and inert', async () => {
        const fake = inBrowser(
            report(
                [channel({ channel: 'toast', kind: 'toast', on: false }), channel({ channel: 'ntfy', on: false })],
                false,
            ),
        );
        await waitFor(() => screen.getByText('ntfy'));
        const said = (fixtures['notification.enable/notifications-off'].response as { error: { message: string } })
            .error.message;
        expect(words(notifications())).toContain(said);
        const switches = within(notifications()).getAllByRole('switch') as HTMLButtonElement[];
        expect(switches.map((each) => [each.getAttribute('aria-checked'), each.disabled])).toEqual([
            ['false', true],
            ['false', true],
        ]);
        fireEvent.click(switches[0]!);
        expect(fake.calls.filter((call) => call.kind === 'write')).toEqual([]);
    });

    it('keeps the switch of a toast Windows did not show, and it turns the channel off', async () => {
        const fake = inBrowser(
            report([
                channel({
                    channel: 'toast',
                    kind: 'toast',
                    outcome: 'silenced',
                    reason: 'disabled_for_application',
                    at: new Date().toISOString(),
                    presence: 'available',
                }),
            ]),
        );
        fake.answerAnyRequest('notification.disable', 'notification.disable/toast');
        await waitFor(() => screen.getByText('Desktop toast'));
        const toast = within(notifications()).getByRole('switch', { name: 'Desktop toast' }) as HTMLButtonElement;
        expect([toast.getAttribute('aria-checked'), toast.disabled]).toEqual(['true', false]);

        fireEvent.click(toast);

        await waitFor(() =>
            expect(
                within(notifications()).getByRole('switch', { name: 'Desktop toast' }).getAttribute('aria-checked'),
            ).toBe('false'),
        );
        expect(fake.sent('notification.disable')).toEqual({ channel: 'toast' });
    });

    it('in the Demo, every switch is inert and says so', async () => {
        mode.value = 'demo';
        const fake = inBrowser(report([channel({ channel: 'toast', kind: 'toast' })]));
        await waitFor(() => screen.getByText('Desktop toast'));
        const toast = within(notifications()).getByRole('switch', { name: 'Desktop toast' }) as HTMLButtonElement;
        expect(toast.disabled).toBe(true);
        expect(words(notifications())).toContain('This is the Demo: its switches change nothing.');
        fireEvent.click(toast);
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
            'Turn one channel on or off with its switch, or with jason notification enable or disable; a provider’s channel is connected or disconnected on the Connect panel. A switch is made in your name. Every notification at once is switched in the settings file.',
        );
    });
});
