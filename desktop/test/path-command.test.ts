// The PATH command: a `jason` the person's terminal finds, which runs the executable the application carries. On
// Windows a one-line shim in the WindowsApps folder, on macOS a wrapper in ~/.local/bin; placed and removed only on
// the person's word, read back on every look, and never put in the place of a `jason` that is not ours.

import assert from 'node:assert/strict';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { fileLogger } from '../src/log.ts';
import { commandFolder, commandMenuOf, commandQuestion, PathCommand, sameFile, type PathCommandDeps } from '../src/path-command.ts';
import { memoryLog } from './harness.ts';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-path-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

const windowsExe = 'C:\\Users\\someone\\AppData\\Local\\Programs\\Jason\\resources\\bundle\\jason.exe';
const macExe = '/Applications/Jason.app/Contents/Resources/bundle/jason';

/** A Windows desk over a folder of the test's own, which stands for %LOCALAPPDATA%\Microsoft\WindowsApps. */
function windows(t: { after(fn: () => void): void }, options: Partial<PathCommandDeps> & { onPath?: boolean; others?: string[] } = {}) {
    const root = scratch(t);
    const folder = join(root, 'WindowsApps');
    mkdirSync(folder);
    const others = options.others ?? [];
    const path = [...(options.onPath === false ? [] : [folder]), ...others].join(';');
    const log = memoryLog();
    const command = new PathCommand({ platform: 'win32', folder, executable: windowsExe, dataDirectory: null, path: () => path, isProgram: (file) => existsSync(file), log, ...options });
    return { root, folder, command, log };
}

test('the folder is %LOCALAPPDATA%\\Microsoft\\WindowsApps on Windows, ~/.local/bin on macOS, none elsewhere', () => {
    assert.equal(commandFolder('win32', { LOCALAPPDATA: 'C:\\Users\\someone\\AppData\\Local' }, 'C:\\Users\\someone'), win32.join('C:\\Users\\someone\\AppData\\Local', 'Microsoft', 'WindowsApps'));
    assert.equal(commandFolder('win32', {}, 'C:\\Users\\someone'), null);
    assert.equal(commandFolder('darwin', {}, '/Users/someone'), posix.join('/Users/someone', '.local', 'bin'));
    assert.equal(commandFolder('linux', {}, '/home/someone'), null);
});

test('on Windows, with the folder on the PATH and no jason anywhere, the shim is offered and nothing is placed until the person says so', (t) => {
    const { folder, command } = windows(t);
    const status = command.status();
    assert.deepEqual(status, { state: 'absent', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], folderOnPath: true, shadows: null, executable: windowsExe });
    assert.deepEqual(readdirSync(folder), [], 'a look places nothing');
});

test('placing the shim writes one line that runs the bundled executable by its full path with every argument, and reads back as placed', (t) => {
    const { folder, command, log } = windows(t);
    assert.deepEqual(command.place(), { state: 'placed', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], shadowedBy: null, executable: windowsExe });
    assert.deepEqual(readdirSync(folder).sort(), ['jason', 'jason.cmd']);
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), `@echo off\r\n"${windowsExe}" %*\r\n`);
    assert.deepEqual(command.status(), { state: 'placed', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], shadowedBy: null, executable: windowsExe });
    assert.deepEqual(log.lines, [{ level: 'info', name: 'path_command_set', fields: { On: true, Name: 'jason', Platform: 'win32' } }]);

    assert.deepEqual(command.remove(), command.status());
    assert.equal(command.status().state, 'absent');
    assert.deepEqual(readdirSync(folder), []);
    assert.deepEqual(log.lines.at(-1), { level: 'info', name: 'path_command_set', fields: { On: false, Name: 'jason', Platform: 'win32' } });
});

test('on Windows a Git Bash wrapper goes beside the shim: placed and removed with it, read back with it, and named by the same rule', (t) => {
    const { folder, command } = windows(t);
    const shim = join(folder, 'jason.cmd');
    const wrapper = join(folder, 'jason');
    const forward = windowsExe.replaceAll('\\', '/');
    const wrapperText = `#!/bin/sh\n# Written by the Jason desktop application.\nexec '${forward}' "$@"\n`;
    assert.deepEqual(command.status(), { state: 'absent', name: 'jason', files: [shim, wrapper], folderOnPath: true, shadows: null, executable: windowsExe });
    command.place();
    assert.deepEqual(readdirSync(folder).sort(), ['jason', 'jason.cmd']);
    assert.equal(readFileSync(wrapper, 'utf8'), wrapperText, 'the macOS wrapper’s text, the path in forward slashes');
    assert.equal(command.status().state, 'placed');
    const menu = commandMenuOf(command.status());
    assert.ok(menu.lines.some((line) => line.includes('Command Prompt, PowerShell and Git Bash') && line.includes(shim) && line.includes(wrapper)), 'the line names the shells it serves');

    // Either file differing is pointing elsewhere: another executable, or the wrapper gone.
    writeFileSync(wrapper, wrapperText.replace(forward, 'D:/Old/jason.exe'));
    assert.deepEqual(command.status(), { state: 'elsewhere', name: 'jason', files: [shim, wrapper], why: `${wrapper} runs D:/Old/jason.exe`, executable: windowsExe });
    rmSync(wrapper);
    assert.deepEqual(command.status(), { state: 'elsewhere', name: 'jason', files: [shim, wrapper], why: `${wrapper} is missing`, executable: windowsExe });
    command.place();
    assert.equal(command.status().state, 'placed', 'placed again on the word, both files');
    command.remove();
    assert.deepEqual(readdirSync(folder), [], 'removed together');

    // A wrapper the desktop did not write is never replaced or removed, and the command is then jason-desktop, both files.
    writeFileSync(wrapper, '#!/bin/sh\necho mine\n');
    assert.deepEqual(command.status(), { state: 'absent', name: 'jason-desktop', files: [join(folder, 'jason-desktop.cmd'), join(folder, 'jason-desktop')], folderOnPath: true, shadows: wrapper, executable: windowsExe });
    command.place();
    command.remove();
    assert.deepEqual(readdirSync(folder), ['jason']);
    assert.equal(readFileSync(wrapper, 'utf8'), '#!/bin/sh\necho mine\n');
});

test('on Windows, with the folder not on the PATH, nothing is placed and the bundled executable’s path is what is shown', (t) => {
    const { folder, command, log } = windows(t, { onPath: false });
    assert.deepEqual(command.status(), { state: 'absent', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], folderOnPath: false, shadows: null, executable: windowsExe });
    assert.throws(() => command.place(), /not on the PATH/);
    assert.deepEqual(readdirSync(folder), []);
    assert.deepEqual(log.lines, []);
    const menu = commandMenuOf(command.status());
    assert.ok(menu.lines.some((line) => line.includes(windowsExe)));
    assert.deepEqual(menu.acts, []);
});

test('the folder on the PATH is matched as Windows matches it: any case, a trailing separator, quotes', (t) => {
    const root = scratch(t);
    const folder = join(root, 'WindowsApps');
    mkdirSync(folder);
    const make = (path: string) => new PathCommand({ platform: 'win32', folder, executable: windowsExe, dataDirectory: null, path: () => path, isProgram: (file) => existsSync(file), log: memoryLog() });
    for (const entry of [folder.toUpperCase(), `${folder}\\`, `"${folder}"`]) assert.equal((make(`C:\\Windows;${entry}`).status() as { folderOnPath: boolean }).folderOnPath, true, entry);
    assert.equal((make(`C:\\Windows;${folder}x`).status() as { folderOnPath: boolean }).folderOnPath, false);
});

test('a jason on the PATH that is not ours is never shadowed: the shim is named jason-desktop and the other is named', (t) => {
    // Another installation's jason, found by the PATH as Windows would find it (the lookup is injected, so the row holds on
    // any machine the tests run on).
    const other = 'C:\\Tools\\Jason\\jason.exe';
    const { folder, command } = windows(t, { others: ['C:\\Tools\\Jason'], isProgram: (file) => file === other || existsSync(file) });
    const status = command.status();
    assert.deepEqual(status, { state: 'absent', name: 'jason-desktop', files: [join(folder, 'jason-desktop.cmd'), join(folder, 'jason-desktop')], folderOnPath: true, shadows: other, executable: windowsExe });
    command.place();
    assert.deepEqual(readdirSync(folder).sort(), ['jason-desktop', 'jason-desktop.cmd']);
    assert.deepEqual(command.status(), { state: 'placed', name: 'jason-desktop', files: [join(folder, 'jason-desktop.cmd'), join(folder, 'jason-desktop')], shadowedBy: null, executable: windowsExe });
});

test('a jason.cmd in the folder that the desktop did not write is never replaced, and never removed', (t) => {
    const { folder, command } = windows(t);
    writeFileSync(join(folder, 'jason.cmd'), '@echo off\r\necho mine\r\n');
    assert.equal(command.status().state, 'absent');
    assert.equal((command.status() as { name: string }).name, 'jason-desktop');
    command.place();
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), '@echo off\r\necho mine\r\n');
    command.remove();
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), '@echo off\r\necho mine\r\n');
    assert.deepEqual(readdirSync(folder).sort(), ['jason.cmd']);

    writeFileSync(join(folder, 'jason-desktop.cmd'), 'also not ours');
    assert.equal(command.status().state, 'taken');
    assert.throws(() => command.place(), /not the desktop’s/);
    command.remove();
    assert.equal(readFileSync(join(folder, 'jason-desktop.cmd'), 'utf8'), 'also not ours');
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), '@echo off\r\necho mine\r\n');
});

test('a shim of ours that runs another executable is shown as pointing elsewhere, never rewritten by a look, and placed again only on the word', (t) => {
    const { folder, command } = windows(t);
    const older = 'D:\\Old\\Jason\\resources\\bundle\\jason.exe';
    writeFileSync(join(folder, 'jason.cmd'), `@echo off\r\n"${older}" %*\r\n`);
    assert.deepEqual(command.status(), { state: 'elsewhere', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], why: `${join(folder, 'jason.cmd')} runs ${older}`, executable: windowsExe });
    command.status();
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), `@echo off\r\n"${older}" %*\r\n`, 'a look rewrites nothing');
    const menu = commandMenuOf(command.status());
    assert.ok(menu.lines.some((line) => line.includes(older)) && menu.lines.some((line) => line.includes(windowsExe)));
    assert.deepEqual(menu.acts.map((act) => act.act), ['place', 'remove']);
    command.place();
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), `@echo off\r\n"${windowsExe}" %*\r\n`);
});

test('the shim names a data directory only when the desktop runs with one, and keeps it out of the terminal it runs in', (t) => {
    const { folder, command } = windows(t, { dataDirectory: 'D:\\Jason data' });
    command.place();
    assert.equal(readFileSync(join(folder, 'jason.cmd'), 'utf8'), `@echo off\r\nsetlocal\r\nset "JASON_DATA_DIR=D:\\Jason data"\r\n"${windowsExe}" %*\r\n`);
    assert.equal(command.status().state, 'placed');
    const other = windows(t, { dataDirectory: 'D:\\100%' });
    assert.throws(() => other.command.place(), /batch file would read/);
    assert.deepEqual(readdirSync(other.folder), []);
});

test('a command of ours that names another data directory, or none where the desktop runs with one, points elsewhere', (t) => {
    const { folder, command } = windows(t);
    const shim = join(folder, 'jason.cmd');
    const wrapper = join(folder, 'jason');
    command.place();
    const withData = (dataDirectory: string | null) => new PathCommand({ platform: 'win32', folder, executable: windowsExe, dataDirectory, path: () => folder, isProgram: (file) => existsSync(file), log: memoryLog() });
    assert.deepEqual(withData('D:\\Jason data').status(), { state: 'elsewhere', name: 'jason', files: [shim, wrapper], why: `${shim} names no data directory`, executable: windowsExe });
    withData('D:\\Jason data').place();
    assert.equal(withData('D:\\Jason data').status().state, 'placed', 'placed again on the word, with the data directory');
    assert.deepEqual(withData(null).status(), { state: 'elsewhere', name: 'jason', files: [shim, wrapper], why: `${shim} names the data directory D:\\Jason data`, executable: windowsExe });
    assert.deepEqual(withData('E:\\Other').status(), { state: 'elsewhere', name: 'jason', files: [shim, wrapper], why: `${shim} names the data directory D:\\Jason data`, executable: windowsExe });
});

test('a jason earlier on the PATH than the one placed is said: the placed line names what a terminal finds first', (t) => {
    const root = scratch(t);
    const folder = join(root, 'WindowsApps');
    mkdirSync(folder);
    const other = 'C:\\Tools\\Jason\\jason.exe';
    let path = folder;
    let installed = false;
    // The folder is the host's (POSIX on Linux), while the lookup joins as Windows does; so the folder's files are known by
    // a Windows comparison of the paths, not by asking the host's file system for a path in the other shape.
    const windowsShape = (file: string) => win32.normalize(file).toLowerCase();
    const inFolder = (file: string) => existsSync(folder) && readdirSync(folder).some((name) => windowsShape(join(folder, name)) === windowsShape(file));
    const found: string[] = [];
    const isProgram = (file: string) => {
        const program = (installed && file === other) || inFolder(file);
        if (program) found.push(file);
        return program;
    };
    const command = new PathCommand({ platform: 'win32', folder, executable: windowsExe, dataDirectory: null, path: () => path, isProgram, log: memoryLog() });
    command.place();
    found.length = 0;
    assert.equal((command.status() as { shadowedBy: string | null }).shadowedBy, null, 'ours is the one found');
    assert.deepEqual(found.map(windowsShape), [windowsShape(join(folder, 'jason.cmd'))], 'the lookup found our shim, not nothing');
    // Another installation is put on the PATH ahead of the folder after the command was placed.
    installed = true;
    path = `C:\\Tools\\Jason;${folder}`;
    const status = command.status();
    assert.deepEqual(status, { state: 'placed', name: 'jason', files: [join(folder, 'jason.cmd'), join(folder, 'jason')], shadowedBy: other, executable: windowsExe });
    assert.ok(commandMenuOf(status).lines.includes(`Another jason earlier on the PATH is found first: ${other}.`));
    // Behind ours, it shadows nothing.
    path = `${folder};C:\\Tools\\Jason`;
    assert.equal((command.status() as { shadowedBy: string | null }).shadowedBy, null);
});

test('a file the PATH finds is told from ours as the platform tells paths apart: separators and case on Windows, exactly on macOS', () => {
    // What the lookup joins against what the host joined: the same file, in two shapes.
    assert.equal(sameFile('win32', '/tmp/x/WindowsApps/jason.cmd', '\\tmp\\x\\WindowsApps\\jason.cmd'), true);
    assert.equal(sameFile('win32', 'C:\\Users\\A\\WindowsApps\\jason.cmd', 'c:/users/a/windowsapps/JASON.CMD'), true);
    assert.equal(sameFile('win32', 'C:\\W\\jason.cmd', 'C:\\W\\jason'), false);
    assert.equal(sameFile('darwin', '/Users/a/.local/bin/jason', '/Users/a/.local//bin/jason'), true);
    assert.equal(sameFile('darwin', '/Users/a/.local/bin/jason', '/users/a/.local/bin/jason'), false);
});

test('the confirmation names the command it places or removes, and every file it writes', () => {
    const exe = 'C:\\J\\jason.exe';
    const files = ['C:\\W\\jason-desktop.cmd', 'C:\\W\\jason-desktop'];
    assert.deepEqual(commandQuestion('place', { state: 'absent', name: 'jason-desktop', files, folderOnPath: true, shadows: 'C:\\other\\jason.exe', executable: exe }), {
        message: 'Put the jason-desktop command on the PATH?',
        detail: 'This writes C:\\W\\jason-desktop.cmd and C:\\W\\jason-desktop, which run C:\\J\\jason.exe.',
        button: 'Place It',
    });
    assert.deepEqual(commandQuestion('remove', { state: 'placed', name: 'jason', files: ['C:\\W\\jason.cmd', 'C:\\W\\jason'], shadowedBy: null, executable: exe }), {
        message: 'Remove the jason command from the PATH?',
        detail: 'This removes C:\\W\\jason.cmd and C:\\W\\jason.',
        button: 'Remove It',
    });
});

test('on macOS the wrapper is ~/.local/bin/jason, executable, made with its folder, read back, and removed', (t) => {
    const root = scratch(t);
    const folder = join(root, 'home', '.local', 'bin');
    const log = memoryLog();
    const make = (dataDirectory: string | null) => new PathCommand({ platform: 'darwin', folder, executable: macExe, dataDirectory, path: () => '/usr/bin:/bin', isProgram: (file) => existsSync(file), log });
    const command = make(null);
    assert.deepEqual(command.status(), { state: 'absent', name: 'jason', files: [join(folder, 'jason')], folderOnPath: false, shadows: null, executable: macExe });
    assert.equal(existsSync(folder), false, 'a look makes no folder');
    command.place();
    assert.equal(readFileSync(join(folder, 'jason'), 'utf8'), `#!/bin/sh\n# Written by the Jason desktop application.\nexec '${macExe}' "$@"\n`);
    if (process.platform !== 'win32') assert.equal(statSync(join(folder, 'jason')).mode & 0o111, 0o111);
    assert.equal(command.status().state, 'placed');
    command.remove();
    assert.deepEqual(readdirSync(folder), []);

    make('/Volumes/It\'s here').place();
    assert.equal(readFileSync(join(folder, 'jason'), 'utf8'), `#!/bin/sh\n# Written by the Jason desktop application.\nJASON_DATA_DIR='/Volumes/It'\\''s here' exec '${macExe}' "$@"\n`);
    assert.equal(make('/Volumes/It\'s here').status().state, 'placed');
    assert.deepEqual(log.lines.map((line) => line.fields), [{ On: true, Name: 'jason', Platform: 'darwin' }, { On: false, Name: 'jason', Platform: 'darwin' }, { On: true, Name: 'jason', Platform: 'darwin' }]);
});

test('on macOS a jason of another installation on the PATH makes the wrapper jason-desktop', () => {
    const folder = '/Users/someone/.local/bin';
    const brew = '/opt/homebrew/bin/jason';
    const command = new PathCommand({ platform: 'darwin', folder, executable: macExe, dataDirectory: null, path: () => `${folder}:/opt/homebrew/bin`, isProgram: (file) => file === brew, log: memoryLog() });
    const status = command.status();
    assert.equal(status.state, 'absent');
    assert.deepEqual([(status as { name: string }).name, (status as { shadows: string }).shadows, (status as { folderOnPath: boolean }).folderOnPath], ['jason-desktop', brew, true]);
});

test('elsewhere than Windows and macOS the PATH command is not offered', () => {
    const command = new PathCommand({ platform: 'linux', folder: null, executable: '/opt/jason', dataDirectory: null, path: () => '', isProgram: () => false, log: memoryLog() });
    assert.deepEqual(command.status(), { state: 'unsupported', executable: '/opt/jason' });
    assert.throws(() => command.place());
    assert.deepEqual(commandMenuOf(command.status()).acts, []);
});

test('the menu always shows the bundled executable’s full path and offers only the act each state allows', () => {
    const exe = 'C:\\J\\jason.exe';
    const file = 'C:\\W\\jason.cmd';
    const cases = [
        [{ state: 'absent', name: 'jason', files: [file], folderOnPath: true, shadows: null, executable: exe }, ['place']],
        [{ state: 'absent', name: 'jason', files: [file], folderOnPath: false, shadows: null, executable: exe }, []],
        [{ state: 'placed', name: 'jason', files: [file], shadowedBy: null, executable: exe }, ['remove']],
        [{ state: 'elsewhere', name: 'jason', files: [file], why: 'D:\\x.exe', executable: exe }, ['place', 'remove']],
        [{ state: 'taken', files: [file], executable: exe }, []],
        [{ state: 'unsupported', executable: exe }, []],
    ] as const;
    for (const [status, acts] of cases) {
        const menu = commandMenuOf(status);
        assert.ok(menu.lines.some((line) => line.includes(exe)), `${status.state} shows the executable`);
        assert.deepEqual(menu.acts.map((act) => act.act), acts, status.state);
    }
    const shadowing = commandMenuOf({ state: 'absent', name: 'jason-desktop', files: ['C:\\W\\jason-desktop.cmd'], folderOnPath: true, shadows: 'C:\\other\\jason.exe', executable: exe });
    assert.ok(shadowing.lines.some((line) => line.includes('C:\\other\\jason.exe')));
    assert.equal(shadowing.acts[0]?.label, 'Put jason-desktop on the PATH…');
});

test('the PATH command’s log lines carry no path: only the act, the name and the platform', (t) => {
    const root = scratch(t);
    const folder = join(root, 'planted-folder');
    mkdirSync(folder);
    const logs = join(root, 'logs');
    const log = fileLogger(logs);
    const command = new PathCommand({ platform: 'win32', folder, executable: 'C:\\planted-exe\\jason.exe', dataDirectory: 'C:\\planted-data', path: () => folder, isProgram: (file) => existsSync(file), log });
    command.place();
    command.remove();
    const text = readdirSync(logs).map((name) => readFileSync(join(logs, name), 'utf8')).join('');
    assert.equal(text.includes('planted'), false, 'a path reached the log');
    assert.deepEqual(text.split(/\r?\n/).filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName), ['path_command_set', 'path_command_set']);
});
