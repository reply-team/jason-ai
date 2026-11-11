// The application's mark: where its files are, that the starting page shows it and moves it only where motion is
// welcome, and that the mark it replaced is no file of this tree.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { configuration } from '../electron-builder.config.mjs';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const repository = join(desktop, '..');
const read = (...parts: string[]): string => readFileSync(join(desktop, ...parts), 'utf8');

function pngSize(file: string): [number, number] {
    const bytes = readFileSync(join(desktop, file));
    assert.equal(bytes.subarray(1, 4).toString('latin1'), 'PNG', `${file} is a PNG`);
    return [bytes.readUInt32BE(16), bytes.readUInt32BE(20)];
}

test('the application icon is the mark at 1024 px, with an icon set for the Mac, in every platform’s configuration', () => {
    assert.deepEqual(pngSize('resources/icon.png'), [1024, 1024]);
    assert.equal(readFileSync(join(desktop, 'resources', 'icon.icns')).subarray(0, 4).toString('latin1'), 'icns');
    const built = configuration({ version: '0.35.0' });
    assert.equal(built.win.icon, 'resources/icon.png');
    assert.equal(built.mac.icon, 'resources/icon.icns');
});

test('the tray has a template image for the menu bar and a coloured mark for the other systems', () => {
    assert.deepEqual(pngSize('resources/tray/trayTemplate.png'), [22, 22]);
    assert.deepEqual(pngSize('resources/tray/trayTemplate@2x.png'), [44, 44]);
    assert.deepEqual(pngSize('resources/tray/tray.png'), [64, 64]);
    assert.match(read('src', 'desk.ts'), /resource\('tray', 'tray\.png'\)/);
});

test('the wordmark and the mark are images the starting page carries, for a light and a dark ground', () => {
    for (const file of ['brand/wordmark.svg', 'brand/wordmark-dark.svg']) {
        const text = read('shell', file);
        assert.match(text, /^<svg /, file);
        assert.doesNotMatch(text, /<script|<style|href=|<image|onload/i, `${file} is plain paths`);
    }
    const page = read('shell', 'starting.html');
    assert.match(page, /<title>Jason<\/title>/);
    assert.match(page, /src="brand\/wordmark\.svg"/);
    assert.match(page, /srcset="brand\/wordmark-dark\.svg"[^>]*prefers-color-scheme: dark/);
    assert.match(page, /class="splash-mark"/);
    // The mark is drawn inline so that it can turn; its paths are the ones of the icon the console's favicon is.
    for (const [, d] of readFileSync(join(repository, 'ui', 'public', 'favicon.svg'), 'utf8').matchAll(/ d="([^"]+)"/g)) assert.ok(page.includes(d!), 'the page draws the mark');
    assert.doesNotMatch(page, /<h1>/, 'the name is the image, with the product’s name in the title and the label');
    assert.match(page, /aria-label="Starting Jason"/);
});

test('the mark turns once and the wordmark and the line fade in, by CSS alone, and neither moves under reduced motion', () => {
    const css = read('shell', 'shell.css');
    assert.match(css, /@keyframes [a-z-]+\s*\{[^}]*rotate/);
    assert.match(css, /\.splash-orbit\s*\{[^}]*animation:[^;]*splash-turn[^;]* 1 /, 'the mark turns once');
    const reduced = /@media \(prefers-reduced-motion: reduce\)\s*\{([^]*?)\n\}/.exec(css);
    assert.ok(reduced, 'a reduced-motion block');
    assert.match(reduced[1]!, /animation:\s*none/);
    assert.doesNotMatch(read('shell', 'starting.html'), /<script[^>]*>[^<]+<\/script>/);
});

/** The sha256 of the files of the mark this one replaced: none of them is in any folder of the tree. */
const replaced = new Set([
    '7dbb307ead8c7a3b34919d104989482c35418379a83ea1f9eb02cc503b072bbe',
    'e650dabffb46eeed19cf87b3e406064690d2337bdedc841347478a8e6ccfa165',
    '25a0684e4a6741bb9acba8d32772f164b9c84f6ccc94aea522b223ca8a880d7d',
    'e125622fd886b6750765b1819f90a0a1c57f7dfeeec4881aba645d5b01c00fa8',
]);

function* files(folder: string): Generator<string> {
    for (const name of readdirSync(folder)) {
        if (['node_modules', 'dist', 'bundles', 'lib', '.git', 'obj', 'bin'].includes(name)) continue;
        const path = join(folder, name);
        if (statSync(path).isDirectory()) yield* files(path);
        else yield path;
    }
}

test('no file of the mark it replaced is left in the tree, and no stylesheet draws the square that stood for it', () => {
    const found: string[] = [];
    for (const folder of ['desktop', 'ui', 'docs', 'install', 'plugins', 'examples']) {
        const at = join(repository, folder);
        if (!existsSync(at)) continue;
        for (const file of files(at)) {
            const bytes = readFileSync(file);
            if (replaced.has(createHash('sha256').update(bytes).digest('hex'))) found.push(file);
            if (file.endsWith('.css') && /\.glyph\s*\{[^}]*border-radius:\s*[56]px [56]px/.test(bytes.toString('utf8'))) found.push(`${file} draws the old square`);
        }
    }
    assert.deepEqual(found, []);
});
