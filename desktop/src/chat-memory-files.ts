// The reading of the person's memory files the chat is given (`chat-memory.ts`): the `AGENTS.md` of each level that
// applies, the client's for every chat and the project's and the campaign's for a campaign's chat, each in the folder the
// runtime records for it (the harness root it reports as marked, the folder on the project and on the campaign), never a
// folder worked out here. A level is skipped, saying nothing, where that folder is not an absolute path, is a link or no
// folder, or does not carry the marker of that level and that id (`.jason/marker.json`, as the runtime writes it); and
// where its file is a link or anything but a regular file, is empty, or is not UTF-8 text. No link is followed: the
// folder, its `.jason` and the file are each looked at without following one, and the file read is the one looked at.
// One bounded read: a file longer than `memoryFileLimit` gives its first bytes and says it was cut.

import { createHash } from 'node:crypto';
import { constants, type BigIntStats } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import { posix, win32 } from 'node:path';
import { memoryFileLimit, memoryFileName, type MemoryFile, type MemoryLevel, type MemoryPlace } from './chat-memory.ts';

/** The most a marker file may hold; the runtime writes one line of a few dozen bytes. */
const markerLimit = 4096;

const markerKinds: Readonly<Record<MemoryLevel, string>> = { client: 'root', project: 'project', campaign: 'campaign' };

/** The memory file a place holds, or null where it holds none the chat is given (the header says which reasons). */
export async function readMemoryFile(place: MemoryPlace, platform: NodeJS.Platform = process.platform): Promise<MemoryFile | null> {
    const paths = platform === 'win32' ? win32 : posix;
    if (typeof place.folder !== 'string' || !paths.isAbsolute(place.folder)) return null;
    if (!(await isPlainFolder(place.folder))) return null;
    if (!(await isPlainFolder(paths.join(place.folder, '.jason')))) return null;
    const marker = await readNoFollow(paths.join(place.folder, '.jason', 'marker.json'), markerLimit);
    if (marker === null || marker.more || !markerNames(marker.bytes, markerKinds[place.level], place.id)) return null;
    const path = paths.join(place.folder, memoryFileName);
    const read = await readNoFollow(path, memoryFileLimit);
    if (read === null) return null;
    const text = textOf(read.bytes, read.more);
    if (text === null || text.trim() === '') return null;
    const digest = `sha256:${createHash('sha256').update(`${place.level}\n${path}\n${read.more ? 'cut' : 'whole'}\n`).update(text, 'utf8').digest('hex')}`;
    return { level: place.level, path, text, cut: read.more, digest };
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
 * A regular file's first bytes, up to `cap`, and whether it holds more; null where the path is a link or anything but a
 * regular file, or where what was opened is not what was looked at (a link or another file put in its place between the
 * two). Opened without following a link where the system can say so, and without waiting, so a pipe does not hold it.
 */
export async function readNoFollow(path: string, cap: number): Promise<{ bytes: Buffer; more: boolean } | null> {
    let before: BigIntStats;
    try {
        before = await lstat(path, { bigint: true });
    } catch {
        return null;
    }
    if (!before.isFile() || before.isSymbolicLink()) return null;
    let handle;
    try {
        handle = await open(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0) | (constants.O_NONBLOCK ?? 0));
    } catch {
        return null;
    }
    try {
        const opened = await handle.stat({ bigint: true });
        if (!opened.isFile() || opened.ino !== before.ino || opened.dev !== before.dev) return null;
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
