import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';
import { stubDesktop } from '../../test/desktop';
import { fullAccessStanding } from '../../platform/full-access';
import { routeCounts } from './route-counts';

function systemFake(ui: 'with-ui' | 'without-ui' = 'with-ui', tokens: 'two' | 'aged' = 'two') {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', `ui.tokens/${tokens}`);
    const rendered = renderApp(fake);
    fake.answer('system.info', `system.info/${ui}`);
    route.value = { screen: 'system' };
    return { fake, rendered };
}

describe('route counts', () => {
    it('says how many go by channel only when some do', () => {
        const counts = { global_override_count: 1, campaign_route_count: 3 };
        expect(routeCounts(counts)).toBe('1 global · 3 per campaign');
        expect(routeCounts({ ...counts, channel_route_count: 0 })).toBe('1 global · 3 per campaign');
        expect(routeCounts({ ...counts, channel_route_count: 2 })).toBe(
            '1 global · 3 per campaign · 2 of them by channel',
        );
    });
});

describe('System', () => {
    it('shows the recorded route counts without a channel clause when none go by channel', async () => {
        systemFake();
        const routes = fixtures['system.info/with-ui'].response.routes;
        await waitFor(() => expect(screen.getByText('This UI')).toBeTruthy());
        expect(routes.channel_route_count).toBe(0);
        expect(screen.getByText('0 global · 0 per campaign')).toBeTruthy();
        expect(screen.queryByText(/by channel/)).toBeNull();
    });

    it('shows the runtime, the port, the dispatcher and this UI', async () => {
        systemFake();
        const info = fixtures['system.info/with-ui'].response;
        await waitFor(() => expect(screen.getByText('This UI')).toBeTruthy());
        expect(screen.getByText(new RegExp(`127\\.0\\.0\\.1:${info.listening!.port}`))).toBeTruthy();
        expect(screen.getByText(info.ui!.version!, { exact: false })).toBeTruthy();
        expect(screen.getByText(/matches the runtime/)).toBeTruthy();
        expect(screen.queryByText(/Autostart/)).toBeNull();
    });

    it('the refresh button is held until the read answers, however long that takes', async () => {
        const { fake } = systemFake();
        await waitFor(() => expect(screen.getByText('This UI')).toBeTruthy());
        const refresh = screen.getByTitle('Refresh') as HTMLButtonElement;
        // Only timers are faked: the button turns for at least half a second, and this read takes longer.
        vi.useFakeTimers({ toFake: ['setTimeout'] });
        try {
            const release = fake.hold('system.info');
            await act(async () => {
                fireEvent.click(refresh);
                await vi.advanceTimersByTimeAsync(600);
            });
            expect(refresh.disabled).toBe(true);

            await act(async () => {
                release();
                await vi.advanceTimersByTimeAsync(0);
            });
            expect(refresh.disabled).toBe(false);
        } finally {
            vi.useRealTimers();
        }
    });

    it('lists paired browsers, keeps this one, and revokes another with the recorded request', async () => {
        const { fake } = systemFake();
        await waitFor(() => expect(screen.getByText('Paired browsers')).toBeTruthy());
        const rows = screen.getAllByRole('row').filter((r) => within(r).queryByRole('button', { name: 'Revoke' }));
        expect(rows.length).toBe(2);
        const mine = rows.find((r) => r.textContent?.includes('this browser'))!;
        expect((within(mine).getByRole('button', { name: 'Revoke' }) as HTMLButtonElement).disabled).toBe(true);
        const other = rows.find((r) => !r.textContent?.includes('this browser'))!;
        fake.answer('ui.revoke', 'ui.revoke/one');
        fireEvent.click(within(other).getByRole('button', { name: 'Revoke' }));
        await waitFor(() => expect(fake.sent('ui.revoke')).toEqual(fixtures['ui.revoke/one'].request));
        await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'ui.tokens').length).toBe(2));
    });

    it('lists the browsers used most recently first, dims the ones unused for a month, and revokes all but this one', async () => {
        // The recording was made at 09:00 UTC on 16 October; how long ago a browser was used is read off the clock.
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
        try {
            const { fake } = systemFake('with-ui', 'aged');
            await waitFor(() => expect(screen.getAllByRole('button', { name: 'Revoke' }).length).toBe(3));
            const rows = screen.getAllByRole('row').filter((r) => within(r).queryByRole('button', { name: 'Revoke' }));
            expect(rows.map((r) => r.textContent?.includes('this browser'))).toEqual([true, false, false]);
            expect(rows.map((r) => r.classList.contains('stale'))).toEqual([false, false, true]);
            expect(rows[2]!.textContent).toContain('not used for a month');

            fake.answer('ui.revoke', 'ui.revoke/stale', 'ui.revoke/never-used');
            fireEvent.click(screen.getByRole('button', { name: 'Revoke all other browsers' }));
            await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'ui.revoke')).toHaveLength(2));
            const others = fixtures['ui.tokens/aged'].response.items.map((t) => t.id).filter((id) => id !== 'uit_1');
            expect(fake.calls.filter((c) => c.operation === 'ui.revoke').map((c) => c.body)).toEqual(
                expect.arrayContaining(others.map((id) => ({ token_id: id }))),
            );
            await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'ui.tokens').length).toBe(2));
        } finally {
            vi.useRealTimers();
        }
    });

    it('says when no UI module is reported', async () => {
        systemFake('without-ui');
        await waitFor(() => expect(screen.getByText(/not reported/)).toBeTruthy());
    });
});

describe('System, the update sentence', () => {
    const update = {
        available: true,
        version: '9.9.9',
        checked_at: '2026-01-01T00:00:00Z',
        release_notes_url: null,
    };
    const recorded = fixtures['system.info/with-ui'].response as { update?: unknown };
    afterEach(() => {
        recorded.update = null;
        delete window.jasonDesktop;
    });

    it('in a browser names the terminal command, as before', async () => {
        recorded.update = update;
        systemFake();
        await waitFor(() => expect(screen.getByText(/Jason 9\.9\.9 is available/)).toBeTruthy());
        expect(screen.getByText(/jason update apply/)).toBeTruthy();
    });

    it("in the desktop names the desktop's own update and not the terminal command", async () => {
        recorded.update = update;
        window.jasonDesktop = {} as NonNullable<typeof window.jasonDesktop>;
        systemFake();
        await waitFor(() => expect(screen.getByText(/Jason 9\.9\.9 is available/)).toBeTruthy());
        expect(screen.getByText(/Check for Updates…/)).toBeTruthy();
        expect(screen.getByText(/updates itself and the jason it carries/)).toBeTruthy();
        expect(screen.queryByText(/jason update apply/)).toBeNull();
    });
});

describe('System, the standing choice of Full access', () => {
    afterEach(() => {
        delete window.jasonDesktop;
        fullAccessStanding.value = null;
    });

    it('in a browser shows no Full access card', async () => {
        systemFake();
        await waitFor(() => expect(screen.getByText('This UI')).toBeTruthy());
        expect(screen.queryByRole('heading', { name: 'Full access' })).toBeNull();
    });

    it('in the desktop says that Full access asks each time while no choice stands', async () => {
        window.jasonDesktop = stubDesktop();
        systemFake();
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Full access' })).toBeTruthy());
        expect(screen.getByText('Full access asks you each time a chat enters it.')).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Ask each time again' })).toBeNull();
    });

    it('in the desktop shows Always and new chats, and takes them back with one button', async () => {
        const desktop = stubDesktop();
        desktop.standing = { givenAt: '2026-11-11T09:00:00.000Z', newChats: true };
        window.jasonDesktop = desktop;
        systemFake();
        await waitFor(() =>
            expect(screen.getByText(/Chats on this computer enter Full access without asking, since /)).toBeTruthy(),
        );
        expect(screen.getByText('New chats start in Full access.')).toBeTruthy();
        fireEvent.click(screen.getByRole('button', { name: 'Ask each time again' }));
        await waitFor(() => expect(screen.getByText('Full access asks you each time a chat enters it.')).toBeTruthy());
        expect(desktop.calls.filter((each) => each.name === 'fullAccess.takeBack')).toHaveLength(1);
        expect(screen.queryByText('New chats start in Full access.')).toBeNull();
    });

    it('reads the choice again each time it shows, so one the desktop never said is shown', async () => {
        const desktop = stubDesktop();
        window.jasonDesktop = desktop;
        systemFake();
        await waitFor(() => expect(screen.getByText('Full access asks you each time a chat enters it.')).toBeTruthy());
        cleanup();
        desktop.standing = { givenAt: '2026-11-11T09:00:00.000Z', newChats: false };
        systemFake();
        await waitFor(() =>
            expect(screen.getByText(/Chats on this computer enter Full access without asking, since /)).toBeTruthy(),
        );
    });

    it('a moment that is no date is shown without it, and the screen still draws', async () => {
        const desktop = stubDesktop();
        desktop.standing = { givenAt: '2026-99-99T99:99:99Z', newChats: false };
        window.jasonDesktop = desktop;
        systemFake();
        await waitFor(() =>
            expect(screen.getByText('Chats on this computer enter Full access without asking.')).toBeTruthy(),
        );
        expect(screen.getByRole('button', { name: 'Ask each time again' })).toBeTruthy();
    });

    it('in a desktop of an earlier version, with no standing choice to read, shows no card', async () => {
        window.jasonDesktop = {} as NonNullable<typeof window.jasonDesktop>;
        systemFake();
        await waitFor(() => expect(screen.getByText('This UI')).toBeTruthy());
        expect(screen.queryByRole('heading', { name: 'Full access' })).toBeNull();
    });
});
