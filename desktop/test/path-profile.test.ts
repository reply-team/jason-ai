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
import { addedWords, desktopFiles, heldBy, takeLines, unsafeFolder, exportLine, installerFiles, interactiveProfile, loginProfile, pathLines, profileMarker, withoutLines } from '../src/path-profile.ts';
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
function desk(t: { after(fn: () => void): void }, options: { shell?: string | null; terminal?: (folder: string) => string | null; desktop?: (folder: string) => string; at?: string } = {}) {
    const at = options.at ?? home(t);
    const folder = join(at, '.local', 'bin');
    const log = memoryLog();
    const terminal = options.terminal ?? (() => '/usr/bin:/bin');
    const desktop = options.desktop ?? (() => '/usr/bin:/bin');
    const shellNamed = options.shell === undefined ? '/bin/zsh' : options.shell;
    const make = (executable = exe) =>
        new PathCommand({ platform: 'linux', folder, executable, dataDirectory: null, path: () => desktop(folder), isProgram: (file) => existsSync(file), account: { home: at, shellNamed, terminalPath: () => terminal(folder) }, log });
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

test('already on the PATH is the login shell’s own PATH: on it, nothing; only on the desktop’s, written; unknown, written', (t) => {
    // On the shell's own PATH, as the desktop read it before adding its usual places: nothing is written.
    const own = desk(t, { terminal: (folder) => `/usr/bin:${folder}`, desktop: (folder) => `/usr/bin:${folder}` });
    assert.equal(own.command.place().said, null);
    assert.deepEqual(['.zprofile', '.zshrc'].filter((name) => existsSync(join(own.home, name))), []);

    // Only on the desktop's own PATH, where the usual places put it: a new terminal does not have it, so it is written.
    const augmented = desk(t, { terminal: () => '/usr/bin:/bin', desktop: (folder) => `/usr/bin:/bin:${folder}` });
    const status = augmented.command.status();
    assert.equal(status.state === 'absent' && status.folderOnPath, true, 'the desktop’s PATH holds it');
    assert.deepEqual(status.state === 'absent' ? status.lines?.add : null, [join(augmented.home, '.zprofile'), join(augmented.home, '.zshrc')]);
    assert.equal(augmented.command.place().said?.added, true);
    assert.equal(text(join(augmented.home, '.zprofile')), block(augmented.line));

    // The shell gave no PATH: the folder is taken as not on it, and written; a second act still adds nothing.
    const unknown = desk(t, { terminal: () => null, desktop: (folder) => `/usr/bin:/bin:${folder}` });
    assert.equal(unknown.command.place().said?.added, true);
    assert.equal(text(join(unknown.home, '.zshrc')), block(unknown.line));
    assert.equal(unknown.make().place().said, null);
    assert.equal(text(join(unknown.home, '.zshrc')), block(unknown.line));

    // And the start hands the account the shell's own reading, with no stand-in where it gave none.
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('const terminalPath = await shellRead;'));
    assert.ok(main.includes('terminalPath: () => terminalPath }'));
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
    assert.equal(
        commandQuestion('remove', placed).detail,
        `This removes ${join(folder, 'jason')}.\n\nA removal leaves the line that puts ${folder} on the PATH in ${join(home, '.zprofile')} and ${join(home, '.zshrc')}: the folder still holds another jason or jason-desktop, found through it.`,
    );
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

test('the lines come out only where the folder then holds nothing but what Jason writes there, as jason uninstall rules', (t) => {
    // Only Jason's files: the installer's two notices beside our wrapper. The removal takes the lines back.
    const ours = desk(t);
    ours.command.place();
    for (const name of ['jason-LICENSE', 'jason-THIRD-PARTY-NOTICES']) writeFileSync(join(ours.folder, name), 'notice\n');
    const before = ours.command.status();
    assert.deepEqual(before.state === 'placed' ? before.lines?.take : null, [join(ours.home, '.zprofile'), join(ours.home, '.zshrc')]);
    assert.equal(commandMenuOf(before).lines.some((line) => line.startsWith('A removal leaves')), false);
    ours.command.remove();
    assert.equal(text(join(ours.home, '.zprofile')), '');
    assert.equal(text(join(ours.home, '.zshrc')), '');

    // Another program in the folder, found through the line: the line stays, and the item and the confirmation say why.
    const shared = desk(t);
    shared.command.place();
    writeFileSync(join(shared.folder, 'claude'), '#!/bin/sh\n');
    const status = shared.command.status();
    assert.deepEqual(status.state === 'placed' ? [status.lines?.take, status.lines?.kept, status.lines?.held] : null, [[], [join(shared.home, '.zprofile'), join(shared.home, '.zshrc')], 'others']);
    const words = `A removal leaves the line that puts ${shared.folder} on the PATH in ${join(shared.home, '.zprofile')} and ${join(shared.home, '.zshrc')}: the folder still holds other programs, found through it.`;
    assert.ok(commandMenuOf(status).lines.includes(words), commandMenuOf(status).lines.join('\n'));
    assert.ok(commandQuestion('remove', status).detail.endsWith(`\n\n${words}`));
    shared.command.remove();
    assert.equal(existsSync(join(shared.folder, 'jason')), false, 'the wrapper is still removed');
    assert.equal(text(join(shared.home, '.zprofile')), block(shared.line), 'the line stays');
    assert.equal(text(join(shared.home, '.zshrc')), block(shared.line));
    // Once the other program is gone, the desktop's own removal takes them back.
    rmSync(join(shared.folder, 'claude'));
    shared.make().remove();
    assert.equal(text(join(shared.home, '.zprofile')), '');
});

test('the files Jason writes in the folder are the C# uninstall’s list, and the desktop’s two names', () => {
    // PathEntry.UnixInstallerFiles is the executable and ReleaseAssets.Notices under ReleaseAssets.NoticeFileName.
    const dotnet = join(import.meta.dirname, '..', '..', 'dotnet', 'src');
    const entry = readFileSync(join(dotnet, 'Jason.Cli', 'Uninstall', 'PathEntry.cs'), 'utf8');
    const assets = readFileSync(join(dotnet, 'Jason.Contracts', 'Update', 'ReleaseAssets.cs'), 'utf8');
    assert.ok(entry.includes('UnixInstallerFiles { get; } =\n        ["jason", .. ReleaseAssets.Notices.Select(ReleaseAssets.NoticeFileName)];'), 'the C# list is the executable and the notices');
    const notices = /public static IReadOnlyList<string> Notices \{ get; \} = \[([^\]]*)\];/.exec(assets)?.[1];
    assert.ok(notices !== undefined);
    const prefix = /public static string NoticeFileName\(string notice\) => "([^"]*)" \+ notice;/.exec(assets)?.[1];
    assert.ok(prefix !== undefined);
    const names = ['jason', ...[...notices.matchAll(/"([^"]+)"/g)].map((match) => `${prefix}${match[1]}`)];
    assert.deepEqual([...installerFiles], names);
    assert.deepEqual([...desktopFiles], ['jason', 'jason-desktop']);
});

test('a removal leaves every other byte of the profile as it was, a last line without its newline among them', (t) => {
    const { home, folder, command, line } = desk(t);
    const before = { '.zprofile': '# mine, no newline at the end', '.zshrc': 'alias a=b\r\n\n\n# kept\t ' };
    for (const [name, body] of Object.entries(before)) writeFileSync(join(home, name), body);
    command.place();
    assert.equal(text(join(home, '.zprofile')), `# mine, no newline at the end${block(line)}`);
    command.remove();
    for (const [name, body] of Object.entries(before)) assert.equal(text(join(home, name)), body, name);
    // Two blocks, one in the middle, both come out; a marker over another folder's line stays.
    const other = exportLine('/elsewhere/bin');
    const middle = `a${block(line)}b\n${block(other)}${block(line)}`;
    assert.equal(withoutLines(middle, folder), `ab\n${block(other)}`);
});

test('a folder holding a character the shell reads in double quotes gets no line: the line to add by hand quotes it', (t) => {
    // `$` and a backtick on every system; `"` and `\\` too where the file system takes them in a name.
    const odd = process.platform === 'win32' ? 'a$b`c' : 'a$b`c"d\\e\'f';
    const at = join(home(t), odd);
    mkdirSync(at);
    const { folder, command } = desk(t, { at });
    const status = command.status();
    const lines = status.state === 'absent' ? status.lines : undefined;
    assert.deepEqual(lines?.refused, [`${folder} holds a character that line cannot carry: a double quote, a dollar sign, a backtick, a backslash or a line break`]);
    assert.ok(lines?.byHand.startsWith("export PATH='") && lines.byHand.endsWith("':\"$PATH\""), lines?.byHand);
    assert.ok(commandMenuOf(status).lines.some((each) => each.includes(lines?.byHand ?? '?') && each.includes('by hand')));
    assert.ok(commandQuestion('place', status).detail.endsWith(`\n${lines?.byHand}`));
    const placed = command.place();
    assert.equal(placed.status.state, 'placed', 'the wrapper is still placed');
    assert.equal(placed.said?.added, false);
    assert.ok(placed.said?.words.endsWith(`by hand: ${lines?.byHand}`));
    assert.deepEqual(['.profile', '.zprofile', '.zshrc'].filter((name) => existsSync(join(at, name))), [], 'nothing is written');
    // The line given by hand puts the folder on the PATH, its characters as they are, and runs nothing.
    if (process.platform !== 'win32') {
        const run = spawnSync('sh', ['-c', `${lines?.byHand ?? ''}; printf '%s' "$PATH"`], { env: { PATH: '/usr/bin:/bin' }, encoding: 'utf8' });
        assert.equal(run.stdout, `${folder}:/usr/bin:/bin`, run.stderr);
    }
});

test('the temporary file beside a profile is made exclusively: a link standing at its name is never written through', (t) => {
    const { home, command, line } = desk(t);
    command.place();
    const profile = join(home, '.zprofile');
    const target = join(home, 'not-a-profile');
    writeFileSync(target, 'kept\n');
    try {
        symlinkSync(target, `${profile}.jason-desktop.tmp`, 'file');
    } catch {
        t.diagnostic('this account cannot make a link to a file here');
        return;
    }
    assert.throws(() => command.remove(), (error: Error) => error.message.includes(profile) && error.message.includes('by hand'));
    assert.equal(readFileSync(target, 'utf8'), 'kept\n', 'nothing written through the link');
    assert.equal(lstatSync(`${profile}.jason-desktop.tmp`).isSymbolicLink(), true, 'the link is left where it stood');
    assert.equal(text(profile), block(line), 'the profile is as it was');
    assert.equal(text(join(home, '.zshrc')), '', 'the other profile is still rewritten');
});

test('a hidden entry or a temporary file of the desktop’s in the folder keeps no line in place', (t) => {
    const { home, folder, command } = desk(t);
    command.place();
    for (const name of ['.DS_Store', '.cache', 'jason.jason-desktop.tmp']) writeFileSync(join(folder, name), '');
    assert.equal(heldBy(folder, ['jason']), null);
    const status = command.status();
    assert.deepEqual(status.state === 'placed' ? status.lines?.take : null, [join(home, '.zprofile'), join(home, '.zshrc')]);
    command.remove();
    assert.equal(text(join(home, '.zprofile')), '');
    assert.equal(heldBy(folder, []), null, 'and a visible program still keeps them');
    writeFileSync(join(folder, 'uv'), '');
    assert.equal(heldBy(folder, []), 'others');
});

test('a line break in the folder’s name is refused like a quote: a block the search could never find again is never written', () => {
    for (const odd of ['/h/a\nb/.local/bin', '/h/a\rb/.local/bin', '/h/a"b', '/h/a$b', '/h/a`b']) assert.equal(unsafeFolder(odd), true, JSON.stringify(odd));
    assert.equal(unsafeFolder('/home/someone/.local/bin'), false);
    assert.equal(unsafeFolder("/home/o'brien/.local/bin"), false, 'a single quote is carried by the double quotes');
});

test('a rewrite that fails after the temporary file was made removes it, and leaves the profile as it was', (t) => {
    const { home, folder, command, line } = desk(t);
    command.place();
    const status = command.status();
    const lines = status.state === 'placed' ? status.lines : undefined;
    assert.ok(lines !== undefined);
    const profile = join(home, '.zprofile');
    // A full disk: part of the text is written, then the write throws.
    const failed = takeLines(lines, (descriptor, data) => {
        writeFileSync(descriptor, data.subarray(0, 3));
        throw Object.assign(new Error('no space left on device'), { code: 'ENOSPC' });
    });
    assert.deepEqual(failed, [profile, join(home, '.zshrc')]);
    for (const file of [profile, join(home, '.zshrc')]) {
        assert.equal(existsSync(`${file}.jason-desktop.tmp`), false, 'nothing this act made is left');
        assert.equal(text(file), block(line), 'the profile is as it was');
    }
    assert.equal(existsSync(join(folder, 'jason')), true);
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
    const lines = (interactive: boolean) => ({ ...pathLines({ home: '/h', shellNamed: null, terminalPath: () => '' }, '/h/.local/bin', 'jason'), interactive });
    const fill = (words: string) => words.replace('$INSTALL_DIR', '/h/.local/bin').replace('$ADDED', '/h/.zprofile and /h/.zshrc').replace('$LINE', exportLine('/h/.local/bin'));
    assert.equal(addedWords(lines(true), ['/h/.zprofile', '/h/.zshrc']), fill(said('open a new terminal')));
    assert.equal(addedWords(lines(false), ['/h/.zprofile', '/h/.zshrc']), fill(said('sign out and back in')));
    // The removal is the uninstall's: the blank line, the marker and the line come out, and a line nobody marked stays.
    assert.equal(withoutLines(`# mine\n${block(exportLine('/h'))}`, '/h'), '# mine\n');
    assert.equal(withoutLines(`# mine${block(exportLine('/h'))}`, '/h'), '# mine', 'a profile that ended without a newline gets none');
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
            // The desktop's CI job installs zsh, the Mac's own shell: there, a machine without it is a failure.
            if (process.env['CI']) assert.fail(`${shell} is not on this machine, and CI installs it`);
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
