import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The theme: Light for a new viewer only, an existing one that never chose keeps the system, a stored choice is kept, the
// system is followed live, and in the desktop the desktop's kept choice is the truth and a desktop that cannot be told is
// no reason for the page not to open.

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

// The modules are loaded afresh for each test, so the place the page runs in is named afresh too: the desktop's own window.
const load = async () => {
    (await import('./places')).setPlace('window');
    return import('./theme');
};
const shown = () => document.documentElement.dataset['theme'];

describe('the theme in a browser', () => {
    it('is Light for a new viewer, even where the system is dark, and stores it', async () => {
        systemDark = true;
        const theme = await load();
        theme.startTheme();
        expect(theme.themeChoice.value).toBe('light');
        expect(shown()).toBe('light');
        expect(localStorage.getItem('jason.ui.theme')).toBe('light');
    });

    it('keeps following the system for an existing viewer that never chose', async () => {
        localStorage.setItem('jason.ui.session', '{}');
        systemDark = true;
        const theme = await load();
        theme.startTheme();
        expect(theme.themeChoice.value).toBe('system');
        expect(shown()).toBe('dark');
    });

    it('does not turn a new viewer into an existing one by the pairing it makes next', async () => {
        const first = await load();
        first.startTheme();
        localStorage.setItem('jason.ui.session', '{}');
        vi.resetModules();
        systemDark = true;
        const again = await load();
        again.startTheme();
        expect(shown()).toBe('light');
    });

    it('keeps a stored choice, system and dark included', async () => {
        localStorage.setItem('jason.ui.theme', 'system');
        systemDark = true;
        let theme = await load();
        theme.startTheme();
        expect(shown()).toBe('dark');
        vi.resetModules();
        localStorage.setItem('jason.ui.theme', 'dark');
        systemDark = false;
        theme = await load();
        theme.startTheme();
        expect(shown()).toBe('dark');
    });

    it('takes a choice at once on every surface, and keeps it for this viewer', async () => {
        const theme = await load();
        theme.startTheme();
        theme.setTheme('dark');
        expect(shown()).toBe('dark');
        expect(localStorage.getItem('jason.ui.theme')).toBe('dark');
        theme.setTheme('light');
        expect(shown()).toBe('light');
    });

    it('follows a change of the system setting without a restart while the choice is the system', async () => {
        localStorage.setItem('jason.ui.theme', 'system');
        const theme = await load();
        theme.startTheme();
        expect(shown()).toBe('light');
        systemDark = true;
        listeners.forEach((each) => each());
        expect(shown()).toBe('dark');
        theme.setTheme('light');
        systemDark = false;
        listeners.forEach((each) => each());
        expect(theme.resolvedTheme.value).toBe('light');
    });
});

describe('the theme in the desktop', () => {
    it('takes the desktop kept choice over the page own storage, which a moved port leaves empty', async () => {
        const theme = await load();
        await theme.bindDesktopTheme({ get: () => Promise.resolve('dark'), set: () => Promise.resolve() });
        theme.startTheme();
        expect(shown()).toBe('dark');
        expect(localStorage.getItem('jason.ui.theme')).toBe('dark');
    });

    it('tells the desktop a choice the page stored when the desktop holds none, and never a default it did not read', async () => {
        const told: string[] = [];
        const set = (choice: string) => {
            told.push(choice);
            return Promise.resolve();
        };
        localStorage.setItem('jason.ui.theme', 'dark');
        let theme = await load();
        await theme.bindDesktopTheme({ get: () => Promise.resolve(null), set });
        expect(told).toEqual(['dark']);
        vi.resetModules();
        localStorage.clear();
        told.length = 0;
        localStorage.setItem('jason.ui.session', '{}');
        theme = await load();
        await theme.bindDesktopTheme({ get: () => Promise.resolve(null), set });
        expect(told).toEqual([]);
    });

    it('takes the system, stores nothing and tells nothing when the desktop holds none and the page storage is empty', async () => {
        const told: string[] = [];
        systemDark = true;
        const theme = await load();
        await theme.bindDesktopTheme({
            get: () => Promise.resolve(null),
            set: (choice) => {
                told.push(choice);
                return Promise.resolve();
            },
        });
        theme.startTheme();
        expect(theme.themeChoice.value).toBe('system');
        expect(shown()).toBe('dark');
        expect(told).toEqual([]);
        expect(localStorage.getItem('jason.ui.theme')).toBeNull();
    });

    it('is Light and tells nothing when the desktop answers Light, a new installation that it wrote Light for', async () => {
        const told: string[] = [];
        systemDark = true;
        const theme = await load();
        await theme.bindDesktopTheme({
            get: () => Promise.resolve('light'),
            set: (choice) => {
                told.push(choice);
                return Promise.resolve();
            },
        });
        theme.startTheme();
        expect(shown()).toBe('light');
        expect(told).toEqual([]);
    });

    it('tells the desktop each later choice', async () => {
        const told: string[] = [];
        const theme = await load();
        await theme.bindDesktopTheme({
            get: () => Promise.resolve('light'),
            set: (choice) => {
                told.push(choice);
                return Promise.resolve();
            },
        });
        theme.setTheme('system');
        expect(told).toEqual(['system']);
    });

    it('opens when the desktop has no theme member at all, a desktop older than the page', async () => {
        const theme = await load();
        await expect(theme.bindDesktopTheme(undefined)).resolves.toBeUndefined();
        theme.setTheme('dark');
        expect(shown()).toBe('dark');
    });

    it('opens when the desktop throws or refuses', async () => {
        const theme = await load();
        await expect(
            theme.bindDesktopTheme({
                get: () => {
                    throw new Error('no member');
                },
                set: () => {
                    throw new Error('no member');
                },
            }),
        ).resolves.toBeUndefined();
        theme.setTheme('dark');
        expect(shown()).toBe('dark');
    });
});
