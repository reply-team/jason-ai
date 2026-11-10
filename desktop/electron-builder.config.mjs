// How electron-builder packages the desktop: an nsis installer for Windows, a dmg and a zip for each Mac architecture,
// and the updater's feed files beside them.
//
//   JASON_VERSION=0.34.0 npx electron-builder --config electron-builder.config.mjs --win --x64 --publish never
//
// The version is the release's, given by JASON_VERSION: package.json says 0.0.0, and the updater compares the release it
// finds against app.getVersion(), which is the packaged package.json's. Each architecture's bundle is the folder
// `node scripts/prepare.mjs --output bundles/<arch>` prepared, at the same version, and a pack refuses any other.
// Nothing here publishes: the release workflow attaches the files.

import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const desktop = dirname(fileURLToPath(import.meta.url));

/** The name every signature the release makes carries, and the one an update must carry to be installed. */
export const signer = 'ReplyApp Inc.';

/**
 * Electron's fuses, flipped in the packaged binary before it is signed. Off: running as plain Node, NODE_OPTIONS, the
 * inspector's command-line switches and an app folder beside app.asar. On: cookies encrypted with the operating
 * system's key, app.asar held to the digest the binary carries, and file: pages' extra privileges, without which the
 * starting page, a file: page inside app.asar, is served by Chromium's own file: handler, which cannot read the archive.
 */
export const fuses = Object.freeze({
    runAsNode: false,
    enableNodeOptionsEnvironmentVariable: false,
    enableNodeCliInspectArguments: false,
    enableCookieEncryption: true,
    enableEmbeddedAsarIntegrityValidation: true,
    onlyLoadAppFromAsar: true,
    loadBrowserProcessSpecificV8Snapshot: false,
    grantFileProtocolExtraPrivileges: true,
});

/**
 * The sha256 of every file of Electron's release, as the electron package the lockfile installs carries them in its
 * checksums.json. npm ci --ignore-scripts installs that package without running its own download, so the lockfile's
 * integrity hash is what holds these digests.
 */
export function electronChecksums() {
    return JSON.parse(readFileSync(join(desktop, 'node_modules', 'electron', 'checksums.json'), 'utf8'));
}

/** The files the chat's fence reads, packaged inside the application beside package.json. */
export const chatLists = Object.freeze(['chat-deny-list.txt', 'chat-allow-list.txt']);

/** What a pack of one architecture finds in its bundle folder: the executable, its two notices and the skills. */
export function bundleFiles(platform) {
    return [platform === 'win32' ? 'jason.exe' : 'jason', 'LICENSE', 'THIRD-PARTY-NOTICES', 'version.json', 'skills'];
}

const semver = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

/**
 * The version a package is built at: a release's (0.34.0) or a candidate's (0.34.0-rc.1). A development version is
 * refused - 0.0.0, a `local` or `dev` pre-release word - and so is nothing at all, because a package carrying one would
 * offer every release as an update to itself and resolve no release of its own.
 */
export function releaseVersion(value) {
    const version = (value ?? '').trim();
    const match = semver.exec(version);
    if (match === null) throw new Error(`JASON_VERSION is "${version}", not a version like 0.34.0 or 0.34.0-rc.1.`);
    const word = (match[4] ?? '').split('.')[0]?.toLowerCase() ?? '';
    if (version.startsWith('0.0.0') || word === 'local' || word === 'dev') {
        throw new Error(`JASON_VERSION is "${version}", a development version; a package is built at a release's or a candidate's.`);
    }
    return version;
}

const archNames = { 0: 'ia32', 1: 'x64', 2: 'armv7l', 3: 'arm64', 4: 'universal' };

/** The folder a pack takes its bundle from, by electron-builder's architecture. */
export function bundleFolder(arch) {
    const name = archNames[arch];
    if (name !== 'x64' && name !== 'arm64') throw new Error(`No bundle is prepared for the architecture ${name ?? arch}.`);
    return join(desktop, 'bundles', name);
}

/** Refuses a bundle that is not one of this version: a file missing, or version.json naming another. */
export function checkBundle(folder, version, platform) {
    const missing = bundleFiles(platform).filter((name) => !existsSync(join(folder, name)));
    if (missing.length > 0) throw new Error(`${folder} has no ${missing.join(', ')}; run node scripts/prepare.mjs --output for this architecture first.`);
    let said;
    try {
        said = JSON.parse(readFileSync(join(folder, 'version.json'), 'utf8')).version;
    } catch {
        said = undefined;
    }
    if (said !== version) throw new Error(`${folder} was prepared as ${String(said)}, and the package is built at ${version}.`);
}

/** Electron's own Chromium notices, from the unpacked distribution into the package's notices folder. */
export function copyChromiumNotices(appOutDir, resources) {
    const source = join(appOutDir, 'LICENSES.chromium.html');
    if (!existsSync(source)) throw new Error(`${appOutDir} holds no LICENSES.chromium.html; the package would carry no Chromium notices.`);
    mkdirSync(join(resources, 'notices'), { recursive: true });
    copyFileSync(source, join(resources, 'notices', 'LICENSES.chromium.html'));
}

/** Where a pack's resources folder is: inside the application bundle on macOS, beside the executable elsewhere. */
function resourcesOf(context) {
    if (context.electronPlatformName !== 'darwin') return join(context.appOutDir, 'resources');
    const app = readdirSync(context.appOutDir).find((name) => name.endsWith('.app'));
    if (app === undefined) throw new Error(`${context.appOutDir} holds no .app.`);
    return join(context.appOutDir, app, 'Contents', 'Resources');
}

/**
 * The configuration for one version. `sign`, when given, is the module that signs each Windows file; without it a
 * Windows package is unsigned, which only a local build is.
 */
export function configuration({ version, sign = null }) {
    return {
        appId: 'ai.jasonharness.desktop',
        productName: 'Jason',
        copyright: 'Copyright (c) 2026 ReplyApp Inc.',
        extraMetadata: { version },
        directories: { output: 'dist', buildResources: 'installer' },
        // Inside app.asar: the compiled main process, the starting page, the tray icons desk.ts loads from the app,
        // the chat's two lists, and package.json. node_modules holds the one runtime dependency, the updater.
        files: ['lib/**/*', 'shell/**/*', 'resources/**/*', 'package.json', ...chatLists],
        asar: true,
        // No rebuild of the production dependencies, which would run their install scripts: the one there is, the
        // updater, is plain JavaScript.
        npmRebuild: false,
        extraResources: [
            { from: 'bundles/${arch}', to: 'bundle' },
            { from: '../LICENSE', to: 'notices/LICENSE' },
            { from: 'THIRD-PARTY-NOTICES', to: 'notices/THIRD-PARTY-NOTICES' },
        ],
        // electron-builder fetches Electron's distribution for each platform and architecture it packs, and @electron/get
        // refuses a zip, downloaded or cached, whose sha256 is not the one the electron package names; without these it
        // would check the zip against a SHASUMS256.txt fetched from the same release.
        electronDownload: { checksums: electronChecksums() },
        electronFuses: { ...fuses },
        publish: [{ provider: 'github', owner: 'reply-team', repo: 'jason-ai', releaseType: 'release' }],
        // A candidate writes latest.yml and latest-mac.yml as a release does, never a channel of its own: the release's
        // prerelease flag is what keeps it from a stable installation, which asks for no pre-release.
        detectUpdateChannel: false,
        generateUpdatesFilesForAllChannels: false,
        beforePack: async (context) => checkBundle(bundleFolder(context.arch), version, context.electronPlatformName),
        afterPack: async (context) => copyChromiumNotices(context.appOutDir, resourcesOf(context)),
        win: {
            target: [{ target: 'nsis', arch: ['x64'] }],
            icon: 'resources/icon.png',
            requestedExecutionLevel: 'asInvoker',
            verifyUpdateCodeSignature: true,
            // The libraries beside Jason.exe are signed as the programs are; the hook leaves one that came signed.
            signExts: ['.dll'],
            signtoolOptions: { publisherName: [signer], signingHashAlgorithms: ['sha256'], ...(sign === null ? {} : { sign }) },
            ...(sign === null ? {} : { forceCodeSigning: true }),
        },
        nsis: {
            oneClick: true,
            perMachine: false,
            artifactName: 'jason-desktop-windows-x64-setup.${ext}',
            include: 'installer/uninstall.nsh',
            deleteAppDataOnUninstall: false,
        },
        mac: {
            target: [
                { target: 'dmg', arch: ['arm64', 'x64'] },
                { target: 'zip', arch: ['arm64', 'x64'] },
            ],
            category: 'public.app-category.business',
            icon: 'resources/icon.png',
            hardenedRuntime: true,
            entitlements: 'installer/entitlements.mac.plist',
            entitlementsInherit: 'installer/entitlements.mac.plist',
            // The bundled jason is signed by its own job with its own entitlements, and keeps that signature.
            signIgnore: ['/Contents/Resources/bundle/jason$'],
            notarize: true,
            // A Mac package is built only by the release, which signs it: one that cannot be signed fails the build.
            forceCodeSigning: true,
            artifactName: 'jason-desktop-macos-${arch}.${ext}',
        },
        // A dmg is installed by hand and is never an update: no feed entry and no blockmap.
        dmg: { sign: true, writeUpdateInfo: false, artifactName: 'jason-desktop-macos-${arch}.${ext}' },
    };
}

export default function () {
    const sign = process.env['JASON_WINDOWS_SIGN'] === '1' ? join(desktop, 'scripts', 'sign-windows.mjs') : null;
    return configuration({ version: releaseVersion(process.env['JASON_VERSION']), sign });
}
