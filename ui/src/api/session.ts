import { computed, signal } from '@preact/signals-react';
import { Bridge, type BridgePair } from '../platform/bridge';
import type { UiSession } from './types';

const key = 'jason.ui.session';
const bridgeKey = 'jason.ui.bridge';

type Stored = Pick<UiSession, 'token' | 'token_id' | 'actor_id' | 'instance_id'>;

/** The pairing this browser holds: in localStorage for the runtime's origin, read on load, cleared on a 401. */
export class Session {
    readonly token = signal<string | null>(null);
    readonly tokenId = signal<string | null>(null);
    readonly actorId = signal<string | null>(null);
    readonly instanceId = signal<string | null>(null);
    readonly paired = computed(() => this.token.value !== null);
    /** The desktop's bridge this tab holds, when the desktop opened the tab; null otherwise. */
    readonly bridge = signal<Bridge | null>(null);

    /**
     * Reads the stored pairing; then, when the link carries a ticket, trades it at the door, wipes it from the address
     * bar, and ends the token this browser held before, so a browser opened again is paired once.
     */
    async bootstrap(): Promise<void> {
        this.restore();
        const { ticket, bridge, hash } = splitFragment(location.hash);
        if (ticket || bridge) history.replaceState(null, '', location.pathname + location.search + hash);
        if (ticket) await this.pair(ticket);
        await this.openBridge(bridge);
    }

    /**
     * The desktop's bridge: a code in the link (`d=<port>.<code>`) is traded once at the bridge for a secret, which this
     * tab keeps in sessionStorage with the port. With no code, a pair kept by an earlier load is tried once, and dropped
     * when the bridge does not take it.
     */
    private async openBridge(code: { port: number; code: string } | null): Promise<void> {
        if (code) {
            try {
                const response = await fetch(`http://127.0.0.1:${code.port}/pair`, {
                    method: 'POST',
                    headers: { 'content-type': 'application/json' },
                    body: JSON.stringify({ code: code.code }),
                });
                const answer = response.ok ? ((await response.json()) as { secret?: unknown }) : null;
                if (answer && typeof answer.secret === 'string') {
                    const pair: BridgePair = { port: code.port, secret: answer.secret };
                    this.keepBridge(pair);
                    this.bridge.value = this.made(pair);
                }
            } catch {
                // The desktop is not there: the screen says what it says without a bridge.
            }
            return;
        }
        const kept = this.keptBridge();
        if (!kept) return;
        const bridge = this.made(kept);
        if (await bridge.alive()) this.bridge.value = bridge;
        else this.dropBridge();
    }

    private made(pair: BridgePair): Bridge {
        const bridge: Bridge = new Bridge(pair, undefined, () => {
            if (this.bridge.value === bridge) this.dropBridge();
        });
        return bridge;
    }

    private keptBridge(): BridgePair | null {
        try {
            const raw = sessionStorage.getItem(bridgeKey);
            if (!raw) return null;
            const pair = JSON.parse(raw) as Partial<BridgePair>;
            return typeof pair.port === 'number' && typeof pair.secret === 'string'
                ? { port: pair.port, secret: pair.secret }
                : null;
        } catch {
            return null;
        }
    }

    private keepBridge(pair: BridgePair): void {
        try {
            sessionStorage.setItem(bridgeKey, JSON.stringify(pair));
        } catch {
            // Then the bridge lasts for this load only.
        }
    }

    /** Forgets the kept pair, and the bridge this tab held. */
    dropBridge(): void {
        try {
            sessionStorage.removeItem(bridgeKey);
        } catch {
            // Nothing was kept.
        }
        this.bridge.value?.close();
        this.bridge.value = null;
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
        tellDesktop(session.token_id);
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

/**
 * In the desktop, the id of the token this page now holds, never the token: the desktop's window keeps its storage in
 * memory, so the desktop ends the token its window held before. A desktop without the member, or one that refuses it,
 * changes nothing here.
 */
function tellDesktop(tokenId: string): void {
    // A desktop older than this page offers no such member.
    const desktop = window.jasonDesktop as { pairing?: { paired?: (id: string) => Promise<void> } } | undefined;
    try {
        void desktop?.pairing?.paired?.(tokenId)?.catch(() => undefined);
    } catch {
        // An older desktop: the token it held stays listed until it is revoked.
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

/**
 * The fragment's two codes, each taken out of it: the runtime's ticket (`t`) and the desktop bridge's pairing code
 * (`d=<port>.<code>`). A `d` that is not a loopback port and a code is dropped from the address and not used.
 */
export function splitFragment(hash: string): {
    ticket: string | null;
    bridge: { port: number; code: string } | null;
    hash: string;
} {
    const at = hash.indexOf('?');
    if (at < 0) return { ticket: null, bridge: null, hash };
    const query = new URLSearchParams(hash.slice(at + 1));
    const ticket = query.get('t');
    const raw = query.get('d');
    query.delete('t');
    query.delete('d');
    const rest = query.toString();
    const match = raw === null ? null : /^(\d{1,5})\.([A-Za-z0-9_-]{8,128})$/.exec(raw);
    const port = match ? Number(match[1]) : 0;
    const bridge = match && port >= 1 && port <= 65535 ? { port, code: match[2]! } : null;
    return { ticket, bridge, hash: hash.slice(0, at) + (rest ? '?' + rest : '') };
}

export const session = new Session();
