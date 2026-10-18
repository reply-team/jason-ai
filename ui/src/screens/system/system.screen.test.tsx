import { fireEvent, screen, waitFor, within } from '@testing-library/preact';
import { describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';

function systemFake(ui: 'with-ui' | 'without-ui' = 'with-ui') {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two');
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

    it('says when no UI module is reported', async () => {
        systemFake('without-ui');
        await waitFor(() => expect(screen.getByText(/not reported/)).toBeTruthy());
    });
});
