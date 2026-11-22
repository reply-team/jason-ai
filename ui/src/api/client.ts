import type { ErrorDetail, ErrorEnvelope } from './types';
import type { Session } from './session';

export interface Api {
    /** One operation, one JSON body. A write carries the paired person as its actor. */
    call<T>(operation: string, body?: object, kind?: 'read' | 'write'): Promise<T>;
}

/** The runtime refused the bearer: the page has to pair again. */
export class Unpaired extends Error {
    constructor() {
        super('not paired');
    }
}

export class ApiError extends Error {
    constructor(
        public readonly code: string,
        message: string,
        public readonly retryable: boolean,
        public readonly status: number,
        /** What the runtime said of each field it refused, where it said it: data only, read by the place that asked. */
        public readonly details?: readonly ErrorDetail[],
    ) {
        super(message);
    }
}

export class HttpApi implements Api {
    constructor(private readonly session: Session) {}

    async call<T>(operation: string, body: object = {}, kind: 'read' | 'write' = 'read'): Promise<T> {
        const token = this.session.token.value;
        if (!token) throw new Unpaired();
        const payload = kind === 'write' ? { ...body, actor: { type: 'human', id: this.session.actorId.value } } : body;
        const response = await fetch(`/v1/${operation}`, {
            method: 'POST',
            headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
            body: JSON.stringify(payload),
        });
        if (response.status === 401) {
            this.session.clear();
            throw new Unpaired();
        }
        const text = await response.text();
        const json: unknown = text ? JSON.parse(text) : {};
        if (!response.ok) {
            const error = (json as Partial<ErrorEnvelope>).error ?? {
                code: `http_${response.status}`,
                message: text,
                retryable: false,
            };
            throw new ApiError(error.code, error.message, error.retryable, response.status, error.details);
        }
        return json as T;
    }
}
