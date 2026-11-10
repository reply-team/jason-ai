import assert from 'node:assert/strict';
import { test } from 'node:test';
import { signalGroup } from '../src/chat-process.ts';

/** A stop of a POSIX host, with the signals it sends recorded and the grace period run by hand. */
function stopRecorded(fails: (pid: number, signal: string) => boolean) {
    const sent: string[] = [];
    const pending: (() => void)[] = [];
    signalGroup(
        4242,
        (pid, signal) => {
            sent.push(`${signal} ${pid}`);
            if (fails(pid, signal)) throw Object.assign(new Error('kill ESRCH'), { code: 'ESRCH' });
        },
        (run) => pending.push(run),
    );
    return { sent, pending };
}

test('a stop kills the host\'s whole group after the grace period, though the host itself has exited by then', () => {
    const { sent, pending } = stopRecorded(() => false);
    assert.deepEqual(sent, ['SIGTERM -4242']);
    assert.equal(pending.length, 1);
    // The host exited on SIGTERM; a process it started that ignores SIGTERM is still in the group.
    pending[0]?.();
    assert.deepEqual(sent, ['SIGTERM -4242', 'SIGKILL -4242']);
});

test('a group already empty when the grace period ends is no error', () => {
    const { sent, pending } = stopRecorded((_pid, signal) => signal === 'SIGKILL');
    assert.doesNotThrow(() => pending[0]?.());
    assert.deepEqual(sent, ['SIGTERM -4242', 'SIGKILL -4242']);
});

test('a group that is gone at the stop is sent nothing more', () => {
    const { sent, pending } = stopRecorded(() => true);
    assert.deepEqual(sent, ['SIGTERM -4242']);
    assert.equal(pending.length, 0);
});
