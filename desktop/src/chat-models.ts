/**
 * Pure: which models each host the chat runs on offers, which effort each of them takes, the list the desktop keeps of
 * what Codex offers, and the exchange that asks `codex app-server` for it. Nothing here starts a process or reads a file.
 *
 * What the vendors state, and all this leans on (read 2026-11-11):
 * - Anthropic, https://code.claude.com/docs/en/cli-reference: of its commands none lists models, so Claude Code's list
 *   is the desktop's own table of the aliases of https://code.claude.com/docs/en/model-config, which "point to the
 *   recommended version for your provider and update over time", and the CLI's `--model` takes a model's full name as
 *   well. The same page on effort: "Fable models | `low`, `medium`, `high`, `xhigh`, `max`", "Opus 5.5, Sonnet 5.5,
 *   Haiku 5.5, Opus 5, Sonnet 5, Opus 4.8, and Opus 4.7 | `low`, `medium`, `high`, `xhigh`, `max`" and "Models not listed
 *   here do not support effort"; its version history: "v2.1.293 | `haiku` resolves to Haiku 5.5 on the Anthropic API",
 *   and before that "`haiku` resolves to Haiku 4.5 on every provider", which is not listed. So the rule that `haiku`
 *   takes effort from 2.1.293 is the Anthropic API's: on another provider the box may offer a level that Claude Code
 *   does not apply. The flag, https://code.claude.com/docs/en/cli-reference: `--effort` "Options: `low`,
 *   `medium`, `high`, `xhigh`, `max`, or `ultracode`. Available levels depend on the model."
 * - OpenAI, https://developers.openai.com/codex/app-server (served as https://learn.chatgpt.com/docs/app-server): "Call
 *   model/list to populate a model picker with the available models and reasoning efforts."; "stdio (--listen
 *   stdio://, default): newline-delimited JSON (JSONL)."; "send an initialize request with your client metadata, then
 *   emit initialized. The server rejects any request on that connection before this handshake."; each entry carries
 *   `supportedReasoningEfforts`, "supported effort options for the model", and `hidden`; "By default, model/list returns
 *   picker-visible models only."; a page names the next by `nextCursor`. Effort on a run,
 *   https://developers.openai.com/codex/config-reference: `model_reasoning_effort` "Reasoning effort advertised by the
 *   selected model, such as low, medium, high, xhigh, max, or ultra. Available levels depend on the model and client."
 *
 * The effort the desktop passes is one of five words. Never `ultracode` nor `ultra`: each is a way of running more
 * agents, not a level (https://developers.openai.com/codex/models: "Ultra uses subagents to handle separate parts of a
 * complex task in parallel.").
 */

import { compareVersions } from './host-version.ts';
import { keyProvider } from './providers-model.ts';

export const efforts = ['low', 'medium', 'high', 'xhigh', 'max'] as const;
export type Effort = (typeof efforts)[number];

export function isEffort(value: unknown): value is Effort {
    return typeof value === 'string' && (efforts as readonly string[]).includes(value);
}

/** A model a host offers: the name the host takes, the words the page shows for it, and the effort levels it takes. */
export interface ModelOffer {
    id: string;
    label: string;
    efforts: Effort[];
}

/** A model name the desktop passes to a host: it begins with a letter or a digit, so it never stands where an option would. */
export const modelShape = /^[A-Za-z0-9][A-Za-z0-9._:[\]/-]{0,99}$/;

/** The Claude Code version from which `haiku` names a model that takes effort. */
export const haikuEffortFrom = '2.1.293';

/** Claude Code's models: the vendor's aliases, each with the levels it takes at the version read before the turn. */
export function claudeCodeModels(version: string | null): ModelOffer[] {
    const every: Effort[] = [...efforts];
    const haiku = version !== null && compareVersions(version, haikuEffortFrom) >= 0 ? every : [];
    return [
        { id: 'opus', label: 'Opus', efforts: every },
        { id: 'sonnet', label: 'Sonnet', efforts: every },
        { id: 'haiku', label: 'Haiku', efforts: haiku },
        { id: 'fable', label: 'Fable', efforts: [...every] },
    ];
}

/** The host ids whose models are asked of the host and kept: Codex on the person's own sign-in, and on the OpenAI key. */
export const listedHosts = ['codex', 'codex-api-key'] as const;

/** One host's list as kept: when it was last asked for, and what it offered then. */
export interface KeptList {
    askedAt: string;
    models: ModelOffer[];
}

export type KeptLists = ReadonlyMap<string, KeptList>;

/** What the models a host offers are, and whether a full model name the person types is taken beside them. */
export interface Offered {
    models: ModelOffer[];
    typed: boolean;
}

/**
 * The models a host id offers: Claude Code's aliases on Claude Code and on Anthropic's key, with a typed full name beside
 * them; the list kept for Codex and for the OpenAI key; and a key provider's own table, with the levels its vendor's
 * guide names (`KeyProvider.efforts`).
 */
export function offeredModels(hostId: string, kept: KeptLists, claudeVersion: string | null): Offered {
    if (hostId === 'claude' || hostId === 'claude-api-key') return { models: claudeCodeModels(claudeVersion), typed: true };
    if ((listedHosts as readonly string[]).includes(hostId)) return { models: (kept.get(hostId)?.models ?? []).map((model) => ({ ...model, efforts: [...model.efforts] })), typed: false };
    const provider = keyProvider(hostId);
    if (provider === undefined) return { models: [], typed: false };
    return { models: provider.models.map((id) => ({ id, label: id, efforts: [...(provider.efforts ?? [])] })), typed: false };
}

/**
 * The names a vendor retired, read as the name it gives now wherever a conversation kept one. DeepSeek,
 * https://api-docs.deepseek.com/quick_start/pricing (read 2026-11-11): "Use deepseek-flash as the model name. The legacy
 * names deepseek-v4-flash and deepseek-v4-flash-vision-exp are still accepted, but the corresponding models have been
 * retired".
 */
const renamed: Readonly<Record<string, ReadonlyMap<string, string>>> = {
    deepseek: new Map([['deepseek-v4-flash', 'deepseek-flash']]),
};

export function currentModel<T extends string | undefined>(hostId: string, model: T): T {
    if (model === undefined) return model;
    return (renamed[hostId]?.get(model) ?? model) as T;
}

/** The sentence a send is refused with when its model is not one its host offers. */
export const modelRefused = 'That model is not one this host offers.';

/**
 * Null when the host offers the model named, by the table or the list kept, or takes it as a typed name (Claude Code
 * and Anthropic's key alone, a name of the model shape); null too when none is named. Else the sentence the send is
 * refused with: a model chosen on one host never reaches another.
 */
export function modelRefusal(hostId: string, model: string | undefined, kept: KeptLists): string | null {
    if (model === undefined) return null;
    const offered = offeredModels(hostId, kept, null);
    if (offered.models.some((each) => each.id === model)) return null;
    return offered.typed && modelShape.test(model) ? null : modelRefused;
}

/** The sentence a send is refused with when its effort is not one its model takes. */
export const effortRefused = 'That effort is not one this model takes.';

/**
 * Null when the named model takes the effort, by the table or the list kept; else the sentence the send is refused
 * with. The host's default model, a typed name and a model no list names take none.
 */
export function effortRefusal(hostId: string, model: string | undefined, effort: Effort, kept: KeptLists, claudeVersion: string | null): string | null {
    if (model === undefined) return effortRefused;
    const offer = offeredModels(hostId, kept, claudeVersion).models.find((each) => each.id === model);
    return offer?.efforts.includes(effort) ? null : effortRefused;
}

const labelLimit = 80;
const unseen = /[\p{Cc}\p{Cf}\u2028\u2029]/gu;

/** A label as the page shows it: one line, no control or format character, at most eighty characters; else the id. */
function labelOf(value: unknown, id: string): string {
    if (typeof value !== 'string') return id;
    const line = Array.from(value.replace(/\s+/g, ' ').replace(unseen, '').trim()).slice(0, labelLimit).join('');
    return line === '' ? id : line;
}

/** The levels of a list that are words of the desktop's, each once, in the order given. */
function effortsOf(values: unknown): Effort[] {
    if (!Array.isArray(values)) return [];
    return [...new Set(values.filter(isEffort))];
}

/** The entries that pass the shapes, each id once. */
function offersOf(entries: readonly { id: unknown; label: unknown; efforts: unknown }[]): ModelOffer[] {
    const seen = new Set<string>();
    const offers: ModelOffer[] = [];
    for (const entry of entries) {
        if (typeof entry.id !== 'string' || !modelShape.test(entry.id) || seen.has(entry.id)) continue;
        seen.add(entry.id);
        offers.push({ id: entry.id, label: labelOf(entry.label, entry.id), efforts: effortsOf(entry.efforts) });
    }
    return offers;
}

const isObject = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);

/**
 * The lists kept in `<data>/desktop/models.json`, by host id. A list is read whole or not at all: one whose moment or
 * entries cannot be read is no list, and a file that is no JSON object holds none. An entry is kept only when its id
 * passes the model shape, and its levels only where they are the desktop's words.
 */
export function parseKeptLists(text: string): Map<string, KeptList> {
    const lists = new Map<string, KeptList>();
    let value: unknown;
    try {
        value = JSON.parse(text);
    } catch {
        return lists;
    }
    if (!isObject(value) || !isObject(value.hosts)) return lists;
    for (const host of listedHosts) {
        const list = value.hosts[host];
        if (!isObject(list) || typeof list.asked_at !== 'string' || Number.isNaN(Date.parse(list.asked_at)) || !Array.isArray(list.models)) continue;
        if (!list.models.every(isObject) || list.models.length > modelLimit) continue;
        lists.set(host, { askedAt: list.asked_at, models: offersOf(list.models as { id: unknown; label: unknown; efforts: unknown }[]) });
    }
    return lists;
}

export function keptListsText(lists: KeptLists): string {
    const hosts: Record<string, unknown> = {};
    for (const [host, list] of lists) hosts[host] = { asked_at: list.askedAt, models: list.models.map((model) => ({ id: model.id, label: model.label, efforts: model.efforts })) };
    return `${JSON.stringify({ hosts }, null, 2)}\n`;
}

/** How an ask of the list ended: the models offered, or why there are none to keep. */
export type ListingOutcome = { models: ModelOffer[] } | { failed: 'refused' | 'unreadable' };

/** The most pages of `model/list` read; a list that names a page past them is not read whole. */
const pageLimit = 20;

/** The most models a host's list holds; a listing past them, and a kept list of more, is not read. */
export const modelLimit = 200;

/**
 * One ask of `codex app-server` for its models, as the protocol it speaks over standard input and output: `initialize`,
 * the `initialized` notification once it is answered, then `model/list` with `includeHidden: false`, page by page. Each
 * request is sent only once the one before it is answered, since the page does not say what the server does with
 * requests queued behind one it has not answered. Nothing starts a thread or a turn, so nothing reaches a model.
 */
export class ListingExchange {
    #version: string;
    #next = 1;
    #waiting: { id: number; method: 'initialize' | 'model/list' } | null = null;
    #models: { id: unknown; label: unknown; efforts: unknown }[] = [];
    #pages = 0;
    #outcome: ListingOutcome | undefined;

    /** `version`: the desktop's own, which `initialize` names as the client's. */
    constructor(version: string) {
        this.#version = version;
    }

    /** How the ask ended; undefined while it goes on. */
    get outcome(): ListingOutcome | undefined {
        return this.#outcome;
    }

    /** The first line to send. */
    start(): string[] {
        return [this.#request('initialize', { clientInfo: { name: 'jason_desktop', title: 'Jason Desktop', version: this.#version } })];
    }

    /** One line the server wrote: the lines to send next, none while an answer is awaited or once the ask has ended. */
    receive(line: string): string[] {
        if (this.#outcome !== undefined || this.#waiting === null) return [];
        let message: unknown;
        try {
            message = JSON.parse(line);
        } catch {
            return [];
        }
        // A notification, or an answer to an id never asked, is not the answer awaited.
        if (!isObject(message) || message.id !== this.#waiting.id) return [];
        if (message.error !== undefined) return this.#end({ failed: 'refused' });
        if (!isObject(message.result)) return this.#end({ failed: 'unreadable' });
        if (this.#waiting.method === 'initialize') {
            return [JSON.stringify({ method: 'initialized', params: {} }), this.#request('model/list', { includeHidden: false })];
        }
        const { data, nextCursor } = message.result;
        if (!Array.isArray(data) || !data.every(isObject) || (nextCursor !== null && nextCursor !== undefined && typeof nextCursor !== 'string')) return this.#end({ failed: 'unreadable' });
        for (const entry of data) {
            if (entry.hidden === true) continue;
            const levels = Array.isArray(entry.supportedReasoningEfforts) ? entry.supportedReasoningEfforts.map((each: unknown) => (isObject(each) ? each.reasoningEffort : undefined)) : [];
            this.#models.push({ id: entry.model, label: entry.displayName, efforts: levels });
        }
        if (this.#models.length > modelLimit) return this.#end({ failed: 'unreadable' });
        if (typeof nextCursor !== 'string') return this.#end({ models: offersOf(this.#models) });
        if (++this.#pages >= pageLimit) return this.#end({ failed: 'unreadable' });
        return [this.#request('model/list', { includeHidden: false, cursor: nextCursor })];
    }

    #request(method: 'initialize' | 'model/list', params: Record<string, unknown>): string {
        const id = this.#next++;
        this.#waiting = { id, method };
        return JSON.stringify({ method, id, params });
    }

    #end(outcome: ListingOutcome): string[] {
        this.#outcome = outcome;
        this.#waiting = null;
        return [];
    }
}
