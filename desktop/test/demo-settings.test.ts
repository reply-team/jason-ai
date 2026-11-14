// The demo installation's settings: written into its own settings file when the desktop makes the demo's directory,
// before the demo's runtime is ever started, so a terminal with JASON_DATA_DIR alone finds the same installation; the
// person's own settings are read for their harness root and never written.

import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { demoReviewSeconds, demoSettingsValues, mergeSettings, writeDemoSettings } from '../src/demo-settings.ts';
import { demoUnopened, enterRun, openDirectory, startOn, writeMode, type Environment } from '../src/mode.ts';
import { dataDirectory, dataLayout, desktopPaths } from '../src/paths.ts';
import { harnessRoot } from '../src/uninstall.ts';

const folder = () => mkdtempSync(join(tmpdir(), 'demo-settings-'));
const readJson = (file: string): Record<string, Record<string, unknown>> => JSON.parse(readFileSync(file, 'utf8')) as Record<string, Record<string, unknown>>;

/** A desktop start as the main module makes it, with the runtime's first start injected after it. */
function startWithRuntime(home: string, env: Environment, startRuntime: (env: Environment) => void) {
    const person = dataDirectory(env, home);
    const steps: string[] = [];
    const run = startOn(person, 'demo', false, {
        open: (dir) => {
            steps.push('open');
            openDirectory(dir);
        },
        prepare: (dir) => {
            steps.push('prepare');
            writeDemoSettings(dir, harnessRoot(env, home, dataLayout(person).userSettingsFile));
        },
        keep: (mode) => writeMode(desktopPaths(person).mode, mode),
    });
    enterRun(env, run);
    steps.push('runtime start');
    startRuntime({ ...env });
    return { run, steps };
}

test('the demo’s settings file holds every value before the demo’s runtime is first started, found by JASON_DATA_DIR alone', () => {
    const home = folder();
    let seen: Record<string, Record<string, unknown>> | null = null;
    const { run, steps } = startWithRuntime(home, { PATH: '/bin' }, (env) => {
        // What the runtime reads: the settings file of the data directory its environment names, nothing else.
        const file = dataLayout(dataDirectory({ JASON_DATA_DIR: env.JASON_DATA_DIR }, home)).userSettingsFile;
        assert.ok(existsSync(file), 'the file is there before the runtime starts');
        seen = readJson(file);
    });
    assert.deepEqual(steps, ['open', 'prepare', 'runtime start']);
    assert.equal(run.mode, 'demo');
    assert.equal(dataLayout(run.data).userSettingsFile, join(home, '.jason-demo', 'config', 'settings.json'));
    assert.deepEqual(seen, {
        Harness: { Root: join(home, 'Jason-demo') },
        Responder: { OnReply: false },
        Manager: { Triggers: ['decision_answered'], ReviewSeconds: 604800 },
        Dispatcher: { EventsCadenceSeconds: 60, TickSeconds: 1 },
    });
    assert.equal(demoReviewSeconds, 604800);
});

test('the harness root is the sibling of the person’s: their settings’ root, or their variable’s, else ~/Jason', () => {
    const home = folder();
    const person = join(home, '.jason');
    mkdirSync(join(person, 'config'), { recursive: true });
    writeFileSync(dataLayout(person).userSettingsFile, JSON.stringify({ harness: { root: join(home, 'Work', 'Agents') } }));
    const fromSettings = join(home, 'a-demo');
    writeDemoSettings(fromSettings, harnessRoot({}, home, dataLayout(person).userSettingsFile));
    assert.equal(readJson(dataLayout(fromSettings).userSettingsFile).Harness!.Root, join(home, 'Work', 'Agents-demo'));
    const fromVariable = join(home, 'b-demo');
    writeDemoSettings(fromVariable, harnessRoot({ JASON_Harness__Root: join(home, 'Roots') }, home, dataLayout(person).userSettingsFile));
    assert.equal(readJson(dataLayout(fromVariable).userSettingsFile).Harness!.Root, join(home, 'Roots-demo'));
});

test('the person’s own settings file is never touched', () => {
    const home = folder();
    const person = join(home, '.jason');
    mkdirSync(join(person, 'config'), { recursive: true });
    const theirs = dataLayout(person).userSettingsFile;
    const text = `${JSON.stringify({ Harness: { Root: join(home, 'Mine') }, Responder: { OnReply: true }, Plugins: { Config: { reply: { key: 'kept' } } } }, null, 2)}\n`;
    writeFileSync(theirs, text);
    const before = statSync(theirs).mtimeMs;
    startWithRuntime(home, {}, () => undefined);
    assert.equal(readFileSync(theirs, 'utf8'), text);
    assert.equal(statSync(theirs).mtimeMs, before);
    assert.equal(readJson(dataLayout(join(home, '.jason-demo')).userSettingsFile).Harness!.Root, join(home, 'Mine-demo'));
});

test('a settings file already in the demo keeps its other keys; the demo’s values replace theirs whatever the case', () => {
    const merged = mergeSettings(
        { harness: { root: '/old', Other: 1 }, Routes: { Default: { Plugin: 'demo' } }, Responder: { onreply: true }, Plugins: { Config: { demo: { story_start: 'x' } } } },
        demoSettingsValues('/new-demo'),
    );
    assert.deepEqual(merged, {
        harness: { Other: 1, Root: '/new-demo' },
        Routes: { Default: { Plugin: 'demo' } },
        Responder: { OnReply: false },
        Plugins: { Config: { demo: { story_start: 'x' } } },
        Manager: { Triggers: ['decision_answered'], ReviewSeconds: 604800 },
        Dispatcher: { EventsCadenceSeconds: 60, TickSeconds: 1 },
    });
});

test('a cheap cut: the demo’s own dispatcher ticks each second, in the demo’s settings alone, the harness default untouched', () => {
    assert.equal(demoSettingsValues('/r/Jason-demo').Dispatcher!.TickSeconds, 1);
    // One second is the shortest tick the runtime accepts, and its own default stays what it was.
    const options = readFileSync(join(import.meta.dirname, '..', '..', 'dotnet', 'src', 'Jason.Runtime', 'Configuration', 'DispatcherOptions.cs'), 'utf8');
    assert.match(options, /public int TickSeconds \{ get; set; \} = 10;/);
    const validators = readFileSync(join(import.meta.dirname, '..', '..', 'dotnet', 'src', 'Jason.Runtime', 'Configuration', 'OptionsValidators.cs'), 'utf8');
    assert.match(validators, /"Dispatcher:TickSeconds", options\.TickSeconds, 1, 3600\)/);
    // The person's own settings are never given it: only the demo's file is written.
    const home = folder();
    const person = join(home, '.jason');
    mkdirSync(join(person, 'config'), { recursive: true });
    writeFileSync(dataLayout(person).userSettingsFile, '{}');
    writeDemoSettings(join(home, '.jason-demo'), join(home, 'Jason'));
    assert.equal(readFileSync(dataLayout(person).userSettingsFile, 'utf8'), '{}');
});

test('the settings written name no route: the route is written beside the plugin’s placing, after these', () => {
    const demo = join(folder(), '.jason-demo');
    writeDemoSettings(demo, '/r/Jason');
    const written = readJson(dataLayout(demo).userSettingsFile);
    assert.deepEqual(Object.keys(written).sort(), ['Dispatcher', 'Harness', 'Manager', 'Responder']);
});

test('a demo settings file that is not a JSON object fails the preparation, and the start is the person’s own', () => {
    const home = folder();
    const demo = join(home, '.jason-demo');
    mkdirSync(join(demo, 'config'), { recursive: true });
    writeFileSync(dataLayout(demo).userSettingsFile, '[1, 2]');
    assert.throws(() => writeDemoSettings(demo, join(home, 'Jason')));
    const { run } = startWithRuntime(home, {}, () => undefined);
    assert.deepEqual(run, { person: join(home, '.jason'), data: join(home, '.jason'), mode: 'live', unopened: demoUnopened });
    assert.equal(readFileSync(dataLayout(demo).userSettingsFile, 'utf8'), '[1, 2]');
});
