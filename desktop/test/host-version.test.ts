// The oldest Claude Code the chat runs a turn on: how its version is read from what `claude --version` printed, how two
// versions compare, and the sentence a host below that version is listed and refused with.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { claudeFloor, compareVersions, floorSentence, printedVersion, sharingVersionReads, versionLimitMs } from '../src/host-version.ts';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { programsOn, type Programs, type RunResult } from '../src/programs.ts';
import { bench } from './harness.ts';

const answered = (stdout: string, stderr = '', exit = 0) => ({ exit, stdout, stderr });

test('the version is the first three dotted numbers on a line naming Claude Code, else anywhere in what was printed, on either stream', () => {
    assert.equal(printedVersion(answered('2.1.300 (Claude Code)\n')), '2.1.300');
    assert.equal(printedVersion(answered('Claude Code version v2.1.300, build 7f3e, ready\n')), '2.1.300');
    assert.equal(printedVersion(answered('', 'warning: something\n2.1.300 (Claude Code)\n')), '2.1.300', 'on standard error alone');
    assert.equal(printedVersion(answered('node 22.11.0 found\n', '2.1.200 (Claude Code)\n')), '2.1.200', 'another program’s version before it does not win');
    assert.equal(printedVersion(answered('2026.11.10 12:00 notice\n2.1.200 (CLAUDE CODE)\n')), '2.1.200', 'nor a date, and the name in any letter case');
    assert.equal(printedVersion(answered('listening on 127.0.0.1\n', 'claude code 2.1.200\n')), '2.1.200');
    assert.equal(printedVersion(answered('node 22.11.0 found\n', '2.1.300\n')), '22.11.0', 'with no line naming Claude Code, the first group anywhere');
    assert.equal(printedVersion(answered('Claude Code\n2.1.300\n')), '2.1.300', 'a line naming it without a number leaves the first group anywhere');
    assert.equal(printedVersion(answered('2.1.300-beta.2 (Claude Code)\n')), '2.1.300-beta.2', 'a pre-release part is kept');
    assert.equal(printedVersion(answered('1.2.3.4\n')), '1.2.3');
    assert.equal(printedVersion(answered('Claude Code\n')), null, 'no three dotted numbers');
    assert.equal(printedVersion(answered('2.1\n')), null);
});

test('a version is read as printed, with a leading v, a pre-release, a leading zero or two digits, and compared to the floor as numbers', () => {
    assert.equal(printedVersion(answered('v2.1.256-beta.1 (Claude Code)\n')), '2.1.256-beta.1');
    assert.match(floorSentence('2.1.256-beta.1') ?? '', /^Claude Code 2\.1\.256-beta\.1 is older than 2\.1\.257/);
    assert.equal(printedVersion(answered('2.01.300 (Claude Code)\n')), '2.01.300');
    assert.equal(floorSentence('2.01.300'), null, 'a leading zero reads as the number: 2.1.300');
    assert.equal(printedVersion(answered('10.0.0 (Claude Code)\n')), '10.0.0');
    assert.equal(floorSentence('10.0.0'), null);
    assert.equal(printedVersion(answered('', '')), null, 'nothing printed on either stream');
    assert.equal(floorSentence(printedVersion(answered('', ''))), floorSentence(null));
});

test('a version is looked for in the first 4 KiB of each stream only, so a long run of digits is read at once, and as no version', () => {
    const digits = '9'.repeat(100_000);
    const started = Date.now();
    assert.equal(printedVersion(answered(digits, digits)), null);
    const took = Date.now() - started;
    assert.ok(took < 1000, `read in ${took} ms`);
    assert.equal(printedVersion(answered(`${'x'.repeat(4096)}\n2.1.300 (Claude Code)\n`)), null, 'a version past 4 KiB of standard output is not read');
    assert.equal(printedVersion(answered('x'.repeat(5000), '2.1.300 (Claude Code)\n')), '2.1.300', 'standard error has its own 4 KiB');
});

test('a version is not read from a program that ended with a code other than zero, or was stopped at its time limit', () => {
    assert.equal(printedVersion(answered('2.1.300 (Claude Code)\n', 'then it failed\n', 1)), null, 'a non-zero exit');
    assert.equal(printedVersion(answered('', 'spawnSync claude ETIMEDOUT', -1)), null, 'a timeout, which the runner reports as -1');
    assert.equal(printedVersion(answered('2.1.300\n', '', -1)), null, 'stopped at its limit after printing');
});

test('the read is bounded, and generously: a host’s first start can be slow', () => {
    assert.ok(versionLimitMs >= 30_000 && versionLimitMs <= 120_000, String(versionLimitMs));
});

test('versions compare number by number, and a pre-release comes before its release', () => {
    assert.ok(compareVersions('2.1.257', '2.1.1000') < 0, 'numeric, not by the letters');
    assert.ok(compareVersions('2.1.1000', '2.1.257') > 0);
    assert.ok(compareVersions('2.10.0', '2.9.999') > 0);
    assert.ok(compareVersions('3.0.0', '2.99.99') > 0);
    assert.equal(compareVersions('2.1.257', '2.1.257'), 0);
    assert.ok(compareVersions('2.1.257-beta.1', '2.1.257') < 0, 'a pre-release before its release');
    assert.ok(compareVersions('2.1.257', '2.1.257-rc.1') > 0);
    assert.ok(compareVersions('2.1.258-beta.1', '2.1.257') > 0, 'a pre-release of a later release is after this one');
    assert.ok(compareVersions('2.1.257-beta.2', '2.1.257-beta.10') < 0, 'numeric pre-release parts compare as numbers');
    assert.ok(compareVersions('2.1.257-alpha', '2.1.257-beta') < 0);
    assert.ok(compareVersions('2.1.257-1', '2.1.257-alpha') < 0, 'a numeric part before a word');
    assert.ok(compareVersions('2.1.257-beta', '2.1.257-beta.1') < 0, 'fewer parts first');
});

test('the floor is 2.1.257: below it the host is held with a sentence, at it and above it nothing is said', () => {
    assert.equal(claudeFloor, '2.1.257');
    const below = floorSentence('2.1.256');
    assert.equal(
        below,
        'Claude Code 2.1.256 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running `claude update` in a terminal, then check again.',
    );
    assert.match(floorSentence('2.1.257-beta.1') ?? '', /^Claude Code 2\.1\.257-beta\.1 is older than 2\.1\.257/);
    assert.equal(floorSentence('2.1.257'), null, 'exactly at the floor');
    assert.equal(floorSentence('2.1.1000'), null, 'above it');
    assert.equal(floorSentence('3.0.0'), null);
});

test('a version that could not be read is held as below the floor, and the sentence says what to type to see it', () => {
    assert.equal(
        floorSentence(null),
        'The version of Claude Code could not be read, so the chat does not run on it. Run `claude --version` in a terminal to see it; the chat needs 2.1.257 or newer, and `claude update` updates it. Then check again.',
    );
});

test('at most one version read is in flight: one asked for while it runs waits on it, and nothing is remembered once it answered', async () => {
    const pending: ((result: RunResult) => void)[] = [];
    const others: string[] = [];
    const programs = sharingVersionReads<Pick<Programs, 'run'>>({
        run: (name) => {
            if (name !== 'claude') {
                others.push(name);
                return Promise.resolve(answered('0.19.0'));
            }
            return new Promise<RunResult>((resolve) => pending.push(resolve));
        },
    });
    const first = programs.run('claude', ['--version'], { timeoutMs: versionLimitMs });
    const second = programs.run('claude', ['--version'], { timeoutMs: versionLimitMs });
    assert.equal(pending.length, 1, 'one read for the two');
    await programs.run('reply', ['--version']);
    assert.deepEqual(others, ['reply'], 'another program is not held behind it');
    pending[0]!(answered('2.1.300 (Claude Code)'));
    assert.deepEqual(await first, answered('2.1.300 (Claude Code)'));
    assert.deepEqual(await second, answered('2.1.300 (Claude Code)'));
    const third = programs.run('claude', ['--version'], { timeoutMs: versionLimitMs });
    assert.equal(pending.length, 2, 'a read asked for after the answer is a new read');
    pending[1]!(answered('2.1.301 (Claude Code)'));
    assert.equal((await third).stdout, '2.1.301 (Claude Code)');
});

test('an output past 8 MiB on a stream answers as unreadable, whatever it holds', async (t) => {
    const b = bench(t, { programs: ['claude'] });
    const file = join(b.root, 'version.json');
    writeFileSync(file, JSON.stringify({ stdout: `2.1.300 (Claude Code)\n${'x'.repeat(9 * 1024 * 1024)}`, exit: 0 }));
    const result = await programsOn({ ...b.env, STUB_CLAUDE_VERSION_FILE: file }, null).run('claude', ['--version'], { timeoutMs: versionLimitMs });
    assert.equal(result.exit, -1, 'the runner ends a program that prints past its 8 MiB');
    assert.equal(printedVersion(result), null);
    assert.equal(floorSentence(printedVersion(result)), floorSentence(null));
});

test('a version read past its bound answers as unreadable at the bound, and a program that outlives the first signal is still ended', { timeout: 20_000 }, async (t) => {
    const b = bench(t, { programs: ['claude'] });
    const ids = join(b.root, 'stuck.json');
    const stuck = programsOn({ ...b.env, STUB_CLAUDE_VERSION_STUCK: ids }, null);
    const alive = (id: number) => {
        try {
            process.kill(id, 0);
            return true;
        } catch {
            return false;
        }
    };
    t.after(() => {
        if (!existsSync(ids)) return;
        for (const id of JSON.parse(readFileSync(ids, 'utf8')) as number[]) if (alive(id)) process.kill(id);
    });
    const bound = 3000;
    const started = Date.now();
    const result = await stuck.run('claude', ['--version'], { timeoutMs: bound });
    const took = Date.now() - started;
    assert.equal(result.exit, -1);
    assert.equal(floorSentence(printedVersion(result)), floorSentence(null), 'unreadable');
    assert.ok(took >= bound - 50 && took < bound + 1500, `answered at the bound, whether or not the program has gone: ${took} ms`);
    assert.ok(existsSync(ids), 'the stand-in started before the bound');
    const started2 = Date.now();
    const kept = JSON.parse(readFileSync(ids, 'utf8')) as number[];
    while (kept.some(alive) && Date.now() - started2 < 6000) await new Promise((resolve) => setTimeout(resolve, 100));
    assert.deepEqual(kept.filter(alive), [], 'the term, then the kill after the grace, end it and what it started');
});
