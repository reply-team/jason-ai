// The demo's seed: a typed list of the harness's own verbs, run through the bundled jason on the demo's data directory.
// The rows hold the list to the command line's own verb map, the demo installation's settings to the story's rulings
// (a harness root of its own, no AI role, routes to the demo provider alone), the order to the one that makes replies
// safe (the story starts only after every approved send has succeeded), the run to its marker, the people to the
// provider package's one set, and "Start the demo over" to removing only the demo's two folders. The story the rows
// run on is a small fixture of the package module's shape, never a copy of the story.

import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { Cli, CliResult } from '../src/cli.ts';
import { knownDesktopEntries } from '../src/chat-hosts.ts';
import { claimDemoFolder, hasDemoMarker } from '../src/demo-folders.ts';
import { askedBy, carryOutOnDisk, CarryOutRefused, carryOutStartOver, leftByAMaking, removeMarkedLast, requestStartOver, requestYoungMs } from '../src/demo-over.ts';
import { demoRuntimeAlive } from '../src/demo-alive.ts';
import { prepareDemo } from '../src/demo-start.ts';
import { fileLogger } from '../src/log.ts';
import { desktopPaths } from '../src/paths.ts';
import {
    buildSeed,
    cancelOpenReviews,
    operatorActor,
    openStatuses,
    demoPlaces,
    ensureSeeded,
    openOnTheSample,
    placesProblem,
    readMarker,
    readyTheSample,
    SampleNotReady,
    seedDepsOnDisk,
    seedWaitStepMs,
    seedVersion,
    startDemoOver,
    storyModule,
    verbsUsed,
    type Preparing,
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

/**
 * The source of one verb: from its `new Command("name"` to the next method declaration. A verb built by a shared
 * builder (`approve` and `reject`, the campaign transitions) has no block of its own and gives null.
 */
function verbBlock(noun: string, name: string): string | null {
    const source = sourceOf(noun);
    const start = source.search(new RegExp(`new Command\\(\\s*"${name}"`));
    if (start < 0) return null;
    const rest = source.slice(start + 1);
    const next = rest.search(/\n\s*(private|public|internal) static /);
    return next < 0 ? rest : rest.slice(0, next);
}

/** The shared option builders in `VerbOptions.cs` and the option each one makes. */
const sharedBuilders: Record<string, string> = { '--file': 'VerbOptions.File(', '--reason': 'VerbOptions.Reason(', '--project': 'ProjectOption(' };

test('every verb and option of the seed is one the command line has, read from that verb\'s own source', () => {
    const steps = buildSeed(fixture());
    const pairs = verbsUsed(steps);
    assert.ok(pairs.length >= 10, pairs.join(', '));
    for (const pair of pairs) {
        const [noun = '', name = ''] = pair.split(' ');
        const source = sourceOf(noun);
        assert.match(source, new RegExp(`new Command\\(\\s*"${noun}"`), `no verb group ${noun}`);
        // A verb is a `new Command("name"` or, for a shared builder, the name handed to it.
        assert.ok(source.includes(`"${name}"`), `jason ${noun} has no ${name}`);
    }
    let held = 0;
    for (const step of steps) {
        if (step.kind !== 'verb' && step.kind !== 'wait' && step.kind !== 'wait-field') continue;
        const noun = String(step.args[0]);
        const name = String(step.args[1]);
        const block = verbBlock(noun, name) ?? sourceOf(noun);
        for (const option of optionsOf(step)) {
            if (option === '--actor') continue;
            const builder = sharedBuilders[option];
            const own = block.includes(`"${option}"`) || (builder !== undefined && block.includes(builder));
            assert.ok(own, `jason ${noun} ${name} has no ${option} of its own`);
            held += 1;
        }
    }
    assert.ok(held >= 8, `${held} options held`);
    // The review's listing takes its filters from the class the listing and the count share (`ListingFilter`).
    const filters = /class ListingFilter[\s\S]*?(?=\n    (private|public|internal) )/.exec(sourceOf('workitem'))?.[0] ?? '';
    for (const option of ['--campaign', '--kind', '--status']) assert.ok(filters.includes(`"${option}"`), `the work item listing has no ${option}`);
    assert.ok((verbBlock('approval', 'list') ?? sourceOf('approval')).includes('"--work-item"'));
    assert.ok(shared.includes('Reason(') && shared.includes('File('));
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
    assert.deepEqual(await cancelOpenReviews(refusing, 'human:a'), { step: 'workitem cancel', detail: 'no', code: 'x' });
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

const startOverDeps = (over: Partial<Parameters<typeof startDemoOver>[0]> = {}) => {
    const seen = { stopped: 0, requested: 0, relaunched: 0 };
    const deps: Parameters<typeof startDemoOver>[0] = {
        places,
        exists: () => true,
        marked: () => true,
        stopRuntime: async () => { seen.stopped += 1; return true; },
        request: () => void (seen.requested += 1),
        relaunch: () => void (seen.relaunched += 1),
        ...over,
    };
    return { deps, seen };
};

test('"Start the demo over" stops the runtime, leaves a request and relaunches, and removes nothing itself', async () => {
    const { deps, seen } = startOverDeps();
    assert.deepEqual(await startDemoOver(deps), { kind: 'started-over' });
    assert.deepEqual(seen, { stopped: 1, requested: 1, relaunched: 1 });
});

test('"Start the demo over" leaves a folder without the marker and names it, before anything stops', async () => {
    const { deps, seen } = startOverDeps({ marked: (folder) => folder !== places.harnessRoot });
    const outcome = await startDemoOver(deps);
    assert.equal(outcome.kind, 'refused');
    assert.ok(outcome.kind === 'refused' && outcome.why.includes(places.harnessRoot));
    assert.deepEqual(seen, { stopped: 0, requested: 0, relaunched: 0 });
    // A folder that is not there is not refused.
    const absent = startOverDeps({ exists: (folder) => folder === places.data });
    assert.equal((await startDemoOver(absent.deps)).kind, 'started-over');
});

test('"Start the demo over" leaves everything as it is when the runtime cannot be stopped or the request cannot be written', async () => {
    const stuck = startOverDeps({ stopRuntime: async () => false });
    assert.equal((await startDemoOver(stuck.deps)).kind, 'refused');
    assert.deepEqual(stuck.seen, { stopped: 0, requested: 0, relaunched: 0 });
    const unwritable = startOverDeps({ request: () => { throw new Error('no'); } });
    assert.equal((await startDemoOver(unwritable.deps)).kind, 'refused');
    assert.equal(unwritable.seen.relaunched, 0);
});

/** A real temp disk: the person's folder and the demo's two, with the real marker, the real logger and the real removal. */
function disk() {
    const root = mkdtempSync(join(tmpdir(), 'jason-over-'));
    const person = join(root, 'jason');
    const demo = { data: join(root, 'jason-demo'), harnessRoot: join(root, 'harness-demo') };
    return { root, person, demo, request: join(person, 'desktop', 'demo-start-over') };
}

/** The carry-out over the real disk, with the asking process, the runtime and the removal named by the row. */
function carry(d: ReturnType<typeof disk>, over: Partial<Parameters<typeof carryOutStartOver>[0]> = {}): void {
    carryOutStartOver({
        ...carryOutOnDisk(d.request, d.demo, () => false),
        runtimeAlive: () => false,
        wait: () => undefined,
        exists: existsSync,
        marked: hasDemoMarker,
        ...over,
    });
}

const claimBoth = (d: ReturnType<typeof disk>): void => {
    claimDemoFolder(d.demo.data);
    claimDemoFolder(d.demo.harnessRoot);
};

const aside = (d: ReturnType<typeof disk>): string[] => readdirSync(d.root).filter((name) => name.includes('.removing-'));

test('on a real disk: start over, a log line written inside the demo, then the next start claims fresh folders', async () => {
    const d = disk();
    try {
        claimBoth(d);
        writeFileSync(join(d.demo.data, 'demo-seed.json'), '{"version":1,"complete":true}');
        const log = fileLogger(join(d.demo.data, 'logs'));
        const outcome = await startDemoOver({
            places: d.demo,
            exists: existsSync,
            marked: hasDemoMarker,
            stopRuntime: async () => true,
            request: () => requestStartOver(d.request, 999_999_999),
            relaunch: () => undefined,
        });
        assert.equal(outcome.kind, 'started-over');
        // The run that is leaving still logs inside the demo: harmless, because its folders were not removed.
        log.info('demo_start_over', {});
        assert.ok(hasDemoMarker(d.demo.data));
        carry(d);
        assert.ok(!existsSync(d.demo.data) && !existsSync(d.demo.harnessRoot) && !existsSync(d.request));
        assert.deepEqual(aside(d), [], 'the renamed copies are removed');
        claimBoth(d);
        assert.ok(hasDemoMarker(d.demo.data) && hasDemoMarker(d.demo.harnessRoot));
        assert.ok(!existsSync(join(d.demo.data, 'demo-seed.json')), 'a fresh demo has no seed marker');
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('a start with no request changes nothing; an unmarked folder is left and the request kept', () => {
    const d = disk();
    try {
        claimBoth(d);
        carry(d);
        assert.ok(hasDemoMarker(d.demo.data) && hasDemoMarker(d.demo.harnessRoot));
        requestStartOver(d.request, 999_999_999);
        rmSync(join(d.demo.harnessRoot, '.jason-demo-folder'));
        writeFileSync(join(d.demo.harnessRoot, 'mine.txt'), 'x');
        assert.throws(() => carry(d), /not one the demo made/);
        assert.ok(existsSync(join(d.demo.harnessRoot, 'mine.txt')) && existsSync(d.request));
        assert.ok(hasDemoMarker(d.demo.data), 'the marked folder is not touched either');
        assert.deepEqual(aside(d), []);
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('a folder that cannot be taken away changes nothing: the marker stays, the request is kept, and the next carry-out works', () => {
    const d = disk();
    try {
        claimBoth(d);
        writeFileSync(join(d.demo.data, 'a-file'), 'x');
        requestStartOver(d.request, 999_999_999);
        const real = carryOutOnDisk(d.request, d.demo, () => false);
        // The first folder moves, the second cannot (a file inside held open on Windows): the rename is one step each, so
        // whichever fails has lost nothing, and the one that moved is carried on with at the next start.
        let moves = 0;
        assert.throws(() => carry(d, { setAside: (folder) => { moves += 1; if (moves === 2) throw new Error('EPERM'); return real.setAside(folder); } }), /could not be taken away/);
        assert.ok(hasDemoMarker(d.demo.harnessRoot), 'the folder that could not move keeps its marker');
        assert.ok(existsSync(d.request));
        carry(d);
        assert.ok(!existsSync(d.demo.harnessRoot) && !existsSync(d.request));
        assert.deepEqual(aside(d), []);
        claimBoth(d);
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('a copy that cannot be removed is left marked beside, and the next carry-out removes it; an unmarked one named like it is not touched', () => {
    const d = disk();
    try {
        claimBoth(d);
        requestStartOver(d.request, 999_999_999);
        carry(d, { removeTree: () => { throw new Error('EBUSY'); } });
        assert.ok(!existsSync(d.request), 'the folders are out of the demo\'s name, so the request is done');
        assert.equal(aside(d).length, 2);
        const stranger = join(d.root, 'jason-demo.removing-1');
        mkdirSync(stranger);
        writeFileSync(join(stranger, 'theirs.txt'), 'x');
        requestStartOver(d.request, 999_999_999);
        carry(d);
        assert.deepEqual(aside(d), ['jason-demo.removing-1'], 'only the stranger is left');
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('the request waits for the run that asked and for a live demo runtime, and keeps itself', () => {
    const d = disk();
    try {
        claimBoth(d);
        requestStartOver(d.request, 4242);
        assert.throws(() => carry(d, { running: (pid) => pid === 4242 }), /still ending/);
        assert.throws(() => carry(d, { runtimeAlive: () => true }), /runtime is still running/);
        assert.ok(existsSync(d.request) && hasDemoMarker(d.demo.data) && hasDemoMarker(d.demo.harnessRoot));
        assert.equal(askedBy(readFileSync(d.request, 'utf8')), 4242);
        // The asking run gone and no runtime: it goes through.
        carry(d, { running: () => false });
        assert.ok(!existsSync(d.demo.data) && !existsSync(d.request));
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('a demo runtime is alive when its run file names a running process or cannot be read, and not otherwise', () => {
    const file = '/x/run/runtime.json';
    const on = (text: string | null, running: boolean, written = 2_000) => ({ exists: () => text !== null, read: () => text ?? '', running: () => running, modifiedMs: () => written, bootMs: () => 1_000 });
    assert.equal(demoRuntimeAlive(file, on(null, true)), false, 'no run file');
    assert.equal(demoRuntimeAlive(file, on('{"pid":77}', true)), true);
    assert.equal(demoRuntimeAlive(file, on('{"pid":77}', false)), false);
    assert.equal(demoRuntimeAlive(file, on('{', false)), true, 'unreadable is not known to be dead');
    assert.equal(demoRuntimeAlive(file, on('{"pid":"x"}', false)), true);
    assert.equal(demoRuntimeAlive(file, on('{"pid":77}', true, 500)), false, 'a run file older than the boot names no runtime, whoever has the pid now');
});

test('preparing the demo under a live runtime removes, copies and writes nothing', () => {
    const calls: string[] = [];
    const deps = { harnessRoot: () => void calls.push('harness'), place: () => void calls.push('place'), route: () => void calls.push('route'), fixedSettings: () => void calls.push('settings') };
    prepareDemo('/d', { ...deps, runtimeAlive: () => true });
    assert.deepEqual(calls, []);
    prepareDemo('/d', { ...deps, runtimeAlive: () => false });
    assert.deepEqual(calls, ['harness', 'place', 'route', 'settings']);
});

test('a request older than two minutes ignores the asking pid; a young one is waited for, then refused with a code', () => {
    const d = disk();
    try {
        claimBoth(d);
        requestStartOver(d.request, 4242);
        const waits: number[] = [];
        // Young and the pid stays: waited for a bounded time, then refused by a code.
        let refusal: unknown;
        try {
            carry(d, { running: () => true, wait: (ms) => void waits.push(ms), askingWaitMs: 1_000, requestAgeMs: () => 5_000 });
        } catch (error) {
            refusal = error;
        }
        assert.ok(refusal instanceof CarryOutRefused && refusal.reason === 'asking_alive');
        assert.equal(waits.reduce((a, b) => a + b, 0), 1_000, 'the wait is bounded');
        // Young and the pid ends while waited for: it goes through at once.
        let polls = 0;
        const young = disk();
        try {
            claimBoth(young);
            requestStartOver(young.request, 4242);
            carry(young, { running: () => (polls += 1) < 3, requestAgeMs: () => 5_000 });
            assert.ok(!existsSync(young.demo.data) && !existsSync(young.request));
        } finally {
            rmSync(young.root, { recursive: true, force: true });
        }
        // Old: the pid is anyone's now, and is not looked at.
        carry(d, { running: () => true, requestAgeMs: () => requestYoungMs + 1 });
        assert.ok(!existsSync(d.demo.data) && !existsSync(d.request));
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('every refusal of a carry-out carries a code', () => {
    const d = disk();
    try {
        claimBoth(d);
        requestStartOver(d.request, 1);
        const reason = (over: Partial<Parameters<typeof carryOutStartOver>[0]>): string => {
            try {
                carry(d, { requestAgeMs: () => 0, ...over });
            } catch (error) {
                return error instanceof CarryOutRefused ? error.reason : 'none';
            }
            return 'carried';
        };
        assert.equal(reason({ runtimeAlive: () => true, running: () => false }), 'runtime_alive');
        assert.equal(reason({ marked: () => false }), 'foreign');
        assert.equal(reason({ setAside: () => { throw new Error('EPERM'); } }), 'not_taken_away');
        const main = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'main.ts'), 'utf8');
        assert.ok(main.includes("log.warn('demo_start_over_not_carried_out', { Cause: startOverNotCarriedOut })"));
        assert.ok(main.includes("log.warn('demo_start_over_refused', { Cause: outcome.reason })"));
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('a copy is removed with its marker last: a removal that stops part-way leaves it marked, and a later one finishes it', () => {
    const d = disk();
    try {
        const copy = join(d.root, 'jason-demo.removing-1');
        claimDemoFolder(copy);
        mkdirSync(join(copy, 'a'));
        writeFileSync(join(copy, 'a', 'one.txt'), 'x');
        writeFileSync(join(copy, 'z.txt'), 'y');
        // The second entry cannot be removed (a file held open on Windows): the marker is still there.
        const taken: string[] = [];
        const holdZ = (path: string): void => {
            if (path.endsWith('z.txt')) throw new Error('EBUSY');
            taken.push(path);
            rmSync(path, { recursive: true, force: true });
        };
        assert.throws(() => removeMarkedLast(copy, holdZ), /EBUSY/);
        assert.ok(hasDemoMarker(copy), 'the marker is the last thing to go');
        assert.ok(!taken.some((path) => path.endsWith('.jason-demo-folder')));
        // Released: a later removal finishes it.
        removeMarkedLast(copy);
        assert.equal(existsSync(copy), false);
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('the request is a known entry of the person\'s desktop folder, so the chat host is refused it', () => {
    assert.equal(desktopPaths(own.data).demoStartOver, join(own.data, 'desktop', 'demo-start-over'));
    assert.ok(knownDesktopEntries.includes('demo-start-over'));
});

test('a marker that is there and cannot be read means the demo needs starting over: it is never seeded again', async () => {
    const run = await seeded();
    for (const text of ['', '{', '{"version":"1"}']) {
        const damaged = { ...run.deps, readText: (file: string) => (file === run.deps.markerFile ? text : run.deps.readText(file)) };
        assert.equal(readMarker(damaged)?.complete, false);
        const before = run.calls.length;
        const outcome = await readyTheSample(damaged, quiet);
        assert.equal(outcome.kind, 'failed');
        assert.equal(run.calls.length, before, 'no verb ran over a seeded installation');
        assert.equal((await ensureSeeded(damaged)).kind, 'needs-start-over');
    }
});

test('the seed writes through a file beside it renamed over it, and a failed step is logged by step and code, never by text', async () => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-seedwrite-'));
    try {
        const deps = seedDepsOnDisk({ cli: { async run() { return { exit: 0, stdout: '{}', stderr: '' }; } }, places, scratch: join(folder, 's'), progress: () => undefined });
        const file = join(folder, 'config', 'settings.json');
        deps.writeText(file, 'one');
        deps.writeText(file, 'two');
        assert.equal(readFileSync(file, 'utf8'), 'two');
        assert.deepEqual(readdirSync(join(folder, 'config')), ['settings.json']);
    } finally {
        rmSync(folder, { recursive: true, force: true });
    }
    const run = await seeded({ failAt: 'campaign create' });
    const ready = await readyTheSample({ ...run.deps, readText: (file) => (file === run.deps.markerFile ? null : run.deps.readText(file)) }, quiet);
    assert.ok(ready.kind === 'failed' && ready.step === 'campaign create' && ready.code === 'refused', JSON.stringify(ready));
});

test('a start of a demo with no marker begins the making, seeds, says it made, and cancels the open reviews; the next start only cancels', async () => {
    const run = await seeded();
    // The marker is read as absent: a fresh demo.
    const fresh = { ...run.deps, readText: (file: string) => (file === run.deps.markerFile ? null : run.deps.readText(file)) };
    const said: string[] = [];
    const watching: Preparing = { begin: () => void said.push(`begin ${run.calls.length}`), made: () => void said.push('made') };
    const before = run.calls.length;
    const first = await readyTheSample(fresh, watching);
    assert.deepEqual(first, { kind: 'ready', seeded: true });
    assert.deepEqual(said, [`begin ${before}`, 'made'], 'begun once, before any verb, and made once');
    assert.ok(run.calls.length > before);
    // The seeded demo started again: no making, no seed, the reviews cancelled.
    const again: string[] = [];
    const spy = { ...run.deps, cli: { async run(args: readonly string[]) { again.push(args.join(' ')); return run.deps.cli.run(args); } } as Cli };
    assert.deepEqual(await readyTheSample(spy, watching), { kind: 'ready', seeded: false });
    assert.deepEqual(said, [`begin ${before}`, 'made']);
    assert.ok(again.every((line) => line.startsWith('workitem ')), again.join(' | '));
    assert.ok(again.some((line) => line.startsWith('workitem list --kind ai_role')));
});

test('a demo whose seed was not finished is not built on: the start says so and points to the menu', async () => {
    const run = await seeded();
    const unfinished = { ...run.deps, readText: (file: string) => (file === run.deps.markerFile ? '{"version":1,"complete":false}' : run.deps.readText(file)) };
    const outcome = await readyTheSample(unfinished, quiet);
    assert.equal(outcome.kind, 'failed');
    assert.match(outcome.kind === 'failed' ? outcome.why : '', /Start the Demo Over/);
});

test('the seed says each of its phases once, in order, as a step and never a text, and that it made the sample', async () => {
    const run = await seeded();
    assert.deepEqual(run.progress, ['campaigns', 'approvals', 'sends', 'story', 'made']);
    const cut = await seeded({ failAt: 'approval approve' });
    assert.deepEqual(cut.progress, ['campaigns', 'approvals'], 'a seed cut short never says it made');
});

test('a cheap cut: the approve step takes the approval its wait saw, and asks for the list no second time', async () => {
    const run = await seeded();
    const waits = buildSeed(fixture()).filter((step) => step.kind === 'wait').length;
    assert.equal(run.calls.filter((call) => call.startsWith('approval list')).length, waits, 'one list per wait, none per approve');
    // Each approve names the approval the wait for its own item listed.
    for (const line of run.approved) assert.match(line, /^approval approve apr_\d+ /);
    assert.equal(run.approved.length, 4);
});

test('a cheap cut: a wait asks again every quarter of a second', async () => {
    assert.equal(seedWaitStepMs, 250);
    const late = await seeded({ lateBy: 2 });
    assert.equal(late.outcome.kind, 'seeded');
    assert.ok(late.slept.length > 0);
    assert.ok(late.slept.every((ms) => ms === 250), late.slept.join(','));
});

test('a seed longer than the ticket\u2019s life still ends on a paired page: the page loads first, the sample is made behind it', async () => {
    // A stand-in for the runtime's one-time link: minted by the start, before the sample, and good for one minute.
    const life = 60_000;
    let clock = 0;
    const minted = clock;
    const load = async (): Promise<string> => (clock - minted <= life ? 'paired' : 'unpaired');
    // Every verb of the seed takes two seconds, so the whole seed outlives the link.
    const fresh = () => fake({ tick: () => void (clock += 2_000), markerAbsent: true });
    const marks: boolean[] = [];
    const run = fresh();
    const opened = await openOnTheSample({ sample: run.deps, load, preparing: (on) => void marks.push(on), request: () => undefined, drop: () => undefined });
    assert.equal(opened.loaded, 'paired');
    assert.deepEqual(marks, [true], 'the mark is up as the page loads');
    const made = await opened.making;
    assert.deepEqual(made, { kind: 'ready', seeded: true });
    assert.ok(clock - minted > life, `the seed outlived the link (${clock} ms)`);
    assert.deepEqual(marks, [true, false], 'and gone once the sample is ready');
    // The order before: the sample made first, then the page, on the same link, which then pairs no more.
    clock = 0;
    const before = fresh();
    await readyTheSample(before.deps, quiet);
    assert.equal(await load(), 'unpaired');
});

test('the mark shows only while a sample is made: a seeded demo and the person\u2019s own installation load with none', async () => {
    const seededDemo = await seeded();
    const marks: boolean[] = [];
    const steps: string[] = [];
    const deps = (sample: SeedDeps | null) => ({
        sample,
        load: async () => void steps.push('load'),
        preparing: (on: boolean) => void marks.push(on),
        request: () => void steps.push('request'),
        drop: () => void steps.push('drop'),
    });
    const again = await openOnTheSample(deps(seededDemo.deps));
    assert.equal(again.making, null);
    const live = await openOnTheSample(deps(null));
    assert.equal(live.making, null);
    assert.deepEqual(marks, []);
    assert.deepEqual(steps, ['load', 'load']);
    // A seeded demo whose reviews cannot be cancelled fails before the page, as it did.
    const refusing = { ...seededDemo.deps, cli: { async run() { return { exit: 1, stdout: '{"error":{"code":"x","message":"no","retryable":false}}', stderr: '' }; } } as Cli };
    await assert.rejects(openOnTheSample(deps(refusing)), (error: unknown) => error instanceof SampleNotReady && error.outcome.kind === 'failed');
});

test('a making leaves the request to start over first and takes it away only once the sample is whole; one that fails keeps it, and the mark goes', async () => {
    const steps: string[] = [];
    const marks: boolean[] = [];
    const deps = (sample: SeedDeps) => ({
        sample,
        load: async () => void steps.push('load'),
        preparing: (on: boolean) => void marks.push(on),
        request: () => void steps.push('request'),
        drop: () => void steps.push('drop'),
    });
    const whole = fake({ markerAbsent: true });
    await (await openOnTheSample(deps(whole.deps))).making;
    assert.deepEqual(steps, ['load', 'request', 'drop']);
    steps.length = 0;
    const cut = fake({ markerAbsent: true, failAt: 'approval approve' });
    const made = await (await openOnTheSample(deps(cut.deps))).making;
    assert.equal(made?.kind, 'failed');
    assert.deepEqual(steps, ['load', 'request'], 'the request stays for the next start');
    assert.deepEqual(marks, [true, false, true, false]);
});

/**
 * What a start does with the request before it chooses (`chooseInstallation`): the carry-out, and on its refusal the
 * record set to Live with the request kept. The record is the person's `desktop/mode`.
 */
function startWith(d: ReturnType<typeof disk>, over: Partial<Parameters<typeof carryOutStartOver>[0]>): string | null {
    const record = join(d.person, 'desktop', 'mode');
    try {
        carry(d, over);
        return null;
    } catch (error) {
        writeFileSync(record, 'live');
        return error instanceof CarryOutRefused ? error.reason : 'failed';
    }
}

test('a second start while the seed runs leaves the record demo and the request in place', () => {
    const d = disk();
    try {
        claimBoth(d);
        mkdirSync(join(d.person, 'desktop'), { recursive: true });
        const record = join(d.person, 'desktop', 'mode');
        writeFileSync(record, 'demo');
        // The running desktop's making left its request, naming that run.
        const running = 4242;
        requestStartOver(d.request, running, true);
        assert.ok(leftByAMaking(readFileSync(d.request, 'utf8')));
        let waited = 0;
        // The run is alive and so is its demo runtime: a double-click, or a start from the tray, while it makes the sample.
        const second = startWith(d, { running: (pid) => pid === running, runtimeAlive: () => true, wait: () => void (waited += 1) });
        assert.equal(second, null, 'nothing refused');
        assert.equal(readFileSync(record, 'utf8'), 'demo');
        assert.ok(existsSync(d.request) && existsSync(d.demo.data) && existsSync(d.demo.harnessRoot), 'nothing carried out');
        assert.equal(waited, 0, 'and the second start is not held');
        // The same in a run whose making failed and that is still open, however old its request.
        const later = startWith(d, { running: (pid) => pid === running, runtimeAlive: () => true, requestAgeMs: () => requestYoungMs * 10 });
        assert.equal(later, null);
        assert.equal(readFileSync(record, 'utf8'), 'demo');
        // Once that run has gone, a real next start carries the request out and makes the sample whole.
        assert.equal(startWith(d, { running: () => false, runtimeAlive: () => false }), null);
        assert.ok(!existsSync(d.request) && !existsSync(d.demo.data));
        assert.equal(readFileSync(record, 'utf8'), 'demo');
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('the case the guide states is unchanged: an old run or a demo runtime still alive at a real next start goes Live and keeps the request', () => {
    const d = disk();
    try {
        claimBoth(d);
        mkdirSync(join(d.person, 'desktop'), { recursive: true });
        const record = join(d.person, 'desktop', 'mode');
        // Start the Demo Over's request, whose run is still ending.
        writeFileSync(record, 'demo');
        requestStartOver(d.request, 4242);
        assert.equal(leftByAMaking(readFileSync(d.request, 'utf8')), false);
        assert.equal(startWith(d, { running: (pid) => pid === 4242 }), 'asking_alive');
        assert.equal(readFileSync(record, 'utf8'), 'live');
        assert.ok(existsSync(d.request));
        // A demo runtime still alive refuses it too.
        writeFileSync(record, 'demo');
        assert.equal(startWith(d, { running: () => false, runtimeAlive: () => true }), 'runtime_alive');
        assert.equal(readFileSync(record, 'utf8'), 'live');
        // A making's request whose run has gone, with a demo runtime a crash left running, is refused as before.
        writeFileSync(record, 'demo');
        requestStartOver(d.request, 4242, true);
        assert.equal(startWith(d, { running: () => false, runtimeAlive: () => true }), 'runtime_alive');
        assert.equal(readFileSync(record, 'utf8'), 'live');
        assert.ok(existsSync(d.request));
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

test('the main module sets the record to Live only where the carry-out refused', () => {
    const main = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'main.ts'), 'utf8');
    const choose = main.slice(main.indexOf('function chooseInstallation('), main.indexOf('function startDesktop('));
    const write = "writeMode(desktopPaths(personDirectory).mode, 'live');";
    assert.equal(choose.split(write).length - 1, 1);
    const caught = choose.indexOf('} catch (error) {', choose.indexOf('carryOutStartOver({'));
    assert.ok(caught > 0 && choose.indexOf(write) > caught);
});

test('on a real disk: a desktop that quits in the middle of the seed leaves the request, and the next start makes the sample whole, never shows the half', async () => {
    const d = disk();
    try {
        claimBoth(d);
        // The first start: the making begins, leaves the request naming its run, and the run quits half-way.
        const half = fake({ markerAbsent: true, failAt: 'approval approve' });
        const onDisk = seedDepsOnDisk({ cli: half.deps.cli, places: d.demo, scratch: join(d.demo.data, 'seed-scratch'), progress: () => undefined });
        const firstRun = { ...onDisk, readStory: half.deps.readStory, sleep: async () => undefined, waitLimitMs: 0 };
        const quitting = await (await openOnTheSample({ sample: firstRun, load: async () => undefined, preparing: () => undefined, request: () => requestStartOver(d.request, 999_999_999, true), drop: () => assert.fail('never whole') })).making;
        assert.ok(quitting?.kind === 'failed' && quitting.step === 'approval approve', JSON.stringify(quitting));
        assert.ok(existsSync(d.request), 'the request outlives the run');
        assert.deepEqual(readMarker(onDisk), { version: seedVersion, complete: false }, 'the marker says the sample is not whole');
        // Without the request, the marker alone keeps it from being shown as ready: the start says so and points to the menu.
        const shown = await readyTheSample(onDisk, quiet);
        assert.ok(shown.kind === 'failed' && /not finished/.test(shown.why));
        // The next start carries the request out before anything names the demo, and makes a whole sample.
        carry(d);
        assert.ok(!existsSync(d.demo.data) && !existsSync(d.request));
        claimBoth(d);
        const next = fake({ markerAbsent: true });
        const nextOnDisk = { ...seedDepsOnDisk({ cli: next.deps.cli, places: d.demo, scratch: join(d.demo.data, 'seed-scratch'), progress: () => undefined }), readStory: next.deps.readStory, sleep: async () => undefined, waitLimitMs: 0 };
        const dropped: string[] = [];
        const made = await (await openOnTheSample({ sample: nextOnDisk, load: async () => undefined, preparing: () => undefined, request: () => requestStartOver(d.request), drop: () => void dropped.push('drop') })).making;
        assert.deepEqual(made, { kind: 'ready', seeded: true });
        assert.deepEqual(dropped, ['drop']);
        assert.deepEqual(readMarker(nextOnDisk), { version: seedVersion, complete: true });
    } finally {
        rmSync(d.root, { recursive: true, force: true });
    }
});

// ---------------------------------------------------------------------------------------------------------------------

/** What a start does around a making, when a row does not watch it. */
const quiet: Preparing = { begin: () => undefined, made: () => undefined };

interface Fake {
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
    /** Every sleep a wait took, in milliseconds. */
    slept: number[];
    settings(): string;
}

interface Run extends Fake {
    outcome: Awaited<ReturnType<typeof ensureSeeded>>;
}

/** Runs the seed against a fake runtime that answers the way the real verbs do, with an in-memory disk. */
async function seeded(options: FakeOptions = {}): Promise<Run> {
    const made = fake(options);
    return { ...made, outcome: await ensureSeeded(made.deps) };
}

interface FakeOptions {
    failAt?: string;
    neverSucceed?: boolean;
    text?: string;
    /** How many times an approval list for an item answers nothing before the item is parked. */
    lateBy?: number;
    /** Called at every verb, as a clock a row keeps. */
    tick?: () => void;
    /** The marker is read as absent at first, as in a demo never seeded. */
    markerAbsent?: boolean;
}

/** A fake runtime that answers the way the real verbs do, with an in-memory disk, and the seed's deps over it. */
function fake(options: FakeOptions = {}): Fake {
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
    const slept: number[] = [];
    const asked = new Map<string, number>();
    const parked: { id: string; item: string; campaign: string }[] = [];
    const decided = new Set<string>();
    const started = new Set<string>();
    let contacts = 0;

    const ok = (value: unknown): CliResult => ({ exit: 0, stdout: `${JSON.stringify(value)}\n`, stderr: '' });
    const bodyOf = (args: readonly string[]): unknown => {
        const at = args.indexOf('--file');
        // A body file is in the fake's own disk, or on the real one when a row seeds through `seedDepsOnDisk`.
        const file = at >= 0 ? String(args[at + 1]) : '';
        const text = at >= 0 ? (files.get(file) ?? (existsSync(file) ? readFileSync(file, 'utf8') : undefined)) : undefined;
        return text === undefined ? undefined : JSON.parse(text);
    };
    const value = (args: readonly string[], option: string): string => String(args[args.indexOf(option) + 1]);

    const cli: Cli = {
        async run(args) {
            const line = args.join(' ');
            calls.push(line);
            options.tick?.();
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
                case 'approval list': {
                    const item = value(args, '--work-item');
                    const times = (asked.get(item) ?? 0) + 1;
                    asked.set(item, times);
                    if (times <= (options.lateBy ?? 0)) return ok({ items: [] });
                    return ok({ items: parked.filter((p) => p.item === item && !decided.has(p.item)) });
                }
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
        sleep: async (ms) => void slept.push(ms),
        now: () => new Date('2026-11-13T12:00:00.000Z'),
        scratch: join(places.data, 'seed-scratch'),
        ...(options.lateBy === undefined ? { waitLimitMs: 0 } : {}),
    };
    if (options.markerAbsent) files.delete(deps.markerFile);
    return { deps, calls, events, writes, imported, workItems, approved, cancelled, progress, slept, settings: () => files.get(join(places.data, 'config', 'settings.json')) ?? '' };
}

test('the main process refuses Start the Demo Over until this run\'s own start has answered, and holds the demo\'s preparation under a live runtime', () => {
    const main = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'main.ts'), 'utf8');
    const refuse = main.indexOf('if (startInFlight) return');
    assert.ok(refuse > 0 && refuse < main.indexOf('await startDemoOver('), 'refused before anything stops');
    assert.ok(main.includes('let startInFlight = true;') && main.includes('} finally {\n        startInFlight = false;'));
    assert.ok(main.includes('runtimeAlive: (data) => demoRuntimeAlive(desktopPaths(data).runFile),'));
});

test('the main process loads the page through openOnTheSample, says the mark to the page and the menu, and refuses a switch or a start over while it shows', () => {
    const main = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'main.ts'), 'utf8');
    // The page is loaded by the one function that makes the sample behind it; nothing else seeds.
    assert.ok(main.includes('await openOnTheSample({'));
    assert.equal(main.includes('readyTheSample('), false);
    assert.equal(main.includes('preparingPage'), false);
    const opened = main.slice(main.indexOf('await openOnTheSample({'));
    assert.ok(opened.indexOf('loadWindow(') > 0 && opened.indexOf('loadWindow(') < opened.indexOf('});'), 'the page is loaded inside it');
    // The mark: the page reads it with the mode and hears it change; the menu draws its lines again.
    assert.ok(main.includes("({ mode: runOn.mode, unopened: runOn.unopened, preparing: sampleMaking })"));
    assert.ok(main.includes("toPage('mode.preparing', on);"));
    assert.ok(main.includes('desk.setDemo(demoMenu(runOn.mode, removalOffered(process.platform), on));'));
    // A switch, the way back and a start over are refused while the sample is made.
    assert.ok(main.includes("handle(seam, 'mode.set', origin, (choice) => applyMode(choice, modeDeps, sampleMaking), refused);"));
    assert.ok(main.includes("const refusal = applyMode('live', modeDeps, sampleMaking);"));
    const startOver = main.slice(main.indexOf('const startOver = async ()'));
    const held = startOver.indexOf('if (sampleMaking) return { refused: demoPreparing };');
    assert.ok(held > 0 && held < startOver.indexOf('await dialog.showMessageBox'));
    // The request a making leaves is the person's start-over request, taken away only once the sample is whole.
    assert.ok(main.includes('request: () => requestStartOver(desktopPaths(runOn.person).demoStartOver, process.pid, true),'));
    assert.ok(main.includes('drop: () => dropStartOver(desktopPaths(runOn.person).demoStartOver, log),'));
});
