import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useApi } from '../api/context';
import { ArchiveDialog } from '../screens/campaigns/archive';
import { TransitionDialog, type Movable, type Transition } from '../screens/campaigns/pause-dialog';
import { agentFacts, peopleOf, readAgent } from './agent-facts';
import { campaigns, reload as reloadCampaigns } from './campaigns';
import { begin, factsOf, placeGates } from './conversation-acts';
import { rowItems, type ConversationFacts, type MenuItem } from './conversation-items';
import { setVariable } from './html';
import { MenuEntries, menuItems, menuKeys } from './menu-entries';
import { openFolder, openSession } from './open-folder';
import { closeRowMenu, dotsElement, drawnRow, focusOpener, rowDrawn, rowElement, rowMenu } from './recent';
import { openAgentRow, openRow } from './side';
import { conversations, here } from './state';

// The menu on a row of AGENTS or CHATS: the acts the row's own view offers, opened on the row by a right-click, Shift+F10,
// the menu key (`side.rowmenu`) or its three dots (`side.rowdots`), and never opening the row by itself. An agent with no
// conversation yet has the agent's acts, and its conversation's held until it has one. The agent's moves keep their own dialogs, which name
// the agent, over whatever the area shows; each act of the conversation shows its chat first and begins there
// (`conversation-acts.ts`).

/** The agent a row is about, as its row says it (a hand-over reaches the row first); else its conversation's; else none. */
function campaignOf(id: string): string | null {
    const row = drawnRow(id);
    if (row !== undefined) return row.campaignId;
    return conversations.value.find((each) => each.id === id)?.campaignId ?? null;
}

/** Whether the row is an agent with no conversation yet. */
const unspoken = (id: string): boolean => drawnRow(id)?.conversationId === null;

/** What is known of a row's conversation: none kept yet for an agent that has none, so its conversation's acts are held. */
function factsOfRow(id: string): ConversationFacts {
    if (!unspoken(id)) return factsOf(id);
    return { kept: undefined, id: undefined, campaignId: campaignOf(id), running: false, removing: false, host: null };
}

/** The agent a row is about, as the console read it last: its own facts, else the list of agents; else none. */
function agentOf(id: string): Movable | null {
    const campaign = campaignOf(id);
    if (campaign === null) return null;
    return agentFacts.value[campaign]?.campaign ?? campaigns.value.find((each) => each.id === campaign) ?? null;
}

/** How many people the agent of a row holds, as the console counted them last. */
function peopleOfRow(id: string): number | null {
    const campaign = campaignOf(id);
    return campaign === null ? null : peopleOf(campaign);
}

type Open = { row: string; dots?: true; agent: Movable } & ({ kind: 'move'; move: Transition } | { kind: 'archive' });

export function RowMenu() {
    const api = useApi();
    const at = rowMenu.value;
    const menu = useRef<HTMLDivElement>(null);
    const [dialog, setDialog] = useState<Open | null>(null);
    const drawn = at !== null && rowDrawn(at.id);

    // Placed where it was opened and kept inside the window, measured again whenever its acts change.
    useLayoutEffect(() => {
        const element = menu.current;
        if (at === null || element === null) return;
        const { width, height } = element.getBoundingClientRect();
        const x = Math.max(4, Math.min(at.x, window.innerWidth - width - 4));
        const y = Math.max(4, Math.min(at.y, window.innerHeight - height - 4));
        setVariable(element, '--row-menu-x', `${x}px`);
        setVariable(element, '--row-menu-y', `${y}px`);
    });
    // Its first act focused as it opens.
    useLayoutEffect(() => {
        if (at !== null) (menuItems(menu.current)[0] ?? menu.current)?.focus();
    }, [at]);
    // An act held while it had the focus gives it to the first act that can be taken, or to the menu itself, so its keys
    // still work; a focus taken elsewhere is left where it is.
    useLayoutEffect(() => {
        const element = menu.current;
        if (element === null) return;
        const focused = document.activeElement;
        const lost = focused === null || focused === document.body || element.contains(focused);
        if (lost && !menuItems(element).includes(focused as HTMLButtonElement) && focused !== element)
            (menuItems(element)[0] ?? element).focus();
    });
    useEffect(() => {
        if (at === null) return;
        // A press on the dots that opened it is theirs: their click closes it.
        const away = (event: MouseEvent) => {
            const target = event.target as Node;
            if (at.dots === true && dotsElement(at.id)?.contains(target)) return;
            if (!menu.current?.contains(target)) closeRowMenu();
        };
        // The row it stands by moves when the sections scroll or the window changes size: the menu closes rather than float.
        const moved = (event: Event) => {
            const row = rowElement(at.id);
            if (
                event.target === document ||
                (event.target instanceof Node && row?.parentNode && event.target.contains(row))
            )
                closeRowMenu();
        };
        const resized = () => closeRowMenu();
        document.addEventListener('mousedown', away);
        document.addEventListener('scroll', moved, true);
        window.addEventListener('resize', resized);
        return () => {
            document.removeEventListener('mousedown', away);
            document.removeEventListener('scroll', moved, true);
            window.removeEventListener('resize', resized);
        };
    }, [at]);
    // A row no longer drawn, by a new list, a filter or a search, takes its menu with it.
    useEffect(() => {
        if (at !== null && !drawn) closeRowMenu();
    }, [at, drawn]);

    const close = (focus: boolean) => {
        closeRowMenu();
        if (focus && at !== null) focusOpener(at);
    };
    const choose = (item: MenuItem) => {
        if (at === null) return;
        const row = at.id;
        const dots = at.dots;
        const agent = agentOf(row);
        closeRowMenu();
        // The focus goes back to what opened the menu, where the act shows nothing that takes it: never to the page.
        focusOpener(at);
        if (item.kind === 'open') (unspoken(row) ? openAgentRow : openRow)(row);
        else if (item.kind === 'folder' || item.kind === 'session') {
            const campaign = campaignOf(row);
            if (campaign !== null)
                void (item.kind === 'folder' ? openFolder(campaign) : openSession(campaign, item.host));
        } else if (item.kind === 'move' && agent)
            setDialog({ row, ...(dots ? { dots } : {}), agent, kind: 'move', move: item.move });
        else if (item.kind === 'archive' && agent)
            setDialog({ row, ...(dots ? { dots } : {}), agent, kind: 'archive' });
        else if (item.kind === 'rename' || item.kind === 'fresh' || item.kind === 'remove')
            void begin(row, item.kind, (held) => here().dock.showChat(held));
    };
    const ended = (open: Open, done: boolean) => {
        setDialog(null);
        if (done) {
            void readAgent(api, open.agent.id);
            void reloadCampaigns();
        }
        focusOpener({ id: open.row, ...(open.dots ? { dots: open.dots } : {}) });
    };

    return (
        <>
            {at !== null && drawn && (
                <div
                    ref={menu}
                    className="agent-menu row-menu"
                    role="menu"
                    aria-label={at.title}
                    tabIndex={-1}
                    onKeyDown={(event) => {
                        // Tab leaves the menu for the row it was opened on.
                        if (event.key === 'Tab') {
                            event.preventDefault();
                            close(true);
                        } else menuKeys(event, menu.current, close);
                    }}
                >
                    <MenuEntries
                        items={rowItems(agentOf(at.id), factsOfRow(at.id), placeGates(), peopleOfRow(at.id))}
                        onChoose={choose}
                    />
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
