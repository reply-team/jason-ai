/**
 * `/export <file name>`: a message of a chat that writes the conversation's words to a file instead of starting a turn.
 * It is caught in `Chat.send` before anything of a turn is built, and listed nowhere: no menu entry and no help line.
 * Any other first word that begins with `/` goes to the host as before.
 *
 * What is written is Markdown: the conversation's title as a heading, the moment it was written, and in order the
 * person's words under "You" and the AI's under "AI", as the record keeps them; no step, card, error, line or path, so
 * nothing the chat does not show as words. Where it is written: a bare file name alone, given on the message's first line
 * and nothing after it, into the campaign's folder for a campaign's chat and the harness root otherwise, as the runtime
 * recorded them and only while each carries the runtime's marker of it. A name with a folder in it, a parent, a drive, a
 * character or a device name a system cannot hold, or a name the harness keeps for its own files, is refused. The file is
 * made new or not at all: a name already there, a link included, is refused and never followed, and nothing is written
 * over.
 */

import { closeSync, constants, fsyncSync, lstatSync, openSync, rmSync, writeSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import type { Conversation } from './chat-conversations.ts';
import { memoryFileName, type MemoryPlace } from './chat-memory.ts';
import { markedFolder } from './chat-memory-files.ts';

/** A sentence the export refuses with, for the chat to say as a send's error. */
export class ExportRefused extends Error {
    constructor(sentence: string) {
        super(sentence);
        this.name = 'ExportRefused';
    }
}

/** The line breaks a name may not hold: the ASCII two and the three Unicode adds. */
const lineBreak = /[\r\n\u0085\u2028\u2029]/;
const word = /^\/export(?=[\s\u0085]|$)/;

/**
 * The words after a message's first word when that word is `/export` alone, '' when none is given; null otherwise.
 * The spaces before the name are taken off, of every kind but a line break, which stays for `nameRefusal` to refuse.
 */
export function exportName(text: string): string | null {
    const trimmed = text.trim();
    return word.test(trimmed) ? trimmed.slice('/export'.length).replace(/^[^\S\r\n\u2028\u2029]+/, '') : null;
}

const withFolder = 'Give a file name alone, such as notes.md, with no folder in it: the file is written in the campaign’s folder, or the harness root for a chat about no campaign.';
const notAName = 'That is not a file name the desktop writes.';
/** The names Windows keeps for its devices, as the stem before the first dot, with the spaces it ignores after it. */
const deviceStem = /^(?:con|prn|aux|nul|conin\$|conout\$|com[0-9¹²³]|lpt[0-9¹²³])$/i;
/**
 * The names the harness keeps for its own files in a folder: the person's memory file, each import file the runtime
 * writes beside it (`AgentHosts.ImportFiles`, held to the runtime's table by a test), and the marker's folder.
 */
const harnessNames: ReadonlySet<string> = new Set([memoryFileName, 'CLAUDE.md', 'GEMINI.md', '.jason'].map((name) => name.toLowerCase()));
/** The names the runtime makes in the harness root: the projects' folder, `projects/` (docs/projects.md). */
const rootNames: ReadonlySet<string> = new Set(['projects']);
/** The longest name the file systems the desktop runs on take, in UTF-8 bytes. */
const nameLimit = 255;

/**
 * Why a name is refused in the folder of `place`, the harness root or a campaign's, in the desktop's sentence; null for
 * a bare file name.
 */
export function nameRefusal(name: string, place: 'root' | 'campaign' = 'campaign'): string | null {
    if (name === '') return 'Name the file to write, such as /export notes.md.';
    if (lineBreak.test(name)) return 'Give the file name on one line after /export.';
    if (/[\\/]/.test(name) || name === '.' || name === '..' || isAbsolute(name) || /^[A-Za-z]:/.test(name)) return withFolder;
    if (
        /[\u0000-\u001f\u007f<>:"|?*]/.test(name) ||
        /[. ]$/.test(name) ||
        deviceStem.test((name.split('.')[0] ?? '').trimEnd()) ||
        Buffer.byteLength(name, 'utf8') > nameLimit
    ) {
        return notAName;
    }
    if (harnessNames.has(name.toLowerCase()) || (place === 'root' && rootNames.has(name.toLowerCase()))) return 'That name is one the harness keeps for its own files. Choose another.';
    return null;
}

/** The conversation's words as Markdown: its title, the moment, and the person's and the AI's words in order. */
export function exportText(conversation: Pick<Conversation, 'title' | 'entries' | 'dropped'>, at: Date): string {
    const parts = [`# ${conversation.title}`, `Written by Jason Desktop at ${at.toISOString()}.`];
    if (conversation.dropped > 0) parts.push('The earliest messages of this conversation were no longer kept.');
    for (const entry of conversation.entries) {
        if (entry.kind === 'user') parts.push('## You', entry.text);
        else if (entry.kind === 'text') parts.push('## AI', entry.text);
    }
    return parts.join('\n\n') + '\n';
}

/**
 * The first half of an export, before anything is asked of the runtime: the name checked, and the conversation's words
 * taken as they are now, so a turn that begins meanwhile adds nothing to the file. Answers the text to write.
 */
export function exportStart(conversation: Pick<Conversation, 'title' | 'entries' | 'dropped' | 'campaign_id'>, name: string, at: Date): string {
    const refusal = nameRefusal(name, conversation.campaign_id === null ? 'root' : 'campaign');
    if (refusal !== null) throw new ExportRefused(refusal);
    if (!conversation.entries.some((entry) => entry.kind === 'user' || entry.kind === 'text')) throw new ExportRefused('This conversation has nothing to write yet.');
    return exportText(conversation, at);
}

/**
 * The second half: `text` written as a new file `name` in the place's folder, once the folder is found to carry the
 * runtime's marker of that place; null, or a place unmarked, has nowhere to go. Answers the sentence the chat says, or
 * null, with nothing written, when `open` says the window closed meanwhile.
 */
export async function exportInto(place: MemoryPlace | null, campaign: boolean, name: string, text: string, open: () => boolean = () => true): Promise<string | null> {
    const marked = place !== null && (await markedFolder(place));
    if (!open()) return null;
    if (place === null || !marked) {
        throw new ExportRefused(
            campaign
                ? 'The folder of this chat’s campaign is not known, or not on this disk, so the file has nowhere to go.'
                : 'The harness root is not known, or not on this disk, so the file has nowhere to go.',
        );
    }
    return `Written: ${writeExport(place.folder, name, text)}`;
}

/**
 * Writes `text` as a new file `name` in `folder` and answers its path. Never over anything: a name already there, a
 * link at it included, is refused before the open and again by the open itself, which makes the file new or fails.
 */
export function writeExport(folder: string, name: string, text: string): string {
    const refusal = nameRefusal(name);
    if (refusal !== null) throw new ExportRefused(refusal);
    const path = join(folder, name);
    const there = `A file or folder of that name is already there, and the desktop never writes over it: ${path}`;
    let found = true;
    try {
        lstatSync(path);
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw new ExportRefused(unwritten(error));
        found = false;
    }
    if (found) throw new ExportRefused(there);
    let handle: number;
    try {
        handle = openSync(path, constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | (constants.O_NOFOLLOW ?? 0), 0o666);
    } catch (error) {
        throw new ExportRefused((error as NodeJS.ErrnoException).code === 'EEXIST' ? there : unwritten(error));
    }
    try {
        const bytes = Buffer.from(text, 'utf8');
        let at = 0;
        while (at < bytes.length) at += writeSync(handle, bytes, at, bytes.length - at);
        fsyncSync(handle);
    } catch (error) {
        throw new ExportRefused(cleanedUp(handle, path, unwritten(error)));
    }
    try {
        closeSync(handle);
    } catch (error) {
        throw new ExportRefused(cleanedUp(null, path, unwritten(error)));
    }
    return path;
}

/**
 * The file this export made and could not finish is closed and taken away again, rather than left half written; the
 * sentence says where it was left when it could not be.
 */
function cleanedUp(handle: number | null, path: string, sentence: string): string {
    if (handle !== null) {
        try {
            closeSync(handle);
        } catch {
            // Closed or not, the remove below is tried.
        }
    }
    try {
        rmSync(path, { force: true });
        return sentence;
    } catch {
        return `${sentence.slice(0, -1)}, and what was written of it is left at ${path}.`;
    }
}

function unwritten(error: unknown): string {
    const code = (error as NodeJS.ErrnoException | null)?.code;
    return `The file could not be written (${typeof code === 'string' ? code : 'unknown'}).`;
}
