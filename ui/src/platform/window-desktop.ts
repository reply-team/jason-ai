import type { JasonDesktop } from './desktop';

/**
 * The desktop's own window's object, as its preload put it on the page; undefined in a browser. The one place the page
 * reads it: every other module is handed an object of the desktop's shape by `places.ts`.
 */
export const windowDesktop = (): JasonDesktop | undefined => window.jasonDesktop;
