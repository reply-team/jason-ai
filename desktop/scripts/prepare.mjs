// Prepares a bundle folder, desktop/bundle/ by default: the jason executable, with the repository's LICENSE and
// THIRD-PARTY-NOTICES beside it as every release's zip and tarball carry them, the skills package `jason skills install --source`
// deploys from, the demo provider's package (plugins/demo), and version.json naming the version all of it was prepared as.
//
//   node scripts/prepare.mjs [--pre rc.1] [--rid win-x64] [--output bundles/x64] [--version 0.34.0] [--executable <file>]
//                            [--demo <archive>]
//
// The version is the repository's VersionPrefix with a pre-release word, so a development build of the desktop carries a
// candidate's version string and resolves that candidate's own release directory; `dev` is refused. `--version` names
// the whole version instead, a release's or a candidate's, which a package is built at. `--executable` takes an
// executable the release has already built and signed, copied byte for byte, instead of publishing this checkout's; the
// release passes it so the package carries the very file its zip or tarball does. Otherwise dotnet publishes, started with a
// file and arguments, never through a shell. The output is a folder inside desktop/, emptied first. `--demo` takes the
// archive of the demo's sample the release's demo job made, carried at demo/jason-demo.zip, which the desktop places at
// a demo's first start; a run without it, a pull request's or a developer's, carries none, and the desktop then makes
// the sample live.

import { execFile } from 'node:child_process';
import { chmodSync, copyFileSync, cpSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { arch, platform } from 'node:os';
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { demoArchive, releaseVersion } from '../electron-builder.config.mjs';

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const root = resolve(desktop, '..');

function option(argv, name, fallback) {
    const at = argv.indexOf(`--${name}`);
    return at >= 0 && argv[at + 1] ? argv[at + 1] : fallback;
}

function defaultRid() {
    const os = { win32: 'win', darwin: 'osx', linux: 'linux' }[platform()];
    const cpu = { x64: 'x64', arm64: 'arm64' }[arch()];
    if (!os || !cpu) throw new Error(`No runtime identifier for ${platform()} ${arch()}; pass --rid.`);
    return `${os}-${cpu}`;
}

/** The repository's VersionPrefix, as dotnet/Directory.Build.props under `repository` names it. */
export function versionPrefix(repository = root) {
    const props = readFileSync(join(repository, 'dotnet', 'Directory.Build.props'), 'utf8');
    const prefix = /<VersionPrefix>([0-9]+\.[0-9]+\.[0-9]+)<\/VersionPrefix>/.exec(props)?.[1];
    if (!prefix) throw new Error('dotnet/Directory.Build.props names no VersionPrefix.');
    return prefix;
}

/**
 * What a run prepares, from its arguments: the folder, the version, the runtime identifier, the executable given, and
 * the demo's sample archive given.
 */
export function plan(argv, prefix = versionPrefix()) {
    const output = resolve(desktop, option(argv, 'output', 'bundle'));
    const inside = relative(desktop, output);
    if (inside === '' || inside.startsWith('..') || isAbsolute(inside)) throw new Error(`--output ${output} is not a folder inside desktop/.`);
    const whole = option(argv, 'version', null);
    let version;
    if (whole !== null) {
        version = releaseVersion(whole);
    } else {
        const pre = option(argv, 'pre', 'rc.1');
        if (!/^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$/.test(pre) || /^dev$/i.test(pre.split('.')[0])) {
            throw new Error(`"${pre}" is not a candidate's pre-release word; a desktop build never carries a dev version.`);
        }
        version = `${prefix}-${pre}`;
    }
    const rid = option(argv, 'rid', defaultRid());
    const given = option(argv, 'executable', null);
    const executable = given === null ? null : resolve(given);
    const name = rid.startsWith('win-') ? 'jason.exe' : 'jason';
    if (executable !== null && basename(executable) !== name) throw new Error(`--executable ${executable} is not a ${name} for ${rid}.`);
    const archive = option(argv, 'demo', null);
    return { output, version, rid, executable, name, demo: archive === null ? null : resolve(archive) };
}

/**
 * Completes a folder that holds the executable: the debug symbols a publish leaves are removed, and the licence, the
 * notices, the skills, the demo provider's package and version.json are put beside it.
 */
export function complete(folder, version, repository = root) {
    for (const name of readdirSync(folder)) if (/\.(pdb|dbg)$/i.test(name)) rmSync(join(folder, name), { force: true });
    copyFileSync(join(repository, 'LICENSE'), join(folder, 'LICENSE'));
    copyFileSync(join(repository, 'THIRD-PARTY-NOTICES'), join(folder, 'THIRD-PARTY-NOTICES'));
    cpSync(join(repository, 'skills'), join(folder, 'skills'), { recursive: true });
    // The demo provider's package exactly as the tree holds it: the desktop places a copy of it in the demo installation.
    cpSync(join(repository, 'plugins', 'demo'), join(folder, 'plugins', 'demo'), { recursive: true });
    writeFileSync(join(folder, 'version.json'), `${JSON.stringify({ version }, null, 2)}\n`);
}

/** Puts the demo's sample archive into the folder at demo/jason-demo.zip, byte for byte; throws when there is none to put. */
export function carryDemo(folder, archive) {
    let file = false;
    try {
        file = statSync(archive).isFile();
    } catch {
        // Not there.
    }
    if (!file) throw new Error(`--demo names no archive at ${archive}.`);
    const target = join(folder, ...demoArchive.split('/'));
    mkdirSync(dirname(target), { recursive: true });
    copyFileSync(archive, target);
}

async function publish(rid, version, output) {
    const args = [
        'publish', join(root, 'dotnet', 'src', 'Jason.App'),
        '--configuration', 'Release',
        '--runtime', rid,
        '--self-contained',
        '-p:PublishSingleFile=true',
        `-p:Version=${version}`,
        '--output', output,
    ];
    console.log(`dotnet ${args.join(' ')}`);
    await new Promise((done, fail) => {
        const child = execFile('dotnet', args, { cwd: root, windowsHide: true, maxBuffer: 64 * 1024 * 1024 }, (error) => (error ? fail(error) : done()));
        child.stdout?.pipe(process.stdout);
        child.stderr?.pipe(process.stderr);
    });
}

async function main() {
    const { output, version, rid, executable, name, demo } = plan(process.argv);
    rmSync(output, { recursive: true, force: true });
    mkdirSync(output, { recursive: true });
    if (executable === null) await publish(rid, version, output);
    else copyFileSync(executable, join(output, name));
    if (name === 'jason') chmodSync(join(output, name), 0o755);
    complete(output, version);
    if (demo !== null) carryDemo(output, demo);
    console.log(`${relative(desktop, output)}/ prepared as ${version} for ${rid}`);
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
