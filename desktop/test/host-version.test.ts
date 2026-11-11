// The oldest Claude Code the chat runs a turn on: how its version is read from what `claude --version` printed, how two
// versions compare, and the sentence a host below that version is listed and refused with.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { claudeFloor, compareVersions, floorSentence, printedVersion, versionLimitMs } from '../src/host-version.ts';

const answered = (stdout: string, stderr = '', exit = 0) => ({ exit, stdout, stderr });

test('the version is the first three dotted numbers anywhere in what was printed, on either stream, whatever stands around it', () => {
    assert.equal(printedVersion(answered('2.1.300 (Claude Code)\n')), '2.1.300');
    assert.equal(printedVersion(answered('Claude Code version v2.1.300, build 7f3e, ready\n')), '2.1.300');
    assert.equal(printedVersion(answered('', 'warning: something\n2.1.300 (Claude Code)\n')), '2.1.300', 'on standard error alone');
    assert.equal(printedVersion(answered('node 22.11.0 found\n', '2.1.300\n')), '22.11.0', 'the first group of the two streams');
    assert.equal(printedVersion(answered('2.1.300-beta.2 (Claude Code)\n')), '2.1.300-beta.2', 'a pre-release part is kept');
    assert.equal(printedVersion(answered('1.2.3.4\n')), '1.2.3');
    assert.equal(printedVersion(answered('Claude Code\n')), null, 'no three dotted numbers');
    assert.equal(printedVersion(answered('2.1\n')), null);
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
