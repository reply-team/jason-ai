// The candidate-version proof: the executable the desktop carries, given the feed the desktop gives it, fetches its own
// candidate's release directory rather than the newest release's. It runs the real executable, so it is opt-in:
//
//   JASON_UI_ZIP=<a jason-ui.zip packaged with JASON_VERSION set to the candidate> npm test
//
// with the bundle prepared (`npm run prepare:bundle`), or JASON_EXE naming another candidate executable.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { cliAt } from '../src/cli.ts';
import { feedArguments } from '../src/installation.ts';

const desktop = join(dirname(fileURLToPath(import.meta.url)), '..');
const bundle = join(desktop, 'bundle');
const executable = process.env.JASON_EXE ?? join(bundle, process.platform === 'win32' ? 'jason.exe' : 'jason');
const zip = process.env.JASON_UI_ZIP;

test('the bundled executable resolves its own candidate’s release directory from the desktop’s feed', { skip: !zip || !existsSync(executable) ? 'set JASON_UI_ZIP and prepare the bundle to run it' : false }, async (t) => {
    const cli = (env: Record<string, string>) => cliAt(executable, env);
    const data = mkdtempSync(join(tmpdir(), 'jason-desktop-candidate-'));
    t.after(() => rmSync(data, { recursive: true, force: true }));
    const env: Record<string, string> = { ...(process.env as Record<string, string>), JASON_DATA_DIR: join(data, 'data'), JASON_Harness__Root: join(data, 'harness') };

    const said = (await cli(env).run(['--version'])).stdout.trim();
    const candidate = /\d+\.\d+\.\d+-[0-9A-Za-z.-]+/.exec(said)?.[0];
    assert.ok(candidate, `the executable says ${said}, which is no candidate's version`);
    assert.doesNotMatch(candidate, /-dev/);

    const bytes = readFileSync(zip!);
    const sums = `${createHash('sha256').update(bytes).digest('hex')}  jason-ui.zip\n`;
    const asked: string[] = [];
    const server = createServer((request, response) => {
        const path = request.url ?? '';
        asked.push(path);
        const own = `/releases/download/v${candidate}/`;
        if (path === `${own}checksums.txt`) response.end(sums);
        else if (path === `${own}jason-ui.zip`) response.end(bytes);
        else {
            response.statusCode = 404;
            response.end();
        }
    });
    await new Promise<void>((done) => server.listen(0, '127.0.0.1', done));
    t.after(() => server.close());
    const port = (server.address() as { port: number }).port;

    const feed = `http://127.0.0.1:${port}/releases/latest/download/manifest.json`;
    const result = await cli(env).run(['ui', 'install', ...feedArguments({ feed, skillsDeployedFor: null, theme: null })]);
    console.log(`candidate ${candidate}; asked ${asked.join(', ')}; exit ${result.exit}; ${result.stdout.trim() || result.stderr.trim()}`);
    assert.equal(result.exit, 0, result.stderr);
    assert.deepEqual(asked, [`/releases/download/v${candidate}/checksums.txt`, `/releases/download/v${candidate}/jason-ui.zip`]);
    assert.equal(asked.some((path) => path.includes('/latest/')), false);
});
