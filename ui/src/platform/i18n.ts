import { signal } from '@preact/signals-react';
import { en, type MessageKey } from '../locale/en';
import { uk } from '../locale/uk';

export const locales = [
    { id: 'en', name: 'English' },
    { id: 'uk', name: 'Українська' },
] as const;

export type Locale = (typeof locales)[number]['id'];

const key = 'jason.ui.locale';
const tables: Readonly<Record<Locale, Readonly<Record<MessageKey, string>>>> = { en, uk };

function read(): Locale {
    try {
        const saved = localStorage.getItem(key);
        if (saved === 'en' || saved === 'uk') return saved;
    } catch {
        // No storage: the default below.
    }
    return 'en';
}

export const locale = signal<Locale>(typeof localStorage === 'undefined' ? 'en' : read());

export function setLocale(next: Locale): void {
    locale.value = next;
    try {
        localStorage.setItem(key, next);
    } catch {
        // Then the choice lasts for this page load.
    }
}

/** A word in the current language, falling back to English; `{name}` takes its parameter. */
export function t(message: MessageKey, parameters: Readonly<Record<string, string>> = {}): string {
    const text = tables[locale.value][message] || tables.en[message];
    return text.replace(/\{(\w+)\}/g, (whole, name: string) => parameters[name] ?? whole);
}
