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

/** Where the literal text leaves a value: between elements, inside a tag, or inside a quoted attribute. */
type Position = { in: 'text' } | { in: 'tag' } | { in: 'quoted'; quote: '"' | "'" };

/** The position after `literal`, read from `from`. Only the template's literal strings move it, never a value. */
function after(literal: string, from: Position): Position {
    let at = from;
    for (const char of literal) {
        if (at.in === 'text') {
            if (char === '<') at = { in: 'tag' };
        } else if (at.in === 'tag') {
            if (char === '>') at = { in: 'text' };
            else if (char === '"' || char === "'") at = { in: 'quoted', quote: char };
        } else if (char === at.quote) {
            at = { in: 'tag' };
        }
    }
    return at;
}

/** Whether a value may stand inside a tag: markup this tag made, nothing, or a list of such. */
function attributeLike(value: Value): boolean {
    if (value instanceof Html || value === null || value === undefined || typeof value === 'boolean') return true;
    return Array.isArray(value) && (value as readonly Value[]).every(attributeLike);
}

/**
 * Markup from a template: each interpolated value is escaped text, markup this tag made, a list of either, or nothing.
 * Escaping keeps text from ending an element or a quoted attribute, but text inside a tag, `<b ${x}>` or an unquoted
 * `title=${x}`, could still add an attribute of its own; so there the tag takes only markup it made, or nothing.
 */
export function html(strings: TemplateStringsArray, ...values: Value[]): Html {
    let markup = strings[0] ?? '';
    let position = after(markup, { in: 'text' });
    values.forEach((value, at) => {
        if (position.in === 'tag' && !attributeLike(value))
            throw new TypeError(
                `A value inside a tag must be markup: quote the attribute or make it with the html tag (after "${strings[at]}").`,
            );
        const literal = strings[at + 1] ?? '';
        markup += piece(value) + literal;
        position = after(literal, position);
    });
    return Html.made(markup);
}

/** The values one after another, with `between` between them. */
export function join(values: readonly Value[], between: Html): Html {
    return Html.made(values.map(piece).join(between.markup));
}

export const markupOf = (value: Html): string => value.markup;

/** The attributes that name what a control does; with its arguments, one of them says which control it is. */
const naming = ['data-input', 'data-enter', 'data-change', 'data-action'] as const;

type Identity = { attribute: string; name: string; args: string | null } | { id: string };
type Selection = { start: number; end: number; direction: 'forward' | 'backward' | 'none' };

function identify(element: Element): Identity | null {
    for (const attribute of naming) {
        const name = element.getAttribute(attribute);
        if (name !== null) return { attribute, name, args: element.getAttribute('data-args') };
    }
    return element.id ? { id: element.id } : null;
}

function same(element: Element, identity: Identity): boolean {
    if ('id' in identity) return element.id === identity.id;
    return (
        element.getAttribute(identity.attribute) === identity.name &&
        element.getAttribute('data-args') === identity.args
    );
}

/** The selection of a text field; null for a control that has none (a button, a checkbox). */
function selectionOf(element: Element): Selection | null {
    if (!(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)) return null;
    try {
        const { selectionStart: start, selectionEnd: end, selectionDirection: direction } = element;
        return start === null || end === null ? null : { start, end, direction: direction ?? 'none' };
    } catch {
        return null;
    }
}

/**
 * The one place the console writes markup into the page. A panel is painted again while a person works in it (the
 * clock, a journal line), and the new markup replaces the control they were in; so the control of the same identity is
 * focused again afterwards, with the same selection, and typing goes on where it was.
 */
export function paint(into: Element, value: Html): void {
    const active = into.ownerDocument.activeElement;
    const identity = active && into.contains(active) ? identify(active) : null;
    const selection = identity && active ? selectionOf(active) : null;
    into.innerHTML = value.markup;
    if (!identity) return;
    const again = Array.from(into.querySelectorAll('*')).find((element) => same(element, identity));
    if (!(again instanceof HTMLElement)) return;
    again.focus({ preventScroll: true });
    if (selection && (again instanceof HTMLInputElement || again instanceof HTMLTextAreaElement)) {
        try {
            again.setSelectionRange(selection.start, selection.end, selection.direction);
        } catch {
            // A field of a kind that takes no selection keeps its focus alone.
        }
    }
}

/** A value computed at run time, set through the CSSOM: never a style attribute in markup. */
export function setVariable(element: HTMLElement, name: `--${string}`, value: string): void {
    element.style.setProperty(name, value);
}
