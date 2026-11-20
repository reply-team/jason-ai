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
import { acceleratorPattern, bindings, bindingsFor, desktopCommands, isDesktopCommand, itemKeys, keyWords, mayCommand, type DesktopCommand } from '../src/keybindings.ts';
import type { DesktopCommand as PageCommand } from '../../ui/src/platform/desktop.d.ts';
import { actShown, updateAnswerOf } from '../src/update-item.ts';
import { menuItemOf, pressItem, type UpdateState } from '../src/updates.ts';

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
    assert.deepEqual(trayView({ state: 'up', approvals: 0, questions: 0 }), { title: '', tooltip: 'Jason AI Harness: nothing waits on you', status: 'Nothing waits on you' });
    assert.deepEqual(trayView({ state: 'up', approvals: 2, questions: 1 }), { title: '3', tooltip: 'Jason AI Harness: 2 approvals and 1 question wait on you', status: '2 approvals and 1 question wait on you' });
    assert.deepEqual(trayView({ state: 'up', approvals: 1, questions: 0 }), { title: '1', tooltip: 'Jason AI Harness: 1 approval waits on you', status: '1 approval waits on you' });
    assert.deepEqual(trayView({ state: 'unreachable' }), { title: '!', tooltip: 'Jason AI Harness: the runtime is not answering', status: 'The runtime is not answering' });
    assert.deepEqual(trayView({ state: 'reading' }), { title: '', tooltip: 'Jason AI Harness', status: 'Reading what waits on you…' });
    // The Demo reads nothing: it is the sample, and the tray says so.
    assert.deepEqual(trayView({ state: 'demo' }), { title: '', tooltip: 'Jason AI Harness: the demo', status: 'The demo: sample data, nothing is sent' });
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

test('a start that failed shows the runtime as not answering in the tray, and reads nothing until a pairing succeeds', async () => {
    const cli = fakeCli(() => page(['a']));
    const timers = handTimers();
    const shown: Attention[] = [];
    const log = memoryLog();
    const watcher = new AttentionWatcher({ cli, log, timers, show: (attention) => shown.push(attention), notify: () => undefined });
    watcher.unreachable();
    assert.deepEqual(shown, [{ state: 'unreachable' }]);
    assert.equal(trayView(shown[0]!).status, 'The runtime is not answering');
    assert.deepEqual(cli.asked, [], 'nothing is read before the first pairing');
    assert.equal(timers.pending.length, 0);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['tray_state', { State: 'unreachable' }]]);

    watcher.start();
    await timers.fire();
    assert.deepEqual(shown.map((attention) => attention.state), ['unreachable', 'unreachable', 'up']);
    watcher.unreachable();
    assert.equal(shown.length, 3, 'a watcher that reads shows what it read, not the failed start');
    watcher.stop();
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

test('the update item and the way back from the Demo have keys of their own, which no other key of the window takes', () => {
    // Where a window shows no menu bar and the desktop no tray, these two acts have no other way in.
    // One spelling for each key: CommandOrControl as CmdOrCtrl, the modifiers in one order.
    const plain = (key: string) => {
        const parts = key.replace(/CommandOrControl/g, 'CmdOrCtrl').split('+');
        return [...parts.slice(0, -1).sort(), parts.at(-1)].join('+');
    };
    const taken = [
        ...bindings.map((binding) => binding.accelerator),
        'CmdOrCtrl+Q', // Quit Jason
        'CmdOrCtrl+K', // the console's palette
        // The Edit and Window menus' own roles and full screen, as Electron 44 gives them on each system.
        'CmdOrCtrl+Z', 'Shift+CmdOrCtrl+Z', 'Control+Y', 'CmdOrCtrl+X', 'CmdOrCtrl+C', 'CmdOrCtrl+V', 'CmdOrCtrl+A',
        'CmdOrCtrl+M', 'CmdOrCtrl+W', 'F11', 'Command+H', 'Command+Alt+H', 'Control+Command+F',
        // GNOME's input method takes Ctrl+Shift+U for a Unicode character while a text field has the focus.
        'CmdOrCtrl+Shift+U',
    ].map(plain);
    for (const key of Object.values(itemKeys)) {
        assert.match(key, acceleratorPattern, key);
        assert.ok(!taken.includes(plain(key)), `${key} is taken`);
    }
    assert.notEqual(itemKeys.update, itemKeys.back);
    // A person reads each as their system spells it.
    assert.equal(keyWords(itemKeys.back, 'linux'), 'Ctrl+Shift+L');
    assert.equal(keyWords(itemKeys.back, 'win32'), 'Ctrl+Shift+L');
    assert.equal(keyWords(itemKeys.back, 'darwin'), 'Cmd+Shift+L');
    assert.equal(keyWords(itemKeys.update, 'linux'), 'Ctrl+Shift+D');
    // The menu's two items carry them, in the window's menu and the tray's alike.
    const desk = readFileSync(join(root, 'src', 'desk.ts'), 'utf8');
    const method = (name: string) => desk.slice(desk.indexOf(`private ${name}(`), desk.indexOf('\n    }\n', desk.indexOf(`private ${name}(`)));
    assert.match(method('updateItems'), /accelerator: itemKeys\.update,/);
    assert.match(method('backItems'), /accelerator: itemKeys\.back,/);
    // A press of the update item goes through `pressItem`, which asks first when the key brought the install and installs
    // only on yes, asked in the desk's own box. The tray's menu has no key, and on Linux it reports a click with no mouse
    // button, which Electron counts as a key's (`triggeredByAccelerator`): a press there is always a click.
    assert.match(method('updateItems'), /click: \(_menuItem, _window, event\) => this\.pressUpdate\(item\.act, !inTray && event\.triggeredByAccelerator === true, 'menu'\),/);
    assert.ok(desk.includes('...this.updateItems(false),'), 'the window’s menu: its key asks');
    assert.match(method('renderTray'), /\.\.\.this\.updateItems\(true\),/);
    // The page and the versions-differ sentence take the update key's words from the desktop, and only where the item can
    // act (`updateKeyWords`): the seam's `updates.key` and the setup's sentence answer from the same function.
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('const updateKey = () => updateKeyWords(updates !== null, updates?.active ?? false, process.platform);'), 'the key where the item can act');
    assert.ok(main.includes("handle(seam, 'updates.key', origin, updateKey, refused);"), 'updates.key answers it');
    assert.match(main, /\n {8}updateKey,\n/, 'the setup names it');
});

// The update item stands in the menu and at the foot of the page's Settings, and both are the one item: the page is
// answered the menu's own item, label and enabled alike, and its press is a click in the menu, through the one method
// the menu's click and key go through. No other way reaches `pressItem`.
test('the page’s update item is the menu’s own, and its press goes the one way the menu’s does', () => {
    const desk = readFileSync(join(root, 'src', 'desk.ts'), 'utf8');
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const body = (name: string) => {
        const at = desk.indexOf(`\n    ${name}(`);
        return desk.slice(at, desk.indexOf('\n    }\n', at));
    };
    // One call of pressItem in the desktop's sources, in the one method, behind the guard of Remove What Jason Placed.
    const sources = readdirSync(join(root, 'src')).filter((name) => name.endsWith('.ts') && name !== 'updates.ts');
    const calls = sources.flatMap((name) => [...readFileSync(join(root, 'src', name), 'utf8').matchAll(/\bpressItem\(/g)].map(() => name));
    assert.deepEqual(calls, ['desk.ts']);
    const press = body('pressUpdate');
    assert.ok(press.startsWith("\n    pressUpdate(act: MenuItem['act'], byKey: boolean, from: 'menu' | 'page'): void {"));
    const pressed = 'void pressItem(updates, act, byKey, (question) => this.box(question)).then((said) => {';
    assert.ok(press.includes(pressed));
    assert.ok(press.indexOf('if (this.removing()) return;') < press.indexOf(pressed), 'the guard first');
    // What a check the person asked for found: asked from the menu, the tray or the key, whose menu closes or shows no
    // label, it comes as a notification too, whose click shows the window; asked from Settings, the page's line alone says
    // it. The sentence is in the item's answer either way (`updates.said`).
    assert.ok(press.includes("if (said !== null && from === 'menu') this.notify(updateNotice(said), () => this.show());"));
    assert.equal(desk.split('this.notify(').length - 1, 2, 'the attention watcher and the update check');
    assert.ok(desk.includes("notify: (text) => this.notify(text, () => this.send('navigate.attention')),"), 'the attention notice opens What Needs You');
    const notify = desk.slice(desk.indexOf('    private notify('), desk.indexOf('\n    }\n', desk.indexOf('    private notify(')));
    assert.ok(notify.includes('if (!Notification.isSupported()) return;'));
    assert.ok(notify.includes("notification.on('click', clicked);"));
    // The item the menu draws is the one the page is answered, disabled alike while Remove What Jason Placed runs, and
    // none in a demo run.
    const item = body('updateItem');
    assert.ok(item.includes('if (this.demo !== null) return null;'));
    assert.ok(item.includes("const item = menuItemOf(updates?.current ?? { step: 'idle' }, updates !== null, updates?.active ?? true);"));
    assert.ok(item.includes('return { ...item, enabled: item.enabled && !this.removing() };'));
    assert.match(desk.slice(desk.indexOf('private updateItems('), desk.indexOf('updateItem(): MenuItem | null')), /const item = this\.updateItem\(\);\n {8}if \(item === null\) return \[\];/);
    // The seam: the item answered with the desktop's version, its act never; a press is a click, never the key's.
    assert.ok(main.includes("handle(seam, 'updates.item', origin, () => updateAnswerOf(app.getVersion(), desk.updateItem(), updates?.said ?? null), refused);"));
    assert.ok(main.includes("handle(seam, 'updates.press', origin, (label) => desk.pressUpdate(actShown(desk.updateItem(), label), false, 'page'), refused);"));
    assert.equal(main.split('desk.pressUpdate(').length - 1, 1, 'the seam presses once, as a click');
    // The page hears of every new drawing of the menu, so its item follows the menu's; the demo's run has no seam to tell.
    assert.ok(body('renderMenu').includes('this.renderTray();\n        this.deps.menuChanged?.();'));
    assert.ok(main.includes("        menuChanged: () => toPage('updates.changed'),\n    });"));
    assert.equal(main.split('menuChanged:').length - 1, 1, 'Live alone');
});

// The page's press acts on what the person saw: it sends the label it drew, and the desktop presses only while its item
// is enabled and says that very label. A step that moved on between the word of the change and the page's repaint is
// never pressed by a click meant for the step before: above all the install, which a click does without asking.
test('a press from the page acts only on the item it drew: the same label, enabled now', async () => {
    const item = (step: UpdateState) => menuItemOf(step, true, true);
    const offered = item({ step: 'offered', version: '0.48.6' });
    const downloaded = item({ step: 'downloaded', version: '0.48.6' });
    assert.equal(actShown(offered, 'Download Jason 0.48.6'), 'download');
    assert.equal(actShown(downloaded, 'Restart Jason and Its Runtime to Install 0.48.6'), 'install');
    // The item moved on from the label the person saw: nothing.
    assert.equal(actShown(downloaded, 'Download Jason 0.48.6'), null);
    assert.equal(actShown(item({ step: 'idle' }), 'Download Jason 0.48.6'), null);
    // Disabled now (a step under way, or Remove What Jason Placed running), no item, or no label: nothing.
    assert.equal(actShown(item({ step: 'downloading', version: '0.48.6' }), 'Downloading Jason 0.48.6…'), null);
    assert.equal(actShown({ ...offered, enabled: false }, 'Download Jason 0.48.6'), null);
    assert.equal(actShown(null, 'Check for Updates…'), null);
    for (const label of [undefined, null, 1, {}, ['Download Jason 0.48.6']]) assert.equal(actShown(offered, label), null);
    // Through the menu's own press: once with the current label, never with an old one, and never a question asked.
    const acts: string[] = [];
    const updater = {
        current: { step: 'downloaded', version: '0.48.6' } as UpdateState,
        check: async () => {
            acts.push('check');
            return null;
        },
        download: async () => void acts.push('download'),
        install: async () => {
            acts.push('install');
            return true;
        },
    };
    const ask = async () => {
        acts.push('asked');
        return 0;
    };
    await pressItem(updater, actShown(downloaded, 'Download Jason 0.48.6'), false, ask);
    assert.deepEqual(acts, [], 'the old label presses nothing');
    await pressItem(updater, actShown(downloaded, 'Restart Jason and Its Runtime to Install 0.48.6'), false, ask);
    assert.deepEqual(acts, ['install'], 'the current label presses once, as a click');
    // The page is answered the label and whether it is enabled, never the act.
    assert.deepEqual(updateAnswerOf('0.48.5', offered, null), { version: '0.48.5', item: { label: 'Download Jason 0.48.6', enabled: true }, said: null });
    assert.deepEqual(updateAnswerOf('0.48.5', null, null), { version: '0.48.5', item: null, said: null });
    // What the person's last check found rides the same answer: no other member of the seam carries it.
    assert.deepEqual(updateAnswerOf('0.48.5', offered, 'Version 0.48.6 is available.'), {
        version: '0.48.5',
        item: { label: 'Download Jason 0.48.6', enabled: true },
        said: 'Version 0.48.6 is available.',
    });
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

// The desktop's commands are the page's at the type level, as seam-types.test.ts holds the seam: `npm run check` is red
// when the page declares a command the desktop's list lacks, or the list names one the page does not declare. The list
// itself stays, for `isDesktopCommand` to read at run time.
type Equal<A, B> = (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;
type Holds<Check extends true> = Check;
export type CommandsAreThePage = Holds<Equal<DesktopCommand, PageCommand>>;

test('the desktop’s list of commands is the page’s DesktopCommand, member for member and in order', () => {
    assert.ok(existsSync(pageDeclaration), `the page’s declaration ${pageDeclaration} is not in this tree`);
    assert.deepEqual([...desktopCommands], declaredCommands(pageDeclaration));
});
