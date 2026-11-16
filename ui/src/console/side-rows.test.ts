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
        expect(css).toMatch(/--hover: #e9edf4;\s*--sel: #e3e7ef;/);
        expect(css.match(/--hover: #1f1f24;\s*--sel: #28282f;/g)).toHaveLength(2);
    });

    it('spaces the rows at 36 pixels from one to the next, each box about 35.5 high', () => {
        expect(css).toMatch(/[.]jc-console [.]item [{][^}]*padding: 7[.]6px 10px;\s*margin-bottom: 0[.]5px;/);
    });
});
