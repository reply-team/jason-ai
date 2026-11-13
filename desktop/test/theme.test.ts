import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from '../src/theme.ts';
import { parseSettings, settingsAtStart, writeSettings, type DesktopSettings } from '../src/settings.ts';

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

test('the two backgrounds are the starting page’s own', () => {
    const css = readFileSync(new URL('../shell/shell.css', import.meta.url), 'utf8');
    assert.ok(css.includes(`--bg: ${lightBackground}`));
    assert.ok(css.includes(`--bg: ${darkBackground}`));
});

test('whether the installation is new is decided in the main module before Electron makes its profile, on either start', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    // Decided where the installation is chosen, which a start runs before it places Electron's profile.
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    assert.ok(choose.includes('installationWasNew = isNewInstallation('));
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
