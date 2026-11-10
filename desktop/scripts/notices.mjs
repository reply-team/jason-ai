// Writes desktop/THIRD-PARTY-NOTICES: what the packaged desktop carries besides its own code. That is Electron, with
// the Chromium it is built on, and the runtime dependencies the lockfile installs (electron-updater and its closure),
// each with the licence text its own package ships.
//
//   node scripts/notices.mjs           writes the file
//   node scripts/notices.mjs --check   exits 1 when the file is not what the lockfile and node_modules give
//
// It reads files only; it starts no process.

import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** The MIT License's terms, for a package that declares MIT and ships no licence file of its own. */
const mitTerms = `Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.`;

function personOf(author) {
    if (typeof author === 'string') return author.replace(/\s*[<(].*$/, '').trim();
    return typeof author?.name === 'string' ? author.name.trim() : null;
}

function sourceOf(repository) {
    const url = typeof repository === 'string' ? repository : repository?.url;
    if (typeof url !== 'string') return null;
    if (/^[\w.-]+\/[\w.-]+$/.test(url)) return `https://github.com/${url}`;
    return url
        .replace(/^git\+/, '')
        .replace(/\.git$/, '')
        .replace(/^git:\/\//, 'https://')
        .replace(/^git@([^:]+):/, 'https://$1/')
        .replace(/^ssh:\/\/git@/, 'https://');
}

/**
 * The licence text a package ships, or, for one that declares MIT and ships none, the MIT terms with the author its
 * package.json names; no copyright line is made up for it.
 */
function licenceText(directory, manifest) {
    const file = readdirSync(directory).find((name) => /^(LICENSE|LICENCE)(\..*)?$/i.test(name));
    if (file) return readFileSync(join(directory, file), 'utf8').replace(/\r\n/g, '\n').trim();
    const author = personOf(manifest.author);
    if (manifest.license === 'MIT' && author) {
        return `The package ships no licence file. Its package.json declares MIT and names ${author} as its\nauthor; the MIT License's terms follow.\n\n${mitTerms}`;
    }
    throw new Error(`${manifest.name} is carried by the desktop, but ${directory} holds no licence file to carry with it`);
}

function section(name, version, licence, source, text) {
    const title = `${name} ${version}: ${licence}`;
    return [title, '-'.repeat(title.length), ...(source ? [source, ''] : []), text, ''].join('\n');
}

/** The notices, as the lockfile and node_modules under `root` give them. */
export function notices(root = desktop) {
    const lock = JSON.parse(readFileSync(join(root, 'package-lock.json'), 'utf8'));
    const carried = Object.entries(lock.packages)
        .filter(([path, entry]) => path.startsWith('node_modules/') && entry.dev !== true)
        .map(([path]) => {
            const directory = join(root, ...path.split('/'));
            const manifest = JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8'));
            return { name: manifest.name, version: manifest.version, licence: manifest.license ?? 'see the text', source: sourceOf(manifest.repository), text: licenceText(directory, manifest) };
        })
        .sort((a, b) => a.name.localeCompare(b.name, 'en') || a.version.localeCompare(b.version, 'en'));

    const electronDirectory = join(root, 'node_modules', 'electron');
    const electron = JSON.parse(readFileSync(join(electronDirectory, 'package.json'), 'utf8'));

    const lines = [
        'Third-party notices for the Jason desktop',
        '=========================================',
        '',
        'The Jason desktop is an Electron application. Besides its own code it carries,',
        'each under the licence named here:',
        '',
        '  1. Electron, and the Chromium it is built on;',
        '  2. the packages it depends on at run time, from its lockfile, each with the',
        '     licence text its own package ships.',
        '',
        'The jason executable the desktop carries is covered by the THIRD-PARTY-NOTICES',
        'at the root of the Jason repository, which every Jason release carries.',
        '',
        '',
        '1. Electron and Chromium',
        '========================',
        '',
        section('electron', electron.version, electron.license, sourceOf(electron.repository), licenceText(electronDirectory, electron)),
        'Chromium and the libraries it is built from are used under their own licences.',
        'Their texts are in LICENSES.chromium.html, which every Electron build carries',
        'beside its executable, and the packaged desktop with it.',
        '',
        '',
        '2. The packages',
        '===============',
        '',
        ...carried.map((entry) => section(entry.name, entry.version, entry.licence, entry.source, entry.text)),
    ];
    return `${lines.join('\n').trimEnd()}\n`;
}

const file = join(desktop, 'THIRD-PARTY-NOTICES');

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const text = notices();
    if (process.argv.includes('--check')) {
        let current = null;
        try {
            current = readFileSync(file, 'utf8');
        } catch {
            // Absent: not current.
        }
        if (current !== text) {
            process.stderr.write('desktop/THIRD-PARTY-NOTICES is not what the lockfile and node_modules give; run node scripts/notices.mjs.\n');
            process.exit(1);
        }
    } else {
        writeFileSync(file, text);
    }
}
