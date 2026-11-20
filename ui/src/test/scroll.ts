// happy-dom lays out nothing: a box has no height and keeps any scroll position it is given. These helpers lay out the
// chats' scrolling boxes by a rule instead, so a screen test can tell a thread followed to its foot from one left where
// it was: a box's view is `view` pixels high; what it holds is its parts stacked in order, the thread, the queued line and
// the cards that wait under them, each `perCharacter` pixels for each character of its words unless `heights` sizes it,
// never less than the view all together; and its place stays between the top and the foot, as a browser keeps it. Each
// part stands where the stacking puts it, as `getBoundingClientRect` says, the box's view starting at 0.

const box = (element: Element) => element.classList.contains('chat-scroll');
const names = ['scrollHeight', 'clientHeight', 'scrollTop', 'getBoundingClientRect'] as const;

/** The parts of a box that are its conversation: the thread and the line queued under it. */
const said = (element: Element) => element.classList.contains('thread') || element.classList.contains('chat-queued');

/** A rectangle `top` to `top + height`, as wide as nothing. */
const rect = (top: number, height: number) =>
    ({ x: 0, y: top, top, left: 0, right: 0, width: 0, height, bottom: top + height, toJSON: () => ({}) }) as DOMRect;

let laid: { part: (element: Element) => number; view: number } | null = null;

/**
 * Lays out every `.chat-scroll` by the rule until `restore` is called; other elements keep happy-dom's own answers.
 * `heights` sizes a part of a box by its own measure instead of its words, where it answers a number.
 */
export function layOutChats(
    initialView = 500,
    perCharacter = 4,
    heights: (part: Element) => number | undefined = () => undefined,
): { restore: () => void; setView: (pixels: number) => void } {
    let view = initialView;
    const proto = HTMLElement.prototype;
    const above = Object.getPrototypeOf(proto) as object;
    const saved = names.map((name) => [name, Object.getOwnPropertyDescriptor(proto, name)] as const);
    const places = new WeakMap<Element, number>();
    const part = (element: Element) => heights(element) ?? (element.textContent ?? '').length * perCharacter;
    const height = (element: Element) =>
        Math.max(
            view,
            Array.from(element.children).reduce((sum, each) => sum + part(each), 0),
        );
    const place = (element: Element) => Math.min(Math.max(places.get(element) ?? 0, 0), height(element) - view);
    laid = { part, view };
    const setView = (pixels: number) => {
        view = pixels;
        laid = { part, view };
    };
    Object.defineProperty(proto, 'scrollHeight', {
        configurable: true,
        get(this: HTMLElement) {
            return box(this) ? height(this) : (Reflect.get(above, 'scrollHeight', this) as number);
        },
    });
    Object.defineProperty(proto, 'clientHeight', {
        configurable: true,
        get(this: HTMLElement) {
            return box(this) ? view : (Reflect.get(above, 'clientHeight', this) as number);
        },
    });
    Object.defineProperty(proto, 'scrollTop', {
        configurable: true,
        get(this: HTMLElement) {
            return box(this) ? place(this) : (Reflect.get(above, 'scrollTop', this) as number);
        },
        set(this: HTMLElement, value: number) {
            if (box(this)) places.set(this, Math.min(Math.max(value, 0), height(this) - view));
            else Reflect.set(above, 'scrollTop', value, this);
        },
    });
    Object.defineProperty(proto, 'getBoundingClientRect', {
        configurable: true,
        writable: true,
        value(this: HTMLElement) {
            if (box(this)) return rect(0, view);
            const parent = this.parentElement;
            if (parent === null || !box(parent))
                return (Reflect.get(above, 'getBoundingClientRect', this) as () => DOMRect).call(this);
            let top = 0;
            for (const each of Array.from(parent.children)) {
                if (each === this) break;
                top += part(each);
            }
            return rect(top - place(parent), part(this));
        },
    });
    return {
        setView,
        restore: () => {
            laid = null;
            for (const [name, descriptor] of saved) {
                if (descriptor) Object.defineProperty(proto, name, descriptor);
                else delete (proto as unknown as Record<string, unknown>)[name];
            }
        },
    };
}

/** How far a box's view stands above its own foot, in pixels; 0 at the foot. */
export function fromFoot(element: HTMLElement): number {
    return element.scrollHeight - element.scrollTop - element.clientHeight;
}

/** Where the conversation ends in a laid-out box, in the box's own coordinates: the bottom of its thread and queued line. */
export function conversationEnd(element: HTMLElement): number {
    if (laid === null) throw new Error('lay out the chats first');
    let top = 0;
    let end = 0;
    for (const each of Array.from(element.children)) {
        top += laid.part(each);
        if (said(each)) end = top;
    }
    return end;
}

/** The place a followed conversation stands at in a laid-out box, by the rule the page keeps, worked out here alone. */
export function followedPlace(element: HTMLElement): number {
    const view = element.clientHeight;
    return Math.max(0, Math.min(element.scrollHeight - view, conversationEnd(element) - view / 2));
}

/** The person scrolls the box to `top`, as a wheel or the scroll bar does: the place moves and the box says it scrolled. */
export function scrollAs(element: HTMLElement, top: number): void {
    element.scrollTop = top;
    element.dispatchEvent(new Event('scroll'));
}

/**
 * Stands in for the page's ResizeObserver until `restore` is called: happy-dom never reports a size, so a test says
 * when an element's size changed (`resized`) and each observer watching it is called, as a browser calls it after a layout.
 */
export function watchSizes(): { resized: (element: Element) => void; restore: () => void } {
    const saved = Object.getOwnPropertyDescriptor(globalThis, 'ResizeObserver');
    const live = new Set<StandInResizeObserver>();
    class StandInResizeObserver {
        readonly watched = new Set<Element>();
        constructor(private readonly callback: ResizeObserverCallback) {
            live.add(this);
        }
        observe(element: Element) {
            this.watched.add(element);
        }
        unobserve(element: Element) {
            this.watched.delete(element);
        }
        disconnect() {
            this.watched.clear();
            live.delete(this);
        }
        report(element: Element) {
            if (this.watched.has(element))
                this.callback([{ target: element } as ResizeObserverEntry], this as unknown as ResizeObserver);
        }
    }
    Object.defineProperty(globalThis, 'ResizeObserver', {
        configurable: true,
        writable: true,
        value: StandInResizeObserver,
    });
    return {
        resized: (element) => {
            for (const each of [...live]) each.report(element);
        },
        restore: () => {
            if (saved) Object.defineProperty(globalThis, 'ResizeObserver', saved);
            else delete (globalThis as Record<string, unknown>).ResizeObserver;
        },
    };
}
