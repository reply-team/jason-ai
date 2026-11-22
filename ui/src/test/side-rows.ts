/**
 * A row of the sidebar's two sections, AGENTS and CHATS, by the name it shows, in the section named or in either. Throws
 * where no row stands, so a test waits for it with `waitFor`, and where more than one does, so a test never passes on
 * the wrong row.
 */
export function sideRow(name: string, section?: 'agents' | 'chats'): HTMLElement {
    const group =
        section === undefined ? '' : `[role="group"][aria-label="${section === 'agents' ? 'Agents' : 'Chats'}"] `;
    const rows = Array.from(document.querySelectorAll<HTMLElement>(`.side .sections ${group}.recent-row`)).filter(
        (each) => each.getAttribute('aria-label') === name,
    );
    const where = section === undefined ? 'the sidebar' : section === 'agents' ? 'AGENTS' : 'CHATS';
    if (rows.length > 1) throw new Error(`${rows.length} rows of ${where} show ${name}.`);
    const [row] = rows;
    if (row === undefined) throw new Error(`No row of ${where} shows ${name}.`);
    return row;
}
