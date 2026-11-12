// Holds what the chat's agent waits on. The host asks through the permission endpoint; the model decides what it can;
// everything else is a card on the asking chat's page, and the answer comes from that page by `answer` and from nowhere
// else. Each request is answered once. "Always" is kept in memory for one chat and is gone with the window.
//
// A turn runs in its chat's mode. Ask first is the above. Plan only refuses every request but a read, which the model
// settles, and the two cards left to it: reading a file outside the chat's folders and opening a web page. Full access
// runs in the host's default mode: the turn's hook (`hook`) answers each tool use by one judgement
// (`fullAccessJudgement`): allow what runs, deny what is refused, and ask about what is held, which Claude Code then puts
// to the prompt tool. A request that reaches the prompt tool in Full access, held, one the host asks about whatever the
// hook said, or one the hook never saw, is judged and carded as in Ask first, under its own title, after Full access's own
// refusals. The tool uses the hook saw are kept by id; each refusal and each hold is said to the log.

import { randomUUID } from 'node:crypto';
import type { HeldReason } from './chat-literal.ts';
import { planExitRefusal, planRefusal, type ChatMode } from './chat-modes.ts';
import type { HookAnswer } from './chat-permission-endpoint.ts';
import { fullAccessJudgement, heldSentence, judge, type ChatScope } from './chat-permission-model.ts';

/** What the permission prompt tool answers, in the shape Claude Code reads. */
export type ToolAnswer = { behavior: 'allow'; updatedInput?: Record<string, unknown> } | { behavior: 'deny'; message: string };

export type BrokerEvent =
    /** `remember` is what "always, in this chat" would cover, as the page prints it; null when it cannot be offered. */
    | { kind: 'permission'; request: string; title: string; detail: string; remember: string | null }
    | { kind: 'permission_gone'; request: string };

/** How one request was settled, for the log: never its tool, its input or its words. */
export interface Decided {
    /** `held`: Full access's hook asked, so the request goes on to a card. */
    outcome: 'allowed' | 'refused' | 'held' | 'timed_out' | 'withdrawn';
    by: 'model' | 'person' | 'host';
    /** The mode of the turn that asked. */
    mode: ChatMode;
    /** A held shell command's closed reason; a hold of any other tool carries none. */
    reason?: HeldReason;
}

export interface BrokerOptions {
    emit: (chatId: string, event: BrokerEvent) => void;
    decided?: (decided: Decided) => void;
    /** How long a card waits for the person; thirty minutes unless a test shortens it. */
    timeoutMs?: number;
    newId?: () => string;
}

interface Turn {
    chat: string;
    scope: ChatScope;
    mode: ChatMode;
    /** Full access: the ids of the tool uses the turn's hook was asked about. */
    hooked: Set<string>;
}

/** The writer tools: in Plan only each is refused, whatever the model would have said. */
const writers: ReadonlySet<string> = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit']);
/** The tools whose cards Plan only keeps: reading a file, or a folder, outside the chat's folders, and opening a web page. */
const planCards: ReadonlySet<string> = new Set(['Read', 'Glob', 'Grep', 'LS', 'WebFetch']);

interface Pending {
    chat: string;
    turn: string;
    mode: ChatMode;
    input: Record<string, unknown>;
    remember: string | null;
    timer: NodeJS.Timeout;
    settle: (answer: ToolAnswer) => void;
    /** Stops listening for the host's withdrawal once the request is settled. */
    forget: () => void;
}

/** Claude Code's guidance: an input of about 40 KB or more is not copied back into the answer. */
const copyLimit = 32_000;
const saidNo = 'The person said no. Do not try another way to do the same thing; ask them what they would like instead.';
const unanswered = 'Nobody answered within thirty minutes, so nothing was run.';
const ended = 'The chat stopped, so nothing was run.';
const notRunning = 'This chat is not running, so nothing was run.';
const withdrawn = 'The request was withdrawn, so nothing was run.';
/** The title of a card in Full access: the card is the one Ask first shows, and this says why Full access shows it. */
export const fullAccessCardTitle = 'Full access asks about this one';

function allowed(input: Record<string, unknown>): ToolAnswer {
    return JSON.stringify(input).length >= copyLimit ? { behavior: 'allow' } : { behavior: 'allow', updatedInput: input };
}

export class PermissionBroker {
    static readonly unanswered = 30 * 60_000;

    #options: BrokerOptions;
    #turns = new Map<string, Turn>();
    #pending = new Map<string, Pending>();
    /** Per chat id, what "always" covers. In memory only: it never reaches the next run. */
    #always = new Map<string, Set<string>>();

    constructor(options: BrokerOptions) {
        this.#options = options;
    }

    openTurn(chatId: string, turnId: string, scope: ChatScope, mode: ChatMode = 'ask_first'): void {
        this.#turns.set(turnId, { chat: chatId, scope, mode, hooked: new Set() });
    }

    /**
     * The turn's hook, asked by Claude Code before each tool use of a turn in Full access: allow what runs, deny what is
     * refused, ask about what is held (`HookAnswer`). The use's id is kept, so a tool that ran without the hook is known
     * (`hooked`). A turn that is not open is refused.
     */
    hook(turnId: string, tool: string, input: Record<string, unknown>, toolUseId: string | null): HookAnswer {
        const turn = this.#turns.get(turnId);
        if (turn === undefined) return { decision: 'deny', reason: notRunning };
        if (toolUseId !== null) turn.hooked.add(toolUseId);
        const judged = fullAccessJudgement(tool, input, turn.scope);
        if (judged.outcome === 'runs') return { decision: 'allow' };
        if (judged.outcome === 'refused') {
            this.#options.decided?.({ outcome: 'refused', by: 'model', mode: turn.mode });
            return { decision: 'deny', reason: judged.message };
        }
        this.#options.decided?.({ outcome: 'held', by: 'model', mode: turn.mode, ...(judged.reason === undefined ? {} : { reason: judged.reason }) });
        return { decision: 'ask', reason: heldSentence };
    }

    /** Whether the turn's hook was asked about this tool use. */
    hooked(turnId: string, toolUseId: string): boolean {
        return this.#turns.get(turnId)?.hooked.has(toolUseId) === true;
    }

    /**
     * The host's question for a turn. A turn that is not open is refused, and so is a request the host withdraws
     * (`signal`, a cancellation the endpoint was sent): its card goes.
     */
    ask(turnId: string, tool: string, input: Record<string, unknown>, signal?: AbortSignal): Promise<ToolAnswer> {
        const turn = this.#turns.get(turnId);
        if (turn === undefined) return Promise.resolve({ behavior: 'deny', message: notRunning });
        const mode = turn.mode;
        const refused = (message: string) => Promise.resolve(this.#decided({ behavior: 'deny', message }, { outcome: 'refused', by: 'model', mode }));
        if (mode === 'plan_only' && tool === 'ExitPlanMode') return refused(planExitRefusal);
        if (mode === 'plan_only' && writers.has(tool)) return refused(planRefusal);
        const judged = judge(tool, input, turn.scope);
        if (judged.verdict === 'allow') return Promise.resolve(this.#decided(allowed(input), { outcome: 'allowed', by: 'model', mode }));
        if (judged.verdict === 'deny') return refused(judged.message);
        if (mode === 'full_access') {
            // What Full access refuses is refused here too, whichever way the request came; anything else is judged and
            // carded as in Ask first.
            const full = fullAccessJudgement(tool, input, turn.scope);
            if (full.outcome === 'refused') return refused(full.message);
        }
        if (mode === 'plan_only' && !planCards.has(tool)) return refused(planRefusal);
        if (judged.remember !== null && this.#always.get(turn.chat)?.has(judged.remember) === true) {
            return Promise.resolve(this.#decided(allowed(input), { outcome: 'allowed', by: 'person', mode }));
        }
        if (signal?.aborted === true) return Promise.resolve(this.#decided({ behavior: 'deny', message: withdrawn }, { outcome: 'withdrawn', by: 'host', mode }));
        const request = (this.#options.newId ?? randomUUID)();
        return new Promise((settle) => {
            const timer = setTimeout(() => {
                if (this.#settle(request, { behavior: 'deny', message: unanswered })) this.#options.decided?.({ outcome: 'timed_out', by: 'person', mode });
            }, this.#options.timeoutMs ?? PermissionBroker.unanswered);
            const withdraw = () => {
                if (this.#settle(request, { behavior: 'deny', message: withdrawn })) this.#options.decided?.({ outcome: 'withdrawn', by: 'host', mode });
            };
            signal?.addEventListener('abort', withdraw, { once: true });
            const forget = () => signal?.removeEventListener('abort', withdraw);
            this.#pending.set(request, { chat: turn.chat, turn: turnId, mode, input, remember: judged.remember, timer, settle, forget });
            const title = mode === 'full_access' ? fullAccessCardTitle : judged.title;
            this.#options.emit(turn.chat, { kind: 'permission', request, title, detail: judged.detail, remember: judged.remember === null ? null : judged.covers });
        });
    }

    /** A person's click on a chat's page. Anything but the asking chat's first answer changes nothing. */
    answer(chatId: string, requestId: string, answer: { allow: boolean; always: boolean }): void {
        const pending = this.#pending.get(requestId);
        if (pending === undefined || pending.chat !== chatId) {
            this.#options.emit(chatId, { kind: 'permission_gone', request: requestId });
            return;
        }
        if (answer.allow && answer.always && pending.remember !== null) {
            const kept = this.#always.get(chatId) ?? new Set<string>();
            kept.add(pending.remember);
            this.#always.set(chatId, kept);
        }
        const settled: ToolAnswer = answer.allow ? allowed(pending.input) : { behavior: 'deny', message: saidNo };
        if (this.#settle(requestId, settled, false)) this.#options.decided?.({ outcome: answer.allow ? 'allowed' : 'refused', by: 'person', mode: pending.mode });
    }

    /** The turn is over: what it waits on is refused, its cards go, and later questions for it are refused. */
    endTurn(turnId: string): void {
        this.#turns.delete(turnId);
        for (const [request, pending] of [...this.#pending]) {
            if (pending.turn === turnId) this.#settle(request, { behavior: 'deny', message: ended });
        }
    }

    /** The window closed: every turn ends and every "always" is forgotten. */
    endAll(): void {
        for (const turn of [...this.#turns.keys()]) this.endTurn(turn);
        for (const request of [...this.#pending.keys()]) this.#settle(request, { behavior: 'deny', message: ended });
        this.#always.clear();
    }

    /** True when a request was waiting; its card goes unless the person's own answer took it. */
    #settle(request: string, answer: ToolAnswer, gone = true): boolean {
        const pending = this.#pending.get(request);
        if (pending === undefined) return false;
        this.#pending.delete(request);
        clearTimeout(pending.timer);
        pending.forget();
        if (gone) this.#options.emit(pending.chat, { kind: 'permission_gone', request });
        pending.settle(answer);
        return true;
    }

    #decided(answer: ToolAnswer, decided: Decided): ToolAnswer {
        this.#options.decided?.(decided);
        return answer;
    }
}
