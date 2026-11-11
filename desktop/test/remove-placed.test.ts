// Remove What Jason Placed, the menu act of macOS and Linux, against stand-ins: a home of the test's own for the
// LaunchAgent, the autostart entry and ~/.local/bin, a data directory of its own for an AppImage's copies of jason, and
// stand-ins for the runtime's stop and jason runtime autostart. No real Mac or Linux machine is touched here; those are
// walked by hand.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { configuration } from '../electron-builder.config.mjs';
import { wrapperScript } from '../src/command-line.ts';
import { autostartEntryFile, launchAgentFile, LoginItem, type ElectronLogin } from '../src/login-item.ts';
import type { StopOutcome } from '../src/own-runtime.ts';
import { PathCommand } from '../src/path-command.ts';
import { debPackage, removalOffered, removalQuestion, removalText, removeApplication, removeLabel, removePlaced, type RemovalScope } from '../src/remove-placed.ts';
import { processesRunFrom, uninstall, type UninstallDeps } from '../src/uninstall.ts';
import { memoryLog } from './harness.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

type Form = 'macos' | 'deb' | 'appimage';
const forms: Form[] = ['macos', 'deb', 'appimage'];
const appImageFile = '/home/someone/Apps/Jason.AppImage';

function scratch(t: { after(fn: () => void): void }): string {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-remove-'));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    // The posix path rules read a path with forward slashes alike on every machine the tests run on.
    return root.replaceAll('\\', '/');
}

const electron: ElectronLogin = {
    getLoginItemSettings: () => assert.fail('Electron’s login item is Windows’ only here'),
    setLoginItemSettings: () => assert.fail('Electron’s login item is Windows’ only here'),
};

interface Bench {
    form: Form;
    home: string;
    folder: string;
    bundles: string | null;
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
    runsFrom?: boolean;
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
        bundles = `${root}/data/desktop/bundle`;
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
                          return options.runsFrom ?? false;
                      },
                  },
        dataDirectory: `${home}/.jason`,
        harnessRoot: `${home}/Jason`,
        updaterCache: form === 'macos' ? `${home}/Library/Caches/jason-desktop-updater` : `${home}/.cache/jason-desktop-updater`,
        log,
    };
    return { form, home, folder, bundles, executable, launcher, item, deps, acts, log };
}

/** Places everything the desktop places: Open Jason at Login, a start at logon of its own, and the jason command. */
async function placeAll(b: Bench): Promise<void> {
    await b.item.set(true);
    writeFileSync(join(b.folder, 'jason'), wrapperScript(b.executable));
}

function loginFile(b: Bench): string {
    return b.form === 'macos' ? launchAgentFile(b.home) : autostartEntryFile(b.home, {});
}

test('on macOS and Linux the steps are taken in order: the runtime, Open Jason at Login, the start at logon, the PATH command, and an AppImage’s copies of jason last', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
        b.deps.autostart = { status: async () => ({ registered: true, executable: b.executable }), disable: async () => (b.acts.push('autostart disable'), false) };
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
        if (b.bundles !== null) assert.equal(existsSync(b.bundles), false, 'the copies of jason are gone');
        assert.equal(report.refused, null);
        assert.deepEqual(report.failed, []);
        assert.deepEqual(report.removed, [
            'The runtime this application carries, which was running: it was stopped with jason runtime stop',
            'Open Jason at login',
            'The runtime’s start at logon',
            join(b.folder, 'jason'),
            ...(b.bundles === null ? [] : [`The copies of jason in ${b.bundles}`]),
        ]);
    }
});

test('a runtime that does not stop, or does not answer, ends the act before anything else is taken off', async (t) => {
    const stops: { stop: () => Promise<StopOutcome>; acts: string[] }[] = [
        { stop: async () => ({ kind: 'failed', exit: 1 }), acts: ['runtime stop'] },
        { stop: async () => Promise.reject(new Error('planted-stop-error')), acts: [] },
        { stop: async () => ({ kind: 'unanswered', pid: 4242, runFile: '/home/someone/.jason/run/runtime.json' }), acts: [] },
    ];
    for (const form of forms) {
        for (const { stop, acts } of stops) {
            const b = bench(t, form, { stop });
            b.deps.autostart = { status: async () => ({ registered: true, executable: b.executable }), disable: async () => (b.acts.push('autostart disable'), false) };
            await placeAll(b);
            const report = await uninstall(b.deps);
            assert.deepEqual(b.acts, acts, `${form}: nothing else was done`);
            assert.equal(b.item.isOn(), true, `${form}: Open Jason at Login stays`);
            assert.deepEqual(readdirSync(b.folder), ['jason'], `${form}: the PATH command stays`);
            if (b.bundles !== null) assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'the copies of jason stay');
            assert.deepEqual([report.removed, report.untouched, report.failed, report.left], [[], [], [], []]);
            assert.equal(report.steps.length, 1);
            assert.equal(report.steps[0]?.step, 'runtime');
            const closing = removalText(report, 'move /Applications/Jason.app to the Trash');
            assert.equal(closing.message, 'Nothing was taken off.');
            assert.deepEqual(closing.buttons, ['OK']);
            assert.equal(closing.quitId, null, 'no Quit is offered');
            assert.match(closing.detail, /^Nothing was removed, because .*(Then|then) choose Remove What Jason Placed again\.$/s);
            assert.doesNotMatch(closing.detail, /uninstall|planted/);
        }
    }
});

test('a start at logon that runs another jason is left and named; on an AppImage one that runs any copy of its jason is its own', async (t) => {
    const others: Record<Form, string> = {
        macos: '/Applications/Other.app/Contents/Resources/bundle/jason',
        deb: '/usr/local/bin/jason',
        appimage: '/home/someone/.jason/bin/jason',
    };
    for (const form of forms) {
        const b = bench(t, form, { registration: { registered: true, executable: others[form] } });
        const report = await uninstall(b.deps);
        assert.ok(!b.acts.includes('autostart disable'), `${form}: another jason’s start at logon is not taken off`);
        assert.ok(report.left.includes(`The runtime’s start at logon, which starts ${others[form]}, not this application’s`), form);
        assert.deepEqual(report.steps.find((one) => one.step === 'autostart'), { step: 'autostart', outcome: 'another' });
    }
    // An AppImage's start at logon set up from another version's copy, which the start had not re-pointed, is its own.
    const b = bench(t, 'appimage');
    b.deps.autostart = { status: async () => ({ registered: true, executable: `${b.bundles}/0.34.0/jason` }), disable: async () => (b.acts.push('autostart disable'), false) };
    assert.ok((await uninstall(b.deps)).removed.includes('The runtime’s start at logon'));
    assert.ok(b.acts.includes('autostart disable'));
    // A start at logon that names a folder beside the copies, sharing their name's beginning, is not one of them.
    const beside = bench(t, 'appimage');
    beside.deps.autostart = { status: async () => ({ registered: true, executable: `${beside.bundles}-old/0.34.0/jason` }), disable: async () => (beside.acts.push('autostart disable'), false) };
    await uninstall(beside.deps);
    assert.ok(!beside.acts.includes('autostart disable'));
});

test('the PATH command’s own wrapper is removed by the name it was put there with, and a wrapper of the other name another program wrote stays', async (t) => {
    const foreign = '#!/bin/sh\nexec /usr/local/lib/other/jason "$@"\n';
    for (const form of forms) {
        // Placed as jason-desktop beside another program's jason.
        const b = bench(t, form);
        writeFileSync(join(b.folder, 'jason'), foreign);
        writeFileSync(join(b.folder, 'jason-desktop'), wrapperScript(b.executable));
        const report = await uninstall(b.deps);
        assert.deepEqual(readdirSync(b.folder), ['jason'], form);
        assert.equal(readFileSync(join(b.folder, 'jason'), 'utf8'), foreign);
        assert.ok(report.removed.includes(join(b.folder, 'jason-desktop')), form);
        assert.ok(report.left.includes(`${join(b.folder, 'jason')}, which another program wrote`), form);

        // Placed as jason, with another program's jason-desktop beside it.
        const c = bench(t, form);
        writeFileSync(join(c.folder, 'jason'), wrapperScript(c.executable));
        writeFileSync(join(c.folder, 'jason-desktop'), foreign);
        await uninstall(c.deps);
        assert.deepEqual(readdirSync(c.folder), ['jason-desktop'], form);
        assert.equal(readFileSync(join(c.folder, 'jason-desktop'), 'utf8'), foreign);
    }
});

test('Cancel changes nothing: the confirmation’s default button is Cancel, and only Remove takes a step', async (t) => {
    for (const form of forms) {
        for (const answer of ['default', 'escape'] as const) {
            const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
            b.deps.autostart = { status: async () => ({ registered: true, executable: b.executable }), disable: async () => (b.acts.push('autostart disable'), false) };
            await placeAll(b);
            let quit = 0;
            let told = 0;
            const asked: number[] = [];
            const report = await removePlaced({
                scope: () => scope(b),
                ask: async (question) => {
                    asked.push(question.defaultId);
                    assert.equal(question.buttons[question.defaultId], 'Cancel', 'Cancel is the default button');
                    return answer === 'default' ? question.defaultId : question.cancelId;
                },
                remove: () => uninstall(b.deps),
                tell: async () => (told += 1),
                quit: () => void (quit += 1),
                log: b.log,
            });
            assert.equal(report, null);
            assert.deepEqual(asked.length, 1);
            assert.deepEqual(b.acts, [], `${form}: no step was taken`);
            assert.equal(b.item.isOn(), true);
            assert.deepEqual(readdirSync(b.folder), ['jason']);
            if (b.bundles !== null) assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0']);
            assert.deepEqual([told, quit], [0, 0], 'nothing is said after a Cancel, and nothing quits');
            assert.deepEqual(b.log.lines.map((line) => [line.name, line.fields]), [['removal_asked', { Answer: 'cancel' }]]);
        }
    }
});

function scope(b: Bench): RemovalScope {
    return {
        command: b.deps.command.status(),
        bundles: b.bundles,
        dataDirectory: b.deps.dataDirectory,
        harnessRoot: b.deps.harnessRoot,
        updaterCache: b.deps.updaterCache,
        next: removeApplication(b.form === 'macos' ? 'darwin' : 'linux', b.form === 'macos' ? null : b.form === 'deb' ? { form: 'deb' } : { form: 'appimage', file: appImageFile }, b.launcher),
    };
}

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

test('the closing dialog says each step’s outcome, names a step that failed with its act by hand, says what removes the application, and offers Quit', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'none' }), runsFrom: true });
        b.deps.autostart = { status: async () => ({ registered: true, executable: b.executable }), disable: async () => null };
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
        assert.equal(shown.message, 'Jason took off what it could; some of it is left.');
        assert.deepEqual(shown.buttons, ['Quit Jason', 'Not Now']);
        assert.equal(shown.quitId, 0);
        assert.equal(quit, 1, `${form}: Quit quits`);
        const sections = shown.detail.split('\n\n');
        assert.deepEqual(sections.map((section) => section.split('\n')[0]), [
            'Taken off:',
            'Nothing to take off:',
            'Could not be taken off, and what does it by hand:',
            'Left in place:',
            sections.at(-1),
        ]);
        assert.ok(sections[0]?.includes(join(b.folder, 'jason')), form);
        assert.ok(sections[1]?.includes('• No runtime of this application’s was running'));
        assert.ok(sections[1]?.includes('• Open Jason at login was off'));
        assert.ok(sections[2]?.includes('• The runtime’s start at logon, which could not be taken off: jason runtime autostart disable takes it off'));
        if (b.bundles !== null) {
            assert.ok(sections[2]?.includes(`• The copies of jason in ${b.bundles}, which a process still runs from: once no Jason runtime or jason command runs, deleting the folder takes them off`));
            assert.deepEqual(readdirSync(b.bundles).sort(), ['0.34.0', '0.35.0'], 'copies a process runs from stay');
        }
        assert.ok(sections[3]?.includes(`• The data directory, ${b.home}/.jason`));
        const next = { macos: 'move /Applications/Jason.app to the Trash', deb: 'run sudo apt remove jason-desktop', appimage: `delete the AppImage file, ${appImageFile}` }[form];
        assert.equal(sections.at(-1), `To finish, quit Jason, then ${next}.`);
    }
    // Everything taken off: said plainly, and Not Now does not quit.
    const b = bench(t, 'macos', { stop: async () => ({ kind: 'stopped' }) });
    await placeAll(b);
    let quit = 0;
    await removePlaced({ scope: () => scope(b), ask: async () => 0, remove: () => uninstall(b.deps), tell: async (shown) => (assert.equal(shown.message, 'Jason took off what it placed.'), shown.cancelId), quit: () => void (quit += 1), log: b.log });
    assert.equal(quit, 0);
    // An unpacked folder on Linux is removed by deleting the folder.
    assert.equal(removeApplication('linux', { form: 'folder' }, '/home/someone/jason-desktop/jason-desktop-app'), 'delete its folder, /home/someone/jason-desktop');
});

test('every step is logged by its name and outcome, and no path', async (t) => {
    for (const form of forms) {
        const b = bench(t, form, { stop: async () => ({ kind: 'stopped' }) });
        b.deps.autostart = { status: async () => ({ registered: true, executable: '/usr/local/bin/jason' }), disable: async () => false };
        await placeAll(b);
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

test('an AppImage’s copies of jason are left while a process runs from them, and processes are read from /proc by the file each runs', () => {
    const folder = '/home/someone/.jason/desktop/bundle';
    const table = (exes: Record<string, string | Error>) => ({
        list: () => Object.keys(exes),
        exe: (pid: string) => {
            const exe = exes[pid];
            if (exe instanceof Error || exe === undefined) throw exe ?? new Error('no such process');
            return exe;
        },
    });
    assert.equal(processesRunFrom(folder, table({ '1': '/usr/lib/systemd/systemd', '77': '/tmp/.mount_JasonQ1w2e3/jason-desktop-app' })), false);
    assert.equal(processesRunFrom(folder, table({ '1': new Error('EACCES'), '4242': `${folder}/0.35.0/jason` })), true, 'a runtime runs from a copy');
    assert.equal(processesRunFrom(folder, table({ '4242': `${folder}/0.34.0/jason (deleted)` })), true, 'a process whose file was deleted still runs from there');
    assert.equal(processesRunFrom(folder, table({ self: `${folder}/0.35.0/jason`, '9': `${folder}-old/jason` })), false, 'only numbered entries are processes, and a folder beside it is not it');
    assert.equal(processesRunFrom(folder, { list: () => { throw new Error('no /proc'); }, exe: () => '' }), true, 'a table that cannot be read takes nothing off');
});

test('the act is offered on macOS and Linux only, in the Jason menu and the tray’s, by an installed build; Windows keeps its uninstaller', () => {
    assert.equal(removalOffered('darwin'), true);
    assert.equal(removalOffered('linux'), true);
    assert.equal(removalOffered('win32'), false);
    assert.equal(removeLabel, 'Remove What Jason Placed…');
    const desk = readFileSync(join(desktop, 'src', 'desk.ts'), 'utf8');
    assert.equal(desk.split('...this.removalItems(),').length - 1, 2, 'the Jason menu and the tray');
    assert.ok(desk.includes('{ label: removeLabel, enabled: app.isPackaged && !this.removing, click: () => void this.removeAct() }'));
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('removalOffered(process.platform)'));
    assert.ok(main.includes('remove: () =>\n                  uninstall({'), 'the menu runs the uninstall’s own steps');
});

test('the deb is removed by the name electron-builder gives it', () => {
    const name = JSON.parse(readFileSync(join(desktop, 'package.json'), 'utf8')).name;
    assert.equal(debPackage, name);
    const built = configuration({ version: '0.35.0' }) as { linux?: { packageName?: string }; deb?: { packageName?: string } };
    assert.equal(built.deb?.packageName ?? built.linux?.packageName ?? name, debPackage);
});
