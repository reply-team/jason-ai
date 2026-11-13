// Makes the demo's sample once, as a release builds it, and writes it into one archive the desktop places at a demo's
// first start instead of making it there.
//
//   node --experimental-strip-types .github/scripts/make-demo-archive.mts --bundle <folder> --home <folder> --output <file>
//
// <bundle> is a folder scripts/prepare.mjs prepared for this platform: the release's jason and the demo provider's
// package (plugins/demo) beside it. <home> is an empty folder, or one not there yet; it stands for a person's home
// directory, and the demo's two folders are made in it exactly where the desktop makes them for a person whose
// installation is <home>/.jason with its harness root at <home>/Jason: the data directory <home>/.jason-demo and the harness root
// <home>/Jason-demo; the bundle is copied to <home>/bundle and run from there. The paths an installation records travel
// in its archive, the executable's among them, so <home> is named for nothing but what it is; the release gives it a
// plain name of its own, never the runner's home, its workspace or its temporary folder.
//
// Nothing of the demo is written here: the desktop's own code makes the demo installation (its folders and their
// markers, the provider placed, the route to it and the fixed settings, as a demo start makes them) and its own seed fills
// it, through the bundled jason, every step a verb a person could type. Then the runtime is stopped, the runtime's logs
// are removed (what a run logged is no part of the sample, and it names the folders it ran from), and `jason export`
// writes the archive. The run fails, writing no archive, unless the seed's own marker says the sample is whole.

import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { cliAt, type Cli } from '../../desktop/src/cli.ts';
import { claimDemoFolder } from '../../desktop/src/demo-folders.ts';
import { carriedDemoPlugin, placeDemoPlugin } from '../../desktop/src/demo-plugin.ts';
import { readMarker, readyTheSample, seedDepsOnDisk, seedVersion } from '../../desktop/src/demo-seed.ts';
import { writeDemoSettings } from '../../desktop/src/demo-settings.ts';
import { openDemoDirectory, prepareDemo, writeDemoRoute } from '../../desktop/src/demo-start.ts';
import { demoDirectory } from '../../desktop/src/mode.ts';

function option(argv: readonly string[], name: string): string {
    const at = argv.indexOf(`--${name}`);
    const value = at >= 0 ? argv[at + 1] : undefined;
    if (value === undefined || value.startsWith('--')) throw new Error(`--${name} is required.`);
    return resolve(value);
}

/** The environment every verb runs with: this process's, with no setting of Jason's from it and the home made <home>. */
function environment(home: string, data: string): Record<string, string> {
    const kept: Record<string, string> = {};
    for (const [name, value] of Object.entries(process.env)) {
        if (value !== undefined && !/^JASON_/i.test(name)) kept[name] = value;
    }
    return { ...kept, HOME: home, JASON_DATA_DIR: data, JASON_Update__CheckEnabled: 'false' };
}

async function verb(cli: Cli, args: string[], timeoutMs = 180_000): Promise<string> {
    const result = await cli.run(args, { timeoutMs });
    if (result.exit !== 0) throw new Error(`jason ${args.join(' ')} exited with ${result.exit}: ${result.stdout.trim()} ${result.stderr.trim()}`);
    return result.stdout;
}

async function main(argv: readonly string[]): Promise<void> {
    const prepared = option(argv, 'bundle');
    const home = option(argv, 'home');
    const output = option(argv, 'output');
    const name = process.platform === 'win32' ? 'jason.exe' : 'jason';
    if (!existsSync(join(prepared, name))) throw new Error(`${prepared} holds no jason; prepare it with desktop/scripts/prepare.mjs first.`);
    if (existsSync(output)) throw new Error(`${output} is already there, and an archive is never written over anything.`);

    // The person's places, as a person whose installation is the default one has them, and the demo's beside them.
    const person = join(home, '.jason');
    const personRoot = join(home, 'Jason');
    const places = { data: demoDirectory(person), harnessRoot: demoDirectory(personRoot) };
    // The bundle, copied into <home> and run from there: an attempt records the command its plugin host was started
    // with, so the executable's folder travels in the database as every other folder does.
    const bundle = join(home, 'bundle');
    if (existsSync(home) && readdirSync(home).length > 0) throw new Error(`${home} holds something; the sample is made in an empty home of its own.`);
    mkdirSync(home, { recursive: true });
    cpSync(prepared, bundle, { recursive: true });
    const executable = join(bundle, name);

    // Every verb runs in <home>, as a person's terminal would, so no folder of the build is the one a verb ran in.
    const base = cliAt(executable, environment(home, places.data));
    const cli: Cli = { run: (args, options) => base.run(args, { ...options, cwd: options?.cwd ?? home }) };

    // The demo installation as a demo start makes it (the desktop's chooseInstallation and its prepare step).
    openDemoDirectory(places.data);
    prepareDemo(places.data, {
        harnessRoot: () => claimDemoFolder(places.harnessRoot),
        place: (data) => placeDemoPlugin(carriedDemoPlugin(bundle), data, person),
        route: writeDemoRoute,
        fixedSettings: (data) => writeDemoSettings(data, personRoot),
    });

    const started = Date.now();
    await verb(cli, ['runtime', 'start']);
    let stopped = false;
    try {
        const deps = seedDepsOnDisk({ cli, places, scratch: join(places.data, 'seed-scratch'), progress: (step) => console.log(`seed: ${step}`) });
        const outcome = await readyTheSample(deps, { begin: () => undefined, made: () => undefined });
        if (outcome.kind !== 'ready' || !outcome.seeded) {
            throw new Error(`The sample was not made: ${outcome.kind === 'failed' ? `${outcome.why} (${outcome.step ?? 'none'}, ${outcome.code ?? 'none'})` : 'it was there already'}.`);
        }
        const marker = readMarker(deps);
        if (marker === null || !marker.complete || marker.version !== seedVersion) {
            throw new Error(`The seed's marker does not say the sample is whole: ${JSON.stringify(marker)}.`);
        }
        console.log(`The sample is whole (seed version ${marker.version}), made in ${Math.round((Date.now() - started) / 1000)} s.`);
        await verb(cli, ['runtime', 'stop']);
        stopped = true;
    } finally {
        if (!stopped) await cli.run(['runtime', 'stop'], { timeoutMs: 60_000 });
    }

    // What the run logged is no part of the sample.
    rmSync(join(places.data, 'logs'), { recursive: true, force: true });
    mkdirSync(dirname(output), { recursive: true });
    await verb(cli, ['export', output]);
    console.log(`${output}: ${statSync(output).size} bytes`);
}

await main(process.argv.slice(2));
