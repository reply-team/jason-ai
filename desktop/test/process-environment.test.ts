import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cliAt } from '../src/cli.ts';
import { pathOf, programsOn } from '../src/programs.ts';
import { markersIn, plantedConfiguration, plantedMarkers } from './planted-session.ts';

// A desktop started from inside a Claude Code session: every process it starts, through either module that starts one,
// is given that environment less the session's markers. The child here is Node, printing the names it was given.

const printNames = 'process.stdout.write(JSON.stringify(Object.keys(process.env)))';
const planted = (): Record<string, string> => {
    const env: Record<string, string> = {};
    for (const [name, value] of Object.entries(process.env)) if (value !== undefined) env[name] = value;
    return { ...env, ...plantedMarkers, claudecode: '1', ...plantedConfiguration };
};

function assertClean(names: readonly string[]): void {
    assert.deepEqual(markersIn(names), []);
    for (const name of Object.keys(plantedConfiguration)) assert.ok(names.includes(name), `${name} was not kept`);
}

test('the bundled jason is started without a parent Claude Code session\'s markers, and with a person\'s configuration', async () => {
    const result = await cliAt(process.execPath, planted(), ['-e', printNames]).run([]);
    assert.equal(result.exit, 0, result.stderr);
    assertClean(JSON.parse(result.stdout) as string[]);
});

test('a program the desktop runs is started without a parent Claude Code session\'s markers, and with a person\'s configuration', async () => {
    const env = planted();
    const programs = programsOn(env);
    assert.ok(programs.find('node') !== null, `no node on ${pathOf(env)}`);
    const result = await programs.run('node', ['-e', printNames]);
    assert.equal(result.exit, 0, result.stderr);
    assertClean(JSON.parse(result.stdout) as string[]);
});
