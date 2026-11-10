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
    return 'system';
}

export const themeChoice = signal<ThemeChoice>(typeof localStorage === 'undefined' ? 'system' : read());
export const resolvedTheme = signal<'light' | 'dark'>('light');

function systemIsDark(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
}

function apply(): void {
    resolvedTheme.value = themeChoice.value === 'system' ? (systemIsDark() ? 'dark' : 'light') : themeChoice.value;
    document.documentElement.dataset['theme'] = resolvedTheme.value;
}

export function setTheme(choice: ThemeChoice): void {
    themeChoice.value = choice;
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
    if (typeof matchMedia === 'function') matchMedia('(prefers-color-scheme: dark)').addEventListener('change', apply);
}
