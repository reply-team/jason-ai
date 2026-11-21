// Pure: the acts a conversation offers, as one list, so the agent's header ("…", `agent-head.tsx`) and the menu on a
// row of Recent (`row-menu.tsx`) offer the same acts by the same words, gated and held by the same rules. Each word is
// a literal, as the header always said it, but the file manager's, which Open in names (`open-folder.ts`), and the
// agent host's, a word of the locale table with the host's name in it.

import { t } from '../platform/i18n';
import { transitions, verbs, type Transition } from '../screens/campaigns/moves-model';

/** An act of the conversation itself. */
export type ConversationAct = 'rename' | 'fresh' | 'remove';

/** One entry of a menu: an act with its word, held (`disabled`) where it cannot be taken now, or a separator. */
export type MenuItem =
    | { kind: 'open'; label: string }
    /** Opens the agent's folder in the system's file manager. */
    | { kind: 'folder'; label: string }
    /** Opens a new session of an agent host in a terminal at the agent's folder. */
    | { kind: 'session'; host: SessionHost['id']; label: string; title: string }
    | { kind: 'move'; move: Transition; label: string }
    | { kind: 'archive'; label: string }
    | { kind: ConversationAct; label: string; disabled: boolean; title?: string }
    | { kind: 'separator' };

/** What is known of the conversation: the desktop's kept id, the id it is held by, and what its chat is doing now. */
export interface ConversationFacts {
    /** The id the desktop keeps it by; undefined for a conversation not kept yet. */
    kept: string | undefined;
    /** The id the console holds it by, the kept one first; undefined where there is neither. */
    id: string | undefined;
    /** The agent it is about; null for a chat about no agent. */
    campaignId: string | null;
    /** Whether a turn of it runs now; false where it is not known. */
    running: boolean;
    /** Whether its removal is being asked now; false where it is not known. */
    removing: boolean;
    /** The host its chat runs on, a `DesktopHost.id`, by the chat's own rule (`runsOn`); null where there is none. */
    host: string | null;
}

/** An agent host Open in found on this computer, which a terminal can open a session of, by the desktop's name for it. */
export interface SessionHost {
    id: 'claude' | 'codex';
    label: string;
}

/** What the place lets the person do, as `can()` and `inTab()` say it there. */
export interface Gates {
    create: boolean;
    rename: boolean;
    restart: boolean;
    remove: boolean;
    /** Whether the console is a tab the desktop opened, where a conversation is neither started fresh nor removed. */
    tab: boolean;
    /**
     * The word of the act that opens an agent's folder in the system's file manager, as Open in names that place; null
     * where the place offers none.
     */
    folder: string | null;
    /**
     * The agent hosts a terminal can open a session of, in Open in's order (Claude Code before Codex), as Open in found
     * them; empty where the place offers none: in a tab, in the demo, before Open in has answered, and where none is found.
     */
    hosts: readonly SessionHost[];
}

const keptTitle = 'Send a message first; then the chat can be renamed';

/**
 * The conversation's acts, as the agent's "…" holds them: for a conversation about an agent, first, the act that opens
 * the agent's folder in the file manager, where the place names one and is no tab, and the act that opens an agent host
 * in a terminal there, where Open in found one and the place is no tab; then Rename, Start fresh, Remove.
 * None where the place makes no chat; Start fresh and Remove are not in a tab; each is held while it cannot be taken:
 * Rename until the conversation is kept or while its removal is asked, Start fresh and Remove while its turn runs or its
 * removal is asked.
 */
export function conversationItems(facts: ConversationFacts, gates: Gates): MenuItem[] {
    if (!gates.create) return [];
    const busy = facts.running || facts.removing;
    const items: MenuItem[] = [];
    if (facts.campaignId !== null && !gates.tab && gates.folder !== null)
        items.push({ kind: 'folder', label: gates.folder });
    // One host, never two: the one the agent's chat runs on where Open in found it, else the first found.
    const host =
        facts.campaignId === null || gates.tab
            ? undefined
            : (gates.hosts.find((each) => each.id === facts.host) ?? gates.hosts[0]);
    if (host !== undefined)
        items.push({
            kind: 'session',
            host: host.id,
            label: t('agent.openHost', { host: host.label }),
            title: t('agent.openHostTitle', { host: host.label }),
        });
    if (gates.rename)
        items.push({
            kind: 'rename',
            label: 'Rename conversation',
            disabled: facts.kept === undefined || facts.removing,
            title: facts.kept === undefined ? keptTitle : 'Give this conversation a name of your own',
        });
    if (!gates.tab && gates.restart)
        items.push({
            kind: 'fresh',
            label: 'Start conversation fresh',
            disabled: facts.id === undefined || facts.kept === undefined || busy,
            title: "The AI's next turn begins without its memory of this conversation; what is shown here stays",
        });
    if (!gates.tab && gates.remove)
        items.push({ kind: 'remove', label: 'Remove conversation', disabled: facts.kept === undefined || busy });
    return items;
}

/**
 * The menu on a row of Recent: Open, and right after it the agent's folder in the file manager and the agent host in a
 * terminal, where the conversation's acts hold them; for an agent whose status is read, its move by that status and Archive…, neither for an archived one;
 * then the conversation's other acts, Remove last after a separator of its own. A chat about no agent, or an agent not
 * read yet, has no move and no Archive…. A separator stands only between two groups that hold something.
 */
export function rowItems(agent: { status: string } | null, facts: ConversationFacts, gates: Gates): MenuItem[] {
    const own = conversationItems(facts, gates);
    const groups: MenuItem[][] = [
        [
            { kind: 'open', label: 'Open' },
            ...own.filter((each) => each.kind === 'folder' || each.kind === 'session'),
            ...(agent === null
                ? []
                : [
                      ...transitions(agent).map((move): MenuItem => ({ kind: 'move', move, label: verbs[move] })),
                      ...(agent.status === 'archived' ? [] : [{ kind: 'archive', label: 'Archive…' } as MenuItem]),
                  ]),
        ],
        own.filter((each) => each.kind !== 'remove' && each.kind !== 'folder' && each.kind !== 'session'),
        own.filter((each) => each.kind === 'remove'),
    ];
    return groups
        .filter((group) => group.length > 0)
        .flatMap((group, at): MenuItem[] => (at === 0 ? group : [{ kind: 'separator' }, ...group]));
}
