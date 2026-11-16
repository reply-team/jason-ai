/**
 * Pure: the agents' roles put on the AI the chat runs on. A fresh installation has no execution profile and names no
 * default, so no AI role can run; once the chat is on Claude Code or Codex and that host is connected, the desktop makes
 * one profile for it with `jason profile create` and names it `Roles:DefaultExecutionProfile` in the installation's
 * settings (`Setup.connectRoles`). In Live only, and only where the installation holds no profile at all and names no
 * default: a profile or a default a person made is never touched.
 */

import type { Conversation } from './chat-conversations.ts';
import type { RolesMark } from './settings.ts';

/** The hosts the roles are put on: the two that drive the chat. */
export type RoleHost = 'claude' | 'codex';

/** The profile made for each: its name and the host word the runtime takes. */
export const roleProfiles: Readonly<Record<RoleHost, { name: string; host: string }>> = {
    claude: { name: 'claude-code', host: 'claude_code' },
    codex: { name: 'codex', host: 'codex' },
};

/**
 * The host the roles go on: the one the chat is set to run on when that is Claude Code or Codex and it is connected;
 * when the chat is on a model-key provider, or on nothing yet, Claude Code if it is connected, else Codex. A chosen host
 * that is not connected is not traded for the other: nothing is done until the chat runs on a connected one. A key
 * provider never gets a profile, since a profile holds no credential.
 */
export function roleHostOf(chosen: string | null, connected: Readonly<Record<RoleHost, boolean>>): RoleHost | null {
    if (chosen === 'claude' || chosen === 'codex') return connected[chosen] ? chosen : null;
    return connected.claude ? 'claude' : connected.codex ? 'codex' : null;
}

/**
 * The program a profile names: the path the desktop found the host at, or the bare name where that path is a script only
 * a shell runs (npm's `.cmd` on Windows), which the runtime refuses as a program and finds by the bare name itself.
 */
export function profileProgram(host: RoleHost, found: string): string {
    return /\.(cmd|bat|ps1)[\s.]*$/i.test(found) ? host : found;
}

/** The host the chat is set to run on: the one its newest conversation's last turn ran on; null before any turn. */
export function lastChosenHost(conversations: readonly Pick<Conversation, 'updated_at' | 'chosen'>[]): string | null {
    const chosen = conversations.filter((each) => each.chosen !== undefined);
    const newest = chosen.reduce<(typeof chosen)[number] | null>((best, each) => (best === null || each.updated_at > best.updated_at ? each : best), null);
    return newest?.chosen?.host ?? null;
}

/** A profile as `jason profile list` and `jason profile get` print it: the fields read here. */
export interface ListedProfile {
    id: string | null;
    name: string;
    disabled: boolean;
    host: string | null;
}

function profileOf(value: unknown): ListedProfile | null {
    if (typeof value !== 'object' || value === null) return null;
    const { id, name, disabled, revision } = value as { id?: unknown; name?: unknown; disabled?: unknown; revision?: { host?: unknown } };
    if (typeof name !== 'string') return null;
    return { id: typeof id === 'string' ? id : null, name, disabled: disabled === true, host: typeof revision?.host === 'string' ? revision.host : null };
}

/**
 * One page of `jason profile list`, whether more follow, and the default in force the page says, by name, or null where
 * none is named; null where it is not one, a page that does not say the default included.
 */
export function readProfilePage(stdout: string): { profiles: ListedProfile[]; more: boolean; defaultInForce: string | null } | null {
    try {
        const raw = JSON.parse(stdout) as { items?: unknown; next_cursor?: unknown; default_execution_profile?: unknown };
        if (!Array.isArray(raw.items)) return null;
        const profiles = raw.items.map(profileOf);
        if (profiles.some((each) => each === null)) return null;
        const named = raw.default_execution_profile;
        if (named !== null && typeof named !== 'string') return null;
        return { profiles: profiles as ListedProfile[], more: typeof raw.next_cursor === 'string', defaultInForce: named };
    } catch {
        return null;
    }
}

/** The default in force `jason profile set-default` answers, by name; null where the answer names none or is not one. */
export function readDefaultAnswer(stdout: string): string | null {
    try {
        const named = (JSON.parse(stdout) as { execution_profile?: unknown } | null)?.execution_profile;
        return typeof named === 'string' ? named : null;
    } catch {
        return null;
    }
}

/** One profile as `jason profile get` prints it; null where it is not one. */
export function readProfile(stdout: string): ListedProfile | null {
    try {
        return profileOf(JSON.parse(stdout));
    } catch {
        return null;
    }
}

/** What one look at the installation leads to. */
export type RolesNext =
    | { kind: 'leave'; why: 'profiles_exist' | 'no_host' }
    | { kind: 'create'; host: RoleHost }
    | { kind: 'name'; name: string };

/**
 * Pure: with no default named, what the profiles say to do. None at all: make one for the host, where there is one. The
 * one profile there, enabled, with the id the desktop's mark recorded when its create succeeded: the profile was made and
 * the default never written, so it is written now, with no host needed and never a second profile. Anything else is a
 * person's, or cannot be shown to be the desktop's — a mark with no id included — and is left.
 */
export function rolesNext(profiles: readonly ListedProfile[], more: boolean, pending: RolesMark | null, host: RoleHost | null): RolesNext {
    if (profiles.length === 0 && !more) return host === null ? { kind: 'leave', why: 'no_host' } : { kind: 'create', host };
    const [only] = profiles;
    const ours = pending?.id !== undefined && only?.id === pending.id && only.name === pending.name;
    if (ours && !more && profiles.length === 1 && !only.disabled) return { kind: 'name', name: only.name };
    return { kind: 'leave', why: 'profiles_exist' };
}

/** The host word of a profile, by the desktop's id of the program that runs it. */
export const hostWords: Readonly<Record<string, string>> = {
    claude: 'claude_code',
    codex: 'codex',
    gemini: 'gemini_cli',
    agy: 'antigravity_cli',
    'cursor-agent': 'cursor_cli',
    copilot: 'copilot_cli',
};
