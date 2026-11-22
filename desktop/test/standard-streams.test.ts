import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

// Outside GNOME and KDE, xdg-open runs Jason Desktop with the pipes `jason ui open` gave it, and that command ends first,
// so a later write to the desktop's own stdout or stderr meets a pipe nobody reads. The child here is Node, as the
// desktop's main process is: it writes to both streams itself, as the `debug` package does to stderr, after its parent
// has closed their reading ends, and says it is still alive over its IPC channel.

const streams = new URL('../src/standard-streams.ts', import.meta.url).href;

test('a write to the main process\'s own stdout or stderr that nobody reads any more is dropped, never an uncaught exception', async () => {
    const script = [
        `import ${JSON.stringify(streams)};`,
        "process.on('message', () => {",
        "    process.stdout.write('a line nobody reads\\n');",
        "    process.stderr.write('a line nobody reads\\n');",
        "    setTimeout(() => process.send('alive', () => process.exit(0)), 200);",
        '});',
        "process.send('ready');",
    ].join('\n');
    const child = spawn(process.execPath, ['--experimental-strip-types', '--no-warnings=ExperimentalWarning', '--input-type=module', '-e', script], {
        stdio: ['ignore', 'pipe', 'pipe', 'ipc'],
    });
    const { stdout, stderr } = child;
    assert.ok(stdout !== null && stderr !== null);
    const said: unknown[] = [];
    child.on('message', (message) => said.push(message));
    child.once('message', () => {
        let open = 2;
        const closed = () => (--open === 0 ? child.send('write') : undefined);
        stdout.once('close', closed);
        stderr.once('close', closed);
        stdout.destroy();
        stderr.destroy();
    });
    const exit = await new Promise<number | null>((resolve) => child.once('exit', (code) => resolve(code)));
    assert.equal(exit, 0, 'the child ended on a failed write');
    assert.deepEqual(said, ['ready', 'alive']);
});

// Imports are evaluated in their order before the module's own statements, so only the first import that loads a module
// comes before whatever another one runs as it loads; a type-only import loads nothing.
test('the main process imports it before anything else, so nothing it loads or runs writes first', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.equal(main.match(/^import (?!type ).*$/m)?.[0], "import './standard-streams.ts';");
});
