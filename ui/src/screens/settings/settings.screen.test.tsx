import { readFileSync } from 'node:fs';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { startLocale } from '../../platform/i18n';
import { setTheme } from '../../platform/theme';
import { route } from '../../router';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

afterEach(() => {
    setTheme('system');
});

describe('Settings', () => {
    it('draws no language choice, and reads none that this browser stored before', async () => {
        localStorage.setItem('jason.ui.locale', 'uk');
        try {
            const fake = new FakeApi();
            renderApp(fake);
            route.value = { screen: 'settings' };
            await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
            expect(screen.queryByRole('heading', { name: 'Language', level: 2 })).toBeNull();
            expect(screen.queryByRole('button', { name: 'English' })).toBeNull();
            expect(screen.getByRole('link', { name: 'Campaigns' })).toBeTruthy();
            startLocale();
            expect(document.documentElement.lang).toBe('en');
            expect(fake.calls.map((c) => c.operation)).toEqual(['system.info']);
        } finally {
            localStorage.removeItem('jason.ui.locale');
        }
    });

    it('marks the Appearance section with an icon, hidden from a screen reader', async () => {
        renderApp(new FakeApi());
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Appearance' }));
        const heading = screen.getByRole('heading', { name: 'Appearance', level: 2 });
        expect(heading.querySelector('svg.settings-icon[aria-hidden="true"]')).toBeTruthy();
    });

    it("sets each icon on the line of its heading's words, in the browser's own stylesheet", () => {
        // Read from the file: the test run hands a stylesheet import back empty. The run starts in ui/.
        const sheet = readFileSync('src/styles/app.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
        const rule = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g)).find(
            (each) => each[1]!.trim() === '.settings-icon',
        );
        expect(rule?.[2]).toMatch(/vertical-align:\s*-2px;/);
        expect(rule?.[2]).toMatch(/color:\s*var\(--accent-text\);/);
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
