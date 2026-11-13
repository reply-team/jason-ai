import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from '../src/theme.ts';
import { parseSettings, settingsAtStart, writeSettings, type DesktopSettings } from '../src/settings.ts';
import { demoDirectory, keepFirstStart, readMode, writeMode } from '../src/mode.ts';
import { desktopPaths } from '../src/paths.ts';
import { requestStartOver } from '../src/demo-over.ts';

const base: DesktopSettings = { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: '0.35.0', theme: null };

function bench() {
    const sources: string[] = [];
    const written: DesktopSettings[] = [];
    return { sources, written, deps: { setSource: (c: string) => void sources.push(c), write: (s: DesktopSettings) => void written.push(s) } };
}

test('each of the three words is accepted, set as the source and written with the other settings kept', () => {
    for (const word of ['system', 'light', 'dark']) {
        const b = bench();
        const next = applyTheme(word, base, b.deps as never);
        assert.deepEqual(b.sources, [word]);
        assert.deepEqual(b.written, [{ ...base, theme: word }]);
        assert.deepEqual(next, { ...base, theme: word });
    }
});

test('a fourth word, nothing, an object and an empty string are refused with nothing written', () => {
    for (const bad of ['sepia', undefined, null, {}, '', 3]) {
        const b = bench();
        assert.throws(() => applyTheme(bad, base, b.deps as never), /not a theme/);
        assert.deepEqual(b.sources, []);
        assert.deepEqual(b.written, []);
    }
});

test('the same choice again writes nothing', () => {
    const b = bench();
    applyTheme('dark', { ...base, theme: 'dark' }, b.deps as never);
    assert.deepEqual(b.written, []);
});

test('the file is written and read back with the feed and the skills beside the theme, and Light is written when chosen', () => {
    const file = join(mkdtempSync(join(tmpdir(), 'theme-')), 'desktop', 'settings.json');
    writeSettings(file, { ...base, theme: 'light' });
    assert.deepEqual(parseSettings(readFileSync(file, 'utf8')), { ...base, theme: 'light' });
    writeSettings(file, base);
    assert.equal(parseSettings(readFileSync(file, 'utf8')).theme, null);
    assert.equal('theme' in JSON.parse(readFileSync(file, 'utf8')), false);
});

test('a new installation meets Light, an existing one that never chose follows the system, a choice is kept', () => {
    assert.equal(startingTheme(null, true), 'light');
    assert.equal(startingTheme(null, false), 'system');
    assert.equal(startingTheme('dark', true), 'dark');
    assert.equal(startingTheme('dark', false), 'dark');
});

test('an installation is new when its data directory is absent or empty, and not when anything is in it', () => {
    assert.equal(isNewInstallation('d', () => { throw new Error('ENOENT'); }), true);
    assert.equal(isNewInstallation('d', () => []), true);
    assert.equal(isNewInstallation('d', () => ['jason.db']), false);
});

test('a data directory that holds only the desktop’s own mode record, or that and a request to start the demo over, is new', () => {
    const person = join(mkdtempSync(join(tmpdir(), 'theme-new-')), '.jason');
    writeMode(desktopPaths(person).mode, 'demo');
    assert.equal(isNewInstallation(person), true, 'the record alone');
    requestStartOver(desktopPaths(person).demoStartOver, 4242);
    assert.equal(isNewInstallation(person), true, 'the record and the request');
    // Anything else of an installation in the desktop's folder, or beside it, makes it one that has been used.
    writeFileSync(desktopPaths(person).settings, '{}');
    assert.equal(isNewInstallation(person), false, 'the desktop’s settings');
    const other = join(mkdtempSync(join(tmpdir(), 'theme-new-')), '.jason');
    writeMode(desktopPaths(other).mode, 'live');
    mkdirSync(join(other, 'logs'));
    assert.equal(isNewInstallation(other), false, 'a folder beside the record');
    // A desktop entry that is a file, not the desktop's folder, is no record of the desktop's.
    const odd = mkdtempSync(join(tmpdir(), 'theme-new-'));
    writeFileSync(join(odd, 'desktop'), '');
    assert.equal(isNewInstallation(odd), false);
});

test('a new person meets Light in the demo they see first, and again in their own installation when they first choose Live', () => {
    const person = join(mkdtempSync(join(tmpdir(), 'theme-first-')), '.jason');
    // The first start: newness is read of the person's directory before anything is written, then the record is kept.
    const isNew = isNewInstallation(person);
    assert.equal(isNew, true);
    assert.equal(keepFirstStart(desktopPaths(person).mode, isNew, false), true);
    assert.equal(readMode(desktopPaths(person).mode), 'demo');
    // The demo run's own settings meet Light, by the person's newness, though the demo's directory is not empty by then.
    const demo = demoDirectory(person);
    mkdirSync(join(demo, 'plugins', 'demo'), { recursive: true });
    assert.equal(isNewInstallation(demo), false, 'what makes the demo run read the person’s newness, not its own');
    assert.equal(startingTheme(null, isNew), 'light');
    assert.equal(settingsAtStart(desktopPaths(demo).settings, isNew).theme, 'light');
    // Live: the record says so, and the person's directory, holding only it, is still new: their own first start is Light.
    writeMode(desktopPaths(person).mode, 'live');
    assert.deepEqual(readdirSync(person), ['desktop']);
    const atLive = isNewInstallation(person);
    assert.equal(atLive, true);
    assert.equal(startingTheme(null, atLive), 'light');
    assert.equal(settingsAtStart(desktopPaths(person).settings, atLive).theme, 'light');
    // From then on the installation has been used.
    assert.equal(isNewInstallation(person), false);
});

test('the two backgrounds are the starting page’s own', () => {
    const css = readFileSync(new URL('../shell/shell.css', import.meta.url), 'utf8');
    assert.ok(css.includes(`--bg: ${lightBackground}`));
    assert.ok(css.includes(`--bg: ${darkBackground}`));
});

test('whether the installation is new is decided in the main module before Electron makes its profile, on either start', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    // Decided where the installation is chosen, which a start runs before it places Electron's profile: of the PERSON's
    // directory, before the first start's record is written and before the run is entered.
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    const read = choose.indexOf('installationWasNew = isNewInstallation(personDirectory);');
    assert.ok(read > 0);
    assert.ok(choose.indexOf('keepFirstStart(') > read, 'the record is written after newness is read');
    assert.ok(choose.indexOf('runOn = startOn(') > read && choose.indexOf('enterRun(process.env, runOn)') > read);
    assert.equal(choose.split('isNewInstallation(').length - 1, 1, 'read once');
    const start = main.slice(main.indexOf('function startDesktop('));
    const chosen = start.indexOf('chooseInstallation(uninstalling');
    const firstKeep = start.indexOf('\n    keepElectronUnderData();');
    assert.ok(chosen > 0 && firstKeep > chosen);
    // Before the choice the profile is placed only by the start a refused link makes, which quits and reads nothing.
    const before = start.slice(0, chosen);
    assert.equal(before.split('keepElectronUnderData();').length - 1, 1);
    assert.ok(before.includes("keepElectronUnderData();\n        startLog().warn('deep_link_refused', { Code: fromArgs.reason });\n        app.exit(1);\n        return;"));
    assert.ok(main.includes('run(links, installationWasNew)'));
});

test('at start the settings file is written when it is missing, with a new installation meeting Light, and left as it is when it is there', () => {
    const folder = mkdtempSync(join(tmpdir(), 'settings-start-'));
    const missing = join(folder, 'existing', 'desktop', 'settings.json');
    assert.deepEqual(settingsAtStart(missing, false), { feed: null, skillsDeployedFor: null, theme: null });
    assert.ok(existsSync(missing), 'written although nothing in it differs from the defaults');
    assert.deepEqual(parseSettings(readFileSync(missing, 'utf8')), { feed: null, skillsDeployedFor: null, theme: null });

    const fresh = join(folder, 'new', 'desktop', 'settings.json');
    assert.equal(settingsAtStart(fresh, true).theme, 'light');
    assert.equal(parseSettings(readFileSync(fresh, 'utf8')).theme, 'light');

    const kept = join(folder, 'kept', 'desktop', 'settings.json');
    writeSettings(kept, { ...base, theme: 'dark' });
    const before = readFileSync(kept, 'utf8');
    const at = statSync(kept).mtimeMs;
    assert.deepEqual(settingsAtStart(kept, true), { ...base, theme: 'dark' });
    assert.equal(readFileSync(kept, 'utf8'), before);
    assert.equal(statSync(kept).mtimeMs, at, 'not written again');
});

test('the main module takes its settings at start from settingsAtStart, so the file is there before any chat', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.ok(main.includes('let settings: DesktopSettings = settingsAtStart(paths.settings, newInstallation);'));
});
