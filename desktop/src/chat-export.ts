/**
 * `/export <file name>`: a message of a chat that writes the conversation's words to a file instead of starting a turn.
 * It is caught in `Chat.send` before anything of a turn is built, and listed nowhere: no menu entry and no help line.
 * Any other first word that begins with `/` goes to the host as before.
 *
 * What is written is Markdown: the conversation's title as a heading, the moment it was written, and in order the
 * person's words under "You" and the AI's under "AI", as the record keeps them; no step, card, error, line or path, so
 * nothing the chat does not show as words. Where it is written: a bare file name alone, into the campaign's folder for a
 * campaign's chat and the harness root otherwise, as the runtime recorded them and only when they are on this disk. A
 * name with a folder in it, a parent, a drive or a character a system cannot hold is refused. The file is made new or
 * not at all: a name already there, a link included, is refused and never followed, and nothing is written over.
 */

import { closeSync, constants, fsyncSync, lstatSync, openSync, rmSync, statSync, writeSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import type { Conversation } from './chat-conversations.ts';

/** A sentence the export refuses with, for the chat to say as a send's error. */
export class ExportRefused extends Error {
    constructor(sentence: string) {
        super(sentence);
        this.name = 'ExportRefused';
    }
}

const word = /^\/export(?:\s+([\s\S]*))?$/;

/** The name after a message's first word when that word is `/export` alone, '' when none is given; null otherwise. */
export function exportName(text: string): string | null {
    const found = word.exec(text.trim());
    return found === null ? null : (found[1] ?? '').trim();
}

const withFolder = 'Give a file name alone, such as notes.md, with no folder in it: the file is written in this chat’s folder.';
const notAName = 'That is not a file name the desktop writes.';
/** The names Windows keeps for its devices, with or without an extension. */
const deviceName = /^(?:con|prn|aux|nul|com[0-9¹²³]|lpt[0-9¹²³])(?:\..*)?$/i;
/** The longest name the file systems the desktop runs on take. */
const nameLimit = 255;

/** Why a name is refused, in the desktop's sentence; null for a bare file name. */
export function nameRefusal(name: string): string | null {
    if (name === '') return 'Name the file to write, such as /export notes.md.';
    if (/[\\/]/.test(name) || name === '.' || name === '..' || isAbsolute(name) || /^[A-Za-z]:/.test(name)) return withFolder;
    if (/[\u0000-\u001f\u007f<>:"|?*]/.test(name) || /[. ]$/.test(name) || deviceName.test(name) || name.length > nameLimit) return notAName;
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

/** Whether a conversation has words to write. */
function hasWords(conversation: Pick<Conversation, 'entries'>): boolean {
    return conversation.entries.some((entry) => entry.kind === 'user' || entry.kind === 'text');
}

/** The folder when it is an absolute path to a folder on this disk; null otherwise. */
function folderOnDisk(path: string | null): string | null {
    if (path === null || !isAbsolute(path)) return null;
    try {
        return statSync(path).isDirectory() ? path : null;
    } catch {
        return null;
    }
}

/**
 * Writes `text` as a new file `name` in `folder` and answers its path. Never over anything: a name already there, a
 * link at it included, is refused before the open and again by the open itself, which makes the file new or fails.
 */
export function writeExport(folder: string, name: string, text: string): string {
    const refusal = nameRefusal(name);
    if (refusal !== null) throw new ExportRefused(refusal);
    const path = join(folder, name);
    const there = `A file of that name is already there, and the desktop never writes over a file: ${path}`;
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
        closeSync(handle);
        // The file this export made is taken away again, rather than left half written.
        rmSync(path, { force: true });
        throw new ExportRefused(unwritten(error));
    }
    closeSync(handle);
    return path;
}

function unwritten(error: unknown): string {
    const code = (error as NodeJS.ErrnoException | null)?.code;
    return `The file could not be written (${typeof code === 'string' ? code : 'unknown'}).`;
}

/**
 * The export of a conversation: its words, taken as they are now, written as a new file into the folder `folderOf`
 * answers, null when there is none. Answers the sentence the chat says; a refusal throws `ExportRefused`.
 */
export async function exportConversation(
    conversation: Pick<Conversation, 'title' | 'entries' | 'dropped' | 'campaign_id'>,
    name: string,
    folderOf: () => Promise<string | null>,
    at: Date,
): Promise<string> {
    const refusal = nameRefusal(name);
    if (refusal !== null) throw new ExportRefused(refusal);
    if (!hasWords(conversation)) throw new ExportRefused('This conversation has nothing to write yet.');
    // The words are taken before the folder is asked for, so a turn that begins meanwhile adds nothing to this file.
    const text = exportText(conversation, at);
    const folder = folderOnDisk(await folderOf());
    if (folder === null) {
        throw new ExportRefused(
            conversation.campaign_id === null
                ? 'The harness root is not known, so the file has nowhere to go.'
                : 'The folder of this chat’s campaign is not known, so the file has nowhere to go.',
        );
    }
    return `Written: ${writeExport(folder, name, text)}`;
}
