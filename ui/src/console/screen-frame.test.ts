import { readFileSync, readdirSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// The frame of the browser shell's screens inside the console's area: a screen that shows no detail draws no rule at
// its right edge, and a view painted inside a card of a screen lets the card close under it. The run starts in ui/.

const strip = (text: string) => text.replace(/\r\n/g, '\n').replace(/[/][*][^]*?[*][/]/g, '');
const components = strip(readFileSync('src/styles/components.css', 'utf8'));
const console_ = strip(readFileSync('src/console/console.css', 'utf8'));

describe('the frame of a screen', () => {
    it('draws no rule at the right edge of a list that has no detail beside it', () => {
        expect(components).toMatch(/\n[.]main[.]single > [.]list [{] border-right: 0; [}]/);
        expect(components).toMatch(/\n[.]list [{] border-right: 1px solid var\(--border\);/);
    });

    it('lets the card of a screen close under a view painted inside it', () => {
        expect(console_).toMatch(
            /\n[.]jc-console [.]harness [.]sys > [.]card > [.]painted [{]\s*height: auto;\s*overflow: visible;\s*[}]/,
        );
    });
});

describe('the agent page in a narrow window', () => {
    it('stacks a send-mode row, label then choice then Apply, instead of squeezing the label', () => {
        const agent = strip(readFileSync(['src/console/', 'agent', '.css'].join(''), 'utf8'));
        const narrow = agent.slice(agent.indexOf('@media (max-width: 820px)'));
        expect(narrow).toMatch(/[.]send-mode-row [{]\s*grid-template-columns: minmax\(0, 1fr\);\s*[}]/);
    });
});

describe('a dialog in the dark theme', () => {
    it('has an edge that shows and a layer behind it that dims, chosen or by the system', () => {
        for (const lead of [":root[data-theme='dark']", ":root:not([data-theme='light'])"]) {
            expect(console_).toContain(`${lead} .jc-console .overlay {`);
            expect(console_).toContain(`${lead} .jc-console .dialog {`);
        }
        expect(console_).toContain('background: rgba(0, 0, 0, 0.62);');
    });
});

describe('a dialog’s layer', () => {
    it('stands over every layer of the page and under the search palette alone', () => {
        // Every z-index of the page's stylesheets, by the rule that sets it.
        const layers = readdirSync('src', { recursive: true, encoding: 'utf8' })
            .filter((name) => name.endsWith('.css'))
            .flatMap((name) =>
                [...strip(readFileSync(`src/${name}`, 'utf8')).matchAll(/([^{}]+)[{]([^{}]*)[}]/g)].flatMap(
                    ([, head, body]) => {
                        const z = /(?:^|;|\s)z-index:\s*(-?\d+)/.exec(body ?? '')?.[1];
                        return z === undefined
                            ? []
                            : [{ selector: (head ?? '').trim().replace(/\s+/g, ' '), z: Number(z) }];
                    },
                ),
            );
        const overlay = layers.filter((each) => each.selector === '.overlay');
        expect(overlay).toEqual([{ selector: '.overlay', z: 50 }]);
        const above = layers.filter((each) => each.z >= 50 && each.selector !== '.overlay');
        expect(above).toEqual([{ selector: '.jc-console .palette-away', z: 60 }]);
        // The layers it must beat are read, each below it: the menus and the narrow sidebar among them.
        const below = new Map(layers.filter((each) => each.z < 50).map((each) => [each.selector, each.z]));
        for (const selector of ['.jc-console .menu', '.picker .menu', '.jc-console .side', '.jc-console .side-away'])
            expect(below.has(selector), selector).toBe(true);
    });
});
