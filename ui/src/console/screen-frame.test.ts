import { readFileSync } from 'node:fs';
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
