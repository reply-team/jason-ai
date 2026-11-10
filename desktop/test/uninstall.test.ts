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
import type { Whose } from '../src/own-runtime.ts';
import { harnessRoot, inside, uninstall, uninstallExit, uninstallStart, uninstallText, updaterCache, type UninstallDeps } from '../src/uninstall.ts';
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
    options: { loginOn?: boolean; registration?: { registered: boolean; executable: string | null } | null; disabled?: boolean | null; whose?: Whose; stop?: () => Promise<{ exit: number } | null> } = {},
): Bench {
    const folder = join(scratch(t), 'WindowsApps');
    mkdirSync(folder);
    const acts: string[] = [];
    let loginOn = options.loginOn ?? false;
    const deps: UninstallDeps = {
        platform: 'win32',
        folder,
        runtime: {
            whose: async () => options.whose ?? 'none',
            stop: async () => {
                acts.push('runtime stop');
                return options.stop ? options.stop() : null;
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
    };
    return { folder, deps, acts };
}

test('only the uninstaller’s start is an uninstall, and a silent one shows nothing', () => {
    assert.equal(uninstallStart(['Jason.exe']), null);
    assert.equal(uninstallStart(['Jason.exe', '--opened-at-login']), null);
    assert.deepEqual(uninstallStart(['Jason.exe', '--uninstall']), { quiet: false });
    assert.deepEqual(uninstallStart(['Jason.exe', '--uninstall', '--quiet']), { quiet: true });
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
    assert.ok((await uninstall(failed.deps)).left.some((line) => line.includes('could not be taken off')));

    const none = bench(t, { registration: null });
    await uninstall(none.deps);
    assert.deepEqual(none.acts, []);
});

test('what stays is said by name: the data directory, the harness root, the updater’s cache and a running runtime', async (t) => {
    const { deps } = bench(t, { whose: 'another' });
    const report = await uninstall(deps);
    const { message, detail } = uninstallText(report);
    assert.equal(message, 'Jason is being removed from this computer.');
    assert.match(detail, /Nothing else was placed on this computer\./);
    for (const named of [
        'The data directory, C:\\Users\\someone\\.jason',
        'The harness root, C:\\Users\\someone\\Jason',
        'The updater’s cache, C:\\Users\\someone\\AppData\\Local\\jason-desktop-updater',
        'A runtime that is running and is not this application’s: it is not stopped here; jason runtime stop stops it',
    ]) {
        assert.ok(detail.includes(named), named);
    }
    assert.equal(uninstallExit(report), 0);
    // With no runtime running, none is named.
    assert.ok(!(await uninstall(bench(t).deps)).left.some((line) => line.includes('runtime that is running')));
});

test('the runtime this application carries is stopped through jason runtime stop before anything is taken off', async (t) => {
    const { folder, deps, acts } = bench(t, { whose: 'ours', loginOn: true });
    writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
    const report = await uninstall(deps);
    assert.deepEqual(acts, ['runtime stop', 'login false']);
    assert.equal(report.removed[0], 'The runtime this application carries, which was running: it was stopped with jason runtime stop');
    assert.ok(report.removed.includes(join(folder, 'jason.cmd')));
    assert.ok(!report.left.some((line) => line.includes('runtime that is running')));
    assert.equal(uninstallExit(report), 0);
});

test('a runtime of another executable is never stopped by the uninstall', async (t) => {
    const { deps, acts } = bench(t, { whose: 'another' });
    await uninstall(deps);
    assert.ok(!acts.includes('runtime stop'));
});

test('when the runtime this application carries does not stop, nothing is taken off and the uninstall goes no further', async (t) => {
    const stops = [async () => ({ exit: 1 }), async () => ({ exit: -1 }), async (): Promise<{ exit: number } | null> => Promise.reject(new Error('planted-stop-error'))];
    for (const stop of stops) {
        const { folder, deps, acts } = bench(t, { whose: 'ours', loginOn: true, registration: { registered: true, executable }, stop });
        writeFileSync(join(folder, 'jason.cmd'), windowsShim(executable));
        writeFileSync(join(folder, 'jason'), gitBashWrapper(executable));
        const report = await uninstall(deps);
        assert.deepEqual(acts, ['runtime stop'], 'nothing else was done');
        assert.deepEqual(readdirSync(folder).sort(), ['jason', 'jason.cmd'], 'the PATH command stays');
        assert.equal(deps.login.isOn(), true, 'Open Jason at login stays');
        assert.deepEqual([report.removed, report.left], [[], []]);
        assert.equal(uninstallExit(report), 1, 'the uninstaller is told to go no further');
        const { message, detail } = uninstallText(report);
        assert.equal(message, 'Jason was not removed.');
        assert.match(detail, /^Nothing was removed, because the runtime this application carries is running and did not stop \(jason runtime stop (exited 1|did not finish)\)\. Stop it with jason runtime stop, then uninstall Jason again\.$/);
        assert.doesNotMatch(detail, /planted/);
    }
});

test('the harness root and the updater’s cache are found as the runtime and electron-updater find them', (t) => {
    const home = 'C:\\Users\\someone';
    const folder = scratch(t);
    const settings = join(folder, 'settings.json');
    assert.equal(harnessRoot({}, home, settings), join(home, 'Jason'));
    writeFileSync(settings, JSON.stringify({ Harness: { Root: 'D:\\Work\\Jason' } }));
    assert.equal(harnessRoot({}, home, settings), 'D:\\Work\\Jason');
    assert.equal(harnessRoot({ JASON_Harness__Root: 'E:\\Mine' }, home, settings), 'E:\\Mine');
    assert.equal(updaterCache({ LOCALAPPDATA: 'C:\\Users\\someone\\AppData\\Local' }, home), win32.join('C:\\Users\\someone\\AppData\\Local', 'jason-desktop-updater'));
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

test('the nsis uninstaller starts the application before it removes the files, on a person’s uninstall only', () => {
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
        'StrCpy $R2 "0"',
        'ClearErrors',
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall' $R2`,
        '${else}',
        'StrCpy $R2 "0"',
        'ClearErrors',
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall --quiet' $R2`,
        '${endIf}',
        // A start that said to go no further ends the uninstaller before it removes a file.
        '${ifNot} ${Errors}',
        '${if} $R2 != "0"',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
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
    assert.deepEqual(uninstallStart(['x', '--uninstall', '--quiet']), { quiet: true });
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
