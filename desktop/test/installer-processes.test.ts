// Which running programs the installer and the uninstaller close. The application is Jason.exe and the runtime it
// carries is jason.exe, and Windows does not tell the two names apart, so a check that goes by the image name closes
// every jason.exe on the machine, a runtime of another installation with its work included. electron-builder's own check
// does that where PowerShell is not available, and where it is, closes every program whose path merely begins with the
// installation folder, the runtime this application carries among them. These rows hold the check that replaces it to
// the one file it may close, the application's own executable by its exact path; neither the installer nor the
// uninstaller is run here.

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const script = readFileSync(join(desktop, 'installer', 'uninstall.nsh'), 'utf8');
const templates = join(desktop, 'node_modules', 'app-builder-lib', 'templates', 'nsis');
const template = (file: string) => readFileSync(join(templates, file), 'utf8');

/** The body of a macro the script defines, from `!macro <name>` to its `!macroend`. */
function macro(name: string): string {
    const start = script.indexOf(`!macro ${name}\n`);
    assert.ok(start >= 0, `installer/uninstall.nsh defines no ${name}.`);
    const end = script.indexOf('!macroend', start);
    assert.ok(end > start, `${name} has no !macroend.`);
    return script.slice(start, end);
}

/** The PowerShell filter the script defines, as PowerShell reads it once nsis has turned each `$$` into `$`. */
function filter(): string {
    const found = /^!define JASON_OWN_APP `([^`]+)`$/m.exec(script);
    assert.ok(found !== null, 'installer/uninstall.nsh defines no JASON_OWN_APP filter.');
    return found[1]!.replaceAll('$$', '$');
}

test('the pinned template closes running programs through a custom check when the script defines one', () => {
    const include = template(join('include', 'allowOnlyOneInstallerInstance.nsh'));
    assert.match(include, /!macro CHECK_APP_RUNNING\b[\s\S]*?!ifmacrodef customCheckAppRunning\s+!insertmacro customCheckAppRunning\s+!else\s+!insertmacro IS_POWERSHELL_AVAILABLE\s+!insertmacro _CHECK_APP_RUNNING\s+!endif/);
    // The installer and the uninstaller reach a close only through CHECK_APP_RUNNING.
    for (const file of ['installSection.nsh', 'uninstaller.nsh', 'installer.nsi', 'oneClick.nsh', 'common.nsh']) {
        const text = template(file);
        assert.doesNotMatch(text, /KILL_PROCESS|taskkill|Stop-Process|nsProcess::/, `${file} closes a program outside CHECK_APP_RUNNING.`);
    }
    assert.match(template('installSection.nsh'), /!insertmacro CHECK_APP_RUNNING/);
    assert.match(template('uninstaller.nsh'), /!insertmacro CHECK_APP_RUNNING/);
});

test('the custom check closes the application by its exact path and never a program by its name', () => {
    const check = macro('customCheckAppRunning');
    assert.match(check, /SetEnvironmentVariable\(t "JASON_DESKTOP_APP", t "\$INSTDIR\\\$\{APP_EXECUTABLE_FILENAME\}"\)/);
    for (const name of [/IMAGENAME/i, /taskkill/i, /tasklist/i, /\/IM\b/, /StartsWith/i, /nsProcess/i, /-like\b/i, /-match\b/i, /\$INSTDIR\\resources/i]) {
        assert.doesNotMatch(script, name, `installer/uninstall.nsh goes by ${name}.`);
    }
    // Every PowerShell command it runs reads the processes through the one filter, and only the second one stops any.
    const commands = [...check.matchAll(/nsExec::Exec `([^`]*)`/g)].map((found) => found[1]!);
    assert.equal(commands.length, 2);
    for (const command of commands) assert.ok(command.includes('${JASON_OWN_APP}'), `A command reads processes another way: ${command}`);
    assert.doesNotMatch(commands[0]!, /Stop-Process/);
    assert.match(commands[1]!, /^"\$PowerShellPath" -NoProfile -NonInteractive -Command "\$\{JASON_OWN_APP\} \| ForEach-Object \{ Stop-Process -Id \$\$_\.ProcessId -Force \}"$/);
    assert.equal(filter(), 'Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase) }');
});

test('the uninstall an update runs closes the application without asking; a person is asked first', () => {
    const check = macro('customCheckAppRunning');
    assert.match(check, /\$\{ifNot\} \$\{isUpdated\}\s+MessageBox MB_OKCANCEL\|MB_ICONEXCLAMATION "\$\(appRunning\)" \/SD IDOK IDOK \+2\s+Quit\s+\$\{endIf\}/);
});

test('the filter finds a running program by its exact path, whatever the case, and not by a path it only begins', { skip: process.platform !== 'win32' && 'the process query is Windows’ own' }, () => {
    const running = (file: string) => {
        try {
            execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `if (@(${filter()}).Count -gt 0) { exit 0 } else { exit 1 }`], {
                env: { ...process.env, JASON_DESKTOP_APP: file },
                stdio: 'ignore',
                windowsHide: true,
            });
            return true;
        } catch {
            return false;
        }
    };
    const self = process.execPath;
    assert.equal(running(self), true);
    assert.equal(running(self.toUpperCase()), true);
    assert.equal(running(dirname(self)), false);
    assert.equal(running(self.slice(0, -1)), false);
    assert.equal(running(`${self}.old`), false);
    assert.equal(running(join(dirname(self), 'jason', basename(self))), false);
});
