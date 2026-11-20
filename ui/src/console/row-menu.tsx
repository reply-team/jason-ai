import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useApi } from '../api/context';
import { ArchiveDialog } from '../screens/campaigns/archive';
import { TransitionDialog, type Movable, type Transition } from '../screens/campaigns/pause-dialog';
import { agentFacts, readAgent } from './agent-facts';
import { campaigns, reload as reloadCampaigns } from './campaigns';
import { begin, factsOf, placeGates } from './conversation-acts';
import { rowItems, type MenuItem } from './conversation-items';
import { setVariable } from './html';
import { MenuEntries, menuItems, menuKeys } from './menu-entries';
import { closeRowMenu, focusRow, rowDrawn, rowMenu } from './recent';
import { openRow } from './side';
import { conversations, here } from './state';

// The menu on a row of Recent: the acts the row's own view offers, opened on the row by a right-click, Shift+F10 or the
// menu key (`side.rowmenu`), and never opening the row by itself. The agent's moves keep their own dialogs, which name
// the agent, over whatever the area shows; each act of the conversation shows its chat first and begins there
// (`conversation-acts.ts`).

/** The agent a conversation is about, as the console read it last: its own facts, else the list of agents; else none. */
function agentOf(id: string): Movable | null {
    const campaign = conversations.value.find((each) => each.id === id)?.campaignId ?? null;
    if (campaign === null) return null;
    return agentFacts.value[campaign]?.campaign ?? campaigns.value.find((each) => each.id === campaign) ?? null;
}

type Open = { row: string; agent: Movable } & ({ kind: 'move'; move: Transition } | { kind: 'archive' });

export function RowMenu() {
    const api = useApi();
    const at = rowMenu.value;
    const menu = useRef<HTMLDivElement>(null);
    const [dialog, setDialog] = useState<Open | null>(null);
    const drawn = at !== null && rowDrawn(at.id);

    // Placed where it was opened, kept inside the window, with its first act focused.
    useLayoutEffect(() => {
        const element = menu.current;
        if (at === null || element === null) return;
        const { width, height } = element.getBoundingClientRect();
        const x = Math.max(4, Math.min(at.x, window.innerWidth - width - 4));
        const y = Math.max(4, Math.min(at.y, window.innerHeight - height - 4));
        setVariable(element, '--row-menu-x', `${x}px`);
        setVariable(element, '--row-menu-y', `${y}px`);
        menuItems(element)[0]?.focus();
    }, [at]);
    useEffect(() => {
        if (at === null) return;
        const away = (event: MouseEvent) => {
            if (!menu.current?.contains(event.target as Node)) closeRowMenu();
        };
        document.addEventListener('mousedown', away);
        return () => document.removeEventListener('mousedown', away);
    }, [at]);
    // A row no longer drawn, by a new list, a filter or a search, takes its menu with it.
    useEffect(() => {
        if (at !== null && !drawn) closeRowMenu();
    }, [at, drawn]);

    const close = (focus: boolean) => {
        const row = at?.id;
        closeRowMenu();
        if (focus && row !== undefined) focusRow(row);
    };
    const choose = (item: MenuItem) => {
        if (at === null) return;
        const row = at.id;
        const agent = agentOf(row);
        closeRowMenu();
        if (item.kind === 'open') openRow(row);
        else if (item.kind === 'move' && agent) setDialog({ row, agent, kind: 'move', move: item.move });
        else if (item.kind === 'archive' && agent) setDialog({ row, agent, kind: 'archive' });
        else if (item.kind === 'rename' || item.kind === 'fresh' || item.kind === 'remove')
            void begin(row, item.kind, (held) => here().dock.showChat(held));
    };
    const ended = (open: Open, done: boolean) => {
        setDialog(null);
        if (done) {
            void readAgent(api, open.agent.id);
            void reloadCampaigns();
        }
        focusRow(open.row);
    };

    return (
        <>
            {at !== null && drawn && (
                <div
                    ref={menu}
                    className="agent-menu row-menu"
                    role="menu"
                    aria-label={at.title}
                    onKeyDown={(event) => menuKeys(event, menu.current, close)}
                >
                    <MenuEntries items={rowItems(agentOf(at.id), factsOf(at.id), placeGates())} onChoose={choose} />
                </div>
            )}
            {dialog !== null && (
                <div className="harness">
                    {dialog.kind === 'move' ? (
                        <TransitionDialog
                            campaign={dialog.agent}
                            kind={dialog.move}
                            onDone={() => ended(dialog, true)}
                            onClose={() => ended(dialog, false)}
                        />
                    ) : (
                        <ArchiveDialog
                            campaign={dialog.agent}
                            onDone={() => ended(dialog, true)}
                            onClose={() => ended(dialog, false)}
                        />
                    )}
                </div>
            )}
        </>
    );
}
