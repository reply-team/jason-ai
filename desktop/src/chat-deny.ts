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
 * person's word. Below Claude Code's floor of Full access one principle draws the line: outside Full access, an act that
 * lets sends go without the person's approval is a card by whichever door, and an act that only narrows needs none. So
 * an approval approved or rejected, a standing approval taken back, a question answered and a campaign started run with
 * no card; a standing approval set, which releases sends nobody reads, does not. From the floor on, where the hook rides
 * an Ask first or Auto-edit turn, every jason verb runs with no card but the forms of `askingCardedForms`. They stay on
 * the deny list, which Plan only still reads.
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
 * approval. The CLI's tests write it on the allow list; the turn takes it off, so it reaches the permission model in Ask
 * first and Auto-edit, which runs it with no card where the hook rides the turn and shows a card below the floor of Full
 * access; Full access runs it. Plan only never had it, being no read.
 */
export const cardedVerbs: readonly string[] = ['reply record'];

/**
 * A jason verb that keeps a card in Ask first and Auto-edit where the turn's hook rides them: in every form, or, where it
 * names options, in a form that gives one of them, by its name alone, after `=` or after `:`.
 */
export interface CardedForm {
    readonly verb: string;
    readonly options?: readonly string[];
}

/**
 * The forms that keep a card in Ask first and Auto-edit where the turn's hook rides them, from Claude Code's floor of Full
 * access on: taking the installation off the machine, placing an archive over it, writing it into an archive, putting
 * back the executable an update replaced and granting a plugin what it may reach, in every form; and every verb that
 * changes the installation in a form that names another source, place, ref or version, by one rule: the plain form,
 * which takes the release's own, runs with no card, and a form that names another keeps one. So `update apply` with a
 * version or a feed, `plugin install` from another feed or over the package there, `skills install` and `skills update`
 * from another source, feed, root or ref, `ui install` from another feed, and `install` told an executable to retire;
 * the search path a terminal has, which `install --terminal-path` gives, names no source and runs with no card. A
 * program a role will be run by is a source of code too: an option of `profile create` and `profile update` that names
 * the program or the command a role will be run by keeps a card, and so does `--file`, whose body can name either.
 * Every other jason verb runs there with no card (`ChatScope.hooked`), the deny list's and `cardedVerbs` among them,
 * each written as the chat's instructions write a person's word; below the floor nothing changed, and Plan only runs
 * none of them.
 */
export const askingCardedForms: readonly CardedForm[] = [
    { verb: 'uninstall' },
    { verb: 'import' },
    { verb: 'export' },
    { verb: 'update rollback' },
    { verb: 'update apply', options: ['--version', '--feed'] },
    { verb: 'plugin grant' },
    { verb: 'plugin install', options: ['--feed', '--replace'] },
    { verb: 'skills install', options: ['--source', '--feed', '--root', '--ref'] },
    { verb: 'skills update', options: ['--source', '--feed', '--root', '--ref'] },
    { verb: 'ui install', options: ['--feed'] },
    { verb: 'install', options: ['--retire'] },
    { verb: 'profile create', options: ['--program', '--cli-command', '--file'] },
    { verb: 'profile update', options: ['--program', '--cli-command', '--file'] },
];

/**
 * The CLI's own command tree: every verb of it but a hidden one, by its words after `jason`, held to the tree
 * `CliApp.BuildRootCommand` builds by DesktopDenyListTests, so a verb added to the CLI is red there until it is named here.
 * Where the hook rides an Ask first or Auto-edit turn, a jason whose words do not begin with one of these, or with a
 * noun's help, is a card (`ChatScope.hooked`): the CLI's parser takes a word before the noun that names no verb, the
 * root's own name or a directive, and the executable starts modes that are no verb of the CLI at all.
 */
export const cliCommandTree: readonly string[] = [
    'approval approve', 'approval get', 'approval list', 'approval list-standing', 'approval reject', 'approval set-standing',
    'approval unset-standing', 'audience availability', 'audience filter-values', 'audience preview', 'block clear', 'block get', 'block list',
    'campaign activity', 'campaign activity-by-period', 'campaign add-contacts', 'campaign archive', 'campaign audience', 'campaign create',
    'campaign get', 'campaign list', 'campaign list-contacts', 'campaign pause', 'campaign remove-contacts', 'campaign set-send-mode',
    'campaign start', 'campaign summary', 'campaign update', 'campaign update-context', 'contact archive', 'contact create', 'contact get',
    'contact list', 'contact thread', 'contact threads', 'contact update', 'decision answer', 'decision get', 'decision list', 'decision raise',
    'doctor', 'events backfill', 'events signal', 'events sources', 'export', 'import', 'install', 'journal append', 'journal kinds', 'journal list',
    'journal watch', 'launches lift', 'logs', 'notification disable', 'notification enable', 'operation describe', 'plugin grant', 'plugin install', 'plugin list',
    'plugin reload', 'plugin revoke', 'preference list', 'preference set', 'preference unset', 'profile create', 'profile disable', 'profile enable',
    'profile get', 'profile list', 'profile set-default', 'profile unset-default', 'profile update', 'project create', 'project get', 'project link',
    'project list', 'project update', 'reply count', 'reply record', 'report get', 'report list', 'report submit', 'role add', 'role list',
    'role set-profile', 'rolenote get', 'rolenote list', 'rolenote set', 'route list', 'route resolve', 'route set', 'route unset',
    'runtime autostart disable', 'runtime autostart enable', 'runtime autostart status', 'runtime drain', 'runtime restart', 'runtime resume',
    'runtime start', 'runtime status', 'runtime stop', 'sender list', 'session open', 'skills install', 'skills update', 'status',
    'subscription list', 'subscription rewind', 'subscription runs', 'suppression add', 'suppression list', 'suppression remove', 'ui install',
    'ui open', 'ui revoke', 'ui tokens', 'uninstall', 'update apply', 'update check', 'update rollback', 'update status', 'workitem cancel',
    'workitem complete', 'workitem count', 'workitem create', 'workitem get', 'workitem heartbeat', 'workitem list', 'workitem resume',
    'workitem review', 'workitem set-result', 'workitem update', 'workitem wait',
];

/**
 * The fence one turn runs under in its mode. Plan only: the fence as it is read, every command and operation of the deny
 * list refused. Every other mode: no command or operation of the deny list is refused. The forms of a person's word
 * (`personsWordForms`) join the allow list and run with no card, and `cardedVerbs` leave it. Every other command of the
 * deny list goes to the permission model as any jason command the host does not run by itself does: where the hook rides
 * the turn it runs with no card but the forms of `askingCardedForms`, and below the floor of Full access it is a card in
 * Ask first and Auto-edit; Full access runs it.
 */
export function modeFence(fence: ChatFence, mode: ChatMode): ChatFence {
    if (mode === 'plan_only') return fence;
    return { ...fence, commands: [], operations: [], allowed: [...fence.allowed.filter((verb) => !cardedVerbs.includes(verb)), ...personsWordForms] };
}

/**
 * The verbs of the CLI that read a request's content from a file or from standard input, by an option its one reader of
 * such content serves (`--file`, `--value-file`, `--note-file`, `--result-file`). Below the floor of Full access no hook
 * rides an Ask first or Auto-edit turn to read where such content comes from, so the host is given no allow rule for a
 * form of one of these verbs (`withoutRequestFiles`): each reaches the permission tool, which refuses a redirect from
 * the installation's own data and runs a plain form with no card, as the host's rule would (`plainForm`), so it costs no
 * card the rule did not. Held to the CLI's command tree by DesktopDenyListTests, so a verb that
 * gains such an option is red there until it is named here.
 */
export const requestFileVerbs: readonly string[] = [
    'campaign add-contacts', 'campaign archive', 'campaign create', 'campaign pause', 'campaign remove-contacts', 'campaign start', 'campaign update',
    'campaign update-context', 'contact archive', 'contact create', 'contact update', 'journal append', 'preference set', 'profile create',
    'profile update', 'project create', 'project update', 'report submit', 'role add', 'rolenote set', 'workitem cancel', 'workitem complete',
    'workitem create', 'workitem resume', 'workitem set-result', 'workitem update',
];

/** The forms whose verb reads no request's content (`requestFileVerbs`): a form is a verb, or a verb and its key. */
export function withoutRequestFiles(forms: readonly string[]): string[] {
    return forms.filter((form) => !requestFileVerbs.some((verb) => form === verb || form.startsWith(`${verb} `)));
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
 * The host's deny rules for the list: each command and everything after it, and, where no hook rides the turn, every
 * jason command that starts with an option, since `jason --actor human:x approval approve` would otherwise pass the
 * `jason` allow rule untouched. Where the hook rides it (`hooked`), such a command reaches the permission model instead,
 * which runs jason's own help and version with no card and refuses any other option first (`optionRefusal`), and Full
 * access runs it.
 */
export function denyRules(entries: readonly string[], hooked = false): string[] {
    return [...entries.map((entry) => `Bash(jason ${entry}:*)`), ...(hooked ? [] : ['Bash(jason -*)'])];
}
