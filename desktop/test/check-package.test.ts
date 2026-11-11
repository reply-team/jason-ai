// How scripts/check-package.mjs reads a built package back: the updater's app-update.yml as electron-builder wrote it
// into the package, the fuse wire of the binary Electron reads its fuses from, and on Linux which updater the package
// makes, each held to the configuration the package was built from. The packages here are made up: a folder or a .app
// holding those files, the fuse wire written by the same library electron-builder flips the fuses with.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { fuses, signer } from '../electron-builder.config.mjs';
import { checkPackage, fuseFile, iconRoot, linuxIconSizes, updateFile, wireOrder, type Form, type Platform } from '../scripts/check-package.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const library = createRequire(join(root, 'package.json'))('@electron/fuses') as {
    FuseV1Options: Record<number, string>;
    FuseVersion: { V1: string };
    flipFuses(path: string, config: Record<string, unknown>): Promise<number>;
};
const sentinel = 'dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX';

/** A package as one of the four kinds a release builds: Windows', a Mac's, and Linux's deb and AppImage. */
type Kind = 'win32' | 'darwin' | 'linux-deb' | 'linux-appimage';
const platformOf = (kind: Kind): Platform => (kind.startsWith('linux') ? 'linux' : (kind as Platform));
const formOf = (kind: Kind): Form | undefined => (kind === 'linux-deb' ? 'deb' : kind === 'linux-appimage' ? 'appimage' : undefined);
const check = (app: string, kind: Kind) => checkPackage(app, platformOf(kind), formOf(kind));

test('the fuse library these rows write with is declared, pinned, and the one copy electron-builder flips the fuses with', () => {
    const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { devDependencies: Record<string, string> };
    const lock = JSON.parse(readFileSync(join(root, 'package-lock.json'), 'utf8')) as { packages: Record<string, { version?: string; devDependencies?: Record<string, string> }> };
    assert.equal(manifest.devDependencies['@electron/fuses'], '1.8.0');
    assert.equal(lock.packages['']?.devDependencies?.['@electron/fuses'], '1.8.0');
    assert.equal(lock.packages['node_modules/@electron/fuses']?.version, '1.8.0');
    assert.deepEqual(Object.keys(lock.packages).filter((path) => path.endsWith('node_modules/@electron/fuses')), ['node_modules/@electron/fuses']);
});

function temporary(t: { after(fn: () => void): void }): string {
    const folder = mkdtempSync(join(tmpdir(), 'jason-check-package-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    return folder;
}

/** A binary holding one fuse wire, or two, of `length` fuses, every one as Electron ships it: '1' or as `shipped` says. */
function binary(length: number, copies = 1, shipped: (index: number) => number = () => 0x31): Buffer {
    const wire = () => Buffer.concat([Buffer.from(sentinel, 'ascii'), Buffer.from([1, length]), Buffer.from(Array.from({ length }, (_, i) => shipped(i)))]);
    const parts = [Buffer.from('MZ program bytes before the wire ')];
    for (let copy = 0; copy < copies; copy++) parts.push(wire(), Buffer.from(' more program bytes '));
    return Buffer.concat(parts);
}

const updaterFile = (platform: Kind) => [
    'owner: reply-team',
    'repo: jason-ai',
    'provider: github',
    'releaseType: release',
    'updaterCacheDirName: jason-desktop-updater',
    ...(platform === 'win32' ? ['publisherName:', `  - ${signer}`] : []),
    '',
].join('\n');

/** The AppArmor profile a deb carries, as app-builder-lib's apparmor-profile.tpl writes it for this program. */
const appArmorProfile = ['abi <abi/4.0>,', 'include <tunables/global>', '', 'profile "jason-desktop-app" "/opt/Jason/jason-desktop-app" flags=(unconfined) {', '  userns,', '', '  include if exists <local/jason-desktop-app>', '}', ''].join('\n');

/** The menu entry an AppImage carries at its root, as electron-builder writes it for the static runtime. */
const menuEntry = (exec: string) => ['[Desktop Entry]', 'Name=Jason', `Exec=${exec}`, 'Terminal=false', 'Type=Application', 'Icon=jason-desktop-app', ''].join('\n');

/**
 * A package as electron-builder lays it out, with its fuses flipped as the configuration says, by the library: a deb's
 * folder, /opt/Jason, carries package-type reading deb; an AppImage's, as --appimage-extract writes it, carries AppRun
 * and the menu entry beside the program, and no package-type.
 */
async function made(
    t: { after(fn: () => void): void },
    platform: Kind,
    options: { length?: number; copies?: number; updater?: string | null; packageType?: string | null; appRun?: boolean; exec?: string | null; profile?: string | null; icons?: number[] } = {},
): Promise<string> {
    const folder = temporary(t);
    const app = platform === 'win32' ? join(folder, 'win-unpacked') : platform === 'darwin' ? join(folder, 'mac-arm64', 'Jason.app') : join(folder, platform === 'linux-deb' ? join('opt', 'Jason') : 'squashfs-root');
    const on = platformOf(platform);
    mkdirSync(dirname(fuseFile(app, on)), { recursive: true });
    mkdirSync(dirname(updateFile(app, on)), { recursive: true });
    writeFileSync(fuseFile(app, on), binary(options.length ?? 9, options.copies ?? 1));
    const config: Record<string, unknown> = { version: library.FuseVersion.V1 };
    for (const [name, value] of Object.entries(fuses)) config[String(wireOrder.indexOf(name))] = value;
    await library.flipFuses(on === 'darwin' ? join(app, 'Contents', 'MacOS', 'Jason') : fuseFile(app, on), config);
    const updater = options.updater === undefined ? updaterFile(platform) : options.updater;
    if (updater !== null) writeFileSync(updateFile(app, on), updater);
    const packageType = options.packageType === undefined ? (platform === 'linux-deb' ? 'deb\n' : null) : options.packageType;
    if (packageType !== null) writeFileSync(join(app, 'resources', 'package-type'), packageType);
    if (options.appRun ?? platform === 'linux-appimage') writeFileSync(join(app, 'AppRun'), '#!/usr/bin/env bash\n');
    if (platform === 'linux-appimage' && options.exec !== null) writeFileSync(join(app, 'jason-desktop-app.desktop'), menuEntry(options.exec ?? 'AppRun %U'));
    if (platform === 'linux-deb' && options.profile !== null) writeFileSync(join(app, 'resources', 'apparmor-profile'), options.profile ?? appArmorProfile);
    if (platform === 'linux-deb' || platform === 'linux-appimage') {
        for (const size of options.icons ?? linuxIconSizes) {
            const target = join(iconRoot(app, formOf(platform) as Form), 'usr', 'share', 'icons', 'hicolor', `${size}x${size}`, 'apps', 'jason-desktop-app.png');
            mkdirSync(dirname(target), { recursive: true });
            copyFileSync(join(root, 'installer', 'linux-icons', `${size}x${size}.png`), target);
        }
    }
    return app;
}

/** Sets one byte of every fuse wire in the package's binary. */
function setFuse(app: string, kind: Kind, index: number, state: number, which: 'every' | 'last' = 'every'): void {
    const file = fuseFile(app, platformOf(kind));
    const bytes = readFileSync(file);
    let at = bytes.indexOf(sentinel);
    const found: number[] = [];
    while (at !== -1) {
        found.push(at);
        at = bytes.indexOf(sentinel, at + 1);
    }
    for (const position of which === 'every' ? found : found.slice(-1)) bytes[position + sentinel.length + 2 + index] = state;
    writeFileSync(file, bytes);
}

test('the wire order is the fuse library\'s, and every fuse the configuration sets has its place in it', () => {
    assert.deepEqual(wireOrder, Object.keys(library.FuseV1Options)
        .filter((key) => /^\d+$/.test(key))
        .map((key) => library.FuseV1Options[Number(key)] ?? '')
        .map((name) => name.charAt(0).toLowerCase() + name.slice(1)));
    for (const name of Object.keys(fuses)) assert.ok(wireOrder.includes(name), name);
});

test('the binary Electron reads its fuses from: the program beside resources on Windows and Linux, the framework inside the .app on macOS', () => {
    assert.equal(fuseFile(join('D:', 'app'), 'win32'), join('D:', 'app', 'Jason.exe'));
    assert.equal(fuseFile(join('opt', 'Jason'), 'linux'), join('opt', 'Jason', 'jason-desktop-app'));
    assert.equal(updateFile(join('opt', 'Jason'), 'linux'), join('opt', 'Jason', 'resources', 'app-update.yml'));
    assert.equal(updateFile(join('D:', 'app'), 'win32'), join('D:', 'app', 'resources', 'app-update.yml'));
    assert.equal(fuseFile(join('dist', 'mac', 'Jason.app'), 'darwin'), join('dist', 'mac', 'Jason.app', 'Contents', 'Frameworks', 'Electron Framework.framework', 'Electron Framework'));
    assert.equal(updateFile(join('dist', 'mac', 'Jason.app'), 'darwin'), join('dist', 'mac', 'Jason.app', 'Contents', 'Resources', 'app-update.yml'));
});

for (const platform of ['win32', 'darwin', 'linux-deb', 'linux-appimage'] as const) {
    test(`${platform}: a package built as configured is accepted, and what was read is said`, async (t) => {
        const app = await made(t, platform);
        const said = check(app, platform);
        assert.ok(said.includes('app-update.yml: provider github, owner reply-team, repo jason-ai' + (platform === 'win32' ? `, publisher ${signer}` : '')), said.join('\n'));
        assert.ok(said.includes('fuse runAsNode: disabled'), said.join('\n'));
        assert.ok(said.includes('fuse onlyLoadAppFromAsar: enabled'), said.join('\n'));
        assert.ok(said.includes('fuse 8: enabled, as Electron left it'), said.join('\n'));
        if (platform === 'linux-deb') assert.ok(said.includes('package-type: deb, so electron-updater makes its deb updater'), said.join('\n'));
        if (platform === 'linux-appimage') {
            assert.ok(said.includes('package-type: none, so electron-updater makes its AppImage updater'), said.join('\n'));
            assert.ok(said.includes('jason-desktop-app.desktop: Exec=AppRun %U'), said.join('\n'));
        }
    });

    for (const [index, name] of wireOrder.entries()) {
        test(`${platform}: a package whose ${name} fuse is not as configured is refused`, async (t) => {
            const app = await made(t, platform);
            const wanted = (fuses as Record<string, boolean>)[name];
            setFuse(app, platform, index, wanted ? 0x30 : 0x31);
            assert.throws(() => check(app, platform), new RegExp(`fuse ${name} is ${wanted ? 'disabled' : 'enabled'}, and the configuration says ${wanted ? 'enabled' : 'disabled'}`));
        });
    }

    test(`${platform}: a fuse removed from the binary is refused`, async (t) => {
        const app = await made(t, platform);
        setFuse(app, platform, wireOrder.indexOf('runAsNode'), 'r'.charCodeAt(0));
        assert.throws(() => check(app, platform), /fuse runAsNode is removed, and the configuration says disabled/);
    });

    test(`${platform}: every copy of the wire is read, the last as the first`, async (t) => {
        const app = await made(t, platform, { copies: 2 });
        assert.doesNotThrow(() => check(app, platform));
        setFuse(app, platform, wireOrder.indexOf('enableNodeOptionsEnvironmentVariable'), 0x31, 'last');
        assert.throws(() => check(app, platform), /fuse enableNodeOptionsEnvironmentVariable is enabled, and the configuration says disabled/);
    });

    test(`${platform}: a binary with no fuse wire, or one too short for the configuration, is refused`, async (t) => {
        const app = await made(t, platform);
        writeFileSync(fuseFile(app, platformOf(platform)), Buffer.from('no wire here'));
        assert.throws(() => check(app, platform), /holds no fuse wire/);
        writeFileSync(fuseFile(app, platformOf(platform)), binary(7));
        assert.throws(() => check(app, platform), /holds 7 fuses, and the configuration sets grantFileProtocolExtraPrivileges/);
    });

    test(`${platform}: a package without app-update.yml is refused`, async (t) => {
        const app = await made(t, platform, { updater: null });
        assert.throws(() => check(app, platform), /holds no app-update\.yml/);
    });

    for (const [key, other] of [['provider', 'generic'], ['owner', 'someone-else'], ['repo', 'another-repository']] as const) {
        test(`${platform}: an app-update.yml naming another ${key} is refused`, async (t) => {
            const app = await made(t, platform, { updater: updaterFile(platform).replace(new RegExp(`^${key}: .*$`, 'm'), `${key}: ${other}`) });
            assert.throws(() => check(app, platform), new RegExp(`app-update\\.yml says ${key} ${other}, and the package is built with ${key} `));
        });

        test(`${platform}: an app-update.yml without its ${key} is refused`, async (t) => {
            const app = await made(t, platform, { updater: updaterFile(platform).replace(new RegExp(`^${key}: .*\\n`, 'm'), '') });
            assert.throws(() => check(app, platform), new RegExp(`app-update\\.yml says ${key} nothing, and the package is built with ${key} `));
        });
    }
}

test('win32: an app-update.yml without the publisher an update must carry is refused, since the updater would check none', async (t) => {
    const app = await made(t, 'win32', { updater: updaterFile('darwin') });
    assert.throws(() => check(app, 'win32'), new RegExp(`app-update\\.yml names the publishers nothing, and an update must be signed by ${signer} alone`));
});

test('win32: an app-update.yml naming another publisher, or one more, is refused', async (t) => {
    const other = await made(t, 'win32', { updater: updaterFile('win32').replace(signer, 'Someone Else Ltd.') });
    assert.throws(() => check(other, 'win32'), /names the publishers Someone Else Ltd\./);
    const more = await made(t, 'win32', { updater: `${updaterFile('win32')}  - Someone Else Ltd.\n` });
    assert.throws(() => check(more, 'win32'), new RegExp(`names the publishers ${signer}, Someone Else Ltd\\.`));
});

test('darwin: a publisher in a Mac package\'s app-update.yml is not read: the Mac updater holds an update to the signature of the application it replaces', async (t) => {
    const app = await made(t, 'darwin', { updater: updaterFile('win32') });
    assert.doesNotThrow(() => check(app, 'darwin'));
});

test('linux: a deb without package-type, or with one naming another package, is refused, since its updater would not be the deb\'s', async (t) => {
    const none = await made(t, 'linux-deb', { packageType: null });
    assert.throws(() => check(none, 'linux-deb'), /holds no resources\/package-type, so electron-updater would make its AppImage updater for a deb/);
    const rpm = await made(t, 'linux-deb', { packageType: 'rpm\n' });
    assert.throws(() => check(rpm, 'linux-deb'), /package-type says rpm, and a deb's must say deb/);
});

test('linux: an AppImage carrying package-type is refused, since electron-updater would update it as that package', async (t) => {
    const app = await made(t, 'linux-appimage', { packageType: 'deb\n' });
    assert.throws(() => check(app, 'linux-appimage'), /The AppImage carries resources\/package-type saying deb, so electron-updater would update it as a deb/);
});

test('linux: each form is what it says: an AppImage has AppRun at its root, and a deb\'s folder has none', async (t) => {
    const flat = await made(t, 'linux-appimage', { appRun: false });
    assert.throws(() => check(flat, 'linux-appimage'), /holds no AppRun; it is not an AppImage's contents/);
    const deb = await made(t, 'linux-deb', { appRun: true });
    assert.throws(() => check(deb, 'linux-deb'), /holds AppRun; it is an AppImage's contents, not a deb's folder/);
});

test('linux: an AppImage whose menu entry starts the program with --no-sandbox is refused', async (t) => {
    const app = await made(t, 'linux-appimage', { exec: 'AppRun --no-sandbox %U' });
    assert.throws(() => check(app, 'linux-appimage'), /jason-desktop-app\.desktop starts the program with --no-sandbox/);
});

test('linux: an AppImage whose menu entry has no Exec line is refused, rather than read as an empty one', async (t) => {
    const app = await made(t, 'linux-appimage');
    writeFileSync(join(app, 'jason-desktop-app.desktop'), ['[Desktop Entry]', 'Name=Jason', 'Type=Application', ''].join('\n'));
    assert.throws(() => check(app, 'linux-appimage'), /jason-desktop-app\.desktop has no Exec line/);
});

test('linux: an AppImage with no menu entry at its root is refused, since there would be no Exec to read', async (t) => {
    const app = await made(t, 'linux-appimage', { exec: null });
    assert.throws(() => check(app, 'linux-appimage'), /holds no \.desktop menu entry at its root/);
});

test('linux: a deb carries the AppArmor profile that lets its program make user namespaces, which its sandbox uses', async (t) => {
    const app = await made(t, 'linux-deb');
    assert.ok(check(app, 'linux-deb').includes('apparmor-profile: userns for /opt/Jason/jason-desktop-app'));
    const none = await made(t, 'linux-deb', { profile: null });
    assert.throws(() => check(none, 'linux-deb'), /holds no resources\/apparmor-profile/);
    const other = await made(t, 'linux-deb', { profile: appArmorProfile.replace('/opt/Jason/jason-desktop-app', '/opt/Jason/jason-desktop') });
    assert.throws(() => check(other, 'linux-deb'), /apparmor-profile does not let \/opt\/Jason\/jason-desktop-app make user namespaces/);
    const without = await made(t, 'linux-deb', { profile: appArmorProfile.replace('  userns,\n', '') });
    assert.throws(() => check(without, 'linux-deb'), /apparmor-profile does not let \/opt\/Jason\/jason-desktop-app make user namespaces/);
});

test('linux: the form is required and is one of deb and appimage, and only Linux has one', async (t) => {
    const app = await made(t, 'linux-deb');
    assert.throws(() => checkPackage(app, 'linux'), /A Linux package is a deb or an AppImage/);
    assert.throws(() => checkPackage(app, 'linux', 'rpm' as Form), /A Linux package is a deb or an AppImage/);
    assert.throws(() => checkPackage(app, 'win32', 'deb'), /Only a Linux package has a form/);
});

test('run as a program: what was read on stdout and exit 0, a refusal on stderr and exit 1, an unknown platform exit 2', async (t) => {
    const app = await made(t, 'win32');
    const script = join(root, 'scripts', 'check-package.mjs');
    const accepted = spawnSync(process.execPath, [script, '--app', app, '--platform', 'win32'], { encoding: 'utf8' });
    assert.equal(accepted.status, 0, accepted.stderr);
    assert.match(accepted.stdout, /fuse runAsNode: disabled/);

    setFuse(app, 'win32', wireOrder.indexOf('runAsNode'), 0x31);
    const refused = spawnSync(process.execPath, [script, '--app', app, '--platform', 'win32'], { encoding: 'utf8' });
    assert.equal(refused.status, 1);
    assert.match(refused.stderr, /fuse runAsNode is enabled, and the configuration says disabled/);

    for (const args of [['--platform', 'freebsd'], ['--platform', 'linux'], ['--platform', 'linux', '--form', 'rpm'], ['--platform', 'win32', '--form', 'deb']]) {
        const usage = spawnSync(process.execPath, [script, '--app', app, ...args], { encoding: 'utf8' });
        assert.equal(usage.status, 2, args.join(' '));
        assert.match(usage.stderr, /--app <package> --platform win32\|darwin\|linux \[--form deb\|appimage\]/);
    }

    const deb = await made(t, 'linux-deb');
    const read = spawnSync(process.execPath, [script, '--app', deb, '--platform', 'linux', '--form', 'deb'], { encoding: 'utf8' });
    assert.equal(read.status, 0, read.stderr);
    assert.match(read.stdout, /package-type: deb/);
});

for (const kind of ['linux-deb', 'linux-appimage'] as const) {
    test(`${kind}: the icon is installed at 32, 128, 256 and 512 px, each the file the tree holds, and what was read is said`, async (t) => {
        const said = check(await made(t, kind), kind);
        for (const size of linuxIconSizes) assert.ok(said.includes(`icon ${size}x${size}: the file the tree holds`), said.join('\n'));
    });

    test(`${kind}: a package missing one of the required icon sizes is refused`, async (t) => {
        for (const size of linuxIconSizes) {
            const app = await made(t, kind, { icons: linuxIconSizes.filter((other) => other !== size) });
            assert.throws(() => check(app, kind), new RegExp(`no icon at ${size}x${size}`));
        }
    });

    test(`${kind}: an icon that is not the file the tree holds is refused`, async (t) => {
        const app = await made(t, kind);
        writeFileSync(join(iconRoot(app, formOf(kind) as Form), 'usr', 'share', 'icons', 'hicolor', '128x128', 'apps', 'jason-desktop-app.png'), 'not the icon');
        assert.throws(() => check(app, kind), /icon at 128x128 is not the file the tree holds/);
    });
}

test('linux: the sizes a package must carry are 32, 128, 256 and 512', () => {
    assert.deepEqual([...linuxIconSizes], [32, 128, 256, 512]);
});
