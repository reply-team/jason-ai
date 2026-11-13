/**
 * Starting the demo over, the part that must not run inside the demo: the running desktop lives in the demo's data
 * directory (its log, its Electron profile, its chat folders), so it cannot remove that folder, and a log line written
 * after the removal would make it again without its marker. The run that is asked leaves a request in the PERSON's
 * `desktop/` folder (`requestStartOver`) and starts again; the next start, before anything names the demo's directory,
 * carries it out (`carryOutStartOver`). Each marked folder is taken out of the demo's name by one rename, never deleted in
 * place: a deletion that fails part-way would lose the marker first and leave a folder nothing could claim again. A start
 * that cannot carry the request out keeps it and throws, which makes that start the person's own (`startOn`), so the next
 * demo start tries again.
 */

import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { demoMarkerName, markPlacedFolder } from './demo-folders.ts';
import type { DemoPlaces } from './demo-seed.ts';
import type { Logger } from './log.ts';

/** The request file's text: the asking run's process id, so a start that is not that run waits for it to end. */
export const startOverRequestText = (pid: number, making = false, placing = false, importPid: number | null = null): string =>
    `pid=${pid}\n${making ? `${makingLine}\n` : ''}${placing ? `${placingLine}\n` : ''}${importPid === null ? '' : `import=${importPid}\n`}`;

/**
 * The line that says a request was left by a making of the sample (`openOnTheSample`), not by Start the Demo Over: the
 * run that left it may still be running, making the sample or showing one whose making failed.
 */
export const makingLine = 'making';

/** Whether a request was left by a making of the sample. */
export const leftByAMaking = (text: string): boolean => text.split('\n').includes(makingLine);

/**
 * The line that says the request was left by a placing of the prepared demo (`demo-archive.ts`): `jason import` makes the
 * demo's harness root itself, from a root that was not there, and the marker is written into it once the import has
 * ended, so a start cut short in between leaves a root of the demo's that carries no marker.
 */
export const placingLine = 'placing';

/** Whether a request was left by a placing of the prepared demo. */
export const leftByAPlacing = (text: string): boolean => text.split('\n').includes(placingLine);

/**
 * The process of the placing's `jason import`, which the request names once it has started: a child that outlives the run
 * that started it (a crash, a kill) still writes into the demo's folders until it ends. Null when the text names none.
 */
export function importedBy(text: string): number | null {
    const found = /^import=(\d+)$/m.exec(text);
    const pid = found === null ? NaN : Number(found[1]);
    return Number.isSafeInteger(pid) && pid > 0 ? pid : null;
}

/** How long a request names its import: the import's own limit, with room; after this the process id says nothing. */
export const importYoungMs = 300_000;

/** What an aside copy's name carries after the folder's own: `<folder>.removing-<time>`. */
export const asideMarker = '.removing-';

/** How long a request names the asking run: the quit is capped at 15 s, so after this the pid says nothing. */
export const requestYoungMs = 120_000;

/** How long a carry-out waits for the asking run to end before it refuses for that reason alone. */
export const askingWaitMs = 5_000;

/** Why a carry-out was refused: a code, never a path. */
export type RefusalReason = 'asking_alive' | 'import_alive' | 'runtime_alive' | 'foreign' | 'not_taken_away' | 'places';

export class CarryOutRefused extends Error {
    readonly reason: RefusalReason;
    constructor(reason: RefusalReason, message: string) {
        super(message);
        this.reason = reason;
    }
}

/**
 * Leaves the request at `file`, naming the asking process, whether a making or a placing left it and the placing's import
 * once it runs; throws when it cannot.
 */
export function requestStartOver(file: string, pid: number = process.pid, making = false, placing = false, importPid: number | null = null): void {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, startOverRequestText(pid, making, placing, importPid));
}

/**
 * Takes the request away once a making of the sample is whole; one that cannot be removed is logged and kept, and the
 * next start then makes the sample again, which costs time and never shows a half.
 */
export function dropStartOver(file: string, log: Pick<Logger, 'warn'>): void {
    try {
        rmSync(file, { force: true });
    } catch (error) {
        log.warn('demo_start_over_request_unremoved', { Code: (error as NodeJS.ErrnoException).code ?? 'unknown' });
    }
}

/**
 * Whether another run, still running, is placing the prepared demo: the request it left names a placing and a process
 * that is not this one and runs. A second start in those seconds writes nothing into the demo: the import would place
 * over it or move it aside.
 */
export function placingInFlight(text: string | null, self: number, running: (pid: number) => boolean): boolean {
    if (text === null || !leftByAPlacing(text)) return false;
    const asking = askedBy(text);
    const importing = importedBy(text);
    return (asking !== null && asking !== self && running(asking)) || (importing !== null && running(importing));
}

/**
 * Whether a carry-out refused for a live demo runtime alone may stop that runtime first (`stopLeftDemoRuntime`): the
 * request was left by a making or a placing, and neither the run that left it nor its import still runs, so the runtime
 * is one a crash or a kill left behind, which nothing else would ever stop. Start the Demo Over's own request stops
 * nothing here: its run stopped the demo's runtime before it asked.
 */
export function leftRuntimeStops(text: string | null, self: number, running: (pid: number) => boolean): boolean {
    if (text === null || !(leftByAMaking(text) || leftByAPlacing(text))) return false;
    // Once for a request: a runtime that is back by the next start is refused as any other (`noteLeftStopped`).
    if (text.split('\n').includes(leftStoppedLine)) return false;
    const asking = askedBy(text);
    const importing = importedBy(text);
    if (asking !== null && asking !== self && running(asking)) return false;
    return importing === null || !running(importing);
}

/** The line that says a left demo runtime was already stopped for this request, and the desktop started again. */
export const leftStoppedLine = 'stopped';

/** Adds `leftStoppedLine` to the request at `file`, once; throws when it cannot. */
export function noteLeftStopped(file: string): void {
    const text = readFileSync(file, 'utf8');
    if (text.split('\n').includes(leftStoppedLine)) return;
    writeFileSync(file, `${text.endsWith('\n') || text === '' ? text : `${text}\n`}${leftStoppedLine}\n`);
}

/** The process that asked, or null when the text does not say. */
export function askedBy(text: string): number | null {
    const found = /^pid=(\d+)$/m.exec(text);
    const pid = found === null ? NaN : Number(found[1]);
    return Number.isSafeInteger(pid) && pid > 0 ? pid : null;
}

export interface CarryOutDeps {
    requested(): boolean;
    /** The text of the request. */
    requestText(): string;
    /** How old the request file is, in milliseconds. */
    requestAgeMs(): number;
    /** Waits, synchronously, for at most this long. */
    wait(ms: number): void;
    /** How long to wait for the asking run; `askingWaitMs` when absent. */
    askingWaitMs?: number;
    /** Whether a process is running. */
    running(pid: number): boolean;
    /** The process doing the carrying out. */
    self: number;
    places: DemoPlaces;
    /** Whether a demo runtime is alive on the demo's data directory (`demoRuntimeAlive`). */
    runtimeAlive(): boolean;
    exists(folder: string): boolean;
    marked(folder: string): boolean;
    /** Renames a folder to a name of its own beside it, in one step, and answers the new path; throws, changing nothing, when it cannot. */
    setAside(folder: string): string;
    /** The aside copies of a folder that are there. */
    asides(folder: string): string[];
    removeTree(folder: string): void;
    /** Writes the marker into an aside copy of a root a placing's import made, so a removal cut short is collected later. */
    mark?(folder: string): void;
    dropRequest(): void;
}

/**
 * Does nothing when no request is there, nor when the request is a making's and its run still runs (a second start of
 * that run). Otherwise, in this order: keeps the request and throws while the asking run is
 * still alive or a demo runtime is; refuses a folder there without the marker, but the harness root a placing's import
 * made (`placingLine`); takes each marked folder out of the demo's
 * name by ONE rename, so a failure changes nothing; removes the renamed copies as far as it can (one it cannot is left,
 * marked, and removed at a later carry-out); drops the request. Never touches anything but the two folders and their own
 * aside copies.
 */
export function carryOutStartOver(deps: CarryOutDeps): void {
    if (!deps.requested()) return;
    const text = deps.requestText();
    const asking = askedBy(text);
    // A making's request whose run is still running is that run's own, left while it makes the sample or shows one whose
    // making failed: this start is a second start of that run, which loses the instance lock and quits. It carries
    // nothing out and is refused nothing, so it never sends the record to Live; the request stays for a real next start.
    if (asking !== null && asking !== deps.self && leftByAMaking(text) && deps.running(asking)) return;
    // The asking run is honoured only while the request is young: an older request is of a run that has long gone, and its
    // process id may be anyone's now. A young one is waited for a few seconds, then refused.
    if (asking !== null && asking !== deps.self && deps.requestAgeMs() <= requestYoungMs) {
        const limit = deps.askingWaitMs ?? askingWaitMs;
        for (let waited = 0; deps.running(asking) && waited < limit; waited += 250) deps.wait(250);
        if (deps.running(asking)) throw new CarryOutRefused('asking_alive', 'The run that asked is still ending.');
    }
    // A placing's import that outlived its run still writes into the demo: it is waited for the same few seconds, then
    // refused, and the request kept, so the start after it ends carries it out.
    const importing = importedBy(text);
    if (importing !== null && importing !== deps.self && deps.requestAgeMs() <= importYoungMs) {
        const limit = deps.askingWaitMs ?? askingWaitMs;
        for (let waited = 0; deps.running(importing) && waited < limit; waited += 250) deps.wait(250);
        if (deps.running(importing)) throw new CarryOutRefused('import_alive', 'The demo is still being placed.');
    }
    if (deps.runtimeAlive()) throw new CarryOutRefused('runtime_alive', 'A demo runtime is still running.');
    const folders = [deps.places.data, deps.places.harnessRoot];
    // A placing's request names the harness root as the import's own, made where nothing stood: it is the demo's while
    // the data directory beside it carries the marker, the marker the root would have been given once the import ended.
    const placed = leftByAPlacing(text) && deps.marked(deps.places.data);
    const ours = (folder: string): boolean => deps.marked(folder) || (placed && folder === deps.places.harnessRoot);
    const foreign = folders.find((folder) => deps.exists(folder) && !ours(folder));
    if (foreign !== undefined) throw new CarryOutRefused('foreign', `The folder ${foreign} is not one the demo made, so it was left.`);
    const moved: string[] = [];
    for (const folder of folders) {
        if (!deps.exists(folder)) continue;
        const unmarked = !deps.marked(folder);
        let aside: string;
        try {
            aside = deps.setAside(folder);
        } catch {
            throw new CarryOutRefused('not_taken_away', `The folder ${folder} could not be taken away.`);
        }
        moved.push(aside);
        if (unmarked) {
            try {
                deps.mark?.(aside);
            } catch {
                // Removed below all the same; only a removal cut short would then be left.
            }
        }
    }
    // What an earlier carry-out left: removed only when it still carries the marker, as far as it goes.
    const leftovers = folders.flatMap((folder) => deps.asides(folder)).filter((aside) => !moved.includes(aside) && deps.marked(aside));
    for (const aside of [...moved, ...leftovers]) {
        try {
            deps.removeTree(aside);
        } catch {
            // Left where it is, marked, and removed at a later carry-out.
        }
    }
    deps.dropRequest();
}

/**
 * Removes a marked folder with its marker last: every other entry first, then the marker, then the folder. A removal that
 * stops part-way (a file held open on Windows) leaves the marker, so the copy is still known as the demo's and a later
 * carry-out, Remove What Jason Placed or the uninstall can finish it. Throws at the first entry it cannot remove.
 */
export function removeMarkedLast(folder: string, remove: (path: string) => void = (path) => rmSync(path, { recursive: true, force: true }), marker: string = demoMarkerName): void {
    for (const name of readdirSync(folder)) {
        if (name !== marker) remove(join(folder, name));
    }
    remove(join(folder, marker));
    rmdirSync(folder);
}

/** The aside copies (`<folder>.removing-*`) that lie beside a folder, as paths; none when the parent cannot be read. */
export function asidesOf(folder: string): string[] {
    try {
        return readdirSync(dirname(folder))
            .filter((name) => name.startsWith(`${basename(folder)}${asideMarker}`))
            .map((name) => join(dirname(folder), name));
    } catch {
        return [];
    }
}

/** The disk side of the above, for the person's request file. */
export function carryOutOnDisk(file: string, places: DemoPlaces, running: (pid: number) => boolean) {
    return {
        requested: (): boolean => existsSync(file),
        requestText: (): string => {
            try {
                return readFileSync(file, 'utf8');
            } catch {
                return '';
            }
        },
        requestAgeMs: (): number => {
            try {
                return Date.now() - statSync(file).mtimeMs;
            } catch {
                return Infinity;
            }
        },
        wait: (ms: number): void => void Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms),
        running,
        self: process.pid,
        places,
        setAside: (folder: string): string => {
            const aside = `${folder}${asideMarker}${Date.now()}`;
            renameSync(folder, aside);
            return aside;
        },
        asides: asidesOf,
        removeTree: removeMarkedLast,
        mark: markPlacedFolder,
        dropRequest: (): void => rmSync(file, { force: true }),
    };
}
