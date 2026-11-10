// Holds what the chat's agent waits on. The host asks through the permission endpoint; the model decides what it can;
// everything else is a card on the asking chat's page, and the answer comes from that page by `answer` and from nowhere
// else. Each request is answered once. "Always" is kept in memory for one chat and is gone with the window.

import { randomUUID } from 'node:crypto';
import { judge, type ChatScope } from './chat-permission-model.ts';

/** What the permission prompt tool answers, in the shape Claude Code reads. */
export type ToolAnswer = { behavior: 'allow'; updatedInput?: Record<string, unknown> } | { behavior: 'deny'; message: string };

export type BrokerEvent =
    | { kind: 'permission'; request: string; title: string; detail: string; remember: boolean }
    | { kind: 'permission_gone'; request: string };

/** How one request was settled, for the log: never its tool, its input or its words. */
export interface Decided {
    outcome: 'allowed' | 'refused' | 'timed_out';
    by: 'model' | 'person';
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
}

interface Pending {
    chat: string;
    turn: string;
    input: Record<string, unknown>;
    remember: string | null;
    timer: NodeJS.Timeout;
    settle: (answer: ToolAnswer) => void;
}

/** Claude Code's guidance: an input of about 40 KB or more is not copied back into the answer. */
const copyLimit = 32_000;
const saidNo = 'The person said no. Do not try another way to do the same thing; ask them what they would like instead.';
const unanswered = 'Nobody answered within thirty minutes, so nothing was run.';
const ended = 'The chat stopped, so nothing was run.';
const notRunning = 'This chat is not running, so nothing was run.';

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

    openTurn(chatId: string, turnId: string, scope: ChatScope): void {
        this.#turns.set(turnId, { chat: chatId, scope });
    }

    /** The host's question for a turn. A turn that is not open is refused. */
    ask(turnId: string, tool: string, input: Record<string, unknown>): Promise<ToolAnswer> {
        const turn = this.#turns.get(turnId);
        if (turn === undefined) return Promise.resolve({ behavior: 'deny', message: notRunning });
        const judged = judge(tool, input, turn.scope);
        if (judged.verdict === 'allow') return Promise.resolve(this.#decided(allowed(input), { outcome: 'allowed', by: 'model' }));
        if (judged.verdict === 'deny') return Promise.resolve(this.#decided({ behavior: 'deny', message: judged.message }, { outcome: 'refused', by: 'model' }));
        if (judged.remember !== null && this.#always.get(turn.chat)?.has(judged.remember) === true) {
            return Promise.resolve(this.#decided(allowed(input), { outcome: 'allowed', by: 'person' }));
        }
        const request = (this.#options.newId ?? randomUUID)();
        return new Promise((settle) => {
            const timer = setTimeout(() => {
                if (this.#settle(request, { behavior: 'deny', message: unanswered })) this.#options.decided?.({ outcome: 'timed_out', by: 'person' });
            }, this.#options.timeoutMs ?? PermissionBroker.unanswered);
            this.#pending.set(request, { chat: turn.chat, turn: turnId, input, remember: judged.remember, timer, settle });
            this.#options.emit(turn.chat, { kind: 'permission', request, title: judged.title, detail: judged.detail, remember: judged.remember !== null });
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
        if (this.#settle(requestId, settled, false)) this.#options.decided?.({ outcome: answer.allow ? 'allowed' : 'refused', by: 'person' });
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
        if (gone) this.#options.emit(pending.chat, { kind: 'permission_gone', request });
        pending.settle(answer);
        return true;
    }

    #decided(answer: ToolAnswer, decided: Decided): ToolAnswer {
        this.#options.decided?.(decided);
        return answer;
    }
}
