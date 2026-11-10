// A stand-in for an agent host running one chat turn: Claude Code's stream-json print mode or Codex's exec JSON
// events, by the name it was started as. What the turn does comes from STUB_HOST_PLAN, a JSON array of steps:
//   { "ask": { "tool": "Bash", "input": { ... } } }  ask the permission prompt tool, then say what it decided
//   { "say": "text" }                                 say a line of text
//   { "wait": "<file>" }                              start a process of its own, write that process's id to the file, and hang
//   { "fail": { "stderr": "words", "code": 3 } }      write to stderr and exit with the code, with no result
//   { "end": "no_result" }                            exit 0 with no result
//   { "end": "error", "text": "words" }               end with a failed result
// With no plan the turn says one line and ends well.
const { spawn } = require('node:child_process');
const { writeFileSync } = require('node:fs');
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

async function claude(args, stdin) {
    const session = process.env.STUB_HOST_SESSION || 'stub-session-1';
    const config = args[args.indexOf('--mcp-config') + 1];
    print({ type: 'system', subtype: 'init', session_id: session });
    print({ type: 'assistant', message: { content: [{ type: 'text', text: `heard ${stdin.length} characters` }] } });
    let n = 0;
    for (const step of plan()) {
        if (step.say !== undefined) print({ type: 'assistant', message: { content: [{ type: 'text', text: step.say }] } });
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
        else if (step.fail !== undefined) {
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
    print({ type: 'thread.started', thread_id: process.env.STUB_HOST_SESSION || '0199a213-81c0-7800-8aa1-bbab2a035a53' });
    print({ type: 'turn.started' });
    print({ type: 'item.started', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'in_progress' } });
    print({ type: 'item.completed', item: { id: 'item_1', type: 'command_execution', command: 'jason campaign list --json', status: 'completed' } });
    print({ type: 'item.completed', item: { id: 'item_2', type: 'agent_message', text: `heard ${stdin.length} characters` } });
    for (const step of plan()) {
        if (step.say !== undefined) print({ type: 'item.completed', item: { id: `item_say_${step.say.length}`, type: 'agent_message', text: step.say } });
        else if (step.wait !== undefined) return wait(step.wait);
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
