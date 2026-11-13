/**
 * Filling the demo installation with its story. The seed is a typed list of the harness's own command-line verbs, run
 * through the application's bundled `jason` on the demo's data directory: every step is a verb a person could type, so
 * the demo is the product doing its real work on invented people, and no verb, operation or column exists for it.
 *
 * The story itself, the seller, the campaigns, the people and their messages, is read from the demo provider package's
 * data module once the package is placed (`demo-story.ts`); this file holds none of it.
 *
 * The order is the story's: the installation's settings (its own harness root, the manager and the responder off, routes
 * naming the demo provider alone), the package placed, the runtime started, the project, the campaigns, the people, the
 * work, and the approvals the story says were already given, in the name of its operator. The provider is told when the
 * story starts only after every send already approved has succeeded: an event is attributed once, when it is stored, so
 * a reply may follow a send only once the send is done.
 *
 * A marker carrying the seed's version is written last, so a seeded directory with the same version is not seeded again.
 * "Start the demo over" removes the demo's two folders and seeds again.
 */

import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import type { Cli, CliResult } from './cli.ts';
import { parseStory, reviewSeconds, type Story, type StoryPerson, type StorySend } from './demo-story.ts';

/** Raised when the story changes in a way a seeded directory must not be kept through. */
export const seedVersion = 1;

/** The only provider the demo installation knows. */
export const demoPluginId = 'demo';

/** A verb argument: text, or the value an earlier step captured. */
export type Arg = string | { ref: string };

export interface BodyFile {
    /** The option the file is passed with, such as `--file`. */
    option: string;
    value: unknown;
}

/** What a step keeps of the verb's answer for the steps after it. */
export type Capture =
    /** The answer's field, kept as `as`. */
    | { kind: 'field'; field: string; as: string }
    /** An import's items in order, the contact each stands for kept as `contact:<key>` for the people, in the order given. */
    | { kind: 'contacts'; keys: readonly string[] };

export interface VerbStep {
    kind: 'verb';
    /** The arguments after `jason`, exactly as a person would type them. */
    args: readonly Arg[];
    body?: BodyFile;
    capture?: Capture;
}

/** Waits, by asking again, until the verb's list has at least `atLeast` items, the way a person watches `approval list`. */
export interface WaitStep {
    kind: 'wait';
    args: readonly Arg[];
    atLeast: number;
    /** The answer's field holding the list. */
    list: string;
}

/** Waits, by asking again, until a field of the verb's answer holds a value, the way a person watches `workitem get`. */
export interface WaitFieldStep {
    kind: 'wait-field';
    args: readonly Arg[];
    field: string;
    equals: string;
}

/** Approves the pending approval of one work item, by `approval approve`, in the story's operator's name. */
export interface ApproveStep {
    kind: 'approve';
    workItem: Arg;
    reason: string;
}

/**
 * Cancels the campaign's open AI-role work items, with `workitem cancel`. Starting a campaign can queue a first review of
 * it; no AI is connected in the demo's runtime, so a review left waiting would end as stopped work on the first screen.
 */
export interface CancelReviewsStep {
    kind: 'cancel-reviews';
    campaign: Arg;
}

/** Writes the demo installation's settings file. */
export interface SettingsStep {
    kind: 'settings';
}

/** Puts the demo provider's package into the demo installation's plugins folder. */
export interface PlaceStep {
    kind: 'place-plugin';
}

/** Tells the provider the moment the story starts, in its configuration: `Plugins:Config:demo:story_start`. */
export interface StoryStartStep {
    kind: 'story-start';
}

export type SeedStep = VerbStep | WaitStep | WaitFieldStep | ApproveStep | CancelReviewsStep | SettingsStep | PlaceStep | StoryStartStep;

export interface DemoPlaces {
    /** The demo installation's data directory. */
    data: string;
    /** The demo installation's harness root. */
    harnessRoot: string;
}

/** The place a person's own folder X has beside it for the demo: the same name with `-demo`, in the same parent. */
export const demoSibling = (own: string): string => `${own.replace(/[\\/]+$/, '')}-demo`;

/**
 * The demo's two folders, both siblings of the person's own and never inside either data directory. The harness root is
 * the person's, `~/Jason` unless their settings name another, with `-demo` after it.
 */
export function demoPlaces(ownData: string, ownHarnessRoot: string): DemoPlaces {
    return { data: demoSibling(ownData), harnessRoot: demoSibling(ownHarnessRoot) };
}

/** Whether `inner` is `outer` or below it. */
export function isWithin(outer: string, inner: string): boolean {
    const path = relative(resolve(outer), resolve(inner));
    return path === '' || (!path.startsWith('..') && !isAbsolute(path) && path.split(sep)[0] !== '..');
}

/** Why a pair of places is not allowed, or null: the demo's harness root is a folder of its own beside the data directories. */
export function placesProblem(places: DemoPlaces, ownData: string, ownHarnessRoot: string): string | null {
    if (isWithin(places.data, places.harnessRoot) || isWithin(places.harnessRoot, places.data)) {
        return 'The demo harness root and the demo data directory must not hold one another.';
    }
    for (const data of [places.data, ownData]) {
        if (isWithin(data, places.harnessRoot)) return 'The demo harness root must not be inside a data directory.';
    }
    if (isWithin(ownHarnessRoot, places.harnessRoot) || isWithin(places.harnessRoot, ownHarnessRoot)) {
        return "The demo harness root must be a sibling of the person's, not inside it or holding it.";
    }
    return null;
}

/**
 * The settings the demo installation is started with, as the settings file holds them. The manager and the responder
 * are off: the responder is switched off (`Responder:OnReply`), and the manager, which has no switch of its own, is left
 * only the trigger it cannot do without and its slowest cadence, so no AI role is woken by the story. Routes name the
 * demo provider alone, and its events are read at the shortest cadence so replies arrive in the first minutes.
 */
export function demoSettings(places: DemoPlaces): Record<string, unknown> {
    return {
        Harness: { Root: places.harnessRoot },
        Responder: { OnReply: false },
        Manager: { Triggers: ['decision_answered'], ReviewSeconds: reviewSeconds },
        // A claim is one item per campaign per scan, so a short tick lets a story of a few dozen sends park in seconds, not minutes.
        Dispatcher: { EventsCadenceSeconds: 60, TickSeconds: 2 },
        Routes: { Default: { Plugin: demoPluginId } },
    };
}

/** The operator as the verbs name a person: `human:<name>`, whether the story writes the prefix or only the name. */
export const operatorActor = (story: Story): string => (story.operator.actor.includes(':') ? story.operator.actor : `human:${story.operator.actor}`);

/**
 * The statuses a work item can be in and not yet finished: every status of the enum but the four of
 * `WorkItemTransitions.Terminal` (succeeded, failed, cancelled, expired). These are the ones a cancel can still change.
 */
export const openStatuses = ['created', 'awaiting_approval', 'scheduled', 'processing', 'waiting'] as const;

const verb = (args: readonly Arg[], extra: Partial<VerbStep> = {}): VerbStep => ({ kind: 'verb', args, ...extra });
const ref = (name: string): Arg => ({ ref: name });
const sendRef = (send: StorySend): Arg => ref(`wi:${send.key}`);

/** The seed: every step in order, built from the story the package holds. */
export function buildSeed(story: Story): SeedStep[] {
    const actor = ['--actor', operatorActor(story)] as const;
    const sender = story.senders.find((s) => s.channel === 'email') ?? story.senders[0];
    if (!sender) throw new Error('The story needs a sender.');
    const steps: SeedStep[] = [
        { kind: 'settings' },
        { kind: 'place-plugin' },
        verb(['runtime', 'start']),
        verb(['project', 'create', '--name', story.project.name, ...actor], { capture: { kind: 'field', field: 'id', as: 'project' } }),
    ];

    const sends: { person: StoryPerson; send: StorySend; campaign: string }[] = [];
    for (const campaign of story.campaigns) {
        const id = ref(`campaign:${campaign.key}`);
        const members = story.people.filter((person) => person.campaign === campaign.key);
        steps.push(
            verb(['campaign', 'create', '--project', ref('project'), ...actor], {
                body: { option: '--file', value: { name: campaign.name, context: { description: campaign.description } } },
                capture: { kind: 'field', field: 'id', as: `campaign:${campaign.key}` },
            }),
            verb(['campaign', 'update', id, '--review-seconds', String(reviewSeconds), ...actor]),
            verb(['campaign', 'add-contacts', id, ...actor], {
                body: {
                    option: '--file',
                    value: members.map((person) => ({
                        first_name: person.first_name,
                        last_name: person.last_name,
                        company: story.companies.find((company) => company.key === person.company)?.name ?? person.company,
                        title: person.title,
                        time_zone: person.time_zone,
                        channels: person.channels.map((channel, index) => ({ channel: channel.channel, value: channel.value, primary: index === 0 })),
                    })),
                },
                capture: { kind: 'contacts', keys: members.map((person) => person.key) },
            }),
        );
        // A draft campaign is people only.
        if (campaign.status === 'draft') continue;
        steps.push(verb(['campaign', 'start', id, ...actor]), { kind: 'cancel-reviews', campaign: id });
        for (const person of members) {
            for (const send of person.sends) {
                sends.push({ person, send, campaign: campaign.key });
                steps.push(
                    verb(['workitem', 'create', id, '--kind', 'provider_op', '--operation', send.operation, '--contact', ref(`contact:${person.key}`), ...actor], {
                        body: {
                            option: '--file',
                            value: {
                                context: {
                                    input: {
                                        channel: send.channel,
                                        sender: { external_id: sender.external_id, address: sender.address },
                                        message: { compose: 'text', format: 'email', subject: send.subject, body: send.body },
                                    },
                                },
                            },
                        },
                        capture: { kind: 'field', field: 'id', as: `wi:${send.key}` },
                    }),
                );
            }
        }
    }

    const active = new Set(story.campaigns.filter((campaign) => campaign.status === 'active').map((campaign) => campaign.key));
    const given = sends.filter((entry) => entry.send.approval === 'given');
    // The sends the story says were approved: each parks, is approved in the operator's name, and then is done.
    for (const { send } of given) {
        steps.push({ kind: 'wait', args: ['approval', 'list', '--work-item', sendRef(send)], list: 'items', atLeast: 1 });
        steps.push({ kind: 'approve', workItem: sendRef(send), reason: 'reviewed and fine to send' });
    }
    for (const { send } of given) steps.push({ kind: 'wait-field', args: ['workitem', 'get', sendRef(send)], field: 'status', equals: 'succeeded' });
    // A paused campaign's given sends went first; only then is it paused.
    for (const campaign of story.campaigns.filter((c) => c.status === 'paused')) steps.push(verb(['campaign', 'pause', ref(`campaign:${campaign.key}`), ...actor]));
    // The sends still waiting for a person, in a campaign that runs, are parked by now for the first screen to show.
    for (const { send, campaign } of sends.filter((entry) => entry.send.approval === 'waiting')) {
        if (active.has(campaign)) steps.push({ kind: 'wait', args: ['approval', 'list', '--work-item', sendRef(send)], list: 'items', atLeast: 1 });
    }
    // Only now may a reply follow a send: the provider counts its replies' times from this moment.
    steps.push({ kind: 'story-start' }, verb(['plugin', 'reload']));
    return steps;
}

/** The verb nouns and verbs the seed uses, as `noun verb` pairs: what a guard holds against the command line's own map. */
export function verbsUsed(steps: readonly SeedStep[]): string[] {
    const used = new Set<string>();
    for (const step of steps) {
        if (step.kind === 'verb' || step.kind === 'wait' || step.kind === 'wait-field') used.add(`${step.args[0]} ${step.args[1]}`);
        if (step.kind === 'approve') {
            used.add('approval list');
            used.add('approval approve');
        }
        if (step.kind === 'cancel-reviews') {
            used.add('workitem list');
            used.add('workitem cancel');
        }
    }
    return [...used].sort();
}

// ---------------------------------------------------------------------------------------------------------------------

export interface SeedMarker {
    version: number;
    /** False while the seed runs and after one that was cut short; true only once every step has run. */
    complete: boolean;
}

export interface SeedDeps {
    cli: Cli;
    places: DemoPlaces;
    /** The file the marker is kept in, inside the demo's data directory, written last. */
    markerFile: string;
    readText(file: string): string | null;
    writeText(file: string, text: string): void;
    /** Makes the folder and the ones above it. */
    makeDirectory(path: string): void;
    removeTree(path: string): void;
    /** Puts the provider package into `<data>/plugins/demo`; throws when it cannot. */
    placePlugin(data: string): void;
    /** The text of the placed package's story module. */
    readStory(data: string): string;
    /** What the window says while this goes on. */
    progress(message: string): void;
    sleep(ms: number): Promise<void>;
    /** The moment now. */
    now(): Date;
    /** The scratch folder the body files are written to; removed after. */
    scratch: string;
    /** How long a wait may last, and how often it asks. */
    waitLimitMs?: number;
    waitStepMs?: number;
}

export type SeedOutcome =
    | { kind: 'already-seeded' }
    | { kind: 'seeded' }
    /** A directory a seed was cut short in, or one an older story seeded: only "Start the demo over" makes it right. */
    | { kind: 'needs-start-over' }
    | { kind: 'failed'; step: string; detail: string };

export function readMarker(deps: Pick<SeedDeps, 'readText' | 'markerFile'>): SeedMarker | null {
    const text = deps.readText(deps.markerFile);
    if (text === null) return null;
    try {
        const value: unknown = JSON.parse(text);
        const version = typeof value === 'object' && value !== null ? (value as { version?: unknown }).version : undefined;
        const complete = typeof value === 'object' && value !== null ? (value as { complete?: unknown }).complete : undefined;
        return typeof version === 'number' ? { version, complete: complete === true } : null;
    } catch {
        return null;
    }
}

function parse(result: CliResult): Record<string, unknown> | null {
    try {
        const value: unknown = JSON.parse(result.stdout.trim());
        return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null;
    } catch {
        return null;
    }
}

function describe(result: CliResult): string {
    const error = parse(result)?.['error'];
    const message = typeof error === 'object' && error !== null ? (error as { message?: unknown }).message : null;
    return typeof message === 'string' ? message : `exit ${result.exit}`;
}

/** Seeds the demo installation unless its marker already carries this version. */
export async function ensureSeeded(deps: SeedDeps, version = seedVersion): Promise<SeedOutcome> {
    const marker = readMarker(deps);
    if (marker?.complete && marker.version === version) return { kind: 'already-seeded' };
    // Something is there that this seed did not finish or did not make: it is never built on, and never removed here.
    if (marker) return { kind: 'needs-start-over' };
    deps.progress('Preparing the sample');
    deps.makeDirectory(deps.places.data);
    deps.makeDirectory(deps.scratch);
    try {
        deps.writeText(deps.markerFile, `${JSON.stringify({ version, complete: false })}\n`);
        // The settings and the package first: the story is read from the package, so it is the one set.
        writeSettingsFile(deps);
        deps.placePlugin(deps.places.data);
        const story = parseStory(deps.readStory(deps.places.data));
        const context: Context = { refs: new Map(), story };
        let count = 0;
        for (const step of buildSeed(story)) {
            count += 1;
            const failed = await runStep(deps, step, context, count);
            if (failed) return { kind: 'failed', step: failed.step, detail: failed.detail };
        }
        deps.writeText(deps.markerFile, `${JSON.stringify({ version, complete: true })}\n`);
        return { kind: 'seeded' };
    } catch (error) {
        return { kind: 'failed', step: 'story', detail: error instanceof Error ? error.message : String(error) };
    } finally {
        deps.removeTree(deps.scratch);
    }
}

const settingsFile = (deps: SeedDeps): string => join(deps.places.data, 'config', 'settings.json');

function writeSettingsFile(deps: SeedDeps): void {
    deps.makeDirectory(dirname(settingsFile(deps)));
    deps.writeText(settingsFile(deps), `${JSON.stringify(demoSettings(deps.places), null, 2)}\n`);
}

interface Context {
    refs: Map<string, string>;
    story: Story;
}

function resolveArg(arg: Arg, refs: ReadonlyMap<string, string>): string {
    if (typeof arg === 'string') return arg;
    const value = refs.get(arg.ref);
    if (value === undefined) throw new Error(`The seed uses ${arg.ref} before an earlier step kept it.`);
    return value;
}

type Failed = { step: string; detail: string } | null;

/**
 * Cancels the open AI-role work items, those of one campaign or of the whole installation, with `workitem list` and
 * `workitem cancel` in the operator's name; null when all are cancelled, else the step that was refused. The desktop calls
 * it at every start of the demo, not only at the seed: no AI role runs in the demo's runtime, so a review that falls due
 * (the first one a week after a campaign starts) would wait for one and end as stopped work on the first screen.
 */
export async function cancelOpenReviews(cli: Cli, actor: string, campaign?: string): Promise<Failed> {
    const listed = await cli.run(['workitem', 'list', ...(campaign === undefined ? [] : ['--campaign', campaign]), '--kind', 'ai_role', ...openStatuses.flatMap((status) => ['--status', status])]);
    if (listed.exit !== 0) return { step: 'workitem list', detail: describe(listed) };
    const items = parse(listed)?.['items'];
    for (const item of Array.isArray(items) ? items : []) {
        const id = (item as { id?: unknown }).id;
        if (typeof id !== 'string') continue;
        const cancelled = await cli.run(['workitem', 'cancel', id, '--reason', 'no AI is connected in the demo', '--actor', actor]);
        if (cancelled.exit !== 0) return { step: 'workitem cancel', detail: describe(cancelled) };
    }
    return null;
}


async function runStep(deps: SeedDeps, step: SeedStep, context: Context, number: number): Promise<Failed> {
    const refs = context.refs;
    const actor = operatorActor(context.story);
    const label = (args: readonly Arg[]) => `${args[0]} ${args[1]}`;
    const limit = deps.waitLimitMs ?? 120_000;
    const every = deps.waitStepMs ?? 1_000;
    try {
        switch (step.kind) {
            case 'settings':
            case 'place-plugin':
                return null;
            case 'verb': {
                const args = step.args.map((arg) => resolveArg(arg, refs));
                if (step.body) {
                    const file = join(deps.scratch, `body-${number}.json`);
                    deps.writeText(file, JSON.stringify(step.body.value));
                    args.push(step.body.option, file);
                }
                const result = await deps.cli.run(args);
                if (result.exit !== 0) return { step: label(step.args), detail: describe(result) };
                capture(step.capture, parse(result), refs);
                return null;
            }
            case 'wait':
            case 'wait-field': {
                const args = step.args.map((arg) => resolveArg(arg, refs));
                for (let waited = 0; ; waited += every) {
                    const result = await deps.cli.run(args);
                    if (result.exit !== 0) return { step: label(step.args), detail: describe(result) };
                    const answer = parse(result);
                    const list = step.kind === 'wait' ? answer?.[step.list] : undefined;
                    const reached = step.kind === 'wait' ? Array.isArray(list) && list.length >= step.atLeast : answer?.[step.field] === step.equals;
                    if (reached) return null;
                    if (waited >= limit) return { step: label(step.args), detail: `not reached after ${limit} ms` };
                    await deps.sleep(every);
                }
            }
            case 'approve': {
                const item = resolveArg(step.workItem, refs);
                const listed = await deps.cli.run(['approval', 'list', '--work-item', item]);
                if (listed.exit !== 0) return { step: 'approval list', detail: describe(listed) };
                const items = parse(listed)?.['items'];
                const id = (Array.isArray(items) ? (items[0] as { id?: unknown } | undefined) : undefined)?.id;
                if (typeof id !== 'string') return { step: 'approval list', detail: `nothing waits on ${item}` };
                const approved = await deps.cli.run(['approval', 'approve', id, '--reason', step.reason, '--actor', actor]);
                return approved.exit === 0 ? null : { step: 'approval approve', detail: describe(approved) };
            }
            case 'cancel-reviews': {
                const campaign = resolveArg(step.campaign, refs);
                const outcome = await cancelOpenReviews(deps.cli, actor, campaign);
                return outcome === null ? null : outcome;
            }
            case 'story-start': {
                const text = deps.readText(settingsFile(deps));
                const settings = (text === null ? {} : JSON.parse(text)) as { Plugins?: { Config?: Record<string, Record<string, string>> } };
                settings.Plugins = { ...settings.Plugins, Config: { ...settings.Plugins?.Config, [demoPluginId]: { story_start: deps.now().toISOString() } } };
                deps.writeText(settingsFile(deps), `${JSON.stringify(settings, null, 2)}\n`);
                return null;
            }
        }
    } catch (error) {
        return { step: step.kind, detail: error instanceof Error ? error.message : String(error) };
    }
}

function capture(how: Capture | undefined, answer: Record<string, unknown> | null, refs: Map<string, string>): void {
    if (!how) return;
    if (how.kind === 'field') {
        const value = answer?.[how.field];
        if (typeof value !== 'string') throw new Error(`The answer has no ${how.field} to keep as ${how.as}.`);
        refs.set(how.as, value);
        return;
    }
    const items = answer?.['items'];
    if (!Array.isArray(items) || items.length !== how.keys.length) throw new Error('The import answered for a different number of people than it was given.');
    how.keys.forEach((key, index) => {
        const contact = (items[index] as { contact_id?: unknown } | undefined)?.contact_id;
        if (typeof contact !== 'string') throw new Error(`The import gave no contact for ${key}.`);
        refs.set(`contact:${key}`, contact);
    });
}

// ---------------------------------------------------------------------------------------------------------------------

export interface StartOverDeps {
    seed: SeedDeps;
    /** Stops the demo installation's runtime; resolves false when it could not be stopped. */
    stopRuntime(): Promise<boolean>;
    /** Loads the window again, in the demo, once the sample is back. */
    relaunch(): void;
}

export type StartOverOutcome = { kind: 'started-over' } | { kind: 'refused'; why: string };

/**
 * "Start the demo over": the demo's runtime is stopped, its two folders are removed, the sample is made again and the
 * window loads again. Nothing of the person's own installation is touched: only the two folders `places` names are
 * removed, and only once the runtime that ran on them has stopped.
 */
export async function startDemoOver(deps: StartOverDeps): Promise<StartOverOutcome> {
    if (!(await deps.stopRuntime())) return { kind: 'refused', why: 'The sample could not be stopped, so it was left as it is.' };
    deps.seed.removeTree(deps.seed.places.data);
    deps.seed.removeTree(deps.seed.places.harnessRoot);
    const outcome = await ensureSeeded(deps.seed);
    if (outcome.kind !== 'seeded') return { kind: 'refused', why: outcome.kind === 'failed' ? `The sample could not be prepared (${outcome.step}).` : 'The sample could not be prepared.' };
    deps.relaunch();
    return { kind: 'started-over' };
}

/** The sentence the window shows while the sample is made. */
export const preparingSentence = 'Preparing the sample: inventing a few fleets and the people who work there. This takes a moment.';

/** A page for the window while the seed runs: a sentence and nothing else; no script, no outside file. */
export function preparingPage(): string {
    const sentence = preparingSentence.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Jason</title><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>html,body{height:100%;margin:0}body{display:flex;align-items:center;justify-content:center;font:16px system-ui,sans-serif;background:#f6f6f4;color:#222}@media (prefers-color-scheme: dark){body{background:#1b1b1a;color:#e8e8e6}}p{max-width:30em;text-align:center;line-height:1.5}</style></head><body><p role="status">${sentence}</p></body></html>`;
}

// ---------------------------------------------------------------------------------------------------------------------

/** The marker's file, in the demo installation's data directory. */
export const markerPath = (data: string): string => join(data, 'demo-seed.json');

/** The plugin folder the provider package is placed in, and the data module of it the story is read from. */
export const placedPackage = (data: string): string => join(data, 'plugins', demoPluginId);
export const storyModule = (data: string): string => join(placedPackage(data), 'modules', 'story.js');

/** The seed's dependencies on the machine's own files. `packageSource` is the provider package the application carries. */
export function seedDepsOnDisk(parts: { cli: Cli; places: DemoPlaces; packageSource: string; scratch: string; progress(message: string): void }): SeedDeps {
    return {
        cli: parts.cli,
        places: parts.places,
        markerFile: markerPath(parts.places.data),
        readText: (file) => (existsSync(file) ? readFileSync(file, 'utf8') : null),
        writeText: (file, text) => {
            mkdirSync(dirname(file), { recursive: true });
            writeFileSync(file, text, { encoding: 'utf8' });
        },
        makeDirectory: (path) => void mkdirSync(path, { recursive: true }),
        removeTree: (path) => rmSync(path, { recursive: true, force: true }),
        placePlugin: (data) => cpSync(parts.packageSource, placedPackage(data), { recursive: true }),
        readStory: (data) => readFileSync(storyModule(data), 'utf8'),
        progress: parts.progress,
        sleep: (ms) => new Promise((done) => setTimeout(done, ms)),
        now: () => new Date(),
        scratch: parts.scratch,
    };
}
