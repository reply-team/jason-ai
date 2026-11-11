import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { route } from '../../router';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

afterEach(() => {
    delete window.jasonDesktop;
});

describe('the Chat screen in a browser', () => {
    it('says the chat is part of the desktop application and links to the releases, asking nothing of anyone', async () => {
        const fake = new FakeApi();
        let asked = 0;
        window.jasonDesktop = new Proxy({} as NonNullable<typeof window.jasonDesktop>, {
            get: () => {
                asked++;
                return undefined;
            },
        });
        renderApp(fake);
        route.value = { screen: 'chat' };
        await waitFor(() => screen.getByRole('heading', { name: 'Chat' }));
        expect(screen.getByText(/part of the desktop application/)).toBeTruthy();
        const link = screen.getByRole('link', { name: /Get Jason Desktop/ });
        expect(link.getAttribute('href')).toBe('https://github.com/reply-team/jason-ai/releases');
        expect(link.getAttribute('rel')).toBe('noopener noreferrer');
        await waitFor(() => expect(fake.calls.map((c) => c.operation)).toEqual(['system.info']));
        expect(asked).toBe(0);
    });

    it('is the Chat row of the navigation', async () => {
        renderApp(new FakeApi());
        route.value = { screen: 'chat' };
        await waitFor(() => screen.getByRole('heading', { name: 'Chat' }));
        expect(screen.getByRole('link', { name: 'Chat' }).getAttribute('href')).toBe('#/chat');
    });
});
