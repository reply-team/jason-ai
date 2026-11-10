import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setLocale } from '../../platform/i18n';
import { setTheme } from '../../platform/theme';
import { route } from '../../router';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

afterEach(() => {
    setLocale('en');
    setTheme('system');
});

describe('Settings', () => {
    it('changes the language of the shell and of the document, and keeps it in this browser, asking the runtime nothing', async () => {
        const fake = new FakeApi();
        renderApp(fake);
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        fireEvent.click(screen.getByRole('button', { name: 'Українська' }));
        await waitFor(() => screen.getByRole('heading', { name: 'Налаштування' }));
        expect(screen.getByRole('link', { name: 'Кампанії' })).toBeTruthy();
        expect(localStorage.getItem('jason.ui.locale')).toBe('uk');
        expect(document.documentElement.lang).toBe('uk');
        fireEvent.click(screen.getByRole('button', { name: 'English' }));
        expect(document.documentElement.lang).toBe('en');
        expect(fake.calls.map((c) => c.operation)).toEqual(['system.info']);
    });

    it('sets the theme on the document', async () => {
        renderApp(new FakeApi());
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        fireEvent.click(screen.getByRole('button', { name: 'Dark' }));
        expect(document.documentElement.dataset['theme']).toBe('dark');
        expect(screen.getByRole('button', { name: 'Dark' }).getAttribute('aria-pressed')).toBe('true');
    });
});
