import { ApiError, Unpaired, type Api } from '../api/client';
import { fixtures, type FixtureName } from './fixtures';

type Call = { operation: string; body: object; kind: 'read' | 'write' };

/**
 * Answers an operation with a recorded exchange, and remembers what it was asked. A screen test states which case
 * each operation answers with; a call to an operation with no case fails the test, so no screen quietly calls more
 * than its test allows.
 */
export class FakeApi implements Api {
    readonly calls: Call[] = [];
    private readonly answers = new Map<string, FixtureName[]>();
    private readonly held = new Map<string, { when: (body: object) => boolean; until: Promise<void> }>();

    /** Queue cases for an operation; each call takes the next, the last one repeats. */
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

    async call<T>(operation: string, body: object = {}, kind: 'read' | 'write' = 'read'): Promise<T> {
        this.calls.push({ operation, body, kind });
        const held = this.held.get(operation);
        if (held?.when(body)) await held.until;
        const queue = this.answers.get(operation);
        const name = queue && (queue.length > 1 ? queue.shift() : queue[0]);
        if (!name) throw new Error(`FakeApi: no recorded case queued for ${operation}`);
        const exchange = fixtures[name];
        if (exchange.status === 401) throw new Unpaired();
        if (exchange.status >= 400) {
            const { error } = exchange.response as { error: { code: string; message: string; retryable: boolean } };
            throw new ApiError(error.code, error.message, error.retryable, exchange.status);
        }
        return exchange.response as T;
    }

    /** The request a screen sent to an operation, for comparing with the recorded one. */
    sent(operation: string): object | undefined {
        return this.calls.filter((c) => c.operation === operation).at(-1)?.body;
    }
}
