/**
 * The AppImage's carried jason. An AppImage runs from a mount that lasts only while it runs (`APPDIR`), so the bundle in
 * its resources is at a path that is gone once the application quits: a runtime started from it, a systemd unit that
 * names it, the PATH command's wrapper and the own-runtime check would all name a file that is not there next time. So
 * each start of an AppImage copies the bundle under `<data>/desktop/bundle/<version>/`, and that copy is the jason the
 * desktop carries everywhere: the CLI it runs, the runtime it starts and stops, the PATH command and the chat's PATH.
 *
 * The copy is written into a temporary folder file by file, each file read and written, never linked, then renamed into
 * place, so a copy under its version is always whole. The folder is copied whole, never a chosen list of files, so
 * `version.json`, the record beside the executable that the command line reads, stays beside the copied `jason`. A copy already there is used only when every file of it, the
 * executable among them, has the SHA-256 of the package's own and no file is more or fewer; one that differs is neither
 * used nor overwritten, and the start stops naming it (`differsWords`), so the copy never silently differs from what the
 * package carries. The copies of other versions are removed, but the one the rollback record names, which a version put
 * back by hand carries. The deb and an unpacked folder carry their bundle where they are installed, and copy nothing.
 */

import { createHash } from 'node:crypto';
import { closeSync, lstatSync, mkdirSync, openSync, readdirSync, readFileSync, readSync, renameSync, rmSync, statSync, writeFileSync, chmodSync } from 'node:fs';
import { join } from 'node:path';
import type { LinuxPackage } from './linux-package.ts';

/** Whether this application copies its bundle out of the package: only an AppImage does. */
export function copiesBundle(form: LinuxPackage | null): boolean {
    return form?.form === 'appimage';
}

/** A copy under the version that no longer matches the package's bundle. */
export class BundleDiffers extends Error {}

/** What the starting page says when the copy under the version differs from the package's bundle. */
export function differsWords(copy: string): string {
    return `The jason this application carries is copied to ${copy}, and that copy no longer matches the one in this package, so Jason did not start with it. Something changed it after it was copied. Remove ${copy} and start Jason again: it copies the package’s jason there again.`;
}

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

function exists(path: string): boolean {
    try {
        lstatSync(path);
        return true;
    } catch {
        return false;
    }
}

export interface BundleCopy {
    /** The package's bundle, under the mount. */
    source: string;
    /** `<data>/desktop/bundle`. */
    bundles: string;
    /** The version the package's bundle was prepared as. */
    version: string;
    /** The versions whose copies stay: the one the rollback record names. */
    keep: readonly string[];
}

/** The carried bundle: the copy under the version, made when it is not there. Throws `BundleDiffers` for one that differs. */
export function carriedBundle(copy: BundleCopy): string {
    // A version is one name under the folder: never empty, a dot, a separator or a leading dot (a temporary folder's).
    if (!/^[0-9A-Za-z][0-9A-Za-z.+-]*$/.test(copy.version)) throw new Error('The package’s bundle names no version a folder can carry.');
    const target = join(copy.bundles, copy.version);
    const wanted = filesOf(copy.source, true);
    if (wanted === null) throw new Error('The package’s bundle holds something that is neither a file nor a folder.');
    if (exists(target)) {
        if (!same(filesOf(target, false), wanted)) throw new BundleDiffers(differsWords(target));
    } else {
        const temporary = join(copy.bundles, `.${copy.version}.tmp-${process.pid}`);
        rmSync(temporary, { recursive: true, force: true });
        try {
            mkdirSync(temporary, { recursive: true });
            for (const path of wanted.keys()) {
                const from = join(copy.source, path);
                const to = join(temporary, path);
                mkdirSync(join(to, '..'), { recursive: true });
                writeFileSync(to, readFileSync(from));
                chmodSync(to, statSync(from).mode & 0o777);
            }
            if (!same(filesOf(temporary, false), wanted)) throw new Error('The copy of the package’s bundle does not read back as the package’s.');
            renameSync(temporary, target);
        } finally {
            rmSync(temporary, { recursive: true, force: true });
        }
    }
    const kept = new Set([copy.version, ...copy.keep]);
    for (const name of readdirSync(copy.bundles)) if (!kept.has(name)) rmSync(join(copy.bundles, name), { recursive: true, force: true });
    return target;
}
