// Which running programs the installer and the uninstaller close, and which they only look for. The application is
// Jason.exe and the runtime it carries is jason.exe, and Windows does not tell the two names apart, so a check that goes
// by the image name closes every jason.exe on the machine, a runtime of another installation with its work included.
// electron-builder's own check does that where PowerShell is not available, and where it is, closes every program whose
// path merely begins with the installation folder, the runtime this application carries among them. These rows hold the
// check that replaces it to the one file it may close, the application's own executable by its exact path, and to the
// one it looks for and never closes, the carried jason executable by its exact path, of whichever data directory, passing
// over only the one process a carried `jason update apply` records while it waits for the installer; neither the installer
// nor the uninstaller is run here.

import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { copyFileSync, linkSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, relative } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { stopRuntimeFlag } from '../src/uninstall.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const script = readFileSync(join(desktop, 'installer', 'uninstall.nsh'), 'utf8');
const templates = join(desktop, 'node_modules', 'app-builder-lib', 'templates', 'nsis');
const template = (file: string) => readFileSync(join(templates, file), 'utf8');

/** The body of a macro a text defines, from `!macro <name>` to its `!macroend`, with where it starts and ends. */
function macroIn(text: string, name: string, where: string): { body: string; start: number; end: number } {
    const found = new RegExp(`^!macro ${name.replace(/[.*+?^${}()|[\]\\:]/g, '\\$&')}\\b.*$`, 'm').exec(text);
    assert.ok(found !== null, `${where} defines no ${name}.`);
    const end = text.indexOf('!macroend', found.index);
    assert.ok(end > found.index, `${name} has no !macroend in ${where}.`);
    return { body: text.slice(found.index, end), start: found.index, end: end + '!macroend'.length };
}

/** The body of a macro the script defines. */
function macro(name: string): string {
    return macroIn(script, name, 'installer/uninstall.nsh').body;
}

/** A PowerShell filter the script defines, as PowerShell reads it once nsis has turned each `$$` into `$`. */
function filter(name: 'JASON_OWN_APP' | 'JASON_OWN_RUNTIME'): string {
    const found = new RegExp(`^!define ${name} \`([^\`]+)\`$`, 'm').exec(script);
    assert.ok(found !== null, `installer/uninstall.nsh defines no ${name} filter.`);
    return found[1]!.replaceAll('$$', '$');
}

/** Every nsis file under a folder, by its path inside it. */
function nsisFiles(folder: string): string[] {
    return readdirSync(folder, { recursive: true, encoding: 'utf8' })
        .filter((file) => /\.(nsh|nsi)$/i.test(file))
        .map((file) => relative(folder, join(folder, file)))
        .sort();
}

/**
 * What closes a program in nsis: electron-builder's macro, the command-line tools, PowerShell's and CIM's, the plugin's,
 * and a window message that asks a program to close or quit, by name or by number.
 */
const closing = /KILL_PROCESS|taskkill|Stop-Process|KillProcess|CloseProcess|Terminate|Invoke-CimMethod|WM_CLOSE|WM_QUIT|WM_DESTROY|SC_CLOSE|0xF060|SendMessage\s+\S+\s+(?:0x0*1[02]|1[68])\b/i;

/** A filter as the script writes it, before nsis turns each `$$` into `$`. */
function filterText(name: 'JASON_OWN_APP' | 'JASON_OWN_RUNTIME'): string {
    const found = new RegExp(`^!define ${name} \`([^\`]+)\`$`, 'm').exec(script);
    assert.ok(found !== null, `installer/uninstall.nsh defines no ${name} filter.`);
    return found[1]!;
}

/** A command the script defines between backticks, as the script writes it. */
function define_(name: string): string {
    const found = new RegExp(`^!define ${name} \`([^\`]+)\`$`, 'm').exec(script);
    assert.ok(found !== null, `installer/uninstall.nsh defines no ${name}.`);
    return found[1]!;
}

/** A sentence the script defines once and shows where it is named. */
function define(name: string): string {
    const found = new RegExp(`^!define ${name} "([^"]*)"$`, 'm').exec(script);
    assert.ok(found !== null, `installer/uninstall.nsh defines no ${name}.`);
    return found[1]!;
}

/** The lines of a text that nsis runs, without blank lines and comments. */
function codeLines(text: string): string[] {
    return text.split(/\r?\n/).map((line) => line.trim()).filter((line) => line !== '' && !line.startsWith(';'));
}

/** The look whether a program runs from the carried executable, by its filter, failing closed. */
const runtimeLook = '"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_RUNTIME_FOUND}"';

/**
 * The PowerShell the installer runs for a runtime look, as PowerShell reads it once nsis has expanded the filter into it
 * and turned each `$$` into `$`: the command that ships, with `filter` standing in for the runtime filter where a row says.
 */
function runtimeFound(filterText: string = filter('JASON_OWN_RUNTIME')): string {
    const found = /^!define JASON_RUNTIME_FOUND `([^`]+)`$/m.exec(script);
    assert.ok(found !== null, 'installer/uninstall.nsh defines no JASON_RUNTIME_FOUND look.');
    const [before, after, ...more] = found[1]!.split('${JASON_OWN_RUNTIME}');
    assert.equal(more.length, 0, 'the look names the filter more than once');
    return `${before!.replaceAll('$$', '$')}${filterText}${after!.replaceAll('$$', '$')}`;
}
/** The look whether the application runs. */
const appLook = '"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_APP}).Count -gt 0) { exit 0 } else { exit 1 }"';
/** The one stop of the whole include: the application's, by its filter. */
const appStop = '"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_OWN_APP} | ForEach-Object { Stop-Process -Id $$_.ProcessId -Force }"';
/** The application's own start that stops the runtime it carries, through jason runtime stop. */
const stopRuntimeLine = `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --stop-runtime'`;

test('the pinned template closes running programs only in its own check, which the custom check replaces', () => {
    const files = nsisFiles(templates);
    assert.ok(files.length >= 15 && files.includes('installer.nsi') && files.includes(join('include', 'allowOnlyOneInstallerInstance.nsh')), `The templates read: ${files.join(', ')}`);
    const include = join('include', 'allowOnlyOneInstallerInstance.nsh');
    const plugin = join('include', 'nsProcess.nsh');
    let inserted = 0;
    for (const file of files) {
        let text = template(file);
        // Where a close is defined, and the one check that uses it, are taken out; nothing else may name one.
        if (file === include) {
            for (const name of ['_CHECK_APP_RUNNING', 'KILL_PROCESS']) {
                const { start, end } = macroIn(text, name, file);
                text = text.slice(0, start) + text.slice(end);
            }
        }
        if (file === plugin) {
            for (const name of ['nsProcess::KillProcess', 'nsProcess::CloseProcess']) {
                const { start, end } = macroIn(text, name, file);
                text = text.slice(0, start) + text.slice(end);
                text = text.replace(new RegExp(`^!define ${name.replace(/:/g, '\\:')} .*$`, 'm'), '');
            }
        }
        assert.doesNotMatch(text, closing, `${file} closes a program outside the template's own check.`);
        inserted += [...text.matchAll(/!insertmacro _CHECK_APP_RUNNING\b/g)].length;
    }
    // The template's own check is inserted once, in CHECK_APP_RUNNING's else branch, which a custom check takes the place of.
    assert.equal(inserted, 1);
    assert.match(template(include), /!macro CHECK_APP_RUNNING\b[\s\S]*?!ifmacrodef customCheckAppRunning\s+!insertmacro customCheckAppRunning\s+!else\s+!insertmacro IS_POWERSHELL_AVAILABLE\s+!insertmacro _CHECK_APP_RUNNING\s+!endif\s+!macroend/);
    assert.match(template('installSection.nsh'), /!insertmacro CHECK_APP_RUNNING/);
    assert.match(template('uninstaller.nsh'), /!insertmacro CHECK_APP_RUNNING/);
});

test('the custom check closes the application by its exact path and never a program by its name', () => {
    const check = macro('customCheckAppRunning');
    assert.match(check, /SetEnvironmentVariable\(t "JASON_DESKTOP_APP", t "\$INSTDIR\\\$\{APP_EXECUTABLE_FILENAME\}"\)/);
    for (const name of [/IMAGENAME/i, /taskkill/i, /tasklist/i, /\/IM\b/, /StartsWith/i, /nsProcess/i, /-like\b/i, /-match\b/i]) {
        assert.doesNotMatch(script, name, `installer/uninstall.nsh goes by ${name}.`);
    }
    // Every PowerShell command it runs reads the processes through one of the two filters; only one stops any, and
    // that one reads the application's.
    const commands = [...check.matchAll(/nsExec::Exec `([^`]*)`/g)].map((found) => found[1]!);
    assert.equal(commands.length, 5);
    // The runtime's filter is read through the look that fails closed, which holds it once (JASON_RUNTIME_FOUND).
    for (const command of commands) assert.ok(command.includes('${JASON_OWN_APP}') || command.includes('${JASON_RUNTIME_FOUND}'), `A command reads processes another way: ${command}`);
    assert.equal(define_('JASON_RUNTIME_FOUND').split('${JASON_OWN_RUNTIME}').length, 2);
    const stopping = commands.filter((command) => /Stop-Process/.test(command));
    assert.deepEqual(stopping, [appStop]);
    assert.equal(filter('JASON_OWN_APP'), 'Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase) }');
});

test('the whole include runs only the commands these rows name, stops only the application, and names the two programs only where it looks for them', () => {
    const lines = script.split(/\r?\n/).map((line) => line.trim());
    const code = lines.filter((line) => line !== '' && !line.startsWith(';') && !line.startsWith('#'));
    // Every way nsis runs a program, a plugin's included.
    const running = /\b(?:Exec|ExecWait|ExecShell|ExecShellWait|ExecDos|ExecCmd|nsExec|ShellExecWait|UAC::Exec|UAC::ShellExec)\b|::Exec/i;
    assert.deepEqual(code.filter((line) => running.test(line)), [
        `nsExec::Exec \`${runtimeLook}\``,
        `nsExec::Exec \`${appLook}\``,
        `nsExec::Exec \`${appStop}\``,
        `nsExec::Exec \`${runtimeLook}\``,
        stopRuntimeLine,
        `nsExec::Exec \`${runtimeLook}\``,
        `nsExec::Exec '"$INSTDIR\\resources\\bundle\\jason.exe" install'`,
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall' $R2`,
        `ExecWait '"$INSTDIR\\\${APP_EXECUTABLE_FILENAME}" --uninstall --quiet' $R2`,
    ]);
    // The one stop, of the application, is the only line of the whole include that ends a program, comments included.
    assert.equal([...script.matchAll(/Stop-Process/g)].length, 1);
    const rest = lines.filter((line) => line !== `nsExec::Exec \`${appStop}\``).join('\n');
    assert.doesNotMatch(rest, closing);
    // The two programs are named in the environment once each, and read only by their filter and the commands above.
    const naming = (word: string) => lines.filter((line) => line.includes(word));
    assert.deepEqual(naming('JASON_DESKTOP_APP'), [
        `!define JASON_OWN_APP \`${filterText('JASON_OWN_APP')}\``,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_APP", t "$INSTDIR\\\${APP_EXECUTABLE_FILENAME}")'`,
    ]);
    assert.deepEqual(naming('JASON_DESKTOP_RUNTIME'), [
        `!define JASON_OWN_RUNTIME \`${filterText('JASON_OWN_RUNTIME')}\``,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_RUNTIME", t "$INSTDIR\\resources\\bundle\\jason.exe")'`,
    ]);
    // The record of the one process the runtime's filter passes over is named once, beside the carried executable, and
    // read by that filter alone.
    assert.deepEqual(naming('JASON_DESKTOP_UPDATING'), [
        `!define JASON_OWN_RUNTIME \`${filterText('JASON_OWN_RUNTIME')}\``,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", t "$INSTDIR\\resources\\bundle\\jason-update.pid")'`,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", i 0)'`,
    ]);
    assert.deepEqual(naming('JASON_OWN_RUNTIME'), [
        `!define JASON_OWN_RUNTIME \`${filterText('JASON_OWN_RUNTIME')}\``,
        `!define JASON_RUNTIME_FOUND \`${define_('JASON_RUNTIME_FOUND')}\``,
    ]);
    assert.deepEqual(naming('JASON_RUNTIME_FOUND'), [
        `!define JASON_RUNTIME_FOUND \`${define_('JASON_RUNTIME_FOUND')}\``,
        `nsExec::Exec \`${runtimeLook}\``,
        `nsExec::Exec \`${runtimeLook}\``,
        `nsExec::Exec \`${runtimeLook}\``,
    ]);
    assert.deepEqual(naming('JASON_OWN_APP'), [`!define JASON_OWN_APP \`${filterText('JASON_OWN_APP')}\``, `nsExec::Exec \`${appLook}\``, `nsExec::Exec \`${appStop}\``]);
    // The uninstaller's own part is the last macro, so the rows over it in uninstall.test.ts read it to the end.
    const unInstall = macroIn(script, 'customUnInstall', 'installer/uninstall.nsh');
    assert.equal(script.slice(unInstall.end).trim(), '');
    assert.ok(unInstall.start > macroIn(script, 'customCheckAppRunning', 'installer/uninstall.nsh').end);
});

test('the carried jason executable is looked for by its exact path, in a filter that no stop reads, and otherwise only named to a person or run to set the machine up', () => {
    const check = macro('customCheckAppRunning');
    // The folder's resources appear in four places: the words a person is shown when a process is still found, which
    // only a message box reads, the path handed to the runtime's filter through the environment, the record that filter
    // reads under the update's own installer, and the installer's last step, which runs that jason's own `jason install`
    // (installer-setup.test.ts holds it).
    assert.deepEqual([...script.matchAll(/\$INSTDIR\\resources/gi)].map((found) => script.slice(script.lastIndexOf('\n', found.index) + 1, script.indexOf('\n', found.index)).trim()), [
        `!define JASON_RUNTIME_STILL "${define('JASON_RUNTIME_STILL')}"`,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_RUNTIME", t "$INSTDIR\\resources\\bundle\\jason.exe")'`,
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", t "$INSTDIR\\resources\\bundle\\jason-update.pid")'`,
        `nsExec::Exec '"$INSTDIR\\resources\\bundle\\jason.exe" install'`,
    ]);
    assert.deepEqual(script.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.includes('JASON_RUNTIME_STILL') && !line.startsWith('!define')), [
        'MessageBox MB_OK|MB_ICONSTOP "${JASON_RUNTIME_STILL}"',
    ]);
    assert.equal(filter('JASON_OWN_RUNTIME'), "& { $skip = -1; try { $named = @((Get-Content -LiteralPath $env:JASON_DESKTOP_UPDATING -TotalCount 1 -ErrorAction Stop) -split ' '); $found = Get-Process -Id ([int]$named[0]) -ErrorAction Stop; $asked = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId=' + $found.Id); if ($found.StartTime.ToFileTimeUtc() -eq [long]$named[1] -and $asked.CommandLine.Contains(' update apply')) { $skip = $found.Id } } catch { }; Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_RUNTIME, [StringComparison]::OrdinalIgnoreCase) -and $_.ProcessId -ne $skip } }");
    const looks = [...check.matchAll(/nsExec::Exec `([^`]*)`/g)].map((found) => found[1]!).filter((command) => command.includes('${JASON_RUNTIME_FOUND}'));
    assert.deepEqual(looks, Array(3).fill(runtimeLook));
});

test('a person is asked about a runtime before the application is closed, so a Cancel leaves Jason as it was', () => {
    const check = macro('customCheckAppRunning');
    const first = check.slice(check.indexOf('Var /GLOBAL JasonRuntimeAsked'), check.indexOf(`nsExec::Exec \`${appLook}`));
    assert.deepEqual(codeLines(first), [
        'Var /GLOBAL JasonRuntimeAsked',
        'StrCpy $JasonRuntimeAsked ""',
        '${ifNot} ${isUpdated}',
        '${AndIfNot} ${Silent}',
        `nsExec::Exec \`${runtimeLook}\``,
        'Pop $R0',
        // Every exit but the one clean "nothing found" is a process found.
        '${if} $R0 != 1',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "${JASON_RUNTIME_ASK}" IDOK +3',
        'SetErrorLevel 1',
        'Quit',
        'StrCpy $JasonRuntimeAsked "asked"',
        '${endIf}',
        '${endIf}',
        '${if} ${isUpdated}',
        'Sleep 300',
        '${endIf}',
    ]);
    assert.ok(check.indexOf(runtimeLook) < check.indexOf('Stop-Process'));
    assert.equal(define('JASON_RUNTIME_ASK'), "A Jason runtime is running from this application's folder. Choose OK to stop it and go on.");
});

test('a runtime running from the carried executable is stopped through the application’s own start on every path, and a Jason process still found there ends the run', () => {
    const check = macro('customCheckAppRunning');
    const after = check.slice(check.indexOf('Stop-Process'));
    const runtime = after.slice(after.indexOf(runtimeLook) + runtimeLook.length + 1);
    assert.deepEqual(codeLines(runtime), [
        'Pop $R0',
        '${if} $R0 != 1',
        // A person not asked before the application was closed is asked now; nobody else is asked.
        '${ifNot} ${isUpdated}',
        '${AndIfNot} ${Silent}',
        '${AndIf} $JasonRuntimeAsked == ""',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "${JASON_RUNTIME_ASK}" IDOK +3',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        // Every path, a person's after OK, a silent run's and an update's, stops it the way a person would.
        stopRuntimeLine,
        'Sleep 1000',
        // The look decides, never the start's exit code.
        `nsExec::Exec \`${runtimeLook}\``,
        'Pop $R0',
        '${if} $R0 != 1',
        '${ifNot} ${isUpdated}',
        '${AndIfNot} ${Silent}',
        'MessageBox MB_OK|MB_ICONSTOP "${JASON_RUNTIME_STILL}"',
        '${endIf}',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        '${endIf}',
        '${else}',
        '${ifNot} ${isUpdated}',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Close Jason if it is open, and stop its runtime with jason runtime stop if it runs, then choose OK." /SD IDOK IDOK +2',
        'Quit',
        '${endIf}',
        '${endIf}',
    ]);
    assert.equal(
        define('JASON_RUNTIME_STILL'),
        "A Jason process from this application's folder is still running, so nothing was changed. If it is a runtime that did not stop, stop it with $INSTDIR\\resources\\bundle\\jason.exe runtime stop, with JASON_DATA_DIR set if it was started with another data directory; other Jason work from this folder ends by itself. Then run this again.",
    );
    // The start is the one the application reads, and the update's application has quit before it is made.
    assert.ok(stopRuntimeLine.endsWith(` ${stopRuntimeFlag}'`));
    assert.ok(check.indexOf(stopRuntimeLine) > check.indexOf('Stop-Process'));
});

test('the uninstall an update runs closes the application without asking; a person is asked first', () => {
    const check = macro('customCheckAppRunning');
    assert.match(check, /\$\{ifNot\} \$\{isUpdated\}\s+MessageBox MB_OKCANCEL\|MB_ICONEXCLAMATION "\$\(appRunning\)" \/SD IDOK IDOK \+2\s+Quit\s+\$\{endIf\}/);
});

test('the filters find a running program by its exact path, whatever the case, and not by a path it only begins', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, () => {
    for (const [name, variable] of [['JASON_OWN_APP', 'JASON_DESKTOP_APP'], ['JASON_OWN_RUNTIME', 'JASON_DESKTOP_RUNTIME']] as const) {
        const running = (file: string) => {
            try {
                execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `if (@(${filter(name)}).Count -gt 0) { exit 0 } else { exit 1 }`], {
                    env: { ...process.env, [variable]: file },
                    stdio: 'ignore',
                    windowsHide: true,
                });
                return true;
            } catch {
                return false;
            }
        };
        const self = process.execPath;
        assert.equal(running(self), true, name);
        assert.equal(running(self.toUpperCase()), true, name);
        assert.equal(running(dirname(self)), false, name);
        assert.equal(running(self.slice(0, -1)), false, name);
        assert.equal(running(`${self}.old`), false, name);
        assert.equal(running(join(dirname(self), 'jason', basename(self))), false, name);
    }
});

test('the runtime filter passes over only the live jason update apply the record names, by its id and its start, and every other process is still found', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, async () => {
    // Processes of the test's own, from a path no other process runs from, so that what the filter finds is theirs alone:
    // node itself, linked under a folder of the test's own, waiting until it is ended. One is started as a carried jason's
    // update apply is, its command line carrying ` update apply`; the other is not.
    const folder = mkdtempSync(join(tmpdir(), 'jason-update-pid-'));
    const image = join(folder, 'jason.exe');
    try {
        linkSync(process.execPath, image);
    } catch {
        copyFileSync(process.execPath, image);
    }
    const children: ChildProcess[] = [];
    const start = async (...extra: string[]): Promise<ChildProcess> => {
        const child = spawn(image, ['-e', 'setInterval(() => {}, 1000)', ...extra], { stdio: 'ignore', windowsHide: true });
        children.push(child);
        await new Promise<void>((resolve, reject) => {
            child.once('spawn', () => resolve());
            child.once('error', reject);
        });
        return child;
    };
    try {
        const record = join(folder, 'jason-update.pid');
        const found = (updating: string) => {
            try {
                execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', runtimeFound()], {
                    env: { ...process.env, JASON_DESKTOP_RUNTIME: image, JASON_DESKTOP_UPDATING: updating },
                    stdio: 'ignore',
                    windowsHide: true,
                });
                return true;
            } catch {
                return false;
            }
        };
        // A process's start as Windows reads it, which is what the carried jason writes (Process.StartTime, as file time).
        const startOf = (child: ChildProcess) => {
            const started = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(Get-Process -Id ${child.pid}).StartTime.ToFileTimeUtc()`], { encoding: 'utf8', windowsHide: true }).trim();
            assert.match(started, /^\d+$/);
            return started;
        };

        const applying = await start('update', 'apply');
        const started = startOf(applying);
        // No record, an empty one, a malformed one: nothing is passed over, and the process is found.
        assert.equal(found(join(folder, 'none.pid')), true);
        writeFileSync(record, '');
        assert.equal(found(record), true);
        writeFileSync(record, 'not a pid');
        assert.equal(found(record), true);
        // A stale record: the id with a start that is not the process's, as a record left behind once its id is reused.
        writeFileSync(record, `${applying.pid} ${BigInt(started) - 1n}`);
        assert.equal(found(record), true, 'a record whose start does not match passed a process over');
        // The update apply the record names, alive, by its id and its start, is passed over.
        writeFileSync(record, `${applying.pid} ${started}`);
        assert.equal(found(record), false, 'the update apply the record names was not passed over');

        // A planted record: a live process at the path, named by its true id and start, that is no update apply.
        const other = await start();
        writeFileSync(record, `${other.pid} ${startOf(other)}`);
        assert.equal(found(record), true, 'a process that is no update apply was passed over');
        // Every other process at the path is still found while the record passes the one over.
        writeFileSync(record, `${applying.pid} ${started}`);
        assert.equal(found(record), true, 'a second process at the carried path was passed over with the first');

        // A record that names a process that has ended passes nothing over: the other process is still found.
        applying.kill();
        await new Promise<void>((resolve) => (applying.exitCode !== null || applying.signalCode !== null ? resolve() : applying.once('exit', () => resolve())));
        assert.equal(found(record), true, 'a record naming a process that has ended passed a process over');
    } finally {
        for (const child of children) {
            child.kill();
            await new Promise<void>((resolve) => (child.exitCode !== null || child.signalCode !== null ? resolve() : child.once('exit', () => resolve())));
        }
        rmSync(folder, { recursive: true, force: true });
    }
});

test('the runtime look fails closed: a query the system does not answer is asked again, then exits 2, which is read as found', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, () => {
    // The command that ships, with a query Windows refuses in place of the filter: the class does not exist.
    const failing = runtimeFound('Get-CimInstance -ClassName Jason_No_Such_Class');
    const began = Date.now();
    let exit = -1;
    try {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', failing], { stdio: 'ignore', windowsHide: true });
        exit = 0;
    } catch (error) {
        exit = (error as { status: number }).status;
    }
    assert.equal(exit, 2, 'a look whose query failed did not exit 2');
    assert.ok(Date.now() - began >= 4 * 500, 'the failed query was not asked again');
    // And the installer reads that exit, as every exit but 1, as a process found: each runtime look is followed by that test.
    const check = macro('customCheckAppRunning');
    const after = [...check.matchAll(/nsExec::Exec `([^`]*)`\s+Pop \$R0\s+\$\{if\} \$R0 (\S+) (\S+)/g)].filter((found) => found[1] === runtimeLook);
    assert.equal(after.length, 3);
    for (const found of after) assert.deepEqual([found[2], found[3]], ['!=', '1']);
});

test('only the update’s own installer and the uninstaller it runs read the record; a person’s install or uninstall reads none, whatever it was started with', () => {
    const check = macro('customCheckAppRunning');
    // electron-builder runs this check in a person's install and uninstall too (`CHECK_APP_RUNNING`), and passes --updated
    // to the old uninstaller an update runs (installUtil.nsh, uninstallOldVersion): only those pass a process over.
    const naming = check.slice(0, check.indexOf('!insertmacro IS_POWERSHELL_AVAILABLE'));
    const underUpdate = naming.indexOf('${if} ${isUpdated}');
    assert.ok(underUpdate >= 0, 'the record is named outside the update’s own word');
    assert.deepEqual(codeLines(naming.slice(underUpdate)), [
        '${if} ${isUpdated}',
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", t "$INSTDIR\\resources\\bundle\\jason-update.pid")'`,
        '${else}',
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", i 0)'`,
        '${endIf}',
    ]);
    assert.ok(!naming.slice(0, underUpdate).includes('JASON_DESKTOP_UPDATING'), 'the record is named before the update’s own word');
});

test('the record the runtime filter reads is named once, inside the installation folder, and read by that filter alone', () => {
    const check = macro('customCheckAppRunning');
    assert.deepEqual([...check.matchAll(/JASON_DESKTOP_UPDATING", t "([^"]*)"/g)].map((found) => found[1]), ['$INSTDIR\\resources\\bundle\\jason-update.pid']);
    assert.ok(check.indexOf('JASON_DESKTOP_UPDATING') < check.indexOf('IS_POWERSHELL_AVAILABLE'), 'the record is named after a look may read it');
    assert.deepEqual([...script.matchAll(/\$\$?env:JASON_DESKTOP_UPDATING/g)].length, 1);
    // NSIS holds a string to 1024 characters; the longest command it runs is the runtime look with its filter.
    assert.ok(`"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -NoProfile -NonInteractive -Command "${define_('JASON_RUNTIME_FOUND').replace('${JASON_OWN_RUNTIME}', filterText('JASON_OWN_RUNTIME'))}"`.length < 1024);
});
