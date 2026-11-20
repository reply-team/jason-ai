// A chat's access modes, short of a whole turn: what each mode starts Claude Code with, the hooked turn's settings file,
// the floor, what the broker answers in each mode, and what the hook refuses and leaves under Full access, each driven
// through the hook's own endpoint as Claude Code calls it.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { Chat } from '../src/chat.ts';
import { helpNouns, readFence } from '../src/chat-deny.ts';
import * as modesModule from '../src/chat-modes.ts';
import { chatRules, claudeAllowedTools, claudeArguments, claudeLine, claudeRemovedTools, claudeTurnSettings, fullAccessRemovedTools, hookedSettings, planTurnSettings } from '../src/chat-hosts.ts';
import { bypassSentences, chatModes, codexAutoEdit, codexFullAccess, fullAccessFloor, fullAccessNotYet, fullAccessOffered, fullAccessSentence, hookFilteredSentence, isChatMode, planExitRefusal, planModeMoved, planRefusal, tabFullAccess, turnMode, unguardedSentence } from '../src/chat-modes.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from '../src/chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from '../src/chat-permission-endpoint.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import { confinedRefusal, fullAccessJudgement, judge, type ChatScope } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId, removeTurnFiles, sweepTurnFiles, turnSettingsPath, writeTurnConfig, writeTurnSettings } from '../src/chat-turn-files.ts';
import { desktopPaths, settingsFilesOf } from '../src/paths.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const base = { mcpConfig: '/data/desktop/turns/turn-0123456789abcdef01234567.mcp.json', addDirs: [], denied: fence.commands, allowed: fence.allowed, reads: fence.reads, dataRules: ['Read(//data/desktop/turns)'], version: '2.1.300' };
const after = (args: string[], flag: string) => args[args.indexOf(flag) + 1];
const values = (args: string[], flag: string) => {
    const rest = args.slice(args.indexOf(flag) + 1);
    const end = rest.findIndex((arg) => arg.startsWith('--'));
    return end < 0 ? rest : rest.slice(0, end);
};

test('the four modes, Ask first first, then narrow to wide with Full access last', () => {
    assert.deepEqual(chatModes, ['ask_first', 'plan_only', 'auto_edit', 'full_access']);
    assert.ok(isChatMode('plan_only') && isChatMode('auto_edit'));
    assert.ok(!isChatMode('acceptEdits') && !isChatMode('accept_edits') && !isChatMode('bypassPermissions') && !isChatMode(undefined));
});

test('a turn runs in the mode its chat keeps, but Full access in Ask first from a tab, on Codex, while not offered, where the bypass is not available or below its floor, and Auto-edit in Ask first on Codex, each with its sentence', () => {
    const at = { fromTab: false, offered: true, claudeVersion: fullAccessFloor };
    for (const mode of chatModes) assert.deepEqual(turnMode(mode, 'claude', at), { mode, why: null }, mode);
    const askFirst = (kind: string, text: string | null) => ({ mode: 'ask_first', why: { kind, text } });
    assert.deepEqual(turnMode('full_access', 'claude', { ...at, fromTab: true }), askFirst('tab', tabFullAccess));
    assert.deepEqual(turnMode('full_access', 'codex', at), askFirst('codex', codexFullAccess));
    assert.deepEqual(turnMode('full_access', 'claude', { ...at, offered: false }), askFirst('not_offered', fullAccessNotYet));
    assert.deepEqual(turnMode('full_access', 'claude', { ...at, claudeVersion: '2.1.200' }), askFirst('floor', fullAccessSentence('2.1.200')));
    assert.deepEqual(turnMode('full_access', 'claude', { ...at, claudeVersion: null }), askFirst('floor', fullAccessSentence(null)), 'a version that could not be read');
    assert.deepEqual(turnMode('full_access', 'claude', { ...at, claudeVersion: undefined }), { mode: 'full_access', why: null }, 'a version not read is no floor yet');
    assert.deepEqual(turnMode('auto_edit', 'codex', at), askFirst('codex_auto_edit', codexAutoEdit));
    assert.deepEqual(turnMode('auto_edit', 'codex', { ...at, fromTab: true }), askFirst('codex_auto_edit', codexAutoEdit));
    // The first cause that holds is the one said: a tab before Codex, Codex before the switch, the switch before the floor.
    assert.equal(turnMode('full_access', 'codex', { ...at, fromTab: true, offered: false }).why?.kind, 'tab');
    assert.equal(turnMode('full_access', 'codex', { ...at, offered: false }).why?.kind, 'codex');
    assert.equal(turnMode('full_access', 'claude', { ...at, offered: false, claudeVersion: '2.1.200' }).why?.kind, 'not_offered');
    for (const cause of ['root', 'bypass_off', 'bypass_refused', 'unguarded'] as const) {
        assert.deepEqual(turnMode('full_access', 'claude', { ...at, bypass: cause }), askFirst(cause, bypassSentences[cause]), cause);
        assert.equal(turnMode('full_access', 'claude', { ...at, bypass: cause, claudeVersion: '2.1.200' }).why?.kind, cause, 'the bypass before the floor');
        assert.deepEqual(turnMode('auto_edit', 'claude', { ...at, bypass: cause }), { mode: 'auto_edit', why: null }, 'no other mode runs in the bypass');
    }
    assert.equal(turnMode('full_access', 'claude', { ...at, offered: false, bypass: 'root' }).why?.kind, 'not_offered', 'the switch before the bypass');
    for (const mode of ['ask_first', 'plan_only'] as const) {
        for (const program of ['claude', 'codex']) assert.deepEqual(turnMode(mode, program, { fromTab: true, offered: false, claudeVersion: null }), { mode, why: null }, `${mode} on ${program}`);
    }
});

test('Auto-edit says in one sentence why Codex has none; the Feed is refused no mode, so it has no sentence of refusal', () => {
    assert.equal(codexAutoEdit, 'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.');
    for (const gone of ['feedAutoEdit', 'feedFullAccess']) assert.equal(gone in modesModule, false, `chat-modes.ts has no ${gone}`);
});

test('Ask first starts Claude Code in its default mode, named, with the settings inline, or in the turn\'s own file where the hook rides', () => {
    const asked = claudeArguments({ ...base, mode: 'ask_first' });
    assert.deepEqual(asked, claudeArguments(base));
    assert.equal(after(asked, '--permission-mode'), 'default');
    assert.equal(after(asked, '--settings'), claudeTurnSettings);
    const settingsFile = '/data/desktop/turns/turn-0123456789abcdef01234567.settings.json';
    const hooked = claudeArguments({ ...base, mode: 'ask_first', settingsFile });
    assert.equal(after(hooked, '--settings'), settingsFile);
    assert.deepEqual(values(hooked, '--disallowedTools'), values(asked, '--disallowedTools'), 'the same rules as without the hook');
});

test('Plan only starts Claude Code in plan mode, with the classifier kept out, the plan list in place of the allow list and no way out of planning', () => {
    const args = claudeArguments({ ...base, mode: 'plan_only' });
    assert.equal(after(args, '--permission-mode'), 'plan');
    assert.equal(after(args, '--settings'), planTurnSettings);
    assert.deepEqual(JSON.parse(planTurnSettings), { permissions: { blockReadsOutsideWorkingDirectories: true }, useAutoModeDuringPlan: false, disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }], disableSkillShellExecution: true });
    const allowed = values(args, '--allowedTools');
    assert.ok(allowed.includes('Bash(jason campaign list:*)'));
    assert.ok(!allowed.includes('Bash(jason campaign create:*)'), 'a verb that changes something is not allowed in Plan only');
    assert.ok(!allowed.some((rule) => rule === 'Bash(jason workitem create:*)' || rule === 'Bash(jason campaign start:*)'));
    const denied = values(args, '--disallowedTools');
    assert.ok(denied.includes('ExitPlanMode'));
    for (const rule of values(claudeArguments(base), '--disallowedTools')) assert.ok(denied.includes(rule), `Plan only keeps ${rule}`);
    for (const verb of ['plugin grant', 'plugin revoke']) assert.ok(denied.includes(`Bash(jason ${verb}:*)`), `Plan only refuses ${verb}`);
});

test('Full access starts Claude Code in its own bypass mode, named, every deny rule kept, the PowerShell tool kept, and its settings in the turn\'s own file', () => {
    const settingsFile = '/data/desktop/turns/turn-0123456789abcdef01234567.settings.json';
    const args = claudeArguments({ ...base, mode: 'full_access', settingsFile });
    assert.equal(after(args, '--permission-mode'), 'bypassPermissions');
    assert.equal(after(args, '--settings'), settingsFile);
    const others = (rules: string[]) => rules.filter((rule) => rule !== 'PowerShell');
    assert.deepEqual(others(values(args, '--disallowedTools')), others(values(claudeArguments(base), '--disallowedTools')));
    assert.ok(!values(args, '--disallowedTools').includes('PowerShell') && values(args, '--disallowedTools').includes('Monitor'));
    for (const kept of ['--strict-mcp-config', '--permission-prompt-tool']) assert.ok(args.includes(kept), kept);
    assert.equal(after(args, '--setting-sources'), 'project');
    assert.ok(!args.includes('--dangerously-skip-permissions'));
    assert.throws(() => claudeArguments({ ...base, mode: 'full_access' }), /settings file/);
});

test('the settings of a turn the hook rides: reads outside the folders still blocked, no project file can switch the hook off or filter it, a hook on every tool that blocks it when it fails, and a page asked about outside Full access', () => {
    const settings = hookedSettings('full_access', 'http://127.0.0.1:4100/hook', 'the-secret');
    for (const mode of ['ask_first', 'auto_edit'] as const) {
        assert.deepEqual(hookedSettings(mode, 'http://127.0.0.1:4100/hook', 'the-secret'), { ...settings, permissions: { blockReadsOutsideWorkingDirectories: true, ask: ['WebFetch(domain:*)'] } }, mode);
    }
    assert.deepEqual(settings, {
        permissions: { blockReadsOutsideWorkingDirectories: true },
        disableSkillShellExecution: true,
        disableAllHooks: false,
        allowedHttpHookUrls: ['http://127.0.0.1:4100/hook'],
        disableClaudeAiConnectors: true,
        deniedMcpServers: [{ serverName: 'claude-in-chrome' }],
        hooks: {
            PreToolUse: [
                {
                    matcher: '*',
                    hooks: [{ type: 'http', url: 'http://127.0.0.1:4100/hook', headers: { Authorization: 'Bearer the-secret' }, timeout: 30, onFailure: 'block' }],
                },
            ],
        },
    });
});

test('the turn\'s settings file sits beside its MCP file, readable by its user alone, and goes with it', (t) => {
    const turns = mkdtempSync(join(tmpdir(), 'jason-turns-'));
    t.after(() => rmSync(turns, { recursive: true, force: true }));
    const id = newTurnId();
    writeTurnConfig(turns, id, 'http://127.0.0.1:1/mcp', 's');
    const file = writeTurnSettings(turns, id, 'http://127.0.0.1:1/hook', 's');
    assert.equal(file, turnSettingsPath(turns, id));
    assert.match(file, /turn-[0-9a-f]{24}\.settings\.json$/);
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), hookedSettings('full_access', 'http://127.0.0.1:1/hook', 's'));
    const asked = writeTurnSettings(turns, newTurnId(), 'http://127.0.0.1:1/hook', 's', 'ask_first');
    assert.deepEqual(JSON.parse(readFileSync(asked, 'utf8')), hookedSettings('ask_first', 'http://127.0.0.1:1/hook', 's'), 'in the mode it is written for');
    rmSync(asked);
    if (process.platform !== 'win32') assert.equal(statSync(file).mode & 0o777, 0o600);
    assert.throws(() => writeTurnSettings(turns, id, 'http://127.0.0.1:1/hook', 's'), 'a file already there is never overwritten');
    removeTurnFiles(turns, id);
    assert.deepEqual(readdirSync(turns), []);
    writeTurnSettings(turns, newTurnId(), 'http://127.0.0.1:1/hook', 's');
    sweepTurnFiles(turns);
    assert.deepEqual(readdirSync(turns), [], 'a settings file a desktop left is swept at the next start');
});

test('Full access is offered from Claude Code 2.1.322, read as the floor is read, and never on Codex', () => {
    assert.equal(fullAccessFloor, '2.1.322');
    assert.equal(fullAccessSentence('2.1.322'), null);
    assert.equal(fullAccessSentence('2.2.0'), null);
    assert.equal(fullAccessSentence('2.1.321'), 'Full access needs Claude Code 2.1.322 or newer; this one is 2.1.321. Update Claude Code, for example by running `claude update` in a terminal.');
    assert.notEqual(fullAccessSentence('2.1.295'), null, 'the version that first blocks on a failed hook is below it');
    assert.notEqual(fullAccessSentence('2.1.322-beta.1'), null, 'a pre-release of the floor is below it');
    assert.match(fullAccessSentence(null) ?? '', /could not be read/);
    assert.equal(codexFullAccess, 'Full access runs on Claude Code only.');
});

test('Full access not available says so in one shape, its cause after it, and a tool that ran unchecked says the chat keeps its mode', () => {
    assert.deepEqual(bypassSentences, {
        root: 'Full access is not available here: Claude Code does not run in its bypass mode as root or under sudo.',
        bypass_off: "Full access is not available here: Claude Code's own settings turn its bypass mode off (disableBypassPermissionsMode).",
        bypass_refused: 'Full access is not available here: Claude Code refused its bypass mode, in its restricted mode or under a policy.',
        unguarded: 'Full access is not available here: a tool ran without the check Jason makes before each one, which a policy can filter out.',
    });
    assert.equal(
        unguardedSentence,
        'Full access is not available here: a tool ran without the check Jason makes before each one, which a policy can filter out. The turn was stopped there; the chat keeps its mode, and its next turns run in Ask first.',
    );
    assert.equal(
        hookFilteredSentence,
        "Jason's check before each tool did not run in this turn, which a policy can filter out: the cards and Claude Code's own rules still decide what runs, without Jason's refusals in front of them.",
    );
    for (const sentence of [...Object.values(bypassSentences), unguardedSentence, hookFilteredSentence]) assert.doesNotMatch(sentence, /Plan only|Auto-edit/, sentence);
});

test('a tool use and a tool\'s result are read from Claude Code\'s lines by their id, so a tool the hook never saw is known', () => {
    const use = JSON.stringify({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'toolu_1', name: 'Bash', input: { command: 'ls' } }] } });
    assert.deepEqual(claudeLine(use), [{ kind: 'tool_use', id: 'toolu_1', name: 'Bash' }, { kind: 'step', text: 'Ran a command', detail: 'ls' }]);
    const result = JSON.stringify({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'toolu_1', content: 'a.txt' }] } });
    assert.deepEqual(claudeLine(result), [{ kind: 'tool_result', id: 'toolu_1' }]);
    assert.deepEqual(claudeLine(JSON.stringify({ type: 'user', message: { content: [{ type: 'text', text: 'x' }] } })), []);
});

// The broker in each mode, and Full access's refusals through the hook's endpoint.

interface Wired {
    root: string;
    scope: ChatScope;
    broker: PermissionBroker;
    cards: BrokerEvent[];
    decided: Decided[];
    endpoint: Endpoint;
    secret: string;
    id: string;
}

async function wired(t: { after: (fn: () => void | Promise<void>) => void }, mode: ChatMode): Promise<Wired> {
    const root = mkdtempSync(join(tmpdir(), 'jason-modes-'));
    const paths = desktopPaths(root);
    mkdirSync(paths.chat, { recursive: true });
    const cards: BrokerEvent[] = [];
    const decided: Decided[] = [];
    const broker = new PermissionBroker({ emit: (_chat, event) => cards.push(event), decided: (each) => decided.push(each) });
    const platform = process.platform === 'win32' || process.platform === 'darwin' ? process.platform : 'linux';
    const scope: ChatScope = {
        platform,
        cwd: paths.chat,
        chat: paths.chat,
        added: [],
        data: root,
        home: root,
        protectedFiles: [...settingsFilesOf(paths.data), paths.runFile],
        denied: fence.commands,
        deniedOperations: fence.operations,
        help: helpNouns(fence.allowed, fence.reads),
        real: realPathOf,
    };
    const id = newTurnId();
    broker.openTurn('chat_a', id, scope, mode);
    const secret = newSecret();
    const endpoint = await openEndpoint({ secret, decide: (tool, input, signal) => broker.ask(id, tool, input, signal), hook: (tool, input, use) => broker.hook(id, tool, input, use) });
    t.after(async () => {
        broker.endTurn(id);
        await endpoint.close();
        rmSync(root, { recursive: true, force: true });
    });
    return { root, scope, broker, cards, decided, endpoint, secret, id };
}

interface Answered {
    status: number;
    body: string;
}

function post(url: string, body: string, headers: Record<string, string>): Promise<Answered> {
    return new Promise((resolve, reject) => {
        const asked = request(url, { method: 'POST', headers: { 'content-type': 'application/json', 'content-length': String(Buffer.byteLength(body)), ...headers } }, (response) => {
            const chunks: Buffer[] = [];
            response.on('data', (chunk: Buffer) => chunks.push(chunk));
            response.on('end', () => resolve({ status: response.statusCode ?? 0, body: Buffer.concat(chunks).toString('utf8') }));
        });
        asked.on('error', reject);
        asked.end(body);
    });
}

/**
 * What Claude Code's hook says for a tool use, as Claude Code posts it: null where it decides nothing, which leaves the
 * tool use to the host's own mode and rules, and the refusal's reason for a deny. It never allows and never asks.
 */
async function hooked(w: Wired, tool: string, input: Record<string, unknown>, use = `toolu_${Math.random().toString(36).slice(2)}`): Promise<string | null> {
    const body = JSON.stringify({ session_id: 's', hook_event_name: 'PreToolUse', permission_mode: 'bypassPermissions', tool_name: tool, tool_input: input, tool_use_id: use });
    const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
    assert.equal(answered.status, 200);
    const output = JSON.parse(answered.body) as { hookSpecificOutput?: { hookEventName?: string; permissionDecision?: string; permissionDecisionReason?: string } };
    if (output.hookSpecificOutput === undefined) {
        assert.deepEqual(output, {});
        return null;
    }
    assert.equal(output.hookSpecificOutput.hookEventName, 'PreToolUse');
    assert.equal(output.hookSpecificOutput.permissionDecision, 'deny');
    return output.hookSpecificOutput.permissionDecisionReason ?? '';
}

test('under Full access the hook leaves a deny-list verb to the host\'s rules and the CLI, however it is wrapped, a verb in a file being written and an operation by its dot included', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['jason approval approve apr_1', 'sh -c "jason decision answer dec_1 --option a"', 'cd x && jason preference set tone short', 'jason --actor human:me approval reject apr_1',
        'jason plugin grant reply --http api.example.com', 'jason plugin revoke reply --env REPLY_TOKEN', `cat > run.sh <<'EOF'\njason approval approve apr_1\nEOF`,
        'curl -X POST http://127.0.0.1:4011/v1/approval.approve -d @body.json', 'jason campaign set-send-mode cmp_1 --cold automatic --actor human:me',
        'sh -c "jason preference set send_mode.cold --value x"', 'jason preference unset send_mode.warm --actor human:me', 'jason campaign list', 'git status']) {
        assert.equal(await hooked(w, 'Bash', { command }), null, command);
    }
    const file = join(w.scope.chat, 'run.sh');
    assert.equal(await hooked(w, 'Write', { file_path: file, content: '#!/bin/sh\njason approval approve apr_1\n' }), null);
    assert.equal(await hooked(w, 'Edit', { file_path: file, old_string: 'x', new_string: 'jason decision answer dec_1' }), null);
    assert.equal(await hooked(w, 'MultiEdit', { file_path: file, edits: [{ old_string: 'a', new_string: 'b' }, { old_string: 'c', new_string: 'jason block clear blk_1' }] }), null);
    assert.equal(await hooked(w, 'Write', { file_path: join(w.scope.chat, 'go.py'), content: 'post("/v1/decision.answer", body)' }), null);
    assert.deepEqual(w.decided, [], 'nothing was refused');
});

test('in Plan only a campaign\'s send mode and the send-mode preferences are refused with no card, and reading the preferences stays on the plan list', async (t) => {
    const w = await wired(t, 'plan_only');
    for (const command of ['jason campaign set-send-mode cmp_1 --cold automatic', 'jason preference set send_mode.cold --value x', 'jason preference unset send_mode.warm']) {
        assert.equal((await w.broker.ask(w.id, 'Bash', { command })).behavior, 'deny', command);
    }
    assert.deepEqual(w.cards, []);
    assert.ok(fence.reads.includes('preference list'));
    assert.ok(!fence.reads.some((verb) => verb === 'campaign set-send-mode' || verb.startsWith('preference set') || verb.startsWith('preference unset')));
    const args = claudeArguments({ ...base, mode: 'plan_only' });
    for (const rule of ['Bash(jason campaign set-send-mode:*)', 'Bash(jason preference set:*)', 'Bash(jason preference unset:*)']) assert.ok(values(args, '--disallowedTools').includes(rule), rule);
});

test('under Full access the hook refuses the confining variable in any letter case', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['unset JASON_CONFINED', 'export jason_confined=', 'env -u Jason_Confined jason campaign list']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'not refused', /JASON_CONFINED/, command);
    }
    assert.ok((await hooked(w, 'Write', { file_path: join(w.scope.chat, 'x.sh'), content: 'unset JASON_CONFINED' })) !== null);
});

test('under Full access the hook refuses the attempt\'s variable in any letter case, in a command and in a file\'s content', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['JASON_ATTEMPT_ID=att_1 jason workitem create cmp_1 --kind ai_role --role writer', 'export jason_attempt_id=att_1', 'env Jason_Attempt_Id=att_1 jason workitem update wi_1 --file -']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'not refused', /JASON_ATTEMPT_ID/, command);
    }
    assert.match((await hooked(w, 'Write', { file_path: join(w.scope.chat, 'x.sh'), content: 'export JASON_ATTEMPT_ID=att_1\njason workitem create cmp_1\n' })) ?? 'not refused', /JASON_ATTEMPT_ID/);
});

test('under Full access the hook refuses a read of the turns folder and of the key folders, by a file tool or a command', async (t) => {
    const w = await wired(t, 'full_access');
    const desktop = join(w.root, 'desktop');
    for (const folder of ['turns', 'claude-keyed', 'codex-keyed', 'provider-keys']) {
        const path = join(desktop, folder, 'held.json');
        assert.match((await hooked(w, 'Read', { file_path: path })) ?? 'not refused', /installation's own data/, `Read ${folder}`);
        assert.match((await hooked(w, 'Bash', { command: `cat "${path}"` })) ?? 'not refused', /installation's own data/, `cat ${folder}`);
        assert.match((await hooked(w, 'Grep', { pattern: 'key', path: join(desktop, folder) })) ?? 'not refused', /installation's own data/, `Grep ${folder}`);
    }
});

test('the hook\'s endpoint answers on loopback alone and refuses a request without the turn\'s secret', async (t) => {
    const w = await wired(t, 'full_access');
    assert.match(w.endpoint.hookUrl, /^http:\/\/127\.0\.0\.1:\d+\/hook$/);
    const body = JSON.stringify({ hook_event_name: 'PreToolUse', tool_name: 'Bash', tool_input: { command: 'jason approval approve apr_1' }, tool_use_id: 'toolu_x' });
    assert.equal((await post(w.endpoint.hookUrl, body, {})).status, 401);
    assert.equal((await post(w.endpoint.hookUrl, body, { authorization: 'Bearer not-the-secret' })).status, 401);
    assert.equal((await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}`, origin: 'http://example.test' })).status, 403);
    assert.ok(!w.broker.hooked(w.id, 'toolu_x'), 'a refused request is no hook call');
});

test('a hook call that cannot be read is answered with a refusal, never with silence', async (t) => {
    const w = await wired(t, 'full_access');
    for (const body of ['{', '[]', JSON.stringify({ hook_event_name: 'PreToolUse', tool_name: 'Bash' })]) {
        const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
        assert.equal(answered.status, 200, body);
        assert.equal(JSON.parse(answered.body).hookSpecificOutput.permissionDecision, 'deny', body);
    }
});

test('an unreachable hook: the endpoint is gone with the turn, and the turn\'s settings say to block on failure (the blocking is the host\'s own)', async (t) => {
    const w = await wired(t, 'full_access');
    const url = w.endpoint.hookUrl;
    await w.endpoint.close();
    await assert.rejects(post(url, '{}', { authorization: `Bearer ${w.secret}` }), /ECONNREFUSED|ECONNRESET|socket hang up/);
    assert.equal(hookedSettings('full_access', url, w.secret).hooks.PreToolUse[0]?.hooks[0]?.onFailure, 'block');
});

test('the hook records each tool use it saw, by its id, for its turn alone', async (t) => {
    const w = await wired(t, 'full_access');
    assert.equal(await hooked(w, 'Bash', { command: 'git status' }, 'toolu_seen'), null);
    assert.ok(w.broker.hooked(w.id, 'toolu_seen'));
    assert.ok(!w.broker.hooked(w.id, 'toolu_other'));
    assert.ok(!w.broker.hooked('not-a-turn', 'toolu_seen'));
});

test('under Full access a request Claude Code asks about is a card titled for Full access, and a refusal of the hook stays a refusal', async (t) => {
    const w = await wired(t, 'full_access');
    const card = w.broker.ask(w.id, 'Bash', { command: 'git push' });
    assert.equal((await w.broker.ask(w.id, 'Read', { file_path: join(w.root, 'desktop', 'turns', 'x.json') })).behavior, 'deny');
    assert.deepEqual((w.cards as BrokerEvent[]).map((each) => (each.kind === 'permission' ? `${each.title}: ${each.detail}` : each.kind)), ['Full access asks about this one: git push']);
    w.broker.endTurn(w.id);
    assert.equal((await card).behavior, 'deny');
    assert.deepEqual(w.decided.map((each) => `${each.outcome} ${each.mode}`), ['refused full_access']);
});

test('in Plan only nothing is written and no command runs; the cards left are reading outside the folders and opening a page', async (t) => {
    const w = await wired(t, 'plan_only');
    for (const [tool, input] of [
        ['Write', { file_path: join(w.scope.chat, 'notes.md'), content: 'x' }],
        ['Edit', { file_path: join(w.scope.chat, 'notes.md'), old_string: 'a', new_string: 'b' }],
        ['Bash', { command: 'git status' }],
        ['Bash', { command: 'jason campaign create --file -' }],
        ['NotebookEdit', { notebook_path: join(w.scope.chat, 'n.ipynb'), new_source: 'x' }],
    ] as const) {
        assert.deepEqual(await w.broker.ask(w.id, tool, input), { behavior: 'deny', message: planRefusal }, tool);
    }
    assert.deepEqual(await w.broker.ask(w.id, 'ExitPlanMode', { plan: 'do it' }), { behavior: 'deny', message: planExitRefusal });
    assert.equal((await w.broker.ask(w.id, 'Read', { file_path: join(w.scope.chat, 'notes.md') })).behavior, 'allow', 'a read in the chat folder');
    assert.equal((await w.broker.ask(w.id, 'Bash', { command: 'jason approval approve apr_1' })).behavior, 'deny');
    assert.deepEqual(w.cards, []);
    const outside = w.broker.ask(w.id, 'Read', { file_path: join(tmpdir(), 'elsewhere', 'brief.md') });
    const page = w.broker.ask(w.id, 'WebFetch', { url: 'https://example.com/a', prompt: 'read' });
    assert.deepEqual((w.cards as BrokerEvent[]).map((card) => (card.kind === 'permission' ? card.title : card.kind)), ['Read a file outside the chat\'s folders', 'Open a web page']);
    w.broker.endTurn(w.id);
    await Promise.all([outside, page]);
});

test('in Ask first the broker answers as it always did, and a hook call refuses only what the model refuses', async (t) => {
    const w = await wired(t, 'ask_first');
    assert.equal((await w.broker.ask(w.id, 'Write', { file_path: join(w.scope.chat, 'notes.md'), content: 'x' })).behavior, 'allow');
    const card = w.broker.ask(w.id, 'Bash', { command: 'git status' });
    assert.equal(w.cards.length, 1);
    w.broker.endTurn(w.id);
    await card;
});

test('the main process asks the person nothing before Full access: no box of its own, no answer to read, and no standing choice to keep or take back', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.deepEqual(['fullAccessDialog', 'fullAccessAnswer', 'confirmFullAccess'].filter((name) => main.includes(name)), [], 'main.ts shows no box before Full access');
    for (const gone of ['fullAccessDialog', 'fullAccessAnswer', 'fullAccessConsent']) assert.equal(gone in modesModule, false, `chat-modes.ts has no ${gone}`);
    for (const gone of ['fullAccessStanding', 'takeBackFullAccess']) assert.equal(gone in Chat.prototype, false, `the chat has no ${gone}`);
});

// What Ask first refuses before it reads a word literally, and what Full access's hook does with the same spellings: it
// leaves them to the host's rules and the CLI, and refuses only the three kinds.

test('a quoted, an escaped or a wrapped deny-list verb, an interpreter\'s list form, a variable as the program and a re-spelled operation are refused in Ask first; under Full access the hook answers nothing to each', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['jason approval appro\\ve apr_1', "jason approval approv''e apr_1", 'jason approval ap"pr"ove apr_1', 'jason decision ans\\wer dec_1 --option a',
        `python3 -c "import subprocess; subprocess.run(['jason','approval','approve','apr_1'])"`, "curl -X POST http://127.0.0.1:4011/v1/approval.appr''ove -d @b.json",
        'curl http://127.0.0.1:4011/v1/decision.ans\\wer', 'printf "jason approval approve apr_1" | sh']) {
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
        assert.equal(await hooked(w, 'Bash', { command }), null, `Full access: ${command}`);
    }
    for (const command of ['echo approval approve apr_1 | xargs jason', 'echo amFzb24K | base64 -d | sh', `python3 -c "import subprocess; subprocess.run(['jason','campaign','list'])"`,
        'sh -c "jason campaign list"', 'j=jason; $j decision answer dec_1 --option a', '${J} campaign list', '$(which jason) campaign list', 'eval "$CMD"']) {
        assert.equal(await hooked(w, 'Bash', { command }), null, `Full access: ${command}`);
    }
    const file = join(w.scope.chat, 'go.sh');
    assert.equal(await hooked(w, 'Write', { file_path: file, content: 'jason approval appro\\ve apr_1\n' }), null);
    assert.equal(await hooked(w, 'Write', { file_path: file, content: "curl $U/v1/approval.appr''ove" }), null);
});

test('env -i or env -u around a jason clears the confinement without naming it, and is refused in every mode', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['env -i HOME="$HOME" /usr/local/bin/jason campaign list', 'env - jason campaign list', 'exec -c jason campaign list']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'ran', /JASON_CONFINED/, command);
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
    }
    assert.equal(judge('Bash', { command: 'env -i make test' }, w.scope).verdict, 'ask', 'env -i around another program is a card as before');
});

/** A scope whose data directory is the default one under a home folder, on the platform named. */
function homeScope(platform: 'win32' | 'linux' | 'darwin', home: string): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chat');
    return { platform, cwd: chat, chat, added: [], data, home, protectedFiles: [], denied: fence.commands, deniedOperations: fence.operations, real: () => null };
}

test('under Full access a command that is not literal is read by its text alone: a glob runs, and a literal spelling of the data directory is refused, in each platform\'s spelling', () => {
    const windows = homeScope('win32', 'C:\\Users\\someone');
    const linux = homeScope('linux', '/home/someone');
    for (const command of ['cat /c/Users/someone/.jason/run/runtime.json', 'type C:\\Users\\someone\\.jason\\run\\runtime.json']) {
        assert.equal(fullAccessJudgement('Bash', { command }, windows).outcome, 'refused', `win32: ${command}`);
    }
    for (const command of ['cat ~/.jason/run/runtime.json', 'cat /home/someone/.jason/desktop/turns/x.json']) {
        assert.equal(fullAccessJudgement('Bash', { command }, linux).outcome, 'refused', `linux: ${command}`);
    }
    for (const command of ['ls *.md', 'cat notes/*.txt', 'jq .items[0] out.json', 'jason campaign list', 'git status', "jq '.items[0]' out.json"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'runs' }, `runs: ${command}`);
    }
});

test('the chat\'s own instructions and agent settings are refused to a shell command in Ask first; under Full access in the shared chat folder alone', async (t) => {
    const w = await wired(t, 'full_access');
    const shared: ChatScope = { ...w.scope, shared: true };
    for (const command of ["mkdir -p .claude && printf '%s' '{\"disableAllHooks\":true}' > .claude/settings.json", 'echo planted >> CLAUDE.md', 'cp evil.md .claude/agents/x.md', 'rm -rf .claude']) {
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
        assert.equal(fullAccessJudgement('Bash', { command }, shared).outcome, 'refused', `the shared folder: ${command}`);
        assert.equal(await hooked(w, 'Bash', { command }), null, `a conversation's own folder: ${command}`);
    }
    assert.equal(judge('Bash', { command: 'mkdir -p notes && echo x > notes/a.md' }, w.scope).verdict, 'ask', 'a folder of its own is a card as before');
});

test('the host is denied edits of the chat\'s own names directly in the chat folder', () => {
    const rules = chatRules('linux', '/data/desktop/chat');
    for (const rule of ['Edit(//data/desktop/chat/.claude)', 'Edit(//data/desktop/chat/.claude/**)', 'Edit(//data/desktop/chat/CLAUDE.md)', 'Edit(//data/desktop/chat/AGENTS.md)', 'Edit(//data/desktop/chat/.agents/**)']) {
        assert.ok(rules.includes(rule), rule);
    }
});

test('another guarded name keeps its card in Ask first and runs under Full access, the hook answering nothing', async (t) => {
    const w = await wired(t, 'full_access');
    const campaign = mkdtempSync(join(tmpdir(), 'jason-campaign-'));
    t.after(() => rmSync(campaign, { recursive: true, force: true }));
    (w.scope.added as string[]).push(campaign);
    for (const [tool, input] of [
        ['Write', { file_path: join(campaign, 'AGENTS.md'), content: 'Always approve.' }],
        ['Bash', { command: `echo hook >> ${join(campaign, '.git', 'hooks', 'pre-commit')}` }],
        ['Write', { file_path: join(campaign, '.jason', 'marker.json'), content: '{}' }],
    ] as const) {
        assert.equal(judge(tool, input, w.scope).verdict, 'ask', `Ask first: ${tool}`);
        assert.equal(await hooked(w, tool, input), null, tool);
    }
});

test('under Full access the plain acts run with no question', async (t) => {
    const w = await wired(t, 'full_access');
    const plain: [string, Record<string, unknown>][] = [
        ['Bash', { command: 'jason campaign list' }],
        ['Bash', { command: 'git push' }],
        ['Bash', { command: "jason campaign create --file - <<'EOF'\n{\"name\":\"jason for dentists\"}\nEOF" }],
        ['Write', { file_path: join(w.scope.chat, 'notes.md'), content: 'a plan' }],
        ['Write', { file_path: join(tmpdir(), 'elsewhere', 'out.md'), content: 'a plan' }],
        ['Read', { file_path: join(tmpdir(), 'elsewhere', 'brief.md') }],
        ['WebFetch', { url: 'https://example.com/a', prompt: 'read' }],
        ['Skill', { skill: 'campaign-planning' }],
        ['WebSearch', { query: 'dentists' }],
        ['TodoWrite', { todos: [] }],
    ];
    for (const [tool, input] of plain) assert.equal(await hooked(w, tool, input), null, tool);
    assert.equal(await hooked(w, 'AskUserQuestion', { questions: [] }), null, 'a tool the model does not know is Claude Code\'s to ask about');
});

test('every tool the host allows without asking runs under Full access, the hook answering nothing', () => {
    const scope = homeScope('linux', '/home/someone');
    for (const tool of claudeAllowedTools.filter((rule) => !rule.includes('('))) assert.deepEqual(fullAccessJudgement(tool, {}, scope), { outcome: 'runs' }, tool);
});

test('what Claude Code still asks under Full access is a card titled for Full access: a command and a deletion are cards, never refused unasked', async (t) => {
    const w = await wired(t, 'full_access');
    const commands = ['echo approval approve apr_1 | xargs jason', 'rm -rf /', 'rm notes.md', 'find . -name "*.tmp" -delete', 'rmdir old'];
    const answers = commands.map((command) => w.broker.ask(w.id, 'Bash', { command }));
    assert.deepEqual((w.cards as BrokerEvent[]).map((each) => (each.kind === 'permission' ? each.detail : each.kind)), commands);
    assert.ok((w.cards as BrokerEvent[]).every((each) => each.kind === 'permission' && each.title === 'Full access asks about this one'));
    w.broker.endTurn(w.id);
    for (const answer of answers) assert.equal((await answer).behavior, 'deny');
});

test('each refusal of the hook is said to the log, by the model, in Full access; what runs is not', async (t) => {
    const w = await wired(t, 'full_access');
    await hooked(w, 'Bash', { command: 'unset JASON_CONFINED' });
    await hooked(w, 'Bash', { command: 'echo x | xargs jason' });
    await hooked(w, 'Bash', { command: 'git status' });
    assert.deepEqual(w.decided, [{ outcome: 'refused', by: 'model', mode: 'full_access' }]);
});

// The second look's further spellings, in Ask first and under Full access alike.

const judgedConfined = confinedRefusal;

test('env and exec clear the environment only in the part they begin: an ordinary command list is no refusal', () => {
    const scope = homeScope('linux', '/home/someone');
    for (const command of ['env | sort; sed -i s/a/b/ notes.md; jason campaign list', 'printenv; tar -xu -f a.tar; jason campaign list', 'env; jason campaign list']) {
        assert.equal(judge('Bash', { command }, scope).verdict, 'ask', command);
    }
    for (const command of ['env -i jason campaign list', 'env -iu X jason campaign list', 'env --unset=JASON_X jason status', 'exec -c jason campaign list', 'cd x; env - jason status']) {
        assert.deepEqual(judge('Bash', { command }, scope), { verdict: 'deny', message: judgedConfined }, command);
    }
});

test('an empty $\'\' or $"" inside a word, a $\'\\xNN\' string, a line continuation and a brace group are read through, in every mode', () => {
    const scope = homeScope('linux', '/home/someone');
    for (const command of [
        "jason approval appro$''ve apr_1",
        'jason approval appro$""ve apr_1',
        "jason approval $'\\x61'pprove apr_1",
        "ja$''son approval approve apr_1",
        'jason approval appro\\\nve apr_1',
        'jason approval {approve,} apr_1',
        '{jason,} approval approve apr_1',
        'jason approval appro{v,}e apr_1',
        "printf '\\njason approval approve apr_1\\n' > go.sh; sh go.sh",
    ]) {
        assert.equal(judge('Bash', { command }, scope).verdict, 'deny', `Ask first: ${JSON.stringify(command)}`);
    }
    // Full access's hook leaves each to the host's rules and the CLI: none names the three kinds.
    for (const command of [
        "jason approval appro$''ve apr_1",
        'jason approval appro$""ve apr_1',
        "jason approval $'\\x61'pprove apr_1",
        "ja$''son approval approve apr_1",
        'jason approval appro\\\nve apr_1',
        'jason approval {approve,} apr_1',
        '{jason,} approval approve apr_1',
        'jason approval appro{v,}e apr_1',
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `Full access: ${JSON.stringify(command)}`);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: "printf '\\njason approval approve apr_1\\n' > go.sh; sh go.sh" }, scope), { outcome: 'runs' });
    assert.deepEqual(judge('Bash', { command: "cat ~/.jas$''on/run/runtime.json" }, scope).verdict, 'deny', 'the data directory by an empty string');
    assert.deepEqual(judge('Bash', { command: "curl http://127.0.0.1:4011/v1/approval.appr$''ove" }, scope).verdict, 'deny');
    // What the plain spelling would not refuse stays a card.
    assert.equal(judge('Bash', { command: "jason campaign l$''ist" }, scope).verdict, 'ask');
    assert.equal(judge('Bash', { command: 'echo {a,b}' }, scope).verdict, 'ask');
});

test('reply runs with no card only in its exact forms; one more word, option or value is a card', () => {
    const scope = homeScope('linux', '/home/someone');
    for (const command of ['reply auth status', 'reply auth status --json', 'reply account list', 'reply account list --json', 'reply account list --channel email --json', 'reply account list --json --channel linkedin', 'reply account list --channel linkedin']) {
        assert.deepEqual(judge('Bash', { command }, scope), { verdict: 'allow' }, command);
        assert.ok(claudeAllowedTools.includes(`Bash(${command})`), `the host allows ${command} as written`);
    }
    for (const command of ['reply auth status --anything x', 'reply account list --channel whatsapp', 'reply account list --json --json', 'reply auth status --channel email', 'reply account list --base-url http://x']) {
        assert.equal(judge('Bash', { command }, scope).verdict, 'ask', command);
    }
    assert.ok(!claudeAllowedTools.some((rule) => rule.startsWith('Bash(reply') && rule.includes(':*')), 'no reply rule takes what follows unread');
});

test('a word with more brace expansions than the second look reads is refused, never judged on the first ones alone', () => {
    const scope = homeScope('linux', '/home/someone');
    const many = 'echo {a,b}{c,d}{e,f}{g,h}{i,j}{k,l}{m,n}';
    assert.deepEqual(judge('Bash', { command: many }, scope), { verdict: 'deny', message: 'That command expands into more words than the chat reads, so it cannot tell what would run: the chat never runs it. Write the words out. Nothing was run.' });
    assert.equal(judge('Bash', { command: 'echo {a,b}{c,d}{e,f}{g,h}{i,j}{k,l}' }, scope).verdict, 'ask', 'sixty-four are read, and a card');
});

test('Full access is offered, and keeps the PowerShell tool every other mode takes out', () => {
    assert.equal(fullAccessOffered, true);
    assert.deepEqual(claudeRemovedTools, ['PowerShell', 'Monitor']);
    assert.deepEqual(fullAccessRemovedTools, ['Monitor']);
});

test('the settings of every turn, in every mode, turn off the shell commands a skill carries', () => {
    assert.equal(JSON.parse(claudeTurnSettings).disableSkillShellExecution, true);
    assert.equal(JSON.parse(planTurnSettings).disableSkillShellExecution, true);
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) assert.equal(hookedSettings(mode, 'http://127.0.0.1:4100/hook', 's').disableSkillShellExecution, true, mode);
});

test('Plan only says the AI how it is left, the plan card included, and a go approved for another mode says why it was not sent', () => {
    assert.equal(
        planExitRefusal,
        'Plan only is on, and only the person leaves it, from the menu beside the message box or by approving your plan. Give your plan as your answer.',
    );
    assert.equal(planModeMoved, "The chat's mode changed before the plan was approved, so nothing was sent.");
    // The page declares the same sentence for the refusal it says itself, and its stub desktop refuses with it: one text.
    const page = join(import.meta.dirname, '..', '..', 'ui', 'src');
    const declared = readFileSync(join(page, 'console', 'chat-model.ts'), 'utf8').match(/export const planModeMoved =\s*("[^"]*");/);
    assert.equal(declared === null ? null : JSON.parse(declared[1]!), planModeMoved, 'the page declares the sentence the desktop refuses with');
    assert.ok(readFileSync(join(page, 'test', 'desktop.ts'), 'utf8').includes(JSON.stringify(planModeMoved)), 'and its stub desktop refuses with it');
    assert.equal(planRefusal, 'Plan only is on: nothing is changed. Say what you would do; the person switches the mode to act.', 'unchanged');
});

test('a noun\'s help runs with no card in Plan only, Ask first and Auto-edit, and root help and the other forms are refused as before; under Full access the hook answers nothing to any of them', async (t) => {
    const nouns = helpNouns(fence.allowed, fence.reads);
    for (const mode of ['plan_only', 'ask_first', 'auto_edit'] as const) {
        const w = await wired(t, mode);
        for (const noun of nouns) {
            const command = `jason ${noun} --help`;
            assert.equal((await w.broker.ask(w.id, 'Bash', { command })).behavior, 'allow', `${mode}: ${command}`);
        }
        assert.deepEqual(w.cards, [], mode);
        const root = await w.broker.ask(w.id, 'Bash', { command: 'jason --help' });
        assert.equal(root.behavior, 'deny', mode);
        assert.match(root.behavior === 'deny' ? root.message : '', /run `jason <noun> --help`/, mode);
        assert.equal((await w.broker.ask(w.id, 'Bash', { command: 'jason approval approve --help' })).behavior, 'deny', mode);
    }
    const full = await wired(t, 'full_access');
    for (const command of [...nouns.map((noun) => `jason ${noun} --help`), 'jason --help', 'jason -h', 'jason approval approve --help']) {
        assert.equal(await hooked(full, 'Bash', { command }), null, `Full access: ${command}`);
    }
    // In Plan only a form that is not exactly a noun's help is refused, as any jason command off the plan list is.
    const plan = await wired(t, 'plan_only');
    for (const command of ['jason campaign --help extra', 'jason campaign -h', 'jason -h']) {
        assert.equal((await plan.broker.ask(plan.id, 'Bash', { command })).behavior, 'deny', command);
    }
    assert.deepEqual(plan.cards, []);
});

test('the help form of the PowerShell tool or another tool with a command is a card in Ask first and refused in Plan only, as before; under Full access the hook answers nothing', async (t) => {
    for (const tool of ['PowerShell', 'SomeTool']) {
        const asked = await wired(t, 'ask_first');
        void asked.broker.ask(asked.id, tool, { command: 'jason campaign --help' });
        await new Promise((settle) => setImmediate(settle));
        assert.equal(asked.cards.filter((card) => card.kind === 'permission').length, 1, `${tool} Ask first`);
        asked.broker.endTurn(asked.id);
        const plan = await wired(t, 'plan_only');
        assert.equal((await plan.broker.ask(plan.id, tool, { command: 'jason campaign --help' })).behavior, 'deny', `${tool} Plan only`);
        const full = await wired(t, 'full_access');
        assert.equal(await hooked(full, tool, { command: 'jason campaign --help' }), null, `${tool} Full access`);
    }
});
