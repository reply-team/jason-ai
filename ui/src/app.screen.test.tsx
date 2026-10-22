import { screen, waitFor } from '@testing-library/preact';
import { describe, expect, it } from 'vitest';
import { FakeApi } from './test/fake-api';
import { renderApp } from './test/render-app';
import { session } from './api/session';

describe('the shell', () => {
    it('shows the sidebar with the screens of this build, in order, and who is paired', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/empty')
            .answer('decision.list', 'decision.list/empty');
        renderApp(fake);
        await waitFor(() => expect(screen.getByRole('navigation')).toBeTruthy());
        const links = screen.getAllByRole('link').map((link) => link.textContent?.trim());
        expect(links).toEqual(['Attention', 'Work items', 'People', 'System']);
        await waitFor(() => expect(screen.getByText(/ada · Jason/)).toBeTruthy());
    });

    it('shows pair-again when the runtime answers 401, and clears the stored pairing', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/unauthorized')
            .answer('decision.list', 'decision.list/empty');
        renderApp(fake);
        await waitFor(() => expect(screen.getByText(/isn't paired/)).toBeTruthy());
        expect(session.paired.value).toBe(false);
        expect(localStorage.getItem('jason.ui.session')).toBeNull();
    });

    it('shows pair-again with no pairing at all and no ticket in the link', async () => {
        const fake = new FakeApi();
        renderApp(fake, { paired: false });
        await waitFor(() => expect(screen.getByText(/jason ui open/)).toBeTruthy());
        expect(fake.calls).toEqual([]);
    });

    it('warns when the build was made for another API version', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/empty')
            .answer('decision.list', 'decision.list/empty');
        renderApp(fake, { apiVersion: 'v0' });
        await waitFor(() => expect(screen.getByText(/built for API v0/)).toBeTruthy());
    });
});
