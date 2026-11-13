/**
 * Starting the demo over, the part that must not run inside the demo: the running desktop lives in the demo's data
 * directory (its log, its Electron profile, its chat folders), so it cannot remove that folder, and a log line written
 * after the removal would make it again without its marker. The run that is asked leaves a request in the PERSON's
 * `desktop/` folder (`requestStartOver`) and starts again; the next start, before anything names the demo's directory,
 * removes the two folders (`carryOutStartOver`) and deletes the request. A removal that fails keeps the request and throws,
 * which makes that start the person's own (`startOn`), so the next demo start tries again.
 */

import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { DemoPlaces } from './demo-seed.ts';

/** The request file's one line. */
export const startOverRequestText = 'The demo is to be made again at its next start.\n';

/** Leaves the request at `file`; throws when it cannot. */
export function requestStartOver(file: string): void {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, startOverRequestText);
}

export interface CarryOutDeps {
    requested(): boolean;
    places: DemoPlaces;
    exists(folder: string): boolean;
    marked(folder: string): boolean;
    removeTree(folder: string): void;
    dropRequest(): void;
}

/**
 * Does nothing when no request is there. Otherwise removes each of the demo's two folders that is there and carries the
 * marker, then drops the request. A folder there without the marker is left and named, and nothing is removed (the request
 * stays); a removal that throws is named and the request stays. Never removes anything but the two folders.
 */
export function carryOutStartOver(deps: CarryOutDeps): void {
    if (!deps.requested()) return;
    const folders = [deps.places.data, deps.places.harnessRoot];
    const foreign = folders.find((folder) => deps.exists(folder) && !deps.marked(folder));
    if (foreign !== undefined) throw new Error(`The folder ${foreign} is not one the demo made, so it was left.`);
    for (const folder of folders) {
        if (!deps.exists(folder)) continue;
        try {
            deps.removeTree(folder);
        } catch {
            throw new Error(`The folder ${folder} could not be removed.`);
        }
    }
    deps.dropRequest();
}

/** The disk side of the two above, for the person's request file. */
export const requestOnDisk = (file: string) => ({
    requested: (): boolean => existsSync(file),
    dropRequest: (): void => rmSync(file, { force: true }),
});
