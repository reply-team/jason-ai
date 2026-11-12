import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The theme: light for a new installation, a stored choice kept, the system's followed live, and the desktop told.

type Listener = () => void;
let systemDark = false;
let listeners: Listener[] = [];

beforeEach(() => {
    vi.resetModules();
    localStorage.clear();
    delete document.documentElement.dataset['theme'];
    systemDark = false;
    listeners = [];
    vi.stubGlobal('matchMedia', () => ({
        get matches() {
            return systemDark;
        },
        addEventListener: (_type: string, listener: Listener) => listeners.push(listener),
    }));
});

afterEach(() => vi.unstubAllGlobals());

const load = () => import('./theme');

describe('the theme', () => {
    it('is light for an installation that has chosen nothing, even where the system is dark', async () => {
        systemDark = true;
        const theme = await load();
        theme.startTheme();
        expect(theme.themeChoice.value).toBe('light');
        expect(document.documentElement.dataset['theme']).toBe('light');
    });

    it('keeps a choice already stored, system included', async () => {
        localStorage.setItem('jason.ui.theme', 'system');
        systemDark = true;
        const theme = await load();
        theme.startTheme();
        expect(theme.themeChoice.value).toBe('system');
        expect(document.documentElement.dataset['theme']).toBe('dark');
    });

    it('keeps a stored dark', async () => {
        localStorage.setItem('jason.ui.theme', 'dark');
        const theme = await load();
        theme.startTheme();
        expect(document.documentElement.dataset['theme']).toBe('dark');
    });

    it('takes a choice at once on every surface, and keeps it for this viewer', async () => {
        const theme = await load();
        theme.startTheme();
        theme.setTheme('dark');
        expect(document.documentElement.dataset['theme']).toBe('dark');
        expect(localStorage.getItem('jason.ui.theme')).toBe('dark');
        theme.setTheme('light');
        expect(document.documentElement.dataset['theme']).toBe('light');
    });

    it('follows a change of the system setting without a restart while the choice is the system', async () => {
        localStorage.setItem('jason.ui.theme', 'system');
        const theme = await load();
        theme.startTheme();
        expect(document.documentElement.dataset['theme']).toBe('light');
        systemDark = true;
        listeners.forEach((each) => each());
        expect(document.documentElement.dataset['theme']).toBe('dark');
        theme.setTheme('light');
        systemDark = false;
        listeners.forEach((each) => each());
        expect(theme.resolvedTheme.value).toBe('light');
    });

    it('tells the desktop the choice as the page starts and each time it changes, and a browser nothing', async () => {
        const theme = await load();
        theme.startTheme();
        theme.setTheme('dark');
        const told: string[] = [];
        theme.bindDesktopTheme((choice) => {
            told.push(choice);
            return Promise.resolve();
        });
        theme.startTheme();
        theme.setTheme('system');
        expect(told).toEqual(['dark', 'system']);
    });

    it('goes on when the desktop refuses the choice', async () => {
        const theme = await load();
        theme.bindDesktopTheme(() => Promise.reject(new Error('refused')));
        theme.setTheme('dark');
        await Promise.resolve();
        expect(document.documentElement.dataset['theme']).toBe('dark');
    });
});
