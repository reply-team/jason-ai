// Pure: the cards the chat's AI asks for. The AI ends a reply with one fenced block whose info string is `card`, holding
// one JSON object with a `kind`; the page takes every such block out of the words it draws, and draws the last block of
// a turn as a card. Six kinds, a closed list, each held to its schema: every member's type and length, own keys only,
// nothing the schema does not name. A block is read once, by `JSON.parse` alone, and never mended, cut or guessed: a
// block that does not read draws one line in the desktop's words, and what it held is never shown.
//
// A block is found by the subset's own reading of fences (`fences`, `markdown.ts`), so the two never disagree about
// where a block ends; this module adds only that a card's fence opens in the line's first column with the info string
// `card` and nothing after it but spaces. A fence indented, inside a list item or a quote, inside another fence, or with
// any other info string is the AI's code, drawn as code.

import { fences } from './markdown';
import { locksChoice, unseen, type Chat } from './chat-model';

/** The kinds a card may be, in the order the chat's instructions list them. */
export const cardKinds = ['choice', 'people_list', 'agent_status', 'needs', 'accounts', 'plan'] as const;
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
 * The words of the chat's own controls: every word a button of the chat draws (the thread's cards, the waiting cards,
 * the header, the composer and its menus), and the verbs of a person's acts on an agent. A choice's label that is one of
 * them, folded as `foldLabel` folds, is refused: a button with a word of the window's own would look like that control.
 * A test reads the chat's buttons and fails while a word of theirs is missing here. Names a control draws from a value
 * (a host's, a model's, a campaign's) are not words of the window's and are not held.
 */
export const controlWords: readonly string[] = [
    // A person's acts, wherever the window offers them.
    'approve',
    'reject',
    'edit',
    'answer',
    'start',
    'resume',
    'pause',
    'send',
    'stop',
    // The waiting cards.
    'details',
    'something else',
    'show more',
    'show the whole message',
    'cancel',
    // The thread's cards: a permission, a plan, a choice.
    'allow',
    'allow for this chat',
    "don't allow",
    'approve and run',
    'approving',
    'keep planning',
    'you chose this',
    'needs you',
    'open folder',
    'check again',
    'not now',
    'connect',
    // The header and the composer.
    'rename',
    'save',
    'start afresh',
    'remove',
    'keep it',
    'remove it',
    'take back',
    'replace',
    'send when the turn ends',
    'ask first',
    'plan only',
    'auto-edit',
    'full access',
    'default effort',
];

/**
 * A label as a person reads it, for comparing two: its compatible forms made one (a full-width letter is the letter),
 * every apostrophe the one apostrophe, its runs of white space one space, no stop or ellipsis at its end, and no case.
 * Letters of another alphabet that look the same are not made one.
 */
export const foldLabel = (label: string): string =>
    label
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

export type Card = ChoiceCard | PeopleListCard | AgentStatusCard | NeedsCard | AccountsCard | PlanCard;

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
 * is left whole: what Approve and run tells the AI to carry out is its answer, so every block of it stays in its words.
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
    if ((lines ? trimmed.replace(/\n/g, '') : trimmed).search(unseen) !== -1) refuse();
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
    plan: ['agent', 'setup'],
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
            shaped(object, ['kind']);
            return { kind };
        case 'plan':
            return planOf(object);
    }
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
