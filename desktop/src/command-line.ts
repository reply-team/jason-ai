/** Pure: what putting `jason` on the PATH would do, so a Jason already installed is never shadowed. */

export interface CommandPlan {
    name: 'jason' | 'jason-desktop';
    /** Set when `jason` is already another program, the reason the plan uses the other name. */
    shadows?: string | undefined;
}

/** `existingJason` is what the PATH finds for `jason`; `wrapperPath` is where ours is, or null when none is placed. */
export function planCommand(existingJason: string | null, wrapperPath: string | null): CommandPlan {
    if (existingJason === null || existingJason === wrapperPath) return { name: 'jason' };
    return { name: 'jason-desktop', shadows: existingJason };
}

/** The marker line of the macOS wrapper, by which a wrapper of ours is told from any other file. */
export const wrapperMarker = '# Written by the Jason desktop application.';

const shellQuote = (value: string) => `'${value.replaceAll("'", `'\\''`)}'`;

/**
 * The wrapper on macOS: the executable the application carries, with the arguments it was given and nothing else. It
 * names no harness root, and a data directory only when the desktop itself runs with one (`JASON_DATA_DIR`), because
 * the desktop and a terminal use the same installation by the same rule: `JASON_DATA_DIR` when the person set it,
 * otherwise `~/.jason`.
 */
export function wrapperScript(executable: string, dataDirectory: string | null = null): string {
    const environment = dataDirectory === null ? '' : `JASON_DATA_DIR=${shellQuote(dataDirectory)} `;
    return ['#!/bin/sh', wrapperMarker, `${environment}exec ${shellQuote(executable)} "$@"`, ''].join('\n');
}

/** The executable (and data directory) a wrapper of ours runs, or null for a file that is not one. */
export function wrapperTarget(text: string): { executable: string; dataDirectory: string | null } | null {
    const quoted = String.raw`'((?:[^']|'\\'')*)'`;
    const shape = new RegExp(String.raw`^#!/bin/sh\n${wrapperMarker.replace(/[.]/g, '\\.')}\n(?:JASON_DATA_DIR=${quoted} )?exec ${quoted} "\$@"\n$`);
    const match = shape.exec(text);
    if (match === null || match[2] === undefined) return null;
    const unquote = (value: string) => value.replaceAll(`'\\''`, "'");
    return { executable: unquote(match[2]), dataDirectory: match[1] === undefined ? null : unquote(match[1]) };
}

function batchSafe(value: string, what: string): string {
    if (value.includes('"') || value.includes('%') || /[\r\n]/.test(value)) throw new Error(`The ${what} holds a character a batch file would read.`);
    return value;
}

/**
 * The Git Bash wrapper on Windows, placed beside the shim. Git Bash, the shell Claude Code's Bash tool runs on Windows,
 * finds `jason` and `jason.exe` but never `jason.cmd`, so with the shim alone `jason` is "command not found" there. It
 * is the macOS wrapper's text with the executable's path in forward slashes, and `"$@"` hands every argument over whole.
 */
export function gitBashWrapper(executable: string, dataDirectory: string | null = null): string {
    return wrapperScript(executable.replaceAll('\\', '/'), dataDirectory);
}

/**
 * The shim on Windows, a batch file that hands its arguments to the executable the application carries, so its exit
 * code is the CLI's. When the desktop runs with a data directory of its own the shim names it, inside `setlocal`, so
 * the terminal it is typed in keeps its own environment.
 *
 * `%*` hands the caller's arguments to cmd.exe's parser a second time, so a quoted argument beside `&`, `|`, `<`, `>`
 * or `^` (JSON passed from PowerShell 7.3 or later, which hands a `.cmd` its arguments in its legacy way) can be split,
 * or run as a command of its own. The shim is for a person typing; a script or an agent passes such arguments to the
 * executable's full path, or runs `jason` from Git Bash, where the wrapper beside the shim passes them whole.
 */
export function windowsShim(executable: string, dataDirectory: string | null = null): string {
    batchSafe(executable, 'executable path');
    const environment = dataDirectory === null ? [] : ['setlocal', `set "JASON_DATA_DIR=${batchSafe(dataDirectory, 'data directory')}"`];
    return ['@echo off', ...environment, `"${executable}" %*`, ''].join('\r\n');
}

/** The executable (and data directory) a shim of ours runs, or null for a file that is not one. */
export function shimTarget(text: string): { executable: string; dataDirectory: string | null } | null {
    const match = /^@echo off\r\n(?:setlocal\r\nset "JASON_DATA_DIR=([^"%\r\n]*)"\r\n)?"([^"%\r\n]+)" %\*\r\n$/.exec(text);
    if (match === null || match[2] === undefined) return null;
    return { executable: match[2], dataDirectory: match[1] ?? null };
}
