import { en, type MessageKey } from '../locale/en';

/** The document says which language it is in, for a screen reader and for the browser's spelling and hyphenation. */
export function startLocale(): void {
    if (typeof document !== 'undefined') document.documentElement.lang = 'en';
}

/** A word of the shell, from the one table; `{name}` takes its parameter. */
export function t(message: MessageKey, parameters: Readonly<Record<string, string>> = {}): string {
    return en[message].replace(/\{(\w+)\}/g, (whole, name: string) => parameters[name] ?? whole);
}
