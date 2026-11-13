/**
 * Bringing up the installation the CLI uses, through the CLI's own verbs: a runtime started only when none is up, the
 * UI build installed only when the runtime is the bundle's version and `jason status` says its UI is not ok, the skills deployed from the bundle, and the link
 * the CLI's pairing prints. Nothing here starts a runtime as the desktop's own child or writes the runtime's files.
 */

import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { AgentFolderUnowned, afterSkillsDeploy, beforeSkillsDeploy, ownAgentFolder, replyRecord, skillsFolders } from './chat-agent-folder.ts';
import type { Programs } from './programs.ts';
import { replySkillsAtStart } from './reply-skills.ts';
import type { SkillsGate } from './skills-gate.ts';
import type { Cli, CliResult } from './cli.ts';
import { pairingLink, runtimeOrigin } from './descriptor.ts';
import type { Logger } from './log.ts';
import type { DesktopPaths } from './paths.ts';
import type { DesktopSettings } from './settings.ts';
import { runtimeVersionIn, statusFacts } from './setup-model.ts';

/** The verbs the desktop runs, by the words that name them; the log names a verb by these and nothing else. */
const verbs = ['status', 'runtime start', 'runtime status', 'runtime stop', 'runtime autostart', 'ui install', 'ui open', 'skills install', 'plugin install', 'plugin reload', 'plugin list', 'route list', 'campaign get', 'project get', 'session open', '--version'] as const;

export function verbOf(args: readonly string[]): string {
    const two = args.slice(0, 2).join(' ');
    return (verbs as readonly string[]).find((verb) => verb === two || verb === args[0]) ?? 'other';
}

/** The CLI with each run logged by its verb, its exit code and how long it took; never its arguments or its output. */
export function loggedCli(cli: Cli, log: Logger, clock: () => number = Date.now): Cli {
    return {
        async run(args, options) {
            const started = clock();
            const result = await cli.run(args, options);
            log.info('cli_exited', { Verb: verbOf(args), Exit: result.exit, ElapsedMs: clock() - started });
            return result;
        },
    };
}

export class StartFailed extends Error {
    readonly step: string;
    readonly exit: number;

    constructor(step: string, result: CliResult | null, said: string) {
        super(said);
        this.step = step;
        this.exit = result?.exit ?? -1;
    }
}

export interface Started {
    origin: string;
    /** The pairing link `jason ui open --no-open` printed. */
    link: string;
    runtimeVersion: string | null;
    /** Whether the runtime is another version than the bundle's, in which case its own UI was loaded as it is. */
    versionsDiffer: boolean;
}

export interface InstallationDeps {
    cli: Cli;
    paths: DesktopPaths;
    log: Logger;
    /** The folder the application carries the executable and the skills package in. */
    bundle: string;
    bundleVersion: string;
    settings: DesktopSettings;
    saveSettings(settings: DesktopSettings): void;
    /** The programs on the PATH, the Reply CLI among them, for the Reply skills a start deploys (`replySkillsAtStart`). */
    programs?: Pick<Programs, 'find' | 'run'>;
    /** The pass that puts the chat folder's top right (`ownAgentFolder`); a test gives one that fails. */
    ownChatFolder?: (chat: string, records: string) => boolean | void;
    /** Held from that pass to the last deploy into the chat folder, so a turn sent meanwhile waits (`SkillsGate`). */
    gate?: SkillsGate;
}

function said(result: CliResult): string {
    return (result.stdout.trim() || result.stderr.trim()).slice(-2000);
}

function readOrigin(file: string): string | null {
    try {
        return runtimeOrigin(readFileSync(file, 'utf8'));
    } catch {
        return null;
    }
}

/** The `--feed` arguments for the verbs that take one, from the desktop's settings. */
export function feedArguments(settings: DesktopSettings): string[] {
    return settings.feed === null ? [] : ['--feed', settings.feed];
}

export async function startInstallation(deps: InstallationDeps): Promise<Started> {
    const { cli, paths, log } = deps;

    let facts = statusFacts((await cli.run(['status'])).stdout);
    if (!facts.runtimeUp) {
        log.info('runtime_starting', {});
        const start = await cli.run(['runtime', 'start'], { timeoutMs: 180_000 });
        if (start.exit !== 0) {
            log.error('runtime_start_failed', { Exit: start.exit });
            throw new StartFailed('runtime start', start, said(start) || 'jason runtime start did not start a runtime.');
        }
        facts = statusFacts((await cli.run(['status'])).stdout);
    }

    const origin = readOrigin(paths.runFile);
    if (origin === null) throw new StartFailed('run file', null, 'The runtime is up, but its run file names no address on this machine.');
    if (facts.runtimeUp) log.info('runtime_found', { Origin: origin });

    const runtimeVersion = runtimeVersionIn((await cli.run(['runtime', 'status'])).stdout);

    if (!facts.sameVersion) {
        log.warn('versions_differ', { RuntimeVersion: runtimeVersion ?? 'unknown', BundleVersion: deps.bundleVersion });
    } else if (facts.uiNeeded) {
        // Only when ui_module is not ok, so a start fetches nothing while the runtime serves this build's UI. An install
        // that fails stops the start only when there is no UI to show at all.
        const ui = await cli.run(['ui', 'install', ...feedArguments(deps.settings)], { timeoutMs: 300_000 });
        log.info('ui_installed', { Exit: ui.exit });
        if (ui.exit !== 0 && !facts.uiServed) throw new StartFailed('ui install', ui, said(ui) || 'jason ui install placed no UI, and the runtime serves none.');
    }

    mkdirSync(paths.chat, { recursive: true });
    // The skills gate is held from the pass over the chat folder to the last deploy into it, however the start ends: a turn
    // sent meanwhile waits for it.
    const release = deps.gate?.hold();
    try {
        // The chat folder's top is put right before anything is deployed into it, so a link a last turn left at .claude or
        // .agents is gone first; the deploy itself makes each skills folder a real one (`beforeSkillsDeploy`). A pass that
        // cannot finish is said by its code, and this start deploys nothing into the chat folder, the Reply skills included,
        // and keeps no mark that it did: the next start tries again, and a turn refuses on the same folder anyway.
        let topRight = true;
        let removedRecorded = false;
        try {
            removedRecorded = (deps.ownChatFolder ?? ownAgentFolder)(paths.chat, paths.skillsRecords) === true;
        } catch (error) {
            if (!(error instanceof AgentFolderUnowned)) throw error;
            log.warn('agent_folder_unowned', { Code: error.code });
            topRight = false;
        }
        // A chat skills folder whose deployment the desktop keeps no record of is deployed afresh (`beforeSkillsDeploy`), and
        // the Reply skills with it: their record goes too, so the step after the deploys copies them in again.
        // A recorded skill the pass found changed was removed with it: the folder is then as good as one with no record, so its
        // records go and this start deploys afresh.
        if (removedRecorded) for (const host of skillsFolders) rmSync(join(paths.skillsRecords, host.record), { force: true });
        const unrecorded = skillsFolders.some((host) => !existsSync(join(paths.skillsRecords, host.record)));
        if (unrecorded && topRight) rmSync(join(paths.skillsRecords, replyRecord), { force: true });
        if (deps.settings.skillsDeployedFor !== deps.bundleVersion || unrecorded) {
            let deployed = true;
            if (facts.sameVersion) {
                const roles = await cli.run(['skills', 'install', '--roles-only', '--source', deps.bundle], { timeoutMs: 300_000 });
                log.info('skills_deployed', { Target: 'roles', Exit: roles.exit });
                deployed = roles.exit === 0;
            } else {
                deployed = false;
            }
            // --root names the directory the skills are deployed into. Claude Code started in the chat folder with project
            // settings reads them from .claude/skills there, and Codex from .agents/skills; the Reply skills are copied into
            // both folders.
            // The CLI's record of each deployment lives under <data>/desktop/skills-records, outside the chat folder: it is put
            // beside the skills for the CLI to read, and moved out again once the CLI has written it.
            if (topRight) {
                const [claudeSkills, codexSkills] = skillsFolders;
                if (claudeSkills === undefined || codexSkills === undefined) throw new Error('The chat has two skills folders.');
                beforeSkillsDeploy(paths.chat, paths.skillsRecords, claudeSkills);
                const chat = await cli.run(['skills', 'install', '--root', join(paths.chat, '.claude', 'skills'), '--host', 'claude_code', '--source', deps.bundle], { timeoutMs: 300_000 });
                afterSkillsDeploy(paths.chat, paths.skillsRecords, claudeSkills);
                log.info('skills_deployed', { Target: 'chat_claude_code', Exit: chat.exit });
                beforeSkillsDeploy(paths.chat, paths.skillsRecords, codexSkills);
                const codex = await cli.run(['skills', 'install', '--root', join(paths.chat, '.agents', 'skills'), '--host', 'codex', '--source', deps.bundle], { timeoutMs: 300_000 });
                afterSkillsDeploy(paths.chat, paths.skillsRecords, codexSkills);
                log.info('skills_deployed', { Target: 'chat_codex', Exit: codex.exit });
                if (deployed && chat.exit === 0 && codex.exit === 0) deps.saveSettings({ ...deps.settings, skillsDeployedFor: deps.bundleVersion });
            }
        }
        // The Reply skills, for a person already signed in, while the desktop keeps no record of them.
        if (topRight && deps.programs !== undefined) await replySkillsAtStart(deps.programs, paths, log);
    } finally {
        release?.();
    }

    const open = await cli.run(['ui', 'open', '--no-open']);
    const link = open.exit === 0 ? pairingLink(open.stdout, origin) : null;
    if (link === null) throw new StartFailed('ui open', open, said(open) || 'jason ui open printed no link to the runtime’s UI.');

    return { origin, link, runtimeVersion, versionsDiffer: !facts.sameVersion };
}
