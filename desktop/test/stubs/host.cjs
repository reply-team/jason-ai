// A stand-in for an agent host running one chat turn: Claude Code's stream-json print mode or Codex's exec JSON
// events, by the name it was started as. What the turn does comes from STUB_HOST_PLAN, a JSON array of steps:
//   { "ask": { "tool": "Bash", "input": { ... } } }  ask the permission prompt tool, then say what it decided
//   { "say": "text" }                                 say a line of text; with "repeat": n, Claude Code says it n times over
//   { "use": { "tool": "Read", "input": { ... } } }  use a tool without asking, as one the allowed tools let through, and
//                                                    say its result; with a Full access settings file (--settings naming a
//                                                    file with a PreToolUse hook), the hook is asked first, as Claude Code
//                                                    asks it in its default mode: a deny, or a hook that fails, says the
//                                                    result refused; an ask goes to the permission prompt tool, whose
//                                                    deny says the result refused; an allow runs it. "prompt": true asks
//                                                    the prompt tool whatever the hook allowed, as Claude Code does for an
//                                                    act no mode approves on its own (a deletion on a critical path)
//   { "unhooked": { "tool": "Bash", "input": {...} } } use a tool and say its result without asking any hook, as a hook
//                                                    filtered out by a managed policy would let it; "prompt": true asks
//                                                    the permission prompt tool first, as the default mode does for a
//                                                    tool no rule allows, and its deny says the result refused
//   { "wait": "<file>" }                              start a process of its own, write that process's id to the file, and hang
//   { "fail": { "stderr": "words", "code": 3 } }      write to stderr and exit with the code, with no result
//   { "end": "no_result" }                            exit 0 with no result
//   { "end": "error", "text": "words" }               end with a failed result
//   { "jam": true }                                   Claude Code only: put a folder where the turn's MCP file is, so its removal fails
//   { "recover": "words" }                            Codex only: report an error that its turn then completes past
//   { "campaign": "name" }                            add a campaign of that name, created now, to STUB_CAMPAIGNS_FILE;
//   { "campaign": { "name", "id", "created_at" } }    or one with the id and the moment given
//   { "sleep": 300 }                                  wait that many milliseconds
//   { "hold": "<file>" }                              wait until that file exists, so a test decides when the turn goes on
//   { "retry": { "status": 401, "error": "..." } }    Claude Code only: report a retry of a failed API request, as system/api_retry
//   { "end": "refused", "status": 401, "text": "w" }  Claude Code only: end with a failed result naming that HTTP status
//   { "refusedMessage": "w" }                         Claude Code only: say an assistant message that reports a failed authentication
// STUB_KEY_SOURCE puts that apiKeySource on Claude Code's init line; without it the line carries none.
// With no plan the turn says one line and ends well. STUB_RESUME says what a resume does: "missing" fails as a session
// that is not there (Claude Code on stderr, Codex with an error event) before anything is said, "other" answers with a
// session other than the one asked for and goes on as a turn would.
// A naming run (`naming` below) answers by STUB_NAMING, a JSON object: { "say": "words" } says them, { "use": true }
// uses a tool first, { "block": "type" } (Claude Code) or { "item": "type" } (Codex) says a content block or an item of
// that type first, { "error": "words" } ends with a failed result, { "sleep": ms } waits first, { "hold": "<file>" } waits until the file exists, { "touch": true } leaves a
// file in the folder it runs in, { "stuck": "<file>" } never answers, outlives the first signal and writes its ids there. With none it ends well and says nothing. STUB_NAMING_LOG, when set, has a line appended
// as each naming run starts and ends.
// STUB_SILENT keeps the turn from saying anything of its own before its plan: "always", or "resumed" on a resume alone,
// as a host that fails on an account, not on the session, ends with nothing said.
const { spawn } = require('node:child_process');
const { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } = require('node:fs');
const { join } = require('node:path');
const { decide } = require('./mcp-http.cjs');

const print = (value) => process.stdout.write(`${JSON.stringify(value)}\n`);
const hang = () => setInterval(() => undefined, 60_000);

function plan() {
    try {
        const steps = JSON.parse(process.env.STUB_HOST_PLAN || '[]');
        return Array.isArray(steps) ? steps : [];
    } catch {
        return [];
    }
}

function wait(file) {
    // On Windows the process is detached, so it leaves the job Node puts its children in and outlives this one unless a
    // stop ends the whole tree; elsewhere it stays in this process's group, which a stop signals.
    const child = spawn(process.execPath, ['-e', 'setInterval(() => undefined, 60000)'], { stdio: 'ignore', windowsHide: true, detached: process.platform === 'win32' });
    writeFileSync(file, String(child.pid));
    hang();
    return true;
}

let made = 0;
function addCampaign(given) {
    const { name, id: named, created_at: at } = typeof given === 'string' ? { name: given } : given;
    const file = process.env.STUB_CAMPAIGNS_FILE;
    let items = [];
    try {
        items = JSON.parse(readFileSync(file, 'utf8'));
    } catch {}
    const id = `cmp_${Date.now().toString(36)}${process.pid}x${++made}`.replace(/[^0-9A-Za-z_]/g, '');
    items.push({ id: named || id, name, status: 'draft', created_at: at || new Date().toISOString() });
    writeFileSync(file, JSON.stringify(items));
}

function silent(asked) {
    return process.env.STUB_SILENT === 'always' || (process.env.STUB_SILENT === 'resumed' && asked !== null);
}

function resumed(args) {
    const at = args.indexOf(args[0] === 'exec' ? 'resume' : '--resume');
    return at < 0 ? null : args[at + 1];
}

/** The turn's PreToolUse hook, when --settings names a file that holds one: its URL, its headers and what a failure does. */
function hookOf(args) {
    const at = args.indexOf('--settings');
    const given = at < 0 ? '' : args[at + 1];
    if (!given || given.startsWith('{')) return null;
    try {
        const settings = JSON.parse(readFileSync(given, 'utf8'));
        const hook = settings.hooks.PreToolUse[0].hooks[0];
        return hook.type === 'http' ? hook : null;
    } catch {
        return null;
    }
}

/** The permission mode the turn was started in: the flag's value, or Claude Code's default. */
function permissionMode(args) {
    const at = args.indexOf('--permission-mode');
    return at < 0 ? 'default' : args[at + 1];
}

/**
 * What the hook decided, as Claude Code reads it: its decision (allow, ask or deny) and reason, no decision for an empty
 * body, or 'failed'.
 */
async function askHook(hook, session, mode, tool, input, id) {
    try {
        const body = JSON.stringify({ session_id: session, hook_event_name: 'PreToolUse', permission_mode: mode, tool_name: tool, tool_input: input, tool_use_id: id });
        const response = await fetch(hook.url, { method: 'POST', headers: { 'content-type': 'application/json', ...hook.headers }, body });
        if (response.status < 200 || response.status > 299) return { failed: true };
        const text = await response.text();
        if (text === '') return { decision: null };
        const output = JSON.parse(text).hookSpecificOutput || {};
        return { decision: output.permissionDecision || null, reason: output.permissionDecisionReason };
    } catch {
        return { failed: true };
    }
}

/** Asks the permission prompt tool, as Claude Code does under -p: the tool result's content and whether it is an error. */
async function prompted(config, tool, input) {
    try {
        const decision = await decide(config, 'jason_desktop', 'decide', tool, input);
        return decision.behavior === 'allow' ? ['ran', false] : [`refused: ${decision.message}`, true];
    } catch (error) {
        return [`refused: the prompt tool failed: ${error.message}`, true];
    }
}

async function claude(args, stdin) {
    const hook = hookOf(args);
    const asked = resumed(args);
    if (asked !== null && process.env.STUB_RESUME === 'missing') {
        process.stderr.write(`No conversation found with session ID: ${asked}\n`);
        process.exit(1);
    }
    const session = asked !== null && process.env.STUB_RESUME === 'other' ? `${asked}-other` : asked ?? (process.env.STUB_HOST_SESSION || 'stub-session-1');
    const config = args[args.indexOf('--mcp-config') + 1];
    print({ type: 'system', subtype: 'init', session_id: session, ...(process.env.STUB_KEY_SOURCE === undefined ? {} : { apiKeySource: process.env.STUB_KEY_SOURCE }) });
    if (!silent(asked)) print({ type: 'assistant', message: { content: [{ type: 'text', text: `heard ${stdin.length} characters` }] } });
    let n = 0;
    for (const step of plan()) {
        if (step.say !== undefined) print({ type: 'assistant', message: { content: [{ type: 'text', text: step.say.repeat(step.repeat ?? 1) }] } });
        else if (step.campaign !== undefined) addCampaign(step.campaign);
        else if (step.sleep !== undefined) await new Promise((resolve) => setTimeout(resolve, step.sleep));
        else if (step.hold !== undefined) while (!existsSync(step.hold)) await new Promise((resolve) => setTimeout(resolve, 20));
        else if (step.refusedMessage !== undefined) {
            print({ type: 'assistant', error: 'authentication_failed', message: { content: [{ type: 'text', text: step.refusedMessage }] } });
        } else if (step.retry !== undefined) {
            print({ type: 'system', subtype: 'api_retry', attempt: 1, max_retries: 10, retry_delay_ms: 600, error_status: step.retry.status ?? null, error: step.retry.error ?? 'unknown', session_id: session });
        } else if (step.end === 'refused') {
            print({ type: 'result', subtype: 'success', is_error: true, api_error_status: step.status, result: step.text, session_id: session });
            process.exit(1);
        } else if (step.use !== undefined) {
            const id = `toolu_use_${++n}`;
            print({ type: 'assistant', message: { content: [{ type: 'tool_use', id, name: step.use.tool, input: step.use.input }] } });
            let content = 'ran';
            let error = false;
            if (hook !== null) {
                const decided = await askHook(hook, session, permissionMode(args), step.use.tool, step.use.input, id);
                if (decided.failed && hook.onFailure === 'block') [content, error] = ['blocked: the hook failed', true];
                else if (decided.decision === 'deny') [content, error] = [`refused: ${decided.reason}`, true];
                else if (decided.decision === 'ask' || step.use.prompt) [content, error] = await prompted(config, step.use.tool, step.use.input);
            } else if (step.use.prompt) [content, error] = await prompted(config, step.use.tool, step.use.input);
            print({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: id, content, is_error: error }] } });
            if (hook !== null) print({ type: 'assistant', message: { content: [{ type: 'text', text: `tool ${content}` }] } });
        } else if (step.unhooked !== undefined) {
            const id = `toolu_unhooked_${++n}`;
            print({ type: 'assistant', message: { content: [{ type: 'tool_use', id, name: step.unhooked.tool, input: step.unhooked.input }] } });
            const [content, error] = step.unhooked.prompt ? await prompted(config, step.unhooked.tool, step.unhooked.input) : ['ran', false];
            print({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: id, content, is_error: error }] } });
            print({ type: 'assistant', message: { content: [{ type: 'text', text: `tool ${content} unhooked` }] } });
        }
        else if (step.ask !== undefined) {
            n++;
            print({ type: 'assistant', message: { content: [{ type: 'tool_use', id: `toolu_${n}`, name: step.ask.tool, input: step.ask.input }] } });
            let said;
            try {
                const decision = await decide(config, 'jason_desktop', 'decide', step.ask.tool, step.ask.input);
                said = decision.behavior === 'allow' ? 'decision allow' : `decision deny: ${decision.message}`;
            } catch (error) {
                said = `decision failed: ${error.message}`;
            }
            print({ type: 'assistant', message: { content: [{ type: 'text', text: said }] } });
        } else if (step.wait !== undefined) return wait(step.wait);
        else if (step.jam !== undefined) {
            rmSync(config, { force: true });
            mkdirSync(config);
            writeFileSync(join(config, 'held'), 'held');
        } else if (step.fail !== undefined) {
            process.stderr.write(`${step.fail.stderr}\n`);
            process.exit(step.fail.code);
        } else if (step.end === 'no_result') process.exit(0);
        else if (step.end === 'error') {
            print({ type: 'result', subtype: 'success', is_error: true, result: step.text, session_id: session });
            process.exit(1);
        }
    }
    print({ type: 'result', subtype: 'success', is_error: false, result: 'done', session_id: session });
    return false;
}

function codex(args, stdin) {
    const asked = resumed(args);
    if (asked !== null && process.env.STUB_RESUME === 'missing') {
        print({ type: 'error', message: `no rollout found for thread id ${asked}` });
        process.exit(1);
    }
    const thread = asked !== null && process.env.STUB_RESUME === 'other' ? `${asked}-other` : asked ?? (process.env.STUB_HOST_SESSION || '0199a213-81c0-7800-8aa1-bbab2a035a53');
    print({ type: 'thread.started', thread_id: thread });
    print({ type: 'turn.started' });
    if (!silent(asked)) {
        print({ type: 'item.started', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'in_progress' } });
        print({ type: 'item.completed', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'completed' } });
        print({ type: 'item.completed', item: { id: 'item_2', type: 'agent_message', text: `heard ${stdin.length} characters` } });
    }
    for (const step of plan()) {
        if (step.say !== undefined) print({ type: 'item.completed', item: { id: `item_say_${step.say.length}`, type: 'agent_message', text: step.say } });
        else if (step.campaign !== undefined) addCampaign(step.campaign);
        else if (step.wait !== undefined) return wait(step.wait);
        else if (step.recover !== undefined) print({ type: 'error', message: step.recover });
        else if (step.end === 'error') {
            print({ type: 'error', message: 'Reconnecting... 1/5' });
            print({ type: 'turn.failed', error: { message: step.text } });
            process.exit(1);
        }
    }
    print({ type: 'turn.completed', usage: { input_tokens: 1, cached_input_tokens: 0, output_tokens: 1 } });
    return false;
}

async function naming(name, args) {
    let asked = {};
    try {
        asked = JSON.parse(process.env.STUB_NAMING || '{}') || {};
    } catch {}
    const log = (word) => process.env.STUB_NAMING_LOG && require('node:fs').appendFileSync(process.env.STUB_NAMING_LOG, `${word} ${process.pid}\n`);
    log('start');
    if (asked.touch) writeFileSync(join(process.cwd(), 'left-behind.txt'), 'x');
    if (asked.stuck !== undefined) {
        // A run that never answers and outlives the first signal, as the stuck version read does: on Windows it leaves a
        // detached process of its own holding its output; the ids of both go to the file named.
        process.on('SIGTERM', () => undefined);
        const ids = [process.pid];
        if (process.platform === 'win32') {
            const kept = spawn(process.execPath, ['-e', 'setInterval(() => undefined, 60000)'], { stdio: ['ignore', 'inherit', 'inherit'], windowsHide: true, detached: true });
            ids.push(kept.pid);
        }
        writeFileSync(asked.stuck, JSON.stringify(ids));
        hang();
        return;
    }
    if (asked.sleep) await new Promise((resolve) => setTimeout(resolve, asked.sleep));
    if (asked.hold !== undefined) while (!existsSync(asked.hold)) await new Promise((resolve) => setTimeout(resolve, 20));
    if (name === 'claude') {
        print({ type: 'system', subtype: 'init', session_id: 'naming-session', ...(process.env.STUB_KEY_SOURCE === undefined ? {} : { apiKeySource: process.env.STUB_KEY_SOURCE }) });
        if (asked.use) print({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'toolu_n', name: 'Read', input: { file_path: '/x' } }] } });
        // A content block of the type named, with no tool use among the ones the turn's reading knows.
        if (asked.block !== undefined) print({ type: 'assistant', message: { content: [{ type: asked.block, id: 'blk_n', name: 'web_fetch', thinking: 'thinking about it', input: {} }] } });
        if (asked.say !== undefined) print({ type: 'assistant', message: { content: [{ type: 'text', text: asked.say }] } });
        print(asked.error !== undefined ? { type: 'result', subtype: 'success', is_error: true, result: asked.error } : { type: 'result', subtype: 'success', is_error: false, result: asked.say ?? '' });
    } else {
        print({ type: 'thread.started', thread_id: '0199a213-81c0-7800-8aa1-bbab2a035a99' });
        print({ type: 'turn.started' });
        if (asked.use) print({ type: 'item.started', item: { id: 'item_n', type: 'command_execution', command: 'ls', status: 'in_progress' } });
        // An item of the type named, started and completed.
        if (asked.item !== undefined) {
            print({ type: 'item.started', item: { id: 'item_t', type: asked.item, text: 'thinking about it' } });
            print({ type: 'item.completed', item: { id: 'item_t', type: asked.item, text: 'thinking about it' } });
        }
        if (asked.say !== undefined) print({ type: 'item.completed', item: { id: 'item_s', type: 'agent_message', text: asked.say } });
        print(asked.error !== undefined ? { type: 'turn.failed', error: { message: asked.error } } : { type: 'turn.completed', usage: { input_tokens: 1, cached_input_tokens: 0, output_tokens: 1 } });
    }
    log('end');
    process.exitCode = asked.error !== undefined ? 1 : 0;
}

module.exports = function host(name, args, stdin) {
    if (name === 'claude') {
        void claude(args, stdin).then(
            (hung) => {
                if (!hung) process.exitCode = 0;
            },
            (error) => {
                process.stderr.write(`${error.stack}\n`);
                process.exit(4);
            },
        );
    } else codex(args, stdin);
};
module.exports.naming = (name, args, stdin) => {
    void naming(name, args, stdin).catch((error) => {
        process.stderr.write(`${error.stack}\n`);
        process.exit(4);
    });
};
