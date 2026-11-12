import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyTheme, darkBackground, isNewInstallation, lightBackground, startingTheme } from '../src/theme.ts';
import { parseSettings, writeSettings, type DesktopSettings } from '../src/settings.ts';

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
