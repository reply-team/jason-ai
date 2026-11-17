import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// The screen tests load no stylesheet, so what makes an optional reason one line at rest is read here, from the sheet.
// The run starts in ui/.

const strip = (text: string) => text.replace(/\r\n/g, '\n').replace(/[/][*][^]*?[*][/]/g, '');
const components = strip(readFileSync('src/styles/components.css', 'utf8'));

describe('an optional reason', () => {
    it('is one line high at rest, grows with what is typed to about six lines, then scrolls, with no handle', () => {
        const start = components.indexOf('\n.textarea.reason {');
        expect(start).toBeGreaterThanOrEqual(0);
        const rule = components.slice(start, components.indexOf('}', start));
        expect(rule).toContain('min-height: var(--control-h);');
        expect(rule).toContain('max-height: calc(6lh + 2 * var(--space-1) + 2px);');
        expect(rule).toContain('field-sizing: content;');
        expect(rule).toContain('overflow-y: auto;');
        expect(rule).toContain('resize: none;');
    });
});
