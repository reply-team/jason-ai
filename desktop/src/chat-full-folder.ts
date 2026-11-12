// The folder a Full access grant works in. Each confirmation makes `<data>/desktop/chat-full/<id>/`: its top is owned by
// the desktop as the chat folder's is (`ownAgentFolder`), the recorded skills are copied in, and the Full access turns of
// that one conversation run there and nothing else does. When the grant ends the folder moves, at once, to
// `<data>/desktop/chat-full-ended/<id>-<moment>/`, the moment in its name being when it moved, and is kept seven days.
// A bounded sweep removes it after that: it walks by `lstat`, never follows a link, checks that a folder it is about to
// enter is the one it looked at, stops after a number of entries it is given and takes up again at the next pass. A
// failure leaves the folder and says a code; nothing here ever refuses a turn.

import { copyFileSync, lstatSync, mkdirSync, opendirSync, readdirSync, readFileSync, renameSync, rmdirSync, unlinkSync, writeFileSync, type BigIntStats } from 'node:fs';
import { join } from 'node:path';
import { ownAgentFolder, skillsFolders } from './chat-agent-folder.ts';
import { newConversationId } from './chat-conversations.ts';

/** How long an ended grant's folder is kept, from the moment it moved. */
export const keptDays = 7;
export const keptMs = keptDays * 24 * 60 * 60 * 1000;

/** How many entries one pass of the sweep takes before it stops and leaves the rest to the next pass. */
export const sweepCap = 50_000;

/** A grant's id: 26 Crockford characters, the moment in the first ten. */
export const grantIdShape = /^[0-9A-HJKMNP-TV-Z]{26}$/;
/** The name of an ended grant's folder: the grant's id, a dash and the moment it moved, in milliseconds. */
export const endedNameShape = /^([0-9A-HJKMNP-TV-Z]{26})-([0-9]{10,16})$/;

export function newGrantId(now = Date.now()): string {
    return newConversationId(now).slice('conv_'.length);
}

export interface FullFolders {
    /** `<data>/desktop/chat-full/`. */
    full: string;
    /** `<data>/desktop/chat-full-ended/`. */
    ended: string;
}

/** A grant's folder could not be made: the system's error code, never its text or a path. */
export class GrantFolderUnmade extends Error {
    readonly code: string;

    constructor(code: unknown) {
        super('The folder for Full access could not be made, so Full access was not given.');
        this.name = 'GrantFolderUnmade';
        this.code = typeof code === 'string' && /^E[A-Z]+$/.test(code) ? code : 'unknown';
    }
}

/** Copies the regular files and folders below `from` into `to`; a link or anything else is not copied. */
function copyOwn(from: string, to: string): void {
    for (const name of readdirSync(from)) {
        const source = join(from, name);
        const stat = lstatSync(source);
        if (stat.isDirectory()) {
            mkdirSync(join(to, name), { recursive: true });
            copyOwn(source, join(to, name));
        } else if (stat.isFile()) {
            copyFileSync(source, join(to, name));
        }
    }
}

/**
 * Makes a grant's folder, with the recorded skills in it. The chat folder's skills folders are read as they stand (every
 * turn puts them right first), copied, and the copy put right against the records as the chat folder's top is, so only
 * what the desktop recorded, with the bytes recorded, is there. Returns the folder's path.
 */
export function makeGrantFolder(folders: FullFolders, id: string, chat: string, records: string, conversation?: string): string {
    if (!grantIdShape.test(id)) throw new Error('That is not a grant id the desktop made.');
    const folder = join(folders.full, id);
    try {
        mkdirSync(folders.full, { recursive: true, mode: 0o700 });
        mkdirSync(folder);
        if (conversation !== undefined) writeFileSync(join(folder, conversationMarker), conversation);
        for (const host of skillsFolders) {
            const skills = join(chat, host.folder, 'skills');
            let stat;
            try {
                stat = lstatSync(skills);
            } catch (error) {
                if ((error as NodeJS.ErrnoException).code === 'ENOENT') continue;
                throw error;
            }
            if (!stat.isDirectory()) continue;
            mkdirSync(join(folder, host.folder, 'skills'), { recursive: true });
            copyOwn(skills, join(folder, host.folder, 'skills'));
        }
        ownAgentFolder(folder, records);
    } catch (error) {
        throw new GrantFolderUnmade((error as NodeJS.ErrnoException | null)?.code);
    }
    return folder;
}

/** The name an ended folder has: the grant's id and the moment it moved. */
export const endedName = (id: string, now: number): string => `${id}-${now}`;

/** The file at a grant folder's top that names the conversation it was made for, so a later start can say where it went. */
export const conversationMarker = '.grant-conversation';

/**
 * Moves a grant's folder to the ended place, named with the moment it moved. Returns the name it has there; null when
 * the folder is not there or could not be moved (a host still holds it), which `moveLeftovers` takes up at the next start.
 */
export function endGrantFolder(folders: FullFolders, id: string, now: number, said: (code: string) => void = () => {}): string | null {
    if (!grantIdShape.test(id)) throw new Error('That is not a grant id the desktop made.');
    const from = join(folders.full, id);
    try {
        lstatSync(from);
    } catch {
        return null;
    }
    const name = endedName(id, now);
    try {
        mkdirSync(folders.ended, { recursive: true, mode: 0o700 });
        renameSync(from, join(folders.ended, name));
    } catch (error) {
        said(codeOf(error));
        return null;
    }
    return name;
}

/**
 * At a start no grant is running, so every folder still in the grants' place is a leftover (a window closed on a turn that
 * held it, or a move that failed): each is moved to the ended place, its seven days counted from now.
 */
export function moveLeftovers(folders: FullFolders, now: number, said: (code: string) => void = () => {}): { name: string; conversation: string | null }[] {
    let names: string[];
    try {
        names = readdirSync(folders.full);
    } catch {
        return [];
    }
    const moved: { name: string; conversation: string | null }[] = [];
    for (const name of names) {
        if (!grantIdShape.test(name)) continue;
        const ended = endGrantFolder(folders, name, now, said);
        if (ended === null) continue;
        let conversation: string | null = null;
        try {
            const marker = join(folders.ended, ended, conversationMarker);
            if (lstatSync(marker).isFile()) conversation = readFileSync(marker, 'utf8').slice(0, 64);
        } catch {
            // No marker: the move is not said to any conversation.
        }
        moved.push({ name: ended, conversation });
    }
    return moved;
}

function codeOf(error: unknown): string {
    const code = (error as NodeJS.ErrnoException | null)?.code;
    return typeof code === 'string' && /^E[A-Z]+$/.test(code) ? code : 'unknown';
}

/** What the sweep does to the file system; a test gives its own to make a failure or a swap happen at one place. */
export interface SweepOps {
    lstat(path: string): BigIntStats;
    /** Up to `limit` names of a folder. */
    list(path: string, limit: number): string[];
    unlink(path: string): void;
    rmdir(path: string): void;
}

export const sweepOps: SweepOps = {
    lstat: (path) => lstatSync(path, { bigint: true }),
    list: (path, limit) => {
        const dir = opendirSync(path);
        try {
            const names: string[] = [];
            for (let entry = dir.readSync(); entry !== null && names.length < limit; entry = dir.readSync()) names.push(entry.name);
            return names;
        } finally {
            dir.closeSync();
        }
    },
    unlink: (path) => unlinkSync(path),
    rmdir: (path) => rmdirSync(path),
};

export interface SweepOptions {
    /** The clock, in milliseconds. */
    now: number;
    /** Entries to take in this pass. */
    cap: number;
    ops?: SweepOps;
    /** A failure's code, once for each folder it stopped. */
    said?: (code: string) => void;
}

export interface Swept {
    /** The ended folders removed whole in this pass. */
    removed: number;
    /** Whether the pass stopped at its cap, leaving what is left to the next. */
    capped: boolean;
}

class Capped extends Error {}

/**
 * Removes the ended folders that are seven days old. Every entry is `lstat`ed by its parent; a folder is looked at again
 * just before it is entered, and entered only if it is still a folder with the same device and inode, so a folder swapped
 * for a link meanwhile is unlinked and never walked. A link is unlinked as a link and what it names is left. A failure at
 * one folder leaves it and says a code; the next folder is taken up.
 */
export function sweepEnded(folders: FullFolders, options: SweepOptions): Swept {
    const ops = options.ops ?? sweepOps;
    const said = options.said ?? (() => {});
    const swept: Swept = { removed: 0, capped: false };
    let used = 0;
    const spend = () => {
        if (++used > options.cap) throw new Capped();
    };
    const remove = (path: string, seen: BigIntStats): void => {
        spend();
        if (!seen.isDirectory()) {
            // A file, or a link or junction: removed as itself, what a link names is never touched.
            ops.unlink(path);
            return;
        }
        const now = ops.lstat(path);
        if (!now.isDirectory() || now.dev !== seen.dev || now.ino !== seen.ino) {
            if (now.isSymbolicLink()) ops.unlink(path);
            else throw Object.assign(new Error('swapped'), { code: 'ESWAPPED' });
            return;
        }
        for (;;) {
            const names = ops.list(path, Math.max(1, options.cap - used));
            if (names.length === 0) break;
            for (const name of names) remove(join(path, name), ops.lstat(join(path, name)));
        }
        ops.rmdir(path);
    };
    let names: string[];
    try {
        names = readdirSync(folders.ended);
    } catch {
        return swept;
    }
    for (const name of names.sort()) {
        const parts = endedNameShape.exec(name);
        if (parts === null) {
            said('ENAME');
            continue;
        }
        if (options.now - Number(parts[2]) < keptMs) continue;
        const path = join(folders.ended, name);
        try {
            remove(path, ops.lstat(path));
            swept.removed++;
        } catch (error) {
            if (error instanceof Capped) {
                swept.capped = true;
                return swept;
            }
            said(codeOf(error));
        }
    }
    return swept;
}
