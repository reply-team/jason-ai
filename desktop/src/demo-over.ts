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
import { demoMarkerName } from './demo-folders.ts';
import type { DemoPlaces } from './demo-seed.ts';
import type { Logger } from './log.ts';

/** The request file's text: the asking run's process id, so a start that is not that run waits for it to end. */
export const startOverRequestText = (pid: number, making = false): string => `pid=${pid}\n${making ? `${makingLine}\n` : ''}`;

/**
 * The line that says a request was left by a making of the sample (`openOnTheSample`), not by Start the Demo Over: the
 * run that left it may still be running, making the sample or showing one whose making failed.
 */
export const makingLine = 'making';

/** Whether a request was left by a making of the sample. */
export const leftByAMaking = (text: string): boolean => text.split('\n').includes(makingLine);

/** What an aside copy's name carries after the folder's own: `<folder>.removing-<time>`. */
export const asideMarker = '.removing-';

/** How long a request names the asking run: the quit is capped at 15 s, so after this the pid says nothing. */
export const requestYoungMs = 120_000;

/** How long a carry-out waits for the asking run to end before it refuses for that reason alone. */
export const askingWaitMs = 5_000;

/** Why a carry-out was refused: a code, never a path. */
export type RefusalReason = 'asking_alive' | 'runtime_alive' | 'foreign' | 'not_taken_away' | 'places';

export class CarryOutRefused extends Error {
    readonly reason: RefusalReason;
    constructor(reason: RefusalReason, message: string) {
        super(message);
        this.reason = reason;
    }
}

/** Leaves the request at `file`, naming the asking process and whether a making left it; throws when it cannot. */
export function requestStartOver(file: string, pid: number = process.pid, making = false): void {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, startOverRequestText(pid, making));
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
    dropRequest(): void;
}

/**
 * Does nothing when no request is there, nor when the request is a making's and its run still runs (a second start of
 * that run). Otherwise, in this order: keeps the request and throws while the asking run is
 * still alive or a demo runtime is; refuses a folder there without the marker; takes each marked folder out of the demo's
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
    if (deps.runtimeAlive()) throw new CarryOutRefused('runtime_alive', 'A demo runtime is still running.');
    const folders = [deps.places.data, deps.places.harnessRoot];
    const foreign = folders.find((folder) => deps.exists(folder) && !deps.marked(folder));
    if (foreign !== undefined) throw new CarryOutRefused('foreign', `The folder ${foreign} is not one the demo made, so it was left.`);
    const moved: string[] = [];
    for (const folder of folders) {
        if (!deps.exists(folder)) continue;
        try {
            moved.push(deps.setAside(folder));
        } catch {
            throw new CarryOutRefused('not_taken_away', `The folder ${folder} could not be taken away.`);
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
        dropRequest: (): void => rmSync(file, { force: true }),
    };
}
