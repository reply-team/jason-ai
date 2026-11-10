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
}

export class ProviderKeys {
    #file: string;
    #box: SecretBox;

    constructor(file: string, box: SecretBox) {
        this.#file = file;
        this.#box = box;
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
        if (!this.#box.isEncryptionAvailable()) {
            throw new Refused('This system offers no encrypted storage to the desktop, so the key was not kept.');
        }
        entries[providerId] = this.#box.encryptString(trimmed).toString('base64');
        this.#write(entries);
        return 'stored';
    }

    /** A provider's key, for the agent host's environment and nothing else. */
    keyFor(providerId: string): string | undefined {
        const entry = this.#read()[providerId];
        if (entry === undefined) return undefined;
        try {
            return this.#box.decryptString(Buffer.from(entry, 'base64'));
        } catch {
            return undefined;
        }
    }
}
