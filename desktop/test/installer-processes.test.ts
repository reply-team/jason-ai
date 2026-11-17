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
import { configuration } from '../electron-builder.config.mjs';
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
    const found = new RegExp(`^!define ${name} (?:\`([^\`]+)\`|(\\d+))$`, 'm').exec(script);
    assert.ok(found !== null, `installer/uninstall.nsh defines no ${name}.`);
    return (found[1] ?? found[2])!;
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
/** The look whether the application runs, by its filter, failing closed as the runtime's does. */
const appLook = '"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_APP_FOUND}"';

/** The PowerShell the installer runs for an application look, as PowerShell reads it, with `filter` where a row says. */
function appFound(filterText: string = filter('JASON_OWN_APP')): string {
    const [before, after, ...more] = define_('JASON_APP_FOUND').split('${JASON_OWN_APP}');
    assert.equal(more.length, 0, 'the look names the filter more than once');
    return `${before!.replaceAll('$$', '$')}${filterText}${after!.replaceAll('$$', '$')}`;
}
/** The bounded wait an update makes for the application to quit, by its filter, failing closed as the looks do. */
const appWait = '"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_APP_GONE}"';

/** The PowerShell the installer runs for that wait, as PowerShell reads it, with `filter` and the bound where a row says. */
function appGone(filterText: string = filter('JASON_OWN_APP'), seconds: string = define_('JASON_APP_WAIT_SECONDS')): string {
    const [before, after, ...more] = define_('JASON_APP_GONE').split('${JASON_OWN_APP}');
    assert.equal(more.length, 0, 'the wait names the filter more than once');
    const bound = (text: string) => text.replaceAll('${JASON_APP_WAIT_SECONDS}', seconds).replaceAll('$$', '$');
    return `${bound(before!)}${filterText}${bound(after!)}`;
}
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
    assert.equal(commands.length, 7);
    // Each filter is read through a look that fails closed, which holds it once (JASON_RUNTIME_FOUND, JASON_APP_FOUND);
    // the one stop reads the application's filter itself.
    for (const command of commands) assert.ok(command.includes('${JASON_APP_FOUND}') || command.includes('${JASON_RUNTIME_FOUND}') || command === appWait || command === appStop, `A command reads processes another way: ${command}`);
    assert.equal(define_('JASON_RUNTIME_FOUND').split('${JASON_OWN_RUNTIME}').length, 2);
    assert.equal(define_('JASON_APP_FOUND').split('${JASON_OWN_APP}').length, 2);
    assert.equal(define_('JASON_APP_GONE').split('${JASON_OWN_APP}').length, 2);
    // The two looks are one wrap around two filters.
    assert.equal(define_('JASON_APP_FOUND').replace('${JASON_OWN_APP}', '${JASON_OWN_RUNTIME}'), define_('JASON_RUNTIME_FOUND'));
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
        `nsExec::Exec \`${appWait}\``,
        `nsExec::Exec \`${appLook}\``,
        `nsExec::Exec \`${appStop}\``,
        `nsExec::Exec \`${appLook}\``,
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
    assert.deepEqual(naming('JASON_OWN_APP'), [
        `!define JASON_OWN_APP \`${filterText('JASON_OWN_APP')}\``,
        `!define JASON_APP_FOUND \`${define_('JASON_APP_FOUND')}\``,
        `!define JASON_APP_GONE \`${define_('JASON_APP_GONE')}\``,
        `nsExec::Exec \`${appStop}\``,
    ]);
    assert.deepEqual(naming('JASON_APP_GONE'), [
        `!define JASON_APP_GONE \`${define_('JASON_APP_GONE')}\``,
        `nsExec::Exec \`${appWait}\``,
    ]);
    assert.deepEqual(naming('JASON_APP_FOUND'), [
        `!define JASON_APP_FOUND \`${define_('JASON_APP_FOUND')}\``,
        `nsExec::Exec \`${appLook}\``,
        `nsExec::Exec \`${appLook}\``,
    ]);
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
        'MessageBox MB_OK|MB_ICONEXCLAMATION "jason install did not set everything up (it answered $R0). Run $INSTDIR\\resources\\bundle\\jason.exe install in a terminal to see what it says." /SD IDOK',
    ]);
    assert.deepEqual(script.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.includes('JASON_RUNTIME_STILL') && !line.startsWith('!define')), [
        'StrCpy $R1 "${JASON_RUNTIME_STILL}"',
    ]);
    assert.equal(filter('JASON_OWN_RUNTIME'), "& { $skip = -1; try { $named = @((Get-Content -LiteralPath $env:JASON_DESKTOP_UPDATING -TotalCount 1 -ErrorAction Stop) -split ' '); $found = Get-Process -Id ([int]$named[0]) -ErrorAction Stop; $asked = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId=' + $found.Id); if ($found.StartTime.ToFileTimeUtc() -eq [long]$named[1] -and $asked.CommandLine.Contains(' update apply')) { $skip = $found.Id } } catch { }; Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_RUNTIME, [StringComparison]::OrdinalIgnoreCase) -and $_.ProcessId -ne $skip } }");
    const looks = [...check.matchAll(/nsExec::Exec `([^`]*)`/g)].map((found) => found[1]!).filter((command) => command.includes('${JASON_RUNTIME_FOUND}'));
    assert.deepEqual(looks, Array(3).fill(runtimeLook));
});

test('a person is asked about a runtime before the application is closed, so a Cancel leaves Jason as it was', () => {
    const check = macro('customCheckAppRunning');
    const first = check.slice(check.indexOf('Var /GLOBAL JasonRuntimeAsked'), check.indexOf(`nsExec::Exec \`${appWait}`));
    assert.deepEqual(codeLines(first), [
        'Var /GLOBAL JasonRuntimeAsked',
        'StrCpy $JasonRuntimeAsked ""',
        '${ifNot} ${isUpdated}',
        '${AndIfNot} ${Silent}',
        `nsExec::Exec \`${runtimeLook}\``,
        'Pop $R0',
        // Every exit but the one clean "nothing found" is a process found; a look that failed is said as such.
        '${if} $R0 != 1',
        '${if} $R0 S== 0',
        'StrCpy $R1 "${JASON_RUNTIME_ASK}"',
        '${else}',
        'StrCpy $R1 "${JASON_LOOK_ASK}"',
        '${endIf}',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" IDOK +3',
        'SetErrorLevel 1',
        'Quit',
        'StrCpy $JasonRuntimeAsked "asked"',
        '${endIf}',
        '${endIf}',
        '${if} ${isUpdated}',
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
        // Chosen by the exit, as the other three are: a look that failed is not said to have found a runtime.
        '${if} $R0 S== 0',
        'StrCpy $R1 "${JASON_RUNTIME_ASK}"',
        '${else}',
        'StrCpy $R1 "${JASON_LOOK_ASK}"',
        '${endIf}',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" IDOK +3',
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
        '${if} $R0 S== 0',
        'StrCpy $R1 "${JASON_RUNTIME_STILL}"',
        '${else}',
        'StrCpy $R1 "${JASON_LOOK_FAILED}"',
        '${endIf}',
        'MessageBox MB_OK|MB_ICONSTOP "$R1" /SD IDOK',
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

test('an update waits for the application to quit; a silent run, updated or not, never closes it; a person who watches is asked before it is closed', () => {
    const check = macro('customCheckAppRunning');
    // The silent way out comes before the question and the stop: a silent run that still finds the application - at the
    // bound of an update's wait, or at a plain install's look - or could not look, ends with error level 3, its own, with
    // no box and nothing closed.
    assert.match(codeLines(check).join('\n'), /\$\{if\} \$\{isUpdated\}\s+nsExec::Exec `[^`]+\$\{JASON_APP_GONE\}[^`]+`\s+\$\{else\}\s+nsExec::Exec `[^`]+\$\{JASON_APP_FOUND\}[^`]+`\s+\$\{endIf\}\s+Pop \$R0\s+\$\{if\} \$R0 != 1\s+\$\{if\} \$\{Silent\}\s+SetErrorLevel 3\s+Quit\s+\$\{endIf\}\s+\$\{if\} \$R0 S== 0\s+StrCpy \$R1 "\$\(appRunning\)"\s+\$\{else\}\s+StrCpy \$R1 "\$\{JASON_APP_UNSURE\}"\s+\$\{endIf\}\s+MessageBox MB_OKCANCEL\|MB_ICONEXCLAMATION "\$R1" \/SD IDOK IDOK \+2\s+Quit\s+DetailPrint "\$\(appClosing\)"/);
    // The one stop comes after that way out, so no silent run reaches it; 3 is said there alone, so a caller that reads
    // it - the install script, a move - knows the application was running and nothing was changed.
    assert.ok(check.indexOf('SetErrorLevel 3') < check.indexOf('Stop-Process'));
    assert.equal([...script.matchAll(/SetErrorLevel 3\b/g)].length, 1);
    assert.equal([...script.matchAll(/SetErrorLevel (\d+)/g)].map((found) => found[1]).sort().join(','), '1,1,1,1,1,2,3');
    // No fixed pause stands in for the wait any more.
    assert.doesNotMatch(check, /Sleep 300\b/);
    // The bound: a minute, three times the twenty seconds late a slow quit is proven at, looked at twice a second.
    assert.equal(define_('JASON_APP_WAIT_SECONDS'), '60');
    assert.equal(define_('JASON_APP_GONE'), "$$ErrorActionPreference = 'Stop'; $$clock = [Diagnostics.Stopwatch]::StartNew(); while ($$true) { try { if (@(${JASON_OWN_APP}).Count -eq 0) { exit 1 }; $$failed = $$false } catch { $$failed = $$true }; if ($$clock.Elapsed.TotalSeconds -ge ${JASON_APP_WAIT_SECONDS}) { if ($$failed) { exit 2 }; exit 0 }; Start-Sleep -Milliseconds 500 }");
    // NSIS holds a string to 1024 characters.
    assert.ok(`"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe" -NoProfile -NonInteractive -Command "${appGone(filterText('JASON_OWN_APP'))}"`.length < 1024);
});

test('the application look fails closed, is made again after the application was closed, and a run where it is still found or that look failed ends with error level 1', () => {
    const check = macro('customCheckAppRunning');
    const waited = check.indexOf(`nsExec::Exec \`${appWait}\``);
    const app = check.slice(check.lastIndexOf('${if} ${isUpdated}', waited), check.indexOf(`nsExec::Exec \`${runtimeLook}\``, check.indexOf('Stop-Process')));
    assert.deepEqual(codeLines(app), [
        // An update waits, bounded, for the application it told to quit; a person's install or uninstall looks once.
        '${if} ${isUpdated}',
        `nsExec::Exec \`${appWait}\``,
        '${else}',
        `nsExec::Exec \`${appLook}\``,
        '${endIf}',
        'Pop $R0',
        // Every exit but the one clean "nothing found" is the application found, a failed look and nsExec's own error included.
        '${if} $R0 != 1',
        // A silent run, an update or not, closes nothing and shows nothing: it ends with its own error level, 3, and what
        // was installed stays as it was.
        '${if} ${Silent}',
        'SetErrorLevel 3',
        'Quit',
        '${endIf}',
        // A run a person watches is asked, as a person's install always was.
        '${if} $R0 S== 0',
        'StrCpy $R1 "$(appRunning)"',
        '${else}',
        'StrCpy $R1 "${JASON_APP_UNSURE}"',
        '${endIf}',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" /SD IDOK IDOK +2',
        'Quit',
        'DetailPrint "$(appClosing)"',
        `nsExec::Exec \`${appStop}\``,
        'Pop $R0',
        'Sleep 1000',
        // The look decides, never the stop's exit code.
        `nsExec::Exec \`${appLook}\``,
        'Pop $R0',
        '${if} $R0 != 1',
        // A person who chose OK is told, an update they watch included.
        '${ifNot} ${Silent}',
        '${if} $R0 S== 0',
        'StrCpy $R1 "${JASON_APP_STILL}"',
        '${else}',
        'StrCpy $R1 "${JASON_LOOK_FAILED}"',
        '${endIf}',
        'MessageBox MB_OK|MB_ICONSTOP "$R1" /SD IDOK',
        '${endIf}',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        '${endIf}',
    ]);
    // Before a file is replaced: the runtime's own look and the files come after this.
    assert.ok(check.indexOf('SetErrorLevel 1', check.indexOf('JASON_APP_STILL')) < check.indexOf(stopRuntimeLine));
});

test('a look that could not be made says so in words of its own, never that Jason is running, and every box a silent run can reach answers itself', () => {
    assert.equal(define('JASON_LOOK_ASK'), "Jason could not look for its own processes, so it cannot tell whether a Jason runtime is running from this application's folder. Choose OK to stop it if it is, and go on.");
    assert.equal(define('JASON_APP_UNSURE'), 'Jason could not look for its own processes, so it cannot tell whether Jason is open. Choose OK to close it if it is, and go on.');
    assert.equal(define('JASON_APP_STILL'), 'Jason is still open, so nothing was changed. Close it, then run this again.');
    assert.equal(define('JASON_LOOK_FAILED'), "Jason could not look for its own processes, so it cannot tell whether Jason is running from this application's folder, and nothing was changed. Run this again.");
    for (const name of ['JASON_LOOK_ASK', 'JASON_APP_UNSURE', 'JASON_LOOK_FAILED']) assert.ok(define(name).startsWith('Jason could not look for its own processes, so it cannot tell whether '), `${name} says a process runs`);
    // Each sentence a failed look chooses is chosen only where the exit is not the clean 0 of a process found.
    const check = macro('customCheckAppRunning');
    for (const name of ['JASON_LOOK_ASK', 'JASON_APP_UNSURE', 'JASON_LOOK_FAILED']) {
        for (const found of check.matchAll(new RegExp(`StrCpy \\$R1 "\\$\\{${name}\\}"`, 'g'))) {
            assert.match(check.slice(0, found.index).trimEnd(), /\$\{if\} \$R0 S== 0\s+StrCpy \$R1 "[^"]+"\s+\$\{else\}$/, `${name} is chosen without the found test before it`);
        }
    }
    // A box without a silent answer is shown only where the run is not silent.
    const lines = codeLines(check);
    lines.forEach((line, index) => {
        if (line.startsWith('MessageBox') && !line.includes('/SD ')) {
            const before = lines.slice(0, index).reverse();
            assert.ok(before.includes('${AndIfNot} ${Silent}'), `a box a silent run reaches has no answer: ${line}`);
        }
    });
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

test('the application look fails closed the same way: a query the system does not answer exits 2, which is read as found', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, () => {
    const failing = appFound('Get-CimInstance -ClassName Jason_No_Such_Class');
    const began = Date.now();
    let exit = -1;
    try {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', failing], { stdio: 'ignore', windowsHide: true });
        exit = 0;
    } catch (error) {
        exit = (error as { status: number }).status;
    }
    assert.equal(exit, 2, 'an application look whose query failed did not exit 2');
    assert.ok(Date.now() - began >= 4 * 500, 'the failed query was not asked again');
    // A query that answers, with nothing found, is the one clean exit 1.
    let clean = -1;
    try {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', appFound('@()')], { stdio: 'ignore', windowsHide: true });
        clean = 0;
    } catch (error) {
        clean = (error as { status: number }).status;
    }
    assert.equal(clean, 1);
    const check = macro('customCheckAppRunning');
    const after = [...check.matchAll(/nsExec::Exec `([^`]*)`\s+(?:\$\{endIf\}\s+)?Pop \$R0\s+\$\{if\} \$R0 (\S+) (\S+)/g)].filter((found) => found[1] === appLook || found[1] === appWait);
    assert.equal(after.length, 2);
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

test('the update’s wait runs as it ships: it ends as soon as the application has quit, and at the bound says found, or a look that failed, never that it is gone', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, async () => {
    // A process of the test's own at a path no other process runs from, standing in for the application: node itself,
    // linked under a folder of the test's own as Jason.exe, ending after the time it is given or waiting until it is ended.
    const folder = mkdtempSync(join(tmpdir(), 'jason-app-wait-'));
    const image = join(folder, 'Jason.exe');
    try {
        linkSync(process.execPath, image);
    } catch {
        copyFileSync(process.execPath, image);
    }
    const children: ChildProcess[] = [];
    const start = async (lives: number): Promise<ChildProcess> => {
        const child = spawn(image, ['-e', lives < 0 ? 'setInterval(() => {}, 1000)' : `setTimeout(() => {}, ${lives})`], { stdio: 'ignore', windowsHide: true });
        children.push(child);
        await new Promise<void>((resolve, reject) => {
            child.once('spawn', () => resolve());
            child.once('error', reject);
        });
        return child;
    };
    // The wait as it ships, with a bound of four seconds in place of the minute and, where a row says, another filter.
    const wait = (filterText?: string): Promise<{ exit: number; seconds: number }> =>
        new Promise((resolve, reject) => {
            const began = Date.now();
            const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', appGone(filterText, '4')], {
                env: { ...process.env, JASON_DESKTOP_APP: image },
                stdio: 'ignore',
                windowsHide: true,
            });
            child.once('error', reject);
            child.once('exit', (code) => resolve({ exit: code ?? -1, seconds: (Date.now() - began) / 1000 }));
        });
    try {
        // Nothing at the path: gone at the first look.
        const none = await wait();
        assert.equal(none.exit, 1, 'a wait with nothing running did not say gone');

        // The application quits two seconds into the wait: gone before the bound.
        await start(2000);
        const quits = await wait();
        assert.equal(quits.exit, 1, 'a wait over an application that quit did not say gone');

        // The application never quits: found at the bound, and not before it.
        const stays = await start(-1);
        const found = await wait();
        assert.equal(found.exit, 0, 'a wait over an application that never quits did not say found');
        assert.ok(found.seconds >= 4, `the wait said found before its bound, after ${found.seconds} s`);
        assert.equal(stays.exitCode, null, 'the wait ended the application');

        // A query the system never answers: 2 at the bound, which the installer reads as found.
        const failed = await wait('Get-CimInstance -ClassName Jason_No_Such_Class');
        assert.equal(failed.exit, 2, 'a wait whose query failed did not exit 2');
        assert.ok(failed.seconds >= 4, `a wait whose query failed ended before its bound, after ${failed.seconds} s`);
    } finally {
        for (const child of children) {
            child.kill();
            await new Promise<void>((resolve) => (child.exitCode !== null || child.signalCode !== null ? resolve() : child.once('exit', () => resolve())));
        }
        rmSync(folder, { recursive: true, force: true });
    }
});

/** The one body both of electron-builder's hooks for the old uninstaller's result insert. */
const resultLines = [
    // The template's own first test, kept: an old uninstaller that could not be started at all is said, and the install
    // goes on over its files, as it always did.
    '${if} ${Errors}',
    'DetailPrint `Uninstall was not successful. Not able to launch uninstaller!`',
    // An old uninstaller that refused: a person who watches is told, as the template tells them; a silent setup shows no
    // box, which would wait for a click no one gives while it holds the installer's mutex against every later setup.
    '${elseIf} $R0 != 0',
    'MessageBox MB_OK|MB_ICONEXCLAMATION "$(uninstallFailed): $R0" /SD IDOK',
    'DetailPrint `Uninstall was not successful. Uninstaller error code: $R0.`',
    'SetErrorLevel 2',
    'Quit',
    '${endIf}',
];

test('the old version’s uninstaller refusing ends a silent setup with error level 2 and no box: both of the pinned template’s hooks for its result are taken', () => {
    // electron-builder 26.18.0 hands the old uninstaller's result to a hook where one is defined, for the shell context and
    // for the current user, and returns: the rest of the function, the box with no silent answer among it, is then not run.
    const util = template(join('include', 'installUtil.nsh'));
    const handle = util.slice(util.indexOf('Function handleUninstallResult'), util.indexOf('FunctionEnd', util.indexOf('Function handleUninstallResult')));
    assert.match(handle, /\$\{if\} "\$rootKey_uninstallResult" == "SHELL_CONTEXT"\s+!ifmacrodef customUnInstallCheck\s+!insertmacro customUnInstallCheck\s+Return\s+!endif\s+\$\{elseif\} "\$rootKey_uninstallResult" == "HKEY_CURRENT_USER"\s+!ifmacrodef customUnInstallCheckCurrentUser\s+!insertmacro customUnInstallCheckCurrentUser\s+Return\s+!endif\s+\$\{endif\}\s+IfErrors 0 \+3/);
    // The template's own box, which these hooks take the place of, has no silent answer; when it gains one this row says so.
    assert.match(handle, /MessageBox MB_OK\|MB_ICONEXCLAMATION "\$\(uninstallFailed\): \$R0"\n/);
    // The install section hands every result it gets to that function, and the old uninstaller's code is in $R0.
    const section = template('installSection.nsh');
    assert.deepEqual([...section.matchAll(/!insertmacro (uninstallOldVersion|handleUninstallResult) (\S+)/g)].map((found) => `${found[1]} ${found[2]}`), [
        'uninstallOldVersion SHELL_CONTEXT',
        'handleUninstallResult SHELL_CONTEXT',
        'uninstallOldVersion HKEY_CURRENT_USER',
        'handleUninstallResult HKEY_CURRENT_USER',
    ]);
    assert.match(util, /ExecWait '"\$uninstallerFileNameTemp" \/S \/KEEP_APP_DATA \$0 _\?=\$installationDir' \$R0/);
    // Its retries end, in a silent run, with no box of their own and the last code still in $R0.
    assert.match(util, /MessageBox MB_RETRYCANCEL\|MB_ICONEXCLAMATION "\$\(appCannotBeClosed\)" \/SD IDCANCEL IDRETRY OneMoreAttempt\s+Return/);

    for (const name of ['customUnInstallCheck', 'customUnInstallCheckCurrentUser']) {
        assert.deepEqual(codeLines(macro(name)), [`!macro ${name}`, '!insertmacro jasonOldUninstallerResult'], `${name} does not take the result`);
    }
    assert.deepEqual(codeLines(macro('jasonOldUninstallerResult')), ['!macro jasonOldUninstallerResult', ...resultLines]);
    // The same sentence, error level and details lines as the template's, so a person who watches sees what they saw.
    for (const line of ['DetailPrint `Uninstall was not successful. Not able to launch uninstaller!`', 'DetailPrint `Uninstall was not successful. Uninstaller error code: $R0.`', 'SetErrorLevel 2']) {
        assert.ok(handle.includes(line), `the template no longer says ${line}`);
    }
});

test('the template’s two other boxes with no silent answer cannot be reached by this installer', () => {
    // installUtil.nsh's "Unsupported" box is readReg's answer to a root it does not know; every caller passes the root
    // uninstallOldVersion was given, which is only ever SHELL_CONTEXT or HKEY_CURRENT_USER (the row above).
    const util = template(join('include', 'installUtil.nsh'));
    assert.match(util, /\$\{else\}\s+MessageBox MB_OK "Unsupported \$\{ROOT_KEY\}"/);
    const callers = nsisFiles(templates).flatMap((file) => [...template(file).matchAll(/!insertmacro readReg \S+ (\S+)/g)].map((found) => found[1]));
    assert.deepEqual([...new Set(callers)], ['"$rootKey"']);
    assert.match(util, /Function uninstallOldVersion[\s\S]*?Exch \$rootKey/);
    // extractAppPackage.nsh's decompression box is compiled only under ZIP_COMPRESSION, which electron-builder defines only
    // for a zip payload (nsis.useZip); this installer's payload is 7z, whose retry box has its silent answer.
    const extract = template(join('include', 'extractAppPackage.nsh'));
    assert.match(extract, /!ifdef ZIP_COMPRESSION\s+nsisunz::Unzip [^\n]+\s+Pop \$R0\s+StrCmp \$R0 "success" \+3\s+MessageBox MB_OK\|MB_ICONEXCLAMATION "\$\(decompressionFailed\)\$\\n\$R0"\s+Quit\s+!else/);
    assert.match(extract, /MessageBox MB_RETRYCANCEL\|MB_ICONEXCLAMATION "\$\(appCannotBeClosed\)" \/SD IDRETRY IDCANCEL AbortExtract7za/);
    const nsis = configuration({ version: '0.49.0' }).nsis as { useZip?: boolean };
    assert.equal(nsis.useZip, undefined, 'the installer is built with a zip payload, which reaches a box with no silent answer');
});

test('no box of the include waits for a click in a silent run', () => {
    // Every box this include shows during an install or an update carries a silent answer, but the two questions about a
    // runtime, which stand where the run is known to be neither an update nor silent; the uninstaller's own part, which
    // reads /S itself, has rows of its own (uninstall.test.ts).
    const lines = codeLines(script.slice(0, script.indexOf('!macro customUnInstall\n')));
    const unanswered = lines.flatMap((line, index) => (line.startsWith('MessageBox') && !line.includes(' /SD ') ? [index] : []));
    assert.deepEqual(unanswered.map((index) => lines[index]), Array(2).fill('MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" IDOK +3'));
    for (const index of unanswered) {
        const opened = lines.lastIndexOf('${ifNot} ${isUpdated}', index);
        assert.ok(opened >= 0 && lines.slice(opened, index).includes('${AndIfNot} ${Silent}'), `a box a silent run reaches has no answer, at line ${index}`);
    }
});
