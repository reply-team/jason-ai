// The label the desktop gives Reply: REPLY_CLIENT, built from the bundle's version, in the environment of the reply it
// starts and nowhere else among its programs; an inherited one, in any letter case, is never passed on beside it.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { replyClient, replyClientVariable, withReplyClient } from '../src/reply-client.ts';
import { bench } from './harness.ts';

// The shape reply-cli publishes for the value (src/config.ts, `CLIENT`): one product token, a lower-case name of up to
// 32 letters, digits or dashes, a slash, and a version of up to 32 letters, digits, dots, plus or minus. A value of any
// other shape is left out by reply-cli without a word.
const publishedShape = /^[a-z][a-z0-9-]{0,31}\/[0-9A-Za-z.+-]{1,32}$/;

const named = (env: Readonly<Record<string, string>>) => Object.entries(env).filter(([name]) => name.toUpperCase() === replyClientVariable);

test('the label is the harness\'s token and the bundle\'s version, cut at the plus sign', () => {
    assert.equal(replyClient('0.35.0'), 'jason-harness/0.35.0');
    assert.equal(replyClient('0.35.0-rc.1'), 'jason-harness/0.35.0-rc.1');
    assert.equal(replyClient('0.35.0+4f2a9c1'), 'jason-harness/0.35.0');
    assert.equal(replyClient('0.35.0-rc.1+4f2a9c1.dirty'), 'jason-harness/0.35.0-rc.1');
});

test('a label always has the shape reply-cli publishes, and a version that would break it gives no label', () => {
    for (const version of ['0.35.0', '0.35.0-rc.1', '1.0.0+abc', '9.99.999-alpha.12+build.7', '0.35.0-rc.1234567890123456789012']) {
        const value = replyClient(version);
        assert.ok(value !== null && publishedShape.test(value), `${version} gave ${value}`);
    }
    for (const version of ['', '+4f2a9c1', '0.35.0 rc', '0.35.0_rc', '0.35.0/1', '0.35.0-rc.12345678901234567890123', 'v0.35.0\n']) {
        assert.equal(replyClient(version), null, JSON.stringify(version));
    }
});

test('with no bundle version there is no label', () => {
    assert.equal(replyClient(null), null);
});

test('the label replaces an inherited one in any letter case, so there is one; with no bundle version the inherited one is still taken out', () => {
    const inheritedCases: Record<string, string>[] = [{ REPLY_CLIENT: 'someone-else/1.0' }, { reply_client: 'someone-else/1.0' }, { Reply_Client: 'someone-else/1.0', REPLY_CLIENT: 'other/2' }];
    for (const inherited of inheritedCases) {
        const base = { PATH: '/x', HOME: '/h', ...inherited };
        assert.deepEqual(named(withReplyClient(base, '0.35.0+4f2a9c1')), [[replyClientVariable, 'jason-harness/0.35.0']], JSON.stringify(inherited));
        const none = withReplyClient(base, null);
        assert.deepEqual(named(none), [], JSON.stringify(inherited));
        assert.deepEqual(none, { PATH: '/x', HOME: '/h' });
    }
});

test('the reply the desktop runs carries the label, and another program it runs does not', async (t) => {
    const b = bench(t, { programs: ['reply', 'npm'], env: { reply_client: 'someone-else/1.0' }, bundleVersion: '0.35.0+4f2a9c1' });
    assert.equal((await b.programs.run('reply', ['auth', 'status', '--json'])).exit, 1);
    assert.equal((await b.programs.run('npm', ['install', '-g', 'something'])).exit, 0);
    const [reply, npm] = b.record();
    assert.equal(reply?.name, 'reply');
    assert.deepEqual(named(reply?.env ?? {}).map(([, value]) => value), ['jason-harness/0.35.0']);
    assert.equal(npm?.name, 'npm');
    assert.deepEqual(named(npm?.env ?? {}).map(([, value]) => value), ['someone-else/1.0'], 'another program is started as before');
});

test('with no bundle version the reply the desktop runs carries no label, not even an inherited one', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { REPLY_CLIENT: 'someone-else/1.0' } });
    await b.programs.run('reply', ['auth', 'status', '--json']);
    const [reply] = b.record();
    assert.equal(reply?.name, 'reply');
    assert.deepEqual(named(reply?.env ?? {}), []);
});
