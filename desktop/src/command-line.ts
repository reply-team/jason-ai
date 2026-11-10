/** Pure: what putting `jason` on the PATH would do, so a Jason already installed is never shadowed. */

export interface CommandPlan {
    name: 'jason' | 'jason-desktop';
    /** Set when `jason` is already another program, the reason the plan uses the other name. */
    shadows?: string | undefined;
}

export function planCommand(existingJason: string | null, wrapperPath: string): CommandPlan {
    if (existingJason === null || existingJason === wrapperPath) return { name: 'jason' };
    return { name: 'jason-desktop', shadows: existingJason };
}

/**
 * The wrapper on macOS: the executable the application carries, with the arguments it was given and nothing else. It
 * names no data directory and no harness root, because the desktop and a terminal use the same installation by the
 * same rule: `JASON_DATA_DIR` when the person set it, otherwise `~/.jason`.
 */
export function wrapperScript(executable: string): string {
    const quote = (value: string) => `'${value.replaceAll("'", `'\\''`)}'`;
    return ['#!/bin/sh', '# Written by the Jason desktop application.', `exec ${quote(executable)} "$@"`, ''].join('\n');
}

/** The shim on Windows, a batch file of one line that hands its arguments to the executable the application carries. */
export function windowsShim(executable: string): string {
    if (executable.includes('"') || executable.includes('%')) throw new Error('The executable path holds a character a batch file would read.');
    return ['@echo off', `"${executable}" %*`, ''].join('\r\n');
}
