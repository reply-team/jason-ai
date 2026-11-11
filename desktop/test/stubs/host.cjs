// A stand-in for an agent host running one chat turn: Claude Code's stream-json print mode or Codex's exec JSON
// events, by the name it was started as. What the turn does comes from STUB_HOST_PLAN, a JSON array of steps:
//   { "ask": { "tool": "Bash", "input": { ... } } }  ask the permission prompt tool, then say what it decided
//   { "say": "text" }                                 say a line of text
//   { "use": { "tool": "Read", "input": { ... } } }  use a tool without asking, as one the allowed tools let through
//   { "wait": "<file>" }                              start a process of its own, write that process's id to the file, and hang
//   { "fail": { "stderr": "words", "code": 3 } }      write to stderr and exit with the code, with no result
//   { "end": "no_result" }                            exit 0 with no result
//   { "end": "error", "text": "words" }               end with a failed result
//   { "jam": true }                                   Claude Code only: put a folder where the turn's MCP file is, so its removal fails
//   { "recover": "words" }                            Codex only: report an error that its turn then completes past
//   { "campaign": "name" }                            add a campaign of that name, created now, to STUB_CAMPAIGNS_FILE;
//   { "campaign": { "name", "id", "created_at" } }    or one with the id and the moment given
//   { "sleep": 300 }                                  wait that many milliseconds
// With no plan the turn says one line and ends well. STUB_RESUME says what a resume does: "missing" fails as a session
// that is not there (Claude Code on stderr, Codex with an error event) before anything is said, "other" answers with a
// session other than the one asked for and goes on as a turn would.
const { spawn } = require('node:child_process');
const { mkdirSync, readFileSync, rmSync, writeFileSync } = require('node:fs');
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

function resumed(args) {
    const at = args.indexOf(args[0] === 'exec' ? 'resume' : '--resume');
    return at < 0 ? null : args[at + 1];
}

async function claude(args, stdin) {
    const asked = resumed(args);
    if (asked !== null && process.env.STUB_RESUME === 'missing') {
        process.stderr.write(`No conversation found with session ID: ${asked}\n`);
        process.exit(1);
    }
    const session = asked !== null && process.env.STUB_RESUME === 'other' ? `${asked}-other` : asked ?? (process.env.STUB_HOST_SESSION || 'stub-session-1');
    const config = args[args.indexOf('--mcp-config') + 1];
    print({ type: 'system', subtype: 'init', session_id: session });
    print({ type: 'assistant', message: { content: [{ type: 'text', text: `heard ${stdin.length} characters` }] } });
    let n = 0;
    for (const step of plan()) {
        if (step.say !== undefined) print({ type: 'assistant', message: { content: [{ type: 'text', text: step.say }] } });
        else if (step.campaign !== undefined) addCampaign(step.campaign);
        else if (step.sleep !== undefined) await new Promise((resolve) => setTimeout(resolve, step.sleep));
        else if (step.use !== undefined) print({ type: 'assistant', message: { content: [{ type: 'tool_use', id: `toolu_use_${++n}`, name: step.use.tool, input: step.use.input }] } });
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
    print({ type: 'item.started', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'in_progress' } });
    print({ type: 'item.completed', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'completed' } });
    print({ type: 'item.completed', item: { id: 'item_2', type: 'agent_message', text: `heard ${stdin.length} characters` } });
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
