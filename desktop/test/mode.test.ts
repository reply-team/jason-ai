import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyMode, defaultMode, modeUnkept, parseMode, readMode, writeMode, type Mode } from '../src/mode.ts';
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
            reload: () => void steps.push('reload'),
        },
    };
}

test('setting a mode writes the file first and then reloads the window, and answers null', () => {
    for (const mode of ['demo', 'live'] as Mode[]) {
        const b = bench();
        assert.equal(applyMode(mode, b.deps), null);
        assert.deepEqual(b.steps, [`write ${mode}`, 'reload']);
    }
});

test('a write that fails does not reload and says so as a refusal', () => {
    const b = bench(true);
    assert.deepEqual(applyMode('demo', b.deps), { refused: modeUnkept });
    assert.deepEqual(b.steps, []);
});

test('a word that is not a mode is refused with nothing written and no reload', () => {
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
    const answer = applyMode('demo', { write: (mode) => writeMode(file, mode), reload: () => void steps.push('reload') });
    assert.deepEqual(answer, { refused: modeUnkept });
    assert.deepEqual(steps, []);
    assert.equal(existsSync(file), false);
});

test('the main process answers mode.get from the file and mode.set through applyMode, reloading the window', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.ok(main.includes("handle(ipcMain, 'mode.get', origin, () => readMode(paths.mode), refused);"));
    assert.ok(main.includes("handle(ipcMain, 'mode.set', origin, (choice) => applyMode(choice, modeDeps), refused);"));
    assert.ok(main.includes('writeMode(paths.mode, mode);'));
    assert.ok(main.includes('reload: () => current?.webContents.reload(),'));
});
