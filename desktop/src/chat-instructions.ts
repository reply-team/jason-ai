// The instruction text in the chat folder: the application's own text, written as the project memory file of both hosts
// the chat drives, CLAUDE.md for Claude Code and AGENTS.md for Codex, byte for byte. Only the chat folder gets them:
// never the harness root, nor a folder a turn adds, where the person's own memory files live.
//
// What stands at a file's name is never followed: a file holds the text only when it is a regular file of that size with
// those bytes, and anything else (a link, another text, nothing) is replaced by a file written beside it and renamed over
// the name. A directory at the name is never removed; the write fails, and the turn is refused until it can be made.
// The chat folder is never followed: a name that is not a directory itself fails the write before anything is written.

import { randomBytes } from 'node:crypto';
import { closeSync, fsyncSync, lstatSync, mkdirSync, openSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The memory files the hosts read from the folder they start in. */
export const memoryFiles = ['CLAUDE.md', 'AGENTS.md'] as const;

/** A memory file that could not be written: its name and the system's error code, never the error's text or a path. */
export class InstructionsUnwritten extends Error {
    readonly file: string;
    readonly code: string;

    constructor(file: string, code: unknown) {
        super(`The chat’s instruction file ${file} could not be written, so the turn was not started. Close anything that has it open and send again.`);
        this.name = 'InstructionsUnwritten';
        this.file = file;
        this.code = typeof code === 'string' && /^E[A-Z]+$/.test(code) ? code : 'unknown';
    }
}

/**
 * Writes each memory file that does not hold the text; returns the names it wrote. The first file that cannot be
 * written throws `InstructionsUnwritten`, and the files after it are not tried.
 */
export function placeInstructions(folder: string, text: string): string[] {
    const wanted = Buffer.from(text, 'utf8');
    // The folder itself is never followed either: a link or a junction at its name would take both files into the
    // folder it names, the person's own memory files there included. A recursive make succeeds on such a name, so the
    // name is looked at after it.
    let isFolder: boolean;
    try {
        mkdirSync(folder, { recursive: true });
        isFolder = lstatSync(folder).isDirectory();
    } catch (error) {
        throw new InstructionsUnwritten(memoryFiles[0], (error as NodeJS.ErrnoException).code);
    }
    if (!isFolder) throw new InstructionsUnwritten(memoryFiles[0], 'ENOTDIR');
    return memoryFiles.filter((name) => {
        const file = join(folder, name);
        if (holds(file, wanted)) return false;
        replace(folder, name, wanted);
        return true;
    });
}

/** Whether the name is a regular file, not a link, of exactly the wanted bytes. */
function holds(file: string, wanted: Buffer): boolean {
    try {
        const stat = lstatSync(file);
        return stat.isFile() && stat.size === wanted.length && readFileSync(file).equals(wanted);
    } catch {
        return false;
    }
}

/** Writes the bytes to a new file in the folder and renames it over the name; the new file goes again if that fails. */
function replace(folder: string, name: string, wanted: Buffer): void {
    const temporary = join(folder, `.${name}.${randomBytes(6).toString('hex')}.tmp`);
    try {
        const handle = openSync(temporary, 'wx');
        try {
            writeFileSync(handle, wanted);
            fsyncSync(handle);
        } finally {
            closeSync(handle);
        }
        renameSync(temporary, join(folder, name));
    } catch (error) {
        try {
            rmSync(temporary, { force: true });
        } catch {
            // A file that cannot be removed now is as harmless as its name; it is never read.
        }
        throw new InstructionsUnwritten(name, (error as NodeJS.ErrnoException).code);
    }
}
