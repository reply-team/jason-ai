import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { ipcModule, listeningModules, listingCallers, navigationOpeners, processModules, processViolations, runFileCalls, signalLines, startDetachedCallers } from './process-rules.ts';

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
    for (const path of [...Object.keys(processModules), ipcModule, ...Object.keys(listeningModules)]) {
        assert.ok(statSync(join(root, path)).isFile(), `${path} is named by the guard and missing`);
    }
});

test('only the two named modules listen: the permission endpoint and the browser bridge', () => {
    assert.deepEqual(Object.keys(listeningModules).sort(), ['src/browser-bridge.ts', 'src/chat-permission-endpoint.ts']);
    for (const path of Object.keys(listeningModules)) {
        const text = readFileSync(join(root, path), 'utf8');
        assert.ok(text.includes("listen(0, '127.0.0.1'"), `${path} listens on the loopback address, on a port the system picks`);
        assert.ok(/request\.headers\.host !== `127\.0\.0\.1:\$\{/.test(text), `${path} checks the Host`);
    }
});

test('a server or a listen planted in a module that is not named is refused, by any form of the import', () => {
    for (const planted of [
        "import { createServer } from 'node:http';",
        "import http from 'http';",
        "import * as net from 'node:net';",
        "const tls = require('node:tls');",
        "const dgram = await import('dgram');",
        "import { createSecureServer } from 'node:http2';",
        'const server = createServer(serve);',
        "server.listen(0, '127.0.0.1');",
        'const wss = new WebSocketServer({ port: 0 });',
    ]) {
        const found = processViolations('src/setup.ts', planted + '\n');
        assert.ok(found.some((line) => /may listen on a port/.test(line)), planted);
    }
    assert.deepEqual(processViolations('src/browser-bridge.ts', "import { createServer } from 'node:http';\nserver.listen(0, '127.0.0.1');\n"), []);
    // A word that only names a program is no server.
    assert.deepEqual(processViolations('src/chat-literal.ts', "if (program === 'net') return true;\n"), []);
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

test('a shell is named only by the login-shell lookup, which returns before it anywhere but macOS and Linux', () => {
    assert.notDeepEqual(processViolations('src/programs.ts', "execFile('cmd.exe', ['/c', 'reply']);"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', 'execFile(process.env.SHELL ?? "", []);'), []);
    assert.notDeepEqual(processViolations('src/shell-path.ts', "execFile(process.env.SHELL ?? '/bin/zsh', ['-ilc', '']);"), []);
    assert.deepEqual(
        processViolations('src/shell-path.ts', "if (process.platform !== 'darwin' && process.platform !== 'linux') return x;\nexecFile(process.env.SHELL ?? '/bin/sh', ['-ilc', '']);"),
        [],
    );
    assert.notDeepEqual(processViolations('src/shell-path.ts', "if (process.platform === 'win32') return x;\nexecFile(process.env.SHELL ?? '/bin/sh', ['-ilc', '']);"), []);
    // The rule names both systems, so a change to either one's side of it is read here first.
    assert.notDeepEqual(processViolations('src/shell-path.ts', "if (process.platform !== 'darwin') return x;\nexecFile(process.env.SHELL ?? '/bin/zsh', ['-ilc', '']);"), []);
});

test('the Windows sandbox check may name cmd.exe and no other shell, and no other module may name it', () => {
    const joined = "const cmd = win32.join(systemRoot, 'System32', 'cmd.exe');";
    assert.deepEqual(processViolations('src/codex-sandbox.ts', joined), []);
    // cmd.exe is named only in that one join under the system root, given to `cmd`, and only once.
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', `${joined}\nconst other = 'cmd.exe';`), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', "const cmd = win32.join(here, 'System32', 'cmd.exe');"), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', "const shell = win32.join(systemRoot, 'System32', 'cmd.exe');"), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', `${joined}\n${joined}`), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', "const shell = 'powershell.exe';"), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', "const shell = 'cmd';"), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', 'const shell = process.env.ComSpec;'), []);
    assert.notDeepEqual(processViolations('src/chat.ts', "const cmd = win32.join(systemRoot, 'System32', 'cmd.exe');"), []);
    // It runs the check by the runner once, and imports nothing that starts a process itself.
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', "import { spawn } from 'node:child_process';"), []);
    assert.notDeepEqual(processViolations('src/codex-sandbox.ts', 'runFile(a, b, c);\nrunFile(a, b, c);'), []);
    // Its two runs start cmd by the path the check made, with the check's own arguments, and Codex as a turn's host is
    // started; no call starts anything else, and neither is made twice.
    const cmdRun = 'runFile(check.cmd, check.inner, env, given)';
    const codexRun = 'runFile(launch.file, [...launch.prefix, ...args], env, given)';
    assert.deepEqual(processViolations('src/codex-sandbox.ts', `${joined}\n${cmdRun};\n${codexRun};`), []);
    for (const planted of [
        `runFile(cmd, check.inner, env, given);\n${codexRun};`,
        `runFile(check.cmd, ['/c', line], env, given);\n${codexRun};`,
        `${cmdRun};\nrunFile(check.cmd, [...launch.prefix, ...args], env, given);`,
        `${cmdRun};\n${cmdRun};`,
        `${codexRun};\n${codexRun};`,
        'runFile(a, b, c);',
    ]) {
        assert.notDeepEqual(processViolations('src/codex-sandbox.ts', planted), [], planted);
    }
    assert.notDeepEqual(processViolations('src/chat.ts', "import { runFile } from './programs.ts';"), []);
});

test('a require of child_process is refused even in a named module, destructured or whole, since only an import names what it takes', () => {
    assert.notDeepEqual(processViolations('src/cli.ts', "const { exec } = require('node:child_process');"), []);
    assert.notDeepEqual(processViolations('src/cli.ts', "const { execFile } = require('child_process');"), []);
    assert.notDeepEqual(processViolations('src/programs.ts', "const cp = require('child_process');"), []);
    assert.notDeepEqual(processViolations('src/programs.ts', "const cp = await import('node:child_process');"), []);
});

test('a second IPC registration surface, webContents.ipc, is refused outside the module that checks the sender', () => {
    assert.notDeepEqual(processViolations('src/main.ts', "window.webContents.ipc.handle('jason:setup.status', () => null);"), []);
    assert.notDeepEqual(processViolations('src/setup.ts', 'contents.ipc.on("x", () => {});'), []);
    assert.notDeepEqual(processViolations('src/main.ts', 'contents.ipc . handleOnce("x", () => {});'), []);
});

test('shell.openPath is refused everywhere, and shell.openExternal anywhere but main.ts’s navigation lock', () => {
    assert.notDeepEqual(processViolations('src/main.ts', 'void shell.openPath(file);'), []);
    assert.notDeepEqual(processViolations('src/setup.ts', 'void shell.openExternal(url);'), []);
    const locked = 'function lockNavigation(contents) {\n    const leave = (url) => void shell.openExternal(url);\n}\n';
    assert.deepEqual(processViolations('src/main.ts', locked), []);
    assert.notDeepEqual(processViolations('src/main.ts', `${locked}\nfunction other() {\n    void shell.openExternal('https://x');\n}\n`), []);
});

test('inside the navigation lock a URL reaches the system browser only through its two named openers, each once', () => {
    assert.deepEqual(Object.keys(navigationOpeners).sort(), ['leave', 'ownPage']);
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    assert.equal([...main.matchAll(/\bshell\s*\.\s*openExternal\b/g)].length, 2, 'main.ts hands a URL to the system browser in two places');
    assert.match(main, /const ownPage = \(url: string\): Promise<void> => openOwnPage\(url, \{ origin, log, open: \(held\) => shell\.openExternal\(held\) \}\);/, 'ownPage hands the system opener to openOwnPage, which holds the address itself');
    const lock = (body: string) => `function lockNavigation(contents) {\n${body}\n}\n`;
    const both = '    const leave = (url) => void shell.openExternal(url);\n    const ownPage = (url) => void shell.openExternal(url);';
    assert.deepEqual(processViolations('src/main.ts', lock(both)), []);
    for (const planted of [
        `${both}\n    const other = (url) => void shell.openExternal(url);`,
        `${both}\n    const guard = (event, url) => 0;\n    contents.setWindowOpenHandler(({ url }) => void shell.openExternal(url));`,
        '    const ownPage = (url) => { void shell.openExternal(url); void shell.openExternal(url); };',
        '    void shell.openExternal(url);',
    ]) {
        assert.notDeepEqual(processViolations('src/main.ts', lock(planted)), [], planted);
    }
});

test('the opener of the runtime\'s own page is called only by the bridge\'s opening act, and a planted call anywhere else fires the rule', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    assert.match(main, /browser: \(\) => openTab\(\{ origin, cli, bridge: \(\) => bridges\.ensure\(\), open: ownPage, log \}\),/);
    const lock = "function lockNavigation(contents) {\n    const ownPage = (url) => openOwnPage(url, { origin, log, open: (held) => shell.openExternal(held) });\n}\n";
    const wired = `import { openOwnPage, openTab } from './browser-bridge.ts';\n${lock}\nconst { leave: toBrowser, ownPage } = lockNavigation(window.webContents, origin, log);\n    browser: () => openTab({ origin, cli, bridge: () => bridges.ensure(), open: ownPage, log }),\n`;
    assert.deepEqual(processViolations('src/main.ts', wired), []);
    for (const planted of [
        'void ownPage(link);',
        '    openLink: ownPage,',
        "    other: () => openTab({ origin, cli, bridge: () => bridges.ensure(), open: ownPage, log }),",
        'const opener = ownPage;',
        'void openOwnPage(link, deps);',
    ]) {
        assert.notDeepEqual(processViolations('src/main.ts', `${wired}${planted}\n`), [], planted);
    }
    assert.notDeepEqual(processViolations('src/setup.ts', "import { openOwnPage } from './browser-bridge.ts';\nvoid openOwnPage(link, deps);\n"), []);
    assert.deepEqual(processViolations('src/browser-bridge.ts', 'export async function openOwnPage(url, deps) {}\n'), []);
});

test('utilityProcess is refused everywhere', () => {
    assert.notDeepEqual(processViolations('src/cli.ts', "import { utilityProcess } from 'electron';"), []);
});

test('an IPC handler registered outside the module that checks the sender is refused', () => {
    assert.notDeepEqual(processViolations('src/main.ts', "ipcMain.handle('jason:setup.status', () => null);"), []);
    assert.notDeepEqual(processViolations('src/setup.ts', "ipcMain.on('x', () => {});"), []);
    assert.deepEqual(processViolations('src/ipc.ts', "ipcMain.handle(channel, guarded);"), []);
});

test('the chat\'s host module is named, and the shell and whole-module rules hold it as they hold the others', () => {
    assert.ok('src/chat-process.ts' in processModules);
    assert.deepEqual(processViolations('src/chat-process.ts', "import { execFile, spawn } from 'node:child_process';\nspawn(file, args, { detached: true });\n"), []);
    assert.notDeepEqual(processViolations('src/chat-process.ts', "import { spawn } from 'node:child_process';\nspawn(file, args, { shell: true });\n"), []);
    assert.notDeepEqual(processViolations('src/chat-process.ts', "import { exec } from 'node:child_process';"), []);
    assert.notDeepEqual(processViolations('src/chat-process.ts', "execFile('cmd.exe', ['/c', 'taskkill']);"), []);
    assert.notDeepEqual(processViolations('src/chat.ts', "import { spawn } from 'node:child_process';"), []);
});

test("an agent host's model listing is one call: codex app-server, started only by the module that keeps the lists", () => {
    assert.match(processModules['src/chat-process.ts'] ?? '', /an agent host's model listing/);
    const text = readFileSync(join(root, 'src/chat-process.ts'), 'utf8');
    assert.ok(text.includes("export const listingArguments: readonly string[] = ['app-server'];"), 'the listing names its one call');
    assert.ok(/spawnHost\(launch, listingArguments, options\)/.test(text), 'the listing starts with those arguments and no others');
    assert.deepEqual(Object.keys(listingCallers), ['src/model-lists.ts']);
    assert.deepEqual(processViolations('src/model-lists.ts', 'startListing(launch, { cwd, env, onLine });'), []);
    for (const path of ['src/chat.ts', 'src/setup.ts', 'src/main.ts']) {
        assert.notDeepEqual(processViolations(path, "import { startListing } from './chat-process.ts';"), [], path);
    }
});

test('the detached start of Open in is called only by main.ts, and a planted call anywhere else fires the rule', () => {
    assert.deepEqual(Object.keys(startDetachedCallers), ['src/main.ts']);
    const planted = (call: string) => `import { startDetached } from './open-in-start.ts';\n${call}\n`;
    assert.deepEqual(processViolations('src/main.ts', planted('start: (line) => startDetached(line, env),')), []);
    for (const path of ['src/chat.ts', 'src/setup.ts', 'src/open-in.ts', 'src/ipc.ts']) {
        const found = processViolations(path, planted("await startDetached({ file: 'x', args: [] }, env);"));
        assert.equal(found.length, 2, path);
        assert.ok(found.some((line) => /starts a detached program/.test(line)), path);
    }
    // The module itself is named in no other form either: a re-export, a namespace import, a require or a dynamic import.
    for (const form of ["export { startDetached as go } from './open-in-start.ts';", "import * as s from './open-in-start';", "const s = require('./open-in-start.ts');", "const s = await import('./open-in-start.ts');"]) {
        assert.notDeepEqual(processViolations('src/ipc.ts', form + '\n'), [], form);
        assert.ok(processViolations('src/ipc.ts', form + '\n').some((line) => /imports the detached starter's module/.test(line)), form);
    }
    assert.deepEqual(processViolations('src/main.ts', "import { startDetached } from './open-in-start.ts';\n"), []);
});

test('a process is signalled only on two lines: a look whether a process runs, with signal 0, and the end of a chat turn', () => {
    for (const [path, line] of Object.entries(signalLines)) {
        assert.ok(statSync(join(root, path)).isFile(), `${path} is named by the guard and missing`);
        assert.ok(readFileSync(join(root, path), 'utf8').split(/\r?\n/).some((one) => one.trim() === line), `${path} no longer holds ${line}`);
        assert.deepEqual(processViolations(path, `    ${line}\n`), []);
    }
    for (const planted of ['process.kill(pid, 0);\nprocess.kill(pid);', "process.kill(pid, 'SIGTERM');", 'process.kill(pid, 9);', "process['kill'](pid, 0);", 'const { kill } = process;']) {
        assert.notDeepEqual(processViolations('src/own-runtime.ts', planted), [], planted);
    }
    for (const path of ['src/main.ts', 'src/uninstall-start.ts', 'src/updates.ts']) {
        const found = processViolations(path, 'if (alive) process.kill(pid, 0);');
        assert.equal(found.length, 1, path);
        assert.match(found[0] ?? '', /signals a process/);
    }
    assert.notDeepEqual(processViolations('src/chat-process.ts', '(target) => process.kill(target, 0),'), []);
});

test('the program runner is called with a command line of a module\'s own only by the runner itself, by a set-up chat\'s one naming run and by the Windows check of Codex\'s sandbox', () => {
    assert.deepEqual(runFileCalls, { 'src/programs.ts': 1, 'src/chat-naming.ts': 1, 'src/codex-sandbox.ts': 2 });
    assert.match(processModules['src/programs.ts'] ?? '', /an agent host's one naming run of a set-up chat/);
    for (const [path, calls] of Object.entries(runFileCalls)) {
        const text = readFileSync(join(root, path), 'utf8');
        assert.equal([...text.matchAll(/\brunFile\s*\(/g)].length - [...text.matchAll(/function\s+runFile\s*\(/g)].length, calls, `${path} no longer makes its ${calls} call`);
    }
    const planted = processViolations('src/chat.ts', "import { runFile } from './programs.ts';\nawait runFile(file, ['--yes'], env);\n");
    assert.deepEqual(planted.map((line) => line.replace(/^src\/chat\.ts /, '').split(';')[0]), ['calls the program runner 1 times', 'imports the program runner']);
    assert.notDeepEqual(processViolations('src/chat-naming.ts', 'await runFile(a, b, c);\nawait runFile(a, b, c);\n'), [], 'a second run in the naming module');
    assert.deepEqual(processViolations('src/chat-naming.ts', 'await runFile(a, b, c);\n'), []);
    assert.deepEqual(processViolations('src/chat.ts', 'const at = paths.runFile;\nreadRunFile(x);\n'), [], 'the run file is no call of the runner');
});

test('the runner is imported only by the two modules that call it: no other module takes runFile, under any name, or programs.ts whole', () => {
    for (const planted of [
        "import { runFile as start } from './programs.ts';\nawait start(file, [], env);\n",
        "import { programLaunch, runFile } from './programs';",
        "import * as programs from './programs.ts';\nawait programs['run' + 'File'](file, [], env);\n",
        "const { runFile: start } = await import('./programs.ts');",
        "const programs = require('./programs.ts');",
        "export { runFile } from './programs.ts';",
    ]) {
        assert.notDeepEqual(processViolations('src/chat.ts', planted), [], planted);
    }
    assert.deepEqual(processViolations('src/chat.ts', "import { programLaunch, type Programs } from './programs.ts';"), []);
    assert.deepEqual(processViolations('src/setup.ts', "import { isProgramName, type ProgramName, type Programs, type RunResult } from './programs.ts';"), []);
    assert.deepEqual(processViolations('src/chat-naming.ts', "import { runFile, type RunResult } from './programs.ts';\nawait runFile(a, b, c);\n"), []);
});
