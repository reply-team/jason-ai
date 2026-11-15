import { fireEvent, screen, within } from '@testing-library/react';

// The two menus in a composer's row, read and used as a person does: open the button, read or click an item, close.
// An item says what it chooses by `data-kind` (a mode, a host, a model, an effort) and `data-value`.

export type MenuName = 'Access mode' | 'Model';
export type ItemKind = 'mode' | 'host' | 'model' | 'effort';

const named = (menu: MenuName) => new RegExp(`^${menu}:`);

/** The button that opens a menu, named by the menu and the value it shows. */
export const menuButton = (menu: MenuName, root: HTMLElement = document.body): HTMLButtonElement =>
    within(root).getByRole<HTMLButtonElement>('button', { name: named(menu) });

export const queryMenuButton = (menu: MenuName, root: HTMLElement = document.body): HTMLButtonElement | null =>
    within(root).queryByRole<HTMLButtonElement>('button', { name: named(menu) });

/** Opens a menu, if it is not open, and returns it. */
export function openMenu(menu: MenuName, root: HTMLElement = document.body): HTMLElement {
    const button = menuButton(menu, root);
    if (button.getAttribute('aria-expanded') !== 'true') fireEvent.click(button);
    return screen.getByRole('menu', { name: named(menu) });
}

/** Closes a menu with Escape, if it is open. */
export function closeMenu(menu: MenuName): void {
    const open = screen.queryByRole('menu', { name: named(menu) });
    if (open) fireEvent.keyDown(open, { key: 'Escape' });
}

/** One item as the menu shows it. */
export interface Choice {
    value: string;
    text: string;
    /** The line under its name: what it does, or why it cannot be chosen; null when none. */
    said: string | null;
    disabled: boolean;
    checked: boolean;
}

const itemsOf = (open: HTMLElement, kind: ItemKind): HTMLElement[] =>
    [...open.querySelectorAll<HTMLElement>('[data-kind]')].filter((each) => each.dataset.kind === kind);

/** The items of one kind in a menu, in order; the menu, and the focus, are left as they were found. */
export function choicesOf(menu: MenuName, kind: ItemKind, root: HTMLElement = document.body): Choice[] {
    const focused = document.activeElement as HTMLElement | null;
    const wasOpen = menuButton(menu, root).getAttribute('aria-expanded') === 'true';
    const items = itemsOf(openMenu(menu, root), kind).map((each) => ({
        value: each.dataset.value ?? '',
        text: each.getAttribute('aria-label') ?? each.textContent ?? '',
        said: each.querySelector('small')?.textContent ?? null,
        disabled: each.getAttribute('aria-disabled') === 'true',
        checked: each.getAttribute('aria-checked') === 'true',
    }));
    if (!wasOpen) closeMenu(menu);
    focused?.focus();
    return items;
}

/** The value chosen of one kind in a menu; null when none of its items is. */
export const currentOf = (menu: MenuName, kind: ItemKind, root: HTMLElement = document.body): string | null =>
    choicesOf(menu, kind, root).find((each) => each.checked)?.value ?? null;

/** Opens a menu and clicks the item of one kind with a value; the menu is closed after, whatever the item did. */
export function chooseIn(menu: MenuName, kind: ItemKind, value: string, root: HTMLElement = document.body): void {
    const item = itemsOf(openMenu(menu, root), kind).find((each) => each.dataset.value === value);
    if (!item) throw new Error(`No ${kind} ${value} in the ${menu} menu`);
    fireEvent.click(item);
    closeMenu(menu);
}
