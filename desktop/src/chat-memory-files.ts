// The reading of the person's memory files the chat is given (`chat-memory.ts`): the `AGENTS.md` of each level that
// applies, the client's for every chat and the project's and the campaign's for a campaign's chat, each in the folder the
// runtime records for it (the harness root it reports as marked, the folder on the project and on the campaign), never a
// folder worked out here. A level has no read, and nothing is said of it, where that folder is not an absolute path, is a
// link or no folder, or does not carry the marker of that level and that id (`.jason/marker.json`, as the runtime writes
// it), or where its file cannot be looked at or opened this time. Its file is absent where the marked folder holds none,
// or an empty one; the chat looks once more, a moment later, before it says so to a session that was given the file.
// A file that is a link, or a name with more than one link (a hard link reaches a file kept anywhere else on the disk),
// and one that is not UTF-8 text, are not given and are named to the agent, as is one that does not answer in time
// (`readMemoryWithin`). No link is followed: the folder, its `.jason` and the file are each
// looked at without following one, and the file read is the one looked at. One bounded read: a file longer than
// `memoryFileLimit` gives its first bytes and says it was cut.

import { createHash } from 'node:crypto';
import { constants, type BigIntStats } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import { posix, win32 } from 'node:path';
import { memoryFileLimit, memoryFileName, memoryReadLimitMs, type MemoryLevel, type MemoryPlace, type MemoryRead, type MemorySkip } from './chat-memory.ts';

/** The most a marker file may hold; the runtime writes one line of a few dozen bytes. */
const markerLimit = 4096;

const markerKinds: Readonly<Record<MemoryLevel, string>> = { client: 'root', project: 'project', campaign: 'campaign' };

const pathsFor = (platform: NodeJS.Platform) => (platform === 'win32' ? win32 : posix);

/**
 * The reads not yet returned, by level and folder. A read past its time cannot be stopped and holds one of the system's
 * file threads until the system returns; a place that hangs would take one more at every turn and starve the process of
 * them, so no read of a place starts while an earlier one of it is pending.
 */
const pendingReads = new Map<string, Promise<MemoryRead | null>>();

/**
 * A level's read, given the most time it may take: past `limitMs` the turn goes on without it, and it is said not to have
 * answered (`slow`). The read itself cannot be stopped; what it finds later is dropped. While an earlier read of the same
 * place is still pending, none is started, and the level is said not to have answered at once.
 */
export async function readMemoryWithin(
    place: MemoryPlace,
    limitMs: number = memoryReadLimitMs,
    platform: NodeJS.Platform = process.platform,
    reader: (place: MemoryPlace, platform: NodeJS.Platform) => Promise<MemoryRead | null> = readMemoryFile,
): Promise<MemoryRead | null> {
    const slow: MemoryRead = { kind: 'skipped', level: place.level, path: pathsFor(platform).join(place.folder, memoryFileName), why: 'slow', digest: null };
    const key = `${place.level}\n${place.folder}`;
    if (pendingReads.has(key)) return slow;
    // Taken off the list once it returns, before the race below answers.
    const reading = reader(place, platform)
        .catch(() => null)
        .finally(() => pendingReads.delete(key));
    pendingReads.set(key, reading);
    let timer: NodeJS.Timeout | undefined;
    const late = new Promise<'late'>((resolve) => {
        timer = setTimeout(() => resolve('late'), limitMs);
    });
    try {
        const read = await Promise.race([reading, late]);
        return read === 'late' ? slow : read;
    } finally {
        clearTimeout(timer);
    }
}

/** What a place's memory file comes to, or null where the level has no read (the header says which reasons). */
export async function readMemoryFile(place: MemoryPlace, platform: NodeJS.Platform = process.platform): Promise<MemoryRead | null> {
    const paths = pathsFor(platform);
    if (!(await markedFolder(place, platform))) return null;
    const path = paths.join(place.folder, memoryFileName);
    const read = await readNoFollow(path, memoryFileLimit);
    if (read === null) return null;
    if (read === 'absent') return { kind: 'absent', level: place.level };
    if (read === 'linked') return skipped(place.level, path, 'linked');
    const text = textOf(read.bytes, read.more);
    if (text === null) return skipped(place.level, path, 'not_text');
    if (text.trim() === '') return { kind: 'absent', level: place.level };
    const digest = `sha256:${createHash('sha256').update(`${place.level}\n${path}\n${read.more ? 'cut' : 'whole'}\n`).update(text, 'utf8').digest('hex')}`;
    return { kind: 'file', level: place.level, path, text, cut: read.more, digest };
}

/**
 * Whether a place's folder is the one the runtime marked for it: an absolute path to a folder itself, not a link, whose
 * `.jason` is a folder too, holding the runtime's marker of that level and id. Read as a memory file's place is read.
 */
export async function markedFolder(place: MemoryPlace, platform: NodeJS.Platform = process.platform): Promise<boolean> {
    const paths = pathsFor(platform);
    if (typeof place.folder !== 'string' || !paths.isAbsolute(place.folder)) return false;
    if (!(await isPlainFolder(place.folder)) || !(await isPlainFolder(paths.join(place.folder, '.jason')))) return false;
    const marker = await readNoFollow(paths.join(place.folder, '.jason', 'marker.json'), markerLimit);
    return typeof marker === 'object' && marker !== null && !marker.more && markerNames(marker.bytes, markerKinds[place.level], place.id);
}

/** A file not given, with what a record keeps of it: its level, its path and why, hashed, never a word of it. */
function skipped(level: MemoryLevel, path: string, why: Exclude<MemorySkip, 'slow'>): MemoryRead {
    return { kind: 'skipped', level, path, why, digest: `skipped:sha256:${createHash('sha256').update(`${level}\n${path}\nskipped ${why}\n`).digest('hex')}` };
}

/** Whether a path is a folder itself: not a link, a junction or anything else. */
async function isPlainFolder(path: string): Promise<boolean> {
    try {
        const found = await lstat(path);
        return found.isDirectory() && !found.isSymbolicLink();
    } catch {
        return false;
    }
}

/** Whether a marker's bytes are the runtime's marker of that kind and id, read as the runtime reads one. */
function markerNames(bytes: Buffer, kind: string, id: string | undefined): boolean {
    let value: unknown;
    try {
        value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
    } catch {
        return false;
    }
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const marker = value as Record<string, unknown>;
    if (marker.marker_version !== 1 || marker.kind !== kind) return false;
    return kind === 'root' ? marker.id === undefined || marker.id === null : typeof id === 'string' && marker.id === id;
}

/**
 * A regular file's first bytes, up to `cap`, and whether it holds more. `absent` where nothing is at the path; `linked`
 * where the path is a link, or a file with more than one name, which may be a hard link to a file kept anywhere; null
 * where it is anything else but a regular file, cannot be opened or read, or where what was opened is not what was looked
 * at (another file put in its place between the two). Opened without following a link where the system can say so, and
 * without waiting, so a pipe does not hold it.
 */
export async function readNoFollow(path: string, cap: number): Promise<{ bytes: Buffer; more: boolean } | 'absent' | 'linked' | null> {
    let before: BigIntStats;
    try {
        before = await lstat(path, { bigint: true });
    } catch (error) {
        return (error as NodeJS.ErrnoException | null)?.code === 'ENOENT' ? 'absent' : null;
    }
    if (before.isSymbolicLink()) return 'linked';
    if (!before.isFile()) return null;
    let handle;
    try {
        handle = await open(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0) | (constants.O_NONBLOCK ?? 0));
    } catch {
        return null;
    }
    try {
        const opened = await handle.stat({ bigint: true });
        if (!opened.isFile() || opened.ino !== before.ino || opened.dev !== before.dev) return null;
        if (opened.nlink !== 1n) return 'linked';
        // One byte more than the cap: a read that fills it says the file holds more.
        const buffer = Buffer.alloc(cap + 1);
        let length = 0;
        while (length < buffer.length) {
            const { bytesRead } = await handle.read(buffer, length, buffer.length - length, length);
            if (bytesRead === 0) break;
            length += bytesRead;
        }
        return { bytes: buffer.subarray(0, Math.min(length, cap)), more: length > cap };
    } catch {
        return null;
    } finally {
        await handle.close();
    }
}

/**
 * The bytes as text: UTF-8, a byte-order mark dropped, and a character the cut split left out whole; null for bytes that
 * are not UTF-8 or that hold a NUL, which no text file does.
 */
function textOf(bytes: Buffer, cut: boolean): string | null {
    let end = bytes.length;
    if (cut) {
        // Back to the start of the last character, then keep it only if all of its bytes are there.
        let start = end;
        while (start > 0 && start > end - 4 && ((bytes[start - 1] ?? 0) & 0xc0) === 0x80) start--;
        if (start > 0) {
            const lead = bytes[start - 1] ?? 0;
            const size = lead < 0x80 ? 1 : lead >= 0xf0 ? 4 : lead >= 0xe0 ? 3 : lead >= 0xc0 ? 2 : 1;
            if (end - (start - 1) < size) end = start - 1;
        }
    }
    let text: string;
    try {
        text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: false }).decode(bytes.subarray(0, end));
    } catch {
        return null;
    }
    return text.includes('\0') ? null : text;
}
