/**
 * The variable reply-cli reads to name the program a call came through: it puts the value before its own product token
 * in the User-Agent of its requests to Reply. A label, not an input: nothing defends it, and nothing relies on it.
 */
export const replyClientVariable = 'REPLY_CLIENT';

/**
 * The shape reply-cli takes for the value (its `src/config.ts`): one product token, a lower-case name of up to 32
 * letters, digits or dashes, a slash, and a version of up to 32 letters, digits, dots, plus or minus. reply-cli leaves a
 * value of any other shape out without a word.
 */
const clientShape = /^[a-z][a-z0-9-]{0,31}\/[0-9A-Za-z.+-]{1,32}$/;

/**
 * The one place the label is built: the harness's token, as the Reply plugin sends it, with the bundle's version
 * (`version.json`, known before any runtime answers) less its build metadata, the plus sign and what follows it. Null
 * when the build carries no bundle version, and when the value would not have the shape reply-cli takes.
 */
export function replyClient(bundleVersion: string | null): string | null {
    if (bundleVersion === null) return null;
    const value = `jason-harness/${bundleVersion.split('+')[0]}`;
    return clientShape.test(value) ? value : null;
}

/**
 * An environment with its `REPLY_CLIENT` taken out, whatever letter case its name was written in, as PATH is, and then
 * set to the label when there is one, so a child never sees two, and never an inherited one.
 */
export function withReplyClient(env: Readonly<Record<string, string>>, bundleVersion: string | null): Record<string, string> {
    const next: Record<string, string> = {};
    for (const [name, value] of Object.entries(env)) if (name.toUpperCase() !== replyClientVariable) next[name] = value;
    const label = replyClient(bundleVersion);
    if (label !== null) next[replyClientVariable] = label;
    return next;
}
