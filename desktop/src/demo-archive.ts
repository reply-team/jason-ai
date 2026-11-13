/**
 * The prepared demo: the sample made once, when the release is built, by the harness's own seed and `jason export`, and
 * carried by the application as `bundle/demo/jason-demo.zip`. At the first start of a demo that holds no sample, and at
 * the start that follows Start the Demo Over, `jason import` places it into the demo's data directory and harness root,
 * so the demo opens in seconds rather than the minute its making takes. The import is the verb as a person would type
 * it; nothing of it is bent for the demo.
 *
 * The order, from the first instant of the start:
 *
 * 1. the plan (`placingPlan`), before the demo's directory is claimed: an archive is placed only into a demo that holds
 *    nothing yet (no data directory, or one holding only the desktop's own marker and folder) and whose harness root is
 *    not there, since an import moves aside a root that holds anything, the marker included; the archive must be there,
 *    readable, and made at most twenty-one days ago, by the moment its own record (`export.json`'s `exported_at`) names,
 *    never a file's date. Anything else makes the sample at this start, as before;
 * 2. the start (`beginPlacing`), once the single-instance lock is won and before the desktop's log is opened: a request
 *    to start the demo over is left first, naming a placing, so a start cut short makes the demo whole again at the next;
 *    the log is held (`LogHold`), since `<data>/logs/` is a part an import places; then `jason import` is started, beside
 *    the window's own start;
 * 3. the end (`settlePlacing`), awaited before the runtime is started: an import that answered is whole only when every
 *    project it placed is recorded at a folder under the demo's harness root (`projectsWhole`), since the import answers
 *    success with a project it could not link; then the harness root the import made is marked, the sample's marker (`demo-seed.json`) is written whole, the request is taken away and the held lines are
 *    written; on any failure what the import placed is taken away, the held lines are written, the failure is logged by
 *    its step and its code, never a path, and the demo is prepared and its sample made at this start, as before.
 */

import { closeSync, existsSync, fstatSync, lstatSync, openSync, readdirSync, readSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { crc32, inflateRawSync } from 'node:zlib';
import type { Cli, CliResult } from './cli.ts';
import { demoMarkerName } from './demo-folders.ts';
import { isWithin, markerPath, seedVersion, type DemoPlaces } from './demo-seed.ts';
import type { Logger } from './log.ts';

/** Where the application carries the prepared demo, under its bundle. */
export const demoArchiveOf = (bundle: string): string => join(bundle, 'demo', 'jason-demo.zip');

/** How old a prepared demo may be when it is placed, in days: an older one is not placed, and the sample is made instead. */
export const archiveFreshDays = 21;

/** How long the import may take, its start of the runtime included, before it is taken as failed. */
export const placingLimitMs = 180_000;

/** The sentence a start says when neither the placing nor the making could begin: the one the making's failure says. */
export const sampleUnprepared = 'The sample could not be prepared. Choose Start the Demo Over in the menu to try again.';

/** The record an export writes at the archive's root. */
const recordName = 'export.json';

/** The largest record read: a demo's lists a few hundred files. */
const recordLimit = 16 * 1024 * 1024;

export class ArchiveUnreadable extends Error {}

function readAt(fd: number, position: number, length: number): Buffer {
    const buffer = Buffer.alloc(length);
    let done = 0;
    while (done < length) {
        const read = readSync(fd, buffer, done, length - done, position + done);
        if (read === 0) throw new ArchiveUnreadable('The archive ends early.');
        done += read;
    }
    return buffer;
}

/**
 * Reads the archive's own record, `export.json`, by the zip's central directory: the record's entry is found by name, its
 * bytes read from where its local header says and inflated, and its checksum compared. Throws `ArchiveUnreadable` for a
 * file that is not such a zip, a zip64 one, a record that is missing, encrypted, too large, damaged or not a JSON object.
 */
export function readExportRecord(file: string): Record<string, unknown> {
    const fd = openSync(file, 'r');
    try {
        const size = fstatSync(fd).size;
        const tailLength = Math.min(size, 22 + 0xffff);
        if (tailLength < 22) throw new ArchiveUnreadable('The archive is too short.');
        const tail = readAt(fd, size - tailLength, tailLength);
        let end = -1;
        for (let at = tail.length - 22; at >= 0; at--) {
            if (tail.readUInt32LE(at) === 0x06054b50) {
                end = at;
                break;
            }
        }
        if (end < 0) throw new ArchiveUnreadable('The archive has no central directory.');
        const count = tail.readUInt16LE(end + 10);
        const directoryLength = tail.readUInt32LE(end + 12);
        const directoryAt = tail.readUInt32LE(end + 16);
        if (count === 0xffff || directoryLength === 0xffffffff || directoryAt === 0xffffffff || directoryAt + directoryLength > size) {
            throw new ArchiveUnreadable('The archive’s central directory cannot be read.');
        }
        const directory = readAt(fd, directoryAt, directoryLength);
        let at = 0;
        for (let entry = 0; entry < count; entry++) {
            if (at + 46 > directory.length || directory.readUInt32LE(at) !== 0x02014b50) throw new ArchiveUnreadable('The archive’s central directory is damaged.');
            const flags = directory.readUInt16LE(at + 8);
            const method = directory.readUInt16LE(at + 10);
            const checksum = directory.readUInt32LE(at + 16);
            const compressed = directory.readUInt32LE(at + 20);
            const length = directory.readUInt32LE(at + 24);
            const nameLength = directory.readUInt16LE(at + 28);
            const extraLength = directory.readUInt16LE(at + 30);
            const commentLength = directory.readUInt16LE(at + 32);
            const local = directory.readUInt32LE(at + 42);
            const name = directory.toString('utf8', at + 46, at + 46 + nameLength);
            at += 46 + nameLength + extraLength + commentLength;
            if (name !== recordName) continue;
            if ((flags & 1) !== 0 || (method !== 0 && method !== 8) || length > recordLimit || compressed > recordLimit) throw new ArchiveUnreadable('The archive’s record cannot be read.');
            const header = readAt(fd, local, 30);
            if (header.readUInt32LE(0) !== 0x04034b50) throw new ArchiveUnreadable('The archive’s record is damaged.');
            const body = readAt(fd, local + 30 + header.readUInt16LE(26) + header.readUInt16LE(28), compressed);
            let bytes: Buffer;
            try {
                bytes = method === 0 ? body : inflateRawSync(body, { maxOutputLength: recordLimit });
            } catch {
                throw new ArchiveUnreadable('The archive’s record is damaged.');
            }
            if (bytes.length !== length || crc32(bytes) !== checksum) throw new ArchiveUnreadable('The archive’s record is damaged.');
            let value: unknown;
            try {
                value = JSON.parse(bytes.toString('utf8'));
            } catch {
                throw new ArchiveUnreadable('The archive’s record is not JSON.');
            }
            if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new ArchiveUnreadable('The archive’s record is not an object.');
            return value as Record<string, unknown>;
        }
        throw new ArchiveUnreadable('The archive carries no record.');
    } finally {
        closeSync(fd);
    }
}

/** The moment the archive's record says it was made (`exported_at`); throws `ArchiveUnreadable` when it says none. */
export function exportedAtOf(file: string): Date {
    const said = readExportRecord(file)['exported_at'];
    const at = typeof said === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(said) ? Date.parse(said) : NaN;
    if (!Number.isFinite(at)) throw new ArchiveUnreadable('The archive’s record names no moment.');
    return new Date(at);
}

/** Pure: whether an archive made at `made` may be placed at `now`: at most `archiveFreshDays` old. */
export function isFresh(made: Date, now: Date): boolean {
    return now.getTime() - made.getTime() <= archiveFreshDays * 24 * 60 * 60 * 1000;
}

/** What the desktop itself may hold in a demo data directory an archive is placed into: its marker and its own folder. */
const desktopsOwn = new Set([demoMarkerName, 'desktop']);

/** Whether the demo's data directory holds nothing an import would place over or move aside: absent, or the desktop's own alone. */
export function holdsNothing(data: string): boolean {
    try {
        return readdirSync(data).every((name) => desktopsOwn.has(name));
    } catch (error) {
        return (error as NodeJS.ErrnoException).code === 'ENOENT';
    }
}

/** Why a start of a demo that holds no sample makes it rather than place it: a step, never a path. */
export type UnplacedStep = 'archive_missing' | 'archive_unreadable' | 'archive_stale' | 'root_present';

export type PlacingPlan =
    /** The demo holds something already: this start is as any other. */
    | { kind: 'none' }
    /** The demo holds nothing and the archive is not placed: the sample is made at this start. */
    | { kind: 'make'; step: UnplacedStep }
    | { kind: 'place'; archive: string; exportedAt: Date };

/** The plan of a demo start (step 1 above). */
export function placingPlan(input: { archive: string; places: DemoPlaces; now: Date }): PlacingPlan {
    if (!holdsNothing(input.places.data)) return { kind: 'none' };
    if (pathThere(input.places.harnessRoot)) return { kind: 'make', step: 'root_present' };
    if (!existsSync(input.archive)) return { kind: 'make', step: 'archive_missing' };
    let exportedAt: Date;
    try {
        exportedAt = exportedAtOf(input.archive);
    } catch {
        return { kind: 'make', step: 'archive_unreadable' };
    }
    if (!isFresh(exportedAt, input.now)) return { kind: 'make', step: 'archive_stale' };
    return { kind: 'place', archive: input.archive, exportedAt };
}

function pathThere(path: string): boolean {
    try {
        lstatSync(path);
        return true;
    } catch {
        return false;
    }
}

export type PlacingOutcome = { kind: 'placed'; elapsedMs: number } | { kind: 'failed'; step: string; code: string };

/** A file-system error's code, or `thrown`: what a log line may carry of a failure, never its text. */
const codeOfError = (error: unknown): string => {
    const code = (error as { code?: unknown } | null)?.code;
    return typeof code === 'string' && /^[A-Za-z0-9_]{1,64}$/.test(code) ? code : 'thrown';
};

/** The verb's snake_case error code, never its message, which names paths; or its exit. */
function codeOfResult(result: CliResult): string {
    try {
        const error = (JSON.parse(result.stdout.trim()) as { error?: { code?: unknown } } | null)?.error;
        if (typeof error?.code === 'string' && /^[a-z0-9_]{1,64}$/.test(error.code)) return error.code;
    } catch {
        // Not the verb's JSON; the exit says it.
    }
    return result.exit === -1 ? 'unstarted' : `exit_${result.exit}`;
}

export interface PlacingStart {
    /** The jason the application carries, on the demo's data directory. */
    cli: Cli;
    archive: string;
    places: DemoPlaces;
    /** Leaves the request to start the demo over, naming a placing; throws when it cannot, and then nothing is placed. */
    request(): void;
    /** Holds the log (`LogHold.hold`). */
    hold(): void;
    /** Milliseconds, for the time the import took. */
    now?: () => number;
}

/**
 * Starts the placing (step 2 above): the request, the hold, then `jason import <archive> --root <the demo's harness root>`
 * on the demo's data directory. Answers a promise that settles when the import has ended, and never rejects.
 */
export function beginPlacing(start: PlacingStart): Promise<PlacingOutcome> {
    try {
        start.request();
    } catch (error) {
        return Promise.resolve({ kind: 'failed', step: 'request', code: codeOfError(error) });
    }
    start.hold();
    const now = start.now ?? Date.now;
    const began = now();
    return start.cli.run(['import', start.archive, '--root', start.places.harnessRoot], { timeoutMs: placingLimitMs }).then(
        (result): PlacingOutcome => (result.exit === 0 ? { kind: 'placed', elapsedMs: Math.max(0, Math.round(now() - began)) } : { kind: 'failed', step: 'import', code: codeOfResult(result) }),
        (): PlacingOutcome => ({ kind: 'failed', step: 'import', code: 'thrown' }),
    );
}

export interface PlacingEnd {
    log: Logger;
    /** Whether every placed project is recorded under the demo's root (`projectsWhole`): null when it is, else a code. */
    linked(): Promise<string | null>;
    /** Writes every held line (`LogHold.release`). */
    release(): void;
    /** Marks the harness root the import made (`markPlacedFolder`). */
    markRoot(): void;
    /** Writes the sample's marker whole (`writeSampleWhole`). */
    seal(): void;
    /** Takes the request away. */
    drop(): void;
    /** Whether a demo runtime is alive on the demo's data directory: the import starts one when it has placed. */
    runtimeAlive(): boolean;
    /** Stops it; resolves false when it could not. */
    stopRuntime(): Promise<boolean>;
    /** Takes away what the import placed (`clearPlaced`). */
    clear(): void;
    /** Prepares the demo as a start that makes its sample does (`prepareDemo`). */
    prepareLive(): void;
}

/** A start whose placing failed and whose making could not be readied either: the start fails, saying `sampleUnprepared`. */
export class PlacingFailed extends Error {}

/**
 * Ends the placing (step 3 above), before the runtime is started: answers `placed` when the sample is placed and sealed,
 * `made` when it fell back to making the sample at this start; throws `PlacingFailed` when the fall-back could not begin.
 * The held lines are written in every case, after the demo's folders are settled and before the first line of the end.
 */
export async function settlePlacing(placing: Promise<PlacingOutcome>, deps: PlacingEnd): Promise<'placed' | 'made'> {
    let outcome: PlacingOutcome = await placing.catch((): PlacingOutcome => ({ kind: 'failed', step: 'import', code: 'thrown' }));
    if (outcome.kind === 'placed') {
        try {
            const unlinked = await deps.linked();
            if (unlinked !== null) outcome = { kind: 'failed', step: 'unlinked', code: unlinked === 'unlinked' ? 'none' : unlinked };
        } catch (error) {
            outcome = { kind: 'failed', step: 'unlinked', code: codeOfError(error) };
        }
    }
    if (outcome.kind === 'placed') {
        // The root first, the seal last: a sample is whole only once both of its folders are the demo's.
        try {
            deps.markRoot();
        } catch (error) {
            outcome = { kind: 'failed', step: 'root', code: codeOfError(error) };
        }
        if (outcome.kind === 'placed') {
            try {
                deps.seal();
            } catch (error) {
                outcome = { kind: 'failed', step: 'seal', code: codeOfError(error) };
            }
        }
        if (outcome.kind === 'placed') {
            deps.drop();
            deps.release();
            deps.log.info('demo_archive_placed', { ElapsedMs: outcome.elapsedMs });
            return 'placed';
        }
    }
    // The fall-back: what the import placed goes, a runtime it started first, so the sample is made on an empty demo.
    let cleared = true;
    try {
        if (deps.runtimeAlive() && !(await deps.stopRuntime())) cleared = false;
        else deps.clear();
    } catch {
        cleared = false;
    }
    deps.release();
    deps.log.warn('demo_archive_unplaced', { Step: outcome.step, Code: outcome.code });
    if (!cleared) {
        deps.log.error('demo_seed_failed', { Step: 'clear', Code: 'none' });
        throw new PlacingFailed(sampleUnprepared);
    }
    try {
        deps.prepareLive();
    } catch (error) {
        deps.log.error('demo_seed_failed', { Step: 'prepare', Code: codeOfError(error) });
        throw new PlacingFailed(sampleUnprepared);
    }
    return 'made';
}

/**
 * Whether every project the runtime holds after the import is recorded at a folder under the demo's harness root, read
 * by `jason project list`, every page of it: null when each is, `unlinked` when one is recorded elsewhere (the folder it
 * was recorded at where the archive was made) or has none, the default project aside, which has no folder until one is
 * needed; the verb's code when the list is not answered.
 */
export async function projectsWhole(cli: Cli, root: string): Promise<string | null> {
    let cursor: string | null = null;
    for (let page = 0; page < 100; page++) {
        const result = await cli.run(['project', 'list', '--limit', '1000', ...(cursor === null ? [] : ['--cursor', cursor])]);
        if (result.exit !== 0) return codeOfResult(result);
        let answer: { items?: unknown; next_cursor?: unknown };
        try {
            answer = JSON.parse(result.stdout.trim()) as { items?: unknown; next_cursor?: unknown };
        } catch {
            return 'unreadable';
        }
        if (!Array.isArray(answer.items)) return 'unreadable';
        for (const item of answer.items as { default?: unknown; folder_path?: unknown }[]) {
            const folder = item.folder_path;
            if (typeof folder === 'string' ? !isWithin(root, folder) : item.default !== true) return 'unlinked';
        }
        if (typeof answer.next_cursor !== 'string' || answer.next_cursor === '') return null;
        cursor = answer.next_cursor;
    }
    return 'unreadable';
}

/**
 * Takes away what an import placed, from a demo that held nothing before it (`holdsNothing`): every entry of the data
 * directory but the desktop's marker, its own folder and the logs, and the harness root, which was not there.
 */
export function clearPlaced(places: DemoPlaces): void {
    for (const name of readdirSync(places.data)) {
        if (desktopsOwn.has(name) || name === 'logs') continue;
        rmSync(join(places.data, name), { recursive: true, force: true });
    }
    rmSync(places.harnessRoot, { recursive: true, force: true });
}

/** Writes the sample's marker as a seed that ran whole writes it, through a file beside it renamed over it. */
export function writeSampleWhole(data: string): void {
    const file = markerPath(data);
    const temporary = `${file}.${process.pid}.tmp`;
    try {
        writeFileSync(temporary, `${JSON.stringify({ version: seedVersion, complete: true })}\n`);
        renameSync(temporary, file);
    } catch (error) {
        rmSync(temporary, { force: true });
        throw error;
    }
}

/** What the starting page says while the prepared demo is placed: the words of the mark the page shows while it is made. */
export const placingWords = 'Preparing the demo…';
