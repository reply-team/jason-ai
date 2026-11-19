// happy-dom lays out nothing: a box has no height and keeps any scroll position it is given. These helpers lay out the
// chats' scrolling boxes by a rule instead, so a screen test can tell a thread followed to its foot from one left where
// it was: a box's view is `view` pixels high, what it holds is `perCharacter` pixels for each character of its words and
// never less than the view, and its place stays between the top and the foot, as a browser keeps it.

const box = (element: Element) => element.classList.contains('chat-scroll');
const names = ['scrollHeight', 'clientHeight', 'scrollTop'] as const;

/** Lays out every `.chat-scroll` by the rule until `restore` is called; other elements keep happy-dom's own answers. */
export function layOutChats(view = 500, perCharacter = 4): { restore: () => void } {
    const proto = HTMLElement.prototype;
    const above = Object.getPrototypeOf(proto) as object;
    const saved = names.map((name) => [name, Object.getOwnPropertyDescriptor(proto, name)] as const);
    const places = new WeakMap<Element, number>();
    const height = (element: Element) => Math.max(view, (element.textContent ?? '').length * perCharacter);
    const place = (element: Element) => Math.min(Math.max(places.get(element) ?? 0, 0), height(element) - view);
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
    return {
        restore: () => {
            for (const [name, descriptor] of saved) {
                if (descriptor) Object.defineProperty(proto, name, descriptor);
                else delete (proto as unknown as Record<string, unknown>)[name];
            }
        },
    };
}

/** How far a box's view stands above its foot, in pixels; 0 at the foot. */
export function fromFoot(element: HTMLElement): number {
    return element.scrollHeight - element.scrollTop - element.clientHeight;
}

/** The person scrolls the box to `top`, as a wheel or the scroll bar does: the place moves and the box says it scrolled. */
export function scrollAs(element: HTMLElement, top: number): void {
    element.scrollTop = top;
    element.dispatchEvent(new Event('scroll'));
}
