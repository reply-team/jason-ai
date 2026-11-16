/**
 * The runtime an earlier version's demo left running. Up to 0.44 the demo was a second installation beside the person's,
 * with a runtime of its own on the demo's data directory (`demoDirectory`); a kill or a crash could leave that runtime
 * running, and it holds the executable this application carries. Live stops it wherever it stops the person's own
 * before that executable is replaced or removed: an update, the installer's and the uninstaller's starts, and Remove
 * What Jason Placed (`stopWithDemo`). The stop is the existing one (`stopOwnRuntime`): the runtime the demo's run file
 * names, stopped through `jason runtime stop` on the demo's directory, and only when it runs from the jason this
 * application carries. The person's runtime is never stopped here: its run file is never read.
 */

import { cliAt, type Cli } from './cli.ts';
import { demoDirectory } from './mode.ts';
import { ownRuntimeDeps, stopOwnRuntime, type OwnRuntimeDeps, type StopOutcome } from './own-runtime.ts';
import { desktopPaths } from './paths.ts';

export interface DemoRuntimeWhere {
    platform: NodeJS.Platform;
    /** The jason executable the application carries. */
    executable: string;
    /** The person's data directory; the demo's is its sibling (`demoDirectory`). */
    person: string;
    /** The environment a jason verb runs with; its `JASON_DATA_DIR` is replaced by the demo's directory. */
    env: Record<string, string>;
}

/** What the stop looks at: the demo's run file, and a jason on the demo's directory. */
export function demoRuntimeDeps(where: DemoRuntimeWhere, cliFor: (executable: string, env: Record<string, string>) => Cli = cliAt): OwnRuntimeDeps {
    const data = demoDirectory(where.person);
    return ownRuntimeDeps(where.platform, where.executable, desktopPaths(data).runFile, cliFor(where.executable, { ...where.env, JASON_DATA_DIR: data }));
}

/** Stops the demo's runtime, and nothing else. */
export function stopDemoRuntime(where: DemoRuntimeWhere, cliFor?: (executable: string, env: Record<string, string>) => Cli): Promise<StopOutcome> {
    return stopOwnRuntime(demoRuntimeDeps(where, cliFor));
}

/**
 * What a start of the person's installation that stops the person's runtime (an update, the installer's and the
 * uninstaller's starts) asks as well: a demo runtime left running by a kill or a crash holds the carried executable too.
 * The person's runtime is stopped first; a stop that failed or did not answer, the person's or the demo's, is the
 * answer, and otherwise it is the person's, or the demo's where the person's had no runtime of this application's.
 */
export async function stopWithDemo(person: () => Promise<StopOutcome>, demo: () => Promise<StopOutcome>): Promise<StopOutcome> {
    const refused = (outcome: StopOutcome): outcome is Extract<StopOutcome, { kind: 'failed' | 'unanswered' }> => outcome.kind === 'failed' || outcome.kind === 'unanswered';
    const own = await person();
    if (refused(own)) return own;
    const other = await demo().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
    if (refused(other)) return { ...other, demo: true };
    return own.kind === 'none' ? other : own;
}
