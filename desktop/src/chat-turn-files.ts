// What one chat turn writes, and removes when it ends: the host's MCP configuration, which names the turn's permission
// endpoint and carries the turn's secret. It lies in `<data>/desktop/turns/`, outside the chat folder, so the permission
// model refuses the agent a read of it and one host rule covers every turn's; the secret is never on argv and never in
// a log. Every function here takes that folder.

import { randomBytes } from 'node:crypto';
import { mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { serverName } from './chat-permission-endpoint.ts';

const turnIdShape = /^[0-9a-f]{24}$/;

export function newTurnId(): string {
    return randomBytes(12).toString('hex');
}

export function newSecret(): string {
    return randomBytes(32).toString('base64url');
}

/**
 * How long the host waits on one call of the permission tool: 35 minutes, above the broker's 30-minute answer window, so
 * the broker always answers (a refusal, when nobody did) before the host gives up. Claude Code's MCP page
 * (https://code.claude.com/docs/en/mcp, "Time limits on MCP tool calls") makes a server entry's `timeout` of 1000 ms or
 * more a wall-clock limit per call that also floors the idle window, from version 2.1.203; without it an HTTP call that
 * sees no response and no progress for five minutes is aborted (from 2.1.187). The limit is the one server's, in the one
 * file the turn owns, rather than a variable in the host's environment.
 */
export const hostCallLimitMs = 2_100_000;

/** Pure: the configuration Claude Code reads with `--mcp-config`. */
export function turnConfig(url: string, secret: string): object {
    return { mcpServers: { [serverName]: { type: 'http', url, headers: { Authorization: `Bearer ${secret}` }, timeout: hostCallLimitMs } } };
}

export function turnConfigPath(turns: string, turnId: string): string {
    if (!turnIdShape.test(turnId)) throw new Error('That is not a turn id the desktop made.');
    return join(turns, `turn-${turnId}.mcp.json`);
}

/**
 * Writes the turn's configuration, readable by this user alone, making the folder when it is not there; a file already
 * there is never overwritten.
 */
export function writeTurnConfig(turns: string, turnId: string, url: string, secret: string): string {
    const file = turnConfigPath(turns, turnId);
    mkdirSync(turns, { recursive: true, mode: 0o700 });
    writeFileSync(file, `${JSON.stringify(turnConfig(url, secret))}\n`, { encoding: 'utf8', mode: 0o600, flag: 'wx' });
    return file;
}

export function removeTurnFiles(turns: string, turnId: string): void {
    rmSync(turnConfigPath(turns, turnId), { force: true });
}

const turnFileShape = /^turn-[0-9a-f]{24}\.mcp\.json$/;

/** The empty folder one naming run of a set-up chat runs in (`chat-naming.ts`), removed after with what it holds. */
export function namingFolderPath(turns: string, id: string): string {
    if (!turnIdShape.test(id)) throw new Error('That is not a turn id the desktop made.');
    return join(turns, `naming-${id}`);
}

const namingFolderShape = /^naming-[0-9a-f]{24}$/;

/**
 * Removes every turn file in `<data>/desktop/turns/`, and every naming run's folder with what is in it, at the desktop's
 * start: a desktop that ended mid-turn, or a removal a scanner held, leaves one behind, a turn file with its secret. Only
 * names of those two shapes there are touched, nothing else under `<data>/desktop`, and one that cannot be removed is
 * left.
 */
export function sweepTurnFiles(turns: string): void {
    let names: string[];
    try {
        names = readdirSync(turns);
    } catch {
        return;
    }
    for (const name of names) {
        const folder = namingFolderShape.test(name);
        if (!folder && !turnFileShape.test(name)) continue;
        try {
            rmSync(join(turns, name), { force: true, recursive: folder });
        } catch {
            // Left for the next start.
        }
    }
}
