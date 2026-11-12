// The chat folder's agent settings, which the desktop owns as it owns the memory files. Claude Code started in the chat
// folder with project settings reads `.claude/` there: its settings, which can add hooks, allow tools or switch the
// Full access hook off, and its skills. The desktop places only the skills: those `jason skills install` wrote, each
// named with its digest in the record it keeps beside them (`.jason-skills.json`), and the Reply skills the Connect card
// copies in (`reply-*`). Before each turn everything else under `.claude/` is removed, a skill file whose bytes are not
// the ones recorded among it, and so are the memory files the desktop does not write (`CLAUDE.local.md`,
// `AGENTS.override.md`), which the hosts would read beside its own. Nothing is followed: a link is removed as a link.
// A turn does not start on a folder this could not put right.

import { createHash } from 'node:crypto';
import { lstatSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';

/** The memory files a host reads in the chat folder beside the two the desktop writes, which it never writes. */
export const unwrittenMemoryFiles = ['CLAUDE.local.md', 'AGENTS.override.md'] as const;

/** The record `jason skills install` keeps in the root it deploys into. */
export const skillsRecordFile = '.jason-skills.json';

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

/** The files the record names, by their path relative to the skills folder, with the digest each was written with. */
function recorded(skills: string): Map<string, string> {
    const files = new Map<string, string>();
    let value: unknown;
    try {
        value = JSON.parse(readFileSync(join(skills, skillsRecordFile), 'utf8'));
    } catch {
        return files;
    }
    const packs = (value as { packs?: unknown } | null)?.packs;
    if (!Array.isArray(packs)) return files;
    for (const pack of packs) {
        const listed = (pack as { files?: unknown } | null)?.files;
        if (!Array.isArray(listed)) continue;
        for (const file of listed) {
            const { path, sha256 } = (file ?? {}) as { path?: unknown; sha256?: unknown };
            // The record's own spelling: relative, forward slashes, no colon, backslash or `..` segment.
            if (typeof path !== 'string' || typeof sha256 !== 'string' || path === '' || path.startsWith('/') || /[\\:\0]/.test(path) || path.split('/').includes('..')) continue;
            files.set(path, sha256.toLowerCase());
        }
    }
    return files;
}

/** Removes a name and what is under it, a link as a link. */
function removed(path: string): void {
    rmSync(path, { recursive: true, force: true });
}

function digest(file: string): string {
    return createHash('sha256').update(readFileSync(file)).digest('hex');
}

/** Removes from the skills folder every entry the desktop did not place, and every recorded file whose bytes changed. */
function ownSkills(skills: string, remove: (path: string) => void): void {
    const kept = recorded(skills);
    const walk = (folder: string, relative: string) => {
        for (const name of readdirSync(folder)) {
            const path = join(folder, name);
            const rel = relative === '' ? name : `${relative}/${name}`;
            const what = kind(path);
            if (relative === '' && name.startsWith('reply-') && what === 'folder') continue;
            if (relative === '' && name === skillsRecordFile && what === 'file') continue;
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

/**
 * Puts the chat folder's agent settings right before a turn: `.claude/` holds the skills the desktop placed and nothing
 * else, and the memory files it does not write are gone. Throws `AgentFolderUnowned` when that cannot be done.
 */
export function ownAgentFolder(chat: string, remove: (path: string) => void = removed): void {
    try {
        for (const name of unwrittenMemoryFiles) if (kind(join(chat, name)) !== null) remove(join(chat, name));
        const claude = join(chat, '.claude');
        const what = kind(claude);
        if (what === null) return;
        if (what !== 'folder') {
            remove(claude);
            return;
        }
        for (const name of readdirSync(claude)) {
            const path = join(claude, name);
            if (name === 'skills' && kind(path) === 'folder') ownSkills(path, remove);
            else remove(path);
        }
    } catch (error) {
        throw new AgentFolderUnowned((error as NodeJS.ErrnoException | null)?.code);
    }
}
