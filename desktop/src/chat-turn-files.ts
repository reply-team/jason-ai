// What one chat turn writes, and removes when it ends: the host's MCP configuration, which names the turn's permission
// endpoint and carries the turn's secret. It lies under `<data>/desktop/`, outside the chat folder, so the permission
// model refuses the agent a read of it; the secret is never on argv and never in a log.

import { randomBytes } from 'node:crypto';
import { rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { serverName } from './chat-permission-endpoint.ts';

const turnIdShape = /^[0-9a-f]{24}$/;

export function newTurnId(): string {
    return randomBytes(12).toString('hex');
}

export function newSecret(): string {
    return randomBytes(32).toString('base64url');
}

/** Pure: the configuration Claude Code reads with `--mcp-config`. */
export function turnConfig(url: string, secret: string): object {
    return { mcpServers: { [serverName]: { type: 'http', url, headers: { Authorization: `Bearer ${secret}` } } } };
}

export function turnConfigPath(desktop: string, turnId: string): string {
    if (!turnIdShape.test(turnId)) throw new Error('That is not a turn id the desktop made.');
    return join(desktop, `turn-${turnId}.mcp.json`);
}

/** Writes the turn's configuration, readable by this user alone; a file already there is never overwritten. */
export function writeTurnConfig(desktop: string, turnId: string, url: string, secret: string): string {
    const file = turnConfigPath(desktop, turnId);
    writeFileSync(file, `${JSON.stringify(turnConfig(url, secret))}\n`, { encoding: 'utf8', mode: 0o600, flag: 'wx' });
    return file;
}

export function removeTurnFiles(desktop: string, turnId: string): void {
    rmSync(turnConfigPath(desktop, turnId), { force: true });
}
