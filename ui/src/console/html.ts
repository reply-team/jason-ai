// The console draws its panels from markup, and what it draws was written by agents and by prospects: a campaign's
// name, a journal line, a message, a field, an agent's words. So markup is made in one way only, the `html` tag below,
// which passes every value it interpolates through `esc` unless the value is markup the tag made itself; and it reaches
// the page in one way only, `paint`. The guard under tests/guards holds the rest of the console to that.

const entities: Readonly<Record<string, string>> = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
};

/** The one escaping function: text, made safe for an element's content and for a quoted attribute. */
export const esc = (text: string): string => text.replace(/[&<>"']/g, (char) => entities[char] ?? char);

/** Markup the `html` tag made. Nothing else can make one, so a value that is one was escaped where it was made. */
export class Html {
    private constructor(readonly markup: string) {}

    /** @internal Only the tag below calls this. */
    static made(markup: string): Html {
        return new Html(markup);
    }
}

export type Value = Html | string | number | boolean | null | undefined | readonly Value[];

function piece(value: Value): string {
    if (value instanceof Html) return value.markup;
    if (value === null || value === undefined || typeof value === 'boolean') return '';
    if (Array.isArray(value)) return (value as readonly Value[]).map(piece).join('');
    return esc(String(value));
}

/** Markup from a template: each interpolated value is escaped text, markup this tag made, a list of either, or nothing. */
export function html(strings: TemplateStringsArray, ...values: Value[]): Html {
    let markup = strings[0] ?? '';
    values.forEach((value, at) => {
        markup += piece(value) + (strings[at + 1] ?? '');
    });
    return Html.made(markup);
}

/** The values one after another, with `between` between them. */
export function join(values: readonly Value[], between: Html): Html {
    return Html.made(values.map(piece).join(between.markup));
}

export const markupOf = (value: Html): string => value.markup;

/** The one place the console writes markup into the page. */
export function paint(into: Element, value: Html): void {
    into.innerHTML = value.markup;
}

/** A value computed at run time, set through the CSSOM: never a style attribute in markup. */
export function setVariable(element: HTMLElement, name: `--${string}`, value: string): void {
    element.style.setProperty(name, value);
}
