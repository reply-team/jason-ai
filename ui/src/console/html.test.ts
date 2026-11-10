import { describe, expect, it } from 'vitest';
import { esc, html, join, markupOf } from './html';

const hostile = '<img src=x onerror="alert(1)">';

describe('the console markup', () => {
    it('escapes every character that would end text or an attribute', () => {
        expect(esc(`a & b < c > d " e ' f`)).toBe('a &amp; b &lt; c &gt; d &quot; e &#39; f');
    });

    it('escapes whatever is interpolated as text, in an element and in an attribute', () => {
        const name = hostile;
        expect(markupOf(html`<b title="${name}">${name}</b>`)).toBe(
            '<b title="&lt;img src=x onerror=&quot;alert(1)&quot;&gt;">&lt;img src=x onerror=&quot;alert(1)&quot;&gt;</b>',
        );
    });

    it('keeps markup it made itself, and escapes the text inside it once', () => {
        const inner = html`<i>${'<x>'}</i>`;
        expect(markupOf(html`<p>${inner}</p>`)).toBe('<p><i>&lt;x&gt;</i></p>');
    });

    it('takes lists, numbers and nothing', () => {
        const rows = ['<a>', 'b'].map((each) => html`<li>${each}</li>`);
        // The exact markup is the point here, so the formatter leaves this template as written.
        // prettier-ignore
        const list = html`<ul>${rows}</ul>`;
        expect(markupOf(list)).toBe('<ul><li>&lt;a&gt;</li><li>b</li></ul>');
        expect(markupOf(html`${3}${null}${undefined}${false}${['<', html`<br />`]}`)).toBe('3&lt;<br />');
        expect(markupOf(join([html`<b>1</b>`, '<2>'], html`<hr />`))).toBe('<b>1</b><hr />&lt;2&gt;');
    });

    it('cannot be given markup as a plain string', () => {
        const forged = { markup: '<img src=x onerror=alert(1)>' };
        expect(markupOf(html`${forged as unknown as string}`)).toBe('[object Object]');
    });
});
