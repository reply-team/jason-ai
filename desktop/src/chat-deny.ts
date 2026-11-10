// The jason commands the chat's agent never runs: the verbs that are a person's own word and the verbs that change the
// installation. They live in one file beside the desktop's package, read by the desktop at each turn and by the CLI's
// tests, so the list the host is fenced with is the list the CLI's own table is checked against.

/** The deny list's file, relative to the desktop's application directory. */
export const denyListFile = 'chat-deny-list.txt';

const command = /^[a-z][a-z-]*( [a-z][a-z-]*)*$/;

/**
 * The commands the file names, in its order: one per line, blank lines and `#` lines skipped. A line that is not a
 * command of lower-case words, or a command named twice, refuses the whole file, so a typo never becomes a gap.
 */
export function parseDenyList(text: string): string[] {
    const entries: string[] = [];
    text.split('\n').forEach((raw, index) => {
        const line = raw.trim();
        if (line === '' || line.startsWith('#')) return;
        if (!command.test(line)) throw new Error(`The deny list's line ${index + 1} is not a jason command of words.`);
        if (entries.includes(line)) throw new Error(`The deny list names "${line}" twice.`);
        entries.push(line);
    });
    return entries;
}

/**
 * The host's deny rules for the list: each command and everything after it, and every jason command that starts with
 * an option, since `jason --actor human:x approval approve` would otherwise pass the `jason` allow rule untouched.
 */
export function denyRules(entries: readonly string[]): string[] {
    return [...entries.map((entry) => `Bash(jason ${entry}:*)`), 'Bash(jason -*)'];
}
