import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { ipcModule, processModules, processViolations } from './process-rules.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

function sources(directory: string): string[] {
    const files: string[] = [];
    for (const name of readdirSync(join(root, directory))) {
        const path = join(directory, name);
        if (statSync(join(root, path)).isDirectory()) files.push(...sources(path));
        else if (/\.(?:ts|mts|cts|js|mjs|cjs)$/.test(name)) files.push(path.replaceAll('\\', '/'));
    }
    return files;
}

test('every source under src/ and scripts/ starts processes only where it is named to', () => {
    const files = [...sources('src'), ...sources('scripts')];
    assert.ok(files.length > 0);
    const found = files.flatMap((path) => processViolations(path, readFileSync(join(root, path), 'utf8')));
    assert.deepEqual(found, []);
});

test('every named module is there, so renaming one cannot pass by leaving the rule nothing to hold', () => {
    for (const path of [...Object.keys(processModules), ipcModule]) {
        assert.ok(statSync(join(root, path)).isFile(), `${path} is named by the guard and missing`);
    }
});

test('a spawn planted in a module that is not named is refused', () => {
    const found = processViolations('src/settings.ts', "import { spawn } from 'node:child_process';\nspawn('jason', []);\n");
    assert.equal(found.length, 1);
    assert.match(found[0] ?? '', /only the named modules/);
    assert.notDeepEqual(processViolations('src/main.ts', "const cp = require('child_process');"), []);
    assert.notDeepEqual(processViolations('src/main.ts', "const cp = await import('node:child_process');"), []);
});

test('a shell option planted even in a named module is refused', () => {
    const found = processViolations('src/programs.ts', "import { execFile } from 'node:child_process';\nexecFile('npm', ['i'], { shell: true });\n");
    assert.deepEqual(found, ['src/programs.ts sets a shell option, which nothing in the desktop may']);
});

test('the shell-starting functions and a whole-module import are refused in a named module', () => {
    assert.notDeepEqual(processViolations('src/cli.ts', "import { exec } from 'node:child_process';"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', "import { execSync, execFile } from 'node:child_process';"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', "import * as cp from 'node:child_process';"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', "import { fork } from 'node:child_process';"), []);
    assert.deepEqual(processViolations('src/cli.ts', "import { execFile, spawn } from 'node:child_process';"), []);
});

test('a shell is named only by the macOS login-shell lookup', () => {
    assert.notDeepEqual(processViolations('src/programs.ts', "execFile('cmd.exe', ['/c', 'reply']);"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', 'execFile(process.env.SHELL ?? "", []);'), []);
    assert.notDeepEqual(processViolations('src/shell-path.ts', "execFile(process.env.SHELL ?? '/bin/zsh', ['-ilc', '']);"), []);
    assert.deepEqual(
        processViolations('src/shell-path.ts', "if (process.platform !== 'darwin') return x;\nexecFile(process.env.SHELL ?? '/bin/zsh', ['-ilc', '']);"),
        [],
    );
});

test('utilityProcess is refused everywhere', () => {
    assert.notDeepEqual(processViolations('src/cli.ts', "import { utilityProcess } from 'electron';"), []);
});

test('an IPC handler registered outside the module that checks the sender is refused', () => {
    assert.notDeepEqual(processViolations('src/main.ts', "ipcMain.handle('jason:setup.status', () => null);"), []);
    assert.notDeepEqual(processViolations('src/setup.ts', "ipcMain.on('x', () => {});"), []);
    assert.deepEqual(processViolations('src/ipc.ts', "ipcMain.handle(channel, guarded);"), []);
});
