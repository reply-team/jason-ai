import { signal } from '@preact/signals-react';

export type ThemeChoice = 'system' | 'light' | 'dark';

const key = 'jason.ui.theme';
/** Whether this load stored Light itself, for a viewer with no key: a default the page wrote, not a choice. */
let adoptedLight = false;
const isChoice = (value: unknown): value is ThemeChoice => value === 'light' || value === 'dark' || value === 'system';

/**
 * The choice this browser keeps. Light is for a viewer that is new: one with no key of the UI stored at all. A viewer
 * that has any (a pairing, a stored choice) is an existing one and, never having chosen, keeps following the system.
 * A new viewer's Light is stored, so the pairing it makes next does not turn it into an existing one.
 */
function read(): ThemeChoice {
    try {
        const saved = localStorage.getItem(key);
        if (isChoice(saved)) return saved;
        const existing = Object.keys(localStorage).some((each) => each.startsWith('jason.ui.'));
        if (existing) return 'system';
        localStorage.setItem(key, 'light');
        adoptedLight = true;
        return 'light';
    } catch {
        return 'light';
    }
}

export const themeChoice = signal<ThemeChoice>(typeof localStorage === 'undefined' ? 'light' : read());
export const resolvedTheme = signal<'light' | 'dark'>('light');

function systemIsDark(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
}

function apply(): void {
    resolvedTheme.value = themeChoice.value === 'system' ? (systemIsDark() ? 'dark' : 'light') : themeChoice.value;
    document.documentElement.dataset['theme'] = resolvedTheme.value;
}

/** What the desktop offers for the theme; a desktop older than this page offers none of it, or only part. */
export interface DesktopThemeSeam {
    get?: () => Promise<ThemeChoice | null>;
    set?: (choice: ThemeChoice) => Promise<void>;
}

let seam: DesktopThemeSeam | undefined;

/** Tells the desktop a choice; never throws, since a desktop that cannot be told is no reason for a page not to open. */
function tell(choice: ThemeChoice): void {
    try {
        void seam?.set?.(choice)?.catch(() => undefined);
    } catch {
        // A desktop with no such member.
    }
}

/**
 * In the desktop, the desktop's kept choice is the truth: it is read before the page's first paint and taken over the
 * page's own storage. When the desktop holds none, the page's own choice, if it was stored before this load, is told to it once,
 * so the next start opens in it; a Light the page adopted on this load is no choice: it takes the system's, stores
 * nothing and tells nothing. Never throws.
 */
export async function bindDesktopTheme(desktopSeam: DesktopThemeSeam | undefined): Promise<void> {
    seam = desktopSeam;
    let kept: ThemeChoice | null;
    try {
        const answer = await seam?.get?.();
        kept = isChoice(answer) ? answer : null;
    } catch {
        kept = null;
    }
    if (kept !== null) {
        themeChoice.value = kept;
        try {
            localStorage.setItem(key, kept);
        } catch {
            // The choice lasts for this page load.
        }
        return;
    }
    // The desktop holds nothing: it follows the system for an installation that existed. The page tells it a choice that
    // was stored before this load; a Light the page just adopted is no choice, so the system's it is, stored nowhere.
    if (adoptedLight) {
        themeChoice.value = 'system';
        try {
            localStorage.removeItem(key);
        } catch {
            // Nothing stored to remove.
        }
        return;
    }
    try {
        if (isChoice(localStorage.getItem(key))) tell(themeChoice.value);
    } catch {
        // No storage: nothing of the page's own to tell.
    }
}

export function setTheme(choice: ThemeChoice): void {
    themeChoice.value = choice;
    tell(choice);
    try {
        localStorage.setItem(key, choice);
    } catch {
        // Then the choice lasts for this page load.
    }
    apply();
}

/** Applies the choice and follows the system while the choice is to. */
export function startTheme(): void {
    apply();
    if (typeof matchMedia === 'function') matchMedia('(prefers-color-scheme: dark)').addEventListener('change', apply);
}
