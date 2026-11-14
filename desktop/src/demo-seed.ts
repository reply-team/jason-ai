/**
 * Filling the demo installation with its story. The seed is a typed list of the harness's own command-line verbs, run
 * through the application's bundled `jason` on the demo's data directory: every step is a verb a person could type, so
 * the demo is the product doing its real work on invented people, and no verb, operation or column exists for it.
 *
 * The story itself, the seller, the campaigns, the people and their messages, is read from the demo provider package's
 * data module once the package is placed (`demo-story.ts`); this file holds none of it.
 *
 * The seed does only the run's work, on an installation that is already made: its directory, its fixed settings (a harness
 * root of its own, the manager and the responder off, `demo-settings.ts`), the provider package placed and the route to it
 * written, and its runtime running. The order is the story's: the project, the campaigns, the people, the work, and the
 * approvals the story says were already given, in the name of its operator. The provider is told when the
 * story starts only after every send already approved has succeeded: an event is attributed once, when it is stored, so
 * a reply may follow a send only once the send is done.
 *
 * A marker carrying the seed's version is written last, so a seeded directory with the same version is not seeded again.
 * "Start the demo over" removes the demo's two folders and seeds again.
 *
 * The page is loaded before the sample is made (`openOnTheSample`): the runtime's link that pairs the page is good for a
 * minute and the seed takes longer, and the page shows the data as it lands. While the sample is made the page and the
 * menu say so, and the person's request to start the demo over is left in place, so a desktop that quits half-way makes
 * the sample whole at its next start rather than show the half.
 */

import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import type { Cli, CliResult } from './cli.ts';
import { demoReviewSeconds } from './demo-settings.ts';
import { demoDirectory } from './mode.ts';
import { dataLayout } from './paths.ts';
import { parseStory, type Story, type StoryPerson, type StorySend } from './demo-story.ts';

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
    /** The first item's id, kept under this name for the steps after it. */
    keep?: string;
}

/** Waits, by asking again, until a field of the verb's answer holds a value, the way a person watches `workitem get`. */
export interface WaitFieldStep {
    kind: 'wait-field';
    args: readonly Arg[];
    field: string;
    equals: string;
}

/** Approves one pending approval, the one the wait before it listed, by `approval approve`, in the story's operator's name. */
export interface ApproveStep {
    kind: 'approve';
    approval: Arg;
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

/** Tells the provider the moment the story starts, in its configuration: `Plugins:Config:demo:story_start`. */
export interface StoryStartStep {
    kind: 'story-start';
}

export type SeedStep = VerbStep | WaitStep | WaitFieldStep | ApproveStep | CancelReviewsStep | StoryStartStep;

export interface DemoPlaces {
    /** The demo installation's data directory. */
    data: string;
    /** The demo installation's harness root. */
    harnessRoot: string;
}

/**
 * The demo's two folders, both siblings of the person's own (the one rule, `demoDirectory`) and never inside either data
 * directory. The harness root is the person's, `~/Jason` unless their settings name another.
 */
export function demoPlaces(ownData: string, ownHarnessRoot: string): DemoPlaces {
    return { data: demoDirectory(ownData), harnessRoot: demoDirectory(ownHarnessRoot) };
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
    if (!sender?.address) throw new Error('The story needs an email sender with an address.');
    const steps: SeedStep[] = [
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
            verb(['campaign', 'update', id, '--review-seconds', String(demoReviewSeconds), ...actor]),
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
    // The sends the story says were approved: each parks, is approved in the operator's name, and then is done. The
    // approval approved is the one the wait listed, so the list is asked for once.
    for (const { send } of given) {
        steps.push({ kind: 'wait', args: ['approval', 'list', '--work-item', sendRef(send)], list: 'items', atLeast: 1, keep: `apr:${send.key}` });
        steps.push({ kind: 'approve', approval: ref(`apr:${send.key}`), reason: 'reviewed and fine to send' });
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
        if (step.kind === 'approve') used.add('approval approve');
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
    /** The text of the placed package's story module. */
    readStory(data: string): string;
    /** Each phase of the seed as it begins (`seedPhase`), and `made` once the sample is whole: steps, never a text. */
    progress(step: SeedPhase | 'made'): void;
    sleep(ms: number): Promise<void>;
    /** The moment now. */
    now(): Date;
    /** The scratch folder the body files are written to; removed after. */
    scratch: string;
    /** How long a wait may last, and how often it asks (`seedWaitStepMs` unless given). */
    waitLimitMs?: number;
    waitStepMs?: number;
}

/** How often a wait asks again: a quarter of a second, so a step is seen soon after the runtime has done it. */
export const seedWaitStepMs = 250;

/** The seed's phases, in the order it goes through them. */
export type SeedPhase = 'campaigns' | 'approvals' | 'sends' | 'story';

/**
 * The phase a step belongs to, for the log line that says where the seed is: the approvals given (a wait that comes
 * before an approve, and the approve), the sends waited on until done, the story told after them, and before all that
 * the project and the campaigns with their people and their work.
 */
export function seedPhase(steps: readonly SeedStep[], index: number): SeedPhase {
    const step = steps[index]!;
    if (step.kind === 'approve' || (step.kind === 'wait' && steps[index + 1]?.kind === 'approve')) return 'approvals';
    if (step.kind === 'wait-field') return 'sends';
    if (step.kind === 'wait' || step.kind === 'story-start') return 'story';
    if (step.kind === 'verb' && ((step.args[0] === 'campaign' && step.args[1] === 'pause') || (step.args[0] === 'plugin' && step.args[1] === 'reload'))) return 'story';
    return 'campaigns';
}

export type SeedOutcome =
    | { kind: 'already-seeded' }
    | { kind: 'seeded' }
    /** A directory a seed was cut short in, or one an older story seeded: only "Start the demo over" makes it right. */
    | { kind: 'needs-start-over' }
    | { kind: 'failed'; step: string; detail: string; code?: string };

/** A marker file that is there and cannot be read: never taken for an absent one, so a seeded demo is not seeded again. */
const unreadableMarker: SeedMarker = { version: -1, complete: false };

export function readMarker(deps: Pick<SeedDeps, 'readText' | 'markerFile'>): SeedMarker | null {
    const text = deps.readText(deps.markerFile);
    if (text === null) return null;
    try {
        const value: unknown = JSON.parse(text);
        const version = typeof value === 'object' && value !== null ? (value as { version?: unknown }).version : undefined;
        const complete = typeof value === 'object' && value !== null ? (value as { complete?: unknown }).complete : undefined;
        return typeof version === 'number' ? { version, complete: complete === true } : unreadableMarker;
    } catch {
        return unreadableMarker;
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

/** The error's snake_case code (never its message, which can hold what a person wrote), or the exit code. */
function codeOf(result: CliResult): string {
    const error = parse(result)?.['error'];
    const code = typeof error === 'object' && error !== null ? (error as { code?: unknown }).code : null;
    return typeof code === 'string' && /^[a-z0-9_]{1,64}$/.test(code) ? code : `exit_${result.exit}`;
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
    deps.makeDirectory(deps.places.data);
    deps.makeDirectory(deps.scratch);
    try {
        deps.writeText(deps.markerFile, `${JSON.stringify({ version, complete: false })}\n`);
        // The story is read from the package the desktop placed, so it is the one set.
        const story = parseStory(deps.readStory(deps.places.data));
        const context: Context = { refs: new Map(), story };
        const steps = buildSeed(story);
        let phase: SeedPhase | null = null;
        for (const [index, step] of steps.entries()) {
            const now = seedPhase(steps, index);
            if (now !== phase) deps.progress((phase = now));
            const failed = await runStep(deps, step, context, index + 1);
            if (failed) return { kind: 'failed', step: failed.step, detail: failed.detail, ...(failed.code === undefined ? {} : { code: failed.code }) };
        }
        deps.writeText(deps.markerFile, `${JSON.stringify({ version, complete: true })}\n`);
        deps.progress('made');
        return { kind: 'seeded' };
    } catch (error) {
        return { kind: 'failed', step: 'story', detail: error instanceof Error ? error.message : String(error) };
    } finally {
        deps.removeTree(deps.scratch);
    }
}

/** Where the demo provider's configuration is kept, by the demo directory's layout: the settings file in layout 1. */
const secretsFile = (deps: SeedDeps): string => dataLayout(deps.places.data).secretsFile;

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

type Failed = { step: string; detail: string; code?: string } | null;

/**
 * Cancels the open AI-role work items, those of one campaign or of the whole installation, with `workitem list` and
 * `workitem cancel` in the operator's name; null when all are cancelled, else the step that was refused. The desktop calls
 * it at every start of the demo, not only at the seed: no AI role runs in the demo's runtime, so a review that falls due
 * (the first one a week after a campaign starts) would wait for one and end as stopped work on the first screen.
 */
export async function cancelOpenReviews(cli: Cli, actor: string, campaign?: string): Promise<Failed> {
    const listed = await cli.run(['workitem', 'list', ...(campaign === undefined ? [] : ['--campaign', campaign]), '--kind', 'ai_role', ...openStatuses.flatMap((status) => ['--status', status])]);
    if (listed.exit !== 0) return { step: 'workitem list', detail: describe(listed), code: codeOf(listed) };
    const items = parse(listed)?.['items'];
    for (const item of Array.isArray(items) ? items : []) {
        const id = (item as { id?: unknown }).id;
        if (typeof id !== 'string') continue;
        const cancelled = await cli.run(['workitem', 'cancel', id, '--reason', 'no AI is connected in the demo', '--actor', actor]);
        if (cancelled.exit !== 0) return { step: 'workitem cancel', detail: describe(cancelled), code: codeOf(cancelled) };
    }
    return null;
}


async function runStep(deps: SeedDeps, step: SeedStep, context: Context, number: number): Promise<Failed> {
    const refs = context.refs;
    const actor = operatorActor(context.story);
    const label = (args: readonly Arg[]) => `${args[0]} ${args[1]}`;
    const limit = deps.waitLimitMs ?? 120_000;
    const every = deps.waitStepMs ?? seedWaitStepMs;
    try {
        switch (step.kind) {
            case 'verb': {
                const args = step.args.map((arg) => resolveArg(arg, refs));
                if (step.body) {
                    const file = join(deps.scratch, `body-${number}.json`);
                    deps.writeText(file, JSON.stringify(step.body.value));
                    args.push(step.body.option, file);
                }
                const result = await deps.cli.run(args);
                if (result.exit !== 0) return { step: label(step.args), detail: describe(result), code: codeOf(result) };
                capture(step.capture, parse(result), refs);
                return null;
            }
            case 'wait':
            case 'wait-field': {
                const args = step.args.map((arg) => resolveArg(arg, refs));
                for (let waited = 0; ; waited += every) {
                    const result = await deps.cli.run(args);
                    if (result.exit !== 0) return { step: label(step.args), detail: describe(result), code: codeOf(result) };
                    const answer = parse(result);
                    const list = step.kind === 'wait' ? answer?.[step.list] : undefined;
                    const reached = step.kind === 'wait' ? Array.isArray(list) && list.length >= step.atLeast : answer?.[step.field] === step.equals;
                    if (reached) {
                        if (step.kind === 'wait' && step.keep !== undefined) {
                            const id = ((list as unknown[])[0] as { id?: unknown } | undefined)?.id;
                            if (typeof id !== 'string') return { step: label(step.args), detail: 'the first item listed has no id' };
                            refs.set(step.keep, id);
                        }
                        return null;
                    }
                    if (waited >= limit) return { step: label(step.args), detail: `not reached after ${limit} ms` };
                    await deps.sleep(every);
                }
            }
            case 'approve': {
                const id = resolveArg(step.approval, refs);
                const approved = await deps.cli.run(['approval', 'approve', id, '--reason', step.reason, '--actor', actor]);
                return approved.exit === 0 ? null : { step: 'approval approve', detail: describe(approved), code: codeOf(approved) };
            }
            case 'cancel-reviews': {
                const campaign = resolveArg(step.campaign, refs);
                const outcome = await cancelOpenReviews(deps.cli, actor, campaign);
                return outcome === null ? null : outcome;
            }
            case 'story-start': {
                const text = deps.readText(secretsFile(deps));
                const settings = (text === null ? {} : JSON.parse(text)) as { Plugins?: { Config?: Record<string, Record<string, string>> } };
                settings.Plugins = { ...settings.Plugins, Config: { ...settings.Plugins?.Config, [demoPluginId]: { story_start: deps.now().toISOString() } } };
                deps.writeText(secretsFile(deps), `${JSON.stringify(settings, null, 2)}\n`);
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
    /** The demo's two folders. */
    places: DemoPlaces;
    /** Whether a folder is there at all. */
    exists(folder: string): boolean;
    /** Whether a folder carries the demo's marker (`hasDemoMarker`): only such a folder is ever removed, at the next start. */
    marked(folder: string): boolean;
    /** Stops the demo installation's runtime; resolves false when it could not be stopped. */
    stopRuntime(): Promise<boolean>;
    /** Leaves the request that the next demo start remove the two folders; throws when it cannot. */
    request(): void;
    /** Starts the desktop again on the demo. */
    relaunch(): void;
}

export type StartOverOutcome = { kind: 'started-over' } | { kind: 'refused'; why: string; reason: 'foreign' | 'stop_failed' | 'request_failed' };

/**
 * "Start the demo over": the demo's runtime is stopped, a request is left in the person's own folder and the desktop starts
 * again. The folders are NOT removed here: this run lives inside them (its log, its profile, its chats), so the next start
 * removes them first, before anything of it names the demo's directory (`carryOutStartOver`), then makes and seeds a fresh
 * demo. Nothing of the person's own installation is touched. A folder of the demo's name without the marker is left and
 * named before anything stops.
 */
export async function startDemoOver(deps: StartOverDeps): Promise<StartOverOutcome> {
    const folders = [deps.places.data, deps.places.harnessRoot];
    const foreign = folders.find((folder) => deps.exists(folder) && !deps.marked(folder));
    if (foreign !== undefined) return { kind: 'refused', why: `The folder ${foreign} is not one the demo made, so nothing was removed. Move it away or remove it yourself, then start the demo over.`, reason: 'foreign' };
    if (!(await deps.stopRuntime())) return { kind: 'refused', why: 'The sample could not be stopped, so it was left as it is.', reason: 'stop_failed' };
    try {
        deps.request();
    } catch {
        return { kind: 'refused', why: 'The request to start the demo over could not be written, so the demo was left as it is.', reason: 'request_failed' };
    }
    deps.relaunch();
    return { kind: 'started-over' };
}

export type ReadyOutcome = { kind: 'ready'; seeded: boolean } | { kind: 'failed'; why: string; step?: string; code?: string };

/** What a start does around a making of the sample: `begin` before its first verb, `made` once the sample is whole. */
export interface Preparing {
    begin(): void | Promise<void>;
    made(): void;
}

/**
 * What every start of the demo does once its runtime is up: a demo with no marker is seeded (between `preparing.begin`
 * and `preparing.made`), one whose seed was not finished is left alone and said so, and in every case the open AI-role
 * work items are cancelled in the operator's name, because no AI role runs in the demo's runtime.
 */
export async function readyTheSample(deps: SeedDeps, preparing: Preparing, version = seedVersion): Promise<ReadyOutcome> {
    const marker = readMarker(deps);
    let seeded = false;
    if (marker === null) {
        await preparing.begin();
        const outcome = await ensureSeeded(deps, version);
        if (outcome.kind === 'failed') return { kind: 'failed', why: `The sample could not be prepared (${outcome.step}). Choose Start the Demo Over in the menu to try again.`, step: outcome.step, code: outcome.code ?? 'none' };
        if (outcome.kind !== 'seeded') return { kind: 'failed', why: 'The sample could not be prepared. Choose Start the Demo Over in the menu to try again.' };
        seeded = true;
    } else if (!marker.complete || marker.version !== version) {
        return { kind: 'failed', why: 'The sample was not finished. Choose Start the Demo Over in the menu to make it again.' };
    }
    let actor: string;
    try {
        actor = operatorActor(parseStory(deps.readStory(deps.places.data)));
    } catch {
        return { kind: 'failed', why: 'The sample could not be read. Choose Start the Demo Over in the menu to make it again.' };
    }
    const failed = await cancelOpenReviews(deps.cli, actor);
    if (failed) return { kind: 'failed', why: `The sample could not be made ready (${failed.step}).`, step: failed.step, code: failed.code ?? 'none' };
    if (seeded) preparing.made();
    return { kind: 'ready', seeded };
}

/** A demo whose sample is there and cannot be made ready: the start fails before the page, as it says. */
export class SampleNotReady extends Error {
    readonly outcome: ReadyOutcome & { kind: 'failed' };
    constructor(outcome: ReadyOutcome & { kind: 'failed' }) {
        super(outcome.why);
        this.outcome = outcome;
    }
}

/** What `openOnTheSample` is given: the demo's sample (null on the person's own installation) and the start's acts. */
export interface OpeningDeps<T> {
    sample: SeedDeps | null;
    /** Loads the page. */
    load(): Promise<T>;
    /** The mark, said to the page and the menu: on while the sample is made, off once it is done either way. */
    preparing(on: boolean): void;
    /** Leaves the person's request to start the demo over, so a making cut short is made whole at the next start. */
    request(): void;
    /** Takes the request away once the sample is whole. */
    drop(): void;
}

/**
 * Loads the page on the sample. A sample already made is made ready first (its reviews cancelled), and one that cannot
 * be fails the start (`SampleNotReady`), as before. A sample not yet made is made behind the page: the page loads at once
 * on the link the start minted, which is good for a minute while the seed takes longer, and shows the data as it lands;
 * `making` settles when the sample is ready or has failed, and never rejects.
 */
export async function openOnTheSample<T>(deps: OpeningDeps<T>): Promise<{ loaded: T; making: Promise<ReadyOutcome> | null }> {
    if (deps.sample === null) return { loaded: await deps.load(), making: null };
    if (readMarker(deps.sample) !== null) {
        const outcome = await readyTheSample(deps.sample, { begin: () => undefined, made: () => undefined });
        if (outcome.kind === 'failed') throw new SampleNotReady(outcome);
        return { loaded: await deps.load(), making: null };
    }
    deps.preparing(true);
    let loaded: T;
    try {
        loaded = await deps.load();
    } catch (error) {
        deps.preparing(false);
        throw error;
    }
    const making = readyTheSample(deps.sample, { begin: deps.request, made: deps.drop }).then(
        (outcome) => {
            deps.preparing(false);
            return outcome;
        },
        (error: unknown): ReadyOutcome => {
            deps.preparing(false);
            return { kind: 'failed', why: 'The sample could not be prepared. Choose Start the Demo Over in the menu to try again.', step: 'start', code: error instanceof Error ? 'thrown' : 'none' };
        },
    );
    return { loaded, making };
}

// ---------------------------------------------------------------------------------------------------------------------

/** The marker's file, in the demo installation's data directory. */
export const markerPath = (data: string): string => join(data, 'demo-seed.json');

/** The plugin folder the provider package is placed in, and the data module of it the story is read from. */
export const placedPackage = (data: string): string => join(dataLayout(data).pluginsDirectory, demoPluginId);
export const storyModule = (data: string): string => join(placedPackage(data), 'modules', 'story.js');

/** The seed's dependencies on the machine's own files. */
export function seedDepsOnDisk(parts: { cli: Cli; places: DemoPlaces; scratch: string; progress(step: SeedPhase | 'made'): void }): SeedDeps {
    return {
        cli: parts.cli,
        places: parts.places,
        markerFile: markerPath(parts.places.data),
        readText: (file) => (existsSync(file) ? readFileSync(file, 'utf8') : null),
        writeText: (file, text) => {
            // Through a file beside it renamed over it: a cut write leaves the earlier text whole, and a runtime that watches
            // the settings file never reads a half.
            mkdirSync(dirname(file), { recursive: true });
            const temporary = `${file}.${process.pid}.tmp`;
            try {
                writeFileSync(temporary, text, { encoding: 'utf8' });
                renameSync(temporary, file);
            } catch (error) {
                rmSync(temporary, { force: true });
                throw error;
            }
        },
        makeDirectory: (path) => void mkdirSync(path, { recursive: true }),
        removeTree: (path) => rmSync(path, { recursive: true, force: true }),
        readStory: (data) => readFileSync(storyModule(data), 'utf8'),
        progress: parts.progress,
        sleep: (ms) => new Promise((done) => setTimeout(done, ms)),
        now: () => new Date(),
        scratch: parts.scratch,
    };
}
