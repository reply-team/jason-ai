import type { JasonDesktop } from './desktop';

/** The desktop's own window's object, as its preload put it on the page; undefined in a browser. */
export const windowDesktop = (): JasonDesktop | undefined => window.jasonDesktop;

/** The desktop as an earlier version of it may be: a part may be missing, and the page asks before it reads one. */
export const earlier = (desktop: JasonDesktop): Partial<JasonDesktop> => desktop;
