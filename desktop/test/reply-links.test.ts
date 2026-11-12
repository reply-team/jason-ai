// The Reply card's links: the address Reply answers for connecting or reconnecting an account, asked by the desktop's
// main process through `reply api` from a closed table of calls, checked before it is shown, and kept nowhere.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { loggedCli } from '../src/installation.ts';
import { fileLogger, type Logger } from '../src/log.ts';
import type { Programs } from '../src/programs.ts';
import { ProviderKeys } from '../src/providers.ts';
import {
    askMailboxConfirmation,
    checkedLinkedInLink,
    isReplyId,
    linkedInNameProblem,
    mailboxAddress,
    mailboxConfirmation,
    mailboxToken,
    readReplyApi,
    replyApiArguments,
    replyApiCalls,
    replyApiLimitMs,
    replyLinkSentences as said,
} from '../src/reply-links.ts';
import { Setup } from '../src/setup.ts';
import { bench, fakeBox, memoryLog, snapshot, type Bench } from './harness.ts';

// Made-up values only: no address, token or id here is anyone's.
const hex = '0123456789abcdef0123456789abcdef';
const connectLink = `https://run.reply.io/linkedin-custom-connect/${hex}`;
const olderLink = `https://run.reply.io/linkedin-connect/${hex}`;
const reconnectLink = 'https://auth.linkedin.reply.io/hosted/made-up-path?state=made-up';
const token = 'madeUpToken_ForTests-0123456789abcdefghijklmnop';
const mailboxPage = `https://run.reply.io/hosted/eac/index/${token}`;
const detail = 'Reply detail text naming user 4242 that is never shown';

function setupOn(
    b: Bench,
    log: Logger = memoryLog(),
    options: { confirm?: (() => Promise<boolean>) | null; programs?: Programs } = {},
): Setup {
    const confirm = options.confirm === undefined ? () => Promise.resolve(true) : options.confirm;
    return new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: options.programs ?? b.programs,
        ...(confirm === null ? {} : { confirmMailboxLink: confirm }),
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log,
        bundleVersion: '0.36.0',
        runtimeVersion: () => '0.36.0',
        settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
    });
}

const answer = (status: number, data: unknown) => JSON.stringify({ status, data });
// reply-cli 0.20.0 is the first that reads a body from standard input, which the LinkedIn acts need.
const signedIn = { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_VERSION: '0.20.0' };
const apiCalls = (b: Bench) => b.record().filter((entry) => entry.name === 'reply' && entry.args[0] === 'api');

// ---- The closed table ----------------------------------------------------------------------------------------------

test('the calls the main process makes through reply api are a closed table of four, with nothing from the page but an id', () => {
    assert.deepEqual(replyApiCalls, {
        linkedin_connect: { method: 'POST', path: '/v3/linkedin-accounts/connection-link', body: 'name' },
        linkedin_pending: { method: 'GET', path: '/v3/linkedin-accounts/pending', body: null },
        linkedin_reconnect: { method: 'POST', path: '/v3/linkedin-accounts/{id}/reconnect', body: null },
        mailbox_page: { method: 'GET', path: '/v1/SharedPageUrl', body: null },
    });
    assert.deepEqual(replyApiArguments('linkedin_connect'), ['api', '/v3/linkedin-accounts/connection-link', '--method', 'POST', '--body', '-', '--json']);
    assert.deepEqual(replyApiArguments('linkedin_pending'), ['api', '/v3/linkedin-accounts/pending', '--json']);
    assert.deepEqual(replyApiArguments('linkedin_reconnect', '77'), ['api', '/v3/linkedin-accounts/77/reconnect', '--method', 'POST', '--json']);
    assert.deepEqual(replyApiArguments('mailbox_page'), ['api', '/v1/SharedPageUrl', '--json']);
    assert.throws(() => replyApiArguments('linkedin_reconnect', '12/../x'));
    assert.throws(() => replyApiArguments('linkedin_reconnect'));
    assert.throws(() => replyApiArguments('other' as never));
});

test('an account id is held to its shape: digits, no leading zero, at most sixteen, a safe integer', () => {
    for (const good of ['1', '77', '9007199254740991']) assert.equal(isReplyId(good), true, good);
    for (const bad of ['0', '-1', '1e3', '12/../x', '01', '12345678901234567', '9007199254740993', ' 7', '7 ', '', 7, null, undefined, {}]) {
        assert.equal(isReplyId(bad), false, String(bad));
    }
});

test('a LinkedIn account’s name is one plain line of one to a hundred characters', () => {
    assert.equal(linkedInNameProblem('Sales LinkedIn'), null);
    assert.equal(linkedInNameProblem(`  ${'x'.repeat(100)}  `), null, 'trimmed before it is counted');
    assert.equal(linkedInNameProblem('😀'.repeat(100)), null, 'counted by characters');
    for (const bad of ['', '   ', 'x'.repeat(101), 'two\nlines', 'tab\there', `bidi${String.fromCodePoint(0x202e)}`, 7, null, undefined]) {
        assert.equal(linkedInNameProblem(bad), said.badName, JSON.stringify(bad));
    }
});

// ---- What Reply answers, checked -----------------------------------------------------------------------------------

test('a LinkedIn link is shown only in one of Reply’s three forms, its host read from it', () => {
    assert.deepEqual(checkedLinkedInLink(connectLink), { link: connectLink, host: 'run.reply.io' });
    assert.deepEqual(checkedLinkedInLink(olderLink), { link: olderLink, host: 'run.reply.io' });
    // The host of the first two is any plain host name: other product domains, and a white-label team's own.
    const white = `https://outreach.example-agency.test/linkedin-custom-connect/${hex}`;
    assert.deepEqual(checkedLinkedInLink(white), { link: white, host: 'outreach.example-agency.test' });
    assert.deepEqual(checkedLinkedInLink(reconnectLink), { link: reconnectLink, host: 'auth.linkedin.reply.io' });
    for (const bad of [
        `http://run.reply.io/linkedin-connect/${hex}`,
        `https://run.reply.io:8443/linkedin-connect/${hex}`,
        `https://user@run.reply.io/linkedin-connect/${hex}`,
        `https://run.reply.io/linkedin-connect/${hex}?next=x`,
        `https://run.reply.io/linkedin-connect/${hex}#x`,
        `https://run.reply.io/linkedin-connect/${hex}/`,
        `https://run.reply.io/linkedin-connect/${hex.toUpperCase()}`,
        `https://run.reply.io/linkedin-connect/${hex.slice(1)}`,
        `https://run.reply.io/linkedin-connect/${hex}0`,
        'https://run.reply.io/other',
        `https://run.reply.io/hosted/eac/index/${token}`,
        `https://run_reply.io/linkedin-connect/${hex}`,
        `https://-run.reply.io/linkedin-connect/${hex}`,
        `https://run..reply.io/linkedin-connect/${hex}`,
        `https://localhost/linkedin-connect/${hex}`,
        `https:///linkedin-connect/${hex}`,
        `https://run.reply.io /linkedin-connect/${hex}`,
        `https://rün.reply.io/linkedin-connect/${hex}`,
        // An address written as numbers is no host name: a browser reads a last label that is a number as IPv4.
        `https://127.0.0.1/linkedin-connect/${hex}`,
        `https://10.0.0.1/linkedin-custom-connect/${hex}`,
        `https://0x7f.1/linkedin-connect/${hex}`,
        `https://run.reply.0x1/linkedin-connect/${hex}`,
        `https://run.reply.42/linkedin-connect/${hex}`,
        'https://auth.linkedin.reply.io.evil.example/x',
        'https://evilauth.linkedin.reply.io/x',
        'https://auth.linkedin.reply.io:8443/x',
        'https://user@auth.linkedin.reply.io/x',
        'http://auth.linkedin.reply.io/x',
        'https://auth.linkedin.reply.io/x#y',
        'https://auth.linkedin.reply.io/x y',
        'https://auth.linkedin.reply.io/x\ny',
        'javascript:alert(1)',
        '',
        7,
        null,
        { connectionLink: connectLink },
    ]) {
        assert.equal(checkedLinkedInLink(bad), null, JSON.stringify(bad));
    }
});

test('a mailbox page is shown only as the hosted page with its token, and Gmail’s and Outlook’s are built from that token alone', () => {
    assert.deepEqual(mailboxToken(mailboxPage), { host: 'run.reply.io', token });
    assert.deepEqual(mailboxToken(`https://app.example-agency.test/hosted/eac/index/${token}`), { host: 'app.example-agency.test', token });
    assert.deepEqual(mailboxToken('https://RUN.reply.io/hosted/eac/index/abcdefgh'), { host: 'run.reply.io', token: 'abcdefgh' });
    // A label of digits is a host name where the last label is not one.
    assert.deepEqual(mailboxToken(`https://10.mail.example-agency.test/hosted/eac/index/${token}`), { host: '10.mail.example-agency.test', token });
    assert.deepEqual(checkedLinkedInLink(`https://0x7f.example.test/linkedin-connect/${hex}`)?.host, '0x7f.example.test');
    for (const bad of [
        `http://run.reply.io/hosted/eac/index/${token}`,
        `https://run.reply.io:444/hosted/eac/index/${token}`,
        `https://user@run.reply.io/hosted/eac/index/${token}`,
        `https://run.reply.io/hosted/eac/index/${token}?x=1`,
        `https://run.reply.io/hosted/eac/index/${token}#x`,
        `https://run.reply.io/hosted/eac/index/${token}/`,
        'https://run.reply.io/hosted/eac/index/abcdefg',
        `https://run.reply.io/hosted/eac/index/${'a'.repeat(201)}`,
        'https://run.reply.io/hosted/eac/index/abc.defgh',
        'https://run.reply.io/hosted/eac/index/abc%2Fdefgh',
        `https://run.reply.io/hosted/eac/ConnectWithGMail?uidToken=${token}`,
        `https://run.reply.io/linkedin-connect/${hex}`,
        `https://run..reply.io/hosted/eac/index/${token}`,
        `https://127.0.0.1/hosted/eac/index/${token}`,
        `https://10.0.0.1/hosted/eac/index/${token}`,
        `https://0x7f.1/hosted/eac/index/${token}`,
        `https://0X7F.1/hosted/eac/index/${token}`,
        '',
        null,
        7,
    ]) {
        assert.equal(mailboxToken(bad), null, JSON.stringify(bad));
    }
    assert.equal(mailboxAddress('run.reply.io', token, 'hosted'), mailboxPage);
    assert.equal(mailboxAddress('run.reply.io', token, 'gmail'), `https://run.reply.io/hosted/eac/ConnectWithGMail?uidToken=${token}`);
    assert.equal(mailboxAddress('run.reply.io', token, 'outlook'), `https://run.reply.io/hosted/eac/ConnectWithOffice?uidToken=${token}&isExchange=false`);
    // The checked token's alphabet needs no escape; the address is still built encoded, so no token could reshape it.
    assert.equal(mailboxAddress('run.reply.io', 'a/b?c&d#e', 'gmail'), 'https://run.reply.io/hosted/eac/ConnectWithGMail?uidToken=a%2Fb%3Fc%26d%23e');
    assert.equal(mailboxAddress('run.reply.io', 'a/b?c', 'hosted'), 'https://run.reply.io/hosted/eac/index/a%2Fb%3Fc');
});

test('reply api’s print is read as Reply’s status and body, and anything else as no answer', () => {
    assert.deepEqual(readReplyApi({ exit: 0, stdout: '{"code":201,"data":{"a":1}}\n', stderr: '' }), { status: 201, data: { a: 1 } });
    assert.deepEqual(readReplyApi({ exit: 1, stdout: '{"code":409,"data":{"code":"x"}}', stderr: '' }), { status: 409, data: { code: 'x' } });
    for (const result of [
        { exit: 3, stdout: '{"code":201,"data":{}}', stderr: '' },
        { exit: -1, stdout: '', stderr: '' },
        { exit: 0, stdout: 'not json', stderr: '' },
        { exit: 0, stdout: '{"code":"201","data":{}}', stderr: '' },
        { exit: 0, stdout: '{"code":201}', stderr: '' },
        { exit: 0, stdout: '{"code":99,"data":{}}', stderr: '' },
        { exit: 0, stdout: '[1]', stderr: '' },
        { exit: 1, stdout: '{"code":201,"data":{}}', stderr: '' },
        { exit: 0, stdout: '{"code":409,"data":{}}', stderr: '' },
    ]) {
        assert.equal(readReplyApi(result), null, JSON.stringify(result));
    }
});

// ---- The acts, against the reply stand-in --------------------------------------------------------------------------

test('a LinkedIn account is added by connection-link, with the name on standard input and never on the command line', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_CONNECT: answer(201, { connectionLink: connectLink, pendingAccountId: 41 }) } });
    assert.deepEqual(await setupOn(b).replyLinkedInConnect('  Made-up Sales Account  '), { kind: 'linkedin', link: connectLink, host: 'run.reply.io', expiresAtUtc: null });
    const [call] = apiCalls(b);
    assert.deepEqual(call?.args, ['api', '/v3/linkedin-accounts/connection-link', '--method', 'POST', '--body', '-', '--json']);
    assert.equal(call?.stdin, JSON.stringify({ name: 'Made-up Sales Account' }));
    assert.equal(b.record().some((entry) => entry.args.some((arg) => arg.includes('Made-up'))), false);
});

test('a waiting connection’s link is asked again from the pending list, and a reconnect names the account’s id alone', async (t) => {
    const pending = [
        { id: 41, name: 'Made-up one', connectionLink: connectLink, expiresAtUtc: '2026-11-19T04:59:00Z', isExpired: false },
        { id: 42, name: 'Made-up two', connectionLink: olderLink, expiresAtUtc: '2026-11-12T04:59:00Z', isExpired: true },
    ];
    const b = bench(t, {
        programs: ['reply'],
        env: { ...signedIn, STUB_REPLY_API_PENDING: answer(200, pending), STUB_REPLY_API_RECONNECT: answer(200, { connectionLink: reconnectLink }) },
    });
    const setup = setupOn(b);
    assert.deepEqual(await setup.replyLinkedInPendingLink('42'), { kind: 'linkedin', link: olderLink, host: 'run.reply.io', expiresAtUtc: '2026-11-12T04:59:00Z' });
    assert.deepEqual(await setup.replyLinkedInPendingLink('43'), { refused: said.pendingGone });
    assert.deepEqual(await setup.replyLinkedInReconnect('77'), { kind: 'linkedin', link: reconnectLink, host: 'auth.linkedin.reply.io', expiresAtUtc: null });
    assert.deepEqual(apiCalls(b).map((entry) => entry.args), [
        ['api', '/v3/linkedin-accounts/pending', '--json'],
        ['api', '/v3/linkedin-accounts/pending', '--json'],
        ['api', '/v3/linkedin-accounts/77/reconnect', '--method', 'POST', '--json'],
    ]);
    assert.ok(apiCalls(b).every((entry) => entry.stdin === ''), 'no body but the name');
});

test('the card lists waiting LinkedIn connections by name, state and expiry, and never their link', async (t) => {
    const pending = [
        { id: 41, name: 'Made-up one', connectionLink: connectLink, expiresAtUtc: '2026-11-19T04:59:00Z', isExpired: false },
        { id: 42, name: 'Made-up two', connectionLink: olderLink, expiresAtUtc: 'not a moment', isExpired: true },
        { id: 'li_7', name: 'An id of another shape', connectionLink: connectLink, isExpired: false },
        { id: 44, name: 7, connectionLink: connectLink },
    ];
    const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_PENDING: answer(200, pending) } });
    const { reply } = await setupOn(b).status();
    assert.deepEqual(reply.linkedinPending, [
        { id: '41', name: 'Made-up one', expiresAtUtc: '2026-11-19T04:59:00Z', isExpired: false },
        { id: '42', name: 'Made-up two', expiresAtUtc: null, isExpired: true },
        { id: '44', name: '', expiresAtUtc: null, isExpired: false },
    ]);
    assert.equal(JSON.stringify(reply).includes(hex), false, 'no link reaches the page with the list');

    // Not read where LinkedIn is not in the plan, nor where the CLI is not signed in; a list that fails is an empty one.
    const absent = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_ACCOUNTS_LINKEDIN: 'refused', STUB_REPLY_API_PENDING: answer(200, pending) } });
    assert.deepEqual((await setupOn(absent).status()).reply.linkedinPending, []);
    assert.deepEqual(apiCalls(absent), []);
    const out = bench(t, { programs: ['reply'], env: { STUB_REPLY_API_PENDING: answer(200, pending) } });
    assert.deepEqual((await setupOn(out).status()).reply.linkedinPending, []);
    assert.deepEqual(apiCalls(out), []);
    const failing = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_PENDING: answer(500, { detail }) } });
    assert.deepEqual((await setupOn(failing).status()).reply.linkedinPending, []);
});

test('a name or an id out of shape is refused before reply starts', async (t) => {
    const b = bench(t, { programs: ['reply'], env: signedIn });
    const setup = setupOn(b);
    for (const name of ['', '   ', 'x'.repeat(101), 'two\nlines', 7, null, undefined, { name: 'x' }]) {
        assert.deepEqual(await setup.replyLinkedInConnect(name), { refused: said.badName }, JSON.stringify(name));
    }
    for (const id of ['0', '-1', '1e3', '12/../x', '12345678901234567', 7, null, undefined]) {
        assert.deepEqual(await setup.replyLinkedInReconnect(id), { refused: said.badId }, JSON.stringify(id));
        assert.deepEqual(await setup.replyLinkedInPendingLink(id), { refused: said.badId }, JSON.stringify(id));
    }
    for (const page of ['smtp', '', 'GMAIL', null, 7]) {
        assert.deepEqual(await setup.replyMailboxLink(page), { refused: said.badPage }, JSON.stringify(page));
    }
    assert.deepEqual(apiCalls(b), []);
});

test('each refusal Reply gives is one fixed sentence keyed on its status and code, never Reply’s own words', async (t) => {
    const problem = (status: number, extra: Record<string, unknown> = {}) => answer(status, { title: detail, detail, ...extra });
    const cases: [string, Record<string, string>, (setup: Setup) => Promise<unknown>, string][] = [
        ['409 limit', { STUB_REPLY_API_CONNECT: problem(409, { code: 'linkedInAccount.limitReached' }) }, (s) => s.replyLinkedInConnect('x'), said.limitReached],
        ['409 payment', { STUB_REPLY_API_CONNECT: problem(409, { code: 'linkedInAccount.paymentFailed' }) }, (s) => s.replyLinkedInConnect('x'), said.paymentFailed],
        ['409 another code', { STUB_REPLY_API_CONNECT: problem(409, { code: 'linkedInAccount.other' }) }, (s) => s.replyLinkedInConnect('x'), said.unreached],
        ['403 plan', { STUB_REPLY_API_CONNECT: problem(403, { reasons: [1] }) }, (s) => s.replyLinkedInConnect('x'), said.notInPlan],
        ['403 permission', { STUB_REPLY_API_CONNECT: problem(403, { reasons: [2] }) }, (s) => s.replyLinkedInConnect('x'), said.notPermitted],
        ['403 with a code', { STUB_REPLY_API_CONNECT: problem(403, { code: 'x.forbidden', reasons: [1] }) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['403 without reasons', { STUB_REPLY_API_CONNECT: problem(403) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['403 with another reason', { STUB_REPLY_API_RECONNECT: problem(403, { reasons: [9] }) }, (s) => s.replyLinkedInReconnect('77'), said.notMade],
        ['400 another code', { STUB_REPLY_API_CONNECT: problem(400, { code: 'linkedInAccount.nameTooLong' }) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['400 on reconnect', { STUB_REPLY_API_RECONNECT: problem(400, { code: 'x.y' }) }, (s) => s.replyLinkedInReconnect('77'), said.notMade],
        ['400 invalid', { STUB_REPLY_API_CONNECT: problem(400, { code: 'linkedInAccount.invalidInput' }) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['400 no code', { STUB_REPLY_API_CONNECT: problem(400) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['400 bare', { STUB_REPLY_API_CONNECT: answer(400, null) }, (s) => s.replyLinkedInConnect('x'), said.notMade],
        ['401', { STUB_REPLY_API_CONNECT: problem(401) }, (s) => s.replyLinkedInConnect('x'), said.signInAgain],
        ['429', { STUB_REPLY_API_CONNECT: problem(429) }, (s) => s.replyLinkedInConnect('x'), said.wait],
        ['500', { STUB_REPLY_API_CONNECT: problem(500) }, (s) => s.replyLinkedInConnect('x'), said.unreached],
        // A connect may have made its waiting row before its answer was lost.
        ['exit 3', { STUB_REPLY_API_CONNECT: JSON.stringify({ exit: 3, stdout: detail }) }, (s) => s.replyLinkedInConnect('x'), said.noAnswerInTime],
        ['no print', { STUB_REPLY_API_CONNECT: JSON.stringify({ exit: 0, stdout: detail }) }, (s) => s.replyLinkedInConnect('x'), said.noAnswerInTime],
        ['reconnect exit 3', { STUB_REPLY_API_RECONNECT: JSON.stringify({ exit: 3, stdout: detail }) }, (s) => s.replyLinkedInReconnect('77'), said.unreached],
        ['mailbox no print', { STUB_REPLY_API_MAILBOX: JSON.stringify({ exit: 0, stdout: detail }) }, (s) => s.replyMailboxLink('gmail'), said.unreached],
        ['201 without a link', { STUB_REPLY_API_CONNECT: answer(201, { pendingAccountId: 41 }) }, (s) => s.replyLinkedInConnect('x'), said.badLink],
        ['201 with a link of another host', { STUB_REPLY_API_CONNECT: answer(201, { connectionLink: `https://run.reply.io.evil.example:8443/linkedin-connect/${hex}` }) }, (s) => s.replyLinkedInConnect('x'), said.badLink],
        ['404 reconnect', { STUB_REPLY_API_RECONNECT: problem(404, { code: 'linkedInAccount.notFound' }) }, (s) => s.replyLinkedInReconnect('77'), said.accountGone],
        ['404 another code', { STUB_REPLY_API_RECONNECT: problem(404, { code: 'route.notFound' }) }, (s) => s.replyLinkedInReconnect('77'), said.unreached],
        ['reconnect http', { STUB_REPLY_API_RECONNECT: answer(200, { connectionLink: 'http://auth.linkedin.reply.io/x' }) }, (s) => s.replyLinkedInReconnect('77'), said.badLink],
        ['pending 403', { STUB_REPLY_API_PENDING: problem(403, { reasons: [1] }) }, (s) => s.replyLinkedInPendingLink('41'), said.notInPlan],
        ['pending not a list', { STUB_REPLY_API_PENDING: answer(200, { items: [] }) }, (s) => s.replyLinkedInPendingLink('41'), said.unreached],
        ['mailbox 403', { STUB_REPLY_API_MAILBOX: problem(403) }, (s) => s.replyMailboxLink('gmail'), said.mailboxPageOff],
        ['mailbox 403 with a code', { STUB_REPLY_API_MAILBOX: problem(403, { code: 'x.forbidden' }) }, (s) => s.replyMailboxLink('hosted'), said.mailboxPageOff],
        ['mailbox 401', { STUB_REPLY_API_MAILBOX: problem(401) }, (s) => s.replyMailboxLink('outlook'), said.signInAgain],
        ['mailbox 409', { STUB_REPLY_API_MAILBOX: problem(409, { code: 'linkedInAccount.limitReached' }) }, (s) => s.replyMailboxLink('gmail'), said.unreached],
        ['mailbox page of another shape', { STUB_REPLY_API_MAILBOX: answer(200, { url: `https://run.reply.io/hosted/eac/index/${token}?x=1` }) }, (s) => s.replyMailboxLink('gmail'), said.badLink],
    ];
    for (const [name, env, act, sentence] of cases) {
        const b = bench(t, { programs: ['reply'], env: { ...signedIn, ...env } });
        const log = memoryLog();
        const refused = await act(setupOn(b, log));
        assert.deepEqual(refused, { refused: sentence }, name);
        assert.equal(JSON.stringify(log.lines).includes('4242'), false, `${name}: nothing of Reply's words is logged`);
    }
    for (const sentence of Object.values(said)) assert.equal(sentence.includes('4242'), false);
});

test('a mailbox is connected through the account’s hosted page: Gmail’s, Outlook’s or the page itself, by the checked token', async (t) => {
    for (const data of [{ url: mailboxPage }, mailboxPage]) {
        const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_MAILBOX: answer(200, data) } });
        const setup = setupOn(b);
        assert.deepEqual(await setup.replyMailboxLink('gmail'), {
            kind: 'mailbox',
            link: `https://run.reply.io/hosted/eac/ConnectWithGMail?uidToken=${token}`,
            host: 'run.reply.io',
            expiresAtUtc: null,
        });
        assert.equal(((await setup.replyMailboxLink('outlook')) as { link: string }).link, `https://run.reply.io/hosted/eac/ConnectWithOffice?uidToken=${token}&isExchange=false`);
        assert.equal(((await setup.replyMailboxLink('hosted')) as { link: string }).link, mailboxPage);
        assert.deepEqual(apiCalls(b).map((entry) => entry.args), Array.from({ length: 3 }, () => ['api', '/v1/SharedPageUrl', '--json']));
    }
    const white = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_MAILBOX: answer(200, { url: `https://mail.example-agency.test/hosted/eac/index/${token}` }) } });
    assert.equal(((await setupOn(white).replyMailboxLink('gmail')) as { host: string }).host, 'mail.example-agency.test');
});

test('one link is asked at a time: a second click while one is asked is refused', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_MAILBOX: answer(200, { url: mailboxPage }) } });
    const setup = setupOn(b);
    const [first, second] = await Promise.all([setup.replyMailboxLink('gmail'), setup.replyLinkedInConnect('x')]);
    assert.equal((first as { kind: string }).kind, 'mailbox');
    assert.deepEqual(second, { refused: said.busy });
    assert.equal(((await setup.replyMailboxLink('hosted')) as { link: string }).link, mailboxPage, 'the next is asked once the first has answered');
});

test('the address is a secret: no log line, no file of the installation and no refusal carries it, nor the name typed', async (t) => {
    const pending = [{ id: 41, name: 'Made-up one', connectionLink: connectLink, expiresAtUtc: '2026-11-19T04:59:00Z', isExpired: false }];
    const b = bench(t, {
        programs: ['reply'],
        env: {
            ...signedIn,
            STUB_REPLY_API_CONNECT: answer(201, { connectionLink: connectLink, pendingAccountId: 41 }),
            STUB_REPLY_API_PENDING: answer(200, pending),
            STUB_REPLY_API_RECONNECT: answer(200, { connectionLink: reconnectLink }),
            STUB_REPLY_API_MAILBOX: answer(200, { url: mailboxPage }),
        },
    });
    const log = fileLogger(b.paths.logs);
    const setup = setupOn(b, log);
    const answered = [
        await setup.replyLinkedInConnect('Made-up secret name'),
        await setup.replyLinkedInPendingLink('41'),
        await setup.replyLinkedInReconnect('77'),
        await setup.replyMailboxLink('gmail'),
        await setup.replyMailboxLink('outlook'),
        await setup.replyMailboxLink('hosted'),
        await setup.status(),
    ];
    assert.ok(answered.slice(0, 6).every((each) => 'link' in (each as object)), 'each act answered its link');
    const secrets = [hex, token, 'made-up-path', 'Made-up secret name'];
    const files = snapshot(b.root);
    files.delete('record.jsonl');
    assert.ok([...files.keys()].some((path) => path.startsWith('data/logs/')), 'the log was written');
    for (const path of files.keys()) {
        const text = readFileSync(join(b.root, path), 'latin1');
        for (const secret of secrets) assert.equal(text.includes(secret), false, `${path} carries ${secret}`);
    }
    const logged = [...snapshot(b.paths.logs).keys()]
        .flatMap((name) => readFileSync(join(b.paths.logs, name), 'utf8').split(/\r?\n/).filter(Boolean))
        .map((line) => JSON.parse(line) as Record<string, unknown>)
        .filter((line) => line['EventName'] === 'reply_api_answered')
        .map((line) => [line['Act'], line['Status'], line['Exit']]);
    assert.deepEqual(logged.slice(0, 3), [['linkedin_connect', 201, 0], ['linkedin_pending', 200, 0], ['linkedin_reconnect', 200, 0]]);
    assert.ok(logged.some(([act]) => act === 'mailbox_page'));
});

// ---- The fix round: the floor, the confirmation, the time limit ------------------------------------------------------

test('the LinkedIn acts need reply-cli 0.20.0, which reads a body from standard input; below it they are refused and the card says so', async (t) => {
    const old = bench(t, {
        programs: ['reply'],
        env: { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_VERSION: '0.19.0', STUB_REPLY_API_MAILBOX: answer(200, { url: mailboxPage }) },
    });
    const setup = setupOn(old);
    assert.deepEqual(await setup.replyLinkedInConnect('x'), { refused: said.linkedInFloor });
    assert.deepEqual(await setup.replyLinkedInReconnect('77'), { refused: said.linkedInFloor });
    assert.deepEqual(await setup.replyLinkedInPendingLink('41'), { refused: said.linkedInFloor });
    // Jason's Reply plugin needs 0.20.0 too, so below it the card offers the CLI's update and says nothing of LinkedIn.
    assert.equal((await setup.status()).reply.linkedinBelowFloor, null);
    assert.deepEqual(apiCalls(old).filter((entry) => entry.args[1] !== '/v3/linkedin-accounts/pending'), [], 'no LinkedIn act was asked');
    // The mailbox act keeps the card's own floor.
    assert.equal(((await setup.replyMailboxLink('hosted')) as { link: string }).link, mailboxPage);

    const current = bench(t, { programs: ['reply'], env: signedIn });
    assert.equal((await setupOn(current).status()).reply.linkedinBelowFloor, null);
    const out = bench(t, { programs: ['reply'], env: { STUB_REPLY_VERSION: '0.19.0' } });
    assert.equal((await setupOn(out).status()).reply.linkedinBelowFloor, null, 'nothing to say while the card offers no act');
});

test('the mailbox link is asked of Reply only after the main process’s own confirmation, each time, and a cancel asks nothing', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_MAILBOX: answer(200, { url: mailboxPage }) } });
    let asked = 0;
    let yes = false;
    const setup = setupOn(b, memoryLog(), { confirm: () => Promise.resolve((asked += 1, yes)) });
    assert.deepEqual(await setup.replyMailboxLink('gmail'), { refused: said.mailboxNotConfirmed });
    assert.deepEqual(apiCalls(b), [], 'cancelled: no call was made');
    yes = true;
    assert.equal(((await setup.replyMailboxLink('gmail')) as { kind: string }).kind, 'mailbox');
    assert.equal(((await setup.replyMailboxLink('hosted')) as { kind: string }).kind, 'mailbox');
    assert.equal(asked, 3, 'asked each time');
    assert.equal(apiCalls(b).length, 2);
    // A LinkedIn link needs none: it expires and connects one account.
    const linked = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_CONNECT: answer(201, { connectionLink: connectLink }) } });
    let linkedAsked = 0;
    assert.equal(((await setupOn(linked, memoryLog(), { confirm: () => Promise.resolve((linkedAsked += 1, false)) }).replyLinkedInConnect('x')) as { kind: string }).kind, 'linkedin');
    assert.equal(linkedAsked, 0);
    // A desktop with no way to confirm asks nothing.
    const none = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_REPLY_API_MAILBOX: answer(200, { url: mailboxPage }) } });
    assert.deepEqual(await setupOn(none, memoryLog(), { confirm: null }).replyMailboxLink('gmail'), { refused: said.mailboxNotConfirmed });
    assert.deepEqual(apiCalls(none), []);
});

test('the confirmation is the main process’s native box: its words, Cancel focused, only the first button is yes, and no window is no', async () => {
    assert.deepEqual(mailboxConfirmation, {
        type: 'question',
        title: 'Show the link for connecting mailboxes?',
        message: 'Show the link for connecting mailboxes?',
        detail: "Anyone who has this link can add and manage this Reply account's mailboxes, and it does not expire.",
        buttons: ['Show the link', 'Cancel'],
        defaultId: 1,
        cancelId: 1,
        noLink: true,
    });
    const shown: unknown[] = [];
    const answering = (response: number) => (window: string, options: unknown) => {
        shown.push([window, options]);
        return Promise.resolve({ response });
    };
    assert.equal(await askMailboxConfirmation('window', answering(0)), true);
    for (const response of [1, 2, -1]) assert.equal(await askMailboxConfirmation('window', answering(response)), false, String(response));
    assert.deepEqual(shown[0], ['window', mailboxConfirmation]);
    const before = shown.length;
    assert.equal(await askMailboxConfirmation(null, answering(0)), false);
    assert.equal(shown.length, before, 'no window: nothing is shown, and the answer is no');
});

test('a call is stopped at thirty seconds, and a connect stopped there says Check now shows whether one was started', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { ...signedIn, STUB_HANG: 'reply' } });
    const limits: (number | undefined)[] = [];
    // The stand-in hangs; its version is answered here, and the limit the desktop asks for is kept and shortened.
    const programs: Programs = {
        find: (name) => b.programs.find(name),
        run: (name, args, options) => {
            if (args[0] === '--version') return Promise.resolve({ exit: 0, stdout: 'reply-cli 0.20.0', stderr: '' });
            limits.push(options?.timeoutMs);
            return b.programs.run(name, args, { ...options, timeoutMs: 1_000 });
        },
    };
    const setup = setupOn(b, memoryLog(), { programs });
    assert.deepEqual(await setup.replyLinkedInConnect('x'), { refused: said.noAnswerInTime });
    assert.deepEqual(await setup.replyMailboxLink('gmail'), { refused: said.unreached });
    assert.equal(replyApiLimitMs, 30_000);
    assert.deepEqual(limits, [30_000, 30_000]);
});
