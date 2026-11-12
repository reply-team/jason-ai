// The check that, on Windows, Codex's sandbox keeps a command from reading the installation's data directory. A
// Codex turn asks no permission and the desktop has no fence of its own around it, so on Windows its sandbox is what
// holds it; the chat runs a Codex turn there only when this check has shown the sandbox holds. The starts here are cmd's,
// outside any sandbox, and Codex's (`runSandboxCheck`), which the chat makes before the turn (`Chat`,
// `#codexSandboxHolds`).
//
// Why it is checked rather than assumed. Every Windows turn names the elevated sandbox (`codexWindowsSandbox`), which
// needs a setup an administrator approves, and OpenAI does not say that naming it fails where it is not set up; it says
// a fall to the other kind can happen. OpenAI, https://learn.chatgpt.com/docs/windows/windows-sandbox: "`elevated`: ...
// Requires administrator-approved setup."; "`unelevated`: ... doesn't support denied read paths."; and "The
// `unelevated` implementation may be selected in configuration or used as a fallback when `elevated` setup isn't
// available." So the desktop looks at the property itself: a command in the sandbox tries to read two files of the data
// directory, and says only whether it could. The two are the runtime's run file, which has an owner-only access list of
// its own, and the desktop's settings file, which keeps the access list the data directory inherits from the profile,
// as the database, the logs and every other file there do; both must be unreadable inside.
//
// What it runs, three times. One cmd line: `echo <ran>`, then for each file `type "<file>" >nul 2>nul && echo <read>`.
// It prints a random marker whatever happens and a marker of each file's own only if that file opened; the files go to
// nul, so nothing of them reaches a pipe in any run. It is handed to cmd in a variable (`checkVariable`), with cmd's
// arguments `/d /c %JASON_SANDBOX_CHECK%`, so no layer between the desktop and cmd quotes it again and every run sees the
// same bytes. First the line runs outside any sandbox, by cmd from the system folder: it must print every marker, which
// shows the line can read both files as written. Then inside Codex's sandbox: the ran marker alone. Then outside once
// more, every marker again, so a file gone or replaced while the check ran is not taken for a sandbox. Any other
// combination proves nothing, and no Codex turn runs on it.
//
// Codex's command. `codex sandbox`, "Run arbitrary commands inside Codex-provided macOS, Linux, or Windows sandboxes"
// (https://developers.openai.com/codex/cli/reference). Its recorded help of 0.167.0-alpha.3.2 (the test's fixture) reads
// `Usage: codex sandbox [OPTIONS] [COMMAND]... [COMMAND]`, with `-c, --config <key=value>` and no platform subcommand;
// the reference groups the command by platform, and an older Codex may want `codex sandbox windows`. So the form with no
// platform word is tried first and, when it printed no ran marker, the form with it; a read marker in either refuses.
// Both are given exactly the settings the turn sets by `-c` (`codexTurnSettings`), and the turn's `--sandbox
// read-only` as the setting `sandbox_mode`, since the subcommand has no flag for it. Unlike `codex exec` it has no
// `--ignore-user-config`: it still reads the rest of the person's `config.toml`, which the turn leaves unread, and only
// what `-c` sets is set over it, the sandbox's kind among them.

import { randomBytes } from 'node:crypto';
import { win32 } from 'node:path';
import { codexTurnSettings } from './chat-hosts.ts';
import { runFile, type RunResult } from './programs.ts';

/** How long each run of the check may take: a first start of the sandbox after an install or an update can be slow. */
export const sandboxLimitMs = 30_000;

/** What the chat shows while the check runs. */
export const checkingSandbox = 'Checking Codex\'s sandbox…';

/** Why a Codex turn does not run on Windows where the check did not show the sandbox holds. */
export const sandboxSentence =
    'Codex on Windows needs its elevated sandbox, set up once by an administrator with `codex sandbox setup --elevated` (https://learn.chatgpt.com/docs/windows/windows-sandbox), or chat on Claude Code.';

/**
 * Why a Codex turn on a provider's key does not run on Windows where the check did not hold: the turn runs under a Codex
 * home of the desktop's own, and where the elevated setup's state lives is not known, so the setup command is no cure.
 */
export const sandboxKeyedSentence = 'Codex on a key cannot be checked on Windows yet: sign in to Codex, or chat on Claude Code.';

/** Why a Codex turn does not run on Windows when there is no run file for the check to read. */
export const sandboxUncheckedSentence = 'The chat could not check Codex\'s sandbox, because Jason\'s runtime is not running. Start it, then send again.';

/** Why a Codex turn does not run on Windows when the desktop's settings file, the check's second file, is not there yet. */
export const sandboxNoSettingsSentence = 'The chat could not check Codex\'s sandbox, because the desktop has not saved its settings yet. Change any setting, then send again.';

/** Why a Codex turn does not run on Windows when a path the check reads holds a character cmd reads as more than a name. */
export const sandboxPathSentence =
    'The chat cannot check Codex\'s sandbox, since the path of Jason\'s data directory holds a character cmd reads as more than a name (one of , ; = % ^ & | < > ! or a quote), so it does not run on Codex here. Chat on Claude Code.';

/** A Windows Codex turn the check did not let run; its message is the sentence the person reads. */
export class SandboxUnproven extends Error {
    constructor(sentence: string) {
        super(sentence);
        this.name = 'SandboxUnproven';
    }
}

/** The variable that hands cmd the check's line, so its quoting is the same in every run. */
export const checkVariable = 'JASON_SANDBOX_CHECK';

export interface SandboxCheck {
    /** cmd, by its full path under the system folder. */
    cmd: string;
    /** cmd's arguments: the line is read from `checkVariable`. */
    inner: string[];
    /** The line, the value of `checkVariable`. */
    line: string;
    /** Codex's arguments, the form without a platform word first. */
    forms: string[][];
    /** Printed whatever happens. */
    ran: string;
    /** Printed each only when its file opened, in the files' order. */
    reads: string[];
}

/**
 * A character cmd reads as more than part of a name: `% ^ & | < > !`, a quote, a line break, and the separators of an
 * internal command's arguments `, ; =`. A path holding one is not checked, and so no Codex turn runs.
 */
const cmdSpecial = /[%^&|<>"!,;=\r\n]/;

/**
 * The check of `files`, with fresh markers unless given; `path` when cmd would read a file's path as more than a name,
 * `root` when `systemRoot` (the environment's `SystemRoot`) is no absolute path of a drive. cmd is started by its full
 * path under the system root, so a cmd.exe in the chat folder, where the check runs, is never the one started.
 */
export function sandboxCheck(files: readonly string[], systemRoot: string, markers?: { ran: string; reads: readonly string[] }): SandboxCheck | 'path' | 'root' {
    if (files.length === 0 || files.some((file) => file === '' || cmdSpecial.test(file))) return 'path';
    if (!/^[A-Za-z]:\\/.test(systemRoot) || cmdSpecial.test(systemRoot)) return 'root';
    const cmd = win32.join(systemRoot, 'System32', 'cmd.exe');
    const ran = markers?.ran ?? `ran-${randomBytes(12).toString('hex')}`;
    const reads = files.map((_, i) => markers?.reads[i] ?? `read-${randomBytes(12).toString('hex')}`);
    const line = [`echo ${ran}`, ...files.map((file, i) => `type "${file}" >nul 2>nul && echo ${reads[i]}`)].join(' & ');
    const inner = ['/d', '/c', `%${checkVariable}%`];
    const settings = [...codexTurnSettings('win32'), 'sandbox_mode="read-only"'].flatMap((setting) => ['-c', setting]);
    return { cmd, inner, line, forms: [['sandbox', ...settings, '--', cmd, ...inner], ['sandbox', 'windows', ...settings, '--', cmd, ...inner]], ran, reads };
}

/** What the check keeps an answer by: the file Codex runs from, the script it is handed when it is one, its version and its home. */
export function sandboxKey(launch: { file: string; prefix: readonly string[] }, version: string, codexHome: string): string {
    return JSON.stringify([launch.file, [...launch.prefix], version, codexHome]);
}

/** The lines a run printed on standard output, each trimmed, the empty ones left out; none for a stop or a failed start. */
function linesOf(result: RunResult): string[] {
    if (result.exit === -1) return [];
    return result.stdout.split(/\r?\n/).map((line) => line.trim()).filter((line) => line !== '');
}

/** Whether a run outside the sandbox printed every marker, each once, in order, and nothing else. */
function readsAll(result: RunResult, check: SandboxCheck): boolean {
    const lines = linesOf(result);
    const expected = [check.ran, ...check.reads];
    return result.exit !== -1 && lines.length === expected.length && lines.every((line, i) => line === expected[i]);
}

/** One run inside the sandbox: a read marker as a whole line, the ran marker alone, no ran marker, or anything else. */
function inside(result: RunResult, check: SandboxCheck): 'reads' | 'holds' | 'none' | 'unproven' {
    if (result.exit === -1) return 'unproven';
    const lines = linesOf(result);
    if (lines.some((line) => check.reads.includes(line))) return 'reads';
    if (check.reads.some((read) => result.stdout.includes(read) || result.stderr.includes(read))) return 'unproven';
    if (lines.length === 1 && lines[0] === check.ran) return 'holds';
    return lines.includes(check.ran) ? 'unproven' : 'none';
}

/** The two starts of a check: cmd outside any sandbox, and Codex with one form's arguments. */
export interface CheckRuns {
    cmd(): Promise<RunResult>;
    codex(args: readonly string[]): Promise<RunResult>;
}

/**
 * The check's two starts: cmd by its full path (`check.cmd`), outside any sandbox, and Codex as a turn's host is started
 * (the file and prefix the programs module found, no shell); each in the turn's environment with the line added, in the
 * chat folder, ended at the limit or at `signal`.
 */
export function checkRuns(
    launch: { file: string; prefix: readonly string[] },
    check: SandboxCheck,
    options: { env: Record<string, string>; cwd: string; limitMs: number; signal: AbortSignal },
): CheckRuns {
    const env = { ...options.env, [checkVariable]: check.line };
    const given = { cwd: options.cwd, timeoutMs: options.limitMs, signal: options.signal };
    return {
        cmd: () => runFile(check.cmd, check.inner, env, given),
        codex: (args) => runFile(launch.file, [...launch.prefix, ...args], env, given),
    };
}

/**
 * Runs the check and says what it found: `holds` only on every marker outside, the ran marker alone inside, and every
 * marker outside again; `reads` when a read marker stood as a line of its own inside; `unproven` otherwise. The form
 * with the platform word is tried only when the one without it printed no ran marker. A read marker seen only on
 * standard error, or inside a longer line, is no read: it proves nothing, and is not kept.
 */
export async function runSandboxCheck(check: SandboxCheck, runs: CheckRuns, signal: AbortSignal): Promise<'holds' | 'reads' | 'unproven'> {
    if (!readsAll(await runs.cmd(), check) || signal.aborted) return 'unproven';
    let held = false;
    for (const form of check.forms) {
        const found = inside(await runs.codex(form), check);
        if (signal.aborted) return 'unproven';
        if (found === 'reads') return 'reads';
        if (found === 'unproven') return 'unproven';
        if (found === 'holds') {
            held = true;
            break;
        }
    }
    if (!held) return 'unproven';
    return readsAll(await runs.cmd(), check) && !signal.aborted ? 'holds' : 'unproven';
}
