// The temporary file a write of the runtime's settings goes through, held to the desktop's shape in
// settings-temporary-files.json, which a test writes from the runtime's own rule: the runtime's start sweeps a stray of
// that shape and `jason export` never carries one, so a writer of the settings that named its file otherwise would leave
// the settings' secrets where neither looks.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { settingsTemporary } from '../src/settings-temporary.ts';

const here = dirname(fileURLToPath(import.meta.url));
const sources = join(here, '..', 'src');

interface Shape {
    writer: string;
    digits: string;
    fewest_digits: number;
    most_digits: number;
}

const written = JSON.parse(readFileSync(join(here, '..', 'settings-temporary-files.json'), 'utf8')) as { suffix: string; shapes: Shape[] };

/** Whether `name` is a temporary file of `file`'s name in the shape `shape`. */
function isOfShape(file: string, name: string, shape: Shape): boolean {
    const prefix = `${file}.`;
    if (!name.startsWith(prefix) || !name.endsWith(written.suffix)) return false;
    const id = name.slice(prefix.length, name.length - written.suffix.length);
    return id.length >= shape.fewest_digits && id.length <= shape.most_digits && [...id].every((c) => shape.digits.includes(c));
}

test('the name a write of the settings goes through is of the desktop’s shape, beside the file it writes, for every process id', () => {
    const desktop = written.shapes.find((shape) => shape.writer === 'desktop');
    assert.ok(desktop, 'settings-temporary-files.json names no desktop shape');
    const file = join('data', 'config', 'settings.json');
    for (const pid of [1, 4242, process.pid, 4_194_304, 2 ** 32 - 1]) {
        const name = settingsTemporary(file, pid);
        assert.equal(dirname(name), dirname(file));
        assert.ok(isOfShape(basename(file), basename(name), desktop), `${basename(name)} is not of the desktop's shape`);
    }
    assert.equal(settingsTemporary(file), settingsTemporary(file, process.pid));
});

/** The four writers of the runtime's settings: the Connect cards, the demo's route, its fixed settings and its seed. */
const settingsWriters = ['setup.ts', 'demo-start.ts', 'demo-settings.ts', 'demo-seed.ts'];

/** Whether a source names the runtime's settings file or the secrets file, by the layout's members or the demo's helper. */
const namesTheSettings = (text: string): boolean => /\b(userSettingsFile|secretsFile|settingsFileOf)\b/.test(text);

test('every source that names the settings file or the secrets file makes its temporary name through settingsTemporary, never its own', () => {
    const naming = readdirSync(sources)
        .filter((name) => name.endsWith('.ts') && name !== 'settings-temporary.ts')
        .filter((name) => namesTheSettings(readFileSync(join(sources, name), 'utf8')));
    for (const writer of settingsWriters) {
        assert.ok(naming.includes(writer), `${writer} no longer names the settings file; move it out of settingsWriters`);
        assert.match(readFileSync(join(sources, writer), 'utf8'), /\bsettingsTemporary\(/, `${writer} writes the settings without settingsTemporary`);
    }
    for (const name of naming) {
        assert.doesNotMatch(readFileSync(join(sources, name), 'utf8'), /\.tmp[`'"]/, `${name} names the settings file and builds a temporary name of its own`);
    }
});
