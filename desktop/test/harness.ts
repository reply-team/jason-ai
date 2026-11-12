// What the tests that run the desktop's code against stand-ins share: a temporary data directory, a stub jason, and
// stub programs on a PATH of their own, each recording what it was asked outside the data directory.

import { copyFileSync, linkSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cliAt, type Cli } from '../src/cli.ts';
import type { Logger } from '../src/log.ts';
import { desktopPaths, type DesktopPaths } from '../src/paths.ts';
import { programsOn, type Programs } from '../src/programs.ts';
import type { SecretBox } from '../src/providers.ts';

const stubs = join(dirname(fileURLToPath(import.meta.url)), 'stubs');

export interface Recorded {
    name: string;
    args: string[];
    stdin: string;
    cwd: string;
    env: Record<string, string>;
}

export interface Bench {
    root: string;
    paths: DesktopPaths;
    env: Record<string, string>;
    cli: Cli;
    programs: Programs;
    record(): Recorded[];
}

/** Puts a stub program on the PATH folder `bin` the way its platform installs one: an npm batch file, or a script. */
function placeProgram(bin: string, name: string): void {
    if (process.platform === 'win32') {
        const folder = join(bin, 'node_modules', `${name}-stub`);
        mkdirSync(folder, { recursive: true });
        writeFileSync(join(folder, 'index.cjs'), `require(${JSON.stringify(join(stubs, 'program.cjs'))});\n`);
        // The shape npm writes for an installed package's command.
        writeFileSync(
            join(bin, `${name}.cmd`),
            ['@ECHO off', 'GOTO start', ':find_dp0', 'SET dp0=%~dp0', 'EXIT /b', ':start', 'SETLOCAL', 'CALL :find_dp0', '',
                'IF EXIST "%dp0%\\node.exe" (', '  SET "_prog=%dp0%\\node.exe"', ') ELSE (', '  SET "_prog=node"', '  SET PATHEXT=%PATHEXT:;.JS;=;%', ')', '',
                `endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "%_prog%"  "%dp0%\\node_modules\\${name}-stub\\index.cjs" %*`, ''].join('\r\n'),
        );
    } else {
        const file = join(bin, name);
        writeFileSync(file, `#!/usr/bin/env node\nrequire(${JSON.stringify(join(stubs, 'program.cjs'))});\n`, { mode: 0o755 });
    }
}

/** A folder holding Node alone, so a PATH can name Node without naming the npm installed beside it. */
function nodeOnly(folder: string): void {
    mkdirSync(folder, { recursive: true });
    const target = join(folder, process.platform === 'win32' ? 'node.exe' : 'node');
    try {
        if (process.platform === 'win32') linkSync(process.execPath, target);
        else symlinkSync(process.execPath, target);
    } catch {
        copyFileSync(process.execPath, target);
    }
}

/**
 * Ends, once the row is over, the processes it left running, by the ids `ids` gives then, and waits until they have gone.
 * A row registers it before its bench: a row's after hooks run in the order they were registered, and on Windows a
 * running stand-in holds the bench's copy of Node, which the bench's removal of its folder would otherwise meet.
 */
export function endWhenOver(t: { after(fn: () => Promise<void>): void }, ids: () => readonly number[]): void {
    const alive = (id: number) => {
        try {
            process.kill(id, 0);
            return true;
        } catch {
            return false;
        }
    };
    t.after(async () => {
        const left = ids().filter(alive);
        for (const id of left) process.kill(id);
        const started = Date.now();
        while (left.some(alive) && Date.now() - started < 10_000) await new Promise((resolve) => setTimeout(resolve, 100));
    });
}

export function bench(t: { after(fn: () => void): void }, options: { programs?: string[]; env?: Record<string, string>; bundleVersion?: string } = {}): Bench {
    const root = mkdtempSync(join(tmpdir(), 'jason-desktop-test-'));
    // On Windows a program that has just ended can hold its image for a moment longer, and a folder holding it refuses
    // removal with EPERM until it lets go; the removal is retried, as Node retries EPERM and EBUSY.
    t.after(() => rmSync(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }));
    const data = join(root, 'data');
    const bin = join(root, 'bin');
    const node = join(root, 'node');
    mkdirSync(data, { recursive: true });
    mkdirSync(bin, { recursive: true });
    nodeOnly(node);
    for (const name of options.programs ?? []) placeProgram(bin, name);
    const recordFile = join(root, 'record.jsonl');

    const env: Record<string, string> = {
        PATH: [bin, node].join(delimiter),
        JASON_DATA_DIR: data,
        JASON_Harness__Root: join(root, 'harness'),
        STUB_RECORD: recordFile,
        ...(process.platform === 'win32' ? { SystemRoot: process.env.SystemRoot ?? 'C:\\Windows', PATHEXT: '.COM;.EXE;.BAT;.CMD' } : {}),
        ...options.env,
    };
    return {
        root,
        paths: desktopPaths(data),
        env,
        cli: cliAt(process.execPath, env, [join(stubs, 'jason.cjs')]),
        programs: programsOn(env, options.bundleVersion ?? null),
        record() {
            try {
                return readFileSync(recordFile, 'utf8').split('\n').filter(Boolean).map((line) => JSON.parse(line) as Recorded);
            } catch {
                return [];
            }
        },
    };
}

/** Every file under a folder with a digest of its content, by its path relative to the folder with forward slashes. */
export function snapshot(folder: string): Map<string, string> {
    const files = new Map<string, string>();
    const walk = (here: string) => {
        let names: string[];
        try {
            names = readdirSync(here);
        } catch {
            return;
        }
        for (const name of names) {
            const path = join(here, name);
            if (statSync(path).isDirectory()) walk(path);
            else files.set(relative(folder, path).replaceAll('\\', '/'), createHash('sha256').update(readFileSync(path)).digest('hex'));
        }
    };
    walk(folder);
    return files;
}

/** The files a stretch of work created or changed. */
export function written(before: Map<string, string>, after: Map<string, string>): string[] {
    return [...after].filter(([path, digest]) => before.get(path) !== digest).map(([path]) => path).sort();
}

/** A logger that keeps what it was told, for asserting the log in memory. */
export function memoryLog(): Logger & { lines: { level: string; name: string; fields: Record<string, unknown> }[] } {
    const lines: { level: string; name: string; fields: Record<string, unknown> }[] = [];
    return {
        lines,
        info: (name, fields) => void lines.push({ level: 'info', name, fields }),
        warn: (name, fields) => void lines.push({ level: 'warn', name, fields }),
        error: (name, fields) => void lines.push({ level: 'error', name, fields }),
    };
}

/** A stand-in for the operating system's encryption: reversible, and plainly not the key's own text. */
export const fakeBox: SecretBox = {
    isEncryptionAvailable: () => true,
    encryptString: (text) => Buffer.from([...Buffer.from(text, 'utf8')].map((byte) => byte ^ 0x5a)),
    decryptString: (encrypted) => Buffer.from([...encrypted].map((byte) => byte ^ 0x5a)).toString('utf8'),
    // A keyring, so the rows read alike on a Linux runner.
    getSelectedStorageBackend: () => 'gnome_libsecret',
};

/**
 * Writes a runtime's run file, naming no address, into the bench's data directory: what the Windows check of Codex's
 * sandbox reads before a chat's first Codex turn (`codex-sandbox.ts`), and finds nothing to read without.
 */
export function placeRunFile(b: Bench): void {
    mkdirSync(dirname(b.paths.runFile), { recursive: true });
    writeFileSync(b.paths.runFile, '{}');
}
