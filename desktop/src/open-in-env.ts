import { leaveRun, type Environment, type PersonEnvironment } from './mode.ts';

// Pure: the two things of Open in that main.ts wires from the run, kept here so a test reads them without Electron.

/**
 * The environment an editor or the file manager is started with: the person's own, not the run's. A demo run points
 * this process at the demo's data directory and takes the person's runtime settings away; an editor started with that
 * would keep it for its whole life, Live after a switch included, so it gets a copy with the person's put back (the run's
 * own environment is not touched, and the terminals, which `jason session open` starts, keep the run's). On Linux,
 * Electron rewrites `XDG_CURRENT_DESKTOP` for itself and keeps the original in `ORIGINAL_XDG_CURRENT_DESKTOP`; the
 * program started gets the original back, as it would from a launcher.
 */
export function editorEnvironment(env: Environment, person: PersonEnvironment, platform: NodeJS.Platform): Environment {
    const copy: Environment = { ...env };
    leaveRun(copy, person);
    if (platform === 'linux' && copy.ORIGINAL_XDG_CURRENT_DESKTOP !== undefined) copy.XDG_CURRENT_DESKTOP = copy.ORIGINAL_XDG_CURRENT_DESKTOP;
    return copy;
}

/** The harness root Open in names: the demo's own in a demo run, else the person's. */
export function openInHarnessRoot(mode: 'live' | 'demo', demoRoot: string, personsRoot: () => string): string {
    return mode === 'demo' ? demoRoot : personsRoot();
}
