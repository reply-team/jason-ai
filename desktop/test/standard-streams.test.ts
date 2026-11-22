import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

// Outside GNOME and KDE, xdg-open runs Jason Desktop with the pipes `jason ui open` gave it, and that command ends first,
// so a later write to the desktop's own stderr meets a pipe nobody reads. The child here is Node, as the desktop's main
// process is: it writes to its stderr itself, as the `debug` package does, after its parent has closed the reading end.

const streams = new URL('../src/standard-streams.ts', import.meta.url).href;

test('a write to the main process\'s own stderr that nobody reads any more is dropped, never an uncaught exception', async () => {
    const script = [
        `import ${JSON.stringify(streams)};`,
        "process.on('message', () => {",
        "    process.stderr.write('a line nobody reads\\n');",
        "    setTimeout(() => { process.stdout.write('alive'); process.exit(0); }, 200);",
        '});',
        "process.send('ready');",
    ].join('\n');
    const child = spawn(process.execPath, ['--experimental-strip-types', '--no-warnings=ExperimentalWarning', '--input-type=module', '-e', script], {
        stdio: ['ignore', 'pipe', 'pipe', 'ipc'],
    });
    const { stdout, stderr } = child;
    assert.ok(stdout !== null && stderr !== null);
    let printed = '';
    stdout.on('data', (chunk: Buffer) => (printed += chunk.toString()));
    child.once('message', () => {
        stderr.once('close', () => child.send('write'));
        stderr.destroy();
    });
    const exit = await new Promise<number | null>((resolve) => child.once('exit', (code) => resolve(code)));
    assert.equal(exit, 0, 'the child ended on the failed write');
    assert.equal(printed, 'alive');
});

// Imports are evaluated in their order before the module's own statements, so only the first import comes before
// whatever another one runs as it loads.
test('the main process imports it before anything else, so nothing it loads or runs writes first', () => {
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    assert.equal(main.match(/^import .*$/m)?.[0], "import './standard-streams.ts';");
});
