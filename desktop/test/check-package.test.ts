// How scripts/check-package.mjs reads a built package back: the updater's app-update.yml as electron-builder wrote it
// into the package, and the fuse wire of the binary Electron reads its fuses from, each held to the configuration the
// package was built from. The packages here are made up: a folder or a .app holding those two files, the fuse wire
// written by the same library electron-builder flips the fuses with.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { fuses, signer } from '../electron-builder.config.mjs';
import { checkPackage, fuseFile, updateFile, wireOrder } from '../scripts/check-package.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const library = createRequire(join(root, 'package.json'))('@electron/fuses') as {
    FuseV1Options: Record<number, string>;
    FuseVersion: { V1: string };
    flipFuses(path: string, config: Record<string, unknown>): Promise<number>;
};
const sentinel = 'dL7pKGdnNz796PbbjQWNKmHXBZaB9tsX';

type Platform = 'win32' | 'darwin';

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

const updaterFile = (platform: Platform) => [
    'owner: reply-team',
    'repo: jason-ai',
    'provider: github',
    'releaseType: release',
    'updaterCacheDirName: jason-desktop-updater',
    ...(platform === 'win32' ? ['publisherName:', `  - ${signer}`] : []),
    '',
].join('\n');

/** A package as electron-builder lays it out, with its fuses flipped as the configuration says, by the library. */
async function made(t: { after(fn: () => void): void }, platform: Platform, options: { length?: number; copies?: number; updater?: string | null } = {}): Promise<string> {
    const folder = temporary(t);
    const app = platform === 'win32' ? join(folder, 'win-unpacked') : join(folder, 'mac-arm64', 'Jason.app');
    mkdirSync(dirname(fuseFile(app, platform)), { recursive: true });
    mkdirSync(dirname(updateFile(app, platform)), { recursive: true });
    writeFileSync(fuseFile(app, platform), binary(options.length ?? 9, options.copies ?? 1));
    const config: Record<string, unknown> = { version: library.FuseVersion.V1 };
    for (const [name, value] of Object.entries(fuses)) config[String(wireOrder.indexOf(name))] = value;
    await library.flipFuses(platform === 'win32' ? fuseFile(app, platform) : join(app, 'Contents', 'MacOS', 'Jason'), config);
    const updater = options.updater === undefined ? updaterFile(platform) : options.updater;
    if (updater !== null) writeFileSync(updateFile(app, platform), updater);
    return app;
}

/** Sets one byte of every fuse wire in the package's binary. */
function setFuse(app: string, platform: Platform, index: number, state: number, which: 'every' | 'last' = 'every'): void {
    const file = fuseFile(app, platform);
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

test('the binary Electron reads its fuses from: Jason.exe beside resources on Windows, the framework inside the .app on macOS', () => {
    assert.equal(fuseFile(join('D:', 'app'), 'win32'), join('D:', 'app', 'Jason.exe'));
    assert.equal(updateFile(join('D:', 'app'), 'win32'), join('D:', 'app', 'resources', 'app-update.yml'));
    assert.equal(fuseFile(join('dist', 'mac', 'Jason.app'), 'darwin'), join('dist', 'mac', 'Jason.app', 'Contents', 'Frameworks', 'Electron Framework.framework', 'Electron Framework'));
    assert.equal(updateFile(join('dist', 'mac', 'Jason.app'), 'darwin'), join('dist', 'mac', 'Jason.app', 'Contents', 'Resources', 'app-update.yml'));
});

for (const platform of ['win32', 'darwin'] as const) {
    test(`${platform}: a package built as configured is accepted, and what was read is said`, async (t) => {
        const app = await made(t, platform);
        const said = checkPackage(app, platform);
        assert.ok(said.includes('app-update.yml: provider github, owner reply-team, repo jason-ai' + (platform === 'win32' ? `, publisher ${signer}` : '')), said.join('\n'));
        assert.ok(said.includes('fuse runAsNode: disabled'), said.join('\n'));
        assert.ok(said.includes('fuse onlyLoadAppFromAsar: enabled'), said.join('\n'));
        assert.ok(said.includes('fuse 8: enabled, as Electron left it'), said.join('\n'));
    });

    for (const [index, name] of wireOrder.entries()) {
        test(`${platform}: a package whose ${name} fuse is not as configured is refused`, async (t) => {
            const app = await made(t, platform);
            const wanted = (fuses as Record<string, boolean>)[name];
            setFuse(app, platform, index, wanted ? 0x30 : 0x31);
            assert.throws(() => checkPackage(app, platform), new RegExp(`fuse ${name} is ${wanted ? 'disabled' : 'enabled'}, and the configuration says ${wanted ? 'enabled' : 'disabled'}`));
        });
    }

    test(`${platform}: a fuse removed from the binary is refused`, async (t) => {
        const app = await made(t, platform);
        setFuse(app, platform, wireOrder.indexOf('runAsNode'), 'r'.charCodeAt(0));
        assert.throws(() => checkPackage(app, platform), /fuse runAsNode is removed, and the configuration says disabled/);
    });

    test(`${platform}: every copy of the wire is read, the last as the first`, async (t) => {
        const app = await made(t, platform, { copies: 2 });
        assert.doesNotThrow(() => checkPackage(app, platform));
        setFuse(app, platform, wireOrder.indexOf('enableNodeOptionsEnvironmentVariable'), 0x31, 'last');
        assert.throws(() => checkPackage(app, platform), /fuse enableNodeOptionsEnvironmentVariable is enabled, and the configuration says disabled/);
    });

    test(`${platform}: a binary with no fuse wire, or one too short for the configuration, is refused`, async (t) => {
        const app = await made(t, platform);
        writeFileSync(fuseFile(app, platform), Buffer.from('no wire here'));
        assert.throws(() => checkPackage(app, platform), /holds no fuse wire/);
        writeFileSync(fuseFile(app, platform), binary(7));
        assert.throws(() => checkPackage(app, platform), /holds 7 fuses, and the configuration sets grantFileProtocolExtraPrivileges/);
    });

    test(`${platform}: a package without app-update.yml is refused`, async (t) => {
        const app = await made(t, platform, { updater: null });
        assert.throws(() => checkPackage(app, platform), /holds no app-update\.yml/);
    });

    for (const [key, other] of [['provider', 'generic'], ['owner', 'someone-else'], ['repo', 'another-repository']] as const) {
        test(`${platform}: an app-update.yml naming another ${key} is refused`, async (t) => {
            const app = await made(t, platform, { updater: updaterFile(platform).replace(new RegExp(`^${key}: .*$`, 'm'), `${key}: ${other}`) });
            assert.throws(() => checkPackage(app, platform), new RegExp(`app-update\\.yml says ${key} ${other}, and the package is built with ${key} `));
        });

        test(`${platform}: an app-update.yml without its ${key} is refused`, async (t) => {
            const app = await made(t, platform, { updater: updaterFile(platform).replace(new RegExp(`^${key}: .*\\n`, 'm'), '') });
            assert.throws(() => checkPackage(app, platform), new RegExp(`app-update\\.yml says ${key} nothing, and the package is built with ${key} `));
        });
    }
}

test('win32: an app-update.yml without the publisher an update must carry is refused, since the updater would check none', async (t) => {
    const app = await made(t, 'win32', { updater: updaterFile('darwin') });
    assert.throws(() => checkPackage(app, 'win32'), new RegExp(`app-update\\.yml names the publishers nothing, and an update must be signed by ${signer} alone`));
});

test('win32: an app-update.yml naming another publisher, or one more, is refused', async (t) => {
    const other = await made(t, 'win32', { updater: updaterFile('win32').replace(signer, 'Someone Else Ltd.') });
    assert.throws(() => checkPackage(other, 'win32'), /names the publishers Someone Else Ltd\./);
    const more = await made(t, 'win32', { updater: `${updaterFile('win32')}  - Someone Else Ltd.\n` });
    assert.throws(() => checkPackage(more, 'win32'), new RegExp(`names the publishers ${signer}, Someone Else Ltd\\.`));
});

test('darwin: a publisher in a Mac package\'s app-update.yml is not read: the Mac updater holds an update to the signature of the application it replaces', async (t) => {
    const app = await made(t, 'darwin', { updater: updaterFile('win32') });
    assert.doesNotThrow(() => checkPackage(app, 'darwin'));
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

    const usage = spawnSync(process.execPath, [script, '--app', app, '--platform', 'linux'], { encoding: 'utf8' });
    assert.equal(usage.status, 2);
    assert.match(usage.stderr, /--app <package> --platform win32\|darwin/);
});
