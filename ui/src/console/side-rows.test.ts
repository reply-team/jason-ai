import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// A sidebar row has three looks: at rest, under the pointer, and the one in front. The one in front is a darker fill
// than the pointer's, so a row under the pointer never looks like the row in front, and each theme gives all three.
// The run starts in ui/.

const css = readFileSync('src/console/console.css', 'utf8')
    .replace(/\r\n/g, '\n')
    .replace(/[/][*][^]*?[*][/]/g, '');

describe('the sidebar rows', () => {
    it('fills the row in front with its own token, held when the pointer is over it', () => {
        expect(css).toMatch(
            /[.]jc-console [.]item\[aria-current='true'\],\s*[.]jc-console [.]item\[aria-current='true']:hover [{]\s*background: var\(--sel\);\s*font-weight: 500;\s*[}]/,
        );
    });

    it('fills a row under the pointer with the lighter token', () => {
        expect(css).toMatch(/[.]jc-console [.]item:hover [{]\s*background: var\(--hover\);/);
    });

    it('defines the row-in-front token in the light theme and in both ways of choosing dark', () => {
        expect(css.match(/--sel: #[0-9a-f]{6};/g)).toHaveLength(3);
        expect(css).toMatch(/--hover: #e9edf4;\s*--sel: #dde3ee;/);
        expect(css.match(/--hover: #1f1f24;\s*--sel: #2b2b32;/g)).toHaveLength(2);
    });

    it('spaces the rows at 36 pixels from one to the next: eight of padding over a 20 pixel line', () => {
        expect(css).toMatch(/[.]jc-console [.]item [{][^}]*padding: 8px 10px;/);
    });
});
