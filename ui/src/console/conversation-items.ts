// Pure: the acts a conversation offers, as one list, so the agent's header ("…", `agent-head.tsx`) and the menu on a
// row of Recent (`row-menu.tsx`) offer the same acts by the same words, gated and held by the same rules. Each word is
// a literal, as the header always said it.

import { transitions, verbs, type Transition } from '../screens/campaigns/moves-model';

/** An act of the conversation itself. */
export type ConversationAct = 'rename' | 'fresh' | 'remove';

/** One entry of a menu: an act with its word, held (`disabled`) where it cannot be taken now, or a separator. */
export type MenuItem =
    | { kind: 'open'; label: string }
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
    /** Whether a turn of it runs now; false where it is not known. */
    running: boolean;
    /** Whether its removal is being asked now; false where it is not known. */
    removing: boolean;
}

/** What the place lets the person do, as `can()` and `inTab()` say it there. */
export interface Gates {
    create: boolean;
    rename: boolean;
    restart: boolean;
    remove: boolean;
    /** Whether the console is a tab the desktop opened, where a conversation is neither started fresh nor removed. */
    tab: boolean;
}

const keptTitle = 'Send a message first; then the chat can be renamed';

/**
 * The conversation's acts, as the agent's "…" holds them: Rename, Start fresh, Remove. None where the place makes no
 * chat; Start fresh and Remove are not in a tab; each is held while it cannot be taken: Rename until the conversation
 * is kept or while its removal is asked, Start fresh and Remove while its turn runs or its removal is asked.
 */
export function conversationItems(facts: ConversationFacts, gates: Gates): MenuItem[] {
    if (!gates.create) return [];
    const busy = facts.running || facts.removing;
    const items: MenuItem[] = [];
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
 * The menu on a row of Recent: Open; for an agent whose status is read, its move by that status and Archive…, neither
 * for an archived one; then the conversation's acts, Remove last after a separator of its own. A chat about no agent, or
 * an agent not read yet, has Open and the conversation's acts alone. A separator stands only between two groups that
 * hold something.
 */
export function rowItems(agent: { status: string } | null, facts: ConversationFacts, gates: Gates): MenuItem[] {
    const own = conversationItems(facts, gates);
    const groups: MenuItem[][] = [
        [
            { kind: 'open', label: 'Open' },
            ...(agent === null
                ? []
                : [
                      ...transitions(agent).map((move): MenuItem => ({ kind: 'move', move, label: verbs[move] })),
                      ...(agent.status === 'archived' ? [] : [{ kind: 'archive', label: 'Archive…' } as MenuItem]),
                  ]),
        ],
        own.filter((each) => each.kind !== 'remove'),
        own.filter((each) => each.kind === 'remove'),
    ];
    return groups
        .filter((group) => group.length > 0)
        .flatMap((group, at): MenuItem[] => (at === 0 ? group : [{ kind: 'separator' }, ...group]));
}
