import { signal } from '@preact/signals-react';
import { en, name as english, type MessageKey } from '../locale/en';
import { uk, name as ukrainian } from '../locale/uk';

/** The languages offered, each named by its own file. */
export const locales = [
    { id: 'en', name: english },
    { id: 'uk', name: ukrainian },
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

/** The document says which language it is in, for a screen reader and for the browser's spelling and hyphenation. */
function apply(): void {
    if (typeof document !== 'undefined') document.documentElement.lang = locale.value;
}

/** Applies the saved choice to the document; each shell calls it when it mounts. */
export function startLocale(): void {
    apply();
}

export function setLocale(next: Locale): void {
    locale.value = next;
    apply();
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
