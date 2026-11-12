// The Reply skills in the chat folder's two skills folders, Claude Code's and Codex's: deployed by the Reply CLI and
// recorded with their digests outside the chat folder (`chat-agent-folder.ts`), by the Connect card's sign-in and, while
// no record is kept, at the desktop's start for a person already signed in.

import { existsSync } from 'node:fs';
import { cp, mkdtemp, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { recordReplySkills, replyRecord } from './chat-agent-folder.ts';
import type { Logger } from './log.ts';
import type { DesktopPaths } from './paths.ts';
import type { Programs, RunResult } from './programs.ts';
import { parseReplyAuth } from './setup-model.ts';

/** How the routine runs the Reply CLI: by its verb's words, for the log, and its arguments. */
export type RunReply = (verb: string, args: readonly string[], options: { cwd?: string; timeoutMs?: number }) => Promise<RunResult>;

/** How long one deployment of the Reply skills may take. */
export const replySkillsLimitMs = 5 * 60_000;

/**
 * Deploys the Reply skills into both skills folders and records them; the exit code of `reply skills install`. The Reply
 * CLI writes them as plain folders only for hosts other than Claude Code, so they are written to a folder of ours for one
 * of those and the Reply ones copied across; the sales skills it adds with them are an older copy of Jason's own and are
 * left out. Every `reply-*` folder that stood there is removed first, never read or kept, and only what the CLI wrote is
 * copied and recorded. On a failure nothing is changed and nothing is recorded.
 */
export async function deployReplySkills(run: RunReply, paths: DesktopPaths): Promise<number> {
    const targets = [join(paths.chat, '.claude', 'skills'), join(paths.chat, '.agents', 'skills')];
    const place = await mkdtemp(join(tmpdir(), 'jason-reply-skills-'));
    try {
        const answer = await run('skills install', ['skills', 'install', 'adapter', '--project', '--agent', 'codex'], { cwd: place, timeoutMs: replySkillsLimitMs });
        if (answer.exit !== 0) return answer.exit;
        const source = join(place, '.agents', 'skills');
        const written = (await readdir(source).catch(() => [] as string[])).filter((name) => name.startsWith('reply-'));
        for (const target of targets) {
            for (const name of await readdir(target).catch(() => [] as string[])) {
                if (name.startsWith('reply-')) await rm(join(target, name), { recursive: true, force: true });
            }
            for (const name of written) await cp(join(source, name), join(target, name), { recursive: true });
        }
        recordReplySkills(paths.chat, paths.skillsRecords);
        return 0;
    } finally {
        await rm(place, { recursive: true, force: true });
    }
}

/**
 * At the start, in the step that deploys Jason's skills: when no record of the Reply skills is kept, the Reply CLI is on
 * the PATH and `reply auth status` says it is signed in, the Reply skills are deployed and recorded as the sign-in does.
 * Soft: a failure is logged by its exit code and the start goes on without them, and the next start tries again, since
 * the record is still absent. Not signed in, or no Reply CLI: nothing is run past the status, and nothing is recorded.
 */
export async function replySkillsAtStart(programs: Pick<Programs, 'find' | 'run'>, paths: DesktopPaths, log: Logger): Promise<void> {
    if (existsSync(join(paths.skillsRecords, replyRecord))) return;
    if (programs.find('reply') === null) return;
    const run: RunReply = async (verb, args, options) => {
        const started = Date.now();
        const result = await programs.run('reply', args, options);
        log.info('program_exited', { Program: 'reply', Verb: verb, Exit: result.exit, ElapsedMs: Date.now() - started });
        return result;
    };
    try {
        const status = await run('auth status', ['auth', 'status', '--json'], { timeoutMs: 30_000 });
        if (!parseReplyAuth(status.stdout).signedIn) return;
        const exit = await deployReplySkills(run, paths);
        log.info('skills_deployed', { Target: 'chat_reply', Exit: exit });
    } catch {
        log.info('skills_deployed', { Target: 'chat_reply', Exit: -1 });
    }
}
