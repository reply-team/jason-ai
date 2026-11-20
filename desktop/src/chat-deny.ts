// The fence around the chat agent's jason commands, three files beside the desktop's package, read by the desktop at each
// turn and by the CLI's tests, and a fourth file beside them: the instruction text the chat folder carries for the hosts.
// The deny list names the jason commands a chat in Plan only never runs (the verbs that are a person's own word and the
// verbs that change the installation) and the Runtime API operations those verbs call; outside Plan only it refuses
// nothing, the forms of a person's word run as the allow list's verbs do, and the rest are the mode's (`modeFence`). The allow
// list names the verbs it runs without asking: every verb of the CLI the deny list does not cover, written from the
// CLI's command tree by its tests. Any other jason command goes to the person's card. The plan list names the allow list's verbs that change nothing,
// the only jason commands a chat in Plan only runs, written by the same tests from a declaration of each verb.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { ChatMode } from './chat-modes.ts';

/** The deny list's file, relative to the desktop's application directory. */
export const denyListFile = 'chat-deny-list.txt';

/** The allow list's file, relative to the desktop's application directory. */
export const allowListFile = 'chat-allow-list.txt';

/** The plan list's file, relative to the desktop's application directory. */
export const planListFile = 'chat-plan-list.txt';

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
 * The verbs the plan list names, in its order, read as the allow list is; a verb that is not on the allow list refuses
 * the whole file, so the plan list never lets through what the allow list does not.
 */
export function parsePlanList(text: string, allowed: readonly string[]): string[] {
    const verbs = parseAllowList(text);
    const stray = verbs.find((verb) => !allowed.includes(verb));
    if (stray !== undefined) throw new Error(`The plan list names "${stray}", which is not on the allow list.`);
    return verbs;
}

/**
 * The fence of one turn: the deny list's commands and operations, the allow list's verbs, the plan list's verbs, and the
 * instruction text the chat folder carries as the hosts' memory file, or null when the application carries none.
 */
export interface ChatFence extends DenyList {
    allowed: readonly string[];
    /** The allow list's verbs that change nothing: the only jason commands a chat in Plan only runs. */
    reads: readonly string[];
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
    const reads = read(planListFile, (path) => parsePlanList(readFileSync(path, 'utf8'), allowed));
    return { ...denied, allowed, reads, instructions: read(instructionsFile, readIfThere) };
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
 * The deny list's verbs that are a person's own word and that a chat outside Plan only runs with no card, as the allow
 * list's verbs are run: the host's own rule lets each through (`modeFence`), and the chat's instructions keep each to the
 * person's word. One principle draws the line: outside Full access, an act that lets sends go without the person's
 * approval is a card by whichever door, and an act that only narrows needs none. So an approval approved or rejected, a
 * standing approval taken back, a question answered and a campaign started run with no card; a standing approval set,
 * which releases sends nobody reads, does not. They stay on the deny list, which Plan only still reads.
 */
export const personsWordVerbs: readonly string[] = ['approval approve', 'approval reject', 'approval unset-standing', 'decision answer', 'campaign start'];

/**
 * The preferences a person's word sets or unsets with no card outside Plan only: the sender's identity and the tone of
 * voice, which shape what is written and release nothing. An allow list of keys, so every other key, the send modes and
 * the interactive host among them, and any key added later, is a card.
 */
export const personsWordPreferenceKeys: readonly string[] = ['sender.identity', 'tone_of_voice'];

/**
 * Every form the host runs by itself outside Plan only for a person's word: each verb of `personsWordVerbs`, and
 * `preference set` and `preference unset` each followed by one key of `personsWordPreferenceKeys`, so the host's rule is
 * key by key. Anthropic, https://code.claude.com/docs/en/permissions (read 2026-11-20): "The `:*` suffix is an equivalent
 * way to write a trailing wildcard, so `Bash(ls:*)` matches the same commands as `Bash(ls *)`", and "`Bash(ls *)`
 * requires a space after `ls`, so `lsof` doesn't match", so `Bash(jason preference set tone_of_voice:*)` takes that key
 * as a whole word and no longer key that begins with it. A key spelled any other way, or put after an option, is not
 * matched and goes to the permission tool, whose card it gets.
 */
export const personsWordForms: readonly string[] = [
    ...personsWordVerbs,
    ...personsWordPreferenceKeys.flatMap((key) => [`preference set ${key}`, `preference unset ${key}`]),
];

/**
 * The allow list's verbs that a chat outside Plan only does not run by itself, since each can let sends go without the
 * person's approval: a reply's verdict (`reply record`) can lift an opt-out and vouch for a draft under a standing
 * approval. The CLI's tests write it on the allow list; the turn takes it off, so it is a card in Ask first and Auto-edit
 * and runs in Full access. Plan only never had it, being no read.
 */
export const cardedVerbs: readonly string[] = ['reply record'];

/**
 * The fence one turn runs under in its mode. Plan only: the fence as it is read, every command and operation of the deny
 * list refused. Every other mode: no command or operation of the deny list is refused. The forms of a person's word
 * (`personsWordForms`) join the allow list and run with no card, and `cardedVerbs` leave it; every other command of the
 * deny list goes to the person as any jason command the host does not run by itself does: a card in Ask first and
 * Auto-edit, and in Full access what Full access does with such a card.
 */
export function modeFence(fence: ChatFence, mode: ChatMode): ChatFence {
    if (mode === 'plan_only') return fence;
    return { ...fence, commands: [], operations: [], allowed: [...fence.allowed.filter((verb) => !cardedVerbs.includes(verb)), ...personsWordForms] };
}

/**
 * The host's allow rules for the allow list: each verb and whatever follows it. A jason command none of them begins, an
 * option before the verb or a doubled space say, is not allowed by the host and goes to the permission tool.
 */
export function allowRules(verbs: readonly string[]): string[] {
    return verbs.map((verb) => `Bash(jason ${verb}:*)`);
}

/**
 * The nouns whose help the chat reads: the first word of each verb of two words or more on the allow list or the plan
 * list, each once, in the order the lists first name it. A verb of one word (`status`, `logs`) is no noun.
 */
export function helpNouns(allowed: readonly string[], reads: readonly string[]): string[] {
    const nouns: string[] = [];
    for (const verb of [...allowed, ...reads]) {
        const words = verb.split(' ');
        if (words.length > 1 && !nouns.includes(words[0] as string)) nouns.push(words[0] as string);
    }
    return nouns;
}

/**
 * The host's allow rules for the nouns' help: exactly `jason <noun> --help`, with no `:*`, so the rule matches that
 * command as written and no longer one. The same rules in every mode: help prints and changes nothing.
 */
export function helpRules(nouns: readonly string[]): string[] {
    return nouns.map((noun) => `Bash(jason ${noun} --help)`);
}

/**
 * The host's deny rules for the list: each command and everything after it, and every jason command that starts with
 * an option, since `jason --actor human:x approval approve` would otherwise pass the `jason` allow rule untouched.
 */
export function denyRules(entries: readonly string[]): string[] {
    return [...entries.map((entry) => `Bash(jason ${entry}:*)`), 'Bash(jason -*)'];
}
