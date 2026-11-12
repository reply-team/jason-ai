// Reads a built package back and holds it to the configuration it was built from: the updater's app-update.yml as
// electron-builder wrote it into the package, and the fuse wire of the binary Electron reads its fuses from.
//
//   node desktop/scripts/check-package.mjs --app <package> --platform win32|darwin|linux [--form deb|appimage]
//
// <package> is the folder holding Jason.exe on Windows and Jason.app on macOS, and on Linux the folder holding the
// program: what a deb installs as /opt/Jason, or what an AppImage's --appimage-extract writes as squashfs-root, which
// --form names. What was read goes to stdout; a refusal goes to stderr with exit 1, and arguments it cannot use with
// exit 2. It reads files only; it starts no process.
//
// On Linux the form decides which updater the package makes. electron-updater makes its deb updater wherever
// resources/package-type reads deb, and its AppImage updater where there is none (electron-updater 6.8.11, out/main.js,
// doLoadAutoUpdater), so a deb must carry the file saying deb and an AppImage must carry none. An AppImage's contents
// hold AppRun at their root and a deb's folder does not, and the menu entry an AppImage carries starts the program
// without --no-sandbox.
//
// app-update.yml names the feed an installed package asks for updates: the provider, the owner and the repository. On
// Windows it also names the publisher an update must be signed by, and electron-updater checks no signature when that
// key is absent, so a Windows package must name the release's signer, and only it.
//
// The fuse wire follows a fixed sentinel in the binary: a version byte, a length byte, then one byte per fuse, '0' off,
// '1' on and 'r' removed. Every copy of the wire in the binary is read. A fuse the configuration does not set is said
// as Electron left it.

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { configuration, fuses, signer } from '../electron-builder.config.mjs';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The icon sizes, in pixels, a Linux package must install, each as the file desktop/installer/linux-icons holds. */
export const linuxIconSizes = Object.freeze([32, 128, 256, 512]);

/** Where a Linux package's file system begins: a deb's folder is /opt/Jason inside it, an AppImage's is its root. */
export function iconRoot(app, form) {
    return form === 'deb' ? dirname(dirname(app)) : app;
}

/** The icon sizes a menu and a dock look for: each installed under hicolor, and byte-equal to the tree's file. */
function checkIcons(app, form) {
    const said = [];
    for (const size of linuxIconSizes) {
        const name = `${size}x${size}`;
        const installed = join(iconRoot(app, form), 'usr', 'share', 'icons', 'hicolor', name, 'apps', `${built.linux.executableName}.png`);
        if (!existsSync(installed)) throw new Error(`The package has no icon at ${name}: ${installed} is not there.`);
        const held = readFileSync(join(desktop, 'installer', 'linux-icons', `${name}.png`));
        if (!held.equals(readFileSync(installed))) throw new Error(`The icon at ${name} is not the file the tree holds: ${installed} differs from installer/linux-icons/${name}.png.`);
        said.push(`icon ${name}: the file the tree holds`);
    }
    return said;
}

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

/** The binary Electron reads its fuses from: the program on Windows and Linux, the Electron framework inside the .app on macOS. */
export function fuseFile(app, platform) {
    if (platform === 'darwin') return join(app, 'Contents', 'Frameworks', 'Electron Framework.framework', 'Electron Framework');
    if (platform === 'linux') return join(app, built.linux.executableName);
    return join(app, `${built.productName}.exe`);
}

/**
 * Where electron-builder writes app-update.yml: the resources folder beside the program, or inside the .app. On Linux
 * both forms write it into the folder they are made from (app-builder-lib's AppImageTarget.js and FpmTarget.js, build).
 */
export function updateFile(app, platform) {
    if (platform === 'darwin') return join(app, 'Contents', 'Resources', 'app-update.yml');
    return join(app, 'resources', 'app-update.yml');
}

/** The scheme a jason link carries, which the macOS bundle and the deb's menu entry declare, and the AppImage does not. */
const linkScheme = 'jason';
const schemeType = `x-scheme-handler/${linkScheme}`;

/** The macOS bundle declares the scheme in Info.plist, so the system hands a link to the application by open-url. */
function checkMacScheme(app) {
    const file = join(app, 'Contents', 'Info.plist');
    if (!existsSync(file)) throw new Error(`${file} is not there, so no link scheme can be read.`);
    const plist = readFileSync(file, 'utf8');
    const declared = /<key>CFBundleURLSchemes<\/key>\s*<array>([\s\S]*?)<\/array>/.exec(plist);
    if (declared === null || !new RegExp(`<string>${linkScheme}</string>`).test(declared[1])) throw new Error(`${file} declares no CFBundleURLSchemes holding ${linkScheme}, so jason links would not reach the application.`);
    return [`Info.plist: CFBundleURLSchemes holds ${linkScheme}`];
}

/** The deb's menu entry takes jason links: it declares the scheme's type and starts the program with the address. */
function checkDebScheme(app) {
    const folder = join(iconRoot(app, 'deb'), 'usr', 'share', 'applications');
    const entries = existsSync(folder) ? readdirSync(folder).filter((name) => name.endsWith('.desktop')).sort() : [];
    if (entries.length === 0) throw new Error(`${folder} holds no .desktop menu entry, so no link scheme can be read.`);
    const said = [];
    for (const entry of entries) {
        const lines = readFileSync(join(folder, entry), 'utf8').split(/\r?\n/);
        const mime = lines.find((line) => line.startsWith('MimeType='));
        const exec = lines.find((line) => line.startsWith('Exec='));
        if (mime === undefined || !mime.slice('MimeType='.length).split(';').includes(schemeType)) throw new Error(`${entry} declares no MimeType ${schemeType}, so the deb would take no jason link.`);
        if (exec === undefined || !/\s%U$/.test(exec)) throw new Error(`${entry} does not start the program with the address (%U): ${exec ?? 'no Exec line'}`);
        said.push(`${entry}: ${mime}`);
    }
    return said;
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

/** Which updater a Linux package makes, and that it is the form it says it is. */
function checkForm(app, form) {
    const said = [];
    const typed = join(app, 'resources', 'package-type');
    const type = existsSync(typed) ? readFileSync(typed, 'utf8').trim() : null;
    const appRun = existsSync(join(app, 'AppRun'));
    if (form === 'deb') {
        if (appRun) throw new Error(`${app} holds AppRun; it is an AppImage's contents, not a deb's folder.`);
        if (type === null) throw new Error(`${app} holds no resources/package-type, so electron-updater would make its AppImage updater for a deb.`);
        if (type !== 'deb') throw new Error(`${typed}: package-type says ${type}, and a deb's must say deb.`);
        said.push('package-type: deb, so electron-updater makes its deb updater');
        // The deb's sandbox where the system restricts user namespaces (Ubuntu 24.04 and later): its after-install copies
        // this profile into /etc/apparmor.d where AppArmor reads it (app-builder-lib 26.18.0,
        // templates/linux/after-install.tpl), and the profile lets the program make user namespaces
        // (templates/linux/apparmor-profile.tpl: `profile "${executable}" "/opt/${sanitizedProductName}/${executable}"
        // flags=(unconfined) { userns, ... }`).
        const profiled = join(app, 'resources', 'apparmor-profile');
        if (!existsSync(profiled)) throw new Error(`${app} holds no resources/apparmor-profile, so the deb installs no profile for its sandbox.`);
        const program = `/opt/${built.productName}/${built.linux.executableName}`;
        const profile = readFileSync(profiled, 'utf8');
        if (!profile.includes(`"${program}"`) || !/^\s*userns,\s*$/m.test(profile)) {
            throw new Error(`resources/apparmor-profile does not let ${program} make user namespaces, which its sandbox uses.`);
        }
        said.push(`apparmor-profile: userns for ${program}`);
        return [...said, ...checkDebScheme(app), ...checkIcons(app, form)];
    }
    if (!appRun) throw new Error(`${app} holds no AppRun; it is not an AppImage's contents.`);
    if (type !== null) throw new Error(`The AppImage carries resources/package-type saying ${type}, so electron-updater would update it as a ${type}.`);
    said.push('package-type: none, so electron-updater makes its AppImage updater');
    const entries = readdirSync(app).filter((name) => name.endsWith('.desktop')).sort();
    if (entries.length === 0) throw new Error(`${app} holds no .desktop menu entry at its root, so there is no Exec to read.`);
    for (const entry of entries) {
        const exec = readFileSync(join(app, entry), 'utf8').split(/\r?\n/).find((line) => line.startsWith('Exec='));
        if (exec === undefined) throw new Error(`${entry} has no Exec line, so what the menu starts cannot be read.`);
        if (/(^|\s)--no-sandbox(\s|$)/.test(exec.slice('Exec='.length))) throw new Error(`${entry} starts the program with --no-sandbox: ${exec}`);
        if (readFileSync(join(app, entry), 'utf8').includes(schemeType)) throw new Error(`${entry} declares ${schemeType}, and the AppImage takes no jason link.`);
        said.push(`${entry}: ${exec}`);
    }
    return [...said, ...checkIcons(app, form)];
}

/**
 * What the package says, line by line; throws when it is not what it was configured to be. `form` is a Linux package's,
 * deb or appimage, and only a Linux package has one.
 */
export function checkPackage(app, platform, form) {
    if (platform === 'linux' && form !== 'deb' && form !== 'appimage') throw new Error(`A Linux package is a deb or an AppImage: --form deb|appimage, not ${String(form)}.`);
    if (platform !== 'linux' && form !== undefined) throw new Error(`Only a Linux package has a form, and this one is ${platform}'s.`);
    return [checkUpdater(app, platform), ...checkFuses(app, platform), ...(platform === 'linux' ? checkForm(app, form) : []), ...(platform === 'darwin' ? checkMacScheme(app) : [])];
}

function main(argv) {
    const app = argv[argv.indexOf('--app') + 1];
    const platform = argv[argv.indexOf('--platform') + 1];
    const form = argv.includes('--form') ? argv[argv.indexOf('--form') + 1] : undefined;
    const known = platform === 'win32' || platform === 'darwin' ? form === undefined : platform === 'linux' && (form === 'deb' || form === 'appimage');
    if (!argv.includes('--app') || !argv.includes('--platform') || app === undefined || !known) {
        process.stderr.write('usage: node check-package.mjs --app <package> --platform win32|darwin|linux [--form deb|appimage]\n');
        return 2;
    }
    try {
        for (const line of checkPackage(app, platform, form)) process.stdout.write(`${line}\n`);
        return 0;
    } catch (error) {
        process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
        return 1;
    }
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = main(process.argv.slice(2));
