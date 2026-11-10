// The tray, its count and its notifications, and the menu's keys: what the desktop's main process does around the
// window, with the CLI and the window stood in for.

import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import type { Cli, CliResult } from '../src/cli.ts';
import { AttentionWatcher, attentionCadenceMs, notice, readAttention, trayView, type Attention } from '../src/attention.ts';
import { fileLogger } from '../src/log.ts';
import { memoryLog } from './harness.ts';
import { acceleratorPattern, bindings, bindingsFor, desktopCommands, isDesktopCommand, mayCommand } from '../src/keybindings.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const fixtures = join(root, '..', 'docs', 'api', 'fixtures');

/** The response a recorded exchange holds, as the CLI prints it: compact JSON on one line. */
function recorded(operation: string, name: string): string {
    const exchange = JSON.parse(readFileSync(join(fixtures, operation, `${name}.json`), 'utf8')) as { response: unknown };
    return JSON.stringify(exchange.response);
}

/** A CLI that answers each verb from a table and records what it was asked. */
function fakeCli(answer: (args: readonly string[]) => CliResult): Cli & { asked: string[][] } {
    const asked: string[][] = [];
    return {
        asked,
        async run(args) {
            asked.push([...args]);
            return answer(args);
        },
    };
}

const ok = (stdout: string): CliResult => ({ exit: 0, stdout, stderr: '' });
const page = (ids: string[], cursor: string | null = null) => ok(JSON.stringify({ items: ids.map((id) => ({ id, campaign_name: 'Planted Campaign', contact_name: 'Planted Person' })), next_cursor: cursor }));

// ---- The count ----------------------------------------------------------------------------------------------------

test('the count is read through jason approval list and jason decision list, pending only, as the CLI prints them', async () => {
    const cli = fakeCli((args) => ok(recorded(args[0] === 'approval' ? 'approval.list' : 'decision.list', 'pending')));
    const attention = await readAttention(cli);
    const approvals = (JSON.parse(recorded('approval.list', 'pending')) as { items: unknown[] }).items.length;
    const questions = (JSON.parse(recorded('decision.list', 'pending')) as { items: unknown[] }).items.length;
    assert.deepEqual(attention, { state: 'up', approvals, questions });
    assert.deepEqual(cli.asked, [
        ['approval', 'list', '--status', 'pending', '--limit', '1000'],
        ['decision', 'list', '--status', 'pending', '--limit', '1000'],
    ]);
});

test('every page is counted by its cursor, and the reading stops at ten pages', async () => {
    const cli = fakeCli((args) => {
        const cursor = args[args.indexOf('--cursor') + 1];
        if (!args.includes('--cursor')) return page(['a', 'b'], 'c1');
        return cursor === 'c1' ? page(['c'], 'c2') : page(['d']);
    });
    assert.deepEqual(await readAttention(cli), { state: 'up', approvals: 4, questions: 4 });
    assert.deepEqual(cli.asked.slice(0, 3), [
        ['approval', 'list', '--status', 'pending', '--limit', '1000'],
        ['approval', 'list', '--status', 'pending', '--limit', '1000', '--cursor', 'c1'],
        ['approval', 'list', '--status', 'pending', '--limit', '1000', '--cursor', 'c2'],
    ]);

    const endless = fakeCli(() => page(['x'], 'again'));
    assert.deepEqual(await readAttention(endless), { state: 'up', approvals: 10, questions: 10 });
    assert.equal(endless.asked.length, 20);
});

test('a runtime that does not answer, or an answer that is not a list, is the unreachable state, never a count', async () => {
    assert.deepEqual(await readAttention(fakeCli(() => ({ exit: 3, stdout: '', stderr: 'No runtime answered.' }))), { state: 'unreachable' });
    assert.deepEqual(await readAttention(fakeCli(() => ok('{"items":"no"}'))), { state: 'unreachable' });
    assert.deepEqual(await readAttention(fakeCli((args) => (args[0] === 'decision' ? ok('not json') : page([])))), { state: 'unreachable' });
});

test('the tray says what waits, or that the runtime is not answering, by counts alone', () => {
    assert.deepEqual(trayView({ state: 'up', approvals: 0, questions: 0 }), { title: '', tooltip: 'Jason: nothing waits on you', status: 'Nothing waits on you' });
    assert.deepEqual(trayView({ state: 'up', approvals: 2, questions: 1 }), { title: '3', tooltip: 'Jason: 2 approvals and 1 question wait on you', status: '2 approvals and 1 question wait on you' });
    assert.deepEqual(trayView({ state: 'up', approvals: 1, questions: 0 }), { title: '1', tooltip: 'Jason: 1 approval waits on you', status: '1 approval waits on you' });
    assert.deepEqual(trayView({ state: 'unreachable' }), { title: '!', tooltip: 'Jason: the runtime is not answering', status: 'The runtime is not answering' });
    assert.deepEqual(trayView({ state: 'reading' }), { title: '', tooltip: 'Jason', status: 'Reading what waits on you…' });
});

test('a notification carries counts only, never a campaign’s name, a person’s name or a question’s text', () => {
    assert.deepEqual(notice({ approvals: 1, questions: 0 }), { title: 'Jason needs you', body: '1 approval waits on you.' });
    assert.deepEqual(notice({ approvals: 0, questions: 3 }), { title: 'Jason needs you', body: '3 questions wait on you.' });
    assert.deepEqual(notice({ approvals: 2, questions: 2 }), { title: 'Jason needs you', body: '2 approvals and 2 questions wait on you.' });
});

/** Timers by hand: a scheduled tick runs when the test says so. */
function handTimers() {
    const pending: { run: () => void; ms: number }[] = [];
    return {
        pending,
        setTimeout: (run: () => void, ms: number) => {
            const entry = { run, ms };
            pending.push(entry);
            return entry;
        },
        clearTimeout: (entry: unknown) => {
            const at = pending.indexOf(entry as { run: () => void; ms: number });
            if (at >= 0) pending.splice(at, 1);
        },
        async fire() {
            const next = pending.shift();
            assert.ok(next, 'a tick was scheduled');
            next.run();
            await new Promise((resolve) => setImmediate(resolve));
            await new Promise((resolve) => setImmediate(resolve));
        },
    };
}

test('a rise in what waits is notified with its counts; the first reading, a fall and a level count are not', async () => {
    let approvals: string[] = ['a'];
    let questions: string[] = [];
    let down = false;
    const cli = fakeCli((args) => (down ? { exit: 3, stdout: '', stderr: '' } : page(args[0] === 'approval' ? approvals : questions)));
    const timers = handTimers();
    const shown: Attention[] = [];
    const notified: string[] = [];
    const watcher = new AttentionWatcher({ cli, log: memoryLog(), timers, show: (attention) => shown.push(attention), notify: (text) => notified.push(text.body) });

    watcher.start();
    await timers.fire();
    assert.deepEqual(notified, [], 'what already waits when the desktop starts is no news');
    approvals = ['a', 'b'];
    await timers.fire();
    questions = ['q'];
    await timers.fire();
    approvals = ['a'];
    await timers.fire();
    await timers.fire();
    down = true;
    await timers.fire();
    down = false;
    await timers.fire();
    assert.deepEqual(notified, ['2 approvals wait on you.', '2 approvals and 1 question wait on you.'], 'a reading after the runtime came back is compared with the last count');
    assert.deepEqual(shown.map((attention) => attention.state), ['reading', 'up', 'up', 'up', 'up', 'unreachable', 'up']);
    assert.equal(timers.pending.length, 1);
    assert.ok(timers.pending.every((entry) => entry.ms >= 30_000), 'the cadence is not under 30 seconds');
    assert.ok(attentionCadenceMs >= 30_000);
    watcher.stop();
    assert.equal(timers.pending.length, 0);
});

test('the count is shown again only when it changed, and a stopped watcher reads nothing more', async () => {
    const cli = fakeCli(() => page(['a']));
    const timers = handTimers();
    const shown: Attention[] = [];
    const watcher = new AttentionWatcher({ cli, log: memoryLog(), timers, show: (attention) => shown.push(attention), notify: () => undefined });
    watcher.start();
    await timers.fire();
    await timers.fire();
    assert.equal(shown.length, 2, 'reading, then the count once');
    watcher.stop();
    const asked = cli.asked.length;
    assert.equal(timers.pending.length, 0);
    assert.equal(cli.asked.length, asked);
});

test('the count’s log lines, read back, carry counts and states only: no name and no text the runtime answered', async (t) => {
    const directory = mkdtempSync(join(tmpdir(), 'jason-desktop-desk-'));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    let items = ['a'];
    let down = false;
    const cli = fakeCli(() => (down ? { exit: 3, stdout: '', stderr: 'planted-run-token-77 No runtime answered at http://127.0.0.1:41234/?token=planted-run-token-77' } : page(items)));
    const timers = handTimers();
    const watcher = new AttentionWatcher({ cli, log: fileLogger(directory), timers, show: () => undefined, notify: () => undefined });
    watcher.start();
    await timers.fire();
    items = ['a', 'b'];
    await timers.fire();
    down = true;
    await timers.fire();
    watcher.stop();
    const text = readdirSync(directory).map((name) => readFileSync(join(directory, name), 'utf8')).join('');
    for (const planted of ['Planted Campaign', 'Planted Person', 'planted-run-token-77', 'token=']) assert.equal(text.includes(planted), false, `${planted} reached the log`);
    const events = text.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line) as Record<string, unknown>);
    assert.deepEqual(events.map((line) => [line.EventName, line.State ?? line.Approvals]), [['tray_state', 'up'], ['attention_notified', 2], ['tray_state', 'unreachable']]);
});

// ---- The keys -----------------------------------------------------------------------------------------------------

test('every key that sends the page a command sends one the page declares', () => {
    for (const binding of bindings) {
        if ('command' in binding.does) assert.ok(isDesktopCommand(binding.does.command), `${binding.label} is bound to ${binding.does.command}, which the page does not declare`);
    }
});

test('every key is an accelerator Electron reads, and no two keys are the same', () => {
    for (const binding of bindings) assert.match(binding.accelerator, acceleratorPattern, binding.label);
    assert.equal(new Set(bindings.map((binding) => binding.accelerator)).size, bindings.length);
    assert.equal(new Set(bindings.map((binding) => binding.label)).size, bindings.length);
});

test('a packaged build offers no developer tools', () => {
    assert.ok(bindingsFor(false).some((binding) => 'window' in binding.does && binding.does.window === 'devtools'));
    assert.equal(bindingsFor(true).some((binding) => 'window' in binding.does && binding.does.window === 'devtools'), false);
});

test('a command reaches the window only while it shows the runtime’s own page', () => {
    assert.equal(mayCommand('http://127.0.0.1:41234/ui/#/attention', 'http://127.0.0.1:41234'), true);
    assert.equal(mayCommand('http://127.0.0.1:50000/ui/', 'http://127.0.0.1:41234'), false);
    assert.equal(mayCommand('file:///C:/app/shell/starting.html', 'http://127.0.0.1:41234'), false);
    assert.equal(mayCommand('http://127.0.0.1:41234/ui/', null), false);
    assert.equal(mayCommand('', 'http://127.0.0.1:41234'), false);
});

/** The members of the page's `DesktopCommand` union, read from its declaration file. */
function declaredCommands(file: string): string[] {
    const source = ts.createSourceFile(file, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true);
    const found: string[] = [];
    source.forEachChild((node) => {
        if (!ts.isTypeAliasDeclaration(node) || node.name.text !== 'DesktopCommand') return;
        const members = ts.isUnionTypeNode(node.type) ? node.type.types : [node.type];
        for (const member of members) {
            if (ts.isLiteralTypeNode(member) && ts.isStringLiteral(member.literal)) found.push(member.literal.text);
        }
    });
    return found;
}

const pageDeclaration = join(root, '..', 'ui', 'src', 'platform', 'desktop.d.ts');

test('the desktop’s list of commands is the page’s DesktopCommand, member for member', { skip: existsSync(pageDeclaration) ? false : 'the page’s declaration is not in this tree yet' }, () => {
    assert.deepEqual([...desktopCommands], declaredCommands(pageDeclaration));
});
