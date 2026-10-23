import { act, fireEvent, screen, waitFor, within } from '@testing-library/preact';
import { describe, expect, it, vi } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';

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

describe('System', () => {
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
