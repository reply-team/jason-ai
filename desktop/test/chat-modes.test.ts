// A chat's access modes, short of a whole turn: what each mode starts Claude Code with, the Full access turn's hook file,
// the confirmation's words and the floor, what the broker answers in each mode, and the refusals that must hold under Full
// access, each driven through the hook's own endpoint as Claude Code calls it.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { request } from 'node:http';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { readFence } from '../src/chat-deny.ts';
import { chatRules, claudeAllowedTools, claudeArguments, claudeLine, claudeTurnSettings, fullAccessSettings, planTurnSettings } from '../src/chat-hosts.ts';
import { chatModes, codexFullAccess, fullAccessDialog, fullAccessFloor, fullAccessSentence, isChatMode, planExitRefusal, planRefusal } from '../src/chat-modes.ts';
import { PermissionBroker, type BrokerEvent, type Decided } from '../src/chat-permission-broker.ts';
import { openEndpoint, type Endpoint } from '../src/chat-permission-endpoint.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import { fullAccessJudgement, heldSentence, judge, toolsThatRunNothing, type ChatScope } from '../src/chat-permission-model.ts';
import { realPathOf } from '../src/chat-real-path.ts';
import { newSecret, newTurnId, removeTurnFiles, sweepTurnFiles, turnSettingsPath, writeTurnConfig, writeTurnSettings } from '../src/chat-turn-files.ts';
import { desktopPaths } from '../src/paths.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const base = { mcpConfig: '/data/desktop/turns/turn-0123456789abcdef01234567.mcp.json', addDirs: [], denied: fence.commands, allowed: fence.allowed, reads: fence.reads, dataRules: ['Read(//data/desktop/turns)'], version: '2.1.300' };
const after = (args: string[], flag: string) => args[args.indexOf(flag) + 1];
const values = (args: string[], flag: string) => {
    const rest = args.slice(args.indexOf(flag) + 1);
    const end = rest.findIndex((arg) => arg.startsWith('--'));
    return end < 0 ? rest : rest.slice(0, end);
};

test('the three modes, Ask first first', () => {
    assert.deepEqual(chatModes, ['ask_first', 'plan_only', 'full_access']);
    assert.ok(isChatMode('plan_only'));
    assert.ok(!isChatMode('auto_edit') && !isChatMode('bypassPermissions') && !isChatMode(undefined));
});

test('Ask first starts Claude Code as every turn did before the modes: no permission mode, the settings inline', () => {
    const asked = claudeArguments({ ...base, mode: 'ask_first' });
    assert.deepEqual(asked, claudeArguments(base));
    assert.ok(!asked.includes('--permission-mode'));
    assert.equal(after(asked, '--settings'), claudeTurnSettings);
});

test('Plan only starts Claude Code in plan mode, with the classifier kept out, the plan list in place of the allow list and no way out of planning', () => {
    const args = claudeArguments({ ...base, mode: 'plan_only' });
    assert.equal(after(args, '--permission-mode'), 'plan');
    assert.equal(after(args, '--settings'), planTurnSettings);
    assert.deepEqual(JSON.parse(planTurnSettings), { permissions: { blockReadsOutsideWorkingDirectories: true }, useAutoModeDuringPlan: false, disableClaudeAiConnectors: true, deniedMcpServers: [{ serverName: 'claude-in-chrome' }] });
    const allowed = values(args, '--allowedTools');
    assert.ok(allowed.includes('Bash(jason campaign list:*)'));
    assert.ok(!allowed.includes('Bash(jason campaign create:*)'), 'a verb that changes something is not allowed in Plan only');
    assert.ok(!allowed.some((rule) => rule === 'Bash(jason workitem create:*)' || rule === 'Bash(jason campaign start:*)'));
    const denied = values(args, '--disallowedTools');
    assert.ok(denied.includes('ExitPlanMode'));
    for (const rule of values(claudeArguments(base), '--disallowedTools')) assert.ok(denied.includes(rule), `Plan only keeps ${rule}`);
});

test('Full access starts Claude Code bypassing its prompts, with every deny rule kept and its settings in the turn\'s own file', () => {
    const settingsFile = '/data/desktop/turns/turn-0123456789abcdef01234567.settings.json';
    const args = claudeArguments({ ...base, mode: 'full_access', settingsFile });
    assert.equal(after(args, '--permission-mode'), 'bypassPermissions');
    assert.equal(after(args, '--settings'), settingsFile);
    assert.deepEqual(values(args, '--disallowedTools'), values(claudeArguments(base), '--disallowedTools'));
    for (const kept of ['--strict-mcp-config', '--permission-prompt-tool']) assert.ok(args.includes(kept), kept);
    assert.equal(after(args, '--setting-sources'), 'project');
    assert.ok(!args.includes('--dangerously-skip-permissions'));
    assert.throws(() => claudeArguments({ ...base, mode: 'full_access' }), /settings file/);
});

test('the Full access turn\'s settings: reads outside the folders still blocked, no project file can switch the hook off or filter it, and a hook on every tool that blocks it when it fails', () => {
    const settings = fullAccessSettings('http://127.0.0.1:4100/hook', 'the-secret');
    assert.deepEqual(settings, {
        permissions: { blockReadsOutsideWorkingDirectories: true },
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
    assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), fullAccessSettings('http://127.0.0.1:1/hook', 's'));
    if (process.platform !== 'win32') assert.equal(statSync(file).mode & 0o777, 0o600);
    assert.throws(() => writeTurnSettings(turns, id, 'http://127.0.0.1:1/hook', 's'), 'a file already there is never overwritten');
    removeTurnFiles(turns, id);
    assert.deepEqual(readdirSync(turns), []);
    writeTurnSettings(turns, newTurnId(), 'http://127.0.0.1:1/hook', 's');
    sweepTurnFiles(turns);
    assert.deepEqual(readdirSync(turns), [], 'a settings file a desktop left is swept at the next start');
});

test('Full access is offered from Claude Code 2.1.295, read as the floor is read, and never on Codex', () => {
    assert.equal(fullAccessFloor, '2.1.295');
    assert.equal(fullAccessSentence('2.1.295'), null);
    assert.equal(fullAccessSentence('2.2.0'), null);
    assert.equal(fullAccessSentence('2.1.294'), 'Full access needs Claude Code 2.1.295 or newer; this one is 2.1.294. Update Claude Code, for example by running `claude update` in a terminal.');
    assert.notEqual(fullAccessSentence('2.1.295-beta.1'), null, 'a pre-release of the floor is below it');
    assert.match(fullAccessSentence(null) ?? '', /could not be read/);
    assert.equal(codexFullAccess, 'Full access runs on Claude Code only.');
});

test('the confirmation says in plain words what Full access does, whose risk it is and what it cannot see; Cancel is focused and Escape cancels', () => {
    const dialog = fullAccessDialog();
    assert.equal(dialog.message, 'Give this chat full access?');
    assert.equal(
        dialog.detail,
        'From now on, in this chat, the agent acts on this computer without asking you first: it runs commands, changes and deletes files and opens web pages, and a page it reads can steer what it does. ' +
            'The risk is yours. ' +
            'Jason still refuses the commands that approve, reject or answer for you, and keeps its keys from the agent, wherever it can see the attempt; it cannot see inside a program the agent writes and runs. ' +
            'Full access lasts until you change it or close Jason.',
    );
    assert.deepEqual(dialog.buttons, ['Give full access', 'Cancel']);
    assert.equal(dialog.defaultId, 1);
    assert.equal(dialog.cancelId, 1);
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
        protectedFiles: [paths.installationSettings, paths.runFile],
        denied: fence.commands,
        deniedOperations: fence.operations,
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

/** What Claude Code's hook says for a tool use, as Claude Code posts it: the refusal's reason, or null when it says nothing. */
async function hooked(w: Wired, tool: string, input: Record<string, unknown>, use = `toolu_${Math.random().toString(36).slice(2)}`): Promise<string | null> {
    const body = JSON.stringify({ session_id: 's', hook_event_name: 'PreToolUse', permission_mode: 'bypassPermissions', tool_name: tool, tool_input: input, tool_use_id: use });
    const answered = await post(w.endpoint.hookUrl, body, { authorization: `Bearer ${w.secret}` });
    assert.equal(answered.status, 200);
    if (answered.body === '') return null;
    const output = JSON.parse(answered.body) as { hookSpecificOutput?: { hookEventName?: string; permissionDecision?: string; permissionDecisionReason?: string } };
    assert.equal(output.hookSpecificOutput?.hookEventName, 'PreToolUse');
    assert.equal(output.hookSpecificOutput?.permissionDecision, 'deny');
    return output.hookSpecificOutput?.permissionDecisionReason ?? '';
}

test('under Full access the hook refuses a deny-list verb in a command, however it is wrapped', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['jason approval approve apr_1', 'sh -c "jason decision answer dec_1 --option a"', 'cd x && jason preference set tone short', 'jason --actor human:me approval reject apr_1']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'not refused', /person's own act/, command);
    }
    assert.equal(await hooked(w, 'Bash', { command: 'jason campaign list' }), null, 'an allow-list verb goes on');
    assert.equal(await hooked(w, 'Bash', { command: 'git status' }), null, 'any other command goes on: Full access asks nothing');
});

test('under Full access the hook refuses the same verb inside the content of a file being written', async (t) => {
    const w = await wired(t, 'full_access');
    const file = join(w.scope.chat, 'run.sh');
    assert.match((await hooked(w, 'Write', { file_path: file, content: '#!/bin/sh\njason approval approve apr_1\n' })) ?? 'not refused', /Nothing was written/);
    assert.match((await hooked(w, 'Edit', { file_path: file, old_string: 'x', new_string: 'jason decision answer dec_1' })) ?? 'not refused', /Nothing was written/);
    assert.match((await hooked(w, 'MultiEdit', { file_path: file, edits: [{ old_string: 'a', new_string: 'b' }, { old_string: 'c', new_string: 'jason block clear blk_1' }] })) ?? 'not refused', /Nothing was written/);
    assert.match((await hooked(w, 'Bash', { command: `cat > run.sh <<'EOF'\njason approval approve apr_1\nEOF` })) ?? 'not refused', /person's own act/);
    assert.equal(await hooked(w, 'Write', { file_path: file, content: 'a plan for the spring campaign\n' }), null);
});

test('under Full access the hook refuses an operation named by its dot, in a command or in a file\'s content', async (t) => {
    const w = await wired(t, 'full_access');
    assert.match((await hooked(w, 'Bash', { command: 'curl -X POST http://127.0.0.1:4011/v1/approval.approve -d @body.json' })) ?? 'not refused', /person's own act/);
    assert.match((await hooked(w, 'Write', { file_path: join(w.scope.chat, 'go.py'), content: 'post("/v1/decision.answer", body)' })) ?? 'not refused', /Nothing was written/);
});

test('under Full access the hook refuses the confining variable in any letter case', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['unset JASON_CONFINED', 'export jason_confined=', 'env -u Jason_Confined jason campaign list']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'not refused', /JASON_CONFINED/, command);
    }
    assert.ok((await hooked(w, 'Write', { file_path: join(w.scope.chat, 'x.sh'), content: 'unset JASON_CONFINED' })) !== null);
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
    assert.equal(fullAccessSettings(url, w.secret).hooks.PreToolUse[0]?.hooks[0]?.onFailure, 'block');
});

test('the hook records each tool use it saw, by its id, for its turn alone', async (t) => {
    const w = await wired(t, 'full_access');
    assert.equal(await hooked(w, 'Bash', { command: 'git status' }, 'toolu_seen'), null);
    assert.ok(w.broker.hooked(w.id, 'toolu_seen'));
    assert.ok(!w.broker.hooked(w.id, 'toolu_other'));
    assert.ok(!w.broker.hooked('not-a-turn', 'toolu_seen'));
});

test('under Full access what would have been a card runs without one, and a refusal stays a refusal', async (t) => {
    const w = await wired(t, 'full_access');
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command: 'git push' }), { behavior: 'allow', updatedInput: { command: 'git push' } });
    assert.equal((await w.broker.ask(w.id, 'Read', { file_path: join(w.root, 'desktop', 'turns', 'x.json') })).behavior, 'deny');
    assert.deepEqual(w.cards, []);
    assert.deepEqual(w.decided.map((each) => each.mode), ['full_access', 'full_access']);
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

test('the main process asks the person itself, with the confirmation\'s own box, and takes only its first button as a yes', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.match(main, /const \{ response \} = await dialog\.showMessageBox\(parent, fullAccessDialog\(\)\);\s+return response === 0;/);
    assert.equal(fullAccessDialog().buttons[0], 'Give full access');
});

// Attempts at getting past Full access's refusals, each through the hook as Claude Code calls it, and the same refusals in Ask first.

const held = /Switch this chat to Ask first/;

test('under Full access a jason handed to xargs is held, saying to switch to Ask first', async (t) => {
    const w = await wired(t, 'full_access');
    assert.match((await hooked(w, 'Bash', { command: 'echo approval approve apr_1 | xargs jason' })) ?? 'ran', held);
    assert.match((await hooked(w, 'Bash', { command: 'echo amFzb24K | base64 -d | sh' })) ?? 'ran', held);
    assert.match((await hooked(w, 'Bash', { command: 'printf "jason approval approve apr_1" | sh' })) ?? 'ran', /person's own act/);
});

test('a quoted deny-list verb is refused in every mode; an escaped one is refused in Ask first and held under Full access, which reads no backslash', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['jason approval appro\\ve apr_1', "jason approval approv''e apr_1", 'jason approval ap"pr"ove apr_1', 'jason decision ans\\wer dec_1 --option a']) {
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
    }
    for (const command of ["jason approval approv''e apr_1", 'jason approval ap"pr"ove apr_1']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'ran', /person's own act/, command);
    }
    for (const command of ['jason approval appro\\ve apr_1', 'jason decision ans\\wer dec_1 --option a']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'ran', held, command);
    }
});

test('a jason in an interpreter\'s list form is refused for a deny-list verb in Ask first, and held for any verb under Full access', async (t) => {
    const w = await wired(t, 'full_access');
    const approve = `python3 -c "import subprocess; subprocess.run(['jason','approval','approve','apr_1'])"`;
    // Full access reads no code given as text: it is held, whatever the code says.
    assert.match((await hooked(w, 'Bash', { command: approve })) ?? 'ran', held);
    assert.equal(judge('Bash', { command: approve }, w.scope).verdict, 'deny');
    const list = `python3 -c "import subprocess; subprocess.run(['jason','campaign','list'])"`;
    assert.match((await hooked(w, 'Bash', { command: list })) ?? 'ran', held);
    assert.match((await hooked(w, 'Bash', { command: 'sh -c "jason campaign list"' })) ?? 'ran', held);
});

test('under Full access a variable, an expansion or eval as the program is held', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ['j=jason; $j decision answer dec_1 --option a', '${J} campaign list', '$(which jason) campaign list', 'eval "$CMD"']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'ran', held, command);
    }
});

test('an operation re-spelled by a quote is refused in every mode; by a backslash it is refused in Ask first and held under Full access', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ["curl -X POST http://127.0.0.1:4011/v1/approval.appr''ove -d @b.json", 'curl http://127.0.0.1:4011/v1/decision.ans\\wer']) {
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
    }
    assert.match((await hooked(w, 'Bash', { command: "curl -X POST http://127.0.0.1:4011/v1/approval.appr''ove -d @b.json" })) ?? 'ran', /person's own act/);
    assert.match((await hooked(w, 'Bash', { command: 'curl http://127.0.0.1:4011/v1/decision.ans\\wer' })) ?? 'ran', held);
});

test('a script written with an escaped verb or a re-spelled operation is refused', async (t) => {
    const w = await wired(t, 'full_access');
    const file = join(w.scope.chat, 'go.sh');
    assert.match((await hooked(w, 'Write', { file_path: file, content: 'jason approval appro\\ve apr_1\n' })) ?? 'written', /Nothing was written/);
    assert.match((await hooked(w, 'Write', { file_path: file, content: "curl $U/v1/approval.appr''ove" })) ?? 'written', /Nothing was written/);
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

test('under Full access a glob or a ~ that could expand into the data directory is held, in each platform\'s spelling', () => {
    const windows = homeScope('win32', 'C:\\Users\\someone');
    for (const command of ['cat ~/.jas?n/desktop/provider-keys/*', 'cat /c/Users/someone/.ja?on/run/runtime.json', 'type C:\\Users\\someone\\.ja*\\run\\runtime.json', 'cat ../*/held.json', 'ls ~other/x']) {
        assert.notEqual(fullAccessJudgement('Bash', { command }, windows).outcome, 'runs', `win32: ${command}`);
    }
    const linux = homeScope('linux', '/home/someone');
    for (const command of ['cat ~/.jas?n/run/runtime.json', 'cat /home/someone/.j*/desktop/turns/*', 'cat ../tu*/x']) {
        assert.notEqual(fullAccessJudgement('Bash', { command }, linux).outcome, 'runs', `linux: ${command}`);
    }
    // The glob spellings the plain reading misses are held, not refused: no literal path names the data there.
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat ~/.jas?n/run/runtime.json' }, linux), { outcome: 'held', message: heldSentence, reason: 'glob' });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat /c/Users/someone/.ja?on/run/runtime.json' }, windows), { outcome: 'held', message: heldSentence, reason: 'glob' });
    // A glob is never literal, wherever it points.
    for (const command of ['ls *.md', 'cat notes/*.txt', 'jq .items[0] out.json']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'held', message: heldSentence, reason: 'glob' }, `held: ${command}`);
    }
    for (const command of ['jason campaign list', 'git status', "jq '.items[0]' out.json"]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'runs' }, `runs: ${command}`);
    }
});

test('the chat\'s own instructions and agent settings are refused to a shell command in every mode', async (t) => {
    const w = await wired(t, 'full_access');
    for (const command of ["mkdir -p .claude && printf '%s' '{\"disableAllHooks\":true}' > .claude/settings.json", 'echo planted >> CLAUDE.md', 'cp evil.md .claude/agents/x.md', 'rm -rf .claude']) {
        assert.match((await hooked(w, 'Bash', { command })) ?? 'ran', /the chat never changes it/, command);
        assert.equal(judge('Bash', { command }, w.scope).verdict, 'deny', `Ask first: ${command}`);
    }
    assert.equal(judge('Bash', { command: 'mkdir -p notes && echo x > notes/a.md' }, w.scope).verdict, 'ask', 'a folder of its own is a card as before');
});

test('the host is denied edits of the chat\'s own names directly in the chat folder', () => {
    const rules = chatRules('linux', '/data/desktop/chat');
    for (const rule of ['Edit(//data/desktop/chat/.claude)', 'Edit(//data/desktop/chat/.claude/**)', 'Edit(//data/desktop/chat/CLAUDE.md)', 'Edit(//data/desktop/chat/AGENTS.md)', 'Edit(//data/desktop/chat/.agents/**)']) {
        assert.ok(rules.includes(rule), rule);
    }
});

test('another guarded name keeps its card in Ask first and is held under Full access', async (t) => {
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
        assert.match((await hooked(w, tool, input)) ?? 'ran', held, tool);
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
    assert.match((await hooked(w, 'AskUserQuestion', { questions: [] })) ?? 'ran', held, 'a tool the model does not know is held');
});

test('every tool the host allows without asking runs with no question under Full access', () => {
    for (const tool of claudeAllowedTools.filter((rule) => !rule.includes('('))) assert.ok(toolsThatRunNothing.has(tool), tool);
});

test('what the host still asks under Full access is the hook\'s judgement, and a deletion is held', async (t) => {
    const w = await wired(t, 'full_access');
    assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command: 'echo approval approve apr_1 | xargs jason' }), { behavior: 'deny', message: heldSentence });
    for (const command of ['rm -rf /', 'rm notes.md', 'find . -name "*.tmp" -delete', 'rmdir old']) {
        assert.deepEqual(await w.broker.ask(w.id, 'Bash', { command }), { behavior: 'deny', message: heldSentence }, command);
    }
    assert.equal((await w.broker.ask(w.id, 'Bash', { command: 'git status' })).behavior, 'allow');
});

test('each refusal of the hook is said to the log as refused, by the model, in Full access', async (t) => {
    const w = await wired(t, 'full_access');
    await hooked(w, 'Bash', { command: 'jason approval approve apr_1' });
    await hooked(w, 'Bash', { command: 'echo x | xargs jason' });
    await hooked(w, 'Bash', { command: 'git status' });
    assert.deepEqual(w.decided, [
        { outcome: 'refused', by: 'model', mode: 'full_access' },
        { outcome: 'refused', by: 'model', mode: 'full_access' },
    ]);
});

// The second look's further spellings, in Ask first and under Full access alike.

const judgedConfined = 'That command names JASON_CONFINED, which confines the jason the chat runs: the chat never reads, sets or clears it. Nothing was run.';

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
    // Full access reads none of these spellings: a $, a backslash and a brace are never literal, so the command is held.
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
        assert.equal(fullAccessJudgement('Bash', { command }, scope).outcome, 'held', `Full access: ${JSON.stringify(command)}`);
    }
    // A script the agent writes and then runs is a program running: what holds there is the confined CLI's own bar.
    assert.equal(fullAccessJudgement('Bash', { command: "printf '\\njason approval approve apr_1\\n' > go.sh; sh go.sh" }, scope).outcome, 'runs');
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
