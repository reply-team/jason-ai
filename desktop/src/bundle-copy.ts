/**
 * The AppImage's carried jason. An AppImage runs from a mount that lasts only while it runs (`APPDIR`), so the bundle in
 * its resources is at a path that is gone once the application quits: a runtime started from it, a systemd unit that
 * names it, the PATH command's wrapper and the own-runtime check would all name a file that is not there next time. So
 * each start of an AppImage copies the bundle under `<data>/desktop/bundle/<version>/`, and that copy is the jason the
 * desktop carries everywhere: the CLI it runs, the runtime it starts and stops, the PATH command and the chat's PATH.
 * Where there is no such copy, the package names no version or the copy failed or differs, the mount's jason is never
 * the carried one for anything that outlasts the run (`carriedOf`): no runtime is started, and neither the start at
 * logon nor the PATH command is offered.
 *
 * The copy is written into a temporary folder file by file, each file read, written and synced, never linked, then
 * renamed into place, so a copy under its version is always whole. The folder is copied whole, never a chosen list of
 * files, so `version.json`, the record beside the executable that the command line reads, stays beside the copied
 * `jason`. A copy already there is used only when every file of it, the executable among them, has the SHA-256 of the
 * package's own and no file is more or fewer; one that differs is neither used nor overwritten, and the start stops
 * naming it (`differsWords`), so the copy never silently differs from what the package carries. The bundles folder and
 * the folder under the version are each a real folder or nothing: a link at either is a difference, never followed.
 *
 * What the person set up from an earlier version names that version's copy: the start at logon and the PATH command.
 * Each start re-points them to its own copy (`isOtherCopy`), the same act as enabling or placing them, and only those
 * the person set up; anything that names a jason outside the bundles folder is left as it is. Another version's copy is
 * removed only after that (`removeOtherCopies`), only when nothing the person set up names it any more and the running
 * runtime was not started from it, and never the one the rollback record names. The deb and an unpacked folder carry
 * their bundle where they are installed, and copy nothing.
 */

import { createHash } from 'node:crypto';
import { closeSync, fsyncSync, lstatSync, mkdirSync, openSync, readdirSync, readFileSync, readSync, renameSync, rmSync, statSync, writeSync, chmodSync } from 'node:fs';
import { dirname, isAbsolute, join, relative } from 'node:path';
import type { LinuxPackage } from './linux-package.ts';
import type { Logger } from './log.ts';
import type { Whose } from './own-runtime.ts';
import { containedIn, sameResolved, resolveReal, type Resolve } from './resolved-path.ts';

/** Whether this application copies its bundle out of the package: only an AppImage does. */
export function copiesBundle(form: LinuxPackage | null): boolean {
    return form?.form === 'appimage';
}

/** A copy under the version, or the bundles folder itself, that is not what the package's bundle copies to. */
export class BundleDiffers extends Error {}

/** What the starting page says when the copy under the version differs from the package's bundle. */
export function differsWords(copy: string): string {
    return `The jason this application carries is copied to ${copy}, and that copy no longer matches the one in this package, so Jason did not start with it. Something changed it after it was copied. Remove ${copy} and start Jason again: it copies the package’s jason there again.`;
}

/**
 * A version as a release names it, and so one name under the folder: never empty, a dot, a separator or a leading dot
 * (a temporary folder's), and never a word a person might give a folder of their own.
 */
const versionName = /^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.+-]*)?$/;
/** A temporary folder a start of this desktop makes beside the copies: `.<version>.tmp-<pid>`. */
const temporaryName = /^\.[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.+-]*)?\.tmp-[0-9]+$/;

function digestOf(file: string): string {
    const hash = createHash('sha256');
    const chunk = Buffer.alloc(1 << 20);
    const handle = openSync(file, 'r');
    try {
        for (let read = readSync(handle, chunk); read > 0; read = readSync(handle, chunk)) hash.update(chunk.subarray(0, read));
    } finally {
        closeSync(handle);
    }
    return hash.digest('hex');
}

/**
 * Every file under `folder` by its path with `/`, and its SHA-256. `follow` reads a link as the file it names (the
 * package's side); without it a link, or anything else that is not a file or a folder, is a difference in itself.
 */
function filesOf(folder: string, follow: boolean): Map<string, string> | null {
    const found = new Map<string, string>();
    const walk = (relative: string): boolean => {
        for (const name of readdirSync(join(folder, relative))) {
            const path = relative === '' ? name : `${relative}/${name}`;
            const full = join(folder, path);
            const entry = follow ? statSync(full) : lstatSync(full);
            if (entry.isDirectory()) {
                if (!walk(path)) return false;
            } else if (entry.isFile()) {
                found.set(path, digestOf(full));
            } else {
                return false;
            }
        }
        return true;
    };
    return walk('') ? found : null;
}

function same(a: Map<string, string> | null, b: Map<string, string>): boolean {
    if (a === null || a.size !== b.size) return false;
    for (const [path, digest] of b) if (a.get(path) !== digest) return false;
    return true;
}

/** What is at a path, by lstat: nothing, a real folder, or anything else, a link to a folder among them. */
function kindAt(path: string): 'absent' | 'folder' | 'other' {
    try {
        return lstatSync(path).isDirectory() ? 'folder' : 'other';
    } catch (error) {
        if ((error as NodeJS.ErrnoException).code === 'ENOENT') return 'absent';
        throw error;
    }
}

/** Syncs a folder's entries to the disk. Windows opens no folder to sync it, and there it is left to the system. */
function syncFolder(folder: string): void {
    let handle: number;
    try {
        handle = openSync(folder, 'r');
    } catch (error) {
        if (process.platform === 'win32') return;
        throw error;
    }
    try {
        fsyncSync(handle);
    } catch (error) {
        if (process.platform !== 'win32') throw error;
    } finally {
        closeSync(handle);
    }
}

/** Writes a file and syncs it, with the package's mode bits masked to 0755. */
function writeSynced(to: string, from: string): void {
    const handle = openSync(to, 'w');
    try {
        const content = readFileSync(from);
        for (let written = 0; written < content.length; ) written += writeSync(handle, content, written);
        fsyncSync(handle);
    } finally {
        closeSync(handle);
    }
    chmodSync(to, statSync(from).mode & 0o755);
}

/**
 * Whether `named` is a jason under the bundles folder other than `current`: another version's copy, to re-point. Decided
 * on resolved paths (`resolved-path.ts`), since what names it reports a resolved path and the bundles folder is held as
 * the data directory was given; where that cannot be told, it is not one, and nothing is re-pointed.
 */
export function isOtherCopy(named: string, bundles: string, current: string, resolve: Resolve = resolveReal): boolean {
    if (named === current || sameResolved(process.platform, named, current, resolve)) return false;
    return containedIn(process.platform, bundles, named, resolve) === 'inside';
}

/** What the bundles folder holds, by lstat: the real folders named as a version and this desktop's temporaries, and everything else by name. */
export type BundleEntries =
    | { kind: 'absent' }
    | { kind: 'other' }
    | { kind: 'unread' }
    | { kind: 'folder'; copies: string[]; temporaries: string[]; others: string[] };

/**
 * The one selection of the copies, which the removal of other versions' copies and the menu's Remove What Jason Placed
 * both act on: only real folders named as a version or as this desktop's temporary; a link, a file or a folder of any
 * other name is another's, and is never removed.
 */
export function bundleEntries(bundles: string): BundleEntries {
    let names: string[];
    try {
        const kind = kindAt(bundles);
        if (kind !== 'folder') return { kind };
        names = readdirSync(bundles);
    } catch {
        return { kind: 'unread' };
    }
    const copies: string[] = [];
    const temporaries: string[] = [];
    const others: string[] = [];
    for (const name of names) {
        let folder = false;
        try {
            folder = kindAt(join(bundles, name)) === 'folder';
        } catch {
            folder = false;
        }
        if (folder && versionName.test(name)) copies.push(name);
        else if (folder && temporaryName.test(name)) temporaries.push(name);
        else others.push(name);
    }
    return { kind: 'folder', copies, temporaries, others };
}

export interface BundleCopy {
    /** The package's bundle, under the mount. */
    source: string;
    /** `<data>/desktop/bundle`. */
    bundles: string;
    /** The version the package's bundle was prepared as. */
    version: string;
}

/**
 * The carried bundle: the copy under the version, made when it is not there. Throws `BundleDiffers` for one that differs
 * and for a bundles folder or a folder under the version that is not a real folder. It removes nothing but its own
 * temporary folder.
 */
export function carriedBundle(copy: BundleCopy): string {
    if (!versionName.test(copy.version)) throw new Error('The package’s bundle names no version a folder can carry.');
    if (kindAt(copy.bundles) === 'other') throw new BundleDiffers(differsWords(copy.bundles));
    const target = join(copy.bundles, copy.version);
    const wanted = filesOf(copy.source, true);
    if (wanted === null) throw new Error('The package’s bundle holds something that is neither a file nor a folder.');
    const there = kindAt(target);
    if (there === 'other') throw new BundleDiffers(differsWords(target));
    if (there === 'folder') {
        if (!same(filesOf(target, false), wanted)) throw new BundleDiffers(differsWords(target));
        return target;
    }
    const temporary = join(copy.bundles, `.${copy.version}.tmp-${process.pid}`);
    rmSync(temporary, { recursive: true, force: true });
    try {
        mkdirSync(temporary, { recursive: true });
        const folders = new Set<string>([temporary]);
        for (const path of wanted.keys()) {
            const to = join(temporary, path);
            mkdirSync(dirname(to), { recursive: true });
            folders.add(dirname(to));
            writeSynced(to, join(copy.source, path));
        }
        for (const folder of folders) syncFolder(folder);
        if (!same(filesOf(temporary, false), wanted)) throw new Error('The copy of the package’s bundle does not read back as the package’s.');
        renameSync(temporary, target);
        syncFolder(copy.bundles);
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
    return target;
}

/**
 * What this start carries, decided once: the folder whose jason it runs, what the starting page says when there is no
 * copy, and whether anything that outlasts the run may name that jason (`outlives`: a runtime it starts, the start at
 * logon, the PATH command) and whether what names another version's copy is re-pointed to it (`repoints`).
 */
export interface Carried {
    bundle: string;
    uncopied: string | null;
    failure: { differs: boolean } | null;
    outlives: boolean;
    repoints: boolean;
}

export function carriedOf(input: {
    form: LinuxPackage | null;
    packaged: string;
    version: string | null;
    bundles: string;
    copy?: (copy: BundleCopy) => string;
}): Carried {
    const { form, packaged, version, bundles } = input;
    if (!copiesBundle(form)) return { bundle: packaged, uncopied: null, failure: null, outlives: true, repoints: false };
    // An AppImage whose package names no version has no copy to carry: its mount's jason is never named past the run.
    if (version === null) return { bundle: packaged, uncopied: null, failure: null, outlives: false, repoints: false };
    try {
        return { bundle: (input.copy ?? carriedBundle)({ source: packaged, bundles, version }), uncopied: null, failure: null, outlives: true, repoints: true };
    } catch (error) {
        const differs = error instanceof BundleDiffers;
        const uncopied = differs
            ? error.message
            : `The jason this application carries could not be copied to ${join(bundles, version)}, so Jason did not start. Check that the data directory can be written to, then start Jason again.`;
        return { bundle: packaged, uncopied, failure: { differs }, outlives: false, repoints: false };
    }
}

export interface CopyRemoval {
    /** `<data>/desktop/bundle`. */
    bundles: string;
    /** This start's version, whose copy stays. */
    version: string;
    /** The versions whose copies stay: the one the rollback record names. */
    keep: readonly string[];
    /** The jason each thing the person set up names, after the re-points; null when that could not be read. */
    named(): Promise<readonly string[] | null>;
    /** Whose the running runtime is, asked as if `executable` were the carried jason (`whoseRuntime`). */
    whose(executable: string): Promise<Whose>;
    log: Logger;
    /** Removes a folder and what it holds; `rmSync` unless a test says otherwise. */
    remove?: (folder: string) => void;
    /** Resolves a path (`resolved-path.ts`); the system's own unless a test says otherwise. */
    resolve?: Resolve;
}

/**
 * Removes the copies of other versions, after the re-points: only real folders named as a version, never this start's
 * or one the rollback record names, never one something the person set up still names, and never one the running
 * runtime was started from. Where either cannot be told, nothing is removed at this start, and a later start tries
 * again. This desktop's own temporaries left by a start that stopped go with the copies. A removal that fails is logged
 * and never stops anything. What was removed is returned.
 */
export async function removeOtherCopies(removal: CopyRemoval): Promise<string[]> {
    const { bundles, log } = removal;
    const entries = bundleEntries(bundles);
    if (entries.kind === 'unread') log.error('bundle_copies_kept', { Why: 'unread' });
    if (entries.kind !== 'folder') return [];
    const kept = new Set([removal.version, ...removal.keep]);
    const temporaries = entries.temporaries;
    let copies = entries.copies.filter((name) => !kept.has(name));
    if (copies.length > 0) {
        const named = await removal.named().catch(() => null);
        if (named === null) {
            log.warn('bundle_copies_kept', { Why: 'unread' });
            return [];
        }
        const free: string[] = [];
        for (const name of copies) {
            const folder = join(bundles, name);
            // What names a jason reports it resolved: a copy it may be in, or cannot be told not to be in, stays.
            if (named.some((jason) => containedIn(process.platform, folder, jason, removal.resolve) !== 'outside')) continue;
            const whose = await removal.whose(join(folder, 'jason')).catch((): Whose => 'unanswered');
            if (whose === 'unanswered') {
                log.warn('bundle_copies_kept', { Why: 'unanswered' });
                return [];
            }
            if (whose !== 'ours') free.push(name);
        }
        copies = free;
    }
    const remove = removal.remove ?? ((folder: string) => rmSync(folder, { recursive: true, force: true }));
    const removed: string[] = [];
    for (const name of [...temporaries, ...copies]) {
        try {
            remove(join(bundles, name));
            removed.push(name);
        } catch {
            log.error('bundle_copy_removal_failed', {});
        }
    }
    if (copies.length > 0) log.info('bundle_copies_removed', { Count: copies.filter((name) => removed.includes(name)).length });
    return removed;
}
