// Uninstall Jason…, the macOS menu's door to the one uninstall, against stand-ins: nothing is started but a Node of the
// test's own, and nothing quits.

import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { startJasonAlone } from '../src/cli.ts';
import { uninstallArgs, uninstallJason, uninstallLabel, uninstallOffered, uninstallOutput, uninstallQuestion, type UninstallScope } from '../src/uninstall-door.ts';
import { memoryLog } from './harness.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

const scope: UninstallScope = {
    application: '/Applications/Jason.app',
    dataDirectory: '/Users/someone/.jason',
    harnessRoot: '/Users/someone/Jason',
    output: '/tmp/jason-uninstall-2026-11-17T12-00-00-000Z.json',
};

test('on Linux the item is offered too, and its dialog names what goes last: the deb’s package through the system, or the AppImage file', () => {
    const base = { dataDirectory: '/home/a/.jason', harnessRoot: '/home/a/Jason', output: '/tmp/out.json' };
    const deb = uninstallQuestion({ ...base, application: '/opt/Jason', form: 'deb' });
    assert.equal(deb.message, 'Uninstall Jason from this computer?');
    assert.ok(deb.detail.includes('Jason itself, the package jason-desktop, taken off by your system first: it asks for an administrator’s password'), deb.detail);
    const appImage = uninstallQuestion({ ...base, application: '/home/a/Applications/jason-desktop-linux-x64.AppImage', form: 'appimage' });
    assert.equal(appImage.message, 'Uninstall Jason from this computer?');
    assert.ok(appImage.detail.includes('Jason itself, /home/a/Applications/jason-desktop-linux-x64.AppImage, last'), appImage.detail);
    assert.equal(uninstallQuestion({ ...base, application: '/Applications/Jason.app' }).message, 'Uninstall Jason from this Mac?');
});

test('the item is offered on macOS and Linux, under its own name', () => {
    assert.equal(uninstallOffered('darwin'), true);
    assert.equal(uninstallOffered('win32'), false);
    assert.equal(uninstallOffered('linux'), true);
    assert.equal(uninstallLabel, 'Uninstall Jason…');
});

test('the dialog says what goes and what stays, and the data directory goes only on a box the person ticks', () => {
    const question = uninstallQuestion(scope);
    assert.equal(question.message, 'Uninstall Jason from this Mac?');
    assert.deepEqual(question.buttons, ['Uninstall', 'Cancel']);
    assert.equal(question.defaultId, 1, 'Cancel is the default');
    assert.equal(question.cancelId, 1);
    assert.equal(question.checkboxChecked, false, 'the data directory is kept unless the person says otherwise');
    assert.equal(question.checkboxLabel, 'Also delete the data directory');
    assert.ok(question.detail.includes('Jason itself, /Applications/Jason.app, last'));
    assert.ok(question.detail.includes('The harness root, /Users/someone/Jason, with your folders and memory files, always'));
    assert.ok(question.detail.includes('The data directory, /Users/someone/.jason, with the campaigns, the journal, the settings and the logs, unless you tick the box below'));
    assert.ok(question.detail.includes(`What was done is written to ${scope.output}.`));
    assert.equal(uninstallOutput('/tmp', new Date('2026-11-17T12:00:00.000Z')), join('/tmp', 'jason-uninstall-2026-11-17T12-00-00-000Z.json'));
});

test('the verb waits for this process always, and purges only on the box', () => {
    assert.deepEqual(uninstallArgs(false, 4321), ['uninstall', '--wait-for', '4321']);
    assert.deepEqual(uninstallArgs(true, 4321), ['uninstall', '--purge-data', '--yes', '--wait-for', '4321']);
});

test('on the person’s word the uninstall is started on its own and the application quits; Cancel starts nothing', async () => {
    for (const [answer, expected] of [
        [{ response: 1, checkboxChecked: true }, null],
        [{ response: 0, checkboxChecked: false }, ['uninstall', '--wait-for', '77']],
        [{ response: 0, checkboxChecked: true }, ['uninstall', '--purge-data', '--yes', '--wait-for', '77']],
    ] as const) {
        const started: { args: string[]; output: string }[] = [];
        let quit = 0;
        const log = memoryLog();
        const outcome = await uninstallJason({ scope: () => scope, ask: async () => answer, start: (args, output) => started.push({ args, output }), pid: 77, quit: () => (quit += 1), log });
        if (expected === null) {
            assert.equal(outcome, 'cancelled');
            assert.deepEqual(started, []);
            assert.equal(quit, 0);
            assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['uninstall_asked', { Answer: 'cancel' }]]);
        } else {
            assert.equal(outcome, 'started');
            assert.deepEqual(started, [{ args: [...expected], output: scope.output }]);
            assert.equal(quit, 1, 'the application quits so the verb can begin');
        }
    }
});

test('the jason it starts outlives this process, its output in the file the dialog named', async (t) => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-uninstall-door-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const output = join(folder, 'out.json');
    startJasonAlone(process.execPath, ['-e', 'console.log(JSON.stringify({ args: process.argv.length }))'], { PATH: process.env['PATH'] ?? '' }, output);
    for (let tries = 0; tries < 100 && !(existsSync(output) && readFileSync(output, 'utf8').includes('args')); tries += 1) {
        await new Promise((resolve) => setTimeout(resolve, 50));
    }
    assert.match(readFileSync(output, 'utf8'), /\{"args":\d+\}/);
});

test('the menu offers the item on macOS in place of Remove What Jason Placed, from the carried jason, started with this process’s id', () => {
    const desk = readFileSync(join(desktop, 'src', 'desk.ts'), 'utf8');
    assert.ok(desk.includes('if (door !== null) return [{ label: uninstallLabel, enabled: app.isPackaged, click: () => void this.uninstallAct(door) }];'));
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('const uninstall = offers.removal && uninstallOffered(process.platform) && version !== null && carried.outlives'));
    assert.ok(main.includes('start: (args: string[], output: string) => startJasonAlone(executable, args, env, output),'));
    assert.ok(main.includes('pid: process.pid,'));
    const cli = readFileSync(join(desktop, 'src', 'cli.ts'), 'utf8');
    assert.ok(cli.includes("const child = spawn(file, [...args], { env: withoutSessionMarkers(env), detached: true, stdio: ['ignore', handle, handle], windowsHide: true });"));
});
