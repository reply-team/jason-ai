import { signal } from '@preact/signals-react';
import type { ComponentType } from 'react';
import type { Route } from '../router';

/** Words an entry shows: fixed, or read at each render. */
export type Text = string | (() => string);

export const textOf = (text: Text): string => (typeof text === 'function' ? text() : text);

/** One of the shell's own screens: the page the router shows for `id`, and its row in the navigation. */
export interface ScreenEntry {
    id: Route['screen'];
    label: Text;
    order: number;
    href: () => string;
    /** True while the item should carry a mark that something needs the person. */
    marked?: () => boolean;
    component: ComponentType;
}

/**
 * The slots the shell is assembled from. The registry is internal: the entries are the screens this build ships, and
 * nothing outside the build can add one.
 */
export interface SlotMap {
    screen: ScreenEntry;
}

export type SlotName = keyof SlotMap;

type Stored = { owner: string; entry: { id: string; order?: number } };

/** Every entry of every active plugin. Reading through `list` inside a component re-renders it on a change. */
export class SlotRegistry {
    readonly version = signal(0);
    private readonly slots = new Map<SlotName, Stored[]>();

    register<S extends SlotName>(slot: S, owner: string, entry: SlotMap[S]): () => void {
        const stored = this.slots.get(slot) ?? [];
        const taken = stored.find((s) => s.entry.id === entry.id);
        if (taken) throw new Error(`${slot} "${entry.id}" is already registered by ${taken.owner}.`);
        const row: Stored = { owner, entry };
        this.slots.set(slot, [...stored, row]);
        this.version.value++;
        return () => {
            this.slots.set(
                slot,
                (this.slots.get(slot) ?? []).filter((s) => s !== row),
            );
            this.version.value++;
        };
    }

    /** The slot's entries by `order`, then id, so one set of plugins always draws one way. */
    list<S extends SlotName>(slot: S): SlotMap[S][] {
        void this.version.value;
        return [...(this.slots.get(slot) ?? [])]
            .sort(
                (a, b) =>
                    (a.entry.order ?? 0) - (b.entry.order ?? 0) ||
                    (a.entry.id < b.entry.id ? -1 : a.entry.id > b.entry.id ? 1 : 0),
            )
            .map((s) => s.entry as SlotMap[S]);
    }
}

export const registry = new SlotRegistry();
