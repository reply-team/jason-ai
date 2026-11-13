import { spawn } from 'node:child_process';
import { withoutSessionMarkers } from './session-markers.ts';
import type { Line } from './open-in.ts';

/**
 * Starts the program an "Open in" act names, an editor or the file manager, and leaves it running on its own: a file and
 * its arguments, no shell, no console, the standard streams closed, and the desktop's own environment less a parent
 * Claude Code session's markers, so an editor's terminal finds the installation this run is on (a demo run's included).
 * Answers null once the system has started it, or the sentence it could not be started with.
 */
export function startDetached(line: Line, env: Readonly<Record<string, string | undefined>>): Promise<string | null> {
    return new Promise((resolve) => {
        let child: ReturnType<typeof spawn>;
        try {
            child = spawn(line.file, line.args, { env: withoutSessionMarkers(env), detached: true, stdio: 'ignore' });
        } catch {
            resolve('That program could not be started.');
            return;
        }
        child.once('error', () => resolve('That program could not be started.'));
        child.once('spawn', () => {
            child.unref();
            resolve(null);
        });
    });
}
