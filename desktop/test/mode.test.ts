import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyMode, defaultMode, demoDirectory, demoMenu, demoMenuLine, demoPreparing, demoUnopened, enterRun, isSettingsVariable, keepFirstStart, modeUnkept, offersOf, openDirectory, opensOnDemo, parseMode, personsOwnOf, readMode, relaunchAsPerson, relaunchOptions, runsOn, startOn, writeMode, type Environment, type Mode } from '../src/mode.ts';
import { dataDirectory, dataLayout, desktopPaths } from '../src/paths.ts';
import { harnessRoot } from '../src/uninstall.ts';
import { isNewInstallation } from '../src/theme.ts';
import { prepareDemo, writeDemoRoute } from '../src/demo-start.ts';
import { claimDemoFolder } from '../src/demo-folders.ts';
import { placeDemoPlugin } from '../src/demo-plugin.ts';
import { writeDemoSettings } from '../src/demo-settings.ts';
import { demoFullAccess } from '../src/chat-modes.ts';

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
test('a start reads the record before it resolves any path, and only then points the process at the run', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    const chosen = choose.indexOf('const personDirectory = dataDirectory(process.env, homedir());');
    const fresh = choose.indexOf('installationWasNew = isNewInstallation(personDirectory);');
    const first = choose.indexOf('keepFirstStart(desktopPaths(personDirectory).mode, installationWasNew, installerStart)');
    const carried = choose.indexOf('carryOutStartOver({');
    const started = choose.indexOf('runOn = startOn(personDirectory, readMode(desktopPaths(personDirectory).mode), installerStart, {');
    const entered = choose.indexOf('personEnvironment = enterRun(process.env, runOn);');
    const firstPath = choose.indexOf('earlierPageWasFound = earlierPageFound(');
    // Newness is read of the person's directory first, then a first start's record is written, then the record is read.
    assert.ok(chosen > 0 && fresh > chosen && first > fresh && carried > first && started > carried && entered > started && firstPath > entered);
    // In the start, nothing between the link's decision and the choice resolves a path, and the profile, the logs and
    // the lock follow the choice.
    const start = main.slice(main.indexOf('function startDesktop('));
    const decided = start.indexOf('const fromArgs = linkIn(');
    const choice = start.indexOf('chooseInstallation(uninstalling');
    const between = start.slice(start.indexOf('    const uninstalling = uninstallStart('), choice);
    for (const path of ['dataDirectory(', 'startLog(', 'keepElectronUnderData(', 'desktopPaths(']) assert.equal(between.includes(path), false, path);
    assert.ok(decided > 0 && choice > decided);
    for (const after of ['keepElectronUnderData();' + String.fromCharCode(10) + '    if (uninstalling !== null) {', 'app.requestSingleInstanceLock(', 'const early = startLog();']) assert.ok(start.indexOf(after) > choice, after);
    // Nothing at the module's top resolves the data directory before a start chooses, but the sandbox's refusal.
    const top = main.slice(main.indexOf('const unsandboxed'), main.indexOf('function chooseInstallation('));
    assert.equal(top.replace(/if \(unsandboxed !== null\) \{[\s\S]*?\} else \{/, '').includes('dataDirectory('), false);
});

// What runs around Electron's own start cannot be run here: the prepared demo's order is held on the main module's text.
test('the prepared demo is placed from the first instant a start is the only one, before its log opens, and waited for before the runtime starts', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const start = main.slice(main.indexOf('function startDesktop('), main.indexOf('function beginDemoPlacement('));
    const lock = start.indexOf('app.requestSingleInstanceLock(');
    const placed = start.indexOf('placement = beginDemoPlacement();');
    const logged = start.indexOf('const early = startLog();');
    const ran = start.indexOf('void app.whenReady().then(() => releasedAfter(logHold, () => run(links, installationWasNew)));');
    assert.ok(lock > 0 && placed > lock && logged > placed && ran > logged, 'the lock, the import, the log, then the window');
    // However the run ends, and at a quit, the held lines are written.
    assert.ok(start.indexOf('releaseAtQuit(app, logHold);') > placed && start.indexOf('releaseAtQuit(app, logHold);') < ran);
    // The import and the run share one reading of the login shell's PATH, which the import waits for.
    const begin = main.slice(main.indexOf('function beginDemoPlacement('));
    assert.ok(begin.includes('pathReadAtStart = pathRead;') && begin.includes('withPath(process.env, await pathRead)'));
    assert.ok(main.includes('const pathRead = pathReadAtStart ?? loginPath(pathOf(process.env), home);'));
    assert.ok(begin.includes('spawned: (pid) => requestStartOver(request, process.pid, true, true, pid),'), 'the import is named in the request');
    // Every log of a start is on the hold, the run's as well as the early one.
    assert.ok(main.includes('return logHold.logger(() => fileLines(logs));'));
    assert.ok(main.includes('const log = logHold.logger(() => fileLines(paths.logs, limitsFrom(readText(dataLayout(paths.data).userSettingsFile))));'));
    // In the run: the window first, then the import's end, then the runtime's start.
    const run = main.slice(main.indexOf('async function run('), main.indexOf('function keepElectronUnderData('));
    const window = run.indexOf('const window = new BrowserWindow({');
    const settled = run.indexOf('await settlePlacing(placement, {');
    const runtime = run.indexOf('const started = await startInstallation({');
    assert.ok(window > 0 && settled > window && runtime > settled);
    // The mark is on from the first menu while the import runs, not only from its end.
    const marked = run.indexOf('if (placement !== null) setPreparing(true);');
    assert.ok(marked > run.indexOf('const setPreparing = (on: boolean) => {') && marked < settled);
    assert.ok(run.includes("await showStarting(window, placement !== null ? placingWords : 'Starting Jason…', false);"));
    // The sample's marker is written by the placing's end alone, which writes it only once the import has succeeded.
    assert.equal(main.split('writeSampleWhole(').length - 1, 1, 'called once');
    assert.ok(run.includes('seal: () => writeSampleWhole(demoWhere.data),'));
    // A start that places the demo neither prepares it nor makes its harness root before the import.
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    assert.ok(choose.indexOf('demoPlacing =') > 0 && choose.indexOf('demoPlacing =') < choose.indexOf('runOn = startOn('));
    assert.ok(choose.includes("prepare: (demo) => (demoPlacing.kind === 'place' || placingElsewhere ? undefined : prepareLive(demo)),"));
    assert.ok(main.includes('request: () => requestStartOver(request, process.pid, true, true),'), 'a placing’s request is a making’s too');
    assert.ok(choose.indexOf('carryOutStartOver({') < choose.indexOf('demoPlacing ='), 'a start over is carried out first, so the plan sees the folders it left');
});

test('a start that finds a demo runtime a crashed making or placing left stops it through the demo’s own stop and starts again', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    const caught = choose.indexOf('} catch (error) {', choose.indexOf('carryOutStartOver({'));
    const left = choose.indexOf("if (reason === 'runtime_alive' && leftRuntimeStops(readText(desktopPaths(personDirectory).demoStartOver), process.pid, processIsRunning)) {");
    assert.ok(caught > 0 && left > caught, 'decided on the carry-out’s own refusal');
    // Only after the demo's instance lock is won, and only the demo's runtime: by the demo's directory, never the person's.
    const start = main.slice(main.indexOf('function startDesktop('));
    assert.ok(start.indexOf('} else if (leftDemoRuntime) {') > start.indexOf('app.requestSingleInstanceLock('));
    const stop = main.slice(main.indexOf('async function stopLeftRuntimeAndStart('));
    assert.ok(stop.includes('await stopDemoRuntime({ platform: process.platform, executable, person: runOn.person, env });'));
    assert.equal(stop.includes('stopOwnRuntime('), false, 'the person’s runtime is never asked to stop');
    assert.ok(stop.includes('bundles: desktopPaths(runOn.data).bundle'), 'with the jason the demo’s runtime runs from');
    assert.ok(stop.includes('note: () => noteLeftStopped(desktopPaths(runOn.person).demoStartOver),'), 'once for a request');
});

const noPreparation = { prepare: () => undefined };

test('a new installation’s first start opens on the demo; any other start goes by its record or the default', () => {
    assert.equal(opensOnDemo(false, true, false), true, 'no record, a new directory');
    assert.equal(opensOnDemo(true, true, false), false, 'a record is kept as it is');
    assert.equal(opensOnDemo(false, false, false), false, 'an installation that existed opens where it did');
    assert.equal(opensOnDemo(false, true, true), false, 'the installer’s and the uninstaller’s starts never do');
    assert.equal(defaultMode, 'live', 'a record that says nothing is still Live');
});

test('a new installation starts in the demo and prepares it without a click: the record written, the demo directory made and prepared', () => {
    const person = join(folder(), '.jason');
    const home = startFirst(person);
    assert.equal(home.wrote, true);
    assert.equal(readMode(desktopPaths(person).mode), 'demo');
    assert.equal(home.run.mode, 'demo');
    assert.equal(home.run.data, `${person}-demo`);
    assert.deepEqual(home.prepared, [`${person}-demo`], 'prepared at the start, nothing asked of the person');
    assert.equal(home.run.unopened, null);
});

test('an existing installation with no mode record opens where it did, Live, and nothing is written', () => {
    const person = join(folder(), '.jason');
    mkdirSync(person, { recursive: true });
    writeFileSync(join(person, 'jason.db'), '');
    const home = startFirst(person);
    assert.equal(home.wrote, false);
    assert.equal(existsSync(desktopPaths(person).mode), false);
    assert.deepEqual(home.run, { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual(home.prepared, []);
});

test('an installation with a mode record opens on its record, whatever else is there', () => {
    for (const mode of ['live', 'demo'] as Mode[]) {
        // The record alone: the directory is new by its contents, and still the record decides.
        const alone = join(folder(), '.jason');
        writeMode(desktopPaths(alone).mode, mode);
        const first = startFirst(alone);
        assert.equal(first.wrote, false);
        assert.equal(first.run.mode, mode);
        assert.equal(readMode(desktopPaths(alone).mode), mode, 'the record is not written over');
    }
    // A record that holds neither word is a record all the same: it reads as the default, as it always has.
    const garbage = join(folder(), '.jason');
    mkdirSync(join(garbage, 'desktop'), { recursive: true });
    writeFileSync(desktopPaths(garbage).mode, 'sample');
    const odd = startFirst(garbage);
    assert.equal(odd.wrote, false);
    assert.equal(odd.run.mode, 'live');
});

test('an update from the release before changes nothing, with a mode record and without one', () => {
    // An installation the release before made: a database, its logs, the desktop's settings; with or without a record.
    const made = (record: Mode | null): string => {
        const person = join(folder(), '.jason');
        mkdirSync(join(person, 'logs'), { recursive: true });
        writeFileSync(join(person, 'jason.db'), '');
        mkdirSync(join(person, 'desktop'), { recursive: true });
        writeFileSync(desktopPaths(person).settings, '{"skills_deployed_for":"0.39.0"}');
        if (record !== null) writeMode(desktopPaths(person).mode, record);
        return person;
    };
    for (const record of [null, 'live', 'demo'] as (Mode | null)[]) {
        const person = made(record);
        const before = readdirSync(join(person, 'desktop')).sort();
        const home = startFirst(person);
        assert.equal(home.isNew, false);
        assert.equal(home.wrote, false);
        assert.equal(home.run.mode, record ?? 'live');
        assert.deepEqual(readdirSync(join(person, 'desktop')).sort(), before, 'nothing written in the person’s desktop folder');
    }
});

test('a record that cannot be written leaves the first start where it was, on the person’s own installation', () => {
    // A file where the person's directory would be: no record can be written under it.
    const person = join(folder(), '.jason');
    writeFileSync(person, '');
    assert.equal(keepFirstStart(desktopPaths(person).mode, true, false), false);
    assert.equal(readMode(desktopPaths(person).mode), 'live');
});

test('nothing of the person’s installation exists before they choose Live, but the record; Live then runs today’s first start', () => {
    const home = folder();
    const person = join(home, '.jason');
    const personHarness = join(home, 'Jason');
    const carried = join(home, 'bundle', 'plugins', 'demo');
    mkdirSync(carried, { recursive: true });
    writeFileSync(join(carried, 'plugin.yaml'), 'id: demo\n');
    // The first start, with the demo's real preparation.
    const first = startFirst(person, (demo) =>
        prepareDemo(demo, {
            harnessRoot: () => claimDemoFolder(demoDirectory(personHarness)),
            place: (data) => placeDemoPlugin(carried, data, person),
            route: writeDemoRoute,
            fixedSettings: (data) => writeDemoSettings(data, personHarness),
        }),
    );
    assert.equal(first.run.mode, 'demo');
    assert.ok(existsSync(join(`${person}-demo`, 'plugins', 'demo', 'plugin.yaml')));
    // Of the person's own: the record and nothing else, and no harness root of theirs.
    assert.deepEqual(readdirSync(person), ['desktop']);
    assert.deepEqual(readdirSync(join(person, 'desktop')), ['mode']);
    assert.equal(existsSync(personHarness), false);
    // Live from the demo: the record written and the desktop started again, which is a first start of their own
    // installation: Live on their directory, new, as a start of a directory never used would be, with nothing prepared.
    const steps: string[] = [];
    assert.equal(applyMode('live', { write: (mode) => writeMode(desktopPaths(person).mode, mode), relaunch: () => void steps.push('relaunch') }), null);
    assert.deepEqual(steps, ['relaunch']);
    const live = startFirst(person);
    const never = startFirst(join(folder(), '.jason'), undefined, 'live');
    assert.deepEqual(live.run, { person, data: person, mode: 'live', unopened: null });
    assert.deepEqual({ ...never.run, person, data: person }, live.run);
    assert.equal(live.isNew, true);
    assert.equal(never.isNew, true);
    assert.deepEqual(live.prepared, []);
    assert.equal(live.wrote, false);
});

test('a first start’s run is a demo run, which main hands to the chat as one, where Full access is not offered', () => {
    const first = startFirst(join(folder(), '.jason'));
    assert.equal(first.run.mode, 'demo');
    assert.equal(demoFullAccess, 'Not in the demo: Full access is for your own installation.');
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const chat = main.slice(main.indexOf('const chat = new Chat({'));
    assert.ok(chat.slice(0, chat.indexOf('});')).includes("demo: runOn.mode === 'demo',"));
});

test('while the demo’s sample is made a switch is refused with the desktop’s words, a word that is not a mode still thrown', () => {
    const b = bench();
    assert.deepEqual(applyMode('live', b.deps, true), { refused: demoPreparing });
    assert.deepEqual(applyMode('demo', b.deps, true), { refused: demoPreparing });
    assert.deepEqual(b.steps, [], 'nothing written, nothing started');
    assert.throws(() => applyMode('sample', b.deps, true), /not a mode/);
    assert.equal(applyMode('live', b.deps, false), null);
    assert.deepEqual(b.steps, ['write live', 'relaunch']);
    assert.equal(demoPreparing, 'The demo is still being prepared. Try again once it is ready.');
});

test('while the demo’s sample is made the menu’s Start the Demo Over and the way back are off, each with its reason', () => {
    const making = demoMenu('demo', true, true)!;
    assert.deepEqual(making.startOver, { label: 'Start the Demo Over: available once the demo is ready', enabled: false });
    assert.deepEqual(making.back, { label: 'Back to Your Own Installation: available once the demo is ready', enabled: false });
    const ready = demoMenu('demo', true, false)!;
    assert.deepEqual(ready.startOver, { label: 'Start the Demo Over', enabled: true });
    assert.deepEqual(ready.back, { label: 'Back to Your Own Installation', enabled: true });
    assert.equal(demoMenu('live', true, true), null);
});

/**
 * A start's choice of installation as the main module makes it (`chooseInstallation`), against a directory of its own:
 * newness read of the person's directory, a first start's record written, the record read, the demo opened and prepared.
 * `record` writes a record first, for a start that is not a first one.
 */
function startFirst(person: string, prepare: (demo: string) => void = () => undefined, record?: Mode) {
    if (record !== undefined) writeMode(desktopPaths(person).mode, record);
    const isNew = isNewInstallation(person);
    const wrote = keepFirstStart(desktopPaths(person).mode, isNew, false);
    const prepared: string[] = [];
    const run = startOn(person, readMode(desktopPaths(person).mode), false, {
        open: openDirectory,
        prepare: (demo) => {
            prepared.push(demo);
            prepare(demo);
        },
        keep: (mode) => writeMode(desktopPaths(person).mode, mode),
    });
    return { isNew, wrote, run, prepared };
}

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
    assert.deepEqual(person, { dataDirectory: undefined, settings: { JASON_Harness__Root: '/p/Mine', jason_harness__root: '/p/Other' } });
    const live: Environment = { PATH: '/bin', JASON_DATA_DIR: '/x', JASON_Harness__Root: '/r' };
    enterRun(live, { person: '/x', data: '/x', mode: 'live' });
    assert.deepEqual(live, { PATH: '/bin', JASON_DATA_DIR: '/x', JASON_Harness__Root: '/r' });
});

test('a route, a role switch or any other runtime setting given by the environment does not reach a demo run’s environment', () => {
    const env: Environment = {
        PATH: '/bin',
        JASON_Routes__Default__Plugin: 'reply',
        jason_responder__onreply: 'true',
        JASON_Plugins__Grants__reply__0: 'api.example',
        JASON_DATA_DIR: '/p/.jason',
        JASON_NO_UNDERSCORES: 'kept',
        JASON_: 'kept too',
        NOT_JASON__Routes: 'kept as well',
    };
    const person = enterRun(env, { person: '/p/.jason', data: '/p/.jason-demo', mode: 'demo' });
    assert.deepEqual(env, { PATH: '/bin', JASON_DATA_DIR: '/p/.jason-demo', JASON_NO_UNDERSCORES: 'kept', JASON_: 'kept too', NOT_JASON__Routes: 'kept as well' });
    assert.equal(Object.keys(env).some(isSettingsVariable), false);
    assert.deepEqual(person.settings, { JASON_Routes__Default__Plugin: 'reply', jason_responder__onreply: 'true', JASON_Plugins__Grants__reply__0: 'api.example' });
});

test('back to Live puts back every runtime settings variable byte for byte, names included, and nothing added since', () => {
    const original: Environment = {
        PATH: '/bin',
        JASON_Routes__Default__Plugin: 'reply',
        jason_Manager__ReviewSeconds: ' 300 ',
        JASON_HARNESS__ROOT: '/p/Ünïcode Root',
        JASON_DATA_DIR: '/p/data',
    };
    const env: Environment = { ...original };
    const person = enterRun(env, { person: '/p/data', data: '/p/data-demo', mode: 'demo' });
    // A variable set during the run, as a settings variable, is no part of the person's environment.
    env.JASON_Dispatcher__TickSeconds = '2';
    let inherited: Environment | null = null;
    relaunchAsPerson({ env, person, relaunch: () => void (inherited = { ...env }), quit: () => undefined });
    assert.deepEqual(inherited, original);
    assert.deepEqual(Object.keys(inherited!).sort(), Object.keys(original).sort());
});

/** One start of the desktop as the main module makes it, against a home of its own: the record read, the process pointed at the run. */
function startDesktop(env: Environment, home: string) {
    const personDirectory = dataDirectory(env, home);
    keepFirstStart(desktopPaths(personDirectory).mode, isNewInstallation(personDirectory), false);
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
    assert.deepEqual(menu.startOver, { label: 'Start the Demo Over', enabled: true });
    // Where the platform offers no Remove What Jason Placed (Windows keeps its uninstaller), there is no line for it.
    assert.equal(demoMenu('demo', false)!.removal, null);
});

test('the demo’s menu says, where an update, the PATH command, the start at logon or Remove What Jason Placed would be, to go back to your own installation', () => {
    for (const item of ['updates', 'autostart', 'command', 'removal'] as const) assert.match(demoMenuLine(item), /go back to your own installation/i);
    assert.notEqual(demoMenuLine('updates'), demoMenuLine('autostart'));
});

test('a demo run hands the chat the person\'s data directory and harness root, read before the run takes the person\'s variables away; a live run hands none', () => {
    const person = join(folder(), '.jason');
    const root = join(folder(), 'MyJason');
    const env: Environment = { JASON_DATA_DIR: person, JASON_Harness__Root: root };
    const settings = dataLayout(person).userSettingsFile;
    const demo = runsOn(person, 'demo', false);
    const own = personsOwnOf(demo, () => harnessRoot(env, '/home/someone', settings));
    enterRun(env, demo);
    assert.deepEqual(own, { data: person, harnessRoot: root });
    // Read after the run is entered, the root the person named is gone: why main reads it first.
    assert.notEqual(harnessRoot(env, '/home/someone', settings), root);
    let read = false;
    assert.equal(personsOwnOf(runsOn(person, 'live', false), () => ((read = true), root)), undefined);
    assert.equal(read, false, 'a live run reads no harness root for the fence');
});

test('main passes the person\'s directories to the chat, read before the run is entered', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    const read = choose.indexOf('personsOwn = personsOwnOf(runOn, () => personHarness);');
    const entered = choose.indexOf('personEnvironment = enterRun(process.env, runOn);');
    assert.ok(read > 0 && entered > read);
    assert.ok(choose.indexOf('const personHarness = harnessRoot(process.env, homedir(), dataLayout(personDirectory).userSettingsFile);') > 0);
    const chat = main.slice(main.indexOf('const chat = new Chat({'));
    assert.ok(chat.slice(0, chat.indexOf('});')).includes('        personsOwn,\n'));
});

test('main tells the menu and the chat whether this is a demo run, where Full access is not offered', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const setup = main.slice(main.indexOf('new Setup({'), main.indexOf('const chat = new Chat({'));
    assert.ok(setup.includes("demo: runOn.mode === 'demo',"));
    const chat = main.slice(main.indexOf('const chat = new Chat({'));
    assert.ok(chat.slice(0, chat.indexOf('});')).includes("demo: runOn.mode === 'demo',"));
});
