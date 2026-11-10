import { describe, expect, it } from 'vitest';
import { markupOf } from './html';
import { markdownView } from './markdown-view';

/** The markup, without the line breaks and indentation the source puts between two tags. */
const drawn = (source: string) => markupOf(markdownView(source)).replace(/>\s*\n\s*</g, '><');

describe("the chat's Markdown, drawn", () => {
    it('draws each element of the subset as its own element', () => {
        expect(
            drawn('# Plan\n\nA *b* **c** `d`\n\n- e\n\n2. f\n\n> g\n\n```\nh\n```\n\n| i | j |\n|---|---|\n| k | l |'),
        ).toBe(
            '<div class="chat-md">' +
                '<h1>Plan</h1>' +
                '<p>A <em>b</em> <strong>c</strong> <code>d</code></p>' +
                '<ul><li><p>e</p></li></ul>' +
                '<ol start="2"><li><p>f</p></li></ol>' +
                '<blockquote><p>g</p></blockquote>' +
                '<pre><code>h</code></pre>' +
                '<div class="chat-md-table"><table><thead><tr><th>i</th><th>j</th></tr></thead>' +
                '<tbody><tr><td>k</td><td>l</td></tr></tbody></table></div>' +
                '</div>',
        );
    });

    it('draws a link as its text and its address, as text, and an address alone once', () => {
        expect(drawn('[guide](https://example.test/g) and [https://example.test](https://example.test)')).toBe(
            '<div class="chat-md"><p><span class="chat-md-link">guide (https://example.test/g)</span> and ' +
                '<span class="chat-md-link">https://example.test</span></p></div>',
        );
    });

    it('escapes every text it draws: raw HTML, a script, a javascript link, a code block and a cell', () => {
        const markup = drawn(
            '<script>alert(1)</script>\n\n[x](javascript:alert`1`)\n\n```\n<img src=x onerror=alert(1)>\n```\n\n| <b>a</b> |\n|---|\n| "q" \'r\' & |',
        );
        expect(markup).not.toMatch(/<(script|img|a|b)[\s>]/);
        expect(markup).not.toContain('href');
        expect(markup).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
        expect(markup).toContain('x (javascript:alert`1`)');
        expect(markup).toContain('&lt;img src=x onerror=alert(1)&gt;');
        expect(markup).toContain('<th>&lt;b&gt;a&lt;/b&gt;</th>');
        expect(markup).toContain('<td>&quot;q&quot; &#39;r&#39; &amp;</td>');
    });

    it('never puts the agent text in an attribute: only the number a list starts at stands in one', () => {
        const hostile = [
            '```js" onload="x',
            'code',
            '```',
            '',
            '7. "><img src=x onerror=alert(1)>',
            '',
            '> ```',
            '> <iframe src=x></iframe>',
            '> ```',
            '',
            '| <a href=x>cell</a> |',
            '|---|',
            '| [x](javascript:"onmouseover="y) |',
        ].join('\n');
        expect(() => markdownView(hostile)).not.toThrow();
        const attributes = [...drawn(hostile).matchAll(/\s([a-z-]+)="([^"]*)"/g)].map(
            (each) => `${each[1]}=${each[2]}`,
        );
        expect(attributes).toEqual(['class=chat-md', 'start=7', 'class=chat-md-table', 'class=chat-md-link']);
    });
});
