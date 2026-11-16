import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// The rules of a narrow tab live under `@media`, which the screen tests' stylesheet reader leaves out; each is held here
// by the declarations its block carries. The run starts in ui/.

function block(file: string, media: string): string {
    const text = readFileSync(file, 'utf8');
    const found: string[] = [];
    for (let at = text.indexOf(`@media (${media}) {`); at >= 0; at = text.indexOf(`@media (${media}) {`, at + 1)) {
        let depth = 0;
        for (let i = text.indexOf('{', at); i < text.length; i++) {
            if (text[i] === '{') depth++;
            if (text[i] === '}' && --depth === 0) {
                found.push(text.slice(at, i + 1).replace(/\s+/g, ' '));
                break;
            }
        }
    }
    if (found.length === 0) throw new Error(`${file} has no @media (${media})`);
    return found.join(' ');
}
const has = (media: string, ...parts: string[]) => {
    const text = block('src/console/console.css', media);
    for (const part of parts) expect(text, part).toContain(part);
};

describe('the console in a narrow tab', () => {
    it('keeps the composer menus on the screen, at the box’s width and edges', () => {
        const text = block('src/console/composer.css', 'max-width: 820px');
        for (const part of [
            '.composer { position: relative; }',
            '.composer .cmenu { position: static; }',
            'left: 0; right: 0; width: auto; max-width: none;',
        ])
            expect(text, part).toContain(part);
    });
    it('scrolls a table in the box around it, and puts the closed sidebar out of reach', () => {
        has(
            'max-width: 820px',
            '.tscroll { max-width: 100%; overflow-x: auto; }',
            'visibility: hidden;',
            '.side.open { transform: none; visibility: visible;',
        );
    });
    it('lets the strip, a history row, the rename row and the start note take the lines they need', () => {
        has(
            'max-width: 520px',
            '.mode-strip { height: auto;',
            'white-space: normal;',
            '.row .what .in { flex: 1 1 100%; white-space: normal;',
        );
        has(
            'max-width: 820px',
            '.chat-bar { flex-wrap: wrap; }',
            '.chat-rename { flex: 1 1 100%; }',
            '.start .start-note.uinote { flex-wrap: wrap; }',
            '.harness .filters .search { flex: 1 1 100%;',
        );
    });
    it('keeps an age and a period label on one line', () => {
        expect(readFileSync('src/console/console.css', 'utf8')).toMatch(
            /\.dash-row \.meta \{\s*flex: none;\s*white-space: nowrap;/,
        );
        expect(readFileSync('src/styles/components.css', 'utf8')).toContain('.segmented button { white-space: nowrap;');
    });
});
