// Pure: the cards the chat's AI asks for. The AI ends a reply with one fenced block whose info string is `card`, holding
// one JSON object with a `kind`; the page takes every such block out of the words it draws, and draws the last block of
// a turn as a card. Nine kinds, a closed list, each held to its schema: every member's type and length, own keys only,
// nothing the schema does not name. A block is read once, by `JSON.parse` alone, and never mended, cut or guessed: a
// block that does not read draws one line in the desktop's words, and what it held is never shown.
//
// A block is found by the subset's own reading of fences (`fences`, `markdown.ts`), so the two never disagree about
// where a block ends; this module adds only that a card's fence opens in the line's first column with the info string
// `card` and nothing after it but spaces. A fence indented, inside a list item or a quote, inside another fence, or with
// any other info string is the AI's code, drawn as code.

import { fences } from './markdown';
import { locksChoice, unseen, type Chat } from './chat-model';

/**
 * What a card's text may not hold: what the chat's titles leave out (`unseen`), and also a character that draws as
 * nothing or as no character: the blank Braille pattern, half a surrogate pair, a private-use character.
 */
const cardUnseen = new RegExp(`${unseen.source.slice(0, -1)}\\p{Cs}\\p{Co}\\u2800]`, 'u');

/** The kinds a card may be, in the order the chat's instructions list them. */
export const cardKinds = [
    'choice',
    'people_list',
    'agent_status',
    'needs',
    'accounts',
    'ai_setup',
    'reply_setup',
    'plan',
    'own_list',
] as const;
export type CardKind = (typeof cardKinds)[number];

/** Why a card block did not draw, as the desktop's log names it. */
export type CardFailure = 'kind_unknown' | 'too_long' | 'malformed' | 'schema';

/** Where a card stands: an agent's chat, a set-up chat about no campaign yet, or the Feed. */
export type CardPlace = 'agent' | 'setup' | 'feed';

/** The most characters (code points) the text between a card's fences may hold; checked before it is parsed. */
export const blockLimit = 16_000;
/** How deep a card's JSON may nest, its own object counted as level one. */
export const depthLimit = 4;

/** The shape of a campaign's id, as the desktop holds one. */
const campaignShape = /^cmp_[0-9A-Za-z]{1,64}$/;

/**
 * The words of the chat's controls that act: a button that approves, rejects, allows or refuses a permission, starts,
 * resumes, pauses, stops, sends, saves, removes, renames, answers, connects, sets or changes the chat's mode or what it
 * runs on, or approves and runs a plan. A choice's label that is one of them, folded as `foldLabel` folds, is refused: a
 * click on it could be taken for that act. The chat's instructions name every one of them. A test reads the chat's
 * buttons and fails while a word of theirs is in none of this list, `passiveWords` and `nameWords`, so a new button is
 * classed by a person. Names a control draws from a value (a host's, a model's, a campaign's) are not held.
 */
export const controlWords: readonly string[] = [
    'Approve',
    'Reject',
    'Approve and run',
    'Approving',
    'Keep planning',
    'Allow',
    'Allow for this chat',
    "Don't allow",
    'Answer',
    'Start',
    'Resume',
    'Pause',
    'Stop',
    'Send',
    'Send when the turn ends',
    'Replace',
    'Save',
    'Save name',
    'Remove',
    'Remove it',
    'Rename',
    'Start afresh',
    'Take back',
    'Connect',
    'Reconnect',
    'Disconnect',
    'Sign in',
    'Add account',
    'Make the link',
    'Set',
    'Keep',
    'Install',
    'Update',
    'Remove the key',
    'Use the public server',
    'Configure',
    'Reload',
    'Unblock',
    'Delete',
    'Delete anyway',
    'Ask first',
    'Plan only',
    'Auto-edit',
    'Full access',
    'Default effort',
    'Add to this agent',
];

/**
 * The words of the chat's controls that only close, open, show or move, and act on nothing: a choice may use them as
 * labels. Held beside `controlWords` so that every word a button of the chat draws is in exactly one of the three lists.
 */
export const passiveWords: readonly string[] = [
    'Cancel',
    'Not now',
    'Close',
    'Details',
    'Edit',
    'Edit message',
    'Something else',
    'Show more',
    'Show the whole message',
    'Keep it',
    'Check again',
    'Check now',
    'Limits',
    'Open folder',
    'Open the shared mailbox page',
    'Needs you',
    'You chose this',
    // The chips (`chips.ts`): a click sends the chip's words as the person's message, and acts on nothing.
    'What is waiting for me?',
    'How are my agents doing?',
    'Help me start a new agent.',
    'Who should this agent reach?',
    'Show me the plan',
    'What can you do here?',
    'Show me the people',
    'What happens when it starts?',
    'How is it going?',
    // The data cards' controls: a read again, which decides nothing, and the places they open.
    'Show now',
    'Refresh',
    'Show what waits',
    'Show them',
    'Open',
    'More in Needs you',
    'Open Accounts',
    'Open Connect',
    'People',
    'Inbox',
    'Results',
    'Settings',
    // The plan card's: a draft opened or folded in place.
    'Show the draft',
    'Hide the draft',
];

/**
 * The words of the chat's buttons that are only a name: of a channel, a provider, a plugin or an AI. Under the AI's own
 * question a bare name reads as an answer, never as the window's act, so a choice may use it as a label.
 */
export const nameWords: readonly string[] = ['Gmail', 'Outlook', 'LinkedIn'];

/**
 * A label as a person reads it, for comparing two: its compatible forms made one (a full-width letter is the letter),
 * every apostrophe the one apostrophe, its runs of white space one space, no stop or ellipsis at its end, and no case.
 * Letters of another alphabet that look the same are not made one.
 */
export const foldLabel = (label: string): string =>
    // The apostrophes first: the compatible form of U+00B4 is a space and a combining mark.
    label
        .replace(/[\u2018\u2019\u201b\u2032\u02bc\u02b9\u0060\u00b4]/gu, "'")
        .normalize('NFKC')
        .replace(/[\u2018\u2019\u201b\u2032\u02bc\u02b9\u0060\u00b4]/gu, "'")
        .replace(/\s+/gu, ' ')
        .trim()
        .replace(/[\s.!?:]+$/u, '')
        .toLowerCase();

/** The control words, folded once. */
const controlFolded = new Set(controlWords.map(foldLabel));

/** The counts of `campaign.summary` a people list may keep to (`docs/work-execution.md`). */
export const standings = ['waiting', 'failed', 'working', 'new', 'idle', 'sequence', 'out', 'removed'] as const;
export type Standing = (typeof standings)[number];

/** The channels a plan's steps may name: the ones `message.send` takes. */
export const planChannels = ['email', 'linkedin'] as const;
export type PlanChannel = (typeof planChannels)[number];

/** The fields a list's column may fill, beside `custom:<name>`: a contact's own fields and its channels. */
export const listFields = [
    'first_name',
    'last_name',
    'company',
    'title',
    'time_zone',
    'email',
    'phone',
    'linkedin',
] as const;
/** What a list's rows are matched by, so a person already held is found and a second press adds nobody twice. */
export const listMatches = ['email', 'linkedin'] as const;
export type ListMatch = (typeof listMatches)[number];
/** The most rows a list card holds: a ceiling of its own, whatever the block's cap would let through. */
export const listRowLimit = 200;
/**
 * The keys the runtime itself writes into a contact's custom fields, when a search lands a person (its own record of
 * them, where they are, their company's particulars): a list never writes one of them.
 */
export const reservedCustomKeys: readonly string[] = [
    'location',
    'company_domain',
    'company_size',
    'company_industry',
    'provider_record',
];

export interface CardOption {
    label: string;
    detail?: string;
}

export interface ChoiceCard {
    kind: 'choice';
    question: string;
    options: CardOption[];
}

export interface PeopleListCard {
    kind: 'people_list';
    campaign?: string;
    search?: string;
    standing?: Standing;
}

export interface AgentStatusCard {
    kind: 'agent_status';
    campaign?: string;
}

export interface NeedsCard {
    kind: 'needs';
    campaign?: string;
}

export interface AccountsCard {
    kind: 'accounts';
}

/** A card that only leads to a place of the window: Connect, where the AI hosts and the model keys are. */
export interface AiSetupCard {
    kind: 'ai_setup';
}

/** A card that only leads to a place of the window: the Accounts page, where Reply's sign-in and its accounts are. */
export interface ReplySetupCard {
    kind: 'reply_setup';
}

export interface PlanStep {
    when?: string;
    channel: PlanChannel;
    subject?: string;
    body?: string;
}

export interface PlanCard {
    kind: 'plan';
    title: string;
    who?: string;
    offer?: string;
    sender?: string;
    goal?: string;
    channels?: PlanChannel[];
    steps?: PlanStep[];
    rules?: string[];
    manual?: string[];
    question?: string;
    options?: CardOption[];
}

/** One column of a list: the field it fills, and the person's own header for it when the list had one. */
export interface ListColumn {
    field: string;
    from?: string;
}

/**
 * A list the person gave in the chat, as the AI read it, for the person's own press: what each column fills, the
 * person's columns it left out, and the rows, each cell as given and `""` where nothing was.
 */
export interface OwnListCard {
    kind: 'own_list';
    campaign?: string;
    match_by: ListMatch;
    columns: ListColumn[];
    left_out: string[];
    rows: string[][];
}

export type Card =
    | ChoiceCard
    | PeopleListCard
    | AgentStatusCard
    | NeedsCard
    | AccountsCard
    | AiSetupCard
    | ReplySetupCard
    | PlanCard
    | OwnListCard;

/** A card block as a text holds it: the text between its fences, and whether a fence closed it. */
export interface CardBlock {
    body: string;
    closed: boolean;
}

/** What a card block reads as where it stands. */
export type Reading =
    | { state: 'card'; card: Card }
    /** In an agent's chat, a card about another campaign: it is drawn as a frame that says so, and reads nothing. */
    | { state: 'elsewhere'; kind: CardKind }
    | { state: 'failed'; kind: CardKind | 'unknown'; failure: CardFailure }
    /** A block still open while its turn runs: nothing is drawn yet. */
    | { state: 'pending' };

/** How many characters a text holds, in code points: a pair of surrogates is one. */
const characters = (text: string): number => text.replace(/[\uD800-\uDBFF][\uDC00-\uDFFF]/g, '_').length;

/** Whether a fence opens a card block: in the first column, with the info string `card` and only spaces after it. */
const opensCard = (indent: number, info: string): boolean => indent === 0 && /^card *$/.test(info);

/** A text's words with every card block taken out, each block's place left as one empty line, and the blocks in order. */
export function splitCards(text: string): { words: string; blocks: CardBlock[] } {
    const cards = fences(text).filter((fence) => opensCard(fence.indent, fence.info));
    if (cards.length === 0) return { words: text, blocks: [] };
    const lines = text.replace(/\r\n?/g, '\n').split('\n');
    const kept: string[] = [];
    const blocks: CardBlock[] = [];
    let at = 0;
    for (const fence of cards) {
        kept.push(...lines.slice(at, fence.open), '');
        const end = fence.close ?? lines.length;
        blocks.push({ body: lines.slice(fence.open + 1, end).join('\n'), closed: fence.close !== null });
        at = end + 1;
    }
    kept.push(...lines.slice(at));
    return { words: kept.join('\n'), blocks };
}

/**
 * A text's words with its last card block alone taken out, its place left as one empty line, and that block; null for a
 * text with none. Every earlier block stays in the words, as written.
 */
export function lastCardOf(text: string): { words: string; block: CardBlock } | null {
    const fence = fences(text)
        .filter((each) => opensCard(each.indent, each.info))
        .at(-1);
    if (fence === undefined) return null;
    const lines = text.replace(/\r\n?/g, '\n').split('\n');
    const end = fence.close ?? lines.length;
    return {
        words: [...lines.slice(0, fence.open), '', ...lines.slice(end + 1)].join('\n'),
        block: { body: lines.slice(fence.open + 1, end).join('\n'), closed: fence.close !== null },
    };
}

/** An entry of a chat as the layout reads it: only a `text` entry's words are read, and a plan's mark. */
export interface LaidEntry {
    kind: string;
    text?: string;
    plan?: boolean;
}

/** The card a turn draws: its last block, after the text entry that held it, and whether its turn is the chat's last. */
export interface PlacedCard extends CardBlock {
    lastTurn: boolean;
}

/**
 * A chat's entries as the thread draws them: by the index of each text entry that held a card block, its words with
 * every block taken out; and by the index of the text entry that held it, each turn's last block. A turn runs from a
 * person's message to the next one; a person's own words are never read for a card. A turn the desktop marked a plan
 * is left whole: what Approve and run tells the AI to carry out is its answer, so every block of it stays in its words,
 * but for a plan card its marked words end in, which the plan's own card takes out and draws inside it (`proposalOf`,
 * which has to read the block to know it is one).
 */
export function cardLayout(entries: readonly LaidEntry[]): {
    words: ReadonlyMap<number, string>;
    cards: ReadonlyMap<number, PlacedCard>;
} {
    const words = new Map<number, string>();
    const cards = new Map<number, PlacedCard>();
    let turnLast: { index: number; block: CardBlock } | null = null;
    let turnWords: number[] = [];
    let turnPlan = false;
    const close = (lastTurn: boolean) => {
        if (turnPlan) for (const index of turnWords) words.delete(index);
        else if (turnLast !== null) cards.set(turnLast.index, { ...turnLast.block, lastTurn });
        turnLast = null;
        turnWords = [];
        turnPlan = false;
    };
    entries.forEach((entry, index) => {
        if (entry.kind === 'user') {
            close(false);
            return;
        }
        if (entry.kind !== 'text' || typeof entry.text !== 'string') return;
        if (entry.plan === true) turnPlan = true;
        const split = splitCards(entry.text);
        if (split.blocks.length === 0) return;
        words.set(index, split.words);
        turnWords.push(index);
        turnLast = { index, block: split.blocks.at(-1)! };
    });
    close(true);
    return { words, cards };
}

/** How deep a value nests, an object or an array counting one level, read no deeper than one past `limit`. */
export function depthOf(value: unknown, limit = depthLimit): number {
    if (value === null || typeof value !== 'object') return 0;
    if (limit < 0) return 1;
    let deepest = 0;
    for (const each of Array.isArray(value) ? value : Object.values(value)) {
        deepest = Math.max(deepest, depthOf(each, limit - 1));
        if (deepest > limit) break;
    }
    return 1 + deepest;
}

/** Why a card was refused: thrown inside the reading of its members, caught once. */
class Refused extends Error {}
const refuse = (): never => {
    throw new Refused();
};

type Fields = Readonly<Record<string, unknown>>;

/** An own member of a parsed object, never one its prototype holds. */
const own = (object: Fields, key: string): unknown =>
    Object.prototype.hasOwnProperty.call(object, key) ? object[key] : undefined;

const isObject = (value: unknown): value is Fields =>
    value !== null && typeof value === 'object' && !Array.isArray(value);

/** An object whose own keys are all among `allowed`, `__proto__` never among them. */
function shaped(value: unknown, allowed: readonly string[]): Fields {
    if (!isObject(value)) return refuse();
    for (const key of Object.keys(value)) if (!allowed.includes(key)) refuse();
    return value;
}

/**
 * A string member, trimmed: required or not, non-empty once trimmed, at most `max` characters, and free of every
 * character a title drops; a draft's body alone may break lines.
 */
function text(object: Fields, key: string, max: number, required: true, lines?: boolean): string;
function text(object: Fields, key: string, max: number, required: false, lines?: boolean): string | undefined;
function text(object: Fields, key: string, max: number, required: boolean, lines = false): string | undefined {
    const value = own(object, key);
    if (value === undefined) return required ? refuse() : undefined;
    return trimmedText(value, max, lines);
}

function trimmedText(value: unknown, max: number, lines = false): string {
    if (typeof value !== 'string') return refuse();
    const trimmed = value.trim();
    if (trimmed === '' || characters(trimmed) > max) refuse();
    if ((lines ? trimmed.replace(/\n/g, '') : trimmed).search(cardUnseen) !== -1) refuse();
    return trimmed;
}

/** A list member of `min` to `max` items, each read by `item`; absent is no list. */
function list<T>(object: Fields, key: string, min: number, max: number, item: (value: unknown) => T): T[] | undefined {
    const value = own(object, key);
    if (value === undefined) return undefined;
    if (!Array.isArray(value) || value.length < min || value.length > max) return refuse();
    return value.map(item);
}

/** The options of a choice, or of a plan's question: two to five, each label unique and none a control's word. */
function options(object: Fields): CardOption[] {
    const read = list(object, 'options', 2, 5, (value): CardOption => {
        const option = shaped(value, ['label', 'detail']);
        const label = text(option, 'label', 80, true);
        const detail = text(option, 'detail', 100, false);
        if (controlFolded.has(foldLabel(label))) refuse();
        return detail === undefined ? { label } : { label, detail };
    });
    if (read === undefined) return refuse();
    if (new Set(read.map((option) => foldLabel(option.label))).size !== read.length) refuse();
    return read;
}

/** A campaign's id, when the card names one: of the desktop's shape. */
function campaign(object: Fields): string | undefined {
    const value = own(object, 'campaign');
    if (value === undefined) return undefined;
    if (typeof value !== 'string' || !campaignShape.test(value)) return refuse();
    return value;
}

function oneOf<T extends string>(value: unknown, allowed: readonly T[]): T {
    return typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : refuse();
}

/** Where each kind may stand (an agent's chat, a set-up chat, the Feed). */
const allowedIn: Readonly<Record<CardKind, readonly CardPlace[]>> = {
    choice: ['agent', 'setup', 'feed'],
    people_list: ['agent', 'setup', 'feed'],
    agent_status: ['agent', 'setup', 'feed'],
    needs: ['agent', 'setup', 'feed'],
    accounts: ['agent', 'setup', 'feed'],
    ai_setup: ['agent', 'setup', 'feed'],
    reply_setup: ['agent', 'setup', 'feed'],
    plan: ['agent', 'setup'],
    // A list is added to the agent whose conversation shows it, where what happened to it is read.
    own_list: ['agent'],
};

/** A kind's members, read; throws `Refused` for anything its schema does not hold. */
function cardOf(kind: CardKind, object: Fields, place: CardPlace): Card {
    switch (kind) {
        case 'choice': {
            const fields = shaped(object, ['kind', 'question', 'options']);
            return { kind, question: text(fields, 'question', 200, true), options: options(fields) };
        }
        case 'people_list': {
            const fields = shaped(object, ['kind', 'campaign', 'search', 'standing']);
            const id = campaign(fields);
            const search = text(fields, 'search', 100, false);
            const standing = own(fields, 'standing');
            // A standing is a count of one campaign: this chat's, or the one named.
            if (standing !== undefined && id === undefined && place !== 'agent') refuse();
            return {
                kind,
                ...(id === undefined ? {} : { campaign: id }),
                ...(search === undefined ? {} : { search }),
                ...(standing === undefined ? {} : { standing: oneOf(standing, standings) }),
            };
        }
        case 'agent_status': {
            const fields = shaped(object, ['kind', 'campaign']);
            const id = campaign(fields);
            if (id === undefined && place !== 'agent') refuse();
            return id === undefined ? { kind } : { kind, campaign: id };
        }
        case 'needs': {
            const fields = shaped(object, ['kind', 'campaign']);
            const id = campaign(fields);
            return id === undefined ? { kind } : { kind, campaign: id };
        }
        case 'accounts':
        case 'ai_setup':
        case 'reply_setup':
            // A card of its kind alone: it names no campaign and carries nothing of the AI's.
            shaped(object, ['kind']);
            return { kind };
        case 'plan':
            return planOf(object);
        case 'own_list':
            return listOf(object);
    }
}

/** A custom field's name, as the runtime's match keys take one. */
const customName = /^custom:([A-Za-z0-9_]{1,64})$/;

/** A list's cell: a string of at most 500 characters on one line, free of what draws as nothing; `""` is no value. */
function cell(value: unknown): string {
    if (typeof value !== 'string') return refuse();
    const trimmed = value.trim();
    if (characters(trimmed) > 500 || value.search(cardUnseen) !== -1) refuse();
    return trimmed;
}

function listOf(object: Fields): OwnListCard {
    const fields = shaped(object, ['kind', 'campaign', 'match_by', 'columns', 'left_out', 'rows']);
    const id = campaign(fields);
    const columns = list(fields, 'columns', 1, 12, (value): ListColumn => {
        const column = shaped(value, ['field', 'from']);
        const field = own(column, 'field');
        if (typeof field !== 'string') return refuse();
        const custom = customName.exec(field)?.[1];
        if (
            !(listFields as readonly string[]).includes(field) &&
            (custom === undefined || reservedCustomKeys.includes(custom))
        )
            refuse();
        const from = text(column, 'from', 80, false);
        return from === undefined ? { field } : { field, from };
    });
    if (columns === undefined || new Set(columns.map((column) => column.field)).size !== columns.length)
        return refuse();
    const matchBy = oneOf(own(fields, 'match_by'), listMatches);
    if (!columns.some((column) => column.field === matchBy)) refuse();
    const leftOut = list(fields, 'left_out', 0, 20, (value) => trimmedText(value, 80)) ?? [];
    const rows = list(fields, 'rows', 1, listRowLimit, (value) => {
        if (!Array.isArray(value) || value.length !== columns.length) return refuse();
        return value.map(cell);
    });
    if (rows === undefined) return refuse();
    return {
        kind: 'own_list',
        ...(id === undefined ? {} : { campaign: id }),
        match_by: matchBy,
        columns,
        left_out: leftOut,
        rows,
    };
}

function planOf(object: Fields): PlanCard {
    const fields = shaped(object, [
        'kind',
        'title',
        'who',
        'offer',
        'sender',
        'goal',
        'channels',
        'steps',
        'rules',
        'manual',
        'question',
        'options',
    ]);
    const channels = list(fields, 'channels', 1, 2, (value) => oneOf(value, planChannels));
    if (channels !== undefined && new Set(channels).size !== channels.length) refuse();
    const steps = list(fields, 'steps', 0, 8, (value): PlanStep => {
        const step = shaped(value, ['when', 'channel', 'subject', 'body']);
        const channel = oneOf(own(step, 'channel'), channels ?? []);
        const when = text(step, 'when', 30, false);
        const subject = text(step, 'subject', 120, false);
        const body = text(step, 'body', 1200, false, true);
        return {
            ...(when === undefined ? {} : { when }),
            channel,
            ...(subject === undefined ? {} : { subject }),
            ...(body === undefined ? {} : { body }),
        };
    });
    const line = (value: unknown) => trimmedText(value, 150);
    const rules = list(fields, 'rules', 0, 4, line);
    const manual = list(fields, 'manual', 0, 4, line);
    const question = text(fields, 'question', 200, false);
    const asked = own(fields, 'options') !== undefined;
    // A question and its answers come together, or neither does.
    if ((question === undefined) !== !asked) refuse();
    const written = {
        title: text(fields, 'title', 80, true),
        who: text(fields, 'who', 200, false),
        offer: text(fields, 'offer', 200, false),
        sender: text(fields, 'sender', 120, false),
        goal: text(fields, 'goal', 120, false),
    };
    return {
        kind: 'plan',
        ...Object.fromEntries(Object.entries(written).filter(([, value]) => value !== undefined)),
        ...(channels === undefined ? {} : { channels }),
        ...(steps === undefined ? {} : { steps }),
        ...(rules === undefined ? {} : { rules }),
        ...(manual === undefined ? {} : { manual }),
        ...(question === undefined ? {} : { question, options: options(fields) }),
    } as PlanCard;
}

/**
 * A card block read where it stands: `place` is the chat's kind, `campaignId` an agent's chat's campaign, and `ended`
 * whether the block's turn has ended. The order is the table's: an open block, the length before the parse, the parse,
 * the kind, the depth, the members, where the kind may stand, and last whether it names another campaign.
 */
export function readCard(
    block: CardBlock,
    where: { place: CardPlace; campaignId: string | null; ended: boolean },
): Reading {
    if (!block.closed)
        return where.ended ? { state: 'failed', kind: 'unknown', failure: 'malformed' } : { state: 'pending' };
    if (characters(block.body) > blockLimit) return { state: 'failed', kind: 'unknown', failure: 'too_long' };
    let parsed: unknown;
    try {
        parsed = JSON.parse(block.body);
    } catch {
        return { state: 'failed', kind: 'unknown', failure: 'malformed' };
    }
    if (!isObject(parsed)) return { state: 'failed', kind: 'unknown', failure: 'malformed' };
    const kind = own(parsed, 'kind');
    if (typeof kind !== 'string' || !(cardKinds as readonly string[]).includes(kind))
        return { state: 'failed', kind: 'unknown', failure: 'kind_unknown' };
    const known = kind as CardKind;
    const failed = { state: 'failed', kind: known, failure: 'schema' } as const;
    if (depthOf(parsed) > depthLimit) return failed;
    if (!allowedIn[known].includes(where.place)) return failed;
    let card: Card;
    try {
        card = cardOf(known, parsed, where.place);
    } catch (error) {
        if (error instanceof Refused) return failed;
        throw error;
    }
    const named = 'campaign' in card ? card.campaign : undefined;
    if (where.place === 'agent' && named !== undefined && named !== where.campaignId)
        return { state: 'elsewhere', kind: known };
    return { state: 'card', card };
}

/** The place a chat is: the Feed, a set-up chat about no campaign yet, or an agent's chat. */
export const placeOf = (chat: { main: boolean; campaignId: string | null }): CardPlace =>
    chat.main ? 'feed' : chat.campaignId === null ? 'setup' : 'agent';

/** The options of a card that asks: a choice's, or a plan's question's; none for any other. */
export const optionsOf = (card: Card): readonly CardOption[] =>
    card.kind === 'choice' ? card.options : card.kind === 'plan' ? (card.options ?? []) : [];

/**
 * The chat's one card that may still be live: the last block of its last entry that locks a choice, when that entry is
 * the AI's words (not a plan the desktop marked) and the block reads as a card that asks. Any later entry of the
 * person's or the AI's locks every earlier card, so no other can be.
 */
export function openChoice(
    chat: Pick<Chat, 'entries' | 'campaignId' | 'main' | 'running' | 'reopened'>,
): { index: number; card: Card } | null {
    let index = chat.entries.length - 1;
    while (index >= 0 && !locksChoice(chat.entries[index]!.kind)) index--;
    const entry = chat.entries[index];
    // One read from the desktop's record is drawn locked (`choiceState`), so it is never open.
    if (index < chat.reopened) return null;
    if (entry?.kind !== 'text' || entry.plan === true) return null;
    const block = splitCards(entry.text).blocks.at(-1);
    if (block === undefined) return null;
    const reading = readCard(block, { place: placeOf(chat), campaignId: chat.campaignId, ended: !chat.running });
    if (reading.state !== 'card' || optionsOf(reading.card).length === 0) return null;
    return { index, card: reading.card };
}
