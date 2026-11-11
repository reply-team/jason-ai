// The desktop never runs without Chromium's sandbox: a start that carries --no-sandbox is refused, saying why, before
// anything else runs. An AppImage's own launcher adds the switch where the system refuses unprivileged user namespaces,
// so that start is told the deb is the package to install.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { sandboxRefusal } from '../src/sandbox.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

test('a start with the sandbox runs; one without it is refused on every form, the AppImage told to install the deb', () => {
    assert.equal(sandboxRefusal(false, { form: 'appimage', file: '/home/a/jason-desktop-linux-x64.AppImage' }), null);
    assert.equal(sandboxRefusal(false, { form: 'deb' }), null);
    assert.equal(sandboxRefusal(false, null), null);

    const appImage = sandboxRefusal(true, { form: 'appimage', file: '/home/a/jason-desktop-linux-x64.AppImage' });
    assert.match(appImage ?? '', /^This system does not let Jason run in its sandbox, so Jason did not start\./);
    assert.match(appImage ?? '', /sudo apt install \.\/jason-desktop-linux-x64\.deb/);
    for (const form of [{ form: 'deb' } as const, { form: 'folder' } as const, null]) {
        assert.equal(sandboxRefusal(true, form), 'Jason does not run without its sandbox, so it did not start. Start it without --no-sandbox.');
    }
});

test('the refusal comes before every other start, whatever the platform, and quits without a window', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const refusal = main.indexOf("const unsandboxed = sandboxRefusal(app.commandLine.hasSwitch('no-sandbox'), linuxForm());");
    assert.ok(refusal > 0, 'main.ts never asks whether the start carries --no-sandbox');
    assert.ok(refusal < main.indexOf('const uninstalling = uninstallStart('), 'the refusal comes after another start is decided');
    assert.match(main, /if \(unsandboxed !== null\) \{\n\s+void app\.whenReady\(\)\.then\(\(\) => \{\n\s+dialog\.showErrorBox\('Jason', unsandboxed\);\n\s+app\.exit\(1\);/);
});
