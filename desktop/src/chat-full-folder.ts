// The ended place, `<data>/desktop/chat-full-ended/`, where a folder goes to be kept seven days before it is removed. Two
// kinds of folder go there. The folders an earlier desktop made for each Full access grant, `<data>/desktop/chat-full/<id>/`,
// which no desktop makes now: for one release a start still moves each one left there. And a conversation's own folder,
// `<data>/desktop/chats/<conversation id>/`, where every turn of it runs (`ownConversationFolder`): moved when the
// conversation is removed, or at a start when no record names it. Each is named `<id>-<moment>` there, the moment being
// when it moved. Nothing is moved out of a place that is not a real folder, a link or a junction included, nor into an
// ended place that is not one. A bounded sweep removes a folder seven days after its move: it walks by `lstat`, never
// follows a link, checks that a folder it is about to enter is the one it looked at, stops after a number of entries it
// is given and takes up again at the next pass. A failure leaves the folder where it is and says a code; nothing here
// ever refuses a turn or removes a folder at the moment it moves.

import { lstatSync, mkdirSync, opendirSync, readdirSync, readFileSync, renameSync, rmdirSync, unlinkSync, type BigIntStats } from 'node:fs';
import { join } from 'node:path';
import { conversationIdShape, newConversationId } from './chat-conversations.ts';

/** How long an ended grant's folder is kept, from the moment it moved. */
export const keptDays = 7;
export const keptMs = keptDays * 24 * 60 * 60 * 1000;

/** How many entries one pass of the sweep takes before it stops and leaves the rest to the next pass. */
export const sweepCap = 50_000;

/** A grant's id: 26 Crockford characters, the moment in the first ten. */
export const grantIdShape = /^[0-9A-HJKMNP-TV-Z]{26}$/;
/**
 * The name of an ended folder: a grant's id or a conversation's (`conv_` and 26 characters), a dash and the moment it
 * moved, in milliseconds.
 */
export const endedNameShape = /^((?:conv_)?[0-9A-HJKMNP-TV-Z]{26})-([0-9]{10,16})$/;

export function newGrantId(now = Date.now()): string {
    return newConversationId(now).slice('conv_'.length);
}

export interface FullFolders {
    /** `<data>/desktop/chat-full/`. */
    full: string;
    /** `<data>/desktop/chat-full-ended/`. */
    ended: string;
    /** `<data>/desktop/chat-full-records/`: the grant-to-conversation records, which no turn reads or writes. */
    records: string;
}

/** The name an ended folder has: the grant's or the conversation's id and the moment it moved. */
export const endedName = (id: string, now: number): string => `${id}-${now}`;

/**
 * The desktop's own record of the conversation a grant was made for, `<records>/<grant id>`, outside every folder a turn
 * works in, so a later start can say where a folder went. A file in the grant's folder is never read for it.
 */
const recordOf = (folders: FullFolders, id: string): string => join(folders.records, id);

/**
 * Moves a grant's folder to the ended place, named with the moment it moved. Returns the name it has there; null when
 * the folder is not there or could not be moved (a host still holds it), which `moveLeftovers` takes up at the next start.
 */
export function endGrantFolder(folders: FullFolders, id: string, now: number, said: (code: string) => void = () => {}): string | null {
    if (!grantIdShape.test(id)) throw new Error('That is not a grant id the desktop made.');
    return moveToEnded(folders, folders.full, id, now, said);
}

/**
 * Moves a conversation's own folder, `<chats>/<id>/`, to the ended place, named with the moment it moved, where the sweep
 * removes it seven days later. Returns the name it has there; null when the folder is not there, or could not be moved
 * (a terminal still holds it, on Windows), and then it stays where it is, everything in it, for a later start.
 */
export function endConversationFolder(chats: string, folders: FullFolders, id: string, now: number, said: (code: string) => void = () => {}): string | null {
    if (!conversationIdShape.test(id)) throw new Error('That is not a conversation id the desktop made.');
    const place = folderKind(chats);
    if (place === 'other') said('ENOTDIR');
    if (place !== 'folder') return null;
    return moveToEnded(folders, chats, id, now, said);
}

/**
 * At a start, the conversations' folders no record names: those whose conversation was removed while a terminal held
 * the folder, or whose record went another way. Each is moved to the ended place, its seven days counted from now. The
 * caller names the records, and calls this only when every record was read: a record that cannot be read names no
 * folder, so none is moved then. A name that is no conversation's id is left alone. Returns the names they have there.
 */
export function moveOwnLeftovers(chats: string, folders: FullFolders, recorded: ReadonlySet<string>, now: number, said: (code: string) => void = () => {}): string[] {
    const place = folderKind(chats);
    if (place === 'other') said('ENOTDIR');
    if (place !== 'folder') return [];
    let names: string[];
    try {
        names = readdirSync(chats);
    } catch (error) {
        said(codeOf(error));
        return [];
    }
    const moved: string[] = [];
    for (const name of names) {
        if (!conversationIdShape.test(name) || recorded.has(name)) continue;
        const ended = moveToEnded(folders, chats, name, now, said);
        if (ended !== null) moved.push(ended);
    }
    return moved;
}

/** What stands at a name, by `lstat`: a folder itself, nothing, or anything else, a link or a junction to a folder included. */
function folderKind(path: string): 'folder' | 'none' | 'other' {
    try {
        const stat = lstatSync(path);
        return stat.isDirectory() && !stat.isSymbolicLink() ? 'folder' : 'other';
    } catch (error) {
        return (error as NodeJS.ErrnoException | null)?.code === 'ENOENT' ? 'none' : 'other';
    }
}

/**
 * Moves `<from>/<id>` to the ended place as `<id>-<now>`, one rename, so nothing in it is removed now. The ended place
 * is made where it is not, and nothing is moved into one that is not a real folder. Null when there is nothing to move
 * or the move failed, which says a code.
 */
function moveToEnded(folders: FullFolders, from: string, id: string, now: number, said: (code: string) => void): string | null {
    const path = join(from, id);
    try {
        lstatSync(path);
    } catch {
        return null;
    }
    const name = endedName(id, now);
    try {
        mkdirSync(folders.ended, { recursive: true, mode: 0o700 });
        if (folderKind(folders.ended) !== 'folder') throw Object.assign(new Error('not a folder'), { code: 'ENOTDIR' });
        renameSync(path, join(folders.ended, name));
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
            conversation = readFileSync(recordOf(folders, name), 'utf8').slice(0, 64);
        } catch {
            // No record: the move is not said to any conversation.
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
            // The folder is gone, and with it the desktop's record of whom a grant's was for; a conversation's has none.
            const id = parts[1] ?? '';
            if (grantIdShape.test(id)) {
                try {
                    unlinkSync(recordOf(folders, id));
                } catch {
                    // Nothing recorded, or left for the next sweep's folder-less pass.
                }
            }
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
