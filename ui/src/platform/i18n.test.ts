import { afterEach, describe, expect, it } from 'vitest';
import * as english from '../locale/en';
import { en } from '../locale/en';
import * as ukrainian from '../locale/uk';
import { uk } from '../locale/uk';
import { locale, locales, t } from './i18n';

afterEach(() => {
    locale.value = 'en';
});

describe('words', () => {
    it('follow the language', () => {
        expect(t('nav.campaigns')).toBe('Campaigns');
        locale.value = 'uk';
        expect(t('nav.campaigns')).toBe('Кампанії');
    });
    it('take parameters, and leave a parameter nobody gave as it is written', () => {
        expect(t('shell.version', { ui: 'v1', runtime: 'v2' })).toBe(
            'This UI was built for API v1; the runtime speaks v2. Reinstall the UI from a release that matches.',
        );
        expect(t('shell.version', { ui: 'v1' })).toContain('speaks {runtime}');
    });
    it('fall back to English where a translation is empty', () => {
        const table = uk as Record<string, string>;
        const kept = table['nav.chat'];
        table['nav.chat'] = '';
        try {
            locale.value = 'uk';
            expect(t('nav.chat')).toBe('Chat');
        } finally {
            if (kept === undefined) delete table['nav.chat'];
            else table['nav.chat'] = kept;
        }
    });
    it('are held in every language for every English key, and no other', () => {
        expect(Object.keys(uk).sort()).toEqual(Object.keys(en).sort());
        for (const [name, text] of Object.entries(uk)) expect(text, name).not.toBe('');
    });
    it('keep the same parameters in every language', () => {
        const parameters = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
        for (const name of Object.keys(en) as (keyof typeof en)[]) {
            expect(parameters(uk[name]), name).toEqual(parameters(en[name]));
        }
    });

    it('name each language in its own words, as its own file says them', () => {
        expect(locales).toEqual([
            { id: 'en', name: english.name },
            { id: 'uk', name: ukrainian.name },
        ]);
        expect(locales.every((each) => typeof each.name === 'string' && each.name.length > 0)).toBe(true);
    });
});
