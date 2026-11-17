// W: a first start of a new version that meets an update still holding the runtime's start is never settled as a failed
// first start. It waits, bounded, asks again, and past the bound stays unsettled, the page saying so.

import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DataNewerRefused, LayoutRefused, StartFailed, UpdateBusy, updateBusyIn } from '../src/installation.ts';
import { startPastUpdate, StillUpdating, stillUpdatingSentence, updateHolds, updatePollMs, updateWaitMs, updateWaitSentence, type SetupState } from '../src/machine-setup.ts';
import { isRejected, readRollback, settleFailedStart, settleStart, writeRollback } from '../src/rollback.ts';
import { memoryLog } from './harness.ts';

const busy = (code = 'update_in_progress') =>
    new UpdateBusy({ exit: 1, stdout: JSON.stringify({ error: { code, message: 'An update to 0.49.0 is in flight.', retryable: true } }), stderr: '' }, code, 'An update to 0.49.0 is in flight.');

/** A rollback record as the update that installed 0.49.0 over 0.48.3 left it: pending, its first start not settled. */
function pendingUpdate(): string {
    const rollback = join(mkdtempSync(join(tmpdir(), 'start-past-update-')), 'desktop', 'rollback');
    writeRollback(rollback, { rejected: [], pending: { fromVersion: '0.48.3', toVersion: '0.49.0', directory: '/kept/0.48.3/Jason' } });
    return rollback;
}

/** A clock the waits move: each sleep advances it by what was asked. */
function clock() {
    let now = 0;
    const slept: number[] = [];
    return { slept, now: () => now, sleep: async (ms: number) => void (slept.push(ms), (now += ms)) };
}

const kinds = { busy: (error: unknown) => error instanceof UpdateBusy, refusedData: (error: unknown) => error instanceof LayoutRefused || error instanceof DataNewerRefused };

test('W: the finish holds the lock at the first start and then lets go: the start is asked again and the version settles as started', async () => {
    const rollback = pendingUpdate();
    const time = clock();
    const said: string[] = [];
    let tries = 0;
    const started = await startPastUpdate(
        async () => {
            tries++;
            if (tries <= 2) throw busy(tries === 1 ? 'update_in_progress' : 'install_in_progress');
            return 'loaded';
        },
        { holds: (error) => updateHolds(error, null, kinds), waiting: async () => void said.push(updateWaitSentence), ...time },
    );
    assert.deepEqual([started, tries, time.slept], ['loaded', 3, [updatePollMs, updatePollMs]]);
    assert.deepEqual(said, [updateWaitSentence], 'the page says once what it waits for');
    // What main does once the window loaded: the update's first start is settled as one that worked.
    assert.equal(settleStart(rollback, '0.49.0', true, memoryLog()), null);
    assert.equal(isRejected(readRollback(rollback), '0.49.0'), false);
    assert.equal(readRollback(rollback).kept?.version, '0.48.3');
    // The refusal is read by its code, never its words.
    assert.deepEqual(updateBusyIn(JSON.stringify({ error: { code: 'install_in_progress', message: 'Setting up.', retryable: true } })), { code: 'install_in_progress', message: 'Setting up.' });
    assert.equal(updateBusyIn(JSON.stringify({ error: { code: 'port_taken', message: 'Taken.' } })), null);
    const installation = readFileSync(new URL('../src/installation.ts', import.meta.url), 'utf8');
    assert.ok(installation.indexOf('const busy = updateBusyIn(start.stdout);') < installation.indexOf("throw new StartFailed('runtime start', start,"));
});

test('W: an update that holds the start past the bound leaves the version unsettled, not rejected, and the page says so; a set-up that met the lock is the same', async () => {
    const cases: [SetupState | null, Error][] = [
        [null, busy()],
        [{ state: 'later', why: 'Another jason holds the update.' }, new StartFailed('runtime start', null, 'jason runtime start did not start a runtime.')],
        [{ state: 'reported', report: {} as never, later: 'Held.' }, new StartFailed('ui install', null, 'no UI')],
    ];
    for (const [setup, error] of cases) {
        const rollback = pendingUpdate();
        const time = clock();
        await assert.rejects(
            startPastUpdate(
                async () => {
                    throw error;
                },
                { holds: (failed) => updateHolds(failed, setup, kinds), waiting: async () => undefined, ...time },
            ),
            (thrown: unknown) => thrown instanceof StillUpdating && thrown.message === stillUpdatingSentence,
        );
        assert.ok(time.now() >= updateWaitMs && time.now() <= updateWaitMs + updatePollMs, 'bounded by updateWaitMs');
        // main's catch settles nothing for it: the record stays pending, and the next start decides.
        assert.deepEqual(readRollback(rollback).pending, { fromVersion: '0.48.3', toVersion: '0.49.0', directory: '/kept/0.48.3/Jason' });
        assert.equal(isRejected(readRollback(rollback), '0.49.0'), false);
    }
    const main = readFileSync(new URL('../src/main.ts', import.meta.url), 'utf8');
    const caught = main.slice(main.indexOf("log.error('window_load_failed'"));
    const still = caught.indexOf('if (error instanceof StillUpdating) {');
    assert.ok(still > 0 && still < caught.indexOf('settleFailedStart('), 'decided before anything is settled');
    const branch = caught.slice(still, caught.indexOf('} else {', still));
    assert.ok(branch.includes('await showStarting(current, stillUpdatingSentence, true);'));
    assert.equal(branch.includes('settle'), false);
    assert.ok(main.includes('busy: (failed) => failed instanceof UpdateBusy,'));
    assert.match(stillUpdatingSentence, /still being finished, so Jason did not start the runtime/);
});

test('W: an ordinary failed first start still lands in rejected as before, and a refused layout or database is never waited for', async () => {
    const rollback = pendingUpdate();
    const plain = new StartFailed('runtime start', { exit: 1, stdout: '', stderr: '' }, 'The port is taken.');
    const time = clock();
    const notLater: SetupState = { state: 'reported', report: {} as never };
    await assert.rejects(
        startPastUpdate(
            async () => {
                throw plain;
            },
            { holds: (error) => updateHolds(error, notLater, kinds), waiting: async () => undefined, ...time },
        ),
        (thrown: unknown) => thrown === plain,
    );
    assert.deepEqual(time.slept, [], 'not waited for');
    settleFailedStart(rollback, '0.49.0', false, memoryLog());
    assert.equal(isRejected(readRollback(rollback), '0.49.0'), true);
    // A layout or a database refused is the data directory's, even where the set-up met the lock: never waited for.
    const later: SetupState = { state: 'later', why: 'Held.' };
    assert.equal(updateHolds(new LayoutRefused({ exit: 1, stdout: '', stderr: '' }, 'layout_newer', 'Newer.'), later, kinds), false);
    assert.equal(updateHolds(new DataNewerRefused({ exit: 1, stdout: '', stderr: '' }, 'Newer.'), later, kinds), false);
    assert.equal(updateHolds(plain, null, kinds), false);
});
