import { describe, expect, it } from 'vitest';
import { scopePlugin, scopeSelector } from './scope';

describe('the shell stylesheet, scoped to the page panel', () => {
    it('makes the document selectors the scope, and keeps a state of the document on the document', () => {
        expect(scopeSelector(':root', '.harness')).toBe('.harness');
        expect(scopeSelector('html', '.harness')).toBe('.harness');
        expect(scopeSelector('body', '.harness')).toBe('.harness');
        expect(scopeSelector(":root[data-theme='dark']", '.harness')).toBe(":root[data-theme='dark'] .harness");
        expect(scopeSelector(':root:not([data-theme="light"]) .card', '.harness')).toBe(
            ':root:not([data-theme="light"]) .harness .card',
        );
        expect(scopeSelector('*::before', '.harness')).toBe('.harness *::before');
        expect(scopeSelector(' .app.chromeless > .content', '.harness')).toBe('.harness .app.chromeless > .content');
        expect(scopeSelector('html.desktop-mac', '.harness')).toBe('.harness html.desktop-mac');
    });

    it('scopes only the file it names, and leaves the steps of a keyframes alone', () => {
        const rules = [
            { selectors: [':root', '.btn'] },
            { selectors: ['to'], parent: { type: 'atrule', name: 'keyframes' } },
        ];
        const root = (file: string) => ({
            source: { input: { file } },
            walkRules: (visit: (rule: (typeof rules)[number]) => void) => rules.forEach(visit),
        });
        scopePlugin('console/harness.css', '.harness').OnceExit(root('C:\\ui\\src\\styles\\app.css'));
        expect(rules[0]!.selectors).toEqual([':root', '.btn']);
        scopePlugin('console/harness.css', '.harness').OnceExit(root('C:\\ui\\src\\console\\harness.css'));
        expect(rules.map((rule) => rule.selectors)).toEqual([['.harness', '.harness .btn'], ['to']]);
    });
});
