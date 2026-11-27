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
 * The variables reply-cli reads to choose the account a run acts as: its profile, a key that comes before any stored
 * sign-in, its team, and where its profiles and sign-ins are kept. The desktop names the account of each run by
 * `--profile` alone, as Jason's Reply plugin does, whose runs never see these, so a profile acts in the team the Reply CLI
 * holds for it.
 */
export const replyAccountVariables = ['REPLY_PROFILE', 'REPLY_API_KEY', 'REPLY_TEAM_ID', 'REPLY_CONFIG_DIR'] as const;

/**
 * The environment of the `reply` the desktop starts: the desktop's own less `replyAccountVariables`, whatever letter case
 * their names were written in, with the label as `withReplyClient` sets it.
 */
export function replyEnvironment(env: Readonly<Record<string, string>>, bundleVersion: string | null): Record<string, string> {
    const kept = Object.fromEntries(Object.entries(env).filter(([name]) => !(replyAccountVariables as readonly string[]).includes(name.toUpperCase())));
    return withReplyClient(kept, bundleVersion);
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
