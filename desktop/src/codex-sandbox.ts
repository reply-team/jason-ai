// The check that, on Windows, Codex's sandbox keeps a command from reading the installation's data directory. A
// Codex turn asks no permission and the desktop has no fence of its own around it, so on Windows its sandbox is what
// holds it; the chat runs a Codex turn there only when this check has shown the sandbox holds. The one start here is
// Codex's (`runSandboxCheck`), which the chat makes before the turn (`Chat`, `#codexSandboxHolds`).
//
// Why it is checked rather than assumed. Every Windows turn names the elevated sandbox (`codexWindowsSandbox`), which
// needs a setup an administrator approves, and OpenAI does not say that naming it fails where it is not set up; it says
// a fall to the other kind can happen. OpenAI, https://learn.chatgpt.com/docs/windows/windows-sandbox: "`elevated`: ...
// Requires administrator-approved setup."; "`unelevated`: ... doesn't support denied read paths."; and "The
// `unelevated` implementation may be selected in configuration or used as a fallback when `elevated` setup isn't
// available." So the desktop looks at the property itself: a command in the sandbox tries to read the runtime's run
// file, and says only whether it could.
//
// What it runs. `codex sandbox`, "Run arbitrary commands inside Codex-provided macOS, Linux, or Windows sandboxes"
// (https://developers.openai.com/codex/cli/reference), with "--config, -c key=value Configuration overrides applied
// before launching the sandbox (repeatable)" and the command after `--`. It is given exactly the settings the turn sets
// by `-c` (`codexTurnSettings`), and the turn's `--sandbox read-only` as the setting `sandbox_mode`, since the
// subcommand has no flag for it. Unlike `codex exec` it has no `--ignore-user-config`: it still reads the rest of the
// person's `config.toml`, and only what `-c` sets is set over it, the sandbox's kind among them.
//
// What it is taken to say. The command prints a random marker whatever happens, and a second random one only if it
// read the run file; it prints nothing of the file. The sandbox holds only when the first marker, alone, is all it
// printed, within the time limit: the second marker anywhere means the file was read, and anything else (no marker, a
// time limit, a start that failed, other output) proves nothing, so no Codex turn runs on it.

import { randomBytes } from 'node:crypto';
import { win32 } from 'node:path';
import { codexTurnSettings } from './chat-hosts.ts';
import { runFile, type RunResult } from './programs.ts';

/** How long the check may take: a first start of the sandbox after an install or an update can be slow. */
export const sandboxLimitMs = 30_000;

/** What the chat shows while the check runs. */
export const checkingSandbox = 'Checking Codex\'s sandbox…';

/** Why a Codex turn does not run on Windows where the check did not show the sandbox holds. */
export const sandboxSentence =
    'Codex on Windows needs its elevated sandbox, set up once by an administrator with `codex sandbox setup --elevated` (https://learn.chatgpt.com/docs/windows/windows-sandbox), or chat on Claude Code.';

/** Why a Codex turn does not run on Windows when there is no run file for the check to read. */
export const sandboxUncheckedSentence = 'The chat could not check Codex\'s sandbox, because Jason\'s runtime is not running. Start it, then send again.';

/** A Windows Codex turn the check did not let run; its message is the sentence the person reads. */
export class SandboxUnproven extends Error {
    constructor(sentence: string) {
        super(sentence);
        this.name = 'SandboxUnproven';
    }
}

export interface SandboxCheck {
    /** The arguments of `codex`. */
    args: string[];
    /** Printed whatever happens. */
    ran: string;
    /** Printed only when the command read the run file. */
    read: string;
}

/** A character cmd reads as more than part of a name: the path is not checked, and so no Codex turn runs. */
const cmdSpecial = /[%^&|<>"!\r\n]/;

/**
 * The check of the run file at `runFile`, with fresh markers; null when cmd would read its path as more than a name, or
 * when `systemRoot` (the environment's `SystemRoot`) is no absolute path. cmd is started by its full path under the
 * system root, so a cmd.exe in the chat folder, where the check runs, is never the one started.
 */
export function sandboxCheck(runFile: string, systemRoot: string, markers?: { ran: string; read: string }): SandboxCheck | null {
    if (runFile === '' || cmdSpecial.test(runFile)) return null;
    if (!/^[A-Za-z]:\\/.test(systemRoot) || cmdSpecial.test(systemRoot)) return null;
    const cmd = win32.join(systemRoot, 'System32', 'cmd.exe');
    const ran = markers?.ran ?? `ran-${randomBytes(12).toString('hex')}`;
    const read = markers?.read ?? `read-${randomBytes(12).toString('hex')}`;
    const settings = [...codexTurnSettings('win32'), 'sandbox_mode="read-only"'];
    return {
        args: ['sandbox', ...settings.flatMap((setting) => ['-c', setting]), '--', cmd, '/d', '/c', 'echo', ran, '&', 'type', runFile, '>nul', '2>nul', '&&', 'echo', read],
        ran,
        read,
    };
}

/**
 * What the check's run says: `holds` when the first marker alone was printed, `reads` when the second was printed
 * anywhere, `unproven` otherwise. The exit code is not read beyond a stop or a failed start (-1): the command's last
 * act fails where the file is refused, and its code is then not zero.
 */
export function sandboxAnswer(result: RunResult, check: SandboxCheck): 'holds' | 'reads' | 'unproven' {
    if (result.stdout.includes(check.read) || result.stderr.includes(check.read)) return 'reads';
    if (result.exit === -1) return 'unproven';
    const lines = result.stdout.split(/\r?\n/).map((line) => line.trim()).filter((line) => line !== '');
    return lines.length === 1 && lines[0] === check.ran ? 'holds' : 'unproven';
}

/**
 * Runs the check with Codex as a turn's host is started (the file and prefix the programs module found, no shell), in
 * the turn's environment and folder, and says what it found. The desktop starts Codex; Codex starts cmd in its sandbox.
 */
export async function runSandboxCheck(
    launch: { file: string; prefix: readonly string[] },
    check: SandboxCheck,
    options: { env: Record<string, string>; cwd: string; limitMs: number; signal: AbortSignal },
): Promise<'holds' | 'reads' | 'unproven'> {
    const result = await runFile(launch.file, [...launch.prefix, ...check.args], options.env, { cwd: options.cwd, timeoutMs: options.limitMs, signal: options.signal });
    return sandboxAnswer(result, check);
}
