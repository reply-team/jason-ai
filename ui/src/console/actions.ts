import { html, type Html } from './html';

// The runtime's CSP allows no inline handler, so the console's markup names what a control does instead:
// `data-action` for a click, `data-submit` on a form, `data-change` and `data-input` on a field, and `data-enter` on a
// text box that sends with Enter. Each names an entry of one closed table of the console's own functions, and its
// arguments travel in `data-args` as a JSON literal, parsed and never evaluated.

/** A value an action takes: JSON, so that it survives the attribute it travels in. */
export type Argument = string | number | boolean | null;

export type Action = (args: readonly unknown[], element: HTMLElement, event: Event) => void;

const table = new Map<string, Action>();

/** Adds the console's own function under its name; a name is defined once. */
export function define(name: string, action: Action): void {
    if (table.has(name)) throw new Error(`The console action ${name} is defined twice.`);
    table.set(name, action);
}

/** The attributes that make a control run the action `name` with `args` when it is clicked. */
export const act = (name: string, ...args: Argument[]): Html =>
    args.length === 0 ? html`data-action="${name}"` : html`data-action="${name}" data-args="${JSON.stringify(args)}"`;

/** The attribute that hands an action's arguments to a form or a field whose action is named by another attribute. */
export const argsOf = (...args: Argument[]): Html => html`data-args="${JSON.stringify(args)}"`;

/** The arguments in `data-args`: a JSON array, or none at all when the attribute is missing or is anything else. */
export function parseArgs(text: string | undefined): readonly unknown[] {
    if (text === undefined) return [];
    try {
        const parsed: unknown = JSON.parse(text);
        return Array.isArray(parsed) ? parsed : [];
    } catch {
        return [];
    }
}

function run(name: string | undefined, element: HTMLElement, event: Event): boolean {
    const action = name === undefined ? undefined : table.get(name);
    if (!action) return false;
    action(parseArgs(element.dataset['args']), element, event);
    return true;
}

function closest(event: Event, attribute: string, root: HTMLElement): HTMLElement | null {
    const target = event.target;
    if (!(target instanceof Element)) return null;
    const found = target.closest<HTMLElement>(`[${attribute}]`);
    return found && root.contains(found) ? found : null;
}

/** One listener per kind of event, on the console's root, for every control inside it. The function it returns stops them. */
export function listen(root: HTMLElement): () => void {
    const click = (event: Event) => {
        const element = closest(event, 'data-action', root);
        if (element && run(element.dataset['action'], element, event)) event.preventDefault();
    };
    const submit = (event: Event) => {
        const element = closest(event, 'data-submit', root);
        if (!element) return;
        event.preventDefault();
        run(element.dataset['submit'], element, event);
    };
    const change = (event: Event) => {
        const element = closest(event, 'data-change', root);
        if (element) run(element.dataset['change'], element, event);
    };
    const input = (event: Event) => {
        const element = closest(event, 'data-input', root);
        if (element) run(element.dataset['input'], element, event);
    };
    const keydown = (event: Event) => {
        if (!(event instanceof KeyboardEvent) || event.key !== 'Enter' || event.shiftKey || event.isComposing) return;
        const element = closest(event, 'data-enter', root);
        if (element && run(element.dataset['enter'], element, event)) event.preventDefault();
    };
    const listeners: [string, (event: Event) => void][] = [
        ['click', click],
        ['submit', submit],
        ['change', change],
        ['input', input],
        ['keydown', keydown],
    ];
    for (const [kind, listener] of listeners) root.addEventListener(kind, listener);
    return () => {
        for (const [kind, listener] of listeners) root.removeEventListener(kind, listener);
    };
}
