// Pure: the oldest Claude Code the chat runs a turn on. A turn's command line carries flags and settings that Claude Code
// reads from this version on and an older one skips without a word (`claudeArguments`), so a turn on an older host would
// run without what they hold. The hosts' listing marks such a host and the chat refuses a turn on it; a version that
// cannot be read is held the same way. Nothing here starts a process; the runner it is given does.

import type { Programs, RunResult } from './programs.ts';

/** The oldest Claude Code the chat runs a turn on. A key provider runs on Claude Code, so it holds for those too. */
export const claudeFloor = '2.1.257';

/**
 * How long reading a host's version may take: generous, since a host's first start after an install or an update can
 * be slow. The version is read again at each listing of the hosts and before each turn, never remembered.
 */
export const versionLimitMs = 30_000;

const versionShape = /\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?/;

const namesClaudeCode = /claude code/i;

/**
 * How much of each stream the version is looked for in. The pattern is unanchored, and over a long run of digits with no
 * dot it backtracks for a time that grows with the square of the run, so it is never given more than this.
 */
const readWindow = 4096;

/**
 * The version in what `--version` printed, standard output first, then standard error, each read in its first 4 KiB:
 * the first three dotted numbers on a line that names Claude Code, in any letter case, whatever else stands on it, so
 * another program's version, a date or an address printed before it never stands for it; when no line names Claude
 * Code beside a number, the first three dotted numbers anywhere. A pre-release part after a hyphen is kept. A program
 * that ended with a code other than zero, or was stopped at its time limit, said no version.
 */
export function printedVersion(result: RunResult): string | null {
    return versionNamed(result, namesClaudeCode);
}

const namesCodex = /codex/i;

/**
 * Codex's version in what `codex --version` printed (`codex-cli 0.122.0`), read as Claude Code's is: the first three
 * dotted numbers on a line that names Codex, else anywhere; none from a program that did not end with zero.
 */
export function printedCodexVersion(result: RunResult): string | null {
    return versionNamed(result, namesCodex);
}

function versionNamed(result: RunResult, names: RegExp): string | null {
    if (result.exit !== 0) return null;
    const text = `${result.stdout.slice(0, readWindow)}\n${result.stderr.slice(0, readWindow)}`;
    const named = text.split(/\r?\n/).find((line) => names.test(line) && versionShape.test(line));
    return versionShape.exec(named ?? text)?.[0] ?? null;
}

const isVersionRead = (name: string, args: readonly string[]) => name === 'claude' && args.length === 1 && args[0] === '--version';

/**
 * The runner with at most one `claude --version` in flight: a read asked for while one runs, by a listing of the hosts
 * or a turn, waits on that read and takes its answer; once it has answered nothing is kept, and the next is a new read.
 * Every other program runs as it is asked.
 */
export function sharingVersionReads<T extends Pick<Programs, 'run'>>(programs: T): T {
    let reading: Promise<RunResult> | null = null;
    const run: Programs['run'] = (name, args, options) => {
        if (!isVersionRead(name, args)) return programs.run(name, args, options);
        reading ??= programs.run(name, args, options).finally(() => {
            reading = null;
        });
        return reading;
    };
    return { ...programs, run };
}

function comparePart(a: string, b: string): number {
    const numeric = /^\d+$/;
    if (numeric.test(a) && numeric.test(b)) return Number(a) - Number(b);
    if (numeric.test(a)) return -1;
    if (numeric.test(b)) return 1;
    return a < b ? -1 : a > b ? 1 : 0;
}

/**
 * Below zero when `a` comes before `b`: the three numbers compared as numbers, then a pre-release before its release,
 * and two pre-releases part by part, a numeric part as a number and before a word, the shorter first when one is the
 * beginning of the other.
 */
export function compareVersions(a: string, b: string): number {
    const [aCore = '', aPre] = splitPre(a);
    const [bCore = '', bPre] = splitPre(b);
    const aNumbers = aCore.split('.').map(Number);
    const bNumbers = bCore.split('.').map(Number);
    for (let index = 0; index < 3; index++) {
        const difference = (aNumbers[index] ?? 0) - (bNumbers[index] ?? 0);
        if (difference !== 0) return difference;
    }
    if (aPre === undefined || bPre === undefined) return aPre === bPre ? 0 : aPre === undefined ? 1 : -1;
    const aParts = aPre.split('.');
    const bParts = bPre.split('.');
    for (let index = 0; index < Math.min(aParts.length, bParts.length); index++) {
        const difference = comparePart(aParts[index] ?? '', bParts[index] ?? '');
        if (difference !== 0) return difference;
    }
    return aParts.length - bParts.length;
}

function splitPre(version: string): [string, string | undefined] {
    const hyphen = version.indexOf('-');
    return hyphen < 0 ? [version, undefined] : [version.slice(0, hyphen), version.slice(hyphen + 1)];
}

/**
 * The sentence a Claude Code below the floor is listed and refused with, naming the version found, the floor and the
 * cure; null at the floor or above it. A version that could not be read (null) is held as below the floor.
 */
export function floorSentence(version: string | null): string | null {
    if (version === null) {
        return `The version of Claude Code could not be read, so the chat does not run on it. Run \`claude --version\` in a terminal to see it; the chat needs ${claudeFloor} or newer, and \`claude update\` updates it. Then check again.`;
    }
    if (compareVersions(version, claudeFloor) >= 0) return null;
    return `Claude Code ${version} is older than ${claudeFloor}, the oldest version the chat runs on. Update Claude Code, for example by running \`claude update\` in a terminal, then check again.`;
}

/**
 * The first Claude Code whose command line takes `--no-chrome`, which every turn carries from it on (`claudeArguments`);
 * below it the turn's settings alone keep the browser out. Anthropic's changelog,
 * https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md (read 2026-11-12), never says which version
 * added the flag; the first entry that names it is 2.1.285's: "Fixed `claude remote-control --help` saying
 * `--[no-]chrome` defaults to the machine's `/chrome` setting". https://code.claude.com/docs/en/cli-reference lists it,
 * "Disable Chrome browser integration for this session", with no version. So the flag waits for the first version the
 * changelog shows knowing it.
 */
export const noChromeFrom = '2.1.285';

/**
 * The oldest Codex the chat runs a turn on: the first whose `exec` takes `--ignore-user-config`, which every Codex turn
 * carries (`codexArguments`), so the person's `config.toml`, and the MCP servers it names, never reach the chat. OpenAI,
 * https://github.com/openai/codex/releases/tag/rust-v0.122.0 (read 2026-11-12): "#18646 feat: add `--ignore-user-config`
 * and `--ignore-rules`", and no earlier release names it; `codex-rs/exec/src/cli.rs` at that tag declares it global to
 * `exec`, `resume` included: "Do not load `$CODEX_HOME/config.toml`; auth still uses `CODEX_HOME`."
 */
export const codexFloor = '0.122.0';

/**
 * The sentence a Codex turn below `codexFloor` is refused with, naming the version found, the floor and the cure; null at
 * the floor or above it. A version that could not be read (null) is held as below the floor.
 */
export function codexFloorSentence(version: string | null): string | null {
    if (version === null) {
        return `The version of Codex could not be read, so the chat does not run on it. Run \`codex --version\` in a terminal to see it; the chat needs ${codexFloor} or newer. Update Codex, then send again.`;
    }
    if (compareVersions(version, codexFloor) >= 0) return null;
    return `Codex ${version} is older than ${codexFloor}, the oldest version the chat runs on. Update Codex, then send again.`;
}
