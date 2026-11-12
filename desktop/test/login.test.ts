// The two login items: "Open Jason at login", the desktop's own (a LaunchAgent on macOS, Electron's login item on
// Windows), and "Start the runtime at logon", which is the CLI's `jason runtime autostart` and nothing of the desktop's.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import type { Cli, CliResult } from '../src/cli.ts';
import { verbOf } from '../src/installation.ts';
import { fileLogger } from '../src/log.ts';
import { autostartEntry, autostartEntryFile, autostartExec, bringForward, launchAgent, launchAgentFile, LoginItem, loginFlag, openedAtLogin, RuntimeAutostart, showsOnSecondStart, type ElectronLogin } from '../src/login-item.ts';
import { memoryLog } from './harness.ts';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-login-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return root;
}

/** Electron's login-item calls, recorded, holding one setting as the system would. */
function electronLogin(): ElectronLogin & { asked: unknown[]; set: unknown[] } {
    let on = false;
    const asked: unknown[] = [];
    const set: unknown[] = [];
    return {
        asked,
        set,
        getLoginItemSettings(options) {
            asked.push(options);
            return { openAtLogin: on };
        },
        setLoginItemSettings(settings) {
            set.push(settings);
            on = settings.openAtLogin;
        },
    };
}

const pathRead = Promise.resolve();
const executable = '/Applications/Jason.app/Contents/MacOS/Jason';

test('the LaunchAgent is the one the application is started by at sign-in, in the background, as the person who signed in', () => {
    assert.equal(launchAgentFile('/Users/someone'), join('/Users/someone', 'Library', 'LaunchAgents', 'ai.jasonharness.desktop.plist'));
    const plist = launchAgent('/Applications/J&son <1>.app/Contents/MacOS/Jason');
    assert.match(plist, /<key>Label<\/key><string>ai\.jasonharness\.desktop<\/string>/);
    assert.match(plist, /<key>ProgramArguments<\/key>\s*<array>\s*<string>\/Applications\/J&amp;son &lt;1&gt;\.app\/Contents\/MacOS\/Jason<\/string>\s*<string>--opened-at-login<\/string>\s*<\/array>/);
    assert.match(plist, /<key>RunAtLoad<\/key><true\/>/);
    assert.match(plist, /<key>LimitLoadToSessionType<\/key><string>Aqua<\/string>/);
    assert.equal(loginFlag, '--opened-at-login');
});

test('a start the computer made is told by its flag, and only by it', () => {
    assert.equal(openedAtLogin(['Jason.exe', '--opened-at-login']), true);
    assert.equal(openedAtLogin(['Jason.exe']), false);
    assert.equal(openedAtLogin(['Jason.exe', '--opened-at-login=no']), false);
});

test('starting Jason again while it runs brings the window forward, hidden or minimized; a start the computer made at sign-in does not', () => {
    assert.equal(showsOnSecondStart(['Jason.exe']), true, 'a shortcut, the Start menu or the Dock');
    assert.equal(showsOnSecondStart(['Jason.exe', '--opened-at-login']), false);

    const window = (minimized: boolean) => {
        const did: string[] = [];
        return { did, isMinimized: () => minimized, restore: () => void did.push('restore'), show: () => void did.push('show'), focus: () => void did.push('focus') };
    };
    const hidden = window(false);
    bringForward(hidden);
    assert.deepEqual(hidden.did, ['show', 'focus'], 'a window hidden in the tray is shown, not only focused');
    const minimized = window(true);
    bringForward(minimized);
    assert.deepEqual(minimized.did, ['restore', 'show', 'focus']);
    bringForward(null);
});

test('Open Jason at login is off by default and nothing is registered until the person turns it on', (t) => {
    const home = scratch(t);
    const electron = electronLogin();
    const mac = new LoginItem({ platform: 'darwin', home, executable, electron, pathRead, log: memoryLog() });
    const windows = new LoginItem({ platform: 'win32', home, executable, electron, pathRead, log: memoryLog() });
    assert.equal(mac.isOn(), false);
    assert.equal(windows.isOn(), false);
    assert.deepEqual(electron.set, [], 'nothing is registered by being read');
    assert.deepEqual(readdirSync(home), [], 'nothing is written by being read');
    assert.deepEqual(electron.asked, [{ args: [loginFlag] }], 'Windows is asked about the item that carries the flag');
});

test('on macOS turning it on writes the LaunchAgent under the person’s home and turning it off removes it', async (t) => {
    const home = scratch(t);
    const electron = electronLogin();
    const log = memoryLog();
    const item = new LoginItem({ platform: 'darwin', home, executable, electron, pathRead, log });
    await item.set(true);
    assert.equal(readFileSync(launchAgentFile(home), 'utf8'), launchAgent(executable));
    assert.equal(item.isOn(), true);
    await item.set(false);
    assert.equal(existsSync(launchAgentFile(home)), false);
    assert.equal(item.isOn(), false);
    assert.deepEqual(electron.set, [], 'macOS does not use Electron’s login item');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['login_item_set', { On: true, Platform: 'darwin' }],
        ['login_item_set', { On: false, Platform: 'darwin' }],
    ]);
});

test('on macOS the item is on only when the LaunchAgent starts this application: one that starts another reads as off', (t) => {
    const home = scratch(t);
    const file = launchAgentFile(home);
    mkdirSync(dirname(file), { recursive: true });
    const item = (exe: string) => new LoginItem({ platform: 'darwin', home, executable: exe, electron: electronLogin(), pathRead, log: memoryLog() });
    writeFileSync(file, launchAgent('/Applications/Old Jason.app/Contents/MacOS/Jason'));
    assert.equal(item(executable).isOn(), false, 'a LaunchAgent of another copy of the application');
    writeFileSync(file, launchAgent(executable));
    assert.equal(item(executable).isOn(), true);
    const ampersand = '/Applications/R&D/Jason.app/Contents/MacOS/Jason';
    writeFileSync(file, launchAgent(ampersand));
    assert.equal(item(ampersand).isOn(), true, 'the executable is read as the plist escapes it');
    writeFileSync(file, 'not a plist');
    assert.equal(item(executable).isOn(), false);
});

test('on Windows turning it on sets Electron’s login item with the flag, and writes no file', async (t) => {
    const home = scratch(t);
    const electron = electronLogin();
    const item = new LoginItem({ platform: 'win32', home, executable, electron, pathRead, log: memoryLog() });
    await item.set(true);
    assert.equal(item.isOn(), true);
    await item.set(false);
    assert.deepEqual(electron.set, [{ openAtLogin: true, args: [loginFlag] }, { openAtLogin: false, args: [loginFlag] }]);
    assert.deepEqual(readdirSync(home), []);
});

test('nothing is registered before the login shell’s PATH has been read', async (t) => {
    const home = scratch(t);
    let read: () => void = () => undefined;
    const shellPath = new Promise<void>((resolve) => {
        read = resolve;
    });
    const item = new LoginItem({ platform: 'darwin', home, executable, electron: electronLogin(), pathRead: shellPath, log: memoryLog() });
    const setting = item.set(true);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(existsSync(launchAgentFile(home)), false, 'the agent waited for the PATH');
    read();
    await setting;
    assert.equal(existsSync(launchAgentFile(home)), true);
});

test('on Linux turning it on writes an XDG autostart entry that starts this application with the flag, and off removes it', async (t) => {
    const home = scratch(t);
    const electron = electronLogin();
    const log = memoryLog();
    const appImage = '/home/someone/Apps/Jason.AppImage';
    const item = new LoginItem({ platform: 'linux', home, executable: appImage, electron, pathRead, log, env: {} });
    const file = join(home, '.config', 'autostart', 'ai.jasonharness.desktop.desktop');
    assert.equal(autostartEntryFile(home, {}), file);
    assert.equal(item.supported, true);
    assert.equal(item.isOn(), false);
    await item.set(true);
    assert.equal(readFileSync(file, 'utf8'), `[Desktop Entry]\n# Written by the Jason desktop application.\nType=Application\nName=Jason\nExec="${appImage}" ${loginFlag}\n`);
    assert.equal(item.isOn(), true);
    assert.deepEqual(readdirSync(dirname(file)), ['ai.jasonharness.desktop.desktop'], 'written by rename, no temporary left');
    await item.set(false);
    assert.equal(existsSync(file), false);
    assert.equal(item.isOn(), false);
    assert.deepEqual(electron.set, [], 'Electron’s login item is macOS’s and Windows’ only');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['login_item_set', { On: true, Platform: 'linux' }],
        ['login_item_set', { On: false, Platform: 'linux' }],
    ]);
});

test('on Linux the entry goes under $XDG_CONFIG_HOME when it names an absolute folder, else ~/.config', () => {
    assert.equal(autostartEntryFile('/home/a', { XDG_CONFIG_HOME: '/srv/config' }), join('/srv/config', 'autostart', 'ai.jasonharness.desktop.desktop'));
    assert.equal(autostartEntryFile('/home/a', { XDG_CONFIG_HOME: 'relative' }), join('/home/a', '.config', 'autostart', 'ai.jasonharness.desktop.desktop'));
    assert.equal(autostartEntryFile('/home/a', { XDG_CONFIG_HOME: '' }), join('/home/a', '.config', 'autostart', 'ai.jasonharness.desktop.desktop'));
});

test('on Linux the Exec line quotes the launcher as the Desktop Entry Specification says, and a launcher it cannot spell is not offered', () => {
    assert.equal(autostartExec('/opt/Jason/jason-desktop-app'), `Exec="/opt/Jason/jason-desktop-app" ${loginFlag}`);
    // Inside quotes a double quote, a backtick, a dollar sign and a backslash take a backslash; the string escape then
    // doubles every backslash.
    assert.equal(autostartExec('/home/a/R&D "x" `y` $z \\w/Jason.AppImage'), `Exec="/home/a/R&D \\\\"x\\\\" \\\\\`y\\\\\` \\\\$z \\\\\\\\w/Jason.AppImage" ${loginFlag}`);
    assert.equal(autostartExec('/home/a/100%/Jason.AppImage'), null, 'a per-cent begins a field code');
    assert.equal(autostartExec('/home/a/new\nline/Jason.AppImage'), null);
    assert.equal(new LoginItem({ platform: 'linux', home: '/home/a', executable: '/home/a/100%/Jason.AppImage', electron: electronLogin(), pathRead, log: memoryLog(), env: {} }).supported, false);
});

test('on Linux the item is on only when the entry starts this application and is not hidden', (t) => {
    const home = scratch(t);
    const file = autostartEntryFile(home, {});
    mkdirSync(dirname(file), { recursive: true });
    const item = (exe: string) => new LoginItem({ platform: 'linux', home, executable: exe, electron: electronLogin(), pathRead, log: memoryLog(), env: {} });
    writeFileSync(file, autostartEntry('/home/someone/Old/Jason.AppImage'));
    assert.equal(item('/home/someone/Apps/Jason.AppImage').isOn(), false, 'an entry of another copy of the application');
    writeFileSync(file, autostartEntry('/home/someone/Apps/Jason.AppImage'));
    assert.equal(item('/home/someone/Apps/Jason.AppImage').isOn(), true);
    writeFileSync(file, `${autostartEntry('/home/someone/Apps/Jason.AppImage')}Hidden=true\n`);
    assert.equal(item('/home/someone/Apps/Jason.AppImage').isOn(), false, 'Hidden=true means the session ignores the entry');
    writeFileSync(file, 'not an entry');
    assert.equal(item('/home/someone/Apps/Jason.AppImage').isOn(), false);
});

test('where there is no login item to set, the item says so and sets nothing', async (t) => {
    const home = scratch(t);
    const electron = electronLogin();
    const log = memoryLog();
    const item = new LoginItem({ platform: 'freebsd', home, executable, electron, pathRead, log });
    assert.equal(item.supported, false);
    assert.equal(item.isOn(), false);
    await item.set(true);
    assert.deepEqual(electron.set, []);
    assert.deepEqual(readdirSync(home), []);
    assert.deepEqual(log.lines, []);
});

function autostartCli(registered: () => boolean, exit = 0): Cli & { asked: string[][] } {
    const asked: string[][] = [];
    return {
        asked,
        async run(args): Promise<CliResult> {
            asked.push([...args]);
            if (exit !== 0) return { exit, stdout: '', stderr: 'refused' };
            const verb = args[2];
            const now = verb === 'enable' ? true : verb === 'disable' ? false : registered();
            const answer = { registered: now, platform: 'windows', data_directory: now ? 'C:/planted-data' : null, executable: now ? 'C:/planted-exe/jason.exe' : null, executable_exists: now ? true : null, artifact_path: null };
            return { exit: 0, stdout: `${JSON.stringify(answer)}\n`, stderr: '' };
        },
    };
}

test('Start the runtime at logon is read and set through jason runtime autostart, and the desktop writes nothing for it', async () => {
    const cli = autostartCli(() => false);
    const log = memoryLog();
    const autostart = new RuntimeAutostart({ cli, log });
    assert.equal(await autostart.registered(), false);
    assert.equal(await autostart.set(true), true);
    assert.equal(await autostart.set(false), false);
    assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'enable'], ['runtime', 'autostart', 'disable']]);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [
        ['autostart_set', { On: true, Exit: 0 }],
        ['autostart_set', { On: false, Exit: 0 }],
    ]);
    assert.equal(verbOf(['runtime', 'autostart', 'enable']), 'runtime autostart', 'the log names the verb');
});

test('an autostart the CLI refused or did not answer is unknown, and logged by its exit alone', async () => {
    const log = memoryLog();
    const refusing = autostartCli(() => true, 1);
    const autostart = new RuntimeAutostart({ cli: refusing, log });
    assert.equal(await autostart.registered(), null);
    assert.equal(await autostart.set(true), null);
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['autostart_set', { On: true, Exit: 1 }]]);
    assert.deepEqual(refusing.asked, [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'enable'], ['runtime', 'autostart', 'status']], 'a set that said nothing is followed by a read');
    const garbled: Cli = { run: async () => ({ exit: 0, stdout: 'not json', stderr: '' }) };
    assert.equal(await new RuntimeAutostart({ cli: garbled, log: memoryLog() }).registered(), null);
});

test('a set the CLI answered without saying what is registered is followed by a read of what is', async () => {
    const asked: string[][] = [];
    const cli: Cli = {
        async run(args) {
            asked.push([...args]);
            if (args[2] === 'status') return { exit: 0, stdout: '{"registered":true}\n', stderr: '' };
            return { exit: 0, stdout: 'done\n', stderr: '' };
        },
    };
    assert.equal(await new RuntimeAutostart({ cli, log: memoryLog() }).set(true), true);
    assert.deepEqual(asked, [['runtime', 'autostart', 'enable'], ['runtime', 'autostart', 'status']]);
});

test('the login items’ log lines, read back, carry the setting and the platform, never a path the CLI printed', async (t) => {
    const root = scratch(t);
    const logs = join(root, 'logs');
    const log = fileLogger(logs);
    await new LoginItem({ platform: 'darwin', home: join(root, 'home'), executable: '/planted-exe/Jason', electron: electronLogin(), pathRead, log }).set(true);
    const autostart = new RuntimeAutostart({ cli: autostartCli(() => false), log });
    await autostart.set(true);
    const text = readdirSync(logs).map((name) => readFileSync(join(logs, name), 'utf8')).join('');
    assert.equal(text.includes('planted'), false, 'a path reached the log');
    assert.deepEqual(text.split(/\r?\n/).filter(Boolean).map((line) => (JSON.parse(line) as { EventName: string }).EventName), ['login_item_set', 'autostart_set']);
});

/** A CLI whose `runtime autostart status` names `executable` when registered, and whose enable registers its own jason. */
function registeredCli(registered: boolean, executable: string, own: string, options: { dataDirectory?: string; enableExit?: number } = {}): Cli & { asked: string[][] } {
    const asked: string[][] = [];
    const data = options.dataDirectory ?? '/data';
    let now = { registered, executable: registered ? executable : null, data_directory: registered ? data : null };
    return {
        asked,
        async run(args): Promise<CliResult> {
            asked.push([...args]);
            if (args[2] === 'enable') {
                if (options.enableExit) return { exit: options.enableExit, stdout: '', stderr: 'refused' };
                now = { registered: true, executable: own, data_directory: '/data' };
            }
            return { exit: 0, stdout: `${JSON.stringify({ ...now, platform: 'linux', executable_exists: true, artifact_path: null })}\n`, stderr: '' };
        },
    };
}

/** Another version's copy under the bundles folder, as main.ts asks it of what a registration names. */
const otherCopy = (named: string) => named.startsWith('/data/desktop/bundle/') && named !== '/data/desktop/bundle/0.36.0/jason';

test('on an AppImage a start at logon registered with another version’s copy of jason is registered again with this start’s copy', async () => {
    const cli = registeredCli(true, '/data/desktop/bundle/0.35.0/jason', '/data/desktop/bundle/0.36.0/jason');
    const log = memoryLog();
    assert.equal(await new RuntimeAutostart({ cli, log }).repoint(otherCopy, '/data'), true);
    assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'enable']], 'the same act as enable, from this start’s jason');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['autostart_repointed', { Exit: 0 }]]);
});

test('a start at logon that names a jason outside the bundles folder, or this start’s copy, is left as it is', async () => {
    for (const named of ['/opt/Jason/resources/bundle/jason', '/home/someone/.local/share/jason/jason', '/data/desktop/bundle/0.36.0/jason']) {
        const cli = registeredCli(true, named, '/data/desktop/bundle/0.36.0/jason');
        const log = memoryLog();
        assert.equal(await new RuntimeAutostart({ cli, log }).repoint(otherCopy, '/data'), false, named);
        assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status']], named);
        assert.deepEqual(log.lines, []);
    }
});

test('a start at logon the person never enabled is not registered by a re-point, nor one the CLI did not answer', async () => {
    const cli = registeredCli(false, '/data/desktop/bundle/0.35.0/jason', '/data/desktop/bundle/0.36.0/jason');
    assert.equal(await new RuntimeAutostart({ cli, log: memoryLog() }).repoint(() => true, '/data'), false);
    assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status']], 'nothing is written');
    const namingAsked: string[][] = [];
    const naming: Cli & { asked: string[][] } = {
        asked: namingAsked,
        async run(args) {
            namingAsked.push([...args]);
            return { exit: 0, stdout: `${JSON.stringify({ registered: false, executable: '/data/desktop/bundle/0.35.0/jason', data_directory: '/data' })}\n`, stderr: '' };
        },
    };
    assert.equal(await new RuntimeAutostart({ cli: naming, log: memoryLog() }).repoint(() => true, '/data'), false, 'an answer that says not registered is never re-pointed, whatever it names');
    assert.deepEqual(naming.asked, [['runtime', 'autostart', 'status']]);
    const refusing = autostartCli(() => true, 1);
    assert.equal(await new RuntimeAutostart({ cli: refusing, log: memoryLog() }).repoint(() => true, '/data'), false);
    assert.deepEqual(refusing.asked, [['runtime', 'autostart', 'status']]);
});

test('a re-point whose enable the CLI refuses is logged as failed with its exit, never as registered again', async () => {
    const cli = registeredCli(true, '/data/desktop/bundle/0.35.0/jason', '/data/desktop/bundle/0.36.0/jason', { enableExit: 1 });
    const log = memoryLog();
    assert.equal(await new RuntimeAutostart({ cli, log }).repoint(otherCopy, '/data'), false);
    assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status'], ['runtime', 'autostart', 'enable']]);
    assert.deepEqual(log.lines.map((line) => [line.level, line.name, line.fields]), [['error', 'repoint_failed', { What: 'autostart', Exit: 1 }]]);
});

test('a start at logon registered for another data directory is not re-pointed to this desktop’s', async () => {
    const cli = registeredCli(true, '/data/desktop/bundle/0.35.0/jason', '/data/desktop/bundle/0.36.0/jason', { dataDirectory: '/srv/other-jason' });
    const log = memoryLog();
    assert.equal(await new RuntimeAutostart({ cli, log }).repoint(otherCopy, '/data'), false);
    assert.deepEqual(cli.asked, [['runtime', 'autostart', 'status']]);
    assert.deepEqual(log.lines, []);
});

test('on Linux an entry GNOME is told to skip, X-GNOME-Autostart-enabled=false, reads as off', (t) => {
    const home = scratch(t);
    const file = autostartEntryFile(home, {});
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, `${autostartEntry('/home/someone/Apps/Jason.AppImage')}X-GNOME-Autostart-enabled=false\n`);
    const item = new LoginItem({ platform: 'linux', home, executable: '/home/someone/Apps/Jason.AppImage', electron: electronLogin(), pathRead, log: memoryLog(), env: {} });
    assert.equal(item.isOn(), false);
});

test('on Linux an update that installs the AppImage under a new file name moves an entry that was on to that file, and writes none that was off', (t) => {
    const home = scratch(t);
    const file = autostartEntryFile(home, {});
    const old = '/home/someone/Apps/Jason-0.35.0.AppImage';
    const renamed = '/home/someone/Apps/Jason-0.36.0.AppImage';
    const log = memoryLog();
    const item = new LoginItem({ platform: 'linux', home, executable: old, electron: electronLogin(), pathRead, log, env: {} });

    assert.equal(item.relocate(renamed), false, 'an entry that is off stays off');
    assert.equal(existsSync(file), false);

    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, autostartEntry(old));
    assert.equal(item.relocate(renamed), true);
    assert.equal(readFileSync(file, 'utf8'), autostartEntry(renamed));
    assert.deepEqual(readdirSync(dirname(file)), ['ai.jasonharness.desktop.desktop'], 'written by rename, no temporary left');
    assert.deepEqual(log.lines.map((line) => [line.name, line.fields]), [['login_item_moved', { Platform: 'linux' }]]);
});
