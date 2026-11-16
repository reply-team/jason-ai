import { narrowerThan } from '../platform/narrow';
import { html, type Html } from './html';

/** The edge below which the console's tables scroll in the box around them (`console.css`, `.tscroll`). */
const scrollsBelow = 820;

/** What a box around a table says of itself where it can scroll: a name, a role and a place in the keyboard's order. */
export const scrollAttrs = (label: string): Html | null =>
    narrowerThan(scrollsBelow) ? html`role="region" tabindex="0" aria-label="${label}"` : null;
