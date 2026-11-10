import { describe, expect, it } from 'vitest';
import { act } from './actions';
import { esc, html, join, markupOf } from './html';

/** The literal strings of a template, for a shape the formatter would rewrite. */
const literal = (...strings: string[]): TemplateStringsArray => Object.assign([...strings], { raw: [...strings] });

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

    it('refuses text where an attribute would stand, and says what to do instead', () => {
        const name = 'x data-action="chat.send"';
        expect(() => html`<b title=${name}>x</b>`).toThrow(/quote the attribute or make it with the html tag/);
        expect(() => html`<b ${name}>x</b>`).toThrow(/quote the attribute or make it with the html tag/);
        expect(() => html(literal('<b\n    ', '\n>x</b>'), name)).toThrow(/quote the attribute/);
        expect(() => html`<b ${3}>x</b>`).toThrow(/quote the attribute or make it with the html tag/);
        expect(() => html`<b ${[name]}>x</b>`).toThrow(/quote the attribute or make it with the html tag/);
    });

    it('takes markup it made, nothing, or a list of such where an attribute would stand, and text once quoted', () => {
        expect(markupOf(html`<button ${act('chat.send', 'c1')}>x</button>`)).toBe(
            '<button data-action="chat.send" data-args="[&quot;c1&quot;]">x</button>',
        );
        expect(markupOf(html`<button ${html`disabled`}>x</button>`)).toBe('<button disabled>x</button>');
        expect(markupOf(html`<button ${null}>x</button>`)).toBe('<button >x</button>');
        expect(markupOf(html`<button ${[html`disabled`, null]}>x</button>`)).toBe('<button disabled>x</button>');
        expect(markupOf(html`<b title="${'a > b'}">${'c > d'}</b>`)).toBe('<b title="a &gt; b">c &gt; d</b>');
        expect(markupOf(html`<b title="${"it's"}">x</b><i>${'y'}</i>`)).toBe('<b title="it&#39;s">x</b><i>y</i>');
        // Single quotes, which the formatter would turn into double ones in a template.
        expect(markupOf(html(literal("<b title='", " > '>", '</b>'), 'a', 'b'))).toBe("<b title='a > '>b</b>");
        // A ">" inside the quotes does not end the tag: the value after them still stands where an attribute would.
        expect(() => html(literal("<b title='", " > ' ", '>x</b>'), 'a', 'c')).toThrow(/quote the attribute/);
    });
});
