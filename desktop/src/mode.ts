/**
 * The mode the window shows: Live, the installation's own data, or Demo, invented sample data. The main process keeps
 * it in a file of its own, `<data>/desktop/mode`, holding one word; it is never kept in the window's storage and never
 * sent to the runtime. A file that is missing, cannot be read or holds anything but one of the two words reads as the
 * default.
 */

import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

export type Mode = 'live' | 'demo';

/** The mode of a window whose file says none: the one line that decides what a new installation opens in. */
export const defaultMode: Mode = 'live';

export const isMode = (value: unknown): value is Mode => value === 'live' || value === 'demo';

/** The desktop's sentence when the mode could not be kept: the window then stays as it is. */
export const modeUnkept = 'The mode could not be saved, so the window stays as it is.';

/** Pure: the mode a file's text says, the default for anything but one of the two words. */
export function parseMode(text: string | null): Mode {
    const word = text?.trim();
    return isMode(word) ? word : defaultMode;
}

export function readMode(file: string): Mode {
    try {
        return parseMode(readFileSync(file, 'utf8'));
    } catch {
        return defaultMode;
    }
}

/** Writes the mode through a file beside it renamed over it, so a reader never meets half a word. */
export function writeMode(file: string, mode: Mode): void {
    mkdirSync(dirname(file), { recursive: true });
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${mode}\n`, { mode: 0o600 });
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

export interface ModeDeps {
    /** Keeps the mode in its file; throws when it cannot. */
    write(mode: Mode): void;
    /** Loads the window's page again, which then reads the mode it opens in. */
    reload(): void;
}

/**
 * The page asks for a mode: the file is written first and the window reloaded after, answering null. A write that
 * fails reloads nothing and answers the refusal; a word that is neither mode is refused before anything is done.
 */
export function applyMode(choice: unknown, deps: ModeDeps): { refused: string } | null {
    if (!isMode(choice)) throw new Error('That is not a mode.');
    try {
        deps.write(choice);
    } catch {
        return { refused: modeUnkept };
    }
    deps.reload();
    return null;
}
