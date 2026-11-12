import { signal } from '@preact/signals-react';

export type ThemeChoice = 'system' | 'light' | 'dark';

const key = 'jason.ui.theme';

function read(): ThemeChoice {
    try {
        const saved = localStorage.getItem(key);
        if (saved === 'light' || saved === 'dark' || saved === 'system') return saved;
    } catch {
        // No storage: the default below.
    }
    // A new installation meets the light theme; a choice already stored is kept as it is.
    return 'light';
}

export const themeChoice = signal<ThemeChoice>(typeof localStorage === 'undefined' ? 'light' : read());
export const resolvedTheme = signal<'light' | 'dark'>('light');

function systemIsDark(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
}

function apply(): void {
    resolvedTheme.value = themeChoice.value === 'system' ? (systemIsDark() ? 'dark' : 'light') : themeChoice.value;
    document.documentElement.dataset['theme'] = resolvedTheme.value;
}

/** Tells the desktop, when there is one, the choice to keep for its own window and starting page; nothing in a browser. */
let tell: ((choice: ThemeChoice) => void) | null = null;

export function bindDesktopTheme(send: ((choice: ThemeChoice) => Promise<void>) | null): void {
    tell = send === null ? null : (choice) => void send(choice).catch(() => undefined);
}

export function setTheme(choice: ThemeChoice): void {
    themeChoice.value = choice;
    tell?.(choice);
    try {
        localStorage.setItem(key, choice);
    } catch {
        // Then the choice lasts for this page load.
    }
    apply();
}

/** Applies the saved choice and follows the system while the choice is to. */
export function startTheme(): void {
    apply();
    tell?.(themeChoice.value);
    if (typeof matchMedia === 'function') matchMedia('(prefers-color-scheme: dark)').addEventListener('change', apply);
}
