// What a person's uninstall takes off besides the application's folder, against files planted in a folder of the test's
// own that stands for %LOCALAPPDATA%\Microsoft\WindowsApps: the PATH command's files that are ours by their content, and
// never a file another program wrote. The nsis uninstaller itself is never run here.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { configuration } from '../electron-builder.config.mjs';
import type { Cli } from '../src/cli.ts';
import { gitBashWrapper, windowsShim } from '../src/command-line.ts';
import { RuntimeAutostart } from '../src/login-item.ts';
import { PathCommand } from '../src/path-command.ts';
import type { StopOutcome } from '../src/own-runtime.ts';
import { harnessRoot, inside, runtimeRefusal, stopRuntimeCode, stopRuntimeFlag, stopRuntimeStart, uninstall, uninstallExit, uninstallStart, uninstallText, updaterCache, type UninstallDeps } from '../src/uninstall.ts';
import { memoryLog } from './harness.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const application = 'C:\\Users\\someone\\AppData\\Local\\Programs\\Jason';
const executable = `${application}\\resources\\bundle\\jason.exe`;
const foreignShim = '@echo off\r\nrem another program\'s jason\r\n"C:\\Tools\\jason.exe" --theirs %*\r\n';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-uninstall-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

interface Bench {
    folder: string;
    deps: UninstallDeps;
    acts: string[];
}

function bench(
    t: { after(fn: () => void): void },
    options: { loginOn?: boolean; registration?: { registered: boolean; executable: string | null } | null; disabled?: boolean | null; stop?: () => Promise<StopOutcome> } = {},
): Bench {
    const folder = join(scratch(t), 'WindowsApps');
    mkdirSync(folder);
    const acts: string[] = [];
    let loginOn = options.loginOn ?? false;
    const deps: UninstallDeps = {
        platform: 'win32',
        folder,
        runtime: {
            // stopOwnRuntime asks jason runtime stop only of a runtime it confirmed as this application's.
            stop: async () => {
                const outcome = await (options.stop ? options.stop() : Promise.resolve<StopOutcome>({ kind: 'none' }));
                if (outcome.kind === 'stopped' || outcome.kind === 'failed') acts.push('runtime stop');
                return outcome;
            },
        },
        command: new PathCommand({ platform: 'win32', folder, executable, dataDirectory: null, path: () => folder, isProgram: () => false, log: memoryLog() }),
        login: {
            isOn: () => loginOn,
            set: async (on) => {
                acts.push(`login ${on}`);
                loginOn = on;
            },
        },
        autostart: {
            status: async () => options.registration ?? { registered: false, executable: null },
            disable: async () => {
                acts.push('autostart disable');
                return options.disabled === undefined ? false : options.disabled;
            },
        },
        application,
        dataDirectory: 'C:\\Users\\someone\\.jason',
        harnessRoot: 'C:\\Users\\someone\\Jason',
        updaterCache: 'C:\\Users\\someone\\AppData\\Local\\jason-desktop-updater',
        bundles: null,
        jason: executable,
        log: memoryLog(),
        // The made-up folders are not on this machine: each path resolves to itself.
        resolve: (path) => path,
    };
    return { folder, deps, acts };
}

test('only the uninstaller’s start is an uninstall, and a silent one shows nothing', () => {
    const installed = { packaged: true, platform: 'win32' } as const;
    assert.equal(uninstallStart(['Jason.exe'], installed), null);
    assert.equal(uninstallStart(['Jason.exe', '--opened-at-login'], installed), null);
    assert.deepEqual(uninstallStart(['Jason.exe', '--uninstall'], installed), { quiet: false });
    assert.deepEqual(uninstallStart(['Jason.exe', '--uninstall', '--quiet'], installed), { quiet: true });
});

test('the installer’s start that stops the carried runtime is honoured only in a packaged application on Windows, and an uninstall beside it wins', () => {
    const installed = { packaged: true, platform: 'win32' } as const;
    assert.equal(stopRuntimeFlag, '--stop-runtime');
    assert.equal(stopRuntimeStart(['Jason.exe', '--stop-runtime'], installed), true);
    assert.equal(stopRuntimeStart(['Jason.exe'], installed), false, 'the flag absent');
    assert.equal(stopRuntimeStart(['Jason.exe', '--opened-at-login'], installed), false);
    assert.equal(stopRuntimeStart(['Jason.exe', '--stop-runtime', '--uninstall'], installed), false, 'the uninstall wins');
    assert.deepEqual(uninstallStart(['Jason.exe', '--stop-runtime', '--uninstall'], installed), { quiet: false });
    assert.equal(uninstallStart(['Jason.exe', '--stop-runtime'], installed), null);
    for (const where of [{ packaged: false, platform: 'win32' }, { packaged: true, platform: 'darwin' }, { packaged: false, platform: 'darwin' }, { packaged: true, platform: 'linux' }] as const) {
        assert.equal(stopRuntimeStart(['Jason', '--stop-runtime'], where), false, JSON.stringify(where));
    }
});

test('the start that stops the carried runtime exits 0 when none of its own runs any more, 1 when one may still run, and 2 when it failed', async () => {
    const cases: { name: string; stop: () => Promise<StopOutcome>; exit: number }[] = [
        { name: 'none', stop: async () => ({ kind: 'none' }), exit: 0 },
        { name: 'another installation’s', stop: async () => ({ kind: 'another' }), exit: 0 },
        { name: 'stopped', stop: async () => ({ kind: 'stopped' }), exit: 0 },
        { name: 'failed', stop: async () => ({ kind: 'failed', exit: 1 }), exit: 1 },
        { name: 'did not finish', stop: async () => ({ kind: 'failed', exit: -1 }), exit: 1 },
        { name: 'unanswered', stop: async () => ({ kind: 'unanswered', pid: 4242, runFile: 'C:/data/run/runtime.json' }), exit: 1 },
        { name: 'unreadable', stop: async () => ({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json', unreadable: true }), exit: 1 },
        { name: 'a rejection', stop: async () => Promise.reject(new Error('planted-stop-error')), exit: 2 },
    ];
    for (const one of cases) assert.equal(await stopRuntimeCode(one.stop), one.exit, one.name);

    // The packaged executable makes this start without a window, a lock or a dialog, and a rejection anywhere exits 2.
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    const start = readFileSync(join(desktop, 'src', 'uninstall-start.ts'), 'utf8');
    const branch = main.slice(main.indexOf('const uninstalling = uninstallStart('), main.indexOf('} else if (!app.requestSingleInstanceLock()) {'));
    assert.ok(branch.includes('} else if (stopRuntimeStart(process.argv, { packaged: app.isPackaged, platform: process.platform })) {'));
    assert.ok(branch.includes('void app.whenReady().then(runStopRuntime).then((code) => app.exit(code), () => app.exit(2));'));
    const from = start.indexOf('export async function runStopRuntime(');
    const run = start.slice(from, start.indexOf('\n}\n', from));
    assert.ok(run.includes('return stopRuntimeCode(() => stopOwnRuntime(carried().own));'));
    assert.doesNotMatch(run, /dialog|BrowserWindow|requestSingleInstanceLock/);
});

test('an uninstall start that fails exits 2, never 0, so the uninstaller says what may be left', () => {
    // app.quit() exits 0, which the uninstaller reads as everything taken off.
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('void app.whenReady().then(() => runUninstall(uninstalling.quiet)).then((code) => app.exit(code), () => app.exit(2));'));
});

test('the uninstall start is honoured only in a packaged application on Windows, where an uninstaller makes it', () => {
    for (const where of [{ packaged: false, platform: 'win32' }, { packaged: true, platform: 'darwin' }, { packaged: false, platform: 'darwin' }, { packaged: true, platform: 'linux' }] as const) {
        assert.equal(uninstallStart(['Jason', '--uninstall'], where), null, JSON.stringify(where));
    }
});

test('the PATH command’s two files that are ours are taken off, and nothing else in the folder', async (t) => {
    const { folder, deps } = bench(t);
    writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
    writeFileSync(join(folder, 'jason'), gitBashWrapper(executable));
    writeFileSync(join(folder, 'python.exe'), '');
    const report = await uninstall(deps);
    assert.deepEqual(readdirSync(folder), ['python.exe']);
    assert.deepEqual(report.removed, [join(folder, 'jason.cmd'), join(folder, 'jason')]);
    assert.ok(!report.left.some((line) => line.includes('another program')));
});

test('a file of ours that runs another executable is still ours, and is taken off with its pair', async (t) => {
    const { folder, deps } = bench(t);
    writeFileSync(join(folder, 'jason.cmd'), windowsShim('D:\\Elsewhere\\jason.exe'));
    writeFileSync(join(folder, 'jason'), gitBashWrapper(executable));
    const report = await uninstall(deps);
    assert.deepEqual(readdirSync(folder), []);
    assert.equal(report.removed.length, 2);
});

test('another program’s jason stays and is named, while the jason-desktop pair placed beside it is taken off', async (t) => {
    const { folder, deps } = bench(t);
    writeFileSync(join(folder, 'jason.cmd'), foreignShim);
    writeFileSync(join(folder, 'jason-desktop.cmd'), windowsShim(executable));
    writeFileSync(join(folder, 'jason-desktop'), gitBashWrapper(executable));
    const report = await uninstall(deps);
    assert.deepEqual(readdirSync(folder), ['jason.cmd']);
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), foreignShim);
    assert.deepEqual(report.removed, [join(folder, 'jason-desktop.cmd'), join(folder, 'jason-desktop')]);
    assert.ok(report.left.includes(`${join(folder, 'jason.cmd')}, which another program wrote`));
});

test('a pair one of whose files another program wrote: ours is taken off, theirs stays and is named', async (t) => {
    const { folder, deps } = bench(t);
    writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
    writeFileSync(join(folder, 'jason'), '#!/bin/sh\nexec /opt/other/jason "$@"\n');
    const report = await uninstall(deps);
    assert.deepEqual(readdirSync(folder), ['jason']);
    assert.deepEqual(report.removed, [join(folder, 'jason.cmd')]);
    assert.ok(report.left.includes(`${join(folder, 'jason')}, which another program wrote`));
});

test('two names both another program’s: nothing is taken off, and both are named', async (t) => {
    const { folder, deps } = bench(t);
    for (const name of ['jason.cmd', 'jason-desktop.cmd']) writeFileSync(join(folder, name), foreignShim);
    const report = await uninstall(deps);
    assert.deepEqual(readdirSync(folder).sort(), ['jason-desktop.cmd', 'jason.cmd']);
    assert.deepEqual(report.removed, []);
    assert.equal(report.left.filter((line) => line.endsWith('which another program wrote')).length, 2);
});

test('Open Jason at login is turned off when it is on, and left alone when it is off', async (t) => {
    const on = bench(t, { loginOn: true });
    assert.ok((await uninstall(on.deps)).removed.includes('Open Jason at login'));
    assert.deepEqual(on.acts, ['login false']);
    const off = bench(t);
    assert.ok(!(await uninstall(off.deps)).removed.includes('Open Jason at login'));
    assert.deepEqual(off.acts, []);
});

test('the runtime’s logon registration is taken off only when it starts this application’s executable', async (t) => {
    const ours = bench(t, { registration: { registered: true, executable: executable.toUpperCase() } });
    assert.ok((await uninstall(ours.deps)).removed.includes('The runtime’s start at logon'));
    assert.deepEqual(ours.acts, ['autostart disable']);

    const theirs = bench(t, { registration: { registered: true, executable: 'C:\\Users\\someone\\.jason\\bin\\jason.exe' } });
    const report = await uninstall(theirs.deps);
    assert.deepEqual(theirs.acts, []);
    assert.ok(report.left.some((line) => line.includes('starts C:\\Users\\someone\\.jason\\bin\\jason.exe, not this application’s')));

    const failed = bench(t, { registration: { registered: true, executable }, disabled: null });
    assert.ok((await uninstall(failed.deps)).failed.some((line) => line.includes('could not be taken off')));

    const none = bench(t, { registration: null });
    await uninstall(none.deps);
    assert.deepEqual(none.acts, []);
});

test('what stays is said by name: the data directory, the harness root, the updater’s cache and a running runtime', async (t) => {
    const { deps } = bench(t, { stop: async () => ({ kind: 'another' }) });
    const report = await uninstall(deps);
    const { message, detail } = uninstallText(report);
    assert.equal(message, 'Jason is being removed from this computer.');
    assert.match(detail, /^Nothing was taken off\.\n\nLeft in place:\n/);
    assert.doesNotMatch(detail, /placed/);
    for (const named of [
        'The data directory, C:\\Users\\someone\\.jason',
        'The harness root, C:\\Users\\someone\\Jason',
        'The updater’s cache, C:\\Users\\someone\\AppData\\Local\\jason-desktop-updater',
        // The uninstaller removes the carried jason next, so the hand act names the jason the runtime was started from.
        'A runtime is running that this application could not confirm as its own: it was not stopped; the jason it was started from stops it with runtime stop',
    ]) {
        assert.ok(detail.includes(named), named);
    }
    assert.equal(uninstallExit(report), 0);
    // With no runtime running, none is named.
    assert.ok(!(await uninstall(bench(t).deps)).left.some((line) => line.includes('A runtime is running')));
});

test('the runtime this application carries is stopped through jason runtime stop before anything is taken off', async (t) => {
    const { folder, deps, acts } = bench(t, { stop: async () => ({ kind: 'stopped' }), loginOn: true });
    writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
    const report = await uninstall(deps);
    assert.deepEqual(acts, ['runtime stop', 'login false']);
    assert.equal(report.removed[0], 'The runtime this application carries, which was running: it was stopped with jason runtime stop');
    assert.ok(report.removed.includes(join(folder, 'jason.cmd')));
    assert.ok(!report.left.some((line) => line.includes('A runtime is running')));
    assert.equal(uninstallExit(report), 0);
});

test('a runtime of another executable is never stopped by the uninstall, and the uninstall goes on', async (t) => {
    const { deps, acts } = bench(t, { stop: async () => ({ kind: 'another' }), loginOn: true });
    const report = await uninstall(deps);
    assert.deepEqual(acts, ['login false']);
    assert.equal(uninstallExit(report), 0);
});

test('when the runtime this application carries does not stop, or a runtime did not answer, nothing is taken off and the uninstall goes no further', async (t) => {
    const stops: { stop: () => Promise<StopOutcome>; acts: string[] }[] = [
        { stop: async () => ({ kind: 'failed', exit: 1 }), acts: ['runtime stop'] },
        { stop: async () => ({ kind: 'failed', exit: -1 }), acts: ['runtime stop'] },
        { stop: async () => Promise.reject(new Error('planted-stop-error')), acts: [] },
        { stop: async () => ({ kind: 'unanswered', pid: 4242, runFile: 'C:/data/run/runtime.json' }), acts: [] },
        { stop: async () => ({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json' }), acts: [] },
        { stop: async () => ({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json', unreadable: true }), acts: [] },
    ];
    for (const { stop, acts: asked } of stops) {
        const { folder, deps, acts } = bench(t, { loginOn: true, registration: { registered: true, executable }, stop });
        writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
        writeFileSync(join(folder, 'jason'), gitBashWrapper(executable));
        const report = await uninstall(deps);
        assert.deepEqual(acts, asked, 'nothing else was done');
        assert.deepEqual(readdirSync(folder).sort(), ['jason', 'jason.cmd'], 'the PATH command stays');
        assert.equal(deps.login.isOn(), true, 'Open Jason at login stays');
        assert.deepEqual([report.removed, report.untouched, report.failed, report.left], [[], [], [], []]);
        assert.equal(uninstallExit(report), 1, 'the uninstaller is told to go no further');
        const { message, detail } = uninstallText(report);
        assert.equal(message, 'Jason was not removed.');
        assert.match(
            detail,
            /^Nothing was removed, because (the runtime this application carries is running and did not stop \(jason runtime stop (exited 1|did not finish)\)\. Stop it with C:\\Users\\someone\\AppData\\Local\\Programs\\Jason\\resources\\bundle\\jason\.exe runtime stop, then uninstall Jason again|a runtime may be running( that did not answer(: its run file names process 4242, which is running|, and its run file names no process)|: its run file, [^,]+, is there and could not be read)\. C:\\Users\\someone\\AppData\\Local\\Programs\\Jason\\resources\\bundle\\jason\.exe runtime status answers once the runtime does[^]*\. Then uninstall Jason again)\.$/,
        );
        assert.doesNotMatch(detail, /planted/);
    }
    assert.equal(
        runtimeRefusal({ kind: 'unanswered', pid: 4242, runFile: 'C:/data/run/runtime.json' }),
        'Nothing was removed, because a runtime may be running that did not answer: its run file names process 4242, which is running. jason runtime status answers once the runtime does. jason runtime start says whether process 4242 is the runtime: it names it if it is, and starts one in place of the run file if it is not. If it is the runtime and never answers, end process 4242. If no Jason runtime is running, deleting C:/data/run/runtime.json lets this go on. Then uninstall Jason again.',
    );
    assert.equal(
        runtimeRefusal({ kind: 'unanswered', pid: null, runFile: 'C:/data/run/runtime.json' }),
        'Nothing was removed, because a runtime may be running that did not answer, and its run file names no process. jason runtime status answers once the runtime does, and jason runtime start starts one in place of a run file left behind. If no Jason runtime is running, deleting C:/data/run/runtime.json lets this go on. Then uninstall Jason again.',
    );
});

test('a removal that fails is named with its act among what could not be taken off, said first among what is left, the others still run, and the uninstall still exits 0', async (t) => {
    const { folder, deps, acts } = bench(t, { loginOn: true, registration: { registered: true, executable } });
    writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
    deps.command = { status: deps.command.status.bind(deps.command), remove: () => { throw new Error('planted-remove-error'); } };
    deps.login = { isOn: () => true, set: async () => { acts.push('login false'); throw new Error('planted-login-error'); } };
    deps.autostart = { status: deps.autostart!.status, disable: async () => { acts.push('autostart disable'); throw new Error('planted-disable-error'); } };
    const report = await uninstall(deps);
    assert.deepEqual(acts, ['login false', 'autostart disable'], 'each removal is tried after one before it failed');
    assert.deepEqual(report.removed, []);
    for (const named of [
        `${join(folder, 'jason.cmd')}, which could not be taken off: deleting the file takes it off`,
        'Open Jason at login, which could not be turned off: the system’s list of apps that open at login holds it',
        // The uninstaller removes the carried jason next, so the hand act names the registration itself.
        'The runtime’s start at logon, which could not be taken off: Task Scheduler holds it as the task named Jason- followed by your account’s SID; deleting that task takes it off',
    ]) {
        assert.ok(report.failed.includes(named), named);
        assert.ok(!report.left.includes(named), named);
    }
    const shown = uninstallText(report).detail;
    assert.match(shown, /\n\nLeft in place:\n• Open Jason at login, which could not be turned off/);
    assert.ok(shown.indexOf('jason.cmd, which could not be taken off') < shown.indexOf('• The data directory'));
    assert.equal(report.refused, null);
    assert.equal(uninstallExit(report), 0);
    assert.doesNotMatch(uninstallText(report).detail, /planted/);

    // Open Jason at login that cannot be read is said as one that could not be turned off, and the rest still runs.
    const unknown = bench(t, { registration: { registered: true, executable } });
    unknown.deps.login = { isOn: () => { throw new Error('planted-login-read-error'); }, set: async () => void unknown.acts.push('login false') };
    const after = await uninstall(unknown.deps);
    assert.deepEqual(unknown.acts, ['autostart disable']);
    assert.ok(after.failed.includes('Open Jason at login, which could not be turned off: the system’s list of apps that open at login holds it'));
    assert.equal(uninstallExit(after), 0);
    assert.doesNotMatch(uninstallText(after).detail, /planted/);

    // A registration that cannot be read is said, and nothing is disabled.
    const unread = bench(t);
    unread.deps.autostart = { status: async () => Promise.reject(new Error('planted-status-error')), disable: async () => (unread.acts.push('autostart disable'), false) };
    const read = await uninstall(unread.deps);
    assert.deepEqual(unread.acts, []);
    assert.ok(read.failed.includes('The runtime’s start at logon, which could not be read: Task Scheduler holds it as the task named Jason- followed by your account’s SID; deleting that task takes it off'));
    assert.equal(uninstallExit(read), 0);

    // A status the CLI did not answer is one that could not be read, too, and nothing is disabled.
    const silent = bench(t);
    silent.deps.autostart = { status: async () => null, disable: async () => (silent.acts.push('autostart disable'), false) };
    const quiet = await uninstall(silent.deps);
    assert.deepEqual(silent.acts, []);
    assert.ok(quiet.failed.includes('The runtime’s start at logon, which could not be read: Task Scheduler holds it as the task named Jason- followed by your account’s SID; deleting that task takes it off'));
});

test('the harness root is read from the settings without regard to the case of its keys, as the runtime reads them', (t) => {
    const home = 'C:\\Users\\someone';
    const settings = join(scratch(t), 'settings.json');
    for (const written of [{ harness: { root: 'D:\\Lower' } }, { HARNESS: { ROOT: 'D:\\Upper' } }, { Harness: { rOOt: 'D:\\Mixed' } }]) {
        writeFileSync(settings, JSON.stringify(written));
        assert.equal(harnessRoot({}, home, settings), Object.values(Object.values(written)[0]!)[0]);
    }
    writeFileSync(settings, JSON.stringify({ harness: { root: 7 } }));
    assert.equal(harnessRoot({}, home, settings), join(home, 'Jason'));
    assert.equal(harnessRoot({ JASON_HARNESS__ROOT: 'E:\\Upper' }, home, settings), 'E:\\Upper');
    assert.equal(harnessRoot({ jason_harness__root: 'E:\\Lower' }, home, settings), 'E:\\Lower');
});

test('the harness root and the updater’s cache are found as the runtime and electron-updater find them', (t) => {
    const home = 'C:\\Users\\someone';
    const folder = scratch(t);
    const settings = join(folder, 'settings.json');
    assert.equal(harnessRoot({}, home, settings), join(home, 'Jason'));
    writeFileSync(settings, JSON.stringify({ Harness: { Root: 'D:\\Work\\Jason' } }));
    assert.equal(harnessRoot({}, home, settings), 'D:\\Work\\Jason');
    assert.equal(harnessRoot({ JASON_Harness__Root: 'E:\\Mine' }, home, settings), 'E:\\Mine');
    assert.equal(updaterCache({ LOCALAPPDATA: 'C:\\Users\\someone\\AppData\\Local' }, home, 'win32'), win32.join('C:\\Users\\someone\\AppData\\Local', 'jason-desktop-updater'));
    // On macOS and Linux as the pinned electron-updater's AppAdapter.getAppCacheDir reads it: ~/Library/Caches; XDG_CACHE_HOME, else ~/.cache.
    assert.equal(updaterCache({ XDG_CACHE_HOME: '/Users/someone/cache' }, '/Users/someone', 'darwin'), '/Users/someone/Library/Caches/jason-desktop-updater');
    assert.equal(updaterCache({ XDG_CACHE_HOME: '/home/someone/cache' }, '/home/someone', 'linux'), '/home/someone/cache/jason-desktop-updater');
    assert.equal(updaterCache({ XDG_CACHE_HOME: '' }, '/home/someone', 'linux'), '/home/someone/.cache/jason-desktop-updater');
    assert.equal(updaterCache({}, '/home/someone', 'linux'), '/home/someone/.cache/jason-desktop-updater');
    // electron-updater names the cache after the package's name.
    assert.equal(JSON.parse(readFileSync(join(desktop, 'package.json'), 'utf8')).name, 'jason-desktop');
});

test('inside the application’s folder is decided by the platform’s own path rules', () => {
    assert.ok(inside('win32', application, executable));
    assert.ok(inside('win32', application, executable.toLowerCase()));
    assert.ok(!inside('win32', application, `${application}-old\\jason.exe`));
    assert.ok(!inside('win32', application, application));
    assert.ok(inside('darwin', '/Applications/Jason.app', '/Applications/Jason.app/Contents/Resources/bundle/jason'));
    assert.ok(!inside('darwin', '/Applications/Jason.app', posix.join('/Applications/jason.app', 'Contents', 'jason')));
});

test('the nsis uninstaller starts the application before it removes the files, on a person’s uninstall only, and goes no further only when the start says so', () => {
    assert.equal(configuration({ version: '0.34.0' }).nsis.include, 'installer/uninstall.nsh');
    const script = readFileSync(join(desktop, 'installer', 'uninstall.nsh'), 'utf8');
    // From the uninstall's own macro on; the check that closes running programs comes before it, with rows of its own
    // (installer-processes.test.ts).
    const lines = script.slice(script.indexOf('!macro customUnInstall\n')).split(/\r?\n/).filter((line) => line.trim() !== '' && !line.trim().startsWith(';')).map((line) => line.trim());
    assert.deepEqual(lines, [
        '!macro customUnInstall',
        '${ifNot} ${isUpdated}',
        '${GetParameters} $R0',
        'ClearErrors',
        '${GetOptions} $R0 "/S" $R1',
        '${if} ${Errors}',
        'StrCpy $R3 ""',
        'StrCpy $R2 "0"',
        'ClearErrors',
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall' $R2`,
        '${else}',
        'StrCpy $R3 "quiet"',
        'StrCpy $R2 "0"',
        'ClearErrors',
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall --quiet' $R2`,
        '${endIf}',
        '${if} ${Errors}',
        'StrCpy $R2 "none"',
        '${endIf}',
        // Only the start's own word, exit 1, ends the uninstaller before it removes a file.
        '${if} $R2 == "1"',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        // A start that crashed, or could not be made, never blocks the uninstall; a person is told what may be left.
        '${if} $R2 != "0"',
        '${andIf} $R3 == ""',
        'MessageBox MB_OK|MB_ICONEXCLAMATION "Jason could not take off what it placed outside its folder, so the jason command on the PATH, Open Jason at login and the runtime\'s start at logon may be left. The uninstall goes on."',
        '${endIf}',
        '${endIf}',
        '!macroend',
    ]);
    // The pinned template runs the macro before it removes the installed files, and nothing before it removes any.
    const section = readFileSync(join(desktop, 'node_modules', 'app-builder-lib', 'templates', 'nsis', 'uninstaller.nsh'), 'utf8');
    const body = section.slice(section.indexOf('Section "un.${UNINSTALL_SECTION_NAME}"'));
    const before = body.slice(0, body.indexOf('!insertmacro customUnInstall'));
    assert.ok(before.length > 0 && body.indexOf('# delete the installed files') > before.length);
    assert.doesNotMatch(before, /Delete|RMDir|DeleteReg|WinShell::Unin/);

    // The flags the script passes are the ones the application reads.
    assert.deepEqual(uninstallStart(['x', '--uninstall', '--quiet'], { packaged: true, platform: 'win32' }), { quiet: true });
});

test('the logon registration is read from what jason runtime autostart status says it starts', async () => {
    const answers = [
        { exit: 0, stdout: JSON.stringify({ registered: true, platform: 'windows', executable }), stderr: '' },
        { exit: 0, stdout: JSON.stringify({ registered: false, platform: 'windows', executable: null }), stderr: '' },
        { exit: 0, stdout: 'not json', stderr: '' },
        { exit: 3, stdout: '', stderr: 'unreachable' },
    ];
    const asked: string[][] = [];
    const cli: Cli = { run: async (args) => (asked.push([...args]), answers.shift() ?? { exit: -1, stdout: '', stderr: '' }) };
    const autostart = new RuntimeAutostart({ cli, log: memoryLog() });
    assert.deepEqual(await autostart.registration(), { registered: true, executable });
    assert.deepEqual(await autostart.registration(), { registered: false, executable: null });
    assert.equal(await autostart.registration(), null);
    assert.equal(await autostart.registration(), null);
    assert.deepEqual(asked, Array(4).fill(['runtime', 'autostart', 'status']));
});
