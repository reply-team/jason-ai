// Pure: a chat as the page keeps it, and what each event of the desktop's does to it. The desktop runs the turn and
// keeps the conversation, what the page drew included; the page holds what it shows, and a conversation reopened is
// drawn from what the desktop kept by the same code as a live turn. A message the person sends while a turn runs
// waits here, one at a time, and nowhere else: it becomes the next turn when this one ends well. When the turn fails, or
// the moment the person stops it, the message is given back: into the box when the box is empty, and otherwise kept in
// sight as not sent. Two texts are never merged, and neither is dropped.
import type {
    DesktopChatEvent,
    DesktopChatMode,
    DesktopChatOptions,
    DesktopConversation,
    DesktopConversationRecord,
    DesktopEffort,
    DesktopHost,
    DesktopKeptEntry,
    DesktopLineEntry,
    DesktopModel,
} from '../platform/desktop';

export type Answer = 'allow' | 'always' | 'deny' | 'gone';

/** A step of the agent's: the desktop's plain words for it, and what it acted on, when it said. */
export interface Step {
    text: string;
    detail?: string;
}

export type Entry =
    | { kind: 'user'; text: string }
    /** `plan`, when present: the desktop marked these words a Plan only turn's plan (`livePlan`). */
    | { kind: 'text'; text: string; plan?: true }
    | { kind: 'step'; step: Step }
    | {
          kind: 'permission';
          request: string;
          title: string;
          detail: string;
          /** What "always, in this chat" would cover, as the button prints it; null when it is not offered. */
          remember: string | null;
          answer: Answer | null;
      }
    | { kind: 'error'; text: string }
    /** The campaigns a set-up turn made, for the person to pick the one this chat is about. */
    | { kind: 'pick'; campaignIds: string[] }
    /** The set-up chat became the conversation of the campaign named: said in the page, not kept by the desktop. */
    | { kind: 'attached'; title: string }
    /** The turn started a fresh session of the host, and why: said in the page, not kept by the desktop. */
    | { kind: 'fresh'; reason: 'host_changed' | 'not_found' }
    /** The person started the conversation afresh: said in the page, not kept by the desktop. */
    | { kind: 'afresh' }
    /**
     * A sentence the desktop said once, in its own words, not kept by the desktop. `folder` is the name of an ended Full
     * access folder the sentence points at, which its Open folder button opens; null for any other.
     */
    | { kind: 'notice'; text: string; folder: string | null }
    /** A line of the Feed, as the desktop kept it. */
    | { kind: 'line'; line: DesktopLineEntry }
    /** Where the Feed's catch-up read back fewer lines than were written while the window was closed. */
    | { kind: 'gap' };

export interface Chat {
    /** The conversation's id, the desktop's, which every turn of it carries. */
    id: string;
    title: string;
    campaignId: string | null;
    /** Whether it is the Feed, the person's main chat, which keeps its name and is never handed over. */
    main: boolean;
    /** The host the next turn runs on: a `DesktopHost.id`, or null when none that drives the chat was found. */
    host: string | null;
    /** The model the next turn names, as the host takes it; null for the host's own. */
    model: string | null;
    /** The effort the next turn names, where its model takes it; null for the host's own. */
    effort: DesktopEffort | null;
    /** Whether the person chose to type a model's full name, on a host that takes one. */
    typing: boolean;
    entries: Entry[];
    running: boolean;
    /** What is typed in the message box. */
    draft: string;
    /** The message sent while the turn runs, which waits for its end; null when none waits. */
    waiting: string | null;
    /** Whether the waiting message is held, not sent: given back while the box held other words. It is never sent. */
    held: boolean;
    /** Whether the person asked the running turn to stop. */
    stopping: boolean;
    /** The message the turn's end made the next turn, until the page has handed it to the desktop. */
    outbox: string | null;
    /** The running turn's words when they were the waiting message, given back if that turn fails. */
    resent: string | null;
    /** How many of the oldest entries the desktop let go to keep the conversation within its size. */
    dropped: number;
    /** Whether the person asked to remove the conversation and has not yet said again. */
    removing: boolean;
    /** The name typed while the person renames the conversation; null when no rename is open. */
    renaming: string | null;
    /**
     * The conversation's access mode as the desktop holds it: from its record, from `chat.setMode`'s answer and from
     * `mode` events, never from what the page asked for. The line that says Full access is on is drawn from this.
     */
    mode: DesktopChatMode;
    /**
     * What the person did with the chat's plan card in this page: approving it (the desktop asked, no answer yet),
     * approved, or kept planning; null when they did neither. Never kept: a reopened page reads the desktop's mark alone.
     */
    plan: 'approving' | 'approved' | 'kept' | null;
    /**
     * The host the chat's last turn was sent to, as the desktop kept it (`chosen`); null before any. A plan is the answer
     * of that host's session alone: another host's session never saw it (`livePlan`).
     */
    turnHost: string | null;
}

/** How much of a set-up chat's first message names it, as the desktop names it. */
const titleLength = 60;

/**
 * What a title never shows, as the desktop drops it: every control and format character, the two separators, and
 * every default-ignorable code point.
 */
export const unseen = /[\p{Cc}\p{Cf}\p{Default_Ignorable_Code_Point}\u2028\u2029]/gu;

/** A set-up chat's title from the person's first message, as the desktop names it: its first sixty characters, on one line. */
function titleOf(text: string): string {
    const characters = Array.from(text.replace(/\s+/g, ' ').replace(unseen, '').trim());
    return characters.length === 0 ? 'New chat' : characters.slice(0, titleLength).join('');
}

export function newChat(id: string, host: string | null, campaign?: { id: string; name: string }): Chat {
    return {
        id,
        title: campaign ? campaign.name : 'New chat',
        campaignId: campaign?.id ?? null,
        main: false,
        host,
        model: null,
        effort: null,
        typing: false,
        entries: [],
        running: false,
        draft: '',
        waiting: null,
        held: false,
        stopping: false,
        outbox: null,
        resent: null,
        dropped: 0,
        removing: false,
        renaming: null,
        mode: 'ask_first',
        plan: null,
        turnHost: null,
    };
}

/** What the desktop kept of a thing the page drew, as the page draws it; a kept card waits for nothing. */
function entryOf(entry: DesktopKeptEntry): Entry {
    switch (entry.kind) {
        case 'step':
            return {
                kind: 'step',
                step: entry.detail === undefined ? { text: entry.text } : { text: entry.text, detail: entry.detail },
            };
        case 'permission':
            return {
                kind: 'permission',
                request: '',
                title: entry.title,
                detail: entry.detail,
                remember: null,
                answer: entry.answer ?? 'gone',
            };
        case 'pick':
            return { kind: 'pick', campaignIds: [...entry.campaignIds] };
        case 'line':
            return { kind: 'line', line: { ...entry } };
        case 'gap':
            return { kind: 'gap' };
        case 'text':
            return entry.plan === true
                ? { kind: 'text', text: entry.text, plan: true }
                : { kind: 'text', text: entry.text };
        default:
            return { kind: entry.kind, text: entry.text };
    }
}

/** A conversation the desktop kept, opened: what the page drew before, and no turn running. */
export function fromRecord(record: DesktopConversationRecord, host: string | null): Chat {
    const chosen = record.chosen;
    return {
        ...newChat(record.id, chosen?.host ?? host),
        model: chosen?.model ?? null,
        effort: chosen?.effort ?? null,
        title: record.title,
        campaignId: record.campaignId,
        main: record.main === true,
        entries: record.entries.map(entryOf),
        dropped: record.dropped,
        // A turn of it runs: a page loaded again shows it running, with Stop, and its events follow.
        running: record.running,
        // A desktop of an earlier version keeps no mode: its chats run as Ask first.
        mode: record.mode ?? 'ask_first',
        turnHost: chosen?.host ?? null,
    };
}

/** What the plan card sends, as the person's message, once the chat is in Ask first. */
export const planGo = 'Go ahead with the plan in your last answer.';

/** The plan card's line on Claude Code: what Approve and run does. */
export const planLine =
    'Approve and run leaves Plan only for Ask first and tells the AI to go ahead; whatever Ask first asks about still comes to you as a card.';

/** The plan card's line on Codex, in place of `planLine`: Ask first runs Codex read-only too. */
export const codexPlanLine =
    'On Codex the approved plan still only reads: every Codex turn here is read-only. To have it carried out, choose Claude Code for this chat.';

/** The plan card's line once it was approved in this page. */
export const planApproved = 'Approved: the chat is in Ask first.';

/** The plan card's line, with no buttons, while the chat is on another host than the one that made the plan. */
export const planOtherHost =
    'This plan came from another AI; the one this chat is on now never saw it. Ask for the plan again to approve it.';

/** Why the plan's go was not sent: the chat was in another mode by the time it was approved, as the desktop says it. */
export const planModeMoved = "The chat's mode changed before the plan was approved, so nothing was sent.";

/** The words with every plan's mark taken off; the same entries when none had one. */
function unplanned(entries: Entry[]): Entry[] {
    if (!entries.some((each) => each.kind === 'text' && each.plan === true)) return entries;
    return entries.map((each) =>
        each.kind === 'text' && each.plan === true ? { kind: 'text', text: each.text } : each,
    );
}

/**
 * The chat in the mode the desktop says. Leaving Plan only ends its plan's card, as the desktop ends it, unless the
 * person's own approval of it is what leaves Plan only: that card stays, inert, saying it was approved.
 */
export function moded(chat: Chat, mode: DesktopChatMode): Chat {
    if (mode === 'plan_only' || chat.plan === 'approving' || chat.plan === 'approved') return { ...chat, mode };
    return { ...chat, mode, entries: unplanned(chat.entries), plan: null };
}

/**
 * Whether the chat's plan card may be approved now, with the go sent to `host`: its last entry is a plan the desktop
 * marked, the chat is still in Plan only, no turn runs, nothing waits to be sent, it is not the Feed, the person has
 * neither approved it nor kept planning, and `host` is the host that made it. Anything after the plan, a message, an
 * error or a note, makes it a plan of the past; another host makes it one that host never saw (`planElsewhere`).
 */
export function livePlan(chat: Chat, host: string | null): boolean {
    return planStands(chat) && host !== null && host === chat.turnHost;
}

/** Whether the chat's plan would be live but `host` is not the host that made it: the card says to ask for it again. */
export function planElsewhere(chat: Chat, host: string | null): boolean {
    return planStands(chat) && host !== null && host !== chat.turnHost;
}

/** `livePlan` on whichever host. */
function planStands(chat: Chat): boolean {
    const last = chat.entries.at(-1);
    return (
        last?.kind === 'text' &&
        last.plan === true &&
        chat.mode === 'plan_only' &&
        !chat.running &&
        chat.waiting === null &&
        !chat.main &&
        chat.plan === null
    );
}

/** What the line under the composer says while the desktop holds the chat in Full access. */
export const fullAccessLine = 'Full access: the AI runs commands without asking, except those Jason cannot read.';

/** What the line under the composer says while the desktop holds the chat in Auto-edit. */
export const autoEditLine =
    "Auto-edit: the AI creates, moves, copies and removes files in the chat's own folder without asking; everything else is as in Ask first.";

/**
 * What the line under the composer says while the chat's host is Codex on the person's sign-in: every Codex turn leaves
 * the person's `config.toml` unread, so none of its MCP servers reach the chat, and neither does a provider or a profile
 * named there.
 */
export const codexConfigLine =
    'The chat does not read your Codex config.toml, so a provider or profile set there is not used; connect a key on the Connect card.';

/**
 * What the line under the composer says while the chat's host is Codex, on the person's sign-in or on the OpenAI key,
 * system by system as it was seen. On Windows the desktop's check has shown Codex's elevated sandbox keeping the run
 * file from a command; on macOS and Linux nobody has looked, and a read-only command reads what the person can, so the
 * line there names the run token among what it can read. Neither says anything of the network or of acting for the
 * person, which nobody has seen; "read-only" is the mode the desktop runs Codex in.
 */
export const codexReachWindows =
    'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, but not the key Jason’s own commands use, its run token. Codex runs it read-only.';
export const codexReachElsewhere =
    'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, and the key Jason’s own commands use, its run token. Codex runs it read-only.';

/** Pure: the line for the system the desktop says it runs on; one that says none is told the line that claims nothing. */
export function codexReachLine(platform: string | undefined): string {
    return platform === 'win32' ? codexReachWindows : codexReachElsewhere;
}

/** The hosts whose turns run on Codex: the person's sign-in, and the OpenAI key. */
export const codexHosts: ReadonlySet<string> = new Set(['codex', 'codex-api-key']);

/** Why Full access cannot be chosen on a host a desktop of an earlier version listed, which offers no modes. */
export const olderDesktop = 'This version of Jason Desktop does not offer Full access.';

/** Why Auto-edit cannot be chosen on a host a desktop of an earlier version listed. */
export const olderDesktopAutoEdit = 'This version of Jason Desktop does not offer Auto-edit.';

/** The modes as the menu beside the composer names them, in its order. */
const modeWords: Readonly<Record<DesktopChatMode, string>> = {
    ask_first: 'Ask first',
    plan_only: 'Plan only',
    auto_edit: 'Auto-edit',
    full_access: 'Full access',
};

/** Why the desktop refuses Full access for the Feed, in its words. */
const feedFullAccess =
    'Full access is not offered for the Feed: its turns are handed lines that others and providers wrote without you asking for them.';

/** Why the desktop refuses Auto-edit on Codex, in its words; with no host the menu says it too. */
const codexAutoEdit =
    'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';

/** Why the desktop refuses Auto-edit for the Feed, in its words. */
const feedAutoEdit =
    'Auto-edit is not offered for the Feed: its turns are handed lines that others and providers wrote without you asking for them.';

/**
 * The menu's choices on the host the next turn runs on, narrow to wide after Ask first: each mode, and why it cannot be
 * chosen there, or null. Full access is disabled with the desktop's own reason: on Codex, or a Claude Code too old for
 * it; with no host, as on Codex; and for the Feed, in the order the desktop refuses it. Auto-edit is disabled the same
 * way, on Codex and for the Feed; it has no version floor.
 */
export function modeChoices(
    host: DesktopHost | null,
    feed = false,
): { mode: DesktopChatMode; label: string; disabled: string | null }[] {
    const full =
        host === null ? 'Full access runs on Claude Code only.' : (host.fullAccess ?? (feed ? feedFullAccess : null));
    const edit = host === null ? codexAutoEdit : (host.autoEdit ?? (feed ? feedAutoEdit : null));
    return (Object.keys(modeWords) as DesktopChatMode[]).map((mode) => ({
        mode,
        label: modeWords[mode],
        disabled: mode === 'full_access' ? full : mode === 'auto_edit' ? edit : null,
    }));
}

/**
 * The hosts as the page reads them: a desktop of an earlier version, whose runtime serves this page, lists none of the
 * models a host offers, and such a host offers none here and takes no typed name.
 */
export function withModels(hosts: readonly DesktopHost[]): DesktopHost[] {
    return hosts.map((host) => {
        const given = host as Partial<
            Pick<DesktopHost, 'models' | 'typedModel' | 'modelsUnread' | 'fullAccess' | 'autoEdit'>
        >;
        return Array.isArray(given.models) &&
            typeof given.typedModel === 'boolean' &&
            given.modelsUnread !== undefined &&
            given.fullAccess !== undefined &&
            given.autoEdit !== undefined
            ? host
            : {
                  ...host,
                  models: Array.isArray(given.models) ? given.models : [],
                  typedModel: given.typedModel === true,
                  modelsUnread: typeof given.modelsUnread === 'string' ? given.modelsUnread : null,
                  fullAccess:
                      typeof given.fullAccess === 'string' || given.fullAccess === null
                          ? given.fullAccess
                          : olderDesktop,
                  autoEdit:
                      typeof given.autoEdit === 'string' || given.autoEdit === null
                          ? given.autoEdit
                          : olderDesktopAutoEdit,
              };
    });
}

/** What the model menu's last option stands for: the person types a model's full name. No model is named so. */
export const anotherModel = '-another';

/** Another host: its own model and effort, until the person chooses others. */
export const hostChosen = (chat: Chat, host: string): Chat => ({
    ...chat,
    host,
    model: null,
    effort: null,
    typing: false,
});

/**
 * The chat as it stands on the host its next turn runs on: as it is on the host it names; on another, that host's own
 * model and effort, so a model is never sent to a host it was not chosen on; one that names no host yet takes it.
 */
export const onHost = (chat: Chat, host: string): Chat =>
    chat.host === host ? chat : chat.host === null ? { ...chat, host } : hostChosen(chat, host);

/** The effort chosen kept when the model takes it too, else the host's own. */
const effortFor = (effort: DesktopEffort | null, offer: DesktopModel | undefined): DesktopEffort | null =>
    effort !== null && offer?.efforts.includes(effort) ? effort : null;

/** A choice in the model menu: the host's own (''), a model the host offers, or `anotherModel` to type one. */
export function modelChosen(chat: Chat, host: DesktopHost, value: string): Chat {
    if (value === '') return { ...chat, host: host.id, model: null, effort: null, typing: false };
    if (value === anotherModel) return { ...chat, host: host.id, model: null, effort: null, typing: true };
    return {
        ...chat,
        host: host.id,
        model: value,
        effort: effortFor(
            chat.effort,
            host.models.find((each) => each.id === value),
        ),
        typing: false,
    };
}

/** A model's full name as the person types it; the turn takes it trimmed. */
export const modelTyped = (chat: Chat, text: string): Chat => ({
    ...chat,
    model: text === '' ? null : text,
    effort: null,
});

/** A choice in the effort menu: the host's own ('') or one of the levels. */
export const effortChosen = (chat: Chat, value: DesktopEffort | ''): Chat => ({
    ...chat,
    effort: value === '' ? null : value,
});

/**
 * What the box shows of the chat's model on a host: the host's model it names, if any; whether the person types one;
 * and a model the host no longer offers and takes no typed name for, which is shown as kept and sent as it is.
 */
export function modelView(
    chat: Chat,
    host: DesktopHost,
): { offer: DesktopModel | null; typing: boolean; kept: string | null } {
    const name = chat.model?.trim() ?? '';
    const offer = host.models.find((each) => each.id === name) ?? null;
    const typing = host.typedModel && (chat.typing || (name !== '' && offer === null));
    return { offer, typing, kept: !host.typedModel && name !== '' && offer === null ? name : null };
}

/** What a turn is sent with: the host, the model when one is named, and the effort when that model takes it. */
export function turnOptions(chat: Chat, host: DesktopHost): DesktopChatOptions {
    const model = chat.model?.trim() ?? '';
    const offer = host.models.find((each) => each.id === model);
    // A model the host no longer offers, and takes no typed name for, is not one the desktop takes: the host's own goes.
    if (model === '' || (offer === undefined && !host.typedModel)) return { host: host.id };
    const effort = effortFor(chat.effort, offer);
    return effort === null ? { host: host.id, model } : { host: host.id, model, effort };
}

/** Whether the send waits: "Another model" is chosen and no name is typed yet. */
export function sendHeld(chat: Chat, host: DesktopHost): boolean {
    return modelView(chat, host).typing && (chat.model?.trim() ?? '') === '';
}

/** The person picked the campaign a set-up chat is about, and the desktop made it that campaign's conversation. */
export const pickedFor = (chat: Chat, conversation: DesktopConversation): Chat => ({
    ...chat,
    campaignId: conversation.campaignId,
    title: conversation.title,
});

/**
 * The person's words go into the chat and its turn starts; the first words of a chat give it its title. `resent` says
 * the words were the waiting message, which the turn gives back if it fails.
 */
export function said(chat: Chat, text: string, resent = false): Chat {
    const first = chat.campaignId === null && !chat.main && !chat.entries.some((each) => each.kind === 'user');
    return {
        ...chat,
        title: first ? titleOf(text) : chat.title,
        entries: [...chat.entries, { kind: 'user', text }],
        running: true,
        resent: resent ? text : null,
    };
}

/** What an event of the desktop's does to the chat it belongs to. */
export function received(chat: Chat, event: DesktopChatEvent): Chat {
    switch (event.kind) {
        case 'step': {
            const step: Step =
                event.detail === undefined ? { text: event.text } : { text: event.text, detail: event.detail };
            return { ...chat, entries: [...chat.entries, { kind: 'step', step }] };
        }
        case 'text':
            return { ...chat, entries: [...chat.entries, { kind: 'text', text: event.text }] };
        case 'permission':
            return {
                ...chat,
                entries: [
                    ...chat.entries,
                    {
                        kind: 'permission',
                        request: event.request,
                        title: event.title,
                        detail: event.detail,
                        remember: event.remember,
                        answer: null,
                    },
                ],
            };
        case 'permission_gone':
            return answered(chat, event.request, 'gone');
        case 'attached':
            return {
                ...chat,
                campaignId: event.campaignId,
                title: event.title,
                entries: [...chat.entries, { kind: 'attached', title: event.title }],
            };
        case 'fresh':
            return { ...chat, entries: [...chat.entries, { kind: 'fresh', reason: event.reason }] };
        case 'pick':
            return { ...chat, entries: [...chat.entries, { kind: 'pick', campaignIds: [...event.campaignIds] }] };
        case 'notice':
            return {
                ...chat,
                entries: [...chat.entries, { kind: 'notice', text: event.text, folder: event.folder ?? null }],
            };
        case 'renamed':
            return { ...chat, title: event.title };
        case 'mode':
            return moded(chat, event.mode);
        // A line never starts a turn: it goes into the thread, and nothing else of the chat changes.
        case 'line': {
            const last = chat.entries.at(-1);
            const entry: Entry = { kind: 'line', line: { ...event.line } };
            if (event.folded && last?.kind === 'line')
                return { ...chat, entries: [...chat.entries.slice(0, -1), entry] };
            return { ...chat, entries: [...chat.entries, entry] };
        }
        case 'gap':
            return { ...chat, entries: [...chat.entries, { kind: 'gap' }] };
        case 'done': {
            // A turn ended: every earlier plan is a plan of the past, and this turn's last words are its plan when the
            // desktop says so. The Feed has none.
            const words = unplanned(chat.entries);
            const last = words.at(-1);
            const entries =
                event.plan === true && !chat.main && last?.kind === 'text'
                    ? [...words.slice(0, -1), { kind: 'text' as const, text: last.text, plan: true as const }]
                    : words;
            const over = ended({ ...chat, entries, plan: null });
            if (event.error === null)
                return chat.waiting !== null && !chat.held ? { ...over, waiting: null, outbox: chat.waiting } : over;
            return givenBackAfterFailure(chat, {
                ...over,
                entries: [...over.entries, { kind: 'error', text: event.error }],
            });
        }
    }
}

/**
 * Words given back to the person: into the box when it is empty, else held in sight as not sent while nothing else
 * is. When both places are taken the words are the failed turn's own, shown in the thread, and stay there alone.
 */
function givenBack(chat: Chat, text: string): Chat {
    if (!chat.draft.trim()) return { ...chat, draft: text };
    if (chat.waiting === null) return { ...chat, waiting: text, held: true };
    return chat;
}

/** The waiting message, when one waits for the turn's end, taken out of waiting and given back. */
function released(chat: Chat): Chat {
    if (chat.waiting === null || chat.held) return chat;
    return givenBack({ ...chat, waiting: null }, chat.waiting);
}

/** A failed turn gives back the waiting message first, which is nowhere else, then the turn's own words if resent. */
function givenBackAfterFailure(before: Chat, after: Chat): Chat {
    const back = released({ ...after, waiting: before.waiting, held: before.held });
    return before.resent === null ? back : givenBack(back, before.resent);
}

/** What is typed in the box. */
export const typed = (chat: Chat, text: string): Chat => ({ ...chat, draft: text });

/**
 * The box's text sent while the turn runs: it waits for the turn's end and the box is cleared. While one already
 * waits, or is held, or the person asked the turn to stop, nothing changes and the text stays in the box, so a second
 * message replaces the first only by Replace.
 */
export function kept(chat: Chat): Chat {
    const text = chat.draft.trim();
    if (!chat.running || chat.stopping || chat.waiting !== null || !text) return chat;
    return { ...chat, waiting: text, draft: '' };
}

/** The box's text takes the waiting message's place, which goes into the box: the two are swapped, never merged. */
export function replaced(chat: Chat): Chat {
    const text = chat.draft.trim();
    if (chat.waiting === null || !text) return chat;
    return { ...chat, waiting: text, draft: chat.waiting };
}

/** The waiting message taken back into the box; only into an empty one, so no word typed there is overwritten. */
export function takenBack(chat: Chat): Chat {
    if (chat.waiting === null || chat.draft.trim()) return chat;
    return { ...chat, waiting: null, held: false, draft: chat.waiting };
}

/**
 * The person asked the running turn to stop. A stopped turn ends with the same `done` as a good one, so the waiting
 * message is given back now, before the desktop is asked, and the end finds nothing to send.
 */
export const stopAsked = (chat: Chat): Chat => (chat.running ? released({ ...chat, stopping: true }) : chat);

/** The page handed the turn's end's message to the desktop. */
export const handed = (chat: Chat): Chat => ({ ...chat, outbox: null });

/** A turn that ended: every request it still held is gone with it. */
function ended(chat: Chat): Chat {
    return {
        ...chat,
        running: false,
        stopping: false,
        resent: null,
        entries: chat.entries.map((each) =>
            each.kind === 'permission' && each.answer === null ? { ...each, answer: 'gone' } : each,
        ),
    };
}

/**
 * A request answered, or no longer held: the first of the two stands, so an answer the person gave stays shown when
 * the desktop says afterwards that the request no longer waits.
 */
export function answered(chat: Chat, request: string, answer: Answer): Chat {
    return {
        ...chat,
        entries: chat.entries.map((each) =>
            each.kind === 'permission' && each.request === request && each.answer === null ? { ...each, answer } : each,
        ),
    };
}

/** A request the desktop refused an answer to: whatever the page showed, it no longer waits. */
export const lost = (chat: Chat, request: string): Chat => ({
    ...chat,
    entries: chat.entries.map((each) =>
        each.kind === 'permission' && each.request === request ? { ...each, answer: 'gone' } : each,
    ),
});

/**
 * A turn the desktop refused to start, or a stop it refused: the chat says why, in the desktop's words, and waits for
 * the person again, with what waited given back as a failed turn gives it back.
 */
export function failed(chat: Chat, error: string): Chat {
    const over = ended(chat);
    return givenBackAfterFailure(chat, {
        ...over,
        outbox: null,
        entries: [...over.entries, { kind: 'error', text: error }],
    });
}

/** Something the desktop refused, said in the chat in its words; a turn that runs goes on. */
export const told = (chat: Chat, error: string): Chat => ({
    ...chat,
    entries: [...chat.entries, { kind: 'error', text: error }],
});

/** The person asked to start the conversation afresh, and the desktop did. */
export const afresh = (chat: Chat): Chat => ({ ...chat, entries: [...chat.entries, { kind: 'afresh' }] });

/** The steps between two messages fold into one line; the last group stays open while the turn runs. */
export type Shown = { kind: 'steps'; steps: Step[]; open: boolean } | Exclude<Entry, { kind: 'step' }>;

export function shown(chat: Chat): Shown[] {
    const out: Shown[] = [];
    let steps: Step[] = [];
    const flush = (open: boolean) => {
        if (steps.length) out.push({ kind: 'steps', steps, open });
        steps = [];
    };
    for (const entry of chat.entries) {
        if (entry.kind === 'step') {
            steps.push(entry.step);
            continue;
        }
        flush(false);
        out.push(entry);
    }
    flush(chat.running);
    return out;
}
