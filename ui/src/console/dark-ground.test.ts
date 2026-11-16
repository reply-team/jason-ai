import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// The console's shell screens (People, Accounts, Settings, an agent's page, Work items, System, Performance) sit on
// `.content`. Its light ground is a fixed colour, so in the dark theme those screens stayed light inside a dark console
// unless a rule gives the ground back to the theme's own `--bg`. The run starts in ui/.

const css = readFileSync('src/console/console.css', 'utf8')
    .replace(/\r\n/g, '\n')
    .replace(/[/][*][^]*?[*][/]/g, '');

describe('the ground of the console screens in the dark theme', () => {
    it('keeps the light ground as it was', () => {
        expect(css).toMatch(/\n[.]jc-console [.]content [{]\s*background: #f6f8fc;\s*[}]/);
    });

    it('takes the theme background when the theme is chosen dark', () => {
        expect(css).toMatch(
            /\n:root\[data-theme='dark'\] [.]jc-console [.]content [{]\s*background: var\(--bg\);\s*[}]/,
        );
    });

    it('takes the theme background when the system asks for dark and the theme is not chosen light', () => {
        const rule = css.search(
            /:root:not\(\[data-theme='light'\]\) [.]jc-console [.]content [{]\s*background: var\(--bg\);\s*[}]/,
        );
        expect(rule).toBeGreaterThanOrEqual(0);
        const opened = css.lastIndexOf('@media', rule);
        expect(css.slice(opened, css.indexOf('{', opened))).toContain('(prefers-color-scheme: dark)');
        expect(css.slice(opened, rule)).not.toMatch(/\n}\n/);
    });
});
