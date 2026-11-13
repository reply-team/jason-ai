import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyMode, defaultMode, demoDirectory, demoMenuLine, demoUnopened, modeUnkept, openDirectory, parseMode, readMode, relaunchOptions, runsOn, startOn, writeMode, type Mode } from '../src/mode.ts';
import { desktopPaths } from '../src/paths.ts';

const folder = () => mkdtempSync(join(tmpdir(), 'mode-'));

test('the default is Live', () => {
    assert.equal(defaultMode, 'live');
});

test('the mode file is the desktop’s own, beside its settings', () => {
    const data = join(folder(), 'data');
    assert.equal(desktopPaths(data).mode, join(data, 'desktop', 'mode'));
});

test('a file holding live or demo reads as that mode, a trailing newline or spaces aside', () => {
    assert.equal(parseMode('live'), 'live');
    assert.equal(parseMode('demo'), 'demo');
    assert.equal(parseMode('demo\n'), 'demo');
    assert.equal(parseMode('  live\r\n'), 'live');
});

test('a file that holds anything else reads as the default', () => {
    for (const garbage of ['', 'Demo', 'DEMO', 'demo live', '{"mode":"demo"}', 'sample', '\u0000demo', 'true']) {
        assert.equal(parseMode(garbage), defaultMode, JSON.stringify(garbage));
    }
});

test('a missing file reads as the default, and so does one that cannot be read', () => {
    const at = folder();
    assert.equal(readMode(join(at, 'desktop', 'mode')), defaultMode);
    // A folder where the file should be cannot be read as text.
    const blocked = join(at, 'blocked');
    mkdirSync(blocked);
    assert.equal(readMode(blocked), defaultMode);
});

test('each mode written is read back, the file holding the one word and nothing beside it left over', () => {
    const file = join(folder(), 'desktop', 'mode');
    for (const mode of ['demo', 'live'] as Mode[]) {
        writeMode(file, mode);
        assert.equal(readMode(file), mode);
        assert.equal(readFileSync(file, 'utf8'), `${mode}\n`);
    }
    assert.deepEqual(readdirSync(join(file, '..')), ['mode']);
});

test('a garbage file written over by a mode reads as that mode', () => {
    const file = join(folder(), 'mode');
    writeFileSync(file, 'nonsense');
    assert.equal(readMode(file), defaultMode);
    writeMode(file, 'demo');
    assert.equal(readMode(file), 'demo');
});

function bench(failWrite = false) {
    const steps: string[] = [];
    return {
        steps,
        deps: {
            write: (mode: Mode) => {
                if (failWrite) throw new Error('EACCES');
                steps.push(`write ${mode}`);
            },
            relaunch: () => void steps.push('relaunch'),
        },
    };
}

test('setting a mode writes the record first and then starts the desktop again, and answers null', () => {
    for (const mode of ['demo', 'live'] as Mode[]) {
        const b = bench();
        assert.equal(applyMode(mode, b.deps), null);
        assert.deepEqual(b.steps, [`write ${mode}`, 'relaunch']);
    }
});

test('a write that fails starts nothing again and says so as a refusal', () => {
    const b = bench(true);
    assert.deepEqual(applyMode('demo', b.deps), { refused: modeUnkept });
    assert.deepEqual(b.steps, []);
});

test('a word that is not a mode is refused with nothing written and nothing started', () => {
    for (const bad of ['Demo', 'sample', '', null, undefined, 1, {}]) {
        const b = bench();
        assert.throws(() => applyMode(bad, b.deps), /not a mode/);
        assert.deepEqual(b.steps, []);
    }
});

test('a real write that fails, the folder being a file, is refused and leaves no file', () => {
    const at = folder();
    const desktop = join(at, 'desktop');
    writeFileSync(desktop, 'not a folder');
    const file = join(desktop, 'mode');
    const steps: string[] = [];
    const answer = applyMode('demo', { write: (mode) => writeMode(file, mode), relaunch: () => void steps.push('relaunch') });
    assert.deepEqual(answer, { refused: modeUnkept });
    assert.deepEqual(steps, []);
    assert.equal(existsSync(file), false);
});

test('the demo’s data directory is the person’s sibling, with -demo after its name, a trailing separator aside', () => {
    const home = join(folder(), 'home');
    assert.equal(demoDirectory(join(home, '.jason')), join(home, '.jason-demo'));
    assert.equal(demoDirectory(join(home, 'my-data')), join(home, 'my-data-demo'));
    assert.equal(demoDirectory(`${join(home, '.jason')}/`), join(home, '.jason-demo'));
    assert.equal(demoDirectory(join(home, '.jason') + String.fromCharCode(92)), join(home, '.jason-demo'));
});

test('a start runs on the person’s directory in Live and on the demo’s in Demo, the record staying in the person’s', () => {
    const person = join(folder(), '.jason');
    assert.deepEqual(runsOn(person, 'live', false), { person, data: person, mode: 'live' });
    assert.deepEqual(runsOn(person, 'demo', false), { person, data: `${person}-demo`, mode: 'demo' });
});

test('the installer’s and the uninstaller’s starts ignore the record and act on the person’s installation', () => {
    const person = join(folder(), '.jason');
    assert.deepEqual(runsOn(person, 'demo', true), { person, data: person, mode: 'live' });
});

test('the record read at start comes from the person’s directory: a demo record there makes the run the demo’s', () => {
    const person = join(folder(), '.jason');
    writeMode(desktopPaths(person).mode, 'demo');
    const run = runsOn(person, readMode(desktopPaths(person).mode), false);
    assert.equal(run.data, `${person}-demo`);
    assert.equal(desktopPaths(run.person).mode, join(person, 'desktop', 'mode'));
});

test('a packaged desktop starts again with nothing after the program, an unpackaged one with its folder', () => {
    assert.deepEqual(relaunchOptions(['C:/Programs/Jason/Jason.exe', 'jason://campaigns', '--opened-at-login'], true, 'C:/Programs/Jason/Jason.exe'), {
        execPath: 'C:/Programs/Jason/Jason.exe',
        args: [],
    });
    assert.deepEqual(relaunchOptions(['/x/electron', '.', '--inspect'], false, '/x/electron'), { execPath: '/x/electron', args: ['.'] });
    // On Linux an AppImage starts again from its file, never from the mount that goes with this run.
    assert.deepEqual(relaunchOptions(['/tmp/.mount_Jason/jason-desktop'], true, '/home/someone/Jason.AppImage').execPath, '/home/someone/Jason.AppImage');
});

test('the main module reads the record before any path is resolved, and only then names the demo’s directory', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const chosen = main.indexOf('const personDirectory = dataDirectory(process.env, homedir());');
    assert.ok(main.indexOf('const runOn: StartRun = startOn(personDirectory, readMode(desktopPaths(personDirectory).mode), installerStart, {') > chosen);
    // A demo directory that cannot be opened sets the PERSON'S record back to Live.
    assert.ok(main.includes('keep: (mode) => writeMode(desktopPaths(personDirectory).mode, mode),'));
    const named = main.indexOf('if (runOn.mode === \'demo\') process.env.JASON_DATA_DIR = runOn.data;');
    const firstPath = main.indexOf('const installationWasNew = isNewInstallation(');
    assert.ok(chosen > 0 && named > chosen && firstPath > named);
    // Nothing before the choice resolves the data directory at the module's top level.
    const before = main.slice(main.indexOf('const unsandboxed'), chosen);
    assert.equal(before.includes('dataDirectory('), false);
    assert.ok(main.includes('uninstallStart(process.argv, { packaged: app.isPackaged, platform: process.platform }) !== null ||'));
});

test('the main process answers mode.get with this run’s mode and whether the demo could not be opened, writes the person’s record and starts again', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.ok(main.includes("handle(ipcMain, 'mode.get', origin, (): ModeAnswer => ({ mode: runOn.mode, unopened: runOn.unopened }), refused);"));
    assert.ok(main.includes("handle(ipcMain, 'mode.set', origin, (choice) => applyMode(choice, modeDeps), refused);"));
    assert.ok(main.includes('writeMode(desktopPaths(runOn.person).mode, mode);'));
    assert.ok(main.includes('app.relaunch(relaunchOptions(process.argv, app.isPackaged, form === null ? process.execPath : launcherOf(form, process.execPath)));'));
    assert.equal(main.includes('webContents.reload()'), false);
});

test('a demo start whose directory can be made and used runs on it, the directory made', () => {
    const person = join(folder(), '.jason');
    writeMode(desktopPaths(person).mode, 'demo');
    const run = startOn(person, 'demo', false, { open: openDirectory, keep: (mode) => writeMode(desktopPaths(person).mode, mode) });
    assert.deepEqual(run, { person, data: `${person}-demo`, mode: 'demo', unopened: null });
    assert.ok(existsSync(`${person}-demo`));
    assert.deepEqual(readdirSync(`${person}-demo`), [], 'nothing is left in it by the check');
    assert.equal(readMode(desktopPaths(person).mode), 'demo');
});

test('a demo start whose directory cannot be made falls back to the person’s, sets the record back to live and says so', () => {
    const person = join(folder(), '.jason');
    writeMode(desktopPaths(person).mode, 'demo');
    // A file where the demo's directory would be: it can be neither made nor used.
    writeFileSync(`${person}-demo`, 'not a folder');
    const run = startOn(person, 'demo', false, { open: openDirectory, keep: (mode) => writeMode(desktopPaths(person).mode, mode) });
    assert.deepEqual(run, { person, data: person, mode: 'live', unopened: demoUnopened });
    assert.equal(readMode(desktopPaths(person).mode), 'live');
    assert.match(demoUnopened, /demo could not be opened/);
});

test('a demo directory that refuses its use falls back the same way, and a record that cannot be set back still falls back', () => {
    const person = join(folder(), '.jason');
    const kept: Mode[] = [];
    const refuse = () => {
        throw new Error('EACCES');
    };
    assert.deepEqual(startOn(person, 'demo', false, { open: refuse, keep: (mode) => void kept.push(mode) }), { person, data: person, mode: 'live', unopened: demoUnopened });
    assert.deepEqual(kept, ['live']);
    assert.deepEqual(startOn(person, 'demo', false, { open: refuse, keep: refuse }), { person, data: person, mode: 'live', unopened: demoUnopened });
});

test('the unopened sentence is the desktop’s own words, never the error’s text nor a path the person gave', () => {
    const person = join(folder(), 'Chosen By The Person', '.jason');
    const leaky = () => {
        throw new Error(`EACCES: permission denied, mkdir '${person}-demo'`);
    };
    const run = startOn(person, 'demo', false, { open: leaky, keep: leaky });
    assert.equal(run.unopened, demoUnopened);
    assert.equal(run.unopened.includes(person), false);
    assert.equal(run.unopened.includes('Chosen By The Person'), false);
    assert.equal(run.unopened.includes('EACCES'), false);
    // The sentence is one constant: no part of it is made from what the start was given.
    assert.equal(demoUnopened, 'The demo could not be opened, so Jason opened your own installation.');
    const source = readFileSync(new URL('../src/mode.ts', import.meta.url), 'utf8');
    const given = [...source.matchAll(/unopened: ([^,;}\n]+)/g)].map((each) => each[1]!.trim());
    assert.ok(given.length > 0);
    for (const value of given) assert.ok(['null', 'demoUnopened', 'string | null'].includes(value), `unopened is only ever null or the constant, not ${value}`);
});

test('a live start and an installer start look at no demo directory', () => {
    const person = join(folder(), '.jason');
    const opened: string[] = [];
    const deps = { open: (dir: string) => void opened.push(dir), keep: () => assert.fail('nothing is set back') };
    assert.deepEqual(startOn(person, 'live', false, deps), { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual(startOn(person, 'demo', true, deps), { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual(opened, []);
});

test('the demo’s menu says, where an update, the PATH command, the start at logon or Remove What Jason Placed would be, to go back to your own installation', () => {
    for (const item of ['updates', 'autostart', 'command', 'removal'] as const) assert.match(demoMenuLine(item), /go back to your own installation/i);
    assert.notEqual(demoMenuLine('updates'), demoMenuLine('autostart'));
});
