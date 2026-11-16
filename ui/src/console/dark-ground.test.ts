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

// The browser shell's screens sit inside the console's area, and the dark theme has one ground: the shell's `--bg`
// (tokens.css, not turned over again by dark.css) is the colour the console's own `--bg` is.
describe('one ground in the dark theme', () => {
    const strip = (text: string) => text.replace(/\r\n/g, '\n').replace(/[/][*][^]*?[*][/]/g, '');
    const darkSheet = strip(readFileSync('src/styles/dark.css', 'utf8'));
    const tokens = strip(readFileSync('src/styles/tokens.css', 'utf8'));
    const escaped = (selector: string) => selector.replace(/[[\]().']/g, (each) => `\\${each}`);
    const ground = (text: string, selector: string) =>
        new RegExp(`${escaped(selector)} [{][^}]*?\\n\\s*--bg: (#[0-9a-f]{6});`).exec(text)?.[1];

    it('leaves --bg to tokens.css in dark.css', () => {
        expect(darkSheet).not.toMatch(/--bg:/);
    });

    it('is the console ground in the shell tokens, with the console block of its own', () => {
        const consoleGround = ground(css, ":root[data-theme='dark'] .jc-console");
        expect(consoleGround).toBe('#0c0c0e');
        expect(ground(tokens, ':root[data-theme="dark"]')).toBe(consoleGround);
    });
});

// The dark theme's tokens are written twice, for the system's choice and for the person's, and say the same.
describe('the two dark token blocks', () => {
    const names = (selector: string): string[] => {
        const start = css.indexOf(`${selector} {`);
        expect(start, selector).toBeGreaterThanOrEqual(0);
        const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start));
        return [...body.matchAll(/(--[a-z0-9-]+):\s*([^;]+);/g)].map((each) => `${each[1]}: ${each[2]}`);
    };

    it('carry the same names and values', () => {
        const chosen = names(":root[data-theme='dark'] .jc-console");
        const system = names(":root:not([data-theme='light']) .jc-console");
        expect(system).toEqual(chosen);
    });

    it('give the fields a ground and an edge of their own, and the accent fills dark ink', () => {
        const chosen = names(":root[data-theme='dark'] .jc-console");
        expect(chosen).toContain('--field-bg: var(--bg)');
        expect(chosen).toContain('--field-line: #3a3a42');
        expect(chosen).toContain('--on-teal: #0c0c0e');
        expect(chosen).toContain('--card-line: var(--line)');
    });
});
