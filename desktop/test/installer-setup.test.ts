// What the Windows installer does once the files are in place: it sets the machine up through the jason the application
// carries, `jason install` with no flags, at every install and every update, silent or not. That routine puts jason in the
// terminal, gives every agent host found its skills and retires an older command-line install; it writes nothing under
// the data directory without --roles, so a person's first start still opens on the Demo, and it starts no process that
// outlives it, so a script that waits for the installer's whole process tree is not held. Nothing here starts a runtime:
// the caller that asked for an install starts one where the routine stopped one. The installer is not run here; these
// rows hold its text and electron-builder's template that runs it.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { configuration } from '../electron-builder.config.mjs';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const include = configuration({ version: '0.49.0' }).nsis.include as string;
const script = readFileSync(join(desktop, include), 'utf8');
const section = readFileSync(join(desktop, 'node_modules', 'app-builder-lib', 'templates', 'nsis', 'installSection.nsh'), 'utf8');

/** The lines nsis runs of a macro the script defines, without blank lines and comments. */
function macroLines(name: string): string[] {
    const found = new RegExp(`^!macro ${name}\\b.*$`, 'm').exec(script);
    assert.ok(found !== null, `${include} defines no ${name}.`);
    const end = script.indexOf('!macroend', found.index);
    assert.ok(end > found.index, `${name} has no !macroend.`);
    return script
        .slice(found.index, end)
        .split(/\r?\n/)
        .slice(1)
        .map((line) => line.trim())
        .filter((line) => line !== '' && !line.startsWith(';'));
}

/** The carried jason, as the installer's own check names it. */
const carried = '$INSTDIR\\resources\\bundle\\jason.exe';

test('the installer sets the machine up through the carried jason install, with no flags', () => {
    const lines = macroLines('customInstall');
    const calls = lines.filter((line) => /\bExec/i.test(line));
    assert.deepEqual(calls, [`nsExec::Exec '"${carried}" install'`], 'the installer runs anything but the carried jason install, or runs it with flags');
    // The check before the install looks for this same executable by this same path, so the two cannot drift apart.
    assert.ok(script.includes(`t "JASON_DESKTOP_RUNTIME", t "${carried}"`), 'the installer\'s own check names another carried jason');
});

test('the setup runs hidden and is waited for, so the installer ends only once it has', () => {
    const lines = macroLines('customInstall');
    // nsExec::Exec runs a console program with no window and waits for it; Exec, ExecShell and ExecWait would open a
    // console window in front of a person, and the first two would not wait.
    assert.ok(!lines.some((line) => /^(Exec|ExecShell|ExecWait|ExecShellWait)\b/i.test(line)), lines.join('\n'));
    const call = lines.findIndex((line) => line.startsWith('nsExec::Exec'));
    assert.equal(lines[call + 1], 'Pop $R0', 'what nsExec left on the stack is not taken off it');
});

test('the setup runs at every install and every update, silent or not', () => {
    const lines = macroLines('customInstall');
    assert.ok(!lines.some((line) => /isUpdated|Silent|isForceRun|\$\{if|\$\{ifNot/i.test(line)), `the setup is conditional:\n${lines.join('\n')}`);
    // electron-builder's install section runs customInstall once the files are in place and before it starts the
    // application or says it succeeded, and under no condition of its own but that the macro is defined.
    const files = section.indexOf('!insertmacro installApplicationFiles');
    const inserted = section.indexOf('!ifmacrodef customInstall\n  !insertmacro customInstall\n!endif');
    const started = section.indexOf('!insertmacro doStartApp', inserted);
    const succeeded = section.indexOf('!insertmacro quitSuccess', inserted);
    assert.ok(files >= 0 && inserted > files, 'electron-builder no longer runs customInstall after the files are in place');
    assert.ok(started > inserted && succeeded > inserted, 'electron-builder starts the application or ends before customInstall');
});

test('a setup that did not get everything done never fails the install, and nothing here starts a runtime', () => {
    const lines = macroLines('customInstall');
    // The files are in place by now: an install that ended with an error here would leave the application installed
    // and say it was not. The routine's own report is what the install scripts read, and the application's start runs
    // it again where its last run was for an older version.
    assert.ok(!lines.some((line) => /SetErrorLevel|Quit|Abort|MessageBox/i.test(line)), lines.join('\n'));
    assert.ok(!lines.some((line) => /runtime/i.test(line)), 'the installer starts or stops a runtime itself');
});
