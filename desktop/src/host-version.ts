// Pure: the oldest Claude Code the chat runs a turn on. A turn's command line carries flags and settings that Claude Code
// reads from this version on and an older one skips without a word (`claudeArguments`), so a turn on an older host would
// run without what they hold. The hosts' listing marks such a host and the chat refuses a turn on it; a version that
// cannot be read is held the same way. Nothing here starts a process.

import type { RunResult } from './programs.ts';

/** The oldest Claude Code the chat runs a turn on. A key provider runs on Claude Code, so it holds for those too. */
export const claudeFloor = '2.1.257';

/**
 * How long reading a host's version may take: generous, since a host's first start after an install or an update can
 * be slow. The version is read again at each listing of the hosts and before each turn, never remembered.
 */
export const versionLimitMs = 30_000;

const versionShape = /\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?/;

/**
 * The version in what `--version` printed: the first three dotted numbers anywhere on standard output, then standard
 * error, whatever stands around them, with a pre-release part after a hyphen. A program that ended with a code other
 * than zero, or was stopped at its time limit, said no version.
 */
export function printedVersion(result: RunResult): string | null {
    if (result.exit !== 0) return null;
    return versionShape.exec(`${result.stdout}\n${result.stderr}`)?.[0] ?? null;
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
