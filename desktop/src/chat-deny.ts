// The fence around the chat agent's jason commands, two files beside the desktop's package, read by the desktop at each
// turn and by the CLI's tests, and a third file beside them: the instruction text the chat folder carries for the hosts.
// The deny list names the jason commands the agent never runs (the verbs that are a person's own word and the verbs that
// change the installation) and the Runtime API operations those verbs call. The allow list names the verbs it runs
// without asking: every verb of the CLI the deny list does not cover, written from the CLI's command tree by its tests.
// Any other jason command goes to the person's card.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/** The deny list's file, relative to the desktop's application directory. */
export const denyListFile = 'chat-deny-list.txt';

/** The allow list's file, relative to the desktop's application directory. */
export const allowListFile = 'chat-allow-list.txt';

/** The chat's instruction text, relative to the desktop's application directory. */
export const instructionsFile = 'chat-instructions.md';

const command = /^[a-z][a-z-]*( [a-z][a-z-]*)*$/;
const operation = /^[a-z][a-z_]*\.[a-z][a-z_]*$/;

/** What the file names: the jason commands, and the Runtime API operations they call. */
export interface DenyList {
    commands: readonly string[];
    operations: readonly string[];
}

/**
 * The commands and the operations the file names, each in its order: one per line, blank lines and `#` lines skipped,
 * an operation told from a command by its dot. A line that is neither, or a line named twice, refuses the whole file, so
 * a typo never becomes a gap.
 */
export function parseDenyList(text: string): DenyList {
    const commands: string[] = [];
    const operations: string[] = [];
    text.split('\n').forEach((raw, index) => {
        const line = raw.trim();
        if (line === '' || line.startsWith('#')) return;
        const into = command.test(line) ? commands : operation.test(line) ? operations : null;
        if (into === null) throw new Error(`The deny list's line ${index + 1} is neither a jason command of words nor an operation.`);
        if (into.includes(line)) throw new Error(`The deny list names "${line}" twice.`);
        into.push(line);
    });
    return { commands, operations };
}

/**
 * The verbs the allow list names, in its order: one per line, blank lines and `#` lines skipped. A line that is not a
 * command of lower-case words, or one named twice, refuses the whole file.
 */
export function parseAllowList(text: string): string[] {
    const verbs: string[] = [];
    text.split('\n').forEach((raw, index) => {
        const line = raw.trim();
        if (line === '' || line.startsWith('#')) return;
        if (!command.test(line)) throw new Error(`The allow list's line ${index + 1} is not a jason command of words.`);
        if (verbs.includes(line)) throw new Error(`The allow list names "${line}" twice.`);
        verbs.push(line);
    });
    return verbs;
}

/**
 * The fence of one turn: the deny list's commands and operations, the allow list's verbs, and the instruction text the
 * chat folder carries as the hosts' memory file, or null when the application carries none.
 */
export interface ChatFence extends DenyList {
    allowed: readonly string[];
    instructions: string | null;
}

/** A file of the fence that could not be read or parsed, by its name; the error it met is its cause. */
export class FenceUnreadable extends Error {
    readonly file: string;

    constructor(file: string, cause: unknown) {
        super(`The chat file ${file} could not be read.`, { cause });
        this.name = 'FenceUnreadable';
        this.file = file;
    }
}

/**
 * Reads the fence from the folder that carries its files. A list that cannot be read or parsed throws; an instruction
 * text that is not there is null, since a turn runs without it, and one that is there and cannot be read throws. What
 * throws is a `FenceUnreadable` that names the file.
 */
export function readFence(directory: string): ChatFence {
    const read = <T>(file: string, take: (path: string) => T): T => {
        try {
            return take(join(directory, file));
        } catch (error) {
            throw new FenceUnreadable(file, error);
        }
    };
    const denied = read(denyListFile, (path) => parseDenyList(readFileSync(path, 'utf8')));
    const allowed = read(allowListFile, (path) => parseAllowList(readFileSync(path, 'utf8')));
    return { ...denied, allowed, instructions: read(instructionsFile, readIfThere) };
}

/** The sentence a turn is refused with when the fence cannot be read: which of its files failed, in the desktop's words. */
export function fenceRefusal(error: unknown): string {
    return error instanceof FenceUnreadable && error.file === instructionsFile
        ? 'The chat’s instruction text could not be read.'
        : 'The chat’s allow and deny lists could not be read.';
}

function readIfThere(file: string): string | null {
    try {
        return readFileSync(file, 'utf8');
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
        throw error;
    }
}

/**
 * The host's allow rules for the allow list: each verb and whatever follows it. A jason command none of them begins, an
 * option before the verb or a doubled space say, is not allowed by the host and goes to the permission tool.
 */
export function allowRules(verbs: readonly string[]): string[] {
    return verbs.map((verb) => `Bash(jason ${verb}:*)`);
}

/**
 * The host's deny rules for the list: each command and everything after it, and every jason command that starts with
 * an option, since `jason --actor human:x approval approve` would otherwise pass the `jason` allow rule untouched.
 */
export function denyRules(entries: readonly string[]): string[] {
    return [...entries.map((entry) => `Bash(jason ${entry}:*)`), 'Bash(jason -*)'];
}
