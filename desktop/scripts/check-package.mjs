// Reads a built package back and holds it to the configuration it was built from: the updater's app-update.yml as
// electron-builder wrote it into the package, and the fuse wire of the binary Electron reads its fuses from.
//
//   node desktop/scripts/check-package.mjs --app <package> --platform win32|darwin
//
// <package> is the folder holding Jason.exe on Windows and Jason.app on macOS. What was read goes to stdout; a refusal
// goes to stderr with exit 1, and arguments it cannot use with exit 2. It reads files only; it starts no process.
//
// app-update.yml names the feed an installed package asks for updates: the provider, the owner and the repository. On
// Windows it also names the publisher an update must be signed by, and electron-updater checks no signature when that
// key is absent, so a Windows package must name the release's signer, and only it.
//
// The fuse wire follows a fixed sentinel in the binary: a version byte, a length byte, then one byte per fuse, '0' off,
// '1' on and 'r' removed. Every copy of the wire in the binary is read. A fuse the configuration does not set is said
// as Electron left it.

import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { configuration, fuses, signer } from '../electron-builder.config.mjs';

const sentinel = Buffer.from('dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX', 'ascii');

/** The fuses in the order of the wire, as @electron/fuses numbers them. */
export const wireOrder = Object.freeze([
    'runAsNode',
    'enableCookieEncryption',
    'enableNodeOptionsEnvironmentVariable',
    'enableNodeCliInspectArguments',
    'enableEmbeddedAsarIntegrityValidation',
    'onlyLoadAppFromAsar',
    'loadBrowserProcessSpecificV8Snapshot',
    'grantFileProtocolExtraPrivileges',
]);

const built = configuration({ version: '0.0.0' });

/** The binary Electron reads its fuses from: the program on Windows, the Electron framework inside the .app on macOS. */
export function fuseFile(app, platform) {
    if (platform === 'darwin') return join(app, 'Contents', 'Frameworks', 'Electron Framework.framework', 'Electron Framework');
    return join(app, `${built.productName}.exe`);
}

/** Where electron-builder writes app-update.yml: the resources folder beside the program, or inside the .app. */
export function updateFile(app, platform) {
    if (platform === 'darwin') return join(app, 'Contents', 'Resources', 'app-update.yml');
    return join(app, 'resources', 'app-update.yml');
}

const stateOf = (byte) => (byte === 0x30 ? 'disabled' : byte === 0x31 ? 'enabled' : byte === 0x72 ? 'removed' : `the byte ${byte}`);

/** The keys of app-update.yml: each `key: value` line, and the items of a list under `key:`. */
function readUpdater(text) {
    const values = new Map();
    let list = null;
    for (const line of text.split(/\r?\n/)) {
        const item = /^\s+-\s+(.*)$/.exec(line);
        if (item !== null && list !== null) {
            list.push(item[1].trim());
            continue;
        }
        const pair = /^([A-Za-z]+):\s*(.*)$/.exec(line);
        if (pair === null) {
            list = null;
            continue;
        }
        if (pair[2].trim() === '') {
            list = [];
            values.set(pair[1], list);
        } else {
            list = null;
            values.set(pair[1], pair[2].trim());
        }
    }
    return values;
}

function checkUpdater(app, platform) {
    const file = updateFile(app, platform);
    if (!existsSync(file)) throw new Error(`${app} holds no app-update.yml at ${file}; the package would ask no feed for updates.`);
    const values = readUpdater(readFileSync(file, 'utf8'));
    const feed = built.publish[0];
    const said = [];
    for (const key of ['provider', 'owner', 'repo']) {
        const value = values.get(key);
        if (value !== feed[key]) {
            throw new Error(`app-update.yml says ${key} ${typeof value === 'string' ? value : 'nothing'}, and the package is built with ${key} ${feed[key]}.`);
        }
        said.push(`${key} ${value}`);
    }
    if (platform === 'win32') {
        const named = values.get('publisherName');
        const publishers = Array.isArray(named) ? named : typeof named === 'string' ? [named] : [];
        if (publishers.length !== 1 || publishers[0] !== signer) {
            throw new Error(`app-update.yml names the publishers ${publishers.length > 0 ? publishers.join(', ') : 'nothing'}, and an update must be signed by ${signer} alone.`);
        }
        said.push(`publisher ${signer}`);
    }
    return `app-update.yml: ${said.join(', ')}`;
}

function checkFuses(app, platform) {
    const file = fuseFile(app, platform);
    if (!existsSync(file)) throw new Error(`${app} holds no ${file}.`);
    const bytes = readFileSync(file);
    const wires = [];
    for (let at = bytes.indexOf(sentinel); at !== -1; at = bytes.indexOf(sentinel, at + 1)) wires.push(at + sentinel.length);
    if (wires.length === 0) throw new Error(`${file} holds no fuse wire.`);
    const said = [];
    for (const [copy, position] of wires.entries()) {
        const version = bytes[position];
        const length = bytes[position + 1] ?? 0;
        if (version !== 1) throw new Error(`${file}'s fuse wire is of version ${version}, and this check reads version 1.`);
        const beyond = wireOrder.findLastIndex((name) => name in fuses);
        if (beyond >= length) throw new Error(`${file} holds ${length} fuses, and the configuration sets ${wireOrder[beyond]}, fuse ${beyond}.`);
        for (const [index, name] of wireOrder.entries()) {
            if (!(name in fuses)) continue;
            const state = stateOf(bytes[position + 2 + index]);
            const wanted = fuses[name] ? 'enabled' : 'disabled';
            if (state !== wanted) throw new Error(`In ${file}, fuse ${name} is ${state}, and the configuration says ${wanted}.`);
            if (copy === 0) said.push(`fuse ${name}: ${state}`);
        }
        for (let index = 0; index < length; index++) {
            const name = wireOrder[index];
            if (name !== undefined && name in fuses) continue;
            if (copy === 0) said.push(`fuse ${name ?? index}: ${stateOf(bytes[position + 2 + index])}, as Electron left it`);
        }
    }
    said.push(`${wires.length} ${wires.length === 1 ? 'copy' : 'copies'} of the fuse wire read in ${file}`);
    return said;
}

/** What the package says, line by line; throws when it is not what it was configured to be. */
export function checkPackage(app, platform) {
    return [checkUpdater(app, platform), ...checkFuses(app, platform)];
}

function main(argv) {
    const app = argv[argv.indexOf('--app') + 1];
    const platform = argv[argv.indexOf('--platform') + 1];
    if (!argv.includes('--app') || !argv.includes('--platform') || app === undefined || (platform !== 'win32' && platform !== 'darwin')) {
        process.stderr.write('usage: node check-package.mjs --app <package> --platform win32|darwin\n');
        return 2;
    }
    try {
        for (const line of checkPackage(app, platform)) process.stdout.write(`${line}\n`);
        return 0;
    } catch (error) {
        process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
        return 1;
    }
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = main(process.argv.slice(2));
