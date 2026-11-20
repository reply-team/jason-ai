import { describe, expect, it } from 'vitest';
import { markupOf } from './html';
import { copied, markdownView } from './markdown-view';

/** A tag's attributes one space apart, however the source broke them over lines. */
const tagsTidied = (markup: string) =>
    markup.replace(/<[^>]+>/g, (tag) => tag.replace(/\s+/g, ' ').replace(/ (\/?)>$/, '$1>'));

/** The markup, without the line breaks and indentation the source puts between two tags, and each icon a bare tag. */
const drawn = (source: string) =>
    tagsTidied(markupOf(markdownView(source)))
        .replace(/>\s*\n\s*</g, '><')
        .replace(/<svg[\s\S]*?<\/svg>/g, '<svg>');

/** A code block's button as drawn, from its opening tag to its closing one. */
function copyButton(): string {
    const markup = tagsTidied(markupOf(markdownView('```\na\n```')));
    return markup.slice(markup.indexOf('<button'), markup.indexOf('</button>'));
}

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
                '<div class="chat-md-code"><button type="button" class="chat-md-copy" aria-label="Copy code" title="Copy code" data-action="chat.copyCode" data-args="[&quot;:0&quot;]"><svg></button>' +
                '<pre><code>h</code></pre></div>' +
                '<div class="chat-md-table"><table><thead><tr><th>i</th><th>j</th></tr></thead>' +
                '<tbody><tr><td>k</td><td>l</td></tr></tbody></table></div>' +
                '</div>',
        );
    });

    it('gives a code block a Copy code button and inline code none', () => {
        expect(drawn('`a`')).not.toContain('button');
        expect(drawn('```\na\n```').match(/<button/g)).toHaveLength(1);
    });

    it('draws Copy code as an icon named by its words: the copy mark, a check once copied, a cross when it failed', () => {
        const marks = (button: string) => ({
            tick: button.includes('class="chat-md-tick"'),
            cross: button.includes('class="chat-md-cross"'),
        });
        try {
            const idle = copyButton();
            expect(idle).toContain('aria-label="Copy code" title="Copy code"');
            expect(idle).toContain('aria-hidden="true"');
            expect(idle.replace(/<[^>]*>/g, '').trim(), 'no words of its own, only the icon').toBe('');
            expect(marks(idle)).toEqual({ tick: false, cross: false });
            copied.value = { key: ':0', outcome: 'copied' };
            const done = copyButton();
            expect(done).toContain('aria-label="Copied" title="Copied"');
            expect(marks(done)).toEqual({ tick: true, cross: false });
            copied.value = { key: ':0', outcome: 'refused' };
            const failed = copyButton();
            expect(failed).toContain('aria-label="Not copied" title="Not copied"');
            expect(marks(failed)).toEqual({ tick: false, cross: true });
        } finally {
            copied.value = null;
        }
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
        const copy = (n: number) => [
            'class=chat-md-code',
            'type=button',
            'class=chat-md-copy',
            'aria-label=Copy code',
            'title=Copy code',
            'data-action=chat.copyCode',
            `data-args=[&quot;:${n}&quot;]`,
        ];
        expect(attributes).toEqual([
            'class=chat-md',
            ...copy(0),
            'start=7',
            ...copy(1),
            'class=chat-md-table',
            'class=chat-md-link',
        ]);
    });
});
