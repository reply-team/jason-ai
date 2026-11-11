// Remove What Jason Placed, the menu act of macOS and Linux, against stand-ins: a home of the test's own for the
// LaunchAgent, the autostart entry and ~/.local/bin, a data directory of its own for an AppImage's copies of jason, and
// stand-ins for the runtime's stop and jason runtime autostart. No real Mac or Linux machine is touched here; those are
// walked by hand.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { configuration } from '../electron-builder.config.mjs';
import { wrapperScript } from '../src/command-line.ts';
import { autostartEntry, autostartEntryFile, launchAgent, launchAgentFile, LoginItem, type ElectronLogin } from '../src/login-item.ts';
import type { StopOutcome } from '../src/own-runtime.ts';
import { PathCommand } from '../src/path-command.ts';
import { desktopPaths } from '../src/paths.ts';
import { debPackage, placedRemoval, RemovalAct, removalOffered, removalQuestion, removalText, removeApplication, removeLabel, removePlaced, type RemovalScope } from '../src/remove-placed.ts';
import { processesRunFrom, stopFromAny, uninstall, type RunsFrom, type UninstallDeps } from '../src/uninstall.ts';
import { memoryLog } from './harness.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

type Form = 'macos' | 'deb' | 'appimage';
const forms: Form[] = ['macos', 'deb', 'appimage'];
const appImageFile = '/home/someone/Apps/Jason.AppImage';
/** A jason of another installation: no folder of this application's holds it. */
const otherJason = '/usr/local/lib/other/jason';

/** The posix path rules read a path with forward slashes alike on every machine the tests run on. */
const slashes = (path: string) => path.replaceAll('\\', '/');
const resolvePosix = (path: string) => slashes(realpathSync.native(path));

function scratch(t: { after(fn: () => void): void }): string {
    const root = realpathSync.native(mkdtempSync(join(tmpdir(), 'jason-desktop-remove-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    return slashes(root);
}

/** A folder link, which Windows makes without an administrator as a junction and every other system as a symbolic link. */
function link(target: string, at: string): void {
    mkdirSync(dirname(at), { recursive: true });
    symlinkSync(target, at, 'junction');
}

const electron: ElectronLogin = {
    getLoginItemSettings: () => assert.fail('Electron’s login item is Windows’ only here'),
    setLoginItemSettings: () => assert.fail('Electron’s login item is Windows’ only here'),
};

interface Bench {
    form: Form;
    root: string;
    home: string;
    folder: string;
    bundles: string | null;
    /** Where the copies are on the disk: the bundles folder, or behind the link to the data directory. */
    realBundles: string | null;
    executable: string;
    launcher: string;
    item: LoginItem;
    deps: UninstallDeps;
    acts: string[];
    log: ReturnType<typeof memoryLog>;
}

interface Options {
    stop?: () => Promise<StopOutcome>;
    registration?: { registered: boolean; executable: string | null } | null;
    disabled?: boolean | null;
    runsFrom?: RunsFrom | ((folder: string) => RunsFrom);
    /** The data directory is reached through a link, as a home on another disk or a linked ~/.jason is. */
    linkedData?: boolean;
}

/** One installation of the form, with nothing placed yet. */
function bench(t: { after(fn: () => void): void }, form: Form, options: Options = {}): Bench {
    const root = scratch(t);
    const home = `${root}/home`;
    const folder = `${home}/.local/bin`;
    mkdirSync(folder, { recursive: true });
    const platform = form === 'macos' ? 'darwin' : 'linux';
    let application: string;
    let executable: string;
    let launcher: string;
    let bundles: string | null = null;
    let realBundles: string | null = null;
    if (form === 'macos') {
        application = '/Applications/Jason.app';
        executable = `${application}/Contents/Resources/bundle/jason`;
        launcher = `${application}/Contents/MacOS/Jason`;
    } else if (form === 'deb') {
        application = '/opt/Jason';
        executable = `${application}/resources/bundle/jason`;
        launcher = `${application}/jason-desktop-app`;
    } else {
        application = '/tmp/.mount_JasonQ1w2e3';
        let data = `${root}/data`;
        if (options.linkedData) {
            const disk = `${root}/disk/jason-data`;
            mkdirSync(disk, { recursive: true });
            link(disk, `${home}/.jason`);
            data = `${home}/.jason`;
            realBundles = `${disk}/desktop/bundle`;
        }
        bundles = `${data}/desktop/bundle`;
        realBundles ??= bundles;
        executable = `${bundles}/0.35.0/jason`;
        launcher = appImageFile;
        for (const version of ['0.34.0', '0.35.0']) {
            mkdirSync(`${bundles}/${version}`, { recursive: true });
            writeFileSync(`${bundles}/${version}/jason`, 'a made-up jason');
            writeFileSync(`${bundles}/${version}/version.json`, JSON.stringify({ version }));
        }
    }
    const log = memoryLog();
    const acts: string[] = [];
    const item = new LoginItem({ platform, home, executable: launcher, electron, pathRead: Promise.resolve(), log: memoryLog(), env: {} });
    const command = new PathCommand({ platform, folder, executable, dataDirectory: null, path: () => folder, isProgram: () => false, log: memoryLog() });
    const runsFrom = options.runsFrom ?? 'none';
    const deps: UninstallDeps = {
        platform,
        folder,
        command: {
            status: () => command.status(),
            remove: () => {
                acts.push('command remove');
                return command.remove();
            },
        },
        login: {
            isOn: () => item.isOn(),
            set: async (on) => {
                acts.push(`login ${on}`);
                await item.set(on);
            },
            elsewhere: () => item.elsewhere(),
        },
        runtime: {
            stop: async () => {
                const outcome = await (options.stop ? options.stop() : Promise.resolve<StopOutcome>({ kind: 'none' }));
                if (outcome.kind === 'stopped' || outcome.kind === 'failed') acts.push('runtime stop');
                return outcome;
            },
        },
        autostart: {
            status: async () => (options.registration === undefined ? { registered: false, executable: null } : options.registration),
            disable: async () => {
                acts.push('autostart disable');
                return options.disabled === undefined ? false : options.disabled;
            },
        },
        application,
        bundles:
            bundles === null
                ? null
                : {
                      folder: bundles,
                      runsFrom: () => {
                          acts.push('bundle look');
                          return typeof runsFrom === 'function' ? runsFrom(bundles ?? '') : runsFrom;
                      },
                  },
        jason: executable,
        dataDirectory: `${home}/.jason`,
        harnessRoot: `${home}/Jason`,
        updaterCache: form === 'macos' ? `${home}/Library/Caches/jason-desktop-updater` : `${home}/.cache/jason-desktop-updater`,
        log,
        resolve: resolvePosix,
    };
    return { form, root, home, folder, bundles, realBundles, executable, launcher, item, deps, acts, log };
}

/** Places everything the desktop places: Open Jason at Login, a start at logon of its own, and the jason command. */
async function placeAll(b: Bench): Promise<void> {
    await b.item.set(true);
    writeFileSync(join(b.folder, 'jason'), wrapperScript(b.executable));
    ownStart(b);
}

/** A start at logon that starts this application's jason, which the stand-in takes off. */
function ownStart(b: Bench, executable = b.executable): void {
    b.deps.autostart = { status: async () => ({ registered: true, executable }), disable: async () => (b.acts.push('autostart disable'), false) };
}

function loginFile(b: Bench): string {
    return b.form === 'macos' ? launchAgentFile(b.home) : autostartEntryFile(b.home, {});
}

function scope(b: Bench): RemovalScope {
    return {
        command: b.deps.command.status(),
        bundles: b.bundles,
        dataDirectory: b.deps.dataDirectory,
        harnessRoot: b.deps.harnessRoot,
        updaterCache: b.deps.updaterCache,
        next: nextOf(b.form),
    };
}

function nextOf(form: Form): string {
    if (form === 'macos') return removeApplication('darwin', null, '/Applications/Jason.app/Contents/MacOS/Jason');
    return removeApplication('linux', form === 'deb' ? { form: 'deb' } : { form: 'appimage', file: appImageFile }, form === 'deb' ? '/opt/Jason/jason-desktop-app' : '/tmp/.mount_JasonQ1w2e3/jason-desktop-app');
}

test('on macOS and Linux the steps are taken in order: the runtime, Open Jason at Login, the start at logon, the PATH command, and an AppImage’s copies of jason last', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
        await placeAll(b);
        assert.ok(existsSync(loginFile(b)), form);
        const report = await uninstall(b.deps);
        assert.deepEqual(b.acts, ['runtime stop', 'login false', 'autostart disable', 'command remove', ...(form === 'appimage' ? ['bundle look'] : [])], form);
        assert.deepEqual(
            report.steps,
            [
                { step: 'runtime', outcome: 'stopped' },
                { step: 'login', outcome: 'off' },
                { step: 'autostart', outcome: 'off' },
                { step: 'command', outcome: 'removed' },
                ...(form === 'appimage' ? [{ step: 'bundle', outcome: 'removed' }] : []),
            ],
            form,
        );
        assert.equal(existsSync(loginFile(b)), false, `${form}: the login item is off`);
        assert.deepEqual(readdirSync(b.folder), [], `${form}: the wrapper is gone`);
        assert.equal(report.refused, null);
        assert.deepEqual(report.failed, []);
        assert.deepEqual(report.removed, [
            'The runtime this application carries, which was running: it was stopped with jason runtime stop',
            'Open Jason at login',
            'The runtime’s start at logon',
            join(b.folder, 'jason'),
            ...(b.bundles === null ? [] : [`The copy of jason in ${join(b.bundles, '0.34.0')}`, `The copy of jason in ${join(b.bundles, '0.35.0')}`, `The folder that held them, ${b.bundles}`]),
        ]);
        // With only copies of its own there, the folder that held them goes once empty.
        if (b.bundles !== null) assert.equal(existsSync(b.bundles), false, 'the copies, and then their empty folder, are gone');
    }
});

test('a runtime that does not stop, or does not answer, ends the act before anything else is taken off, and the hand act names the carried jason by its full path', async (t) => {
    const stops: { stop: () => Promise<StopOutcome>; acts: string[] }[] = [
        { stop: async () => ({ kind: 'failed', exit: 1 }), acts: ['runtime stop'] },
        { stop: async () => Promise.reject(new Error('planted-stop-error')), acts: [] },
        { stop: async () => ({ kind: 'unanswered', pid: 4242, runFile: '/home/someone/.jason/run/runtime.json' }), acts: [] },
    ];
    for (const form of forms) {
        for (const { stop, acts } of stops) {
            const b = bench(t, form, { stop });
            await placeAll(b);
            const report = await uninstall(b.deps);
            assert.deepEqual(b.acts, acts, `${form}: nothing else was done`);
            assert.equal(b.item.isOn(), true, `${form}: Open Jason at Login stays`);
            assert.deepEqual(readdirSync(b.folder), ['jason'], `${form}: the PATH command stays`);
            if (b.bundles !== null) assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'the copies of jason stay');
            assert.deepEqual([report.removed, report.untouched, report.failed, report.left], [[], [], [], []]);
            assert.equal(report.steps.length, 1);
            assert.equal(report.steps[0]?.step, 'runtime');
            const closing = removalText(report, nextOf(form));
            assert.equal(closing.message, 'Nothing was taken off.');
            assert.deepEqual(closing.buttons, ['OK']);
            assert.equal(closing.quitId, null, 'no Quit is offered');
            assert.match(closing.detail, /^Nothing was removed, because .*(Then|then) choose Remove What Jason Placed again\.$/s);
            assert.ok(closing.detail.includes(`${b.executable} runtime`), `${form}: the hand act names ${b.executable}`);
            assert.doesNotMatch(closing.detail, /(^|[ .,(])jason runtime (stop|status|start)\b(?! (exited|did not))/, 'never a bare jason, which the PATH may not hold');
            assert.doesNotMatch(closing.detail, /uninstall|planted/);
        }
    }
});

test('a start at logon that runs another jason is left and named; on an AppImage one that runs any copy of its jason is its own', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { registration: { registered: true, executable: otherJason } });
        const report = await uninstall(b.deps);
        assert.ok(!b.acts.includes('autostart disable'), `${form}: another jason’s start at logon is not taken off`);
        assert.ok(report.left.includes(`The runtime’s start at logon, which starts ${otherJason}, not this application’s`), form);
        assert.deepEqual(report.steps.find((one) => one.step === 'autostart'), { step: 'autostart', outcome: 'another' });
    }
    // An AppImage's start at logon set up from another version's copy, which the start had not re-pointed, is its own.
    const b = bench(t, 'appimage');
    ownStart(b, `${b.bundles}/0.34.0/jason`);
    assert.ok((await uninstall(b.deps)).removed.includes('The runtime’s start at logon'));
    assert.ok(b.acts.includes('autostart disable'));
    // A start at logon that names a folder beside the copies, sharing their name's beginning, is not one of them.
    const beside = bench(t, 'appimage');
    ownStart(beside, `${beside.bundles}-old/0.34.0/jason`);
    await uninstall(beside.deps);
    assert.ok(!beside.acts.includes('autostart disable'));
});

test('with the data directory reached through a link, the start at logon a copy runs is its own, and copies a process runs from stay', async (t) => {
    const b = bench(t, 'appimage', { linkedData: true });
    assert.ok(b.realBundles !== null && b.bundles !== null && b.realBundles !== b.bundles);
    // What the system and the command line report is resolved: the registration and /proc both name the disk's path.
    ownStart(b, `${b.realBundles}/0.34.0/jason`);
    const table = { list: () => ['1', '4242'], exe: (pid: string) => (pid === '4242' ? `${b.realBundles}/0.34.0/jason` : '/usr/lib/systemd/systemd') };
    b.deps.bundles = { folder: b.bundles, runsFrom: () => processesRunFrom(b.bundles ?? '', table, resolvePosix) };
    const report = await uninstall(b.deps);
    assert.ok(b.acts.includes('autostart disable'), 'the copy’s start at logon is this application’s');
    assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'a copy a runtime runs from is not removed');
    assert.deepEqual(report.steps.find((one) => one.step === 'bundle'), { step: 'bundle', outcome: 'running' });
    // The same folder with nothing running from it goes.
    const c = bench(t, 'appimage', { linkedData: true });
    c.deps.bundles = { folder: c.bundles ?? '', runsFrom: () => processesRunFrom(c.bundles ?? '', { list: () => ['1'], exe: () => '/usr/lib/systemd/systemd' }, resolvePosix) };
    await uninstall(c.deps);
    assert.equal(existsSync(c.bundles ?? ''), false);
    assert.equal(existsSync(c.realBundles ?? ''), false, 'the copies on the disk the link reaches are gone with them');
});

test('only the real folders named as versions are removed from the bundles folder: a link, a file or a folder of another name stays and is named, and so does the folder', async (t) => {
    const b = bench(t, 'appimage');
    const bundles = b.bundles ?? '';
    const elsewhere = `${b.root}/elsewhere/0.33.0`;
    mkdirSync(elsewhere, { recursive: true });
    writeFileSync(`${elsewhere}/jason`, 'not this application’s');
    link(`${b.root}/elsewhere/0.33.0`, `${bundles}/0.33.0`);
    mkdirSync(`${bundles}/notes`);
    writeFileSync(`${bundles}/a-file`, 'a file');
    mkdirSync(`${bundles}/.0.35.0.tmp-7`);
    const report = await uninstall(b.deps);
    assert.deepEqual(readdirSync(bundles).sort(), ['0.33.0', 'a-file', 'notes']);
    assert.ok(existsSync(`${elsewhere}/jason`), 'nothing behind the link is touched');
    assert.deepEqual(report.removed.filter((line) => line.startsWith('The copy')).sort(), [`The copy of jason in ${join(bundles, '.0.35.0.tmp-7')}`, `The copy of jason in ${join(bundles, '0.34.0')}`, `The copy of jason in ${join(bundles, '0.35.0')}`]);
    for (const name of ['0.33.0', 'a-file', 'notes']) assert.ok(report.left.includes(`${join(bundles, name)}, which is not a copy of jason this application made: it was left`), name);
    assert.ok(!report.removed.some((line) => line.startsWith('The folder that held them')));

    // A link at the bundles folder itself is left and named; the copies behind it stay.
    const c = bench(t, 'appimage');
    const real = `${c.root}/disk/bundle`;
    mkdirSync(real, { recursive: true });
    mkdirSync(`${real}/0.34.0`);
    rmSync(c.bundles ?? '', { recursive: true, force: true });
    link(real, c.bundles ?? '');
    const linked = await uninstall(c.deps);
    assert.ok(existsSync(`${real}/0.34.0`));
    assert.ok(linked.left.includes(`${c.bundles}, which is not a folder this application made (a link or a file): it was left`));
    assert.deepEqual(linked.steps.find((one) => one.step === 'bundle'), { step: 'bundle', outcome: 'not_folder' });
    assert.ok(!c.acts.includes('bundle look'), 'nothing was looked for, since nothing would be removed');
});

test('on an AppImage a runtime started from any copy of its jason is this application’s, and is stopped through jason before its start at logon and its copies go', async (t) => {
    const root = scratch(t);
    const paths = desktopPaths(`${root}/data`);
    const bundles = paths.bundle.replaceAll('\\', '/');
    for (const version of ['0.34.0', '0.35.0']) mkdirSync(`${bundles}/${version}`, { recursive: true });
    const current = join(paths.bundle, '0.35.0', 'jason');
    const asked: string[] = [];
    const removal = placedRemoval({
        platform: 'linux',
        home: `${root}/home`,
        env: {},
        form: { form: 'appimage', file: appImageFile },
        execPath: '/tmp/.mount_JasonQ1w2e3/jason-desktop-app',
        executable: current,
        version: '0.35.0',
        paths,
        command: { status: () => ({ state: 'unsupported', executable: current }), remove: () => ({ state: 'unsupported', executable: current }) },
        autostart: { registration: async () => ({ registered: false, executable: null }), set: async () => false },
        makeCommand: () => assert.fail('the start offers a command'),
        makeAutostart: () => assert.fail('the start offers a start at logon'),
        login: { isOn: () => false, set: async () => undefined },
        // The runtime runs from 0.34.0's copy: asked as this start's copy it is another's; asked as 0.34.0's, it is stopped.
        stopAs: async (executable) => (asked.push(executable), executable.includes('0.34.0') ? { kind: 'stopped' } : { kind: 'another' }),
        runsFrom: () => 'none',
        log: memoryLog(),
    });
    const report = await removal.remove();
    assert.deepEqual(asked, [current, join(paths.bundle, '0.34.0', 'jason')], 'this start’s copy first, then each other copy');
    assert.deepEqual(report.steps[0], { step: 'runtime', outcome: 'stopped' });
    assert.ok(report.removed.includes('The runtime this application carries, which was running: it was stopped with jason runtime stop'));
    // The first answer that is not another's is the answer; all another's is another's.
    assert.deepEqual(await stopFromAny(['a', 'b'], async (one) => (one === 'a' ? { kind: 'none' } : assert.fail('asked past an answer'))), { kind: 'none' });
    assert.deepEqual(await stopFromAny(['a', 'b'], async () => ({ kind: 'another' })), { kind: 'another' });
});

test('a runtime left running, or a start at logon left, names the carried jason by its full path, and an AppImage keeps the copies that jason is in', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'another' }) });
        const report = await uninstall(b.deps);
        assert.ok(report.left.includes(`A runtime is running that this application could not confirm as its own: it was not stopped; ${b.executable} runtime stop stops it`), form);
        if (b.bundles !== null) {
            assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'the copies the hand act runs stay');
            assert.ok(report.left.includes(`The copies of jason in ${b.bundles}, kept because a hand act above runs ${b.executable}: once it is done and nothing runs from them, deleting the folders named as versions in it takes them off`));
        }
        const failed = bench(t, form, { registration: { registered: true, executable: null } });
        ownStart(failed);
        failed.deps.autostart = { status: async () => ({ registered: true, executable: failed.executable }), disable: async () => null };
        const after = await uninstall(failed.deps);
        assert.ok(after.failed.includes(`The runtime’s start at logon, which could not be taken off: ${failed.executable} runtime autostart disable takes it off`), form);
    }
    // A path with a space is quoted.
    const spaced = bench(t, 'macos', { stop: async () => ({ kind: 'another' }) });
    spaced.deps.jason = '/Applications/My Apps/Jason.app/Contents/Resources/bundle/jason';
    assert.ok((await uninstall(spaced.deps)).left.some((line) => line.endsWith('; "/Applications/My Apps/Jason.app/Contents/Resources/bundle/jason" runtime stop stops it')));
});

test('a login file under the desktop’s own name that starts another path is named among what is left, never said to be off', async (t) => {
    for (const form of ['macos', 'deb'] as const) {
        const b = bench(t, form);
        const file = loginFile(b);
        mkdirSync(dirname(file), { recursive: true });
        writeFileSync(file, form === 'macos' ? launchAgent('/Users/someone/Downloads/Jason.app/Contents/MacOS/Jason') : autostartEntry('/home/someone/Old/Jason.AppImage'));
        const report = await uninstall(b.deps);
        assert.ok(existsSync(file), `${form}: it is not this application’s to remove`);
        assert.ok(!report.untouched.includes('Open Jason at login was off'), form);
        const starts = form === 'macos' ? '/Users/someone/Downloads/Jason.app/Contents/MacOS/Jason' : '"/home/someone/Old/Jason.AppImage" --opened-at-login';
        assert.ok(report.left.includes(`${file}, under Jason’s name, which starts ${starts}, not this application: deleting it takes it off`), `${form}: ${report.left.join(' | ')}`);
        assert.deepEqual(report.steps.find((one) => one.step === 'login'), { step: 'login', outcome: 'another' });
    }
    // A Linux entry of this application's that is turned off is named as off, with its file.
    const hidden = bench(t, 'deb');
    const entry = loginFile(hidden);
    mkdirSync(dirname(entry), { recursive: true });
    writeFileSync(entry, `${autostartEntry(hidden.launcher)}Hidden=true\n`);
    assert.ok((await uninstall(hidden.deps)).left.includes(`${entry}, under Jason’s name, which is turned off (Hidden=true): deleting it takes it off`));
    // With no file at all, it was off.
    assert.ok((await uninstall(bench(t, 'macos').deps)).untouched.includes('Open Jason at login was off'));
});

test('the PATH command’s own wrapper is removed by the name it was put there with, and a wrapper of the other name another program wrote stays', async (t) => {
    const foreign = '#!/bin/sh\nexec /usr/local/lib/other/jason "$@"\n';
    for (const form of forms) {
        const b = bench(t, form);
        writeFileSync(join(b.folder, 'jason'), foreign);
        writeFileSync(join(b.folder, 'jason-desktop'), wrapperScript(b.executable));
        const report = await uninstall(b.deps);
        assert.deepEqual(readdirSync(b.folder), ['jason'], form);
        assert.equal(readFileSync(join(b.folder, 'jason'), 'utf8'), foreign);
        assert.ok(report.removed.includes(join(b.folder, 'jason-desktop')), form);
        assert.ok(report.left.includes(`${join(b.folder, 'jason')}, which another program wrote`), form);

        const c = bench(t, form);
        writeFileSync(join(c.folder, 'jason'), wrapperScript(c.executable));
        writeFileSync(join(c.folder, 'jason-desktop'), foreign);
        await uninstall(c.deps);
        assert.deepEqual(readdirSync(c.folder), ['jason-desktop'], form);
        assert.equal(readFileSync(join(c.folder, 'jason-desktop'), 'utf8'), foreign);
    }
});

test('Cancel changes nothing: the confirmation’s default and cancel button is Cancel, and only Remove takes a step', async (t) => {
    for (const form of forms) {
        // The default button (Return), and a response that is no button at all, as a dialog closed some other way gives.
        for (const answer of ['default', 'no button'] as const) {
            const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
            await placeAll(b);
            let quit = 0;
            let told = 0;
            const report = await removePlaced({
                scope: () => scope(b),
                ask: async (question) => {
                    assert.equal(question.buttons[question.defaultId], 'Cancel', 'Cancel is the default button');
                    assert.equal(question.buttons[question.cancelId], 'Cancel', 'Escape is Cancel');
                    return answer === 'default' ? question.defaultId : question.buttons.length + 5;
                },
                remove: () => uninstall(b.deps),
                tell: async () => (told += 1),
                quit: () => void (quit += 1),
                log: b.log,
            });
            assert.equal(report, null);
            assert.deepEqual(b.acts, [], `${form}: no step was taken`);
            assert.equal(b.item.isOn(), true);
            assert.deepEqual(readdirSync(b.folder), ['jason']);
            if (b.bundles !== null) assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0']);
            assert.deepEqual([told, quit], [0, 0], 'nothing is said after a Cancel, and nothing quits');
            assert.deepEqual(b.log.lines.map((line) => [line.name, line.fields]), [['removal_asked', { Answer: 'cancel' }]]);
        }
    }
});

test('the confirmation names what will be taken off and what stays, on each platform', async (t) => {
    for (const form of forms) {
        const b = bench(t, form);
        await placeAll(b);
        const question = removalQuestion(scope(b));
        assert.equal(question.message, 'Remove what Jason placed on this computer?');
        assert.deepEqual(question.buttons, ['Remove', 'Cancel']);
        assert.deepEqual([question.defaultId, question.cancelId], [1, 1]);
        const [off, stays, last] = question.detail.split('\n\n');
        assert.match(off ?? '', /^This takes off:\n/);
        for (const named of [
            'The runtime this application carries, if it is running: it is stopped with jason runtime stop, and nothing else is taken off if it does not stop',
            '• Open Jason at Login',
            'The runtime’s start at logon, if it starts this application’s jason',
            `The jason command on the PATH: ${join(b.folder, 'jason')}`,
        ]) {
            assert.ok(off?.includes(named), `${form}: ${named}`);
        }
        assert.equal(off?.includes('The copies of jason in'), form === 'appimage', form);
        if (b.bundles !== null) assert.ok(off?.includes(`The copies of jason in ${b.bundles}, if nothing runs from them`));
        assert.match(stays ?? '', /^This leaves:\n/);
        for (const named of [`The data directory, ${b.home}/.jason`, `The harness root, ${b.home}/Jason`, `The updater’s cache, ${b.deps.updaterCache}`]) {
            assert.ok(stays?.includes(named), `${form}: ${named}`);
        }
        const next = { macos: 'move /Applications/Jason.app to the Trash', deb: `run sudo apt remove ${debPackage}`, appimage: `delete the AppImage file, ${appImageFile}` }[form];
        assert.equal(last, `Jason itself stays until you quit it and ${next}.`);
    }
    // With no command of this application's placed, the item says it is taken off only if it was placed.
    const none = bench(t, 'deb');
    assert.ok(removalQuestion(scope(none)).detail.includes('• The jason command on the PATH, if this application placed it'));
});

test('a value a person or another program wrote is made inert in the dialogs: no line of its own, and no longer than the cap', async (t) => {
    const b = bench(t, 'deb', { stop: async () => ({ kind: 'none' }) });
    const forged = `${b.home}/Jason\n\nThis leaves:\n• nothing`;
    b.deps.harnessRoot = forged;
    const question = removalQuestion({ ...scope(b), harnessRoot: forged, dataDirectory: `/data/${'x'.repeat(500)}` });
    assert.equal(question.detail.split('\n\nThis leaves:\n').length, 2, 'one section of what stays, not a forged second');
    assert.ok(question.detail.includes(`The harness root, ${b.home}/Jason\\n\\nThis leaves:\\n• nothing,`));
    assert.ok(!question.detail.includes('x'.repeat(400)), 'a long value is cut');
    ownStart(b, `/opt/other/jason${String.fromCharCode(0x202e)}nosaj`);
    const closing = removalText(await uninstall(b.deps), nextOf('deb'));
    assert.ok(closing.detail.includes('/opt/other/jason\\u202enosaj'));
    assert.ok(closing.detail.includes(`The harness root, ${b.home}/Jason\\n\\nThis leaves`));
});

test('the closing dialog says each step’s outcome, names a step that failed with its act by hand, says what removes the application, and offers Quit', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'none' }), runsFrom: 'running' });
        writeFileSync(join(b.folder, 'jason'), wrapperScript(b.executable));
        let quit = 0;
        let closing: ReturnType<typeof removalText> | null = null;
        const report = await removePlaced({
            scope: () => scope(b),
            ask: async (question) => question.buttons.indexOf('Remove'),
            remove: () => uninstall(b.deps),
            tell: async (shown) => {
                closing = shown;
                return shown.quitId ?? -1;
            },
            quit: () => void (quit += 1),
            log: b.log,
        });
        assert.ok(report !== null && closing !== null);
        const shown = closing as ReturnType<typeof removalText>;
        assert.equal(shown.message, form === 'appimage' ? 'Jason took off what it could; some of it is left.' : 'Jason took off what it placed.');
        assert.deepEqual(shown.buttons, ['Quit Jason', 'Not Now']);
        assert.equal(shown.quitId, 0);
        assert.equal(quit, 1, `${form}: Quit quits`);
        const sections = shown.detail.split('\n\n');
        assert.deepEqual(sections.map((section) => section.split('\n')[0]), [
            'Taken off:',
            'Nothing to take off:',
            ...(form === 'appimage' ? ['Could not be taken off, and what does it by hand:'] : []),
            'Left in place:',
            sections.at(-1),
        ]);
        assert.ok(sections[0]?.includes(join(b.folder, 'jason')), form);
        assert.ok(sections[1]?.includes(`• No runtime was running with the data directory ${b.home}/.jason`), 'said of the data directory looked at');
        assert.ok(sections[1]?.includes('• Open Jason at login was off'));
        assert.ok(sections[1]?.includes('• The runtime’s start at logon was not set up'));
        if (b.bundles !== null) {
            assert.ok(sections[2]?.includes(`• The copies of jason in ${b.bundles}, which a process still runs from: once nothing runs from them, deleting the folders named as versions in it takes them off`));
            assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'copies a process runs from stay');
        }
        const next = { macos: 'move /Applications/Jason.app to the Trash', deb: 'run sudo apt remove jason-desktop', appimage: `delete the AppImage file, ${appImageFile}` }[form];
        assert.equal(sections.at(-1), `To finish, quit Jason, then ${next}.`);
    }
    // An unreadable process table has its own sentence.
    const unread = bench(t, 'appimage', { runsFrom: 'unread' });
    assert.ok((await uninstall(unread.deps)).failed.includes(`The copies of jason in ${unread.bundles}, since whether a process runs from them could not be read: once nothing runs from them, deleting the folders named as versions in it takes them off`));
    // An AppImage whose copies went says it works on no further until it is quit; Not Now does not quit.
    const gone = bench(t, 'appimage', { stop: async () => ({ kind: 'stopped' }) });
    let quit = 0;
    await removePlaced({
        scope: () => scope(gone),
        ask: async () => 0,
        remove: () => uninstall(gone.deps),
        tell: async (shown) => {
            assert.equal(shown.message, 'Jason took off what it placed.');
            assert.ok(shown.detail.endsWith(`The jason this Jason ran went with its copies, so it does not work on until you quit it. To finish, quit Jason, then delete the AppImage file, ${appImageFile}.`));
            return shown.cancelId;
        },
        quit: () => void (quit += 1),
        log: gone.log,
    });
    assert.equal(quit, 0);
    // An unpacked folder on Linux is removed by deleting the folder; a Jason.app macOS put aside is named without that path.
    assert.equal(removeApplication('linux', { form: 'folder' }, '/home/someone/jason-desktop/jason-desktop-app'), 'delete its folder, /home/someone/jason-desktop');
    assert.equal(removeApplication('darwin', null, '/private/var/folders/xy/abc/T/AppTranslocation/0A1B/d/Jason.app/Contents/MacOS/Jason'), 'move Jason.app to the Trash from the folder you opened it from');
});

test('a build that carries no jason says that neither the runtime nor its start at logon was looked at', async (t) => {
    const b = bench(t, 'deb');
    b.deps.runtime = null;
    b.deps.autostart = null;
    const report = await uninstall(b.deps);
    assert.ok(report.left.includes('The runtime and its start at logon, which were not looked at, since this build carries no jason'));
    assert.ok(!report.untouched.some((line) => line.startsWith('No runtime was running')));
    assert.deepEqual(report.steps.slice(0, 1), [{ step: 'runtime', outcome: 'no_cli' }]);
});

test('every step is logged by its name and outcome, and no path', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
        await placeAll(b);
        ownStart(b, otherJason);
        await removePlaced({ scope: () => scope(b), ask: async () => 0, remove: () => uninstall(b.deps), tell: async () => 1, quit: () => undefined, log: b.log });
        assert.deepEqual(b.log.lines.map((line) => [line.name, line.fields]), [
            ['removal_asked', { Answer: 'remove' }],
            ['removal_step', { Step: 'runtime', Outcome: 'stopped' }],
            ['removal_step', { Step: 'login', Outcome: 'off' }],
            ['removal_step', { Step: 'autostart', Outcome: 'another' }],
            ['removal_step', { Step: 'command', Outcome: 'removed' }],
            ...(form === 'appimage' ? [['removal_step', { Step: 'bundle', Outcome: 'removed' }]] : []),
        ]);
        assert.doesNotMatch(JSON.stringify(b.log.lines), /\//, `${form}: no path is logged`);
    }
});

test('processes are read from /proc by the file each runs, against the folder resolved; a table that cannot be read says so', (t) => {
    const folder = '/home/someone/.jason/desktop/bundle';
    const itself = (path: string) => path;
    const table = (exes: Record<string, string | Error>) => ({
        list: () => Object.keys(exes),
        exe: (pid: string) => {
            const exe = exes[pid];
            if (exe instanceof Error || exe === undefined) throw exe ?? new Error('no such process');
            return exe;
        },
    });
    assert.equal(processesRunFrom(folder, table({ '1': '/usr/lib/systemd/systemd', '77': '/tmp/.mount_JasonQ1w2e3/jason-desktop-app' }), itself), 'none');
    assert.equal(processesRunFrom(folder, table({ '1': new Error('EACCES'), '4242': `${folder}/0.35.0/jason` }), itself), 'running', 'a runtime runs from a copy');
    assert.equal(processesRunFrom(folder, table({ '4242': `${folder}/0.34.0/jason (deleted)` }), itself), 'running', 'a process whose file was deleted still runs from there');
    assert.equal(processesRunFrom(folder, table({ self: `${folder}/0.35.0/jason`, '9': `${folder}-old/jason` }), itself), 'none', 'only numbered entries are processes, and a folder beside it is not it');
    assert.equal(processesRunFrom(folder, { list: () => { throw new Error('no /proc'); }, exe: () => '' }, itself), 'unread', 'a table that cannot be read takes nothing off');
    assert.equal(processesRunFrom(folder, table({}), () => { throw new Error('ENOENT'); }), 'unread', 'a folder that cannot be resolved takes nothing off');
    // The kernel names the file resolved; the folder held through a link is resolved before it is compared.
    const root = scratch(t);
    mkdirSync(`${root}/disk/bundle/0.34.0`, { recursive: true });
    link(`${root}/disk/bundle`, `${root}/home/bundle`);
    assert.equal(processesRunFrom(`${root}/home/bundle`, table({ '4242': `${root}/disk/bundle/0.34.0/jason` }), resolvePosix), 'running');
});

test('the act is offered on macOS and Linux only, in the Jason menu and the tray’s, by an installed build; Windows keeps its uninstaller', () => {
    assert.equal(removalOffered('darwin'), true);
    assert.equal(removalOffered('linux'), true);
    assert.equal(removalOffered('win32'), false);
    assert.equal(removeLabel, 'Remove What Jason Placed…');
    const desk = readFileSync(join(desktop, 'src', 'desk.ts'), 'utf8');
    assert.equal(desk.split('...this.removalItems(),').length - 1, 2, 'the Jason menu and the tray');
    assert.ok(desk.includes('{ label: removeLabel, enabled: app.isPackaged && !this.removing(), click: () => void this.removeAct() }'));
    // While it runs the installation's other acts are off, and after it the menu reads the start at logon again.
    assert.equal(desk.split('!this.removing()').length - 1, 5, 'login, start at logon, the update, the command’s acts and the act itself');
    assert.ok(desk.includes('await this.removal.run();') && desk.includes('await this.readAutostart().catch(() => undefined);'));
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('const removal = removalOffered(process.platform)\n        ? placedRemoval({'));
});

test('the act runs once at a time: a second choice while it runs asks nothing, and the menu is told when it begins and ends', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    let asked = 0;
    let changed = 0;
    const act = new RemovalAct({
        scope: () => ({ command: { state: 'unsupported', executable: '/x/jason' }, bundles: null, dataDirectory: '/d', harnessRoot: '/h', updaterCache: '/c', next: 'move /Applications/Jason.app to the Trash' }),
        ask: async () => {
            asked += 1;
            await held;
            return 1;
        },
        remove: async () => assert.fail('Cancel removes nothing'),
        tell: async () => 0,
        quit: () => undefined,
        log: memoryLog(),
        changed: () => void (changed += 1),
    });
    const first = act.run();
    assert.equal(act.busy, true);
    assert.equal(await act.run(), 'busy');
    release();
    assert.equal(await first, null);
    assert.equal(act.busy, false);
    assert.deepEqual([asked, changed], [1, 2]);
});

test('a start that offers neither the PATH command nor the start at logon still takes off what an earlier start placed, through ones made for the act', async (t) => {
    const root = scratch(t);
    const paths = desktopPaths(`${root}/data`);
    const made: string[] = [];
    const input = {
        platform: 'linux' as const,
        home: `${root}/home`,
        env: {},
        form: { form: 'appimage' as const, file: appImageFile },
        execPath: '/tmp/.mount_JasonQ1w2e3/jason-desktop-app',
        executable: '/tmp/.mount_JasonQ1w2e3/resources/bundle/jason',
        paths,
        command: null,
        autostart: null,
        makeCommand: () => (made.push('command'), { status: () => ({ state: 'unsupported' as const, executable: '' }), remove: () => (made.push('command remove'), { state: 'unsupported' as const, executable: '' }) }),
        makeAutostart: () => (made.push('autostart'), { registration: async () => ({ registered: true, executable: `${paths.bundle}/0.34.0/jason` }), set: async (on: boolean) => (made.push(`autostart ${on}`), false) }),
        login: { isOn: () => false, set: async () => undefined },
        stopAs: async (): Promise<StopOutcome> => ({ kind: 'none' }),
        runsFrom: (): RunsFrom => 'none',
        log: memoryLog(),
    };
    const removal = placedRemoval({ ...input, version: '0.35.0' });
    await removal.remove();
    assert.deepEqual(made, ['command', 'autostart', 'autostart false', 'command remove']);
    // A build that carries no jason makes no start at logon, and looks for no runtime.
    made.length = 0;
    const bare = await placedRemoval({ ...input, version: null }).remove();
    assert.deepEqual(made, ['command', 'command remove']);
    assert.deepEqual(bare.steps.slice(0, 2), [{ step: 'runtime', outcome: 'no_cli' }, { step: 'login', outcome: 'was_off' }]);
});

test('the deb is removed by the name electron-builder gives it', () => {
    const name = JSON.parse(readFileSync(join(desktop, 'package.json'), 'utf8')).name;
    assert.equal(debPackage, name);
    const built = configuration({ version: '0.35.0' }) as { linux?: { packageName?: string }; deb?: { packageName?: string } };
    assert.equal(built.deb?.packageName ?? built.linux?.packageName ?? name, debPackage);
});
