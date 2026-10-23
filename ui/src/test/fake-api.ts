import { ApiError, Unpaired, type Api } from '../api/client';
import { fixtures, type FixtureName } from './fixtures';

type Call = { operation: string; body: object; kind: 'read' | 'write' };

/** The same JSON, whatever order its keys were written in; a key set to undefined is no key, as on the wire. */
function canonical(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(canonical);
    if (value === null || typeof value !== 'object') return value;
    return Object.fromEntries(
        Object.entries(value)
            .filter(([, v]) => v !== undefined)
            .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
            .map(([k, v]) => [k, canonical(v)]),
    );
}

let watched: FakeApi | null = null;

/** The fake a test renders the app with, so the screen project's afterEach can fail the test on what it left unanswered. */
export function watch(fake: FakeApi): void {
    watched = fake;
}

/** The calls the watched fake could not answer; the fake is forgotten, ready for the next test. */
export function takeUnanswered(): { operation: string; body: object }[] {
    const unanswered = watched?.unanswered ?? [];
    watched = null;
    return unanswered;
}

/** Whether a request sent is the request a recording was made with. */
export function matches(recorded: object, sent: object): boolean {
    return JSON.stringify(canonical(recorded)) === JSON.stringify(canonical(sent));
}

/**
 * Answers an operation only with a recorded exchange whose request is the one the screen sent, and remembers what it
 * was asked. A screen test states which cases each operation may answer with; a call no case was recorded for fails
 * the test, even when the screen catches the error, so no screen quietly asks more than its test allows.
 */
export class FakeApi implements Api {
    readonly calls: Call[] = [];
    /** The calls nothing queued was recorded for; the screen project's afterEach fails the test on any. */
    readonly unanswered: Call[] = [];
    private readonly answers = new Map<string, FixtureName[]>();
    private readonly held = new Map<string, { when: (body: object) => boolean; until: Promise<void> }>();
    private readonly gone = new Set<string>();

    /** The paired person the client puts on every write, as the recordings were made with. */
    constructor(private readonly actorId = 'ada') {}

    /**
     * The cases an operation may answer with. A call takes the first whose recorded request it equals; when several
     * equal it, each call takes the next and the last repeats.
     */
    answer(operation: string, ...cases: FixtureName[]): this {
        this.answers.set(operation, cases);
        return this;
    }

    /**
     * Holds the answers to an operation — those whose request matches, when a test says which — until the release it
     * returns is called: what a screen shows while it waits. A call not held takes its case first.
     */
    hold(operation: string, when: (body: object) => boolean = () => true): () => void {
        let release = () => {};
        this.held.set(operation, { when, until: new Promise<void>((resolve) => (release = resolve)) });
        return () => {
            this.held.delete(operation);
            release();
        };
    }

    /**
     * Makes an operation unreachable until the returned function is called: the runtime does not answer at all, and the
     * call fails as the browser's fetch does when nothing is listening. There is no exchange to record for it.
     */
    unreachable(operation: string): () => void {
        this.gone.add(operation);
        return () => this.gone.delete(operation);
    }

    async call<T>(operation: string, given: object = {}, kind: 'read' | 'write' = 'read'): Promise<T> {
        const call = { operation, body: given, kind };
        this.calls.push(call);
        if (this.gone.has(operation)) throw new TypeError('Failed to fetch');
        const body = kind === 'write' ? { ...given, actor: { type: 'human', id: this.actorId } } : given;
        const held = this.held.get(operation);
        if (held?.when(given)) await held.until;
        const queue = this.answers.get(operation) ?? [];
        const name = this.take(queue, body);
        if (!name) {
            this.unanswered.push(call);
            throw new Error(
                `FakeApi: nothing recorded for ${operation} ${JSON.stringify(body)}; on offer: ${queue.join(', ') || 'none'}`,
            );
        }
        const exchange = fixtures[name];
        if (exchange.status === 401) throw new Unpaired();
        if (exchange.status >= 400) {
            const { error } = exchange.response as { error: { code: string; message: string; retryable: boolean } };
            throw new ApiError(error.code, error.message, error.retryable, exchange.status);
        }
        return exchange.response as T;
    }

    /** The request a screen sent to an operation, as the screen gave it: without the actor the client adds. */
    sent(operation: string): object | undefined {
        return this.calls.filter((c) => c.operation === operation).at(-1)?.body;
    }

    /** The first queued case recorded with this request; taken from the queue unless it is the last such case. */
    private take(queue: FixtureName[], body: object): FixtureName | undefined {
        const fitting = queue.filter((name) => matches(fixtures[name].request as object, body));
        const name = fitting[0];
        if (name && fitting.length > 1) queue.splice(queue.indexOf(name), 1);
        return name;
    }
}
