import { computed, signal } from '@preact/signals';
import type { UiSession } from './types';

const key = 'jason.ui.session';

type Stored = Pick<UiSession, 'token' | 'token_id' | 'actor_id' | 'instance_id'>;

/** The pairing this browser holds: in localStorage for the runtime's origin, read on load, cleared on a 401. */
export class Session {
    readonly token = signal<string | null>(null);
    readonly tokenId = signal<string | null>(null);
    readonly actorId = signal<string | null>(null);
    readonly instanceId = signal<string | null>(null);
    readonly paired = computed(() => this.token.value !== null);

    /**
     * Reads the stored pairing; then, when the link carries a ticket, trades it at the door, wipes it from the address
     * bar, and ends the token this browser held before, so a browser opened again is paired once.
     */
    async bootstrap(): Promise<void> {
        this.restore();
        const { ticket, hash } = splitTicket(location.hash);
        if (!ticket) return;
        history.replaceState(null, '', location.pathname + location.search + hash);
        await this.pair(ticket);
    }

    async pair(ticket: string): Promise<void> {
        const previous = this.tokenId.value;
        const response = await fetch('/ui/session', {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ ticket }),
        });
        if (!response.ok) return;
        const session = (await response.json()) as UiSession;
        this.store(session);
        if (previous && previous !== session.token_id) await revoke(previous, session.token);
    }

    clear(): void {
        try {
            localStorage.removeItem(key);
        } catch {
            // Storage may be unavailable; the signals are what the page reads.
        }
        this.token.value = null;
        this.tokenId.value = null;
        this.actorId.value = null;
        this.instanceId.value = null;
    }

    private restore(): void {
        try {
            const raw = localStorage.getItem(key);
            if (raw) this.apply(JSON.parse(raw) as Stored);
        } catch {
            // Unreadable storage is "not paired".
        }
    }

    private store(session: UiSession): void {
        const stored: Stored = {
            token: session.token,
            token_id: session.token_id,
            actor_id: session.actor_id,
            instance_id: session.instance_id,
        };
        try {
            localStorage.setItem(key, JSON.stringify(stored));
        } catch {
            // Then the pairing lasts for this page load only.
        }
        this.apply(stored);
    }

    private apply(stored: Stored): void {
        this.token.value = stored.token;
        this.tokenId.value = stored.token_id;
        this.actorId.value = stored.actor_id;
        this.instanceId.value = stored.instance_id;
    }
}

/** The old token of a browser paired again, ended with the new one. A refusal leaves a stray row in `jason ui tokens`, nothing more. */
async function revoke(tokenId: string, token: string): Promise<void> {
    try {
        await fetch('/v1/ui.revoke', {
            method: 'POST',
            headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
            body: JSON.stringify({ token_id: tokenId }),
        });
    } catch {
        // Unreachable: the old token stays listed until jason ui revoke.
    }
}

/** `#/attention/approvals/apr_1?t=TICKET` → the ticket and the hash without it. */
export function splitTicket(hash: string): { ticket: string | null; hash: string } {
    const at = hash.indexOf('?');
    if (at < 0) return { ticket: null, hash };
    const query = new URLSearchParams(hash.slice(at + 1));
    const ticket = query.get('t');
    query.delete('t');
    const rest = query.toString();
    return { ticket, hash: hash.slice(0, at) + (rest ? '?' + rest : '') };
}

export const session = new Session();
