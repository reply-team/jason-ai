import { sameControl } from './html';

// A chat the person opens by their own act has the caret in its box, so they can type at once: a row of Chats or of
// Agents, the Feed's row, each plus, a choice in Search, a row menu's Open, the window's New chat, Jason's mark for Start,
// and Back or Forward to a chat or to Start. The act notes where the focus stood (`openedHere`), and the dock gives the
// box of the pane it shows the focus once that pane is drawn (`giveBox`), with the caret at the end of the words there.
// Where the pane shows an agent's opening, its box is the opening's. Nothing else moves the focus: arrowing along the
// rows, a row's menu, a rename, a removal, an answer arriving or a card appearing call neither. Where the box is not
// there, or disabled, or the person has since put the focus somewhere else, or a menu or a dialog is open, the focus
// stays where it is and nothing takes it in the box's place.

/** A person's act that opens a pane with a box: where the focus stood when they acted. */
export interface Opened {
    readonly from: Element | null;
}

/** Where the focus stands now, as a person's act that opens a chat notes it. */
export function openedHere(): Opened {
    return { from: document.activeElement };
}

/** The box of a chat's pane (the opening's, while it asks Start's question, is the same box), and Start's. */
const boxes = {
    chat: 'textarea[aria-label="Your message"]',
    start: 'textarea[aria-label="Who do you want to reach?"]',
} as const;

/** What stands over the page and holds the focus while it is open: a menu or list, the palette, a dialog. */
const takers = '[role="menu"], [role="listbox"], [role="dialog"], [role="alertdialog"], dialog[open]';

/** Whether something shown on the page holds the focus: what stands in a hidden pane is not on the screen. */
function taken(): boolean {
    return Array.from(document.querySelectorAll(takers)).some((each) => each.closest('[hidden]') === null);
}

/** The latest ask: an earlier one still waiting for its pane gives way to it. */
let asks = 0;

/** Forgets an ask still waiting for its pane: the area moved on to something else. */
function dropBox(): void {
    asks++;
}

/**
 * The area shows `pane`: for a person's act that opened it (`opened`), its box takes the focus once the area has drawn
 * it, with the caret at the end of its words; for anything else, an ask still waiting is forgotten. A pane of no box, an
 * ask the area moved past and a pane not drawn within a few frames take nothing; nor does a box that is disabled, a focus
 * the person has put elsewhere meanwhile, or a menu or a dialog open.
 */
export function giveBox(pane: string, opened: Opened | undefined): void {
    const box = pane === 'start' ? boxes.start : pane.startsWith('chat:') ? boxes.chat : null;
    if (opened === undefined || box === null) return dropBox();
    const mine = ++asks;
    const attempt = (tries: number): void => {
        if (mine !== asks) return;
        const section = document.querySelector<HTMLElement>(`.jc-console .area > .pane[data-pane="${pane}"]`);
        const field = section?.hidden === false ? section.querySelector<HTMLTextAreaElement>(box) : null;
        // A menu closing as the pane is drawn (a row menu's Open) is waited for, as a pane not drawn yet is.
        if (!field || taken()) {
            if (tries > 0) setTimeout(() => attempt(tries - 1), 0);
            return;
        }
        if (field.disabled) return;
        const now = document.activeElement;
        // The focus the person put somewhere else since they acted stays there; one the act left (on its control, or on
        // the one a repaint of the sidebar drew in its place), or one a pane hidden let go of, is free.
        const left = opened.from !== null && now !== null && sameControl(opened.from, now);
        if (now !== null && now !== document.body && now !== field && !left) return;
        field.focus();
        const end = field.value.length;
        field.setSelectionRange(end, end);
    };
    attempt(20);
}
