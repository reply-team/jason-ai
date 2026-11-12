// The Reply card's account acts: each one verb of the Reply CLI, run by the main process on the person's click, behind
// the main process's own box where it changes something; a link the CLI makes goes to the system opener and nowhere else.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { loggedCli } from '../src/installation.ts';
import { fileLogger, type Logger } from '../src/log.ts';
import type { Programs } from '../src/programs.ts';
import { ProviderKeys } from '../src/providers.ts';
import {
    accountWords,
    actBox,
    askBox,
    belowMinimumSentence,
    isOpenableLink,
    isReplyId,
    linkedInNameProblem,
    otherRefusal,
    refusalOf,
    replyAccountSentences as said,
    replyActLimitMs,
    sharedPageBox,
    type ReplyBox,
} from '../src/reply-accounts.ts';
import {
    connectArguments,
    deleteArguments,
    limitsArguments,
    pendingArguments,
    readDailyLimit,
    readDone,
    readLink,
    readPending,
    readState,
    reconnectArguments,
    replyCliCodes,
    replyCliEnding,
    replyCliSharedPageWarning,
    stateArguments,
    unblockArguments,
} from '../src/reply-cli-account.ts';
import { replyMinimumVersion } from '../src/setup-model.ts';
import { Setup } from '../src/setup.ts';
import { bench, fakeBox, memoryLog, snapshot, type Bench } from './harness.ts';
import { cliCodes, fixtureLinks, printed, sharedPageWarning } from './reply-cli-account-fixture.ts';

// ---- The bench ------------------------------------------------------------------------------------------------------

/** The two accounts the stand-in lists: a mailbox and a LinkedIn account, made up. */
const listed = {
    STUB_REPLY_SIGNED_IN: 'yes',
    STUB_REPLY_ACCOUNTS_EMAIL: JSON.stringify([{ id: 12, channel: 'email', name: 'Made-up Sales', address: 'sales@example.test', reply_status: 'connected' }]),
    STUB_REPLY_ACCOUNTS_LINKEDIN: JSON.stringify([{ id: 34, channel: 'linkedin', name: 'Made-up Ada', address: '', reply_status: 'enabled' }]),
};
const print = (value: unknown) => JSON.stringify({ print: value });
const refused = (code: string, extra: Record<string, unknown> = {}) => JSON.stringify({ code, ...extra });
/** What a refusal said, read off an answer of either kind. */
const why = (answer: unknown) => answer as { refused?: string; offer?: string | null };

interface Harness {
    setup: Setup;
    boxes: ReplyBox[];
    opened: string[];
    log: Logger & { lines: { level: string; name: string; fields: Record<string, unknown> }[] };
    /** The account verbs `reply` was started with, each its arguments. */
    verbs(): string[][];
}

function harness(
    b: Bench,
    options: { yes?: boolean | (() => boolean); open?: ((link: string) => Promise<void>) | null; confirm?: null; programs?: Programs; log?: Logger } = {},
): Harness {
    const boxes: ReplyBox[] = [];
    const opened: string[] = [];
    const log = memoryLog();
    const yes = options.yes ?? true;
    const setup = new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: options.programs ?? b.programs,
        ...(options.confirm === null
            ? {}
            : {
                  confirm: (box: ReplyBox) => {
                      boxes.push(box);
                      return Promise.resolve(typeof yes === 'function' ? yes() : yes);
                  },
              }),
        ...(options.open === null ? {} : { openLink: options.open ?? ((link: string) => (opened.push(link), Promise.resolve())) }),
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log: options.log ?? log,
        bundleVersion: '0.37.0',
        runtimeVersion: () => '0.37.0',
        settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
    });
    const verbs = () => b.record().filter((entry) => entry.name === 'reply' && entry.args[0] === 'account' && entry.args[1] !== 'list' && entry.args[1] !== 'pending').map((entry) => entry.args);
    return { setup, boxes, opened, log, verbs };
}

/** A harness whose card has read the list, as a person's click always follows one. */
async function listedHarness(t: Parameters<typeof bench>[0], env: Record<string, string>, options: Parameters<typeof harness>[1] = {}): Promise<Harness & { b: Bench }> {
    const b = bench(t, { programs: ['reply'], env: { ...listed, ...env } });
    const h = harness(b, options);
    await h.setup.status();
    return { ...h, b };
}

// ---- The CLI's contract, held to what the CLI's own tests print -----------------------------------------------------

test('each verb is started with its own arguments: a name as one argument, the shared page and the force only when asked', () => {
    assert.deepEqual(connectArguments({ channel: 'linkedin', name: 'Made-up Ada' }), ['account', 'connect', '--channel', 'linkedin', '--name=Made-up Ada', '--json']);
    assert.deepEqual(connectArguments({ channel: 'linkedin', name: '--yes' }), ['account', 'connect', '--channel', 'linkedin', '--name=--yes', '--json'], 'a name is never read as an option');
    assert.deepEqual(connectArguments({ channel: 'email', provider: 'gmail', allowSharedPage: false }), ['account', 'connect', '--channel', 'email', '--provider', 'gmail', '--json']);
    assert.deepEqual(connectArguments({ channel: 'email', provider: 'office365', allowSharedPage: true }), [
        'account', 'connect', '--channel', 'email', '--provider', 'office365', '--allow-shared-page', '--json',
    ]);
    assert.deepEqual(reconnectArguments('email', '12', false), ['account', 'reconnect', '12', '--channel', 'email', '--json']);
    assert.deepEqual(reconnectArguments('email', '12', true), ['account', 'reconnect', '12', '--channel', 'email', '--allow-shared-page', '--json']);
    assert.deepEqual(reconnectArguments('linkedin', '34', true), ['account', 'reconnect', '34', '--channel', 'linkedin', '--json'], 'never for LinkedIn');
    assert.deepEqual(pendingArguments('linkedin'), ['account', 'pending', '--channel', 'linkedin', '--json']);
    assert.deepEqual(limitsArguments('12'), ['account', 'limits', '12', '--channel', 'email', '--json']);
    assert.deepEqual(limitsArguments('12', 0), ['account', 'limits', '12', '--channel', 'email', '--daily', '0', '--json']);
    assert.deepEqual(stateArguments('pause', '34'), ['account', 'pause', '34', '--channel', 'linkedin', '--json']);
    assert.deepEqual(stateArguments('resume', '34'), ['account', 'resume', '34', '--channel', 'linkedin', '--json']);
    assert.deepEqual(unblockArguments('12'), ['account', 'unblock', '12', '--json']);
    assert.deepEqual(deleteArguments('email', '12', true), ['account', 'delete', '12', '--channel', 'email', '--yes', '--json'], 'never forced for email');
    assert.deepEqual(deleteArguments('linkedin', '34', false), ['account', 'delete', '34', '--channel', 'linkedin', '--yes', '--json']);
    assert.deepEqual(deleteArguments('linkedin', '34', true), ['account', 'delete', '34', '--channel', 'linkedin', '--yes', '--force', '--json']);
});

test('the prints the CLI’s own tests pin are read, and a print of any other shape is not', () => {
    const json = (value: unknown) => JSON.stringify(value);
    assert.deepEqual(readLink(json(printed.connectEmail)), { channel: 'email', link: fixtureLinks.emailConnect, expiresAt: '2026-11-18T12:00:00Z', pendingId: 50, kind: 'connect', linkKind: 'connection_link' });
    assert.deepEqual(readLink(json(printed.connectEmailShared)), { channel: 'email', link: fixtureLinks.sharedGmail, expiresAt: null, pendingId: null, kind: 'connect', linkKind: 'shared_page' });
    assert.deepEqual(readLink(json(printed.connectLinkedIn))?.pendingId, 51);
    assert.deepEqual(readLink(json(printed.reconnectEmail))?.kind, 'reconnect');
    assert.deepEqual(readLink(json(printed.reconnectEmailShared))?.linkKind, 'shared_page');
    assert.deepEqual(readLink(json(printed.reconnectLinkedIn))?.channel, 'linkedin');
    for (const bad of ['', 'not json', '[1]', json({ ...printed.connectLinkedIn, link: '' }), json({ ...printed.connectLinkedIn, kind: 'other' }), json({ ...printed.connectLinkedIn, link_kind: 'x' }), json({ ...printed.connectLinkedIn, channel: 'sms' })]) {
        assert.equal(readLink(bad), null, bad);
    }
    assert.deepEqual(readPending(json(printed.pending)), [
        { channel: 'email', id: '50', name: 'Made-up mailbox', kind: 'connect', expiresAt: '2026-11-18T12:00:00Z', expired: false },
        { channel: 'email', id: '52', name: '', kind: 'reconnect', expiresAt: '2026-11-01T12:00:00Z', expired: true },
        { channel: 'linkedin', id: '7', name: 'Made-up LinkedIn', kind: 'connect', expiresAt: '2026-11-19T00:00:00Z', expired: false },
    ]);
    assert.deepEqual(readPending(json(printed.pendingUnsupported)), []);
    assert.equal(readPending(json({ items: 'no' })), null);
    assert.deepEqual(readDailyLimit(json(printed.limitsEmail)), { daily: 500 });
    assert.deepEqual(readDailyLimit(json({ ...printed.limitsEmail, limits: null })), { daily: null });
    assert.equal(readDailyLimit(json({ channel: 'email' })), null);
    assert.deepEqual(readState(json(printed.pause)), { status: 'inactive', changed: true });
    assert.deepEqual(readState(json(printed.pauseUnchanged)), { status: 'inactive', changed: false });
    assert.deepEqual(readState(json(printed.resume)), { status: 'active', changed: true });
    assert.equal(readState(json({ status: 'other' })), null);
    assert.equal(readDone(json(printed.unblock), 'unblocked'), true);
    assert.equal(readDone(json(printed.deleteEmail), 'deleted'), true);
    assert.equal(readDone(json(printed.deleteLinkedIn), 'deleted'), true);
    assert.equal(readDone(json({ deleted: 'yes' }), 'deleted'), false);
});

test('an ending is read from the exit and the code of the last JSON line on stderr, never from the CLI’s words', () => {
    const line = (code: unknown) => JSON.stringify({ error: { code, title: 'never read' } });
    assert.deepEqual(replyCliEnding({ exit: 0, stdout: '{}', stderr: '' }), { kind: 'done', stdout: '{}' });
    assert.deepEqual(replyCliEnding({ exit: 1, stdout: '', stderr: line('linkedin.account_in_use') }), { kind: 'refused', code: 'linkedin.account_in_use' });
    assert.deepEqual(replyCliEnding({ exit: 1, stdout: '', stderr: `a progress line\n${line('rate_limited')}\n` }), { kind: 'refused', code: 'rate_limited' });
    assert.deepEqual(replyCliEnding({ exit: 2, stdout: '', stderr: line('usage.confirm') }), { kind: 'refused', code: 'usage.confirm' });
    assert.deepEqual(replyCliEnding({ exit: 3, stdout: '', stderr: line('outcome.unknown') }), { kind: 'unknown' });
    for (const stderr of ['', 'error: unknown option', line(7), line('Not A Code'), line('x'.repeat(65)), line('a..b')]) {
        assert.deepEqual(replyCliEnding({ exit: 1, stdout: '', stderr }), { kind: 'refused', code: null }, stderr);
    }
    for (const exit of [-1, 4, 7]) assert.deepEqual(replyCliEnding({ exit, stdout: '', stderr: line('reply.refused') }), { kind: 'unreached' });
});

test('the codes the desktop acts on are the CLI’s, and the shared page’s warning is the CLI’s own words', () => {
    const known = new Set<string>([...cliCodes.account, ...cliCodes.common]);
    for (const code of Object.values(replyCliCodes)) assert.ok(known.has(code), code);
    assert.equal(replyCliSharedPageWarning, sharedPageWarning);
});

// ---- What each code does ---------------------------------------------------------------------------------------------

test('each code the card acts on is handled as ruled, and every other code, known or not, is one sentence carrying it', () => {
    const connect = { verb: 'connect' as const, channel: 'email' as const, write: true };
    const remove = { verb: 'delete' as const, channel: 'linkedin' as const, write: true };
    const pause = { verb: 'pause' as const, channel: 'linkedin' as const, write: true };
    const code = (value: string) => ({ kind: 'refused' as const, code: value });
    // The shared page: offered after not_available on an email link asked without it, and never on the shared page's own.
    assert.deepEqual(refusalOf(code('email_link.not_available'), connect), { refused: said.sharedPageOffer, code: 'email_link.not_available', offer: 'shared_page', reread: false });
    assert.equal(refusalOf(code('email_link.not_available'), { ...connect, verb: 'reconnect' }).offer, 'shared_page');
    assert.equal(refusalOf(code('email_link.not_available'), { ...connect, sharedPage: true }).offer, null);
    assert.equal(refusalOf(code('email_link.not_available'), { ...connect, channel: 'linkedin' }).offer, null);
    // A signed-out LinkedIn account offers Reconnect.
    assert.deepEqual(refusalOf(code('linkedin.invalid_cookie'), pause), { refused: said.signedOut, code: 'linkedin.invalid_cookie', offer: 'reconnect', reread: false });
    // A LinkedIn account in use offers the force, never on a forced delete.
    assert.deepEqual(refusalOf(code('linkedin.account_in_use'), remove), { refused: said.inUse, code: 'linkedin.account_in_use', offer: 'force', reread: false });
    assert.equal(refusalOf(code('linkedin.account_in_use'), { ...remove, force: true }).offer, null);
    // The list is read again, and nothing is repeated.
    for (const [value, sentence] of [
        ['outcome.unknown', said.outcomeUnknown],
        ['account.status_changed', said.stateChanged],
        ['account.status_unknown', said.stateUnknown],
        ['email_account.not_found', said.accountGone],
        ['linkedin.account_not_found', said.accountGone],
    ] as const) {
        assert.deepEqual(refusalOf(code(value), pause), { refused: sentence, code: value, offer: null, reread: true }, value);
    }
    assert.deepEqual(refusalOf({ kind: 'unknown' }, remove), { refused: said.outcomeUnknown, code: 'outcome.unknown', offer: null, reread: true });
    // The plan's limits: a sentence, nothing offered.
    for (const [value, sentence] of [
        ['email_account.limit_reached', said.mailboxLimit],
        ['email_account.no_free_mailbox_slots', said.mailboxSeat],
        ['linkedin.account_limit_reached', said.linkedInLimit],
        ['access.feature_unavailable', said.notInPlan],
        ['shared_page.not_enabled', said.sharedPageOff],
    ] as const) {
        assert.deepEqual(refusalOf(code(value), connect), { refused: sentence, code: value, offer: null, reread: false }, value);
    }
    // Every other code, the CLI's own, a usage error and one nobody has seen yet, by the one sentence.
    for (const value of ['email_account.reconnect_not_supported', 'email_link.provider_required', 'access.denied', 'rate_limited', 'reply.unavailable', 'reply.refused', 'usage.confirm', 'usage.name', 'linkedin.something_new']) {
        assert.deepEqual(refusalOf(code(value), connect), { refused: otherRefusal(value), code: value, offer: null, reread: false }, value);
        assert.ok(otherRefusal(value).includes(value));
    }
    assert.deepEqual(refusalOf({ kind: 'refused', code: null }, connect), { refused: otherRefusal(null), code: null, offer: null, reread: false });
    // No answer at all: a write may have been done, so the list is read again; a read is only tried again.
    assert.deepEqual(refusalOf({ kind: 'unreached' }, remove), { refused: said.unreachedWrite, code: null, offer: null, reread: true });
    assert.deepEqual(refusalOf({ kind: 'unreached' }, { verb: 'limits', channel: 'email', write: false }), { refused: said.unreached, code: null, offer: null, reread: false });
});

test('a link is handed to the opener only as https on a host name, with no user, port or anything but printable ASCII', () => {
    for (const good of [fixtureLinks.emailConnect, fixtureLinks.linkedinConnect, fixtureLinks.sharedGmail, 'https://auth.linkedin.reply.io/hosted/x?state=y']) {
        assert.equal(isOpenableLink(good), true, good);
    }
    for (const bad of [
        'http://app.example.test/x',
        'file:///etc/hosts',
        'javascript:alert(1)',
        'reply://open',
        'https://user@app.example.test/x',
        'https://app.example.test:8443/x',
        'https://127.0.0.1/x',
        'https://0x7f.1/x',
        'https://[::1]/x',
        'https://localhost/x',
        'https://app.example.test/x y',
        'https://app.example.test/é',
        '',
        7,
        null,
    ]) {
        assert.equal(isOpenableLink(bad), false, JSON.stringify(bad));
    }
});

test('the boxes are the main process’s own: they name the account as the list does and the act, Cancel focused', async () => {
    const mailbox = { channel: 'email', name: 'Made-up Sales', address: 'sales@example.test' };
    const linkedin = { channel: 'linkedin', name: 'Made-up Ada', address: '' };
    assert.equal(accountWords(mailbox), 'the mailbox Made-up Sales (sales@example.test)');
    assert.equal(accountWords(linkedin), 'the LinkedIn account Made-up Ada');
    assert.equal(accountWords({ channel: 'email', name: '', address: '' }), 'the mailbox with no name');
    assert.equal(accountWords({ channel: 'email', name: `two\nlines${'x'.repeat(100)}`, address: '' }).includes('\n'), false);
    assert.deepEqual(actBox('delete', mailbox), {
        type: 'warning',
        title: 'Delete the mailbox Made-up Sales (sales@example.test) from Reply?',
        message: 'Delete the mailbox Made-up Sales (sales@example.test) from Reply?',
        detail: 'Reply removes it, and this cannot be undone.',
        buttons: ['Delete', 'Cancel'],
        defaultId: 1,
        cancelId: 1,
        noLink: true,
    });
    assert.equal(actBox('pause', linkedin).message, 'Pause the LinkedIn account Made-up Ada?');
    assert.equal(actBox('resume', linkedin).message, 'Resume the LinkedIn account Made-up Ada?');
    assert.equal(actBox('unblock', mailbox).message, 'Unblock the mailbox Made-up Sales (sales@example.test)?');
    assert.equal(actBox('limits', mailbox, 200).message, 'Set the daily limit of the mailbox Made-up Sales (sales@example.test) to 200?');
    assert.match(actBox('force', linkedin).detail, /Sequences use this LinkedIn account/);
    assert.deepEqual(actBox('force', linkedin).buttons, ['Delete anyway', 'Cancel']);
    assert.ok(sharedPageBox().detail.includes(replyCliSharedPageWarning), 'the shared page’s box carries the CLI’s warning');
    for (const each of [sharedPageBox(), actBox('delete', mailbox), actBox('force', linkedin)]) {
        assert.equal(each.defaultId, 1);
        assert.equal(each.cancelId, 1);
        assert.equal(each.buttons[1], 'Cancel');
    }
    const shown: unknown[] = [];
    const answering = (response: number) => (window: string, options: ReplyBox) => (shown.push([window, options]), Promise.resolve({ response }));
    assert.equal(await askBox('window', answering(0), sharedPageBox()), true);
    for (const response of [1, 2, -1]) assert.equal(await askBox('window', answering(response), sharedPageBox()), false);
    const before = shown.length;
    assert.equal(await askBox(null, answering(0), sharedPageBox()), false);
    assert.equal(shown.length, before, 'no window: nothing is shown, and the answer is no');
});

test('an id is held to Reply’s shape and a LinkedIn account’s name to one plain line of up to a hundred characters', () => {
    for (const good of ['1', '77', '9007199254740991']) assert.equal(isReplyId(good), true, good);
    for (const bad of ['0', '-1', '1e3', '12/../x', '01', '12345678901234567', '9007199254740993', ' 7', '', 7, null]) assert.equal(isReplyId(bad), false, String(bad));
    assert.equal(linkedInNameProblem('Made-up Sales'), null);
    assert.equal(linkedInNameProblem('😀'.repeat(100)), null);
    for (const bad of ['', '   ', 'x'.repeat(101), 'two\nlines', `bidi${String.fromCodePoint(0x202e)}`, 7, null]) assert.equal(linkedInNameProblem(bad), said.badName, JSON.stringify(bad));
});

// ---- The acts, against the reply stand-in ----------------------------------------------------------------------------

test('a LinkedIn account is connected by its verb, and the link goes to the opener while the page learns only that it opened', async (t) => {
    const h = await listedHarness(t, { STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn) });
    const answer = await h.setup.replyConnect('linkedin', { name: '  Made-up Ada  ' });
    assert.deepEqual(answer, { opened: true, channel: 'linkedin', kind: 'connect', linkKind: 'connection_link', expiresAt: null, pendingId: 51 });
    assert.deepEqual(h.opened, [fixtureLinks.linkedinConnect]);
    assert.deepEqual(h.verbs(), [['account', 'connect', '--channel', 'linkedin', '--name=Made-up Ada', '--json']]);
    assert.deepEqual(h.boxes, [], 'a link that expires and connects one account needs no box');
});

test('a mailbox is connected for its provider, and reconnected by the id the list gave', async (t) => {
    const h = await listedHarness(t, { STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectEmail), STUB_REPLY_ACCOUNT_RECONNECT: print(printed.reconnectLinkedIn) });
    assert.deepEqual(await h.setup.replyConnect('email', { provider: 'office365' }), { opened: true, channel: 'email', kind: 'connect', linkKind: 'connection_link', expiresAt: '2026-11-18T12:00:00Z', pendingId: 50 });
    assert.equal(((await h.setup.replyReconnect('linkedin', '34')) as { kind: string }).kind, 'reconnect');
    assert.deepEqual(h.verbs(), [
        ['account', 'connect', '--channel', 'email', '--provider', 'office365', '--json'],
        ['account', 'reconnect', '34', '--channel', 'linkedin', '--json'],
    ]);
    assert.deepEqual(h.opened, [fixtureLinks.emailConnect, fixtureLinks.linkedinConnect]);
});

test('an act names only an account the list gave, by channel and id, and anything out of shape is refused before reply starts', async (t) => {
    const h = await listedHarness(t, {});
    const no = (refusedWith: string) => ({ refused: refusedWith, code: null, offer: null, reread: false });
    assert.deepEqual(await h.setup.replyReconnect('email', '34'), no(said.badId), 'a LinkedIn id named as a mailbox');
    assert.deepEqual(await h.setup.replyReconnect('linkedin', '99'), no(said.badId));
    assert.deepEqual(await h.setup.replyReconnect('sms', '12'), no(said.badRequest));
    assert.deepEqual(await h.setup.replyReconnect('linkedin', '34', { sharedPage: true }), no(said.badRequest));
    assert.deepEqual(await h.setup.replyPause('12'), no(said.badId), 'a mailbox has no pause');
    assert.deepEqual(await h.setup.replyResume('12/../x'), no(said.badId));
    assert.deepEqual(await h.setup.replyUnblock('34'), no(said.badId), 'a LinkedIn account has no unblock');
    assert.deepEqual(await h.setup.replyLimits('34', 10), no(said.badId));
    for (const daily of [-1, 1.5, '10', 2_147_483_648, Number.NaN]) assert.deepEqual(await h.setup.replyLimits('12', daily), no(said.badLimit), String(daily));
    assert.deepEqual(await h.setup.replyDelete('email', '12', { force: true }), no(said.badRequest), 'never forced for a mailbox');
    for (const name of ['', 'x'.repeat(101), 'two\nlines', 7, null]) assert.deepEqual(await h.setup.replyConnect('linkedin', { name }), no(said.badName));
    assert.deepEqual(await h.setup.replyConnect('linkedin', { name: 'x', sharedPage: true }), no(said.badRequest));
    for (const options of [{}, { provider: 'yahoo' }, { provider: 'outlook' }, null]) assert.deepEqual(await h.setup.replyConnect('email', options), no(said.badRequest));
    assert.deepEqual(await h.setup.replyConnect('sms', { provider: 'gmail' }), no(said.badRequest));
    assert.deepEqual(h.verbs(), []);
    assert.equal(h.boxes.length, 0);
});

test('pause, resume, unblock, limits and delete each ask the person first: no yes, no call', async (t) => {
    const env = {
        STUB_REPLY_ACCOUNT_PAUSE: print(printed.pause),
        STUB_REPLY_ACCOUNT_RESUME: print(printed.resume),
        STUB_REPLY_ACCOUNT_UNBLOCK: print({ ...printed.unblock, id: 12 }),
        STUB_REPLY_ACCOUNT_LIMITS: print(printed.limitsEmail),
        STUB_REPLY_ACCOUNT_DELETE: print(printed.deleteEmail),
    };
    const no = await listedHarness(t, env, { yes: false });
    const nothing = { refused: said.notConfirmed, code: null, offer: null, reread: false };
    assert.deepEqual(await no.setup.replyPause('34'), nothing);
    assert.deepEqual(await no.setup.replyResume('34'), nothing);
    assert.deepEqual(await no.setup.replyUnblock('12'), nothing);
    assert.deepEqual(await no.setup.replyLimits('12', 200), nothing);
    assert.deepEqual(await no.setup.replyDelete('email', '12'), nothing);
    assert.deepEqual(await no.setup.replyDelete('linkedin', '34'), nothing);
    assert.deepEqual(no.verbs(), [], 'no yes, no call');
    assert.deepEqual(no.boxes.map((each) => each.message), [
        'Pause the LinkedIn account Made-up Ada?',
        'Resume the LinkedIn account Made-up Ada?',
        'Unblock the mailbox Made-up Sales (sales@example.test)?',
        'Set the daily limit of the mailbox Made-up Sales (sales@example.test) to 200?',
        'Delete the mailbox Made-up Sales (sales@example.test) from Reply?',
        'Delete the LinkedIn account Made-up Ada from Reply?',
    ]);
    // With no box to ask in, the answer is no.
    const none = await listedHarness(t, env, { confirm: null });
    assert.deepEqual(await none.setup.replyDelete('email', '12'), nothing);
    assert.deepEqual(none.verbs(), []);

    const yes = await listedHarness(t, env);
    assert.deepEqual(await yes.setup.replyPause('34'), { done: true });
    assert.deepEqual(await yes.setup.replyResume('34'), { done: true });
    assert.deepEqual(await yes.setup.replyUnblock('12'), { done: true });
    assert.deepEqual(await yes.setup.replyLimits('12', 200), { daily: 500 });
    assert.deepEqual(await yes.setup.replyDelete('email', '12'), { done: true });
    assert.deepEqual(yes.verbs(), [
        ['account', 'pause', '34', '--channel', 'linkedin', '--json'],
        ['account', 'resume', '34', '--channel', 'linkedin', '--json'],
        ['account', 'unblock', '12', '--json'],
        ['account', 'limits', '12', '--channel', 'email', '--daily', '200', '--json'],
        ['account', 'delete', '12', '--channel', 'email', '--yes', '--json'],
    ]);
    // Reading a limit changes nothing, so it asks nothing.
    const boxes = yes.boxes.length;
    assert.deepEqual(await yes.setup.replyLimits('12'), { daily: 500 });
    assert.equal(yes.boxes.length, boxes);
    assert.deepEqual(yes.verbs().at(-1), ['account', 'limits', '12', '--channel', 'email', '--json']);
});

test('the shared page is the one door email_link.not_available opens: offered then, behind the CLI’s warning, never by default', async (t) => {
    let yes = false;
    const h = await listedHarness(
        t,
        {
            STUB_REPLY_ACCOUNT_CONNECT: refused('email_link.not_available', { error: { hint: `With --allow-shared-page ...; ${sharedPageWarning}.` } }),
            STUB_REPLY_ACCOUNT_CONNECT_SHARED: print(printed.connectEmailShared),
            STUB_REPLY_ACCOUNT_RECONNECT: refused('email_link.not_available'),
            STUB_REPLY_ACCOUNT_RECONNECT_SHARED: print(printed.reconnectEmailShared),
        },
        { yes: () => yes },
    );
    // Asked before the CLI said so, it is refused, and nothing is asked of the person or of Reply.
    assert.deepEqual(await h.setup.replyConnect('email', { provider: 'gmail', sharedPage: true }), { refused: said.notOffered, code: null, offer: null, reread: false });
    assert.deepEqual(h.verbs(), []);
    assert.equal(h.boxes.length, 0);
    // The first try never passes it; its refusal offers the page.
    assert.deepEqual(await h.setup.replyConnect('email', { provider: 'gmail' }), { refused: said.sharedPageOffer, code: 'email_link.not_available', offer: 'shared_page', reread: false });
    assert.deepEqual(h.opened, []);
    // A no in the box asks nothing; the offer stays.
    assert.deepEqual(await h.setup.replyConnect('email', { provider: 'gmail', sharedPage: true }), { refused: said.notConfirmed, code: null, offer: null, reread: false });
    assert.deepEqual(h.boxes, [sharedPageBox()]);
    assert.equal(h.verbs().length, 1);
    // A yes repeats the same ask with --allow-shared-page, once.
    yes = true;
    assert.deepEqual(await h.setup.replyConnect('email', { provider: 'gmail', sharedPage: true }), { opened: true, channel: 'email', kind: 'connect', linkKind: 'shared_page', expiresAt: null, pendingId: null });
    assert.deepEqual(h.opened, [fixtureLinks.sharedGmail]);
    assert.deepEqual(h.verbs(), [
        ['account', 'connect', '--channel', 'email', '--provider', 'gmail', '--json'],
        ['account', 'connect', '--channel', 'email', '--provider', 'gmail', '--allow-shared-page', '--json'],
    ]);
    assert.deepEqual(why(await h.setup.replyConnect('email', { provider: 'gmail', sharedPage: true })).refused, said.notOffered, 'the door closes once gone through');
    // The door is the ask's own: Outlook's is not opened by Gmail's.
    assert.deepEqual(why(await h.setup.replyConnect('email', { provider: 'office365', sharedPage: true })).refused, said.notOffered);
    // A reconnect has its own, by the account.
    assert.equal(why(await h.setup.replyReconnect('email', '12')).offer, 'shared_page');
    assert.equal(((await h.setup.replyReconnect('email', '12', { sharedPage: true })) as { linkKind: string }).linkKind, 'shared_page');
    assert.deepEqual(h.verbs().slice(2), [
        ['account', 'reconnect', '12', '--channel', 'email', '--json'],
        ['account', 'reconnect', '12', '--channel', 'email', '--allow-shared-page', '--json'],
    ]);
    assert.ok(h.boxes.every((each) => each.detail.includes(replyCliSharedPageWarning)));
});

test('a LinkedIn account in use is the one door to --force: a box of its own, and never forced on the first try', async (t) => {
    let yes = true;
    const h = await listedHarness(t, { STUB_REPLY_ACCOUNT_DELETE: refused('linkedin.account_in_use'), STUB_REPLY_ACCOUNT_DELETE_FORCE: print(printed.deleteLinkedIn) }, { yes: () => yes });
    assert.deepEqual(await h.setup.replyDelete('linkedin', '34', { force: true }), { refused: said.notOffered, code: null, offer: null, reread: false });
    assert.deepEqual(h.verbs(), []);
    assert.deepEqual(await h.setup.replyDelete('linkedin', '34'), { refused: said.inUse, code: 'linkedin.account_in_use', offer: 'force', reread: false });
    yes = false;
    assert.deepEqual(why(await h.setup.replyDelete('linkedin', '34', { force: true })).refused, said.notConfirmed);
    assert.equal(h.verbs().length, 1, 'a no to the second box sends nothing');
    yes = true;
    assert.deepEqual(await h.setup.replyDelete('linkedin', '34', { force: true }), { done: true });
    assert.deepEqual(h.verbs(), [
        ['account', 'delete', '34', '--channel', 'linkedin', '--yes', '--json'],
        ['account', 'delete', '34', '--channel', 'linkedin', '--yes', '--force', '--json'],
    ]);
    assert.deepEqual(h.boxes.map((each) => each.buttons[0]), ['Delete', 'Delete anyway', 'Delete anyway']);
    assert.match(h.boxes[1]!.detail, /Sequences use this LinkedIn account/);
});

test('a signed-out LinkedIn account offers Reconnect; the codes that leave the state unknown read the list again', async (t) => {
    const h = await listedHarness(t, {
        STUB_REPLY_ACCOUNT_PAUSE: refused('linkedin.invalid_cookie'),
        STUB_REPLY_ACCOUNT_RESUME: refused('account.status_changed'),
        STUB_REPLY_ACCOUNT_UNBLOCK: refused('email_account.not_found'),
        STUB_REPLY_ACCOUNT_DELETE: JSON.stringify({ code: 'outcome.unknown', exit: 3 }),
        STUB_REPLY_ACCOUNT_LIMITS: refused('usage.range'),
        STUB_REPLY_ACCOUNT_RECONNECT: refused('linkedin.some_code_nobody_has_seen'),
        STUB_REPLY_ACCOUNT_CONNECT: refused('linkedin.account_limit_reached'),
    });
    assert.deepEqual(await h.setup.replyPause('34'), { refused: said.signedOut, code: 'linkedin.invalid_cookie', offer: 'reconnect', reread: false });
    assert.deepEqual(await h.setup.replyResume('34'), { refused: said.stateChanged, code: 'account.status_changed', offer: null, reread: true });
    assert.deepEqual(await h.setup.replyUnblock('12'), { refused: said.accountGone, code: 'email_account.not_found', offer: null, reread: true });
    assert.deepEqual(await h.setup.replyDelete('linkedin', '34'), { refused: said.outcomeUnknown, code: 'outcome.unknown', offer: null, reread: true });
    assert.deepEqual(await h.setup.replyLimits('12', 5), { refused: otherRefusal('usage.range'), code: 'usage.range', offer: null, reread: false });
    assert.deepEqual(await h.setup.replyReconnect('linkedin', '34'), { refused: otherRefusal('linkedin.some_code_nobody_has_seen'), code: 'linkedin.some_code_nobody_has_seen', offer: null, reread: false });
    assert.deepEqual(await h.setup.replyConnect('linkedin', { name: 'x' }), { refused: said.linkedInLimit, code: 'linkedin.account_limit_reached', offer: null, reread: false });
    // Each act is logged by its verb, channel, exit and code; a usage error says it is one, the desktop's own defect.
    const logged = h.log.lines.filter((line) => line.name === 'reply_account_act').map((line) => line.fields);
    assert.deepEqual(logged.map((fields) => [fields['Act'], fields['Code'], fields['Usage']]), [
        ['pause', 'linkedin.invalid_cookie', false],
        ['resume', 'account.status_changed', false],
        ['unblock', 'email_account.not_found', false],
        ['delete', 'outcome.unknown', false],
        ['limits', 'usage.range', true],
        ['reconnect', 'linkedin.some_code_nobody_has_seen', false],
        ['connect', 'linkedin.account_limit_reached', false],
    ]);
    assert.equal(JSON.stringify(h.log.lines).includes('never shows'), false, 'nothing of the CLI’s words is logged');
});

test('the link is a secret: no log line, no file, no refusal and no answer to the page carries it', async (t) => {
    const secret = fixtureLinks.token;
    const b = bench(t, {
        programs: ['reply'],
        env: {
            ...listed,
            STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn),
            STUB_REPLY_ACCOUNT_RECONNECT: print(printed.reconnectLinkedIn),
            // A refusal whose words carry the token, as no CLI should: the desktop reads its code and nothing else.
            STUB_REPLY_ACCOUNT_CONNECT_SHARED: JSON.stringify({ code: 'reply.unavailable', error: { detail: fixtureLinks.sharedIndex } }),
        },
    });
    const log = fileLogger(b.paths.logs);
    const h = harness(b, { log });
    await h.setup.status();
    const answers: unknown[] = [
        await h.setup.replyConnect('linkedin', { name: 'Made-up Ada' }),
        await h.setup.replyReconnect('linkedin', '34'),
        await h.setup.status(),
    ];
    // An answer of a shape the desktop does not read, or a link it does not open, is said without the link.
    const odd = bench(t, { programs: ['reply'], env: { ...listed, STUB_REPLY_ACCOUNT_CONNECT: print({ ...printed.connectLinkedIn, link: `http://app.example.test/${secret}` }), STUB_REPLY_ACCOUNT_RECONNECT: JSON.stringify({ exit: 0, stdout: `{"link":"${fixtureLinks.linkedinConnect}"` }) } });
    const oddLog = fileLogger(odd.paths.logs);
    const o = harness(odd, { log: oddLog });
    await o.setup.status();
    answers.push(await o.setup.replyConnect('linkedin', { name: 'x' }), await o.setup.replyReconnect('linkedin', '34'));
    assert.deepEqual(answers.slice(3), [
        { refused: said.badLink, code: null, offer: null, reread: true },
        { refused: said.outcomeUnknown, code: null, offer: null, reread: true },
    ]);
    assert.deepEqual(o.opened, [], 'nothing of either is opened');
    assert.equal(JSON.stringify(answers).includes(secret), false, 'no answer to the page carries the link');
    for (const root of [b.root, odd.root]) {
        const files = snapshot(root);
        // The stand-in's own record of what it was started with holds its environment, which the test wrote.
        files.delete('record.jsonl');
        assert.ok([...files.keys()].some((path) => path.startsWith('data/logs/')), 'the log was written');
        for (const path of files.keys()) assert.equal(readFileSync(join(root, path), 'latin1').includes(secret), false, `${path} carries the link`);
    }
    assert.equal(h.opened.length, 2);
});

test('the opener is the only way out: with none, or one that fails, the page is told and nothing else holds the link', async (t) => {
    const none = await listedHarness(t, { STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn) }, { open: null });
    assert.deepEqual(await none.setup.replyConnect('linkedin', { name: 'x' }), { refused: said.badLink, code: null, offer: null, reread: true });
    const failing = await listedHarness(t, { STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn) }, { open: () => Promise.reject(new Error(`could not open ${fixtureLinks.linkedinConnect}`)) });
    const answer = await failing.setup.replyConnect('linkedin', { name: 'x' });
    assert.deepEqual(answer, { refused: said.notOpened, code: null, offer: null, reread: true });
    assert.equal(JSON.stringify(failing.log.lines).includes(fixtureLinks.token), false);
});

test('below the Reply CLI the acts need, the card says to update it and offers nothing: no list is read and no act runs', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { ...listed, STUB_REPLY_VERSION: '0.20.0', STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn) } });
    const h = harness(b);
    const { reply } = await h.setup.status();
    assert.equal(reply.belowMinimum, belowMinimumSentence(replyMinimumVersion));
    assert.equal(reply.belowMinimum, 'Managing Reply accounts here needs the Reply CLI 0.21.0 or later: update the Reply CLI.');
    assert.deepEqual([reply.accounts, reply.pending], [[], []]);
    assert.deepEqual(await h.setup.replyConnect('linkedin', { name: 'x' }), { refused: reply.belowMinimum, code: null, offer: null, reread: false });
    assert.deepEqual(await h.setup.replyDelete('email', '12'), { refused: said.badId, code: null, offer: null, reread: false }, 'nothing listed, so nothing to name');
    assert.equal(b.record().some((entry) => entry.name === 'reply' && entry.args[0] === 'account'), false);
    assert.equal(h.boxes.length, 0);
    // At the minimum there is nothing to say.
    const current = await listedHarness(t, {});
    assert.equal((await current.setup.status()).reply.belowMinimum, null);
});

test('the links made and not used yet are read for each channel that was listed, never with a link', async (t) => {
    const h = await listedHarness(t, { STUB_REPLY_ACCOUNT_PENDING: print(printed.pending) });
    const { reply } = await h.setup.status();
    assert.deepEqual(reply.pending, [
        { channel: 'email', id: '50', name: 'Made-up mailbox', kind: 'connect', expiresAt: '2026-11-18T12:00:00Z', expired: false },
        { channel: 'email', id: '52', name: '', kind: 'reconnect', expiresAt: '2026-11-01T12:00:00Z', expired: true },
        { channel: 'linkedin', id: '7', name: 'Made-up LinkedIn', kind: 'connect', expiresAt: '2026-11-19T00:00:00Z', expired: false },
    ]);
    const asked = h.b.record().filter((entry) => entry.args[1] === 'pending').map((entry) => entry.args);
    assert.deepEqual(asked.slice(0, 2), [pendingArguments('email'), pendingArguments('linkedin')]);
    // A channel the plan lacks is not read; a read that fails lists none.
    const absent = await listedHarness(t, { STUB_REPLY_ACCOUNTS_LINKEDIN: 'refused', STUB_REPLY_ACCOUNT_PENDING: refused('reply.unavailable') });
    assert.deepEqual((await absent.setup.status()).reply.pending, []);
    assert.deepEqual(absent.b.record().filter((entry) => entry.args[1] === 'pending').map((entry) => entry.args[3]), ['email', 'email']);
});

test('one act at a time: a second click while one runs is refused, and a verb is stopped at thirty seconds', async (t) => {
    const h = await listedHarness(t, { STUB_REPLY_ACCOUNT_CONNECT: print(printed.connectLinkedIn) });
    const [first, second] = await Promise.all([h.setup.replyConnect('linkedin', { name: 'x' }), h.setup.replyPause('34')]);
    assert.equal((first as { opened: boolean }).opened, true);
    assert.deepEqual(second, { refused: said.busy, code: null, offer: null, reread: false });

    const b = bench(t, { programs: ['reply'], env: listed });
    const limits: (number | undefined)[] = [];
    // The verb hangs where a test cannot wait thirty seconds: the limit asked for is kept, and the run ends as stopped.
    const programs: Programs = {
        find: (name) => b.programs.find(name),
        run: (name, args, options) => {
            if (args[0] === 'account' && args[1] === 'delete') {
                limits.push(options?.timeoutMs);
                return Promise.resolve({ exit: -1, stdout: '', stderr: '' });
            }
            return b.programs.run(name, args, options);
        },
    };
    const stopped = harness(b, { programs });
    await stopped.setup.status();
    assert.deepEqual(await stopped.setup.replyDelete('email', '12'), { refused: said.unreachedWrite, code: null, offer: null, reread: true });
    assert.equal(replyActLimitMs, 30_000);
    assert.deepEqual(limits, [30_000]);
});
