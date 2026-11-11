// How electron-builder packages the desktop: an nsis installer for Windows, a dmg and a zip for each Mac architecture,
// an AppImage and a deb for Linux on x64, and the updater's feed files beside them.
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

/** The chat's three lists and its instruction text, packaged inside the application beside package.json. */
export const chatFiles = Object.freeze(['chat-deny-list.txt', 'chat-allow-list.txt', 'chat-plan-list.txt', 'chat-instructions.md']);

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

/** Electron's own Chromium notices, from the folder the unpacked distribution left them in into the package's notices folder. */
export function copyChromiumNotices(from, resources) {
    const source = join(from, 'LICENSES.chromium.html');
    if (!existsSync(source)) throw new Error(`${from} holds no LICENSES.chromium.html; the package would carry no Chromium notices.`);
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

/** How long a deb waits for the AppImage packed beside it before it stops the pack: far longer than an AppImage takes. */
const linuxWaitMs = 15 * 60 * 1000;

/**
 * The order of the two Linux forms in one pack. electron-builder builds a platform's targets at once, from the one
 * unpacked folder, and the deb writes resources/package-type into that folder as it builds (app-builder-lib 26.18.0,
 * out/targets/FpmTarget.js, build: "Extra file needed for auto-updater to detect installation method"); electron-updater
 * makes its deb updater wherever that file reads deb (electron-updater 6.8.11, out/main.js, doLoadAutoUpdater). An
 * AppImage copied from the folder after that would update itself as a deb. So where one pack makes both, the deb starts
 * only once the AppImage is built: electron-builder awaits these hooks (out/packager.js, emitArtifactBuildStarted and
 * emitArtifactBuildCompleted), the deb emits its start before it writes the file, and the AppImage emits its end after
 * its archive is made. An AppImage that fails ends no wait, so the wait has a limit and then stops the pack. Should the
 * order not hold, scripts/check-package.mjs refuses an AppImage carrying package-type.
 */
function linuxOrder(limitMs) {
    let appImage = null;
    return {
        packing(context) {
            const names = (context?.targets ?? []).map((target) => target.name);
            if (context?.electronPlatformName !== 'linux' || !names.includes('appImage') || !names.includes('deb')) {
                appImage = null;
                return;
            }
            let built;
            const done = new Promise((resolve) => {
                built = resolve;
            });
            appImage = { done, built };
        },
        async started(event) {
            if (event?.targetPresentableName !== 'deb' || appImage === null) return;
            let timer;
            const limit = new Promise((_, refuse) => {
                timer = setTimeout(() => refuse(new Error('The deb waited for the AppImage, which was not built; the deb is not packed beside an AppImage that failed.')), limitMs);
            });
            try {
                await Promise.race([appImage.done, limit]);
            } finally {
                clearTimeout(timer);
            }
        },
        completed(event) {
            if (event?.target?.name === 'appImage' && appImage !== null) appImage.built();
        },
    };
}

/**
 * The configuration for one version. `sign`, when given, is the module that signs each Windows file; without it a
 * Windows package is unsigned, which only a local build is. `linuxWaitMs` is how long a deb waits for the AppImage packed
 * beside it, for a test to make short.
 */
export function configuration({ version, sign = null, linuxWaitMs: waitMs = linuxWaitMs }) {
    const order = linuxOrder(waitMs);
    return {
        appId: 'ai.jasonharness.desktop',
        productName: 'Jason',
        copyright: 'Copyright (c) 2026 ReplyApp Inc.',
        // The home page is set for the deb, which must name one: FpmTarget's computeFpmMetaInfoOptions refuses a deb whose
        // AppInfo.computePackageUrl finds none (metadata.homepage, else a repository, which desktop/ names nowhere). In the
        // pinned app-builder-lib 26.18.0 two other readers see it: the nsis installer, whose uninstall entry's help and
        // about links it becomes when nsis names none (out/targets/nsis/NsisTarget.js, UNINSTALL_URL_*), and the Windows
        // signing manager, which hands it to the sign hook as `site`, which scripts/sign-windows.mjs does not read. It is
        // also in app.asar's package.json, which nothing of the desktop reads. Nothing on the Mac reads it.
        extraMetadata: { version, homepage: 'https://github.com/reply-team/jason-ai' },
        directories: { output: 'dist', buildResources: 'installer' },
        // Inside app.asar: the compiled main process, the starting page, the tray icons desk.ts loads from the app,
        // the chat's two lists and its instruction text, and package.json. node_modules holds the one runtime
        // dependency, the updater.
        files: ['lib/**/*', 'shell/**/*', 'resources/**/*', 'package.json', ...chatFiles],
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
        // A candidate writes latest.yml, latest-mac.yml and latest-linux.yml as a release does, never a channel of its
        // own: the release's prerelease flag is what keeps it from a stable installation, which asks for no pre-release.
        detectUpdateChannel: false,
        generateUpdatesFilesForAllChannels: false,
        beforePack: async (context) => {
            order.packing(context);
            checkBundle(bundleFolder(context.arch), version, context.electronPlatformName);
        },
        artifactBuildStarted: (event) => order.started(event),
        artifactBuildCompleted: (event) => order.completed(event),
        // Electron's Chromium notices are where electron-builder left them once it unpacked the distribution: beside
        // Jason.exe on Windows, and on macOS already moved from beside the .app into its resources, which the .app carries.
        afterPack: async (context) => {
            const resources = resourcesOf(context);
            copyChromiumNotices(context.electronPlatformName === 'darwin' ? resources : context.appOutDir, resources);
        },
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
            icon: 'installer/icon.icns',
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
        // Linux, x64 alone: an AppImage and a deb, both updates, named in one feed, latest-linux.yml, where each updater
        // finds its own file by its extension. Neither is signed: a person checks either against checksums.txt.
        linux: {
            target: [
                { target: 'AppImage', arch: ['x64'] },
                { target: 'deb', arch: ['x64'] },
            ],
            // The deb links /usr/bin/<executable>, and electron-builder's own name for it is the package's, jason-desktop,
            // which is the name the PATH command falls back to.
            executableName: 'jason-desktop-app',
            // Nothing after the program in the menu entry's Exec, where the legacy AppImage toolset puts --no-sandbox.
            executableArgs: [],
            // A folder of the icon set's own PNGs, each named by its size: app-builder-lib 26.18.0, out/options/linuxOptions.d.ts,
            // icon: "The path to icon set directory or one png file, relative to the build resources or to the project
            // directory. The icon filename must contain the size (e.g. 32x32.png) of the icon." The deb installs each as
            // /usr/share/icons/hicolor/<size>x<size>/apps/<executable>.png (out/targets/FpmTarget.js), the AppImage under
            // usr/share/icons/hicolor the same way (out/targets/appimage/appLauncher.js).
            icon: 'installer/linux-icons',
            category: 'Office',
            synopsis: 'A window onto the installation the jason command line uses',
            maintainer: `${signer} <support@reply.io>`,
        },
        // electron-builder spells x64 as x86_64 in an AppImage's name and amd64 in a deb's, so these say x64 themselves.
        appImage: { artifactName: 'jason-desktop-linux-x64.${ext}' },
        // The jason a deb carries needs the ICU library, whose package is named by its version: recommended, by
        // alternatives, so apt installs one where none is, and never a dependency that would refuse a system whose ICU is
        // named otherwise. A deb's recommends replace electron-builder's own (FpmTarget.getDefaultRecommends), which is
        // kept first; its dependencies stay electron-builder's (getDefaultDepends).
        deb: {
            artifactName: 'jason-desktop-linux-x64.${ext}',
            recommends: ['libappindicator3-1', 'libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70'],
        },
        // The AppImage's runtime: 1.0.3 is the static runtime, which needs no libfuse2, where the default, 0.0.0, is the
        // legacy FUSE 2 one (app-builder-lib 26.18.0, out/configuration.d.ts, ToolsetConfig.appimage, which lists 1.0.3
        // among its betas; out/toolsets/linux.js pins its archive's sha256).
        toolsets: { appimage: '1.0.3' },
    };
}

export default function () {
    const sign = process.env['JASON_WINDOWS_SIGN'] === '1' ? join(desktop, 'scripts', 'sign-windows.mjs') : null;
    return configuration({ version: releaseVersion(process.env['JASON_VERSION']), sign });
}
