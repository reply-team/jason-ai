/**
 * The update of an installed Reply CLI the desktop makes at its start, where that CLI is older than the one Jason's Reply
 * plugin needs (`Setup.updateReplyAtStart`): the switch it answers to, the sentences the card says of it, and the moment
 * `main.ts` wires it to. Pure but for the moment, which only waits and asks.
 */

import { readFileSync } from 'node:fs';
import type { Mode } from './mode.ts';
import type { RunResult } from './programs.ts';
import { parseInstallationSettings, versionIn } from './setup-model.ts';

// ---- The switch ----------------------------------------------------------------------------------------------------

/** What the switch says: the update may run, the person turned it off, or it could not be read, where nothing is run. */
export type UpdateSwitch = 'on' | 'off' | 'unread';

const switchPath = 'update:checkenabled';
const variablePrefix = 'jason_';

/** Every value the settings hold at the path, its keys read without regard to case, nested or joined by a colon. */
function valuesAt(record: Record<string, unknown>, before = ''): unknown[] {
    const found: unknown[] = [];
    for (const [name, value] of Object.entries(record)) {
        const path = `${before}${name.toLowerCase()}`;
        if (path === switchPath) found.push(value);
        else if (switchPath.startsWith(`${path}:`) && typeof value === 'object' && value !== null && !Array.isArray(value)) found.push(...valuesAt(value as Record<string, unknown>, `${path}:`));
    }
    return found;
}

/** One value as the runtime's configuration binds a switch: true or false, or either word in any case; null for any other. */
function switchValue(value: unknown): boolean | null {
    const word = String(value).trim().toLowerCase();
    return word === 'true' ? true : word === 'false' ? false : null;
}

/**
 * Pure: `Update:CheckEnabled` as the desktop reads it, from its own environment and the installation's settings file's text,
 * null where there is no file. False in either place turns the update off: a variable that says true does not turn on what
 * the file turned off, though the runtime's own configuration would take the variable. Neither is on, the runtime's default.
 * A file that does not parse, or a value in either place that is not a switch, is `unread`, whatever else is said. A
 * variable set only for a runtime started elsewhere is not in this environment.
 */
export function updateSwitch(env: Readonly<Record<string, string | undefined>>, text: string | null): UpdateSwitch {
    let inFile: unknown[];
    try {
        inFile = valuesAt(parseInstallationSettings(text));
    } catch {
        return 'unread';
    }
    const inEnvironment = Object.entries(env)
        .filter(([name, value]) => value !== undefined && name.toLowerCase().startsWith(variablePrefix) && name.slice(variablePrefix.length).replaceAll('__', ':').toLowerCase() === switchPath)
        .map(([, value]) => value);
    const said = [...inEnvironment, ...inFile].map(switchValue);
    if (said.includes(null)) return 'unread';
    return said.includes(false) ? 'off' : 'on';
}

/** The switch as it stands now; a settings file that is there and cannot be read is `unread`. */
export function readUpdateSwitch(env: Readonly<Record<string, string | undefined>>, settingsFile: string): UpdateSwitch {
    let text: string | null;
    try {
        text = readFileSync(settingsFile, 'utf8');
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') return 'unread';
        text = null;
    }
    return updateSwitch(env, text);
}

// ---- What the card says ---------------------------------------------------------------------------------------------

const lineLimit = 200;

/**
 * Pure: the first line npm printed, its errors before its output, as one plain line for the card: no colour codes, no
 * control or invisible characters, cut, and with no full stop of its own. It goes to the page alone, never to the log.
 */
export function npmLine(result: RunResult): string {
    for (const printed of [result.stderr, result.stdout]) {
        for (const each of printed.split(/[\r\n]+/)) {
            const line = each
                .replace(/\u001b\[[0-9;?]*[ -/]*[@-~]/g, '')
                .replace(/[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\s]+/gu, ' ')
                .trim();
            if (line === '') continue;
            const letters = [...line];
            return letters.length > lineLimit ? `${letters.slice(0, lineLimit).join('').trimEnd()}…` : line.replace(/[.]+$/, '');
        }
    }
    return `npm ended with code ${result.exit}`;
}

export const startUpdateSentences = {
    updated: (version: string): string => `Jason updated the Reply CLI to ${version} when it started.`,
    failed: (line: string): string => `Jason tried to update the Reply CLI when it started and could not: ${line}.`,
    stillOld: (version: string): string =>
        `Jason updated the Reply CLI when it started, but the one in use is still ${version}: it was installed another way. Update it the way you installed it.`,
} as const;

/** What the failed sentence carries where npm ended well and the Reply CLI then said no version. */
export const versionUnsaid = 'the Reply CLI did not say its version after it';

/** The version of the Reply CLI the runtime found at its last load, as `jason plugin list` printed it; null where it says none. */
export function runtimeReplyVersion(stdout: string): string | null {
    try {
        const raw = JSON.parse(stdout) as { plugins?: { id?: unknown; capabilities?: { exec?: { requested?: { name?: unknown; version?: unknown }[] | null } | null } | null }[] };
        const requested = raw.plugins?.find((each) => each.id === 'reply')?.capabilities?.exec?.requested ?? [];
        const version = requested.find((each) => each.name === 'reply')?.version;
        return typeof version === 'string' ? versionIn(version) : null;
    } catch {
        return null;
    }
}

// ---- The moment ----------------------------------------------------------------------------------------------------

export interface ReplyUpdateMomentDeps {
    /** Resolves once the machine set-up's runs at this start have ended, its finishing step among them. */
    setUp(): Promise<void>;
    update(): Promise<unknown>;
    failed(): void;
}

/**
 * The start, once the runtime is up and the window loaded: in Live alone, once the machine set-up has ended, the update is
 * asked for in the background. In the demo nothing is asked or read. Nothing it meets stops the start; a failure is only told.
 */
export function replyUpdateAtStart(mode: Mode, deps: ReplyUpdateMomentDeps): void {
    if (mode !== 'live') return;
    void (async () => {
        await deps.setUp().catch(() => undefined);
        await deps.update();
    })().catch(() => deps.failed());
}
