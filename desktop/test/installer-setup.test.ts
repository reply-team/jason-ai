// What the Windows installer does once the files are in place: it sets the machine up through the jason the application
// carries, `jason install` with no flags, at every install and every update, silent or not. That routine puts jason in the
// terminal, gives Claude Code and Codex, where each is found, their skills and retires an older command-line install; it writes nothing under
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
    // Nothing before the setup decides whether it runs; the one test after it only says what it answered.
    const ran = lines.indexOf('Pop $R0');
    assert.ok(!lines.slice(0, ran).some((line) => /isUpdated|Silent|isForceRun|\$\{if|\$\{ifNot/i.test(line)), `the setup is conditional:\n${lines.join('\n')}`);
    assert.ok(!lines.some((line) => /isUpdated|Silent|isForceRun/i.test(line)), lines.join('\n'));
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

test('a setup that did not set everything up is said in one line of a run a person watches, naming jason install', () => {
    const lines = macroLines('customInstall');
    const ran = lines.indexOf('Pop $R0');
    assert.deepEqual(lines.slice(ran + 1), [
        // A string test: nsExec leaves "error" or "timeout" where the program could not be run, which is no answer of 0.
        '${if} $R0 S!= 0',
        `DetailPrint "jason install did not set everything up (it answered $R0). Run ${carried} install in a terminal to see what it says."`,
        '${endIf}',
    ]);
});

test('a runtime the install stopped is started again by whoever ran the installer, never by the installer itself', () => {
    // The rule every caller keeps - a runtime that answered before the install and none after is started again from the
    // carried jason - is not the installer's: a runtime it started would be its child and outlive it, and a script that
    // waits for the installer's whole process tree would wait for it for ever. A person's run and the window's own update
    // (--force-run) start the application once the setup is done, and its start brings the runtime up as every start of
    // it does; a run a terminal asked for - the install script's, or jason update apply's - is that command's to restart.
    const nsis = configuration({ version: '0.49.0' }).nsis as { oneClick?: boolean; runAfterFinish?: boolean };
    assert.equal(nsis.oneClick, true, 'the installer is no longer one-click');
    assert.notEqual(nsis.runAfterFinish, false, 'the installer no longer starts the application when it ends');
    const oneClick = section.indexOf('!ifdef ONE_CLICK', section.indexOf('!ifmacrodef customInstall'));
    const runAfter = section.indexOf('!ifdef RUN_AFTER_FINISH', oneClick);
    assert.ok(oneClick >= 0 && runAfter > oneClick, 'electron-builder no longer decides under RUN_AFTER_FINISH whether to start the application');
    assert.match(section.slice(runAfter), /^!ifdef RUN_AFTER_FINISH\s+\$\{ifNot\} \$\{Silent\}\s+\$\{orIf\} \$\{isForceRun\}\s+!insertmacro doStartApp/,
        'a one-click installer no longer starts the application after a run that is not silent, or one with --force-run');
    assert.ok(script.includes('Nothing here starts a runtime'), 'the include no longer says that it starts no runtime');
});
