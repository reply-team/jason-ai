// The demo's seed: a typed list of the harness's own verbs, run through the bundled jason on the demo's data directory.
// The rows hold the list to the command line's own verb map, the demo installation's settings to the story's rulings
// (a harness root of its own, no AI role, routes to the demo provider alone), the order to the one that makes replies
// safe (the story starts only after every approved send has succeeded), the run to its marker, the people to the
// provider package's one set, and "Start the demo over" to removing only the demo's two folders. The story the rows
// run on is a small fixture of the package module's shape, never a copy of the story.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { Cli, CliResult } from '../src/cli.ts';
import {
    buildSeed,
    cancelOpenReviews,
    operatorActor,
    openStatuses,
    demoPlaces,
    ensureSeeded,
    placesProblem,
    preparingPage,
    preparingSentence,
    readMarker,
    readyTheSample,
    seedVersion,
    startDemoOver,
    storyModule,
    verbsUsed,
    type SeedDeps,
    type SeedStep,
} from '../src/demo-seed.ts';
import { demoDirectory } from '../src/mode.ts';
import { parseStory, type Story } from '../src/demo-story.ts';

const repository = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const commands = join(repository, 'dotnet', 'src', 'Jason.Cli', 'Commands');
/** Options every verb shares: `--reason`, `--file`, `--human`. */
const shared = readFileSync(join(commands, 'VerbOptions.cs'), 'utf8');

const own = { data: join('C:', 'Users', 'someone', '.jason'), harness: join('C:', 'Users', 'someone', 'Jason') };
const places = demoPlaces(own.data, own.harness);

// ---------------------------------------------------------------------------------------------------------------------
// A fixture in the package module's shape: four campaigns (running, one with a send waiting, paused, draft).

function fixture(): Story {
    const person = (key: string, campaign: string, sends: { key: string; approval: 'given' | 'waiting' }[]): Story['people'][number] => ({
        key,
        first_name: key.toUpperCase(),
        last_name: 'Fixture',
        title: 'Operations Manager',
        company: 'co1',
        time_zone: 'America/Chicago',
        channels: [
            { channel: 'email', value: `${key}@fixture.example` },
            { channel: 'phone', value: '+1-555-0100' },
        ],
        campaign,
        sends: sends.map((send) => ({ key: send.key, operation: 'message.send', channel: 'email', approval: send.approval, subject: `Subject ${send.key}`, body: `Body ${send.key}` })),
        replies: [],
    });
    return {
        seller: { name: 'Fixture Seller', domain: 'fixture.example' },
        operator: { name: 'Fixture Operator', actor: 'human:fixture-operator' },
        project: { name: 'Fixture Project' },
        senders: [{ external_id: 's1', channel: 'email', name: 'Fixture Sender', address: 'sender@fixture.example' }],
        campaigns: [
            { key: 'run', name: 'Running', status: 'active', description: 'd1' },
            { key: 'ask', name: 'Asking', status: 'active', description: 'd2' },
            { key: 'hold', name: 'Held', status: 'paused', description: 'd3' },
            { key: 'new', name: 'Fresh', status: 'draft', description: 'd4' },
        ],
        companies: [{ key: 'co1', name: 'Fixture Freight', domain: 'fixture.example', city: 'Town', region: 'TX', trucks: 40 }],
        people: [
            person('a1', 'run', [{ key: 'sa1', approval: 'given' }]),
            person('a2', 'run', [{ key: 'sa2', approval: 'given' }]),
            person('b1', 'ask', [{ key: 'sb1', approval: 'waiting' }]),
            person('b2', 'ask', [{ key: 'sb2', approval: 'waiting' }]),
            person('b3', 'ask', [{ key: 'sb3', approval: 'given' }]),
            person('c1', 'hold', [{ key: 'sc1', approval: 'given' }]),
            person('d1', 'new', []),
        ],
    };
}

const moduleText = (story: Story): string => `export const story = ${JSON.stringify(story)};\n`;

// ---------------------------------------------------------------------------------------------------------------------

/** The command line's source files for a noun: `Runtime*.cs` for `runtime`, `WorkItem*.cs` for `workitem`, and so on. */
function sourceOf(noun: string): string {
    return readdirSync(commands)
        .filter((name) => name.toLowerCase().startsWith(noun) && name.endsWith('.cs'))
        .map((name) => readFileSync(join(commands, name), 'utf8'))
        .join('\n');
}

function optionsOf(step: SeedStep): string[] {
    const args = step.kind === 'verb' || step.kind === 'wait' || step.kind === 'wait-field' ? step.args : [];
    const named = args.filter((arg): arg is string => typeof arg === 'string' && arg.startsWith('--'));
    return step.kind === 'verb' && step.body ? [...named, step.body.option] : named;
}

test('every verb and option of the seed is one the command line has, read from its own source', () => {
    const steps = buildSeed(fixture());
    const pairs = verbsUsed(steps);
    assert.ok(pairs.length >= 10, pairs.join(', '));
    for (const pair of pairs) {
        const [noun = '', name = ''] = pair.split(' ');
        const source = sourceOf(noun);
        assert.match(source, new RegExp(`new Command\\(\\s*"${noun}"`), `no verb group ${noun}`);
        // A verb is a `new Command("name"` or, for approve and reject that share one builder, the name handed to it.
        assert.ok(source.includes(`"${name}"`), `jason ${noun} has no ${name}`);
    }
    for (const step of steps) {
        if (step.kind !== 'verb' && step.kind !== 'wait' && step.kind !== 'wait-field') continue;
        const noun = String(step.args[0]);
        const source = sourceOf(noun);
        for (const option of optionsOf(step)) {
            if (option === '--actor') continue;
            assert.ok(source.includes(`"${option}"`) || shared.includes(`"${option}"`), `jason ${noun} ${String(step.args[1])} has no ${option}`);
        }
    }
    // The options the cancel of a review and the approval use as well.
    for (const option of ['--campaign', '--kind', '--status']) assert.ok(sourceOf('workitem').includes(`"${option}"`));
    assert.ok(sourceOf('approval').includes('"--work-item"'));
});

test('the operator is named as a person whether the story writes the prefix or only the name', async () => {
    const bare = fixture();
    bare.operator.actor = 'fixture.operator';
    assert.equal(operatorActor(bare), 'human:fixture.operator');
    assert.equal(operatorActor(fixture()), 'human:fixture-operator');
    for (const step of buildSeed(bare)) {
        if (step.kind !== 'verb') continue;
        const at = step.args.indexOf('--actor');
        if (at >= 0) assert.equal(step.args[at + 1], 'human:fixture.operator');
    }
});

test('the open states a review is cancelled in are the work item statuses that are not terminal, read from the runtime source', () => {
    // dotnet/src/Jason.Contracts/Api/Enums.cs, `enum WorkItemStatus`: every status, in the words the verb takes (snake_case).
    const enums = readFileSync(join(repository, 'dotnet', 'src', 'Jason.Contracts', 'Api', 'Enums.cs'), 'utf8');
    const body = /enum WorkItemStatus\s*\{([^}]*)\}/.exec(enums)?.[1] ?? '';
    const all = [...body.matchAll(/^\s*([A-Z][A-Za-z]*),/gm)].map((m) => (m[1] ?? '').replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase());
    // dotnet/src/Jason.Runtime/WorkItems/WorkItemTransitions.cs, `Terminal`: finished, expired included.
    const transitions = readFileSync(join(repository, 'dotnet', 'src', 'Jason.Runtime', 'WorkItems', 'WorkItemTransitions.cs'), 'utf8');
    const terminalBody = /Terminal \{ get; \} =\s*new HashSet<WorkItemStatus> \{([^}]*)\}/.exec(transitions)?.[1] ?? '';
    const terminal = [...terminalBody.matchAll(/WorkItemStatus\.([A-Za-z]+)/g)].map((m) => (m[1] ?? '').replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase());
    assert.equal(all.length, 9);
    assert.equal(terminal.length, 4);
    assert.deepEqual([...openStatuses].sort(), all.filter((status) => !terminal.includes(status)).sort());
});

test('a seeded demo started again with one open review gets that review cancelled', async () => {
    const calls: string[] = [];
    const cli: Cli = {
        async run(args) {
            calls.push(args.join(' '));
            const items = args[1] === 'list' ? [{ id: 'wi_review_1' }] : [];
            return { exit: 0, stdout: `${JSON.stringify({ items })}
`, stderr: '' };
        },
    };
    assert.equal(await cancelOpenReviews(cli, 'human:fixture-operator'), null);
    assert.ok(calls[0]?.startsWith('workitem list --kind ai_role --status created'));
    assert.ok(!calls[0]?.includes('--campaign'), 'every campaign, not one');
    assert.equal(calls[1], 'workitem cancel wi_review_1 --reason no AI is connected in the demo --actor human:fixture-operator');
    // A refused cancel is said, not swallowed.
    const refusing: Cli = { async run(args) { return args[1] === 'list' ? { exit: 0, stdout: '{"items":[{"id":"wi_1"}]}', stderr: '' } : { exit: 1, stdout: '{"error":{"code":"x","message":"no","retryable":false}}', stderr: '' }; } };
    assert.deepEqual(await cancelOpenReviews(refusing, 'human:a'), { step: 'workitem cancel', detail: 'no' });
});

test('the real story the package carries: the one set runs through the seed, every person once, every given send approved and no waiting one', async () => {
    const text = readFileSync(join(repository, 'plugins', 'demo', 'modules', 'story.js'), 'utf8');
    const story = parseStory(text);
    assert.equal(story.people.length, 54);
    assert.equal(story.companies.length, 18);
    assert.equal(story.campaigns.length, 4);
    assert.equal(story.operator.actor, 'dana.whitfield');
    // Invented: only .example addresses, and every company a person names is one the module lists.
    for (const person of story.people) {
        for (const channel of person.channels) if (channel.channel === 'email') assert.match(channel.value, /^[a-z0-9.]+@[a-z0-9-]+\.example$/);
        assert.ok(story.companies.some((company) => company.key === person.company), `${person.key} names no company of the story`);
    }
    const run = await seeded({ text });
    assert.equal(run.outcome.kind, 'seeded');
    assert.equal(run.imported.length, 54);
    assert.equal(new Set(run.imported).size, 54);
    const sends = story.people.flatMap((person) => person.sends);
    const given = sends.filter((send) => send.approval === 'given');
    assert.ok(given.length > 0 && given.length < sends.length, 'the story has both approved and waiting sends');
    assert.equal(run.workItems.length, sends.filter((send) => story.campaigns.find((c) => c.key === story.people.find((p) => p.sends.includes(send))?.campaign)?.status !== 'draft').length);
    assert.equal(run.approved.length, given.length);
    assert.ok(run.approved.every((line) => line.includes('--actor human:dana.whitfield')));
    const senderOf = (item: { body: unknown }) => (item.body as { context: { input: { sender: { external_id: string; address: string } } } }).context.input.sender;
    for (const item of run.workItems) assert.deepEqual(senderOf(item), { external_id: 'demo-sender-email', address: 'dana@tillerwave.example' });
    assert.ok(run.events.indexOf('write story_start') > run.events.map((e, i) => (e.includes('=succeeded') ? i : -1)).reduce((a, b) => Math.max(a, b), -1));
});

test('no step is called demo: the nouns are the harness\'s own', () => {
    for (const pair of verbsUsed(buildSeed(fixture()))) assert.doesNotMatch(pair, /demo|seed/i);
});

test('the demo\'s two folders are the siblings the one rule makes, beside the person\'s and inside neither data directory', () => {
    assert.equal(places.data, demoDirectory(own.data));
    assert.equal(places.harnessRoot, demoDirectory(own.harness));
    assert.equal(placesProblem(places, own.data, own.harness), null);
    assert.notEqual(places.harnessRoot, own.harness);
    assert.notEqual(places.data, own.data);
    // The refusals the rule makes.
    assert.notEqual(placesProblem({ data: places.data, harnessRoot: join(places.data, 'harness') }, own.data, own.harness), null);
    assert.notEqual(placesProblem({ data: places.data, harnessRoot: join(own.data, 'demo') }, own.data, own.harness), null);
    assert.notEqual(placesProblem({ data: places.data, harnessRoot: join(own.harness, 'demo') }, own.data, own.harness), null);
    assert.notEqual(placesProblem({ data: join(places.harnessRoot, 'data'), harnessRoot: places.harnessRoot }, own.data, own.harness), null);
});

test('the seed does only the run\'s work: no route, no plugin, no runtime start and none of the fixed settings', async () => {
    for (const step of buildSeed(fixture())) {
        assert.ok(['verb', 'wait', 'wait-field', 'approve', 'cancel-reviews', 'story-start'].includes(step.kind), step.kind);
        if (step.kind !== 'verb') continue;
        assert.ok(!['route', 'runtime'].includes(String(step.args[0])), `jason ${String(step.args[0])} is not the run's work`);
        assert.ok(!step.args.includes('--plugin'));
    }
    // The only thing the seed writes to the settings file is the moment the story starts, in the demo provider's configuration.
    const run = await seeded();
    assert.deepEqual(JSON.parse(run.settings()), { Plugins: { Config: { demo: { story_start: '2026-11-13T12:00:00.000Z' } } } });
});

test('the story is read from the package\'s data module and from nothing else: one literal, nothing evaluated', () => {
    assert.ok(storyModule(places.data).endsWith(join('plugins', 'demo', 'modules', 'story.js')));
    assert.equal(parseStory(moduleText(fixture())).project.name, 'Fixture Project');
    const refused = (text: string) => assert.throws(() => parseStory(text));
    refused(`import x from 'y';\n${moduleText(fixture())}`);
    refused(`${moduleText(fixture())}console.log(1);\n`);
    refused(`export const story = ${JSON.stringify(fixture())}`);
    refused(moduleText(fixture()).replace('Fixture Seller', 'Fixture Sellér'));
    refused(`export const story = (() => ${JSON.stringify(fixture())})();\n`);
    const loose = fixture();
    loose.people[0]!.campaign = 'nowhere';
    refused(moduleText(loose));
});

test('the order of the story: approved sends succeed first, a paused campaign is paused after, and only then does the story start', async () => {
    const run = await seeded();
    assert.equal(run.outcome.kind, 'seeded');
    const at = (needle: string | RegExp, from = 0) => run.events.findIndex((event, index) => index >= from && (typeof needle === 'string' ? event.includes(needle) : needle.test(event)));
    const start = at('write story_start');
    assert.ok(start > 0, 'the provider is told when the story starts');
    const lastApproval = run.events.map((event, index) => (event.startsWith('cli approval approve') ? index : -1)).filter((index) => index >= 0).at(-1) ?? -1;
    const lastDone = run.events.map((event, index) => (event.startsWith('cli workitem get') && event.includes('=succeeded') ? index : -1)).filter((index) => index >= 0).at(-1) ?? -1;
    assert.ok(lastApproval > 0 && lastDone > lastApproval, 'every approved send succeeded after it was approved');
    assert.ok(start > lastDone, 'the story starts after the last approved send succeeded');
    assert.ok(at('cli campaign pause') > lastDone && at('cli campaign pause') < start, 'the paused campaign is paused after its given sends');
    assert.equal(run.events.at(-1), 'cli plugin reload', 'the plugins are reloaded last, after the story_start is set');
    assert.equal(run.events.at(-2), 'write story_start');
    const parked = run.events.filter((event) => event.startsWith('cli approval list --work-item'));
    assert.ok(parked.length >= 5, 'every given send and every waiting send of a running campaign was seen parked');
    // The moment is the seed's own clock, an ISO moment.
    assert.match(run.settings(), /"story_start": "2026-11-13T12:00:00\.000Z"/);
});

test('a fake runtime lets the whole seed run: refs are kept in order, every person is made once, the marker is written last', async () => {
    const story = fixture();
    const run = await seeded();
    assert.equal(run.outcome.kind, 'seeded');
    assert.deepEqual(readMarker(run.deps), { version: seedVersion, complete: true });
    assert.equal(run.writes.at(-1)?.file, run.deps.markerFile, 'the marker is the last thing written');
    assert.equal(run.calls.filter((c) => c.startsWith('project create')).length, 1);
    assert.equal(run.calls.filter((c) => c.startsWith('campaign create')).length, 4);
    // Every person is made once, in the campaign the story gives them, through their email and phone.
    assert.deepEqual(run.imported.sort(), story.people.map((p) => p.channels[0]?.value).sort());
    // The draft is never started; the running, the asking and the paused are, and the paused is paused.
    assert.equal(run.calls.filter((c) => c.startsWith('campaign start')).length, 3);
    assert.equal(run.calls.filter((c) => c.startsWith('campaign pause')).length, 1);
    // Reviews a start queued are cancelled right after it, with the existing verb, in the operator's name.
    assert.equal(run.cancelled.length, 3);
    for (const call of run.calls.filter((c) => c.startsWith('workitem list'))) assert.equal(call.split(' --status ').length - 1, openStatuses.length);
    assert.ok(run.cancelled.every((line) => line.includes('--actor human:fixture-operator')));
    // Only the sends the story gives as approved were approved, each in the operator's name; the waiting ones were left.
    assert.equal(run.approved.length, 4);
    assert.ok(run.approved.every((line) => line.includes('--actor human:fixture-operator')));
    assert.equal(run.workItems.length, 6);
    // Each work item is a send of the demo provider's kind with its arguments under context.input, from the story's sender.
    for (const item of run.workItems) {
        assert.ok(item.args.includes('provider_op') && item.args.includes('message.send'));
        const input = (item.body as { context: { input: { channel: string; sender: { external_id: string; address: string }; message: { format: string; subject: string; body: string } } } }).context.input;
        assert.equal(input.channel, 'email');
        assert.deepEqual(input.sender, { external_id: 's1', address: 'sender@fixture.example' });
        assert.equal(input.message.format, 'email');
        assert.match(input.message.subject, /^Subject s/);
    }
    // No question is raised and no route written: no verb does the first without a role's run, and the settings hold the second.
    assert.equal(run.calls.some((c) => c.startsWith('decision') || c.startsWith('route')), false);
});

test('a seeded directory with the same version is not seeded again, and one cut short is never built on', async () => {
    const first = await seeded();
    const before = first.calls.length;
    assert.deepEqual(await ensureSeeded(first.deps), { kind: 'already-seeded' });
    assert.equal(first.calls.length, before, 'nothing is asked of the runtime');
    // A newer story over an older seed: the old directory is not built on.
    assert.deepEqual(await ensureSeeded(first.deps, seedVersion + 1), { kind: 'needs-start-over' });
    assert.equal(first.calls.length, before);
    // A seed that failed half-way leaves a marker that is not complete.
    const cut = await seeded({ failAt: 'campaign create' });
    assert.equal(cut.outcome.kind, 'failed');
    assert.deepEqual(readMarker(cut.deps), { version: seedVersion, complete: false });
    const calls = cut.calls.length;
    assert.deepEqual(await ensureSeeded(cut.deps), { kind: 'needs-start-over' });
    assert.equal(cut.calls.length, calls);
});

test('a cut-short seed is said as the step that failed, with the runtime\'s own words', async () => {
    const cut = await seeded({ failAt: 'project create' });
    assert.equal(cut.outcome.kind, 'failed');
    assert.equal(cut.outcome.kind === 'failed' && cut.outcome.step, 'project create');
    assert.equal(cut.outcome.kind === 'failed' && cut.outcome.detail, 'said by the fake');
});

test('a send that never succeeds ends the wait as a failure naming it, and the story never starts', async () => {
    const stuck = await seeded({ neverSucceed: true });
    assert.equal(stuck.outcome.kind === 'failed' && stuck.outcome.step, 'workitem get');
    assert.ok(!stuck.events.includes('write story_start'));
});

test('the seed and the provider package work from one set: the desktop holds no copy of it', async () => {
    const story = fixture();
    const run = await seeded();
    // What the seed makes is what the module held, nothing added: the subjects of the sends are the story's.
    const subjects = run.workItems.map((item) => (item.body as { context: { input: { message: { subject: string } } } }).context.input.message.subject).sort();
    assert.deepEqual(subjects, story.people.flatMap((p) => p.sends.map((s) => s.subject)).sort());
    // And no person's address, no company's name and no message is written in the desktop's sources.
    const sources = readdirSync(join(repository, 'desktop', 'src')).map((name) => readFileSync(join(repository, 'desktop', 'src', name), 'utf8')).join('\n');
    assert.doesNotMatch(sources, /@[a-z]+\.example/);
    assert.doesNotMatch(sources, /Dana Whitfield|Tillerwave|Last-Mile/);
});

test('"Start the demo over" removes the demo\'s two folders and nothing else, then relaunches, and the start after it seeds', async () => {
    const run = await seeded();
    const removed: string[] = [];
    let relaunched = 0;
    const seed = { ...run.deps, removeTree: (path: string) => void removed.push(path), readText: (file: string) => (file === run.deps.markerFile ? null : run.deps.readText(file)) };
    const outcome = await startDemoOver({ seed, exists: () => true, marked: () => true, stopRuntime: async () => true, relaunch: () => void (relaunched += 1) });
    assert.deepEqual(outcome, { kind: 'started-over' });
    assert.deepEqual(removed.slice(0, 2).sort(), [places.data, places.harnessRoot].sort());
    assert.ok(!removed.includes(own.data) && !removed.includes(own.harness));
    assert.equal(relaunched, 1);
});

test('"Start the demo over" removes a folder only when it carries the marker: one without it is left and named, and nothing is stopped', async () => {
    const run = await seeded();
    const removed: string[] = [];
    let stopped = 0;
    let relaunched = 0;
    const outcome = await startDemoOver({
        seed: { ...run.deps, removeTree: (path: string) => void removed.push(path) },
        exists: () => true,
        marked: (folder) => folder !== places.harnessRoot,
        stopRuntime: async () => { stopped += 1; return true; },
        relaunch: () => void (relaunched += 1),
    });
    assert.equal(outcome.kind, 'refused');
    assert.ok(outcome.kind === 'refused' && outcome.why.includes(places.harnessRoot));
    assert.deepEqual(removed, [], 'the marked folder is not removed either: the act is refused whole');
    assert.equal(stopped, 0);
    assert.equal(relaunched, 0);
    // A folder that is not there is nothing to remove, and is not refused.
    const absent: string[] = [];
    const again = await startDemoOver({ seed: { ...run.deps, removeTree: (path: string) => void absent.push(path) }, exists: (folder) => folder === places.data, marked: () => true, stopRuntime: async () => true, relaunch: () => undefined });
    assert.equal(again.kind, 'started-over');
    assert.deepEqual(absent, [places.data]);
});

test('"Start the demo over" leaves everything as it is when the demo\'s runtime cannot be stopped', async () => {
    const run = await seeded();
    const removed: string[] = [];
    let relaunched = 0;
    const outcome = await startDemoOver({ seed: { ...run.deps, removeTree: (path: string) => void removed.push(path) }, exists: () => true, marked: () => true, stopRuntime: async () => false, relaunch: () => void (relaunched += 1) });
    assert.equal(outcome.kind, 'refused');
    assert.deepEqual(removed, []);
    assert.equal(relaunched, 0);
});

test('a start of a demo with no marker shows the preparing page, seeds, and cancels the open reviews; the next start only cancels', async () => {
    const run = await seeded();
    // The marker is read as absent: a fresh demo.
    const fresh = { ...run.deps, readText: (file: string) => (file === run.deps.markerFile ? null : run.deps.readText(file)) };
    let shown = 0;
    const before = run.calls.length;
    const first = await readyTheSample(fresh, async () => void (shown += 1));
    assert.deepEqual(first, { kind: 'ready', seeded: true });
    assert.equal(shown, 1, 'the preparing page is shown once, before the seed');
    assert.ok(run.calls.length > before);
    // The seeded demo started again: no page, no seed, the reviews cancelled.
    const again: string[] = [];
    const spy = { ...run.deps, cli: { async run(args: readonly string[]) { again.push(args.join(' ')); return run.deps.cli.run(args); } } as Cli };
    assert.deepEqual(await readyTheSample(spy, async () => void (shown += 1)), { kind: 'ready', seeded: false });
    assert.equal(shown, 1);
    assert.ok(again.every((line) => line.startsWith('workitem ')), again.join(' | '));
    assert.ok(again.some((line) => line.startsWith('workitem list --kind ai_role')));
});

test('a demo whose seed was not finished is not built on: the start says so and points to the menu', async () => {
    const run = await seeded();
    const unfinished = { ...run.deps, readText: (file: string) => (file === run.deps.markerFile ? '{"version":1,"complete":false}' : run.deps.readText(file)) };
    const outcome = await readyTheSample(unfinished, async () => undefined);
    assert.equal(outcome.kind, 'failed');
    assert.match(outcome.kind === 'failed' ? outcome.why : '', /Start the Demo Over/);
});

test('while the seed runs the window says the sample is being prepared, in a page with no script and no outside file', async () => {
    const run = await seeded();
    assert.equal(run.progress[0], 'Preparing the sample');
    const page = preparingPage();
    assert.ok(page.includes(preparingSentence));
    assert.doesNotMatch(page, /<script|src=|href=|https?:/i);
    assert.match(page, /default-src 'none'/);
});

// ---------------------------------------------------------------------------------------------------------------------

interface Run {
    outcome: Awaited<ReturnType<typeof ensureSeeded>>;
    deps: SeedDeps;
    calls: string[];
    /** Every verb and every write that matters, in the order they happened. */
    events: string[];
    writes: { file: string; text: string }[];
    imported: string[];
    workItems: { campaign: string; args: string[]; body: unknown }[];
    approved: string[];
    cancelled: string[];
    progress: string[];
    settings(): string;
}

/** Runs the seed against a fake runtime that answers the way the real verbs do, with an in-memory disk. */
async function seeded(options: { failAt?: string; neverSucceed?: boolean; text?: string } = {}): Promise<Run> {
    const story = options.text === undefined ? fixture() : parseStory(options.text);
    const files = new Map<string, string>();
    const calls: string[] = [];
    const events: string[] = [];
    const writes: { file: string; text: string }[] = [];
    const imported: string[] = [];
    const workItems: Run['workItems'] = [];
    const approved: string[] = [];
    const cancelled: string[] = [];
    const progress: string[] = [];
    const parked: { id: string; item: string; campaign: string }[] = [];
    const decided = new Set<string>();
    const started = new Set<string>();
    let contacts = 0;

    const ok = (value: unknown): CliResult => ({ exit: 0, stdout: `${JSON.stringify(value)}\n`, stderr: '' });
    const bodyOf = (args: readonly string[]): unknown => {
        const at = args.indexOf('--file');
        const text = at >= 0 ? files.get(String(args[at + 1])) : undefined;
        return text === undefined ? undefined : JSON.parse(text);
    };
    const value = (args: readonly string[], option: string): string => String(args[args.indexOf(option) + 1]);

    const cli: Cli = {
        async run(args) {
            const line = args.join(' ');
            calls.push(line);
            const pair = `${args[0]} ${args[1]}`;
            events.push(pair === 'workitem get' ? `cli ${line}=${decided.has(String(args[2])) && !options.neverSucceed ? 'succeeded' : 'awaiting_approval'}` : `cli ${line}`);
            if (options.failAt === pair) return { exit: 1, stdout: JSON.stringify({ error: { code: 'refused', message: 'said by the fake', retryable: false } }), stderr: '' };
            switch (pair) {
                case 'project create':
                    return ok({ id: 'prj_1' });
                case 'campaign create': {
                    const body = bodyOf(args) as { name: string };
                    return ok({ id: `cmp_${story.campaigns.find((c) => c.name === body.name)?.key}` });
                }
                case 'campaign add-contacts': {
                    const members = bodyOf(args) as { channels: { value: string }[] }[];
                    for (const member of members) imported.push(String(member.channels[0]?.value));
                    return ok({ items: members.map(() => ({ contact_id: `cnt_${(contacts += 1)}` })) });
                }
                case 'campaign start':
                    started.add(String(args[2]));
                    return ok({});
                case 'workitem create': {
                    workItems.push({ campaign: String(args[2]), args: [...args], body: bodyOf(args) });
                    const id = `wi_${workItems.length}`;
                    // An item of a started, running campaign parks; the paused one is claimed before it is paused.
                    parked.push({ id: `apr_${workItems.length}`, item: id, campaign: String(args[2]) });
                    return ok({ id });
                }
                case 'workitem list':
                    return ok({ items: [{ id: `wi_review_${args[3]}` }] });
                case 'workitem cancel':
                    cancelled.push(line);
                    return ok({});
                case 'workitem get':
                    return ok({ status: decided.has(String(args[2])) && !options.neverSucceed ? 'succeeded' : 'awaiting_approval' });
                case 'approval list':
                    return ok({ items: parked.filter((p) => p.item === value(args, '--work-item') && !decided.has(p.item)) });
                case 'approval approve': {
                    approved.push(line);
                    const found = parked.find((p) => p.id === args[2]);
                    if (found) decided.add(found.item);
                    return ok({});
                }
                default:
                    return ok({});
            }
        },
    };

    const deps: SeedDeps = {
        cli,
        places,
        markerFile: join(places.data, 'demo-seed.json'),
        readText: (file) => files.get(file) ?? null,
        writeText: (file, text) => {
            files.set(file, text);
            writes.push({ file, text });
            if (text.includes('story_start')) events.push('write story_start');
        },
        makeDirectory: () => undefined,
        removeTree: () => undefined,
        readStory: () => options.text ?? moduleText(story),
        progress: (message) => void progress.push(message),
        sleep: async () => undefined,
        now: () => new Date('2026-11-13T12:00:00.000Z'),
        scratch: join(places.data, 'seed-scratch'),
        waitLimitMs: 0,
    };
    const outcome = await ensureSeeded(deps);
    return { outcome, deps, calls, events, writes, imported, workItems, approved, cancelled, progress, settings: () => files.get(join(places.data, 'config', 'settings.json')) ?? '' };
}
