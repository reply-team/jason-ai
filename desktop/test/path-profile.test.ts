// The PATH lines of macOS and Linux: placing the PATH command where a new terminal's PATH does not hold its folder also
// puts the folder there, by install.sh's own rule, and a removal takes back what it wrote. Every row works in a home of
// its own under the system's temporary folder; none reads or writes the person's.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { commandMenuOf, commandQuestion, PathCommand } from '../src/path-command.ts';
import { addedWords, exportLine, interactiveProfile, loginProfile, pathLines, profileMarker, withoutLines } from '../src/path-profile.ts';
import { memoryLog } from './harness.ts';

const script = readFileSync(join(import.meta.dirname, '..', '..', 'install', 'install.sh'), 'utf8');
const exe = '/opt/Jason/resources/bundle/jason';

/**
 * A home of the row's own. On Windows its path loses the drive, so it holds no colon, as a macOS or Linux path holds
 * none: the PATH is split on colons there, and the file system reads `\Users\...` on the drive the tests run from.
 */
function home(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-profile-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const at = join(process.platform === 'win32' ? root.replace(/^[A-Za-z]:/, '') : root, 'home');
    mkdirSync(at);
    return at;
}

/** A Linux desk over that home: the account's shell, and the PATH a new terminal of it has. */
function desk(t: { after(fn: () => void): void }, options: { shell?: string | null; terminal?: (folder: string) => string; at?: string } = {}) {
    const at = options.at ?? home(t);
    const folder = join(at, '.local', 'bin');
    const log = memoryLog();
    const terminal = options.terminal ?? (() => '/usr/bin:/bin');
    const shellNamed = options.shell === undefined ? '/bin/zsh' : options.shell;
    const make = (executable = exe) =>
        new PathCommand({ platform: 'linux', folder, executable, dataDirectory: null, path: () => '/usr/bin:/bin', isProgram: (file) => existsSync(file), account: { home: at, shellNamed, terminalPath: () => terminal(folder) }, log });
    return { home: at, folder, command: make(), make, log, line: exportLine(folder) };
}

const block = (line: string) => `\n${profileMarker}\n${line}\n`;
const text = (file: string) => (existsSync(file) ? readFileSync(file, 'utf8') : null);

test('each shell gets the files install.sh writes: its login profile, and its interactive file for zsh and bash', (t) => {
    const cases: { shell: string | null; files?: string[]; login: string; interactive: string | null }[] = [
        { shell: '/bin/zsh', login: '.zprofile', interactive: '.zshrc' },
        { shell: '/bin/bash', files: ['.bash_profile', '.bash_login'], login: '.bash_profile', interactive: '.bashrc' },
        { shell: '/usr/bin/bash', files: ['.bash_login'], login: '.bash_login', interactive: '.bashrc' },
        { shell: 'bash', login: '.profile', interactive: '.bashrc' },
        { shell: '/usr/bin/fish', login: '.profile', interactive: null },
        { shell: null, login: '.profile', interactive: null },
    ];
    for (const each of cases) {
        const { home, folder, command, line } = desk(t, { shell: each.shell });
        for (const name of each.files ?? []) writeFileSync(join(home, name), '# mine\n');
        const written = [join(home, each.login), ...(each.interactive === null ? [] : [join(home, each.interactive)])];
        const status = command.status();
        assert.equal(status.state, 'absent');
        assert.deepEqual(status.state === 'absent' ? status.lines?.add : null, written, `${each.shell}: the files the act would write`);

        const placed = command.place();
        assert.equal(placed.status.state, 'placed', 'the wrapper is placed');
        assert.equal(readFileSync(join(folder, 'jason'), 'utf8').includes(exe), true);
        assert.deepEqual(placed.said, {
            added: true,
            words: each.interactive === null
                ? `Added ${folder} to the PATH in ${written.join(' and ')}; sign out and back in for a new terminal to find it, or run: ${line}`
                : `Added ${folder} to the PATH in ${written.join(' and ')}; open a new terminal, or run: ${line}`,
        });
        for (const file of written) assert.equal(text(file), `${(each.files ?? []).some((name) => join(home, name) === file) ? '# mine\n' : ''}${block(line)}`, `${each.shell}: ${file}`);
        // No other profile is touched: bash with ~/.bash_profile never reads ~/.profile, and nothing goes there.
        for (const name of ['.profile', '.bash_profile', '.bash_login', '.zprofile', '.bashrc', '.zshrc']) {
            const file = join(home, name);
            if (!written.includes(file)) assert.equal(text(file), (each.files ?? []).includes(name) ? '# mine\n' : null, `${each.shell}: ${name} untouched`);
        }
    }
});

test('a folder a new terminal already has on its PATH gets no line, and the item says nothing about one', (t) => {
    const { home, folder, command } = desk(t, { terminal: (folder) => `/usr/bin:${folder}/:/bin` });
    const status = command.status();
    assert.deepEqual(status.state === 'absent' ? status.lines?.add : null, []);
    assert.deepEqual(command.place().said, null);
    assert.equal(existsSync(join(folder, 'jason')), true);
    assert.deepEqual(['.profile', '.zprofile', '.zshrc'].filter((name) => existsSync(join(home, name))), [], 'no profile is written');
    assert.equal(commandMenuOf(command.status()).lines.some((line) => line.includes('terminal')), false);
});

test('a second act adds nothing, and a profile that already holds the whole line, marked or not, is left as it is', (t) => {
    const { home, command, make, line } = desk(t);
    command.place();
    const once = [text(join(home, '.zprofile')), text(join(home, '.zshrc'))];
    // The lines are there now, though this desktop's terminal PATH was read before they were.
    const again = make().status();
    assert.deepEqual(again.state === 'placed' ? again.lines?.add : null, []);
    assert.deepEqual(make().place().said, null);
    assert.deepEqual([text(join(home, '.zprofile')), text(join(home, '.zshrc'))], once, 'no second block');

    const other = desk(t);
    writeFileSync(join(other.home, '.zshrc'), `# mine\n${other.line}\n`);
    other.command.place();
    assert.equal(text(join(other.home, '.zshrc')), `# mine\n${other.line}\n`, 'the person’s own line is the line');
    assert.equal(text(join(other.home, '.zprofile')), block(other.line));
    assert.equal(line.length > 0, true);
});

test('the item and its confirmation say what the act will write: the wrapper, the profiles and the two lines', (t) => {
    const { home, folder, command, line } = desk(t);
    const status = command.status();
    const menu = commandMenuOf(status);
    assert.equal(menu.lines.some((each) => each.includes('a terminal finds')), false, 'the old sentence is gone');
    assert.ok(menu.lines.includes(`${folder} is not on a new terminal’s PATH yet; putting jason on the PATH adds it in ${join(home, '.zprofile')} and ${join(home, '.zshrc')}.`), menu.lines.join('\n'));
    assert.deepEqual(menu.acts.map((act) => act.label), ['Put jason on the PATH…']);
    assert.deepEqual(commandQuestion('place', status), {
        message: 'Put the jason command on the PATH?',
        detail: `This writes ${join(folder, 'jason')}, which run ${exe}.\n\nTo put ${folder} on the PATH, it adds these two lines to ${join(home, '.zprofile')} and ${join(home, '.zshrc')}:\n${profileMarker}\n${line}`,
        button: 'Place It',
    });
    command.place();
    assert.deepEqual(commandQuestion('remove', command.status()), {
        message: 'Remove the jason command from the PATH?',
        detail: `This removes ${join(folder, 'jason')}.\n\nIt also takes these two lines back out of ${join(home, '.zprofile')} and ${join(home, '.zshrc')}:\n${profileMarker}\n${line}`,
        button: 'Remove It',
    });
});

test('a removal takes back the marker and the line it wrote, every other byte as it was, and never a line nobody marked', (t) => {
    const { home, folder, command, line } = desk(t);
    writeFileSync(join(home, '.zshrc'), '# mine\r\nalias x=y\n');
    writeFileSync(join(home, '.profile'), `${line}\n`);
    command.place();
    assert.equal(text(join(home, '.zshrc')), `# mine\r\nalias x=y\n${block(line)}`);
    command.remove();
    assert.equal(existsSync(join(folder, 'jason')), false);
    assert.equal(text(join(home, '.zshrc')), '# mine\r\nalias x=y\n');
    assert.equal(text(join(home, '.zprofile')), '', 'the file the act made is left, empty');
    assert.equal(text(join(home, '.profile')), `${line}\n`, 'a line no marker heads is the person’s own');
    const status = command.status();
    assert.deepEqual(status.state === 'absent' ? status.lines?.take : null, []);
});

test('the lines stay while the folder holds another jason or jason-desktop, and come out with the last of them', (t) => {
    const { home, folder, command, make, line } = desk(t);
    command.place();
    // Another installation's jason-desktop beside ours: removing ours leaves the folder serving it.
    writeFileSync(join(folder, 'jason-desktop'), '#!/bin/sh\nexec /usr/lib/other/jason "$@"\n');
    const placed = command.status();
    assert.deepEqual(placed.state === 'placed' ? placed.lines?.take : null, []);
    assert.equal(commandQuestion('remove', placed).detail, `This removes ${join(folder, 'jason')}.`);
    command.remove();
    assert.equal(existsSync(join(folder, 'jason')), false);
    assert.equal(text(join(home, '.zprofile')), block(line), 'the line stays');
    rmSync(join(folder, 'jason-desktop'));

    // Another installation's jason: the desktop's own command is then jason-desktop, and its removal keeps the lines.
    writeFileSync(join(folder, 'jason'), '#!/bin/sh\nexec /usr/lib/other/jason "$@"\n');
    const named = make();
    assert.equal(named.place().status.state, 'placed');
    assert.equal(existsSync(join(folder, 'jason-desktop')), true);
    named.remove();
    assert.equal(existsSync(join(folder, 'jason-desktop')), false);
    assert.equal(text(join(home, '.zprofile')), block(line));

    // The last jason gone, the desktop's own removal takes them back even with no command of its own placed.
    rmSync(join(folder, 'jason'));
    assert.deepEqual(make().marked(), [join(home, '.zprofile'), join(home, '.zshrc')]);
    make().remove();
    assert.deepEqual(make().marked(), []);
    assert.equal(text(join(home, '.zprofile')), '');
});

test('a profile that is a link, a folder or cannot be written takes nothing, nor does any other; the wrapper is still placed and the line is given', (t) => {
    const refusals: [string, (file: string, home: string) => boolean, RegExp][] = [
        ['a link', (file, home) => {
            writeFileSync(join(home, 'dotfiles-zshrc'), '# kept elsewhere\n');
            try {
                symlinkSync(join(home, 'dotfiles-zshrc'), file, 'file');
                return true;
            } catch {
                return false;
            }
        }, /\.zshrc is a link/],
        ['a folder', (file) => {
            mkdirSync(file);
            return true;
        }, /\.zshrc is a folder/],
        ['read-only', (file) => {
            writeFileSync(file, '# mine\n');
            chmodSync(file, 0o444);
            // An account that may write whatever its mode says (root) cannot stand for this row.
            try {
                writeFileSync(file, '# mine\n', { flag: 'a' });
                return false;
            } catch {
                return true;
            }
        }, /\.zshrc cannot be written/],
    ];
    for (const [kind, plant, why] of refusals) {
        const { home, folder, command, line } = desk(t);
        const file = join(home, '.zshrc');
        if (!plant(file, home)) {
            t.diagnostic(`${kind}: this account cannot make one here; the row stands on the others`);
            continue;
        }
        const before = lstatSync(file).isFile() ? readFileSync(file, 'utf8') : null;
        const status = command.status();
        const menu = commandMenuOf(status);
        assert.ok(menu.lines.some((each) => why.test(each) && each.includes(line) && each.includes('by hand')), `${kind}: ${menu.lines.join('\n')}`);
        assert.match(commandQuestion('place', status).detail, why);
        const placed = command.place();
        assert.equal(placed.status.state, 'placed', `${kind}: the wrapper is placed`);
        assert.equal(placed.said?.added, false);
        assert.match(placed.said?.words ?? '', why);
        assert.ok(placed.said?.words.endsWith(`by hand: ${line}`));
        assert.equal(existsSync(join(home, '.zprofile')), false, `${kind}: nothing is half-written`);
        if (before !== null) assert.equal(readFileSync(file, 'utf8'), before);
        if (kind === 'a link') assert.equal(readFileSync(join(home, 'dotfiles-zshrc'), 'utf8'), '# kept elsewhere\n', 'nothing is written through a link');
        assert.equal(existsSync(join(folder, 'jason')), true);
        if (kind === 'read-only') chmodSync(file, 0o644);
    }
});

test('a command an earlier version placed without the lines is offered them, and the act writes the lines alone', (t) => {
    const { home, folder, command, make, line } = desk(t);
    // Placed as 0.48 placed it: the wrapper, no line.
    new PathCommand({ platform: 'linux', folder, executable: exe, dataDirectory: null, path: () => '/usr/bin:/bin', isProgram: (file) => existsSync(file), log: memoryLog() }).place();
    const wrapper = readFileSync(join(folder, 'jason'), 'utf8');
    const status = command.status();
    assert.equal(status.state, 'placed');
    const menu = commandMenuOf(status);
    assert.deepEqual(menu.acts.map((act) => act.act), ['place', 'remove']);
    assert.equal(commandQuestion('place', status).detail.startsWith('To put '), true, 'it writes no wrapper, so it names none');
    const placed = make().place();
    assert.equal(placed.said?.added, true);
    assert.equal(readFileSync(join(folder, 'jason'), 'utf8'), wrapper);
    assert.equal(text(join(home, '.zprofile')), block(line));
    assert.deepEqual(commandMenuOf(make().status()).acts.map((act) => act.act), ['remove']);
});

test('the lines are a look on Windows no more than before: no account, no line, the same statuses', (t) => {
    const at = home(t);
    const folder = join(at, 'WindowsApps');
    mkdirSync(folder);
    const command = new PathCommand({ platform: 'win32', folder, executable: 'C:\\J\\jason.exe', dataDirectory: null, path: () => folder, isProgram: (file) => existsSync(file), account: { home: at, shellNamed: '/bin/zsh', terminalPath: () => '' }, log: memoryLog() });
    assert.equal('lines' in command.status(), false);
    assert.equal(command.place().said, null);
    assert.deepEqual(command.marked(), []);
    assert.equal(existsSync(join(at, '.zprofile')), false);
});

test('the TypeScript copy is held to install.sh: the marker, the line, the append, the search and the words after it', () => {
    assert.ok(script.includes(`MARKER="${profileMarker}"`), 'the marker is the script’s');
    assert.ok(script.includes('LINE="export PATH=\\"$INSTALL_DIR:\\$PATH\\""'), 'the line is the script’s');
    assert.equal(exportLine('/h/.local/bin'), 'export PATH="/h/.local/bin:$PATH"');
    assert.ok(script.includes(`printf '\\n%s\\n%s\\n' "$MARKER" "$LINE" >> "$1"`), 'appended as the script appends');
    assert.ok(script.includes('grep -qxF "$LINE" "$1"'), 'the whole line is looked for');
    const said = (tail: string) => {
        const line = script.split('\n').find((each) => each.includes('echo "Added $INSTALL_DIR to the PATH in $ADDED; ') && each.includes(tail));
        assert.ok(line !== undefined, tail);
        return line.trim().replace(/^echo "(.*)"$/, '$1');
    };
    const lines = (interactive: boolean) => ({ ...pathLines({ home: '/h', shellNamed: null, terminalPath: () => '' }, '/h/.local/bin', true), interactive });
    const fill = (words: string) => words.replace('$INSTALL_DIR', '/h/.local/bin').replace('$ADDED', '/h/.zprofile and /h/.zshrc').replace('$LINE', exportLine('/h/.local/bin'));
    assert.equal(addedWords(lines(true), ['/h/.zprofile', '/h/.zshrc']), fill(said('open a new terminal')));
    assert.equal(addedWords(lines(false), ['/h/.zprofile', '/h/.zshrc']), fill(said('sign out and back in')));
    // The removal is the uninstall's: the blank line, the marker and the line come out, and a line nobody marked stays.
    assert.equal(withoutLines(`# mine\n${block(exportLine('/h'))}`, '/h'), '# mine\n');
    assert.equal(withoutLines(`# mine${block(exportLine('/h'))}`, '/h'), '# mine\n');
    assert.equal(withoutLines(`a\n${exportLine('/h')}\n`, '/h'), `a\n${exportLine('/h')}\n`);
});

/** The script's own function, by its name, as the script defines it. */
function scriptFunction(name: string): string {
    const start = script.indexOf(`\n${name}() {\n`);
    assert.ok(start >= 0, name);
    const end = script.indexOf('\n}\n', start);
    return script.slice(start + 1, end + 2);
}

test('the TypeScript rule answers each case as the script’s own functions do, run in sh', { skip: process.platform === 'win32' && 'the script runs on macOS and Linux' }, (t) => {
    const functions = `${scriptFunction('login_profile')}\n${scriptFunction('interactive_profile')}\nlogin_profile\ninteractive_profile\n`;
    const cases: { shell: string | undefined; files?: string[]; dirs?: string[]; dangling?: string[] }[] = [
        { shell: '/bin/zsh' },
        { shell: 'zsh' },
        { shell: '/bin/bash', files: ['.bash_profile', '.bash_login', '.profile'] },
        { shell: '/bin/bash', files: ['.bash_login', '.profile'] },
        { shell: '/bin/bash', files: ['.profile'] },
        { shell: '/bin/bash' },
        { shell: '/bin/bash', dirs: ['.bash_profile'] },
        { shell: '/bin/bash', dangling: ['.bash_profile'] },
        { shell: '/bin/bash/' },
        { shell: '/usr/local/bin/fish' },
        { shell: '' },
        { shell: undefined },
    ];
    for (const each of cases) {
        const at = home(t);
        for (const name of each.files ?? []) writeFileSync(join(at, name), '');
        for (const name of each.dirs ?? []) mkdirSync(join(at, name));
        for (const name of each.dangling ?? []) symlinkSync(join(at, 'gone'), join(at, name));
        const env: Record<string, string> = { HOME: at, PATH: '/usr/bin:/bin' };
        if (each.shell !== undefined) env['SHELL'] = each.shell;
        const run = spawnSync('sh', ['-eu', '-c', functions], { env, encoding: 'utf8' });
        assert.equal(run.status, 0, run.stderr);
        const named = each.shell === undefined || each.shell === '' ? null : each.shell;
        const interactive = interactiveProfile(at, named);
        assert.deepEqual(run.stdout.split('\n').filter(Boolean), [loginProfile(at, named), ...(interactive === null ? [] : [interactive])], JSON.stringify(each));
    }
});

test('after the act a real login shell of a scratch home finds jason: bash, and zsh where the machine has it', { skip: process.platform === 'win32' && 'a login shell of macOS and Linux' }, (t) => {
    for (const shell of ['bash', 'zsh']) {
        const found = spawnSync('sh', ['-c', `command -v ${shell}`], { encoding: 'utf8' }).stdout.trim();
        if (found === '') {
            t.diagnostic(`${shell} is not on this machine`);
            continue;
        }
        const { home, folder, command } = desk(t, { shell: found });
        command.place();
        const env = { HOME: home, ZDOTDIR: home, SHELL: found, PATH: '/usr/bin:/bin', TERM: 'dumb' };
        const login = spawnSync(found, ['-l', '-c', 'command -v jason'], { env, encoding: 'utf8' });
        assert.equal(login.stdout.trim(), join(folder, 'jason'), `${shell} -l: ${login.stderr}`);
        // A terminal a desktop opens is no login shell, and reads the interactive file alone.
        const interactive = spawnSync(found, ['-i', '-c', 'command -v jason'], { env, encoding: 'utf8' });
        assert.equal(interactive.stdout.trim(), join(folder, 'jason'), `${shell} -i: ${interactive.stderr}`);
        // And once removed, neither finds it.
        command.remove();
        assert.equal(spawnSync(found, ['-l', '-c', 'command -v jason'], { env, encoding: 'utf8' }).stdout.trim(), '');
    }
});
