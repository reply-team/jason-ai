// The fence around the chat agent's jason commands, two files beside the desktop's package, read by the desktop at each
// turn and by the CLI's tests. The deny list names the jason commands the agent never runs (the verbs that are a person's
// own word and the verbs that change the installation) and the Runtime API operations those verbs call. The allow list
// names the verbs it runs without asking: every verb of the CLI the deny list does not cover, written from the CLI's
// command tree by its tests. Any other jason command goes to the person's card.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/** The deny list's file, relative to the desktop's application directory. */
export const denyListFile = 'chat-deny-list.txt';

/** The allow list's file, relative to the desktop's application directory. */
export const allowListFile = 'chat-allow-list.txt';

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

/** The fence of one turn: the deny list's commands and operations, and the allow list's verbs. */
export interface ChatFence extends DenyList {
    allowed: readonly string[];
}

/** Reads the fence from the folder that carries the two files; a file that cannot be read or parsed throws. */
export function readFence(directory: string): ChatFence {
    const denied = parseDenyList(readFileSync(join(directory, denyListFile), 'utf8'));
    const allowed = parseAllowList(readFileSync(join(directory, allowListFile), 'utf8'));
    return { ...denied, allowed };
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
