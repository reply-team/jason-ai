// The chat folder's agent settings, which the desktop owns as it owns the memory files. Claude Code started in the chat
// folder with project settings reads `.claude/` there, and Codex reads `.agents/skills`; a host also loads the skills of a
// `.claude/` it meets below the folder it started in. The desktop places only skills: those `jason skills install` wrote,
// and the Reply skills the Connect card copies in. What it placed is recorded outside the chat folder, under
// `<data>/desktop/skills-records/`, which the chat neither reads nor writes: the CLI's own record of each deployment,
// moved there from the folder it deployed into, and the Reply skills' files with their digests. Before each turn:
// `.claude/` and `.agents/` hold their `skills/` and nothing else, and in it only the files recorded there with the bytes
// recorded; `.codex/` and the memory files the desktop does not write (`CLAUDE.local.md`, `AGENTS.override.md`) are
// removed from the top; and every `.claude`, `.agents` and `.codex` anywhere below the top is removed. Nothing is
// followed: a link is removed as a link, so nothing outside the chat folder is ever deleted. A turn does not start on a
// folder this could not put right.

import { createHash } from 'node:crypto';
import { copyFileSync, lstatSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The memory files a host reads in the chat folder beside the two the desktop writes, which it never writes. */
export const unwrittenMemoryFiles = ['CLAUDE.local.md', 'AGENTS.override.md'] as const;

/** The record `jason skills install` keeps in the root it deploys into. */
export const skillsRecordFile = '.jason-skills.json';

/** The hosts' skills folders in the chat folder, each with the name its deployment's record goes under. */
export const skillsFolders: readonly { folder: '.claude' | '.agents'; record: string }[] = [
    { folder: '.claude', record: 'claude.json' },
    { folder: '.agents', record: 'agents.json' },
];

/** The record of the Reply skills the Connect card copied, by skills folder. */
export const replyRecord = 'reply.json';

/** The folder names a host loads agent settings or skills from, wherever they stand below the chat folder's top. */
const agentFolders = new Set(['.claude', '.agents', '.codex']);

/** The agent settings folder could not be put right: the system's error code, never its text or a path. */
export class AgentFolderUnowned extends Error {
    readonly code: string;

    constructor(code: unknown) {
        super('The chat’s agent settings folder could not be put right, so the turn was not started. Close anything that has it open and send again.');
        this.name = 'AgentFolderUnowned';
        this.code = typeof code === 'string' && /^E[A-Z]+$/.test(code) ? code : 'unknown';
    }
}

function kind(path: string): 'file' | 'folder' | 'other' | null {
    try {
        const stat = lstatSync(path);
        return stat.isDirectory() ? 'folder' : stat.isFile() ? 'file' : 'other';
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
        throw error;
    }
}

function digest(file: string): string {
    return createHash('sha256').update(readFileSync(file)).digest('hex');
}

/** A path the records may name: relative, forward slashes, no colon, backslash, NUL or `..` segment. */
const recordable = (path: unknown): path is string =>
    typeof path === 'string' && path !== '' && !path.startsWith('/') && !/[\\:\0]/.test(path) && !path.split('/').includes('..');

function readJson(file: string): unknown {
    try {
        return JSON.parse(readFileSync(file, 'utf8'));
    } catch {
        return null;
    }
}

/** The files the records name for one skills folder, by path relative to it, with the digest each was written with. */
function recorded(records: string, folder: (typeof skillsFolders)[number]): Map<string, string> {
    const files = new Map<string, string>();
    const keep = (path: unknown, sha256: unknown) => {
        if (recordable(path) && typeof sha256 === 'string') files.set(path, sha256.toLowerCase());
    };
    const deployed = readJson(join(records, folder.record)) as { packs?: unknown } | null;
    for (const pack of Array.isArray(deployed?.packs) ? deployed.packs : []) {
        const listed = (pack as { files?: unknown } | null)?.files;
        for (const file of Array.isArray(listed) ? listed : []) keep((file as { path?: unknown })?.path, (file as { sha256?: unknown })?.sha256);
    }
    const reply = readJson(join(records, replyRecord)) as Record<string, unknown> | null;
    const replyFiles = reply?.[folder.folder];
    for (const file of Array.isArray(replyFiles) ? replyFiles : []) keep((file as { path?: unknown })?.path, (file as { sha256?: unknown })?.sha256);
    return files;
}

/** Removes from a skills folder every entry the records do not name, and every named file whose bytes changed. */
function ownSkills(skills: string, kept: Map<string, string>, remove: (path: string) => void): void {
    const walk = (folder: string, relative: string) => {
        for (const name of readdirSync(folder)) {
            const path = join(folder, name);
            const rel = relative === '' ? name : `${relative}/${name}`;
            const what = kind(path);
            if (what === 'folder' && [...kept.keys()].some((each) => each.startsWith(`${rel}/`))) {
                walk(path, rel);
                continue;
            }
            if (what === 'file' && kept.get(rel) === digest(path)) continue;
            remove(path);
        }
    };
    walk(skills, '');
}

/** Removes every `.claude`, `.agents` and `.codex` below a folder, never walking into a link. */
function removeNested(folder: string, remove: (path: string) => void): void {
    for (const name of readdirSync(folder)) {
        const path = join(folder, name);
        if (agentFolders.has(name.toLowerCase())) remove(path);
        else if (kind(path) === 'folder') removeNested(path, remove);
    }
}

/** Removes a name and what is under it; a link is removed as a link and what it names is left. */
function removed(path: string): void {
    rmSync(path, { recursive: true, force: true });
}

/**
 * Puts the chat folder's agent settings right before a turn, against the records under `records`. Throws
 * `AgentFolderUnowned` when that cannot be done.
 */
export function ownAgentFolder(chat: string, records: string, remove: (path: string) => void = removed): void {
    try {
        for (const name of [...unwrittenMemoryFiles, '.codex']) if (kind(join(chat, name)) !== null) remove(join(chat, name));
        for (const name of kind(chat) === 'folder' ? readdirSync(chat) : []) {
            if (skillsFolders.some((each) => each.folder === name)) continue;
            const path = join(chat, name);
            if (agentFolders.has(name.toLowerCase())) remove(path);
            else if (kind(path) === 'folder') removeNested(path, remove);
        }
        for (const host of skillsFolders) {
            const top = join(chat, host.folder);
            const what = kind(top);
            if (what === null) continue;
            if (what !== 'folder') {
                remove(top);
                continue;
            }
            for (const name of readdirSync(top)) {
                const path = join(top, name);
                if (name === 'skills' && kind(path) === 'folder') {
                    ownSkills(path, recorded(records, host), remove);
                    removeNested(path, remove);
                } else remove(path);
            }
        }
    } catch (error) {
        throw new AgentFolderUnowned((error as NodeJS.ErrnoException | null)?.code);
    }
}

/**
 * Before `jason skills install` deploys into a host's skills folder: the record the desktop keeps of its last deployment
 * there is put back beside the skills, where the CLI reads what it wrote; with none kept, the folder starts empty, so a
 * file nobody recorded is never taken for one the CLI wrote.
 */
export function beforeSkillsDeploy(chat: string, records: string, host: (typeof skillsFolders)[number]): void {
    const skills = join(chat, host.folder, 'skills');
    const kept = join(records, host.record);
    if (kind(kept) !== 'file') {
        rmSync(skills, { recursive: true, force: true });
        return;
    }
    mkdirSync(skills, { recursive: true });
    copyFileSync(kept, join(skills, skillsRecordFile));
}

/** After `jason skills install`: its record moves out of the chat folder into the desktop's records. */
export function afterSkillsDeploy(chat: string, records: string, host: (typeof skillsFolders)[number]): void {
    const written = join(chat, host.folder, 'skills', skillsRecordFile);
    if (kind(written) !== 'file') return;
    mkdirSync(records, { recursive: true, mode: 0o700 });
    const target = join(records, host.record);
    const beside = `${target}.${process.pid}.tmp`;
    copyFileSync(written, beside);
    renameSync(beside, target);
    rmSync(written, { force: true });
}

/** Records, by skills folder, every file of the Reply skills (`reply-*`) the Connect card copied there, with its digest. */
export function recordReplySkills(chat: string, records: string): void {
    const listed: Record<string, { path: string; sha256: string }[]> = {};
    for (const host of skillsFolders) {
        const skills = join(chat, host.folder, 'skills');
        const files: { path: string; sha256: string }[] = [];
        const walk = (folder: string, relative: string) => {
            for (const name of readdirSync(folder)) {
                const path = join(folder, name);
                const rel = `${relative}/${name}`;
                const what = kind(path);
                if (what === 'folder') walk(path, rel);
                else if (what === 'file') files.push({ path: rel, sha256: digest(path) });
            }
        };
        for (const name of kind(skills) === 'folder' ? readdirSync(skills) : []) {
            if (name.startsWith('reply-') && kind(join(skills, name)) === 'folder') walk(join(skills, name), name);
        }
        listed[host.folder] = files;
    }
    mkdirSync(records, { recursive: true, mode: 0o700 });
    const target = join(records, replyRecord);
    writeFileSync(`${target}.${process.pid}.tmp`, `${JSON.stringify(listed)}\n`, { mode: 0o600 });
    renameSync(`${target}.${process.pid}.tmp`, target);
}
