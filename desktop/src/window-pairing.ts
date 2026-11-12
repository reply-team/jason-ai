/**
 * The window's pairing with the runtime, kept off the disk. The page keeps its UI token, which has the run token's
 * rights, in its own storage; under a persistent session that storage is a file of `<data>/desktop/electron/` a command
 * of a Codex chat can read. So the window runs in a partition Electron keeps in memory (`windowPartition`): whatever
 * build of the page the runtime serves, nothing of its session reaches the disk, and each start pairs afresh, as it
 * always has (`main.ts`, `repair.ts`).
 *
 * What an earlier version's page left on disk is ended at the first start: every UI token is revoked through the
 * runtime's existing `ui.revoke` with `all`, before the window loads, then Electron's default session is cleared and its
 * page storage folders removed (`loadWindow`). Revoking every token is the only complete way: the ids an earlier page
 * stored are kept per origin, which Electron cannot list, and `ui.tokens` cannot tell a window's row from a browser's.
 * A browser paired with `jason ui open` pairs again once.
 *
 * The page reports the id of the token it holds, never the token (`WindowPairing`), so the token of the previous start
 * or of a pairing with a runtime on another port is revoked once the next pairing has succeeded, and the paired list
 * keeps one row for the window. The record (`<data>/desktop/window-pairing.json`) holds that id and the flag; an id is
 * no credential, since ending a token takes the run token or a UI token.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { Cli } from './cli.ts';
import type { Logger } from './log.ts';

/** The session the window's page runs in. With no `persist:` prefix, Electron keeps it in memory only. */
export const windowPartition = 'jason-window';

/** Whether Electron keeps a partition's session on disk. */
export function persists(partition: string): boolean {
    return partition.startsWith('persist:');
}

/** The folders of Electron's default session that an earlier version's page wrote, under `<data>/desktop/electron/`. */
export const earlierPageStorage = ['Local Storage', 'Session Storage', 'IndexedDB'] as const;

/** A UI token's id as the runtime writes it: `uit_` and a ULID. A token is 43 characters of base64url and never this. */
const tokenIdShape = /^uit_[0-9A-HJKMNP-TV-Z]{26}$/;

export function isTokenId(value: unknown): value is string {
    return typeof value === 'string' && tokenIdShape.test(value);
}

export interface WindowPairingState {
    /** Whether what an earlier version's page left has been unpaired, or there was none. */
    cleared: boolean;
    /** The id of the UI token the window's page reported last. */
    tokenId: string | null;
}

/** Pure: the record a file says, anything said wrongly taken as unset. */
export function parseWindowPairing(text: string | null): WindowPairingState {
    let raw: unknown;
    try {
        raw = text === null ? null : JSON.parse(text);
    } catch {
        raw = null;
    }
    const value = (raw ?? {}) as { page_storage_cleared?: unknown; token_id?: unknown };
    return { cleared: value.page_storage_cleared === true, tokenId: isTokenId(value.token_id) ? value.token_id : null };
}

export function readWindowPairing(file: string): WindowPairingState {
    try {
        return parseWindowPairing(readFileSync(file, 'utf8'));
    } catch {
        return parseWindowPairing(null);
    }
}

/** Writes the record whole, through a file beside it renamed over it. It throws what the file system threw. */
export function writeWindowPairing(file: string, state: WindowPairingState): void {
    mkdirSync(dirname(file), { recursive: true });
    const body = { page_storage_cleared: state.cleared, ...(state.tokenId === null ? {} : { token_id: state.tokenId }) };
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, `${JSON.stringify(body, null, 2)}\n`, { mode: 0o600 });
    renameSync(temporary, file);
}

/** Whether an earlier version's page left its storage: read before Electron is ready, before it makes anything there. */
export function earlierPageFound(electron: string): boolean {
    return existsSync(join(electron, earlierPageStorage[0]));
}

export interface LoadDeps {
    cli: Cli;
    /** Clears Electron's default session: its storage and its cache. */
    clearSession(): Promise<void>;
    /** Removes a folder and everything in it; throws what the file system threw. */
    remove(folder: string): void;
    /** `<data>/desktop/electron/`. */
    electron: string;
    log: Logger;
}

function codeOf(error: unknown): string {
    const code = (error as { code?: unknown } | null)?.code;
    return typeof code === 'string' && /^[A-Z][A-Z0-9_]{1,31}$/.test(code) ? code : 'unknown';
}

/**
 * Loads the window, first ending what an earlier version's page left when that is still to do: every UI token revoked,
 * then the default session cleared and its page storage removed. A revoke that fails removes nothing and leaves the
 * flag unset, so the next start tries again; a removal that fails after it is logged and the flag set, since the tokens
 * are dead. The window loads either way. Answers whether nothing is left to clear.
 */
export async function loadWindow(state: { cleared: boolean; found: boolean }, deps: LoadDeps, load: () => Promise<void>): Promise<{ cleared: boolean }> {
    let cleared = state.cleared || !state.found;
    if (!cleared) cleared = await clearEarlierPage(deps);
    await load();
    return { cleared };
}

async function clearEarlierPage(deps: LoadDeps): Promise<boolean> {
    const revoke = await deps.cli.run(['ui', 'revoke', '--all']);
    if (revoke.exit !== 0) {
        deps.log.warn('page_storage_clear_failed', { Step: 'revoke', Code: String(revoke.exit) });
        return false;
    }
    let revoked = 0;
    try {
        const count = (JSON.parse(revoke.stdout) as { revoked?: unknown }).revoked;
        if (typeof count === 'number' && Number.isInteger(count) && count >= 0) revoked = count;
    } catch {
        // The count is said in the log only; the revoke succeeded.
    }
    try {
        await deps.clearSession();
    } catch (error) {
        deps.log.warn('page_storage_clear_failed', { Step: 'session', Code: codeOf(error) });
    }
    for (const name of earlierPageStorage) {
        try {
            deps.remove(join(deps.electron, name));
        } catch (error) {
            deps.log.warn('page_storage_clear_failed', { Step: 'remove', Code: codeOf(error) });
        }
    }
    deps.log.info('page_storage_cleared', { Revoked: revoked });
    return true;
}

export interface PairingDeps {
    cli: Cli;
    log: Logger;
    state(): WindowPairingState;
    save(state: WindowPairingState): void;
}

/** The window's page reporting the id of the token it now holds; the previous one is ended then, and only then. */
export class WindowPairing {
    readonly #deps: PairingDeps;
    #queue: Promise<void> = Promise.resolve();

    constructor(deps: PairingDeps) {
        this.#deps = deps;
    }

    /**
     * Keeps the reported id and revokes the one kept before it, if another. Anything but a token id is refused, so the
     * member can never carry the token itself. One report at a time, in the order they came.
     */
    paired(tokenId: unknown): Promise<void> {
        if (!isTokenId(tokenId)) return Promise.reject(new Error('That is not a token id.'));
        const next = this.#queue.then(() => this.#take(tokenId));
        this.#queue = next.catch(() => undefined);
        return next;
    }

    async #take(tokenId: string): Promise<void> {
        const state = this.#deps.state();
        const previous = state.tokenId;
        if (previous === tokenId) return;
        this.#deps.save({ ...state, tokenId });
        if (previous === null) return;
        const revoke = await this.#deps.cli.run(['ui', 'revoke', previous]);
        if (revoke.exit !== 0) this.#deps.log.warn('window_token_revoke_failed', { Exit: revoke.exit });
    }
}
