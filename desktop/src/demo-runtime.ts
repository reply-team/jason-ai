/**
 * The demo's runtime. A demo run starts it as any run starts its installation's (`jason runtime start`); it is stopped
 * when the demo run quits, the switch back to Live included, and before the demo is started over. The stop is the
 * existing one (`stopOwnRuntime`): the runtime the demo's run file names, stopped through `jason runtime stop` on the
 * demo's directory, and only when it runs from the jason this application carries. The person's runtime is never
 * stopped here: its run file is never read.
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

/** Stops the demo's runtime, and nothing else: what quitting a demo run and "Start the demo over" call. */
export function stopDemoRuntime(where: DemoRuntimeWhere, cliFor?: (executable: string, env: Record<string, string>) => Cli): Promise<StopOutcome> {
    return stopOwnRuntime(demoRuntimeDeps(where, cliFor));
}

/** How long a demo run's quit waits for the demo's runtime to stop before it goes on all the same. */
export const demoStopCapMs = 15_000;

export interface DemoQuitDeps {
    stop(): Promise<unknown>;
    quit(): void;
    /** Called once when the stop has not answered within the cap and the quit goes on without it. */
    timedOut(): void;
    /** The cap; `demoStopCapMs` unless a test says otherwise. */
    capMs?: number;
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

/**
 * A demo run's quit: the first is held (`beforeQuit` answers true, and Electron's `before-quit` is prevented) while the
 * demo's runtime is stopped; once the stop has answered, whatever it answered, or the cap has passed without an answer,
 * the quit is asked again and goes on.
 */
export class DemoQuit {
    private state: 'running' | 'stopping' | 'stopped' = 'running';
    private readonly deps: DemoQuitDeps;

    constructor(deps: DemoQuitDeps) {
        this.deps = deps;
    }

    /** Called from `before-quit`: whether this quit is to be held. */
    beforeQuit(): boolean {
        if (this.state === 'stopped') return false;
        if (this.state === 'stopping') return true;
        this.state = 'stopping';
        let timer: ReturnType<typeof setTimeout> | undefined;
        const capped = new Promise<'timed_out'>((resolve) => {
            timer = setTimeout(() => resolve('timed_out'), this.deps.capMs ?? demoStopCapMs);
        });
        const answered = this.deps.stop().then(
            () => 'answered' as const,
            () => 'answered' as const,
        );
        void Promise.race([answered, capped]).then((how) => {
            clearTimeout(timer);
            if (how === 'timed_out') this.deps.timedOut();
            this.state = 'stopped';
            this.deps.quit();
        });
        return true;
    }
}

/** The part of Electron's app a demo run's quit is held on: its `before-quit` and its quit. */
export interface QuittingApp {
    on(event: 'before-quit', listener: (event: { preventDefault(): void }) => void): unknown;
    quit(): void;
}

/**
 * Holds a demo run's quit for the demo's runtime's stop (`DemoQuit`); a Live run's quit is held for nothing and stops
 * nothing. Answers the hold, or null in Live.
 */
export function holdDemoQuit(app: QuittingApp, mode: 'live' | 'demo', deps: Omit<DemoQuitDeps, 'quit'>): DemoQuit | null {
    if (mode !== 'demo') return null;
    const hold = new DemoQuit({ ...deps, quit: () => app.quit() });
    app.on('before-quit', (event) => {
        if (hold.beforeQuit()) event.preventDefault();
    });
    return hold;
}

export interface LeftRuntimeDeps {
    /** Stops the demo's runtime (`stopDemoRuntime`): the demo's run file, the demo's directory, never the person's. */
    stop(): Promise<StopOutcome>;
    /** Starts the desktop again on the demo, which then carries the request out. */
    startAgain(): void;
    /** What a refused carry-out does: the record set to Live, the person told, their own installation started. */
    refuse(): void;
}

/**
 * The start that found a demo runtime a crashed making or placing left (`leftRuntimeStops`): it opens no window, stops
 * that runtime and starts again, so the next start carries the request out; a stop that fails, or finds a runtime that
 * is not this application's, refuses as a carry-out refuses. Answers which it did.
 */
export async function stopLeftDemoRuntime(deps: LeftRuntimeDeps): Promise<'started-again' | 'refused'> {
    const outcome = await deps.stop().catch((): StopOutcome => ({ kind: 'failed', exit: -1 }));
    if (outcome.kind === 'stopped' || outcome.kind === 'none') {
        deps.startAgain();
        return 'started-again';
    }
    deps.refuse();
    return 'refused';
}
