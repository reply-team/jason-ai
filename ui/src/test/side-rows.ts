import { conversations } from '../console/state';

/**
 * A row of the sidebar's two sections, AGENTS and CHATS, as a person finds it: by the name it shows, or, for an agent's
 * row, which shows the agent's name, by the title of the conversation behind it. Throws where no row stands, so a test
 * waits for it with `waitFor`.
 */
export function sideRow(name: string): HTMLElement {
    const rows = Array.from(document.querySelectorAll<HTMLElement>('.side .sections .recent-row'));
    const shown = rows.find((each) => each.getAttribute('aria-label') === name);
    if (shown) return shown;
    const ids = conversations.value.filter((each) => each.title === name).map((each) => JSON.stringify([each.id]));
    const behind = rows.find((each) => ids.includes(each.dataset['args'] ?? ''));
    if (behind) return behind;
    throw new Error(`No row of the sidebar shows ${name}, nor stands for a conversation of that title.`);
}
