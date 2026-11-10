// The jason commands the chat's agent never runs: the verbs that are a person's own word and the verbs that change the
// installation, and the Runtime API operations those verbs call. They live in one file beside the desktop's package,
// read by the desktop at each turn and by the CLI's tests, so the list the host is fenced with is the list the CLI's own
// tables are checked against.

/** The deny list's file, relative to the desktop's application directory. */
export const denyListFile = 'chat-deny-list.txt';

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
 * The host's deny rules for the list: each command and everything after it, and every jason command that starts with
 * an option, since `jason --actor human:x approval approve` would otherwise pass the `jason` allow rule untouched.
 */
export function denyRules(entries: readonly string[]): string[] {
    return [...entries.map((entry) => `Bash(jason ${entry}:*)`), 'Bash(jason -*)'];
}
