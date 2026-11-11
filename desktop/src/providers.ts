/**
 * The AI providers' keys: the desktop's own, encrypted by the operating system (Electron's `safeStorage`) into
 * `<data>/desktop/provider-keys`. A key is read back only to start an agent host with it; it never reaches the
 * installation's settings, the desktop's settings, a log or any other process.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { keyProvider } from './providers-model.ts';
import { Refused } from './setup-model.ts';

/** The part of Electron's `safeStorage` the store uses. */
export interface SecretBox {
    isEncryptionAvailable(): boolean;
    encryptString(text: string): Buffer;
    decryptString(encrypted: Buffer): string;
    /** Linux only: which password manager the encryption's key is kept by. */
    getSelectedStorageBackend?(): string;
}

/** What a provider-key card says on Linux when the session offers no keyring. */
export const noKeyring =
    'This session offers no keyring (GNOME Keyring or KWallet) to the desktop, so the key was not kept. Start one in your session and enter the key again.';

/**
 * Whether the box keeps its key with a password manager. On Linux the pinned Electron's `electron.d.ts` says
 * `getSelectedStorageBackend()` returns `basic_text` "When the desktop environment is not recognised or if the following
 * command line flag is provided `--password-store="basic"`", where the key is a fixed one rather than a keyring's, and
 * `unknown` "When the function is called before app has emitted the `ready` event". Neither keeps a key safe, so on Linux
 * a key is neither kept nor read under them, nor under a box that names no backend at all. Elsewhere the question does not
 * arise.
 */
function keyringHeld(box: SecretBox, platform: NodeJS.Platform): boolean {
    if (platform !== 'linux') return true;
    const backend = box.getSelectedStorageBackend?.();
    return backend !== undefined && backend !== 'basic_text' && backend !== 'unknown';
}

export class ProviderKeys {
    #file: string;
    #box: SecretBox;
    #platform: NodeJS.Platform;

    constructor(file: string, box: SecretBox, platform: NodeJS.Platform = process.platform) {
        this.#file = file;
        this.#box = box;
        this.#platform = platform;
    }

    #read(): Record<string, string> {
        try {
            const raw = JSON.parse(readFileSync(this.#file, 'utf8')) as unknown;
            if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return {};
            return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((entry): entry is [string, string] => typeof entry[1] === 'string'));
        } catch {
            return {};
        }
    }

    #write(entries: Record<string, string>): void {
        if (Object.keys(entries).length === 0) {
            rmSync(this.#file, { force: true });
            return;
        }
        mkdirSync(dirname(this.#file), { recursive: true });
        const temporary = `${this.#file}.${process.pid}.tmp`;
        writeFileSync(temporary, JSON.stringify(entries), { mode: 0o600 });
        renameSync(temporary, this.#file);
    }

    /** Which providers have a key stored. */
    stored(): string[] {
        if (!keyringHeld(this.#box, this.#platform)) return [];
        return Object.keys(this.#read()).filter((id) => keyProvider(id) !== undefined);
    }

    /** Stores a provider's key, or removes it when the key is empty. */
    set(providerId: string, key: string): 'stored' | 'removed' {
        if (keyProvider(providerId) === undefined) throw new Refused('That is not a provider the desktop keeps a key for.');
        const entries = this.#read();
        const trimmed = key.trim();
        if (trimmed === '') {
            delete entries[providerId];
            this.#write(entries);
            return 'removed';
        }
        if (!keyringHeld(this.#box, this.#platform)) throw new Refused(noKeyring);
        if (!this.#box.isEncryptionAvailable()) {
            throw new Refused('This system offers no encrypted storage to the desktop, so the key was not kept.');
        }
        entries[providerId] = this.#box.encryptString(trimmed).toString('base64');
        this.#write(entries);
        return 'stored';
    }

    /** A provider's key, for the agent host's environment and nothing else. */
    keyFor(providerId: string): string | undefined {
        if (!keyringHeld(this.#box, this.#platform)) return undefined;
        const entry = this.#read()[providerId];
        if (entry === undefined) return undefined;
        try {
            return this.#box.decryptString(Buffer.from(entry, 'base64'));
        } catch {
            return undefined;
        }
    }
}
