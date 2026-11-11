// What electron-builder is told to make: which Electron, which fuses, which version, which feed, which files. The
// package itself is built only by the release's signing jobs and by hand; these rows hold the configuration it is built
// from, and the release's own steps read the built package back.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { allowListFile, denyListFile, instructionsFile } from '../src/chat-deny.ts';
import { bundleFiles, bundleFolder, chatFiles, checkBundle, configuration, copyChromiumNotices, fuses, releaseVersion, signer } from '../electron-builder.config.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { devDependencies: Record<string, string> };
const lock = JSON.parse(readFileSync(join(root, 'package-lock.json'), 'utf8')) as { packages: Record<string, { version?: string; integrity?: string; dev?: boolean }> };

type Configuration = ReturnType<typeof configuration>;
const built: Configuration = configuration({ version: '0.34.0-rc.1' });

function temporary(t: { after(fn: () => void): void }): string {
    const folder = mkdtempSync(join(tmpdir(), 'jason-packaging-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    return folder;
}

test('the package is built on Electron 44.10.2 exactly', () => {
    assert.equal(manifest.devDependencies['electron'], '44.10.2');
    assert.equal(lock.packages['node_modules/electron']?.version, '44.10.2');
});

test('each Electron zip a pack unpacks is held to the sha256 the lockfile’s electron package names, never to sums fetched beside it', () => {
    // npm ci --ignore-scripts installs the electron package, held by the lockfile's integrity hash, without running its
    // own download; electron-builder fetches the distribution itself, and @electron/get refuses a zip, downloaded or
    // cached, whose sha256 is not the one given here, rather than one it would read from SHASUMS256.txt on the same host.
    const electron = join(root, 'node_modules', 'electron');
    const installed = JSON.parse(readFileSync(join(electron, 'package.json'), 'utf8')) as { version: string };
    const checksums = JSON.parse(readFileSync(join(electron, 'checksums.json'), 'utf8')) as Record<string, string>;
    assert.equal(installed.version, lock.packages['node_modules/electron']?.version);
    assert.match(lock.packages['node_modules/electron']?.integrity ?? '', /^sha512-/);
    assert.deepEqual(built.electronDownload, { checksums });
    for (const zip of ['win32-x64', 'darwin-arm64', 'darwin-x64']) {
        assert.match(checksums[`electron-v${installed.version}-${zip}.zip`] ?? '', /^[0-9a-f]{64}$/, `no sha256 for Electron's ${zip} zip`);
    }
    // No distribution of its own: the one fetched is the one checked.
    assert.equal('electronDist' in built, false);
});

test('electron-builder is a pinned development dependency, installed from the lockfile and never carried', () => {
    assert.match(manifest.devDependencies['electron-builder'] ?? '', /^\d+\.\d+\.\d+$/);
    const entry = lock.packages['node_modules/electron-builder'];
    assert.equal(entry?.version, manifest.devDependencies['electron-builder']);
    assert.equal(entry?.dev, true);
});

test('no package’s script runs in a pack: no rebuild of the dependencies, and nothing around the two scripts the release runs', () => {
    // electron-builder rebuilds the production dependencies by default, running their install scripts; the one there
    // is, electron-updater, is plain JavaScript and needs none.
    assert.equal(built.npmRebuild, false);
    const scripts = (JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as { scripts: Record<string, string> }).scripts;
    assert.equal(scripts['package'], 'npm run build && electron-builder --config electron-builder.config.mjs --publish never');
    assert.equal(scripts['build'], 'tsc -p tsconfig.json');
    // npm runs pre<name> and post<name> around a script, and the install hooks on an install the release never asks for.
    for (const name of ['package', 'build']) {
        assert.equal(scripts[`pre${name}`], undefined, `pre${name}`);
        assert.equal(scripts[`post${name}`], undefined, `post${name}`);
    }
    for (const name of ['preinstall', 'install', 'postinstall', 'prepare', 'prepublish', 'preprepare', 'postprepare']) assert.equal(scripts[name], undefined, name);
});

test('the fuses: no plain Node, no NODE_OPTIONS, no inspector, only app.asar and only app.asar as signed', () => {
    assert.deepEqual(built.electronFuses, {
        runAsNode: false,
        enableNodeOptionsEnvironmentVariable: false,
        enableNodeCliInspectArguments: false,
        enableCookieEncryption: true,
        enableEmbeddedAsarIntegrityValidation: true,
        onlyLoadAppFromAsar: true,
        loadBrowserProcessSpecificV8Snapshot: false,
        // On: the starting page is a file: page inside app.asar, and with this fuse off Chromium's own file: handler
        // serves it, which cannot read the archive - a packaged start showed ERR_FILE_NOT_FOUND and went no further.
        grantFileProtocolExtraPrivileges: true,
    });
    assert.equal(built.asar, true);
    assert.ok(Object.isFrozen(fuses));
});

test('the package is built at the version asked for, a release’s or a candidate’s, never a development one', () => {
    assert.equal(configuration({ version: '0.34.0' }).extraMetadata.version, '0.34.0');
    assert.equal(built.extraMetadata.version, '0.34.0-rc.1');
    assert.equal(releaseVersion(' 0.34.0 '), '0.34.0');
    assert.equal(releaseVersion('0.34.0-rc.1'), '0.34.0-rc.1');
    for (const refused of [undefined, '', '0.0.0', '0.0.0-local', '0.34.0-local', '0.34.0-dev.3', 'v0.34.0', '0.34', 'latest']) {
        assert.throws(() => releaseVersion(refused), /JASON_VERSION/, String(refused));
    }
});

test('app-update.yml names this repository’s releases, and an update must carry the release’s signer', () => {
    assert.deepEqual(built.publish, [{ provider: 'github', owner: 'reply-team', repo: 'jason-ai', releaseType: 'release' }]);
    assert.equal(signer, 'ReplyApp Inc.');
    assert.deepEqual(built.win.signtoolOptions.publisherName, ['ReplyApp Inc.']);
    assert.equal(built.win.verifyUpdateCodeSignature, true);
    assert.deepEqual(built.win.signtoolOptions.signingHashAlgorithms, ['sha256']);
});

test('a candidate writes the feed files a stable release does: latest.yml and latest-mac.yml, never rc.yml', () => {
    // The release's prerelease flag is what keeps a candidate from a stable installation, never a channel of its own; a
    // dmg is installed by hand and never an update, so it writes no feed entry and no blockmap.
    assert.equal(built.detectUpdateChannel, false);
    assert.equal(built.generateUpdatesFilesForAllChannels, false);
    assert.equal(built.dmg.writeUpdateInfo, false);
});

test('Windows gets one per-user nsis installer; each Mac architecture a dmg and a zip; no name carries a version', () => {
    assert.deepEqual(built.win.target, [{ target: 'nsis', arch: ['x64'] }]);
    assert.equal(built.nsis.oneClick, true);
    assert.equal(built.nsis.perMachine, false);
    assert.equal(built.nsis.deleteAppDataOnUninstall, false);
    assert.deepEqual(built.mac.target, [
        { target: 'dmg', arch: ['arm64', 'x64'] },
        { target: 'zip', arch: ['arm64', 'x64'] },
    ]);
    const names = [built.nsis.artifactName, built.mac.artifactName, built.dmg.artifactName];
    for (const name of names) {
        assert.ok(!name.includes('${version}'), name);
        assert.match(name.replaceAll('${arch}', 'arm64').replaceAll('${ext}', 'zip'), /^[a-z0-9][a-z0-9.-]*$/);
    }
    // The updater tells a Mac's architecture by the file name, so only the arm64 files may say arm64.
    assert.ok(built.mac.artifactName.includes('${arch}'));
});

test('a Windows package is signed when a signing module is given, and every Mac package always', () => {
    assert.equal('sign' in built.win.signtoolOptions, false);
    assert.equal('forceCodeSigning' in built.win, false);
    const signed = configuration({ version: '0.34.0', sign: '/x/sign-windows.mjs' });
    assert.equal(signed.win.signtoolOptions.sign, '/x/sign-windows.mjs');
    assert.equal(signed.win.forceCodeSigning, true);
    // The libraries beside Jason.exe are signed as the programs are, so the package carries no unsigned library; the
    // hook leaves one that came signed with its own signature.
    assert.deepEqual(built.win.signExts, ['.dll']);
    assert.equal(built.mac.forceCodeSigning, true);
    assert.equal(built.mac.hardenedRuntime, true);
    assert.equal(built.mac.notarize, true);
    assert.equal(built.dmg.sign, true);
});

test('the bundled jason keeps its own signature on macOS, and the application asks for V8’s two entitlements alone', () => {
    assert.deepEqual(built.mac.signIgnore, ['/Contents/Resources/bundle/jason$']);
    assert.ok(new RegExp(built.mac.signIgnore[0] ?? '').test('/Applications/Jason.app/Contents/Resources/bundle/jason'));
    assert.ok(!new RegExp(built.mac.signIgnore[0] ?? '').test('/Applications/Jason.app/Contents/MacOS/Jason'));
    for (const file of [built.mac.entitlements, built.mac.entitlementsInherit]) {
        const keys = [...readFileSync(join(root, file), 'utf8').matchAll(/<key>([^<]+)<\/key>/g)].map((match) => match[1]);
        assert.deepEqual(keys, ['com.apple.security.cs.allow-jit', 'com.apple.security.cs.allow-unsigned-executable-memory']);
    }
});

test('app.asar carries the main process, the starting page, the tray icons, the chat’s lists and instruction text, and package.json', () => {
    assert.deepEqual(built.files, ['lib/**/*', 'shell/**/*', 'resources/**/*', 'package.json', 'chat-deny-list.txt', 'chat-allow-list.txt', 'chat-instructions.md']);
    assert.deepEqual(chatFiles, ['chat-deny-list.txt', 'chat-allow-list.txt', 'chat-instructions.md']);
    for (const icon of ['resources/icon.png', 'resources/icon.icns', 'resources/tray/tray.png', 'resources/tray/trayTemplate.png', 'resources/tray/trayTemplate@2x.png']) assert.ok(existsSync(join(root, icon)), icon);
    // buildResources is left out of app.asar by electron-builder, so it is never resources/.
    assert.equal(built.directories.buildResources, 'installer');
});

test('beside app.asar: the bundle of the architecture built, and the licence with both notices', () => {
    assert.deepEqual(built.extraResources, [
        { from: 'bundles/${arch}', to: 'bundle' },
        { from: '../LICENSE', to: 'notices/LICENSE' },
        { from: 'THIRD-PARTY-NOTICES', to: 'notices/THIRD-PARTY-NOTICES' },
    ]);
    assert.ok(existsSync(join(root, '..', 'LICENSE')));
    assert.ok(existsSync(join(root, 'THIRD-PARTY-NOTICES')));
    assert.equal(bundleFolder(1), join(root, 'bundles', 'x64'));
    assert.equal(bundleFolder(3), join(root, 'bundles', 'arm64'));
    assert.throws(() => bundleFolder(0), /ia32/);
    assert.throws(() => bundleFolder(4), /universal/);
});

test('a bundle is the executable with its licence and notices, version.json and the skills, at the package’s version', (t) => {
    assert.deepEqual(bundleFiles('win32'), ['jason.exe', 'LICENSE', 'THIRD-PARTY-NOTICES', 'version.json', 'skills']);
    assert.deepEqual(bundleFiles('darwin'), ['jason', 'LICENSE', 'THIRD-PARTY-NOTICES', 'version.json', 'skills']);
    const folder = temporary(t);
    for (const name of ['jason.exe', 'LICENSE', 'THIRD-PARTY-NOTICES']) writeFileSync(join(folder, name), 'x');
    mkdirSync(join(folder, 'skills'));
    assert.throws(() => checkBundle(folder, '0.34.0', 'win32'), /has no version\.json/);
    writeFileSync(join(folder, 'version.json'), JSON.stringify({ version: '0.34.0-rc.1' }));
    assert.throws(() => checkBundle(folder, '0.34.0', 'win32'), /prepared as 0\.34\.0-rc\.1, and the package is built at 0\.34\.0/);
    writeFileSync(join(folder, 'version.json'), JSON.stringify({ version: '0.34.0' }));
    checkBundle(folder, '0.34.0', 'win32');
    rmSync(join(folder, 'THIRD-PARTY-NOTICES'));
    assert.throws(() => checkBundle(folder, '0.34.0', 'win32'), /has no THIRD-PARTY-NOTICES/);
    assert.throws(() => checkBundle(folder, '0.34.0', 'darwin'), /has no jason, THIRD-PARTY-NOTICES/);
});

test('Chromium’s notices go into the package’s notices folder, and a distribution without them stops the pack', (t) => {
    const out = temporary(t);
    const resources = join(out, 'resources');
    assert.throws(() => copyChromiumNotices(out, resources), /no LICENSES\.chromium\.html/);
    writeFileSync(join(out, 'LICENSES.chromium.html'), '<html>chromium</html>');
    copyChromiumNotices(out, resources);
    assert.equal(readFileSync(join(resources, 'notices', 'LICENSES.chromium.html'), 'utf8'), '<html>chromium</html>');
    assert.equal(typeof built.beforePack, 'function');
});

test('after each pack the Chromium notices land in that platform’s resources folder, from where electron-builder left them: beside Jason.exe, or inside Jason.app', async (t) => {
    // Windows: electron-builder's unpacked folder, Electron's notices at its top, resources/ beside the program.
    const win = temporary(t);
    writeFileSync(join(win, 'LICENSES.chromium.html'), '<html>chromium win32</html>');
    await built.afterPack({ appOutDir: win, electronPlatformName: 'win32', arch: 1 });
    assert.equal(readFileSync(join(win, 'resources', 'notices', 'LICENSES.chromium.html'), 'utf8'), '<html>chromium win32</html>');

    // macOS: electron-builder has already moved Electron's notices from beside the .app into its resources folder, so
    // that the application carries them; nothing is left beside Jason.app.
    const mac = temporary(t);
    const contents = join(mac, 'Jason.app', 'Contents', 'Resources');
    mkdirSync(contents, { recursive: true });
    writeFileSync(join(contents, 'LICENSES.chromium.html'), '<html>chromium darwin</html>');
    await built.afterPack({ appOutDir: mac, electronPlatformName: 'darwin', arch: 3 });
    assert.equal(readFileSync(join(contents, 'notices', 'LICENSES.chromium.html'), 'utf8'), '<html>chromium darwin</html>');
    assert.equal(existsSync(join(mac, 'resources')), false);

    // A Mac pack with no .app, or a distribution without the notices where that platform's are, stops the pack.
    const empty = temporary(t);
    writeFileSync(join(empty, 'LICENSES.chromium.html'), '<html>chromium</html>');
    await assert.rejects(built.afterPack({ appOutDir: empty, electronPlatformName: 'darwin', arch: 3 }), /holds no \.app/);
    const beside = temporary(t);
    writeFileSync(join(beside, 'LICENSES.chromium.html'), '<html>chromium</html>');
    mkdirSync(join(beside, 'Jason.app', 'Contents', 'Resources'), { recursive: true });
    await assert.rejects(built.afterPack({ appOutDir: beside, electronPlatformName: 'darwin', arch: 3 }), /Resources holds no LICENSES\.chromium\.html/);
    await assert.rejects(built.afterPack({ appOutDir: temporary(t), electronPlatformName: 'win32', arch: 1 }), /no LICENSES\.chromium\.html/);
});

test('the package carries no UI zip: the CLI installs the UI of the runtime’s own release from the feed', () => {
    const text = JSON.stringify(built) + readFileSync(join(root, 'scripts', 'prepare.mjs'), 'utf8');
    assert.ok(!/jason-ui|ui\.zip|ui[\\/]dist/.test(text));
    assert.ok(!bundleFiles('win32').some((name) => /ui/i.test(name)));
});

test('no protocol and no file type is registered: the package opens no link and no document', () => {
    const text = JSON.stringify(built);
    assert.ok(!/protocols|fileAssociations|jason:/.test(text));
});

test('DevTools are closed in a packaged build, the window’s and the key that would open them', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const preferences = /webPreferences: \{([^}]*)\}/.exec(main)?.[1] ?? '';
    assert.match(preferences, /\bdevTools: !app\.isPackaged,/);
    const keys = readFileSync(join(root, 'src', 'keybindings.ts'), 'utf8');
    assert.match(keys, /binding\.does\.window === 'devtools' && packaged/);
});

test('both chat list files and the instruction text are packaged where the main process reads them', () => {
    // The fence's own names, each a file at the application's root, which files puts in app.asar and main.ts reads.
    assert.deepEqual([...chatFiles].sort(), [denyListFile, allowListFile, instructionsFile].sort());
    for (const name of chatFiles) assert.ok(existsSync(join(root, name)), name);
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    assert.match(main, /readFence\(app\.getAppPath\(\)\)/);
});
