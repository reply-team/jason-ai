// The desktop never runs without Chromium's sandbox: a start that carries --no-sandbox, however the switch arrived, is
// refused, saying why in a native dialog shown once Electron is ready and no window made, and quits with a non-zero exit
// and one log line, before anything else of the start is decided. An AppImage's own launcher adds the switch where the
// system refuses unprivileged user namespaces, so that start is told the deb is the package to install.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { refuseStart, sandboxRefusal } from '../src/sandbox.ts';
import { debFile } from '../src/updates.ts';
import type { Logger } from '../src/log.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const appImage = { form: 'appimage', file: '/home/a/jason-desktop-linux-x64.AppImage' } as const;

test('a start with the sandbox runs, on every form and platform', () => {
    for (const form of [appImage, { form: 'deb' } as const, { form: 'folder' } as const, null]) {
        assert.equal(sandboxRefusal(false, ['/opt/Jason/jason-desktop-app', '--opened-at-login'], form), null);
    }
});

test('the switch is seen however it arrived: as Electron holds it, or in argv alone', () => {
    const exe = '/tmp/.mount_jason/jason-desktop-app';
    assert.notEqual(sandboxRefusal(true, [exe], null), null, 'held by Electron, not in argv');
    assert.notEqual(sandboxRefusal(false, [exe, '--no-sandbox'], null), null, 'in argv, not held by Electron');
    assert.notEqual(sandboxRefusal(false, [exe, '--no-sandbox=1'], null), null, 'with a value');
    assert.equal(sandboxRefusal(false, [exe, '--no-sandboxes', '--sandbox'], null), null, 'other switches are not it');
});

test('an AppImage refused is told to install the deb; every other start is told the sandbox is required', () => {
    const said = sandboxRefusal(true, [], appImage);
    assert.equal(said?.form, 'appimage');
    assert.equal(
        said?.message,
        `This system does not let Jason run in its sandbox, so Jason did not start. Install the deb package instead, with sudo apt install ./${debFile}.`,
    );
    for (const [form, named] of [[{ form: 'deb' } as const, 'deb'], [{ form: 'folder' } as const, 'folder'], [null, 'none']] as const) {
        assert.deepEqual(sandboxRefusal(true, [], form), { form: named, message: 'Jason does not start without its sandbox.' });
    }
});

test('a refused start waits for Electron, logs one line with no path, shows the dialog, and exits 1, in that order', async () => {
    const steps: string[] = [];
    let ready!: () => void;
    const whenReady = new Promise<void>((resolve) => {
        ready = resolve;
    });
    const lines: unknown[] = [];
    const log = { warn: (name: string, fields: unknown) => lines.push([name, fields]), info: () => assert.fail('info'), error: () => assert.fail('error') } as unknown as Logger;
    const done = refuseStart(
        { form: 'appimage', message: 'm' },
        {
            whenReady: () => whenReady.then(() => steps.push('ready')),
            logger: () => {
                steps.push('logger');
                return log;
            },
            showErrorBox: (title, text) => steps.push(`dialog ${title}: ${text}`),
            exit: (code) => steps.push(`exit ${code}`),
        },
    );
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(steps, [], 'something ran before Electron was ready');
    ready();
    await done;
    assert.deepEqual(steps, ['ready', 'logger', 'dialog Jason AI Harness: m', 'exit 1']);
    assert.deepEqual(lines, [['sandbox_refused', { Form: 'appimage' }]]);
});

test('before the refusal, main.ts runs nothing: every top-level statement above it is an import, a declaration, or the one let of the window to show', () => {
    const file = join(root, 'src', 'main.ts');
    const source = ts.createSourceFile(file, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true);
    const decision = source.statements.findIndex(
        (statement) => ts.isVariableStatement(statement) && statement.declarationList.declarations.some((declaration) => declaration.name.getText(source) === 'unsandboxed'),
    );
    assert.ok(decision > 0, 'main.ts declares no unsandboxed');
    for (const statement of source.statements.slice(0, decision)) {
        const text = statement.getText(source).split('\n')[0] ?? '';
        if (ts.isImportDeclaration(statement) || ts.isFunctionDeclaration(statement) || ts.isInterfaceDeclaration(statement) || ts.isTypeAliasDeclaration(statement)) continue;
        assert.ok(
            ts.isVariableStatement(statement) && text.startsWith('let showWindow: () => void = () => bringForward('),
            `main.ts runs a statement before the refusal: ${text}`,
        );
    }
});

test('the refusal is the first thing the main process decides, and nothing else of a start runs beside it', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const decided = main.indexOf("const unsandboxed = sandboxRefusal(app.commandLine.hasSwitch('no-sandbox'), process.argv, linuxForm());");
    assert.ok(decided > 0, 'main.ts never asks whether the start carries --no-sandbox');
    // Everything of a start comes after it: the uninstall and stop-runtime starts, the single-instance lock, and run(),
    // which copies the bundle, calls jason and makes the window. Electron's own paths are set by both branches.
    for (const later of ['uninstallStart(process.argv', 'linkIn(process.argv', 'app.requestSingleInstanceLock(', 'app.whenReady().then(() => releasedAfter(logHold, () => run(links, installationWasNew)))', 'new BrowserWindow(']) {
        const at = main.indexOf(later);
        assert.ok(at > 0, later);
        if (later !== 'new BrowserWindow(') assert.ok(at > decided, `${later} comes before the refusal`);
    }
    // The refusal's branch does nothing but refuse, and the rest of the start is the other branch.
    const branch = /if \(unsandboxed !== null\) \{\n([\s\S]*?)\n\} else \{\n    startDesktop\(\);\n\}/.exec(main);
    assert.ok(branch, 'the refusal is not one branch with the start as the other');
    assert.doesNotMatch(branch[1] ?? '', /run\(|cliAt|carriedBundle|BrowserWindow|requestSingleInstanceLock|uninstallStart|stopRuntimeStart/);
    assert.match(branch[1] ?? '', /^    keepElectronUnderData\(\);\n    void refuseStart\(unsandboxed, \{/);
    // The window is made only inside run(), which only the start's branch reaches.
    assert.ok(main.indexOf('new BrowserWindow(') > main.indexOf('async function run('));
});
