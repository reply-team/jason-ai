/**
 * What waits on a person, for the tray: the pending approvals and the pending questions, counted from `jason approval
 * list` and `jason decision list` as the CLI prints them. The main process holds no token of the runtime's (the page
 * holds the pairing's), so it asks through the CLI as a person at a terminal would; it adds no route and keeps nothing
 * for a screen. Only the ids are counted: no name, no campaign, no question's text is kept or shown.
 */

import type { Cli } from './cli.ts';
import type { Logger } from './log.ts';
import { windowTitle } from './window-title.ts';

export type Attention = { state: 'reading' } | { state: 'up'; approvals: number; questions: number } | { state: 'unreachable' };

export interface Waiting {
    approvals: number;
    questions: number;
}

/** How often the count is read: never more often than every 30 seconds. */
export const attentionCadenceMs = 30_000;

/** The most a list verb takes in one page (`Paging.MaxLimit`), and how many pages a reading follows. */
const pageLimit = 1000;
const pagesRead = 10;

function pageOf(stdout: string): { count: number; cursor: string | null } | null {
    try {
        const parsed = JSON.parse(stdout) as { items?: unknown; next_cursor?: unknown };
        if (!Array.isArray(parsed.items)) return null;
        const cursor = typeof parsed.next_cursor === 'string' && parsed.next_cursor !== '' ? parsed.next_cursor : null;
        return { count: parsed.items.length, cursor };
    } catch {
        return null;
    }
}

async function countPending(cli: Cli, noun: 'approval' | 'decision'): Promise<number | null> {
    let count = 0;
    let cursor: string | null = null;
    for (let index = 0; index < pagesRead; index++) {
        const result = await cli.run([noun, 'list', '--status', 'pending', '--limit', String(pageLimit), ...(cursor === null ? [] : ['--cursor', cursor])]);
        const read = result.exit === 0 ? pageOf(result.stdout) : null;
        if (read === null) return null;
        count += read.count;
        cursor = read.cursor;
        if (cursor === null) break;
    }
    return count;
}

export async function readAttention(cli: Cli): Promise<Attention> {
    const approvals = await countPending(cli, 'approval');
    const questions = approvals === null ? null : await countPending(cli, 'decision');
    return approvals === null || questions === null ? { state: 'unreachable' } : { state: 'up', approvals, questions };
}

function counted(waiting: Waiting): string {
    const parts: string[] = [];
    if (waiting.approvals > 0) parts.push(`${waiting.approvals} ${waiting.approvals === 1 ? 'approval' : 'approvals'}`);
    if (waiting.questions > 0) parts.push(`${waiting.questions} ${waiting.questions === 1 ? 'question' : 'questions'}`);
    const total = waiting.approvals + waiting.questions;
    return `${parts.join(' and ')} ${total === 1 ? 'waits' : 'wait'} on you`;
}

export interface TrayView {
    /** Beside the icon in the macOS menu bar. */
    title: string;
    tooltip: string;
    /** The menu's first line, which does nothing when clicked. */
    status: string;
}

export function trayView(attention: Attention): TrayView {
    if (attention.state === 'reading') return { title: '', tooltip: windowTitle, status: 'Reading what waits on you…' };
    if (attention.state === 'unreachable') return { title: '!', tooltip: `${windowTitle}: the runtime is not answering`, status: 'The runtime is not answering' };
    const total = attention.approvals + attention.questions;
    if (total === 0) return { title: '', tooltip: `${windowTitle}: nothing waits on you`, status: 'Nothing waits on you' };
    const said = counted(attention);
    return { title: String(total), tooltip: `${windowTitle}: ${said}`, status: said.charAt(0).toUpperCase() + said.slice(1) };
}

/** A system notification's words: a pointer to what waits, by its counts. */
export function notice(waiting: Waiting): { title: string; body: string } {
    const said = counted(waiting);
    return { title: 'Jason needs you', body: `${said.charAt(0).toUpperCase()}${said.slice(1)}.` };
}

export interface Timers {
    setTimeout(run: () => void, ms: number): unknown;
    clearTimeout(handle: unknown): void;
}

export interface AttentionDeps {
    cli: Cli;
    log: Logger;
    timers?: Timers;
    show(attention: Attention): void;
    notify(text: { title: string; body: string }): void;
}

const same = (a: Attention, b: Attention) =>
    a.state === b.state && (a.state !== 'up' || b.state !== 'up' || (a.approvals === b.approvals && a.questions === b.questions));

/**
 * Reads the count on the cadence and tells the tray when it changed. A rise in either count, against the last count the
 * runtime gave, is notified; what already waits when the desktop starts is not news, and a spell without an answer
 * is not a reading.
 */
export class AttentionWatcher {
    private readonly deps: AttentionDeps;
    private readonly timers: Timers;
    private shown: Attention = { state: 'reading' };
    private last: Waiting | null = null;
    private handle: unknown = null;
    private running = false;

    constructor(deps: AttentionDeps) {
        this.deps = deps;
        this.timers = deps.timers ?? { setTimeout: (run, ms) => setTimeout(run, ms), clearTimeout: (handle) => clearTimeout(handle as NodeJS.Timeout) };
    }

    start(): void {
        if (this.running) return;
        this.running = true;
        this.deps.show(this.shown);
        this.schedule(0);
    }

    /**
     * A start that failed: the tray says the runtime is not answering, and nothing is read until the first pairing that
     * succeeds starts the watcher. Once it reads, what it read is what the tray shows.
     */
    unreachable(): void {
        if (this.running || this.shown.state === 'unreachable') return;
        this.shown = { state: 'unreachable' };
        this.deps.log.info('tray_state', { State: 'unreachable' });
        this.deps.show(this.shown);
    }

    /** Whether it reads the count: from `start` until `stop`. */
    get reading(): boolean {
        return this.running;
    }

    stop(): void {
        this.running = false;
        if (this.handle !== null) this.timers.clearTimeout(this.handle);
        this.handle = null;
    }

    private schedule(ms: number): void {
        this.handle = this.timers.setTimeout(() => void this.tick(), ms);
    }

    private async tick(): Promise<void> {
        this.handle = null;
        const read = await readAttention(this.deps.cli);
        if (!this.running) return;
        if (read.state === 'up') {
            const before = this.last;
            this.last = { approvals: read.approvals, questions: read.questions };
            if (before !== null && (read.approvals > before.approvals || read.questions > before.questions)) {
                this.deps.notify(notice(this.last));
                this.deps.log.info('attention_notified', { Approvals: read.approvals, Questions: read.questions });
            }
        }
        if (!same(read, this.shown)) {
            if (read.state !== this.shown.state) this.deps.log.info('tray_state', { State: read.state });
            this.shown = read;
            this.deps.show(read);
        }
        this.schedule(attentionCadenceMs);
    }
}
