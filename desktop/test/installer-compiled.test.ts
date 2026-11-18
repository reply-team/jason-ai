// The include compiled with the pinned template's own check, and run. The line-by-line rows in installer-processes.test.ts
// read the include as text; these compile it with makensis, the version app-builder-lib 26.18.0 downloads for a pack
// (out/toolsets/windows.js, getLegacyNsisBin: "nsis-3.0.4.1"), together with the template's real CHECK_APP_RUNNING and
// IS_POWERSHELL_AVAILABLE (templates/nsis/include/allowOnlyOneInstallerInstance.nsh), nothing of either replaced, and run
// the result silently over a scratch folder. A replaced IS_POWERSHELL_AVAILABLE is what once hid that a setup started from
// a PowerShell 7 session took PowerShell for absent: these rows run with that session's module path inherited.
//
// The harness around the two is the least a script needs to compile them: the application's executable name, the three
// sentences the include names from the template's messages, whether the run is an update (a run given --updated), and the
// installation folder, which a setup takes from /D=. Two forms, by the order each pinned entry point runs our hooks in:
// the installer's .onInit runs preInit first (templates/nsis/installer.nsi), and its install section the check
// (installSection.nsh); the uninstaller's un.onInit runs the check before any hook of ours (uninstaller.nsh, un.onInit).
//
// They run on Windows where electron-builder's NSIS is on this machine - in its cache once a pack has run, or where
// ELECTRON_BUILDER_NSIS_DIR names it - and where PowerShell 7 is, whose module path they inherit.

import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const include = join(desktop, 'installer', 'uninstall.nsh');
const templateIncludes = join(desktop, 'node_modules', 'app-builder-lib', 'templates', 'nsis', 'include');

/** The NSIS bundle a pack of this desktop compiles with, where this machine has it, as electron-builder finds it. */
function nsisBundle(): string | null {
    const named = process.env.ELECTRON_BUILDER_NSIS_DIR;
    if (named && existsSync(join(named, 'Bin', 'makensis.exe'))) return named;
    const cache = process.env.ELECTRON_BUILDER_CACHE ?? (process.env.LOCALAPPDATA ? join(process.env.LOCALAPPDATA, 'electron-builder', 'Cache') : null);
    const versions = cache === null ? null : join(cache, 'nsis-3.0.4.1');
    if (versions === null || !existsSync(versions)) return null;
    for (const name of readdirSync(versions)) {
        const bundle = join(versions, name);
        if (statSync(bundle).isDirectory() && existsSync(join(bundle, 'Bin', 'makensis.exe'))) return bundle;
    }
    return null;
}

/** The module path a PowerShell 7 session hands every program it starts, read from PowerShell 7 itself. */
function pwshModulePath(): string | null {
    try {
        return execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', '[Console]::Out.Write($env:PSModulePath)'], { encoding: 'utf8', windowsHide: true });
    } catch {
        return null;
    }
}

const powershell = join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');

/**
 * Whether the template's own test of PowerShell's policy, run as it ships, fails for Windows PowerShell started with this
 * module path: what makes the inherited path a hazard on this machine at all.
 */
function policyTestFails(modulePath: string): boolean {
    try {
        execFileSync(powershell, ['-NoProfile', '-NonInteractive', '-C', "if ((Get-ExecutionPolicy -Scope Process) -eq 'Restricted') { exit 1 } else { exit 0 }"], {
            env: { ...process.env, PSModulePath: modulePath },
            stdio: 'ignore',
            windowsHide: true,
        });
        return false;
    } catch {
        return true;
    }
}

const bundle = process.platform === 'win32' ? nsisBundle() : null;
const inherited = bundle === null ? null : pwshModulePath();
const skip = process.platform !== 'win32'
    ? 'a setup is Windows’ own'
    : bundle === null
        ? 'electron-builder’s NSIS 3.0.4.1 is not on this machine: a pack (npm run package) puts it in its cache, or ELECTRON_BUILDER_NSIS_DIR names it'
        : inherited === null
            ? 'PowerShell 7 is not on this machine, so there is no module path of its to inherit'
            : !policyTestFails(inherited)
                ? 'Windows PowerShell started with PowerShell 7’s module path loads its own modules on this machine, so the path is no hazard here'
                : false;

type Form = 'installer' | 'uninstaller';

/** The harness, as above: the include, then the template's check, run in the order the form's entry point runs them. */
function harness(form: Form, output: string): string {
    return [
        'Unicode true',
        'RequestExecutionLevel user',
        `OutFile "${output}"`,
        'Name "Jason"',
        '!include LogicLib.nsh',
        '!include FileFunc.nsh',
        `!addincludedir "${templateIncludes}"`,
        '!define APP_EXECUTABLE_FILENAME "Jason.exe"',
        'Var HarnessUpdated',
        '!define isUpdated `"$HarnessUpdated" == "1"`',
        'LangString appRunning 1033 "Jason is running. Click OK to close it."',
        'LangString appClosing 1033 "Closing running Jason..."',
        'LangString uninstallFailed 1033 "Failed to uninstall old application files."',
        `!include "${include}"`,
        '!include "allowOnlyOneInstallerInstance.nsh"',
        'Function .onInit',
        '  StrCpy $HarnessUpdated "0"',
        '  ${GetParameters} $0',
        '  ClearErrors',
        '  ${GetOptions} $0 "--updated" $1',
        '  ${IfNot} ${Errors}',
        '    StrCpy $HarnessUpdated "1"',
        '  ${EndIf}',
        ...(form === 'installer' ? ['  !ifmacrodef preInit', '    !insertmacro preInit', '  !endif'] : ['  !insertmacro CHECK_APP_RUNNING']),
        'FunctionEnd',
        'Section',
        ...(form === 'installer' ? ['  !insertmacro CHECK_APP_RUNNING'] : []),
        'SectionEnd',
        '',
    ].join('\r\n');
}

/** Runs a compiled harness silently over `folder` with this environment, and says its exit code. */
function run(program: string, folder: string, env: NodeJS.ProcessEnv, extra: string[] = []): Promise<number | null> {
    return new Promise((resolve, reject) => {
        // /D= is the last argument and never quoted, as NSIS reads it.
        const child = spawn(program, ['/S', ...extra, `/D=${folder}`], { env, stdio: 'ignore', windowsHide: true, windowsVerbatimArguments: true });
        const bound = setTimeout(() => {
            child.kill();
            reject(new Error(`${program} did not end within two minutes`));
        }, 120_000);
        child.on('error', reject);
        child.on('exit', (code) => {
            clearTimeout(bound);
            resolve(code);
        });
    });
}

/** This environment, with the module path given or none, as a PowerShell 7 session or another caller would hand it on. */
function environment(modulePath: string | null, more: Record<string, string> = {}): NodeJS.ProcessEnv {
    const env: NodeJS.ProcessEnv = { ...process.env, ...more };
    for (const name of Object.keys(env)) if (name.toUpperCase() === 'PSMODULEPATH') delete env[name];
    if (modulePath !== null) env.PSModulePath = modulePath;
    return env;
}

describe('the include compiled with the template’s own check, run silently with PowerShell 7’s module path inherited', { skip }, () => {
    let scratch = '';
    const programs = new Map<Form, string>();
    const running: ChildProcess[] = [];

    before(() => {
        scratch = mkdtempSync(join(tmpdir(), 'jason-installer-compiled-'));
        for (const form of ['installer', 'uninstaller'] as const) {
            const source = join(scratch, `${form}.nsi`);
            const program = join(scratch, `${form}.exe`);
            writeFileSync(source, harness(form, program), 'utf8');
            execFileSync(join(bundle!, 'Bin', 'makensis.exe'), ['-WX', '-V1', '-INPUTCHARSET', 'UTF8', source], {
                env: { ...process.env, NSISDIR: bundle! },
                stdio: 'pipe',
                windowsHide: true,
            });
            programs.set(form, program);
        }
    });

    after(async () => {
        // Each stand-in is ended and waited for, so the folder that holds its image can go.
        await Promise.all(running.filter((child) => child.exitCode === null && child.signalCode === null).map((child) => new Promise((resolve) => {
            child.once('exit', resolve);
            child.kill();
        })));
        rmSync(scratch, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    });

    /** A folder named for the row, holding a running stand-in for the application where the row says. */
    async function folder(name: string, applicationRuns: boolean): Promise<{ path: string; application: ChildProcess | null }> {
        const path = join(scratch, name);
        mkdirSync(path, { recursive: true });
        if (!applicationRuns) return { path, application: null };
        const image = join(path, 'Jason.exe');
        copyFileSync(process.execPath, image);
        const application = spawn(image, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore', windowsHide: true });
        running.push(application);
        await new Promise((resolve) => setTimeout(resolve, 700));
        assert.equal(application.exitCode, null, 'the stand-in for the application did not start');
        return { path, application };
    }

    for (const form of ['installer', 'uninstaller'] as const) {
        test(`the ${form}, nothing running: it goes on, 0`, async () => {
            const { path } = await folder(`${form}-clean`, false);
            assert.equal(await run(programs.get(form)!, path, environment(inherited)), 0);
        });

        test(`the ${form}, the application running: it ends with 3 and leaves the application running`, async () => {
            const { path, application } = await folder(`${form}-running`, true);
            assert.equal(await run(programs.get(form)!, path, environment(inherited)), 3);
            assert.equal(application!.exitCode, null, 'the silent run ended the application');
        });

        test(`the ${form}, an update over the application that has quit: it goes on, 0`, async () => {
            const { path } = await folder(`${form}-update`, false);
            assert.equal(await run(programs.get(form)!, path, environment(inherited), ['--updated']), 0);
        });

        test(`the ${form}, an empty module path inherited: it goes on, 0`, async () => {
            const { path } = await folder(`${form}-empty`, false);
            assert.equal(await run(programs.get(form)!, path, environment('')), 0);
        });

        // PowerShell that is there and refuses to run anything is the template's own case of PowerShell not available: its
        // policy for this process, which Windows PowerShell reads from PSExecutionPolicyPreference, is Restricted.
        test(`the ${form}, PowerShell truly unavailable: a silent run still ends with 3, nothing running`, async () => {
            const { path } = await folder(`${form}-no-powershell`, false);
            assert.equal(await run(programs.get(form)!, path, environment(inherited, { PSExecutionPolicyPreference: 'Restricted' })), 3);
        });
    }
});
