import { html, type Html } from './html';

const shieldLine = 'M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z';

/**
 * The marks of the four access modes, as path data, so the composer's row (drawn in JSX) and this table draw the same
 * lines: Full access a shield with "!", Ask first a shield with "?", Auto-edit a pencil, Plan only an eye.
 */
export const accessMarks = {
    accFull: [shieldLine, 'M12 7.8v4.9', 'M12 16.2h.01'],
    accAsk: [shieldLine, 'M9.7 9.7a2.4 2.4 0 014.6 1c0 1.6-2.3 1.9-2.3 3.2', 'M12 16.9h.01'],
    accEdit: ['M4 20h4L19.3 8.7a2.1 2.1 0 00-3-3L5 17z', 'M14.6 7.4l3 3'],
    accPlan: [
        'M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z',
        'M15 12a3 3 0 11-6 0 3 3 0 016 0z',
    ],
} as const;

/**
 * The marks of the chat bar's three icon buttons, as path data, so the bar (drawn in JSX) and this table draw the same
 * lines: Rename a pencil, Start fresh a circular arrow, Remove a bin.
 */
export const barMarks = {
    rename: ['M4 20l1-4L16.5 4.5a2.1 2.1 0 013 3L8 19z', 'M14.5 6.5l3 3'],
    restart: ['M20.5 15a8.5 8.5 0 11-2-8.8L21 9', 'M21 4v5h-5'],
    remove: ['M4 7h16', 'M10 4h4', 'M6 7l1 13h10l1-13', 'M10 11v5', 'M14 11v5'],
} as const;

/**
 * A note's close control, a cross, as path data, so the thread (drawn by the painter) and the sidebar's notes (drawn in
 * JSX) draw the same lines.
 */
export const closeMark = ['M6 6l12 12', 'M18 6L6 18'] as const;

const lines = (paths: readonly string[]): Html => html`${paths.map((each) => html`<path d="${each}" />`)}`;

// Copy code's marks: two sheets, the one in front whole and the one behind showing at its corner. Once copied the front
// sheet holds a check, and when the copy failed a cross; each is named for a stylesheet, which colours it.
const sheets = html`<rect x="9" y="9" width="12" height="12" rx="2" /><path
        d="M15 9V5a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2h4"
    />`;

// The console's line icons: paths on a 24-unit grid, stroked in the current colour. Needs you is a check in a circle,
// Settings a cog, Recent's filter two sliders and a note's close control a cross, as the console's look draws them.
const icons = {
    home: html`<path d="M3 11l9-7 9 7v9H3z" />`,
    att: html`<path d="M12 3v12M12 19v.5" /><circle cx="12" cy="12" r="10" />`,
    needs: html`<circle cx="12" cy="12" r="10" /><path d="M8.5 11.8l2.6 2.6 4.6-5" />`,
    camp: html`<path d="M4 4h16v6H4zM4 14h10v6H4z" />`,
    people: html`<circle cx="9" cy="8" r="4" /><path
            d="M2 21c1-4 4-6 7-6s6 2 7 6M17 11a3 3 0 100-6M22 21c-.5-2.5-2-4-4-4.6"
        />`,
    work: html`<path d="M4 6h16M4 12h16M4 18h10" />`,
    perf: html`<path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />`,
    plus: html`<path d="M12 5v14M5 12h14" />`,
    shield: lines([shieldLine]),
    accFull: lines(accessMarks.accFull),
    accAsk: lines(accessMarks.accAsk),
    accEdit: lines(accessMarks.accEdit),
    accPlan: lines(accessMarks.accPlan),
    journal: html`<path d="M6 3h12v18H6zM9 7h6M9 11h6M9 15h4" />`,
    plug: html`<path d="M9 2v6M15 2v6M6 8h12v4a6 6 0 01-12 0zM12 18v4" />`,
    ops: html`<path d="M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z" />`,
    gear: html`<path
            d="M9.6 4.4L10.4 1.5L13.6 1.5L14.4 4.4L15.7 4.9L18.3 3.5L20.5 5.7L19.1 8.3L19.6 9.6L22.5 10.4L22.5 13.6L19.6 14.4L19.1 15.7L20.5 18.3L18.3 20.5L15.7 19.1L14.4 19.6L13.6 22.5L10.4 22.5L9.6 19.6L8.3 19.1L5.7 20.5L3.5 18.3L4.9 15.7L4.4 14.4L1.5 13.6L1.5 10.4L4.4 9.6L4.9 8.3L3.5 5.7L5.7 3.5L8.3 4.9z"
        /><circle cx="12" cy="12" r="3.5" />`,
    plusc: html`<circle cx="12" cy="12" r="10" /><path d="M12 8v8M8 12h8" />`,
    search: html`<circle cx="11" cy="11" r="7" /><path d="M16 16l5 5" />`,
    filter: html`<path d="M4 7h10M18 7h2M4 17h2M10 17h10" /><circle cx="16" cy="7" r="2" /><circle
            cx="8"
            cy="17"
            r="2"
        />`,
    mail: html`<rect x="3" y="5" width="18" height="14" rx="2" /><path d="M3 7l9 6 9-6" />`,
    linkedin: html`<path d="M6 10v9M6 6v.01M11 19v-9M11 14a4 4 0 018 0v5" />`,
    check: html`<path d="M5 12l5 5 9-10" />`,
    close: lines(closeMark),
    chev: html`<path d="M6 9l6 6 6-6" />`,
    more: html`<circle cx="5" cy="12" r="1.2" /><circle cx="12" cy="12" r="1.2" /><circle cx="19" cy="12" r="1.2" />`,
    pulse: html`<path d="M3 12h4l3-8 4 16 3-8h4" />`,
    up: html`<path d="M12 19V5M5 12l7-7 7 7" />`,
    stop: html`<rect x="7" y="7" width="10" height="10" rx="2" fill="currentColor" />`,
    chat: html`<path d="M4 5h16v11H9l-5 4z" />`,
    theme: html`<circle cx="12" cy="12" r="9" /><path d="M12 3a9 9 0 010 18z" fill="currentColor" />`,
    copy: sheets,
    copied: html`${sheets}<path class="chat-md-tick" d="M12.5 15l2 2 4-4" />`,
    notCopied: html`${sheets}<path class="chat-md-cross" d="M13 13l5 5M18 13l-5 5" />`,
    globe: html`<circle cx="12" cy="12" r="9" /><path
            d="M3 12h18M12 3c2.5 2.5 3.8 5.5 3.8 9s-1.3 6.5-3.8 9c-2.5-2.5-3.8-5.5-3.8-9S9.5 5.5 12 3z"
        />`,
} as const;

export type IconName = keyof typeof icons;

/** An icon, `size` pixels square; `className` names it for a stylesheet. */
export const svg = (name: IconName, size = 16, className?: string): Html =>
    html`<svg
        ${className ? html`class="${className}"` : null}
        width="${size}"
        height="${size}"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
    >
        ${icons[name]}
    </svg>`;
