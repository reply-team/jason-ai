// Which running programs the installer and the uninstaller close, and which they only look for. The application is
// Jason.exe and the runtime it carries is jason.exe, and Windows does not tell the two names apart, so a check that goes
// by the image name closes every jason.exe on the machine, a runtime of another installation with its work included.
// electron-builder's own check does that where PowerShell is not available, and where it is, closes every program whose
// path merely begins with the installation folder, the runtime this application carries among them. These rows hold the
// check that replaces it to the one file it may close, the application's own executable by its exact path, and to the
// one it looks for and never closes, the carried jason executable by its exact path, of whichever data directory;
// neither the installer nor the uninstaller is run here.

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { basename, dirname, join, relative } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

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

/** What closes a program in nsis: electron-builder's macro, the command-line tools, PowerShell's, and the plugin's. */
const closing = /KILL_PROCESS|taskkill|Stop-Process|KillProcess|CloseProcess|TerminateProcess/i;

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
    assert.equal(commands.length, 4);
    for (const command of commands) assert.ok(command.includes('${JASON_OWN_APP}') || command.includes('${JASON_OWN_RUNTIME}'), `A command reads processes another way: ${command}`);
    const stopping = commands.filter((command) => /Stop-Process/.test(command));
    assert.deepEqual(stopping, ['"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_OWN_APP} | ForEach-Object { Stop-Process -Id $$_.ProcessId -Force }"']);
    assert.equal(filter('JASON_OWN_APP'), 'Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase) }');
});

test('the carried jason executable is looked for by its exact path, in a filter that no stop reads', () => {
    const check = macro('customCheckAppRunning');
    // The folder's resources appear in one place: the path handed to the runtime's filter through the environment.
    assert.deepEqual([...script.matchAll(/\$INSTDIR\\resources/gi)].map((found) => script.slice(script.lastIndexOf('\n', found.index) + 1, script.indexOf('\n', found.index)).trim()), [
        `System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_RUNTIME", t "$INSTDIR\\resources\\bundle\\jason.exe")'`,
    ]);
    assert.equal(filter('JASON_OWN_RUNTIME'), 'Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_RUNTIME, [StringComparison]::OrdinalIgnoreCase) }');
    const looks = [...check.matchAll(/nsExec::Exec `([^`]*)`/g)].map((found) => found[1]!).filter((command) => command.includes('${JASON_OWN_RUNTIME}'));
    assert.deepEqual(looks, Array(2).fill('"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 }"'));
});

test('a runtime running from the carried executable ends an update and a silent run before a file is replaced; a person is told and asked to look again', () => {
    const check = macro('customCheckAppRunning');
    const runtime = check.slice(check.indexOf('${JASON_OWN_RUNTIME}'));
    const lines = runtime.split(/\r?\n/).map((line) => line.trim()).filter((line) => line !== '' && !line.startsWith(';'));
    assert.deepEqual(lines.slice(1, 22), [
        'Pop $R0',
        '${if} $R0 == 0',
        '${if} ${isUpdated}',
        '${orIf} ${Silent}',
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        `MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "A Jason runtime is running from this application's folder. Stop it with jason runtime stop, then choose OK." IDOK +3`,
        'SetErrorLevel 1',
        'Quit',
        'nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 }"`',
        'Pop $R0',
        '${if} $R0 == 0',
        `MessageBox MB_OK|MB_ICONSTOP "A Jason runtime is still running from this application's folder, so nothing was changed. Stop it with jason runtime stop, then run this again."`,
        'SetErrorLevel 1',
        'Quit',
        '${endIf}',
        '${endIf}',
        '${else}',
        '${ifNot} ${isUpdated}',
        'MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Close Jason if it is open, and stop its runtime with jason runtime stop if it runs, then choose OK." /SD IDOK IDOK +2',
    ]);
    // The look for the runtime comes after the application is closed, so an update's application has quit by then.
    assert.ok(check.indexOf('${JASON_OWN_RUNTIME}') > check.indexOf('Stop-Process'));
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
