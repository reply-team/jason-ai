import type { KeyboardEvent } from 'react';
import type { MenuItem } from './conversation-items';

// The entries of the console's React menus and the keys they take, one way for each: the agent's "…" and the menu on a
// row of the sidebar.

/** The acts of a menu, each a menuitem, held where it cannot be taken now, with a separator between groups. */
export function MenuEntries({ items, onChoose }: { items: MenuItem[]; onChoose: (item: MenuItem) => void }) {
    return (
        <>
            {items.map((item, at) =>
                item.kind === 'separator' ? (
                    <div key={`separator-${at}`} className="menu-separator" role="separator" />
                ) : (
                    <button
                        key={item.kind === 'move' ? `move-${item.move}` : item.kind}
                        type="button"
                        role="menuitem"
                        disabled={'disabled' in item ? item.disabled : false}
                        title={'title' in item ? item.title : undefined}
                        onClick={() => onChoose(item)}
                    >
                        {item.label}
                    </button>
                ),
            )}
        </>
    );
}

/** The acts of the menu that can be taken now, in its order. */
export function menuItems(menu: HTMLElement | null): HTMLButtonElement[] {
    return [...(menu?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? [])];
}

/**
 * A menu's keys: Escape closes it and gives the focus back, the arrows move round its acts, Home and End go to the first
 * and the last, and Tab closes it and lets the focus go on. Enter takes the act focused, as a button does.
 */
export function menuKeys(event: KeyboardEvent<HTMLElement>, menu: HTMLElement | null, close: (focus: boolean) => void) {
    const all = menuItems(menu);
    const at = all.indexOf(document.activeElement as HTMLButtonElement);
    if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        close(true);
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        all[(at + step + all.length) % all.length]?.focus();
    } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault();
        all[event.key === 'Home' ? 0 : all.length - 1]?.focus();
    } else if (event.key === 'Tab') close(false);
}
