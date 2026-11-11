import { describe, expect, it } from 'vitest';
import { en } from '../locale/en';
import { t } from './i18n';

describe('words', () => {
    it('are the English table', () => {
        expect(t('nav.campaigns')).toBe('Campaigns');
        expect(t('settings.title')).toBe(en['settings.title']);
    });
    it('take parameters, and leave a parameter nobody gave as it is written', () => {
        expect(t('shell.version', { ui: 'v1', runtime: 'v2' })).toBe(
            'This UI was built for API v1; the runtime speaks v2. Reinstall the UI from a release that matches.',
        );
        expect(t('shell.version', { ui: 'v1' })).toContain('speaks {runtime}');
    });
    it('hold no empty text', () => {
        for (const [name, text] of Object.entries(en)) expect(text, name).not.toBe('');
    });
});
