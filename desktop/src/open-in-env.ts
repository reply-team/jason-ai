import { leaveRun, type Environment, type PersonEnvironment } from './mode.ts';

// Pure: what Open in's editors and file manager are started with, which main.ts wires from the run, kept here so a test
// reads it without Electron.

/**
 * The environment an editor or the file manager is started with: the person's own, as it held it when the desktop
 * started. An editor keeps its environment for its whole life, so it gets a copy with the person's `JASON_DATA_DIR` and
 * runtime settings put back (the run's own environment is not touched, and the terminals, which `jason session open`
 * starts, keep the run's). On Linux, Electron rewrites `XDG_CURRENT_DESKTOP` for itself and keeps the original in
 * `ORIGINAL_XDG_CURRENT_DESKTOP`; the program started gets the original back, as it would from a launcher.
 */
export function editorEnvironment(env: Environment, person: PersonEnvironment, platform: NodeJS.Platform): Environment {
    const copy: Environment = { ...env };
    leaveRun(copy, person);
    if (platform === 'linux' && copy.ORIGINAL_XDG_CURRENT_DESKTOP !== undefined) copy.XDG_CURRENT_DESKTOP = copy.ORIGINAL_XDG_CURRENT_DESKTOP;
    return copy;
}
