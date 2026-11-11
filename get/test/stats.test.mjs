// The counts page: only a request Access signed for this application reads it, and every value it prints is escaped.

import { beforeEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { AUD, TEAM, accessKey, accessToken, certs, fakeDb, fakeFetch, send } from '../fakes.mjs';
import { forgetKeys } from '../src/access.mjs';

const key = await accessKey('k-current');
const other = await accessKey('k-other');
const env = (rows = []) => ({ COUNTS: fakeDb({ rows }), ACCESS_TEAM: TEAM, ACCESS_AUD: AUD });

beforeEach(() => forgetKeys());

async function stats(token, { environment = env(), fetch = fakeFetch(certs(key)), path = '/stats' } = {}) {
  const headers = token === undefined ? {} : { 'cf-access-jwt-assertion': token };
  return (await send(path, { headers, env: environment, fetch })).response;
}

test('a token Access signed for this application, in its time, reads the page', async () => {
  const response = await stats(await accessToken(key));
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('content-type'), 'text/html; charset=utf-8');
  assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.match(response.headers.get('content-security-policy'), /default-src 'none'/);
});

test('every other request is refused with 403', async () => {
  const now = Math.floor(new Date('2026-11-12T10:00:00Z').getTime() / 1000);
  const refused = {
    'no token': undefined,
    'an empty token': '',
    'not a token': 'abc.def',
    'another key': await accessToken(other, {}, { kid: 'k-current' }),
    'a key the team does not list': await accessToken(other),
    'another audience': await accessToken(key, { aud: ['someone-else'] }),
    'no audience': await accessToken(key, { aud: undefined }),
    'another issuer': await accessToken(key, { iss: 'https://another-team.cloudflareaccess.com' }),
    expired: await accessToken(key, { exp: now - 1 }),
    'no expiry': await accessToken(key, { exp: undefined }),
    'not yet valid': await accessToken(key, { nbf: now + 3600 }),
    'another algorithm': await accessToken(key, {}, { alg: 'HS256' }),
    'no algorithm': await accessToken(key, {}, { alg: 'none' }),
  };
  for (const [why, token] of Object.entries(refused)) {
    const response = await stats(token);
    assert.equal(response.status, 403, why);
    assert.equal(await response.text(), 'Forbidden.\n', why);
  }
});

test('without the team or the audience configured, the page is refused rather than open', async () => {
  const token = await accessToken(key);
  for (const environment of [{ COUNTS: fakeDb(), ACCESS_AUD: AUD }, { COUNTS: fakeDb(), ACCESS_TEAM: TEAM }, { COUNTS: fakeDb(), ACCESS_TEAM: 'a/b', ACCESS_AUD: AUD }]) {
    assert.equal((await stats(token, { environment })).status, 403);
  }
});

test('an audience given as one string is accepted as Access may write it', async () => {
  assert.equal((await stats(await accessToken(key, { aud: AUD }))).status, 200);
});

test('the team keys are read once and kept', async () => {
  const fetch = fakeFetch(certs(key));
  const token = await accessToken(key);
  assert.equal((await stats(token, { fetch })).status, 200);
  assert.equal((await stats(token, { fetch })).status, 200);
  assert.equal(fetch.asked.length, 1);
  assert.equal(fetch.asked[0].address, `https://${TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`);
});

test('a page of counts: installs that are not bots as the headline, the classes beside it, desktop by system', async () => {
  const rows = [
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'fetcher', n: 5 },
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'browser', n: 2 },
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'bot', n: 9 },
    { day: '2026-11-12', path: '/desktop/windows', ref: 'linkedin', client: 'browser', n: 3 },
    { day: '2026-11-12', path: '/desktop/mac', ref: 'linkedin', client: 'bot', n: 4 },
    { day: '2026-11-11', path: '/desktop/linux-appimage', ref: 'none', client: 'fetcher', n: 1 },
  ];
  const environment = env(rows);
  const response = await send('/stats?format=csv', { headers: { 'cf-access-jwt-assertion': await accessToken(key) }, env: environment, fetch: fakeFetch(certs(key)) });
  assert.equal(response.response.status, 200);
  assert.equal(response.response.headers.get('content-type'), 'text/csv; charset=utf-8');
  assert.deepEqual((await response.response.text()).trim().split('\n'), [
    'day,source,installs,installs_fetcher,installs_browser,windows,mac,mac_intel,linux_deb,linux_appimage,bots',
    '2026-11-12,linkedin,7,5,2,3,0,0,0,0,13',
    '2026-11-11,none,0,0,0,0,0,0,0,1,0',
  ]);
  assert.deepEqual(environment.COUNTS.reads[0].args, ['2026-08-15']);
});

test('every value the page prints is escaped, though each comes from a fixed set', async () => {
  const rows = [{ day: '2026-11-12<i>', path: '/install', ref: '<script>"&\'', client: 'fetcher', n: 1 }];
  const html = await (await stats(await accessToken(key), { environment: env(rows) })).text();
  assert.ok(!html.includes('<script>'));
  assert.ok(!html.includes('<i>'));
  assert.ok(html.includes('&lt;script&gt;&quot;&amp;&#39;'));
  const csv = await (await stats(await accessToken(key), { environment: env(rows), path: '/stats?format=csv' })).text();
  assert.ok(csv.includes('"<script>""&\'"'));
});

test('a store that cannot be read answers 503 with a fixed text', async () => {
  const response = await stats(await accessToken(key), { environment: { COUNTS: fakeDb({ failing: true }), ACCESS_TEAM: TEAM, ACCESS_AUD: AUD } });
  assert.equal(response.status, 503);
  assert.equal(await response.text(), 'The counts could not be read.\n');
});
