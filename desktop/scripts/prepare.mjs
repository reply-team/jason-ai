// Prepares desktop/bundle/: the jason executable of this checkout, published as the release publishes it, the skills
// package `jason skills install --source` deploys from, and version.json naming the version both were prepared as.
//
//   node scripts/prepare.mjs [--pre rc.1] [--rid win-x64]
//
// The version is the repository's VersionPrefix with a pre-release word, so a build of the desktop always carries a
// candidate's version string and resolves that candidate's own release directory; `dev` is refused. dotnet is started
// with a file and arguments, never through a shell.

import { execFile } from 'node:child_process';
import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { arch, platform } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const root = resolve(desktop, '..');
const bundle = join(desktop, 'bundle');

function option(name, fallback) {
    const at = process.argv.indexOf(`--${name}`);
    return at >= 0 && process.argv[at + 1] ? process.argv[at + 1] : fallback;
}

function defaultRid() {
    const os = { win32: 'win', darwin: 'osx', linux: 'linux' }[platform()];
    const cpu = { x64: 'x64', arm64: 'arm64' }[arch()];
    if (!os || !cpu) throw new Error(`No runtime identifier for ${platform()} ${arch()}; pass --rid.`);
    return `${os}-${cpu}`;
}

const props = readFileSync(join(root, 'dotnet', 'Directory.Build.props'), 'utf8');
const prefix = /<VersionPrefix>([0-9]+\.[0-9]+\.[0-9]+)<\/VersionPrefix>/.exec(props)?.[1];
if (!prefix) throw new Error('dotnet/Directory.Build.props names no VersionPrefix.');
const pre = option('pre', 'rc.1');
if (!/^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$/.test(pre) || /^dev$/i.test(pre.split('.')[0])) {
    throw new Error(`"${pre}" is not a candidate's pre-release word; a desktop build never carries a dev version.`);
}
const version = `${prefix}-${pre}`;
const rid = option('rid', defaultRid());

rmSync(bundle, { recursive: true, force: true });
mkdirSync(bundle, { recursive: true });

const args = [
    'publish', join(root, 'dotnet', 'src', 'Jason.App'),
    '--configuration', 'Release',
    '--runtime', rid,
    '--self-contained',
    '-p:PublishSingleFile=true',
    `-p:Version=${version}`,
    '--output', bundle,
];
console.log(`dotnet ${args.join(' ')}`);
await new Promise((done, fail) => {
    const child = execFile('dotnet', args, { cwd: root, windowsHide: true, maxBuffer: 64 * 1024 * 1024 }, (error) => (error ? fail(error) : done()));
    child.stdout?.pipe(process.stdout);
    child.stderr?.pipe(process.stderr);
});

// Only the executable is carried: the debug symbols the publish leaves beside it are not.
for (const name of readdirSync(bundle)) if (/\.(pdb|dbg)$/i.test(name)) rmSync(join(bundle, name), { force: true });

cpSync(join(root, 'skills'), join(bundle, 'skills'), { recursive: true });
writeFileSync(join(bundle, 'version.json'), `${JSON.stringify({ version }, null, 2)}\n`);
console.log(`bundle/ prepared as ${version} for ${rid}`);
