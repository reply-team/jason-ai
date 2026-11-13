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

import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import type { DemoPlaces } from './demo-seed.ts';

/** The request file's text: the asking run's process id, so a start that is not that run waits for it to end. */
export const startOverRequestText = (pid: number): string => `pid=${pid}\n`;

/** What an aside copy's name carries after the folder's own: `<folder>.removing-<time>`. */
export const asideMarker = '.removing-';

/** Leaves the request at `file`, naming the asking process; throws when it cannot. */
export function requestStartOver(file: string, pid: number = process.pid): void {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, startOverRequestText(pid));
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
 * Does nothing when no request is there. Otherwise, in this order: keeps the request and throws while the asking run is
 * still alive or a demo runtime is; refuses a folder there without the marker; takes each marked folder out of the demo's
 * name by ONE rename, so a failure changes nothing; removes the renamed copies as far as it can (one it cannot is left,
 * marked, and removed at a later carry-out); drops the request. Never touches anything but the two folders and their own
 * aside copies.
 */
export function carryOutStartOver(deps: CarryOutDeps): void {
    if (!deps.requested()) return;
    const asking = askedBy(deps.requestText());
    if (asking !== null && asking !== deps.self && deps.running(asking)) throw new Error('The run that asked is still ending.');
    if (deps.runtimeAlive()) throw new Error('A demo runtime is still running.');
    const folders = [deps.places.data, deps.places.harnessRoot];
    const foreign = folders.find((folder) => deps.exists(folder) && !deps.marked(folder));
    if (foreign !== undefined) throw new Error(`The folder ${foreign} is not one the demo made, so it was left.`);
    const moved: string[] = [];
    for (const folder of folders) {
        if (!deps.exists(folder)) continue;
        try {
            moved.push(deps.setAside(folder));
        } catch {
            throw new Error(`The folder ${folder} could not be taken away.`);
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
        running,
        self: process.pid,
        places,
        setAside: (folder: string): string => {
            const aside = `${folder}${asideMarker}${Date.now()}`;
            renameSync(folder, aside);
            return aside;
        },
        asides: asidesOf,
        removeTree: (folder: string): void => rmSync(folder, { recursive: true, force: true }),
        dropRequest: (): void => rmSync(file, { force: true }),
    };
}
