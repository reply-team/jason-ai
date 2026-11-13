import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyMode, defaultMode, demoDirectory, demoMenu, demoMenuLine, demoUnopened, enterRun, modeUnkept, offersOf, openDirectory, parseMode, readMode, relaunchAsPerson, relaunchOptions, runsOn, startOn, writeMode, type Environment, type Mode } from '../src/mode.ts';
import { dataDirectory, desktopPaths } from '../src/paths.ts';

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

// The one ordering guard left on the main module's text: what runs before Electron resolves any path cannot be run here.
test('the main module reads the record before any path is resolved, and only then points the process at the run', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const chosen = main.indexOf('const personDirectory = dataDirectory(process.env, homedir());');
    const started = main.indexOf('const runOn: StartRun = startOn(personDirectory, readMode(desktopPaths(personDirectory).mode), installerStart, {');
    const entered = main.indexOf('const personEnvironment = enterRun(process.env, runOn);');
    const firstPath = main.indexOf('const installationWasNew = isNewInstallation(');
    assert.ok(chosen > 0 && started > chosen && entered > started && firstPath > entered);
    // Nothing before the choice resolves the data directory at the module's top level.
    assert.equal(main.slice(main.indexOf('const unsandboxed'), chosen).includes('dataDirectory('), false);
});

const noPreparation = { prepare: () => undefined };

test('a demo start whose directory can be made and used runs on it, the directory made', () => {
    const person = join(folder(), '.jason');
    writeMode(desktopPaths(person).mode, 'demo');
    const run = startOn(person, 'demo', false, { open: openDirectory, ...noPreparation, keep: (mode) => writeMode(desktopPaths(person).mode, mode) });
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
    const run = startOn(person, 'demo', false, { open: openDirectory, ...noPreparation, keep: (mode) => writeMode(desktopPaths(person).mode, mode) });
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
    assert.deepEqual(startOn(person, 'demo', false, { open: refuse, ...noPreparation, keep: (mode) => void kept.push(mode) }), { person, data: person, mode: 'live', unopened: demoUnopened });
    assert.deepEqual(kept, ['live']);
    assert.deepEqual(startOn(person, 'demo', false, { open: refuse, ...noPreparation, keep: refuse }), { person, data: person, mode: 'live', unopened: demoUnopened });
});

test('the demo is prepared after its directory is made and before the start goes on; a preparation that fails falls back', () => {
    const person = join(folder(), '.jason');
    const steps: string[] = [];
    const run = startOn(person, 'demo', false, {
        open: (dir) => void steps.push(`open ${dir}`),
        prepare: (dir) => void steps.push(`prepare ${dir}`),
        keep: () => assert.fail('nothing is set back'),
    });
    assert.deepEqual(steps, [`open ${person}-demo`, `prepare ${person}-demo`]);
    assert.equal(run.mode, 'demo');
    const kept: Mode[] = [];
    const failed = startOn(person, 'demo', false, {
        open: () => undefined,
        prepare: () => {
            throw new Error('settings unwritable');
        },
        keep: (mode) => void kept.push(mode),
    });
    assert.deepEqual(failed, { person, data: person, mode: 'live', unopened: demoUnopened });
    assert.deepEqual(kept, ['live']);
});

test('the unopened sentence is the desktop’s own words, never the error’s text nor a path the person gave', () => {
    const person = join(folder(), 'Chosen By The Person', '.jason');
    const leaky = () => {
        throw new Error(`EACCES: permission denied, mkdir '${person}-demo'`);
    };
    const run = startOn(person, 'demo', false, { open: leaky, prepare: leaky, keep: leaky });
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

test('a live start and an installer start look at no demo directory and prepare nothing', () => {
    const person = join(folder(), '.jason');
    const touched: string[] = [];
    const deps = { open: (dir: string) => void touched.push(dir), prepare: (dir: string) => void touched.push(dir), keep: () => assert.fail('nothing is set back') };
    assert.deepEqual(startOn(person, 'live', false, deps), { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual(startOn(person, 'demo', true, deps), { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual(touched, []);
});

test('a demo run points this process at the demo’s directory and away from a harness root the person set; a live run changes nothing', () => {
    const run = { person: '/p/.jason', data: '/p/.jason-demo', mode: 'demo' as const };
    const env: Environment = { PATH: '/bin', JASON_Harness__Root: '/p/Mine', jason_harness__root: '/p/Other' };
    const person = enterRun(env, run);
    assert.deepEqual(env, { PATH: '/bin', JASON_DATA_DIR: '/p/.jason-demo' });
    assert.deepEqual(person, { dataDirectory: undefined, harnessRoot: { JASON_Harness__Root: '/p/Mine', jason_harness__root: '/p/Other' } });
    const live: Environment = { PATH: '/bin', JASON_DATA_DIR: '/x', JASON_Harness__Root: '/r' };
    enterRun(live, { person: '/x', data: '/x', mode: 'live' });
    assert.deepEqual(live, { PATH: '/bin', JASON_DATA_DIR: '/x', JASON_Harness__Root: '/r' });
});

/** One start of the desktop as the main module makes it, against a home of its own: the record read, the process pointed at the run. */
function startDesktop(env: Environment, home: string) {
    const personDirectory = dataDirectory(env, home);
    const run = startOn(personDirectory, readMode(desktopPaths(personDirectory).mode), false, {
        open: openDirectory,
        ...noPreparation,
        keep: (mode) => writeMode(desktopPaths(personDirectory).mode, mode),
    });
    return { run, person: enterRun(env, run), data: dataDirectory(env, home) };
}

/** The switch the page asks for, from a run, as the main process makes it: the record written, the desktop started again with the environment it would inherit. */
function switchTo(mode: Mode, env: Environment, started: ReturnType<typeof startDesktop>): Environment {
    let inherited: Environment | null = null;
    const steps: string[] = [];
    const refused = applyMode(mode, {
        write: (next) => writeMode(desktopPaths(started.run.person).mode, next),
        relaunch: () =>
            relaunchAsPerson({
                env,
                person: started.person,
                relaunch: () => {
                    steps.push('relaunch');
                    inherited = { ...env };
                },
                quit: () => void steps.push('quit'),
            }),
    });
    assert.equal(refused, null);
    assert.deepEqual(steps, ['relaunch', 'quit']);
    return inherited!;
}

test('from Demo, the switch to Live starts the desktop again on the person’s directory, with no JASON_DATA_DIR when they gave none', () => {
    const home = folder();
    const env: Environment = { PATH: '/bin' };
    writeMode(desktopPaths(join(home, '.jason')).mode, 'demo');
    const demo = startDesktop(env, home);
    assert.equal(demo.run.mode, 'demo');
    assert.equal(demo.data, join(home, '.jason-demo'), 'this run is the demo’s');
    const inherited = switchTo('live', env, demo);
    assert.deepEqual(inherited, { PATH: '/bin' }, 'the person’s environment, exactly');
    const next = startDesktop(inherited, home);
    assert.deepEqual(next.run, { person: join(home, '.jason'), data: join(home, '.jason'), mode: 'live', unopened: null });
    assert.equal(next.data, join(home, '.jason'));
    assert.equal(existsSync(desktopPaths(join(home, '.jason-demo')).mode), false, 'no record was ever written in the demo’s directory');
});

test('from Demo, the switch to Live puts back a JASON_DATA_DIR and a harness root the person gave, and Demo again is the same sibling', () => {
    const home = folder();
    const mine = join(home, 'elsewhere', 'data');
    const env: Environment = { JASON_DATA_DIR: mine, JASON_Harness__Root: join(home, 'Roots') };
    writeMode(desktopPaths(mine).mode, 'demo');
    const demo = startDesktop(env, home);
    assert.equal(demo.data, `${mine}-demo`);
    const inherited = switchTo('live', env, demo);
    assert.deepEqual(inherited, { JASON_DATA_DIR: mine, JASON_Harness__Root: join(home, 'Roots') });
    const live = startDesktop(inherited, home);
    assert.deepEqual(live.run, { person: mine, data: mine, mode: 'live', unopened: null });
    // And back to Demo: the record is the person's, and the demo is the same sibling, never a sibling of the demo.
    const again = startDesktop(switchTo('demo', inherited, live), home);
    assert.equal(again.data, `${mine}-demo`);
    assert.equal(existsSync(`${mine}-demo-demo`), false);
});

test('a demo run offers nothing that outlasts it, Remove What Jason Placed included, and offers the way back; a live run offers it all', () => {
    assert.deepEqual(offersOf('demo'), { updates: false, autostart: false, command: false, removal: false, back: true });
    assert.deepEqual(offersOf('live'), { updates: true, autostart: true, command: true, removal: true, back: false });
    assert.equal(demoMenu('live', true), null);
    const menu = demoMenu('demo', true)!;
    for (const line of [menu.updates, menu.autostart, menu.command, menu.removal!]) {
        assert.equal(line.enabled, false);
        assert.match(line.label, /: not in the demo\. Go back to your own installation for this\.$/);
    }
    assert.equal(menu.removal!.label, 'Remove What Jason Placed: not in the demo. Go back to your own installation for this.');
    assert.deepEqual(menu.back, { label: 'Back to Your Own Installation', enabled: true });
    // Where the platform offers no Remove What Jason Placed (Windows keeps its uninstaller), there is no line for it.
    assert.equal(demoMenu('demo', false)!.removal, null);
});

test('the demo’s menu says, where an update, the PATH command, the start at logon or Remove What Jason Placed would be, to go back to your own installation', () => {
    for (const item of ['updates', 'autostart', 'command', 'removal'] as const) assert.match(demoMenuLine(item), /go back to your own installation/i);
    assert.notEqual(demoMenuLine('updates'), demoMenuLine('autostart'));
});
