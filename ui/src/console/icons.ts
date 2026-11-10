import { html, type Html } from './html';

// The console's line icons: paths on a 24-unit grid, stroked in the current colour.
const icons = {
    home: html`<path d="M3 11l9-7 9 7v9H3z" />`,
    att: html`<path d="M12 3v12M12 19v.5" /><circle cx="12" cy="12" r="10" />`,
    camp: html`<path d="M4 4h16v6H4zM4 14h10v6H4z" />`,
    people: html`<circle cx="9" cy="8" r="4" /><path
            d="M2 21c1-4 4-6 7-6s6 2 7 6M17 11a3 3 0 100-6M22 21c-.5-2.5-2-4-4-4.6"
        />`,
    work: html`<path d="M4 6h16M4 12h16M4 18h10" />`,
    perf: html`<path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />`,
    plus: html`<path d="M12 5v14M5 12h14" />`,
    shield: html`<path d="M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z" />`,
    journal: html`<path d="M6 3h12v18H6zM9 7h6M9 11h6M9 15h4" />`,
    plug: html`<path d="M9 2v6M15 2v6M6 8h12v4a6 6 0 01-12 0zM12 18v4" />`,
    ops: html`<path d="M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z" />`,
    gear: html`<circle cx="12" cy="12" r="3" /><path
            d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1"
        />`,
    pulse: html`<path d="M3 12h4l3-8 4 16 3-8h4" />`,
    up: html`<path d="M12 19V5M5 12l7-7 7 7" />`,
    stop: html`<rect x="7" y="7" width="10" height="10" rx="2" fill="currentColor" />`,
    chat: html`<path d="M4 5h16v11H9l-5 4z" />`,
} as const;

export type IconName = keyof typeof icons;

export const svg = (name: IconName, size = 16): Html =>
    html`<svg
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
