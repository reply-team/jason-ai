// The counts page: only a request Access signed for this application reads it, its read is bounded, and every value it
// prints is escaped.

import { beforeEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { AUD, TEAM, accessKey, accessToken, certs, fakeDb, fakeFetch, send, sqliteDb } from '../fakes.mjs';
import { forgetKeys } from '../src/access.mjs';
import { readCounts, renderHtml, tabulate } from '../src/stats.mjs';

const key = await accessKey('k-current');
const other = await accessKey('k-other');
const T = new Date('2026-11-12T10:00:00Z');
const at = (minutes) => new Date(T.getTime() + minutes * 60 * 1000);
const seconds = (moment) => Math.floor(moment.getTime() / 1000);

/** A store holding the rows given, each as the table keeps it. */
function store(rows = []) {
  const db = sqliteDb();
  const insert = db.database.prepare('INSERT INTO counts (day, path, ref, client, n) VALUES (?, ?, ?, ?, ?)');
  for (const { day, path, ref, client, n } of rows) insert.run(day, path, ref, client, n);
  return db;
}

const env = (db = store()) => ({ COUNTS: db, ACCESS_TEAM: TEAM, ACCESS_AUD: AUD });

beforeEach(() => forgetKeys());

async function stats(token, { environment = env(), fetch = fakeFetch(certs(key)), path = '/stats', now = T } = {}) {
  const headers = token === undefined ? {} : { 'cf-access-jwt-assertion': token };
  return (await send(path, { headers, env: environment, fetch, now })).response;
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
  const now = seconds(T);
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

test('when the team keys cannot be read, the page is refused', async () => {
  const address = `https://${TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`;
  const answers = {
    'an answer other than 200, even one listing the key': () => new Response(JSON.stringify({ keys: [key.jwk] }), { status: 503 }),
    'a network error': new Error('unreachable'),
    'a body that is not JSON': () => new Response('<html>', { status: 200 }),
    'JSON with no keys': () => new Response('{"public_cert":{}}', { status: 200 }),
  };
  const token = await accessToken(key);
  for (const [why, answer] of Object.entries(answers)) {
    forgetKeys();
    const response = await stats(token, { fetch: fakeFetch({ [address]: answer }) });
    assert.equal(response.status, 403, why);
  }
});

test('without the team or the audience configured, the page is refused rather than open', async () => {
  const token = await accessToken(key);
  for (const environment of [{ COUNTS: store(), ACCESS_AUD: AUD }, { COUNTS: store(), ACCESS_TEAM: TEAM }, { COUNTS: store(), ACCESS_TEAM: 'a/b', ACCESS_AUD: AUD }]) {
    assert.equal((await stats(token, { environment })).status, 403);
  }
});

test('the team name is lowercased before use, as Access writes it in the issuer', async () => {
  const response = await stats(await accessToken(key), { environment: { COUNTS: store(), ACCESS_TEAM: TEAM.toUpperCase(), ACCESS_AUD: AUD } });
  assert.equal(response.status, 200);
});

test('an audience given as one string is accepted as Access may write it', async () => {
  assert.equal((await stats(await accessToken(key, { aud: AUD }))).status, 200);
});

test('the team keys are kept for an hour, then read again', async () => {
  const fetch = fakeFetch(certs(key));
  const token = await accessToken(key, { exp: seconds(at(180)) });
  assert.equal((await stats(token, { fetch, now: T })).status, 200);
  assert.equal((await stats(token, { fetch, now: at(59) })).status, 200);
  assert.equal(fetch.asked.length, 1);
  assert.equal((await stats(token, { fetch, now: at(61) })).status, 200);
  assert.equal(fetch.asked.length, 2);
  assert.equal(fetch.asked[0].address, `https://${TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`);
});

test('a key the kept keys lack is looked for again at most every five minutes, and a rotated key is then taken', async () => {
  const rotated = await accessKey('k-rotated');
  let listed = [key];
  const address = `https://${TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`;
  const fetch = fakeFetch({ [address]: () => new Response(JSON.stringify({ keys: listed.map((one) => one.jwk) })) });
  const old = await accessToken(key, { exp: seconds(at(180)) });
  const fresh = await accessToken(rotated, { exp: seconds(at(180)) });

  assert.equal((await stats(old, { fetch, now: T })).status, 200);
  listed = [rotated, key];
  assert.equal((await stats(fresh, { fetch, now: at(1) })).status, 403);
  assert.equal((await stats(fresh, { fetch, now: at(4) })).status, 403);
  assert.equal(fetch.asked.length, 1);
  assert.equal((await stats(fresh, { fetch, now: at(6) })).status, 200);
  assert.equal(fetch.asked.length, 2);
  assert.equal((await stats(old, { fetch, now: at(7) })).status, 200);
  assert.equal(fetch.asked.length, 2);
});

test('a page of counts: installs that are not bots as the headline, the classes beside it, desktop by system', async () => {
  const db = store([
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'fetcher', n: 5 },
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'browser', n: 2 },
    { day: '2026-11-12', path: '/install', ref: 'linkedin', client: 'bot', n: 9 },
    { day: '2026-11-12', path: '/desktop/windows', ref: 'linkedin', client: 'browser', n: 3 },
    { day: '2026-11-12', path: '/desktop/mac', ref: 'linkedin', client: 'bot', n: 4 },
    { day: '2026-11-11', path: '/desktop/linux-appimage', ref: '(none)', client: 'fetcher', n: 1 },
    { day: '2026-08-14', path: '/install', ref: 'old', client: 'fetcher', n: 7 },
  ]);
  const response = await stats(await accessToken(key), { environment: env(db), path: '/stats?format=csv' });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('content-type'), 'text/csv; charset=utf-8');
  assert.deepEqual((await response.text()).trim().split('\n'), [
    'day,source,installs,installs_fetcher,installs_browser,windows,mac,mac_intel,linux_deb,linux_appimage,bots',
    '2026-11-12,linkedin,7,5,2,3,0,0,0,0,13',
    '2026-11-11,"(none)",0,0,0,0,0,0,0,1,0',
  ]);
  assert.ok(db.reads.every((read) => read.args[0] === '2026-08-15'));
});

test('the page keeps the sources with the most requests and folds the rest into one row, saying how many', async () => {
  const rows = [];
  for (let i = 0; i < 205; i += 1) rows.push({ day: '2026-11-12', path: '/install', ref: `s${String(i).padStart(3, '0')}`, client: 'fetcher', n: 1000 - i });
  const db = store(rows);
  const read = await readCounts(db, T);
  assert.equal(read.folded, 5);
  assert.equal(read.truncated, false);
  const lines = tabulate(read.rows);
  assert.equal(lines.length, 201);
  const rest = lines.find((line) => line.source === '(rest)');
  assert.equal(rest.installs, 800 + 799 + 798 + 797 + 796);
  assert.ok(!lines.some((line) => ['s200', 's204'].includes(line.source)));
  assert.ok(lines.some((line) => line.source === 's199'));
  const html = renderHtml(lines, T, read);
  assert.match(html, /5 sources with fewer requests are folded into the row \(rest\)/);
});

test('the read stops at its row limit, and the page says so', async () => {
  const rows = [];
  for (let day = 1; day <= 9; day += 1) rows.push({ day: `2026-11-0${day}`, path: '/install', ref: 'x', client: 'fetcher', n: 1 });
  const read = await readCounts(store(rows), T, { limit: 4 });
  assert.equal(read.rows.length, 4);
  assert.equal(read.truncated, true);
  assert.deepEqual(read.rows.map((row) => row.day), ['2026-11-09', '2026-11-08', '2026-11-07', '2026-11-06']);
  assert.match(renderHtml(tabulate(read.rows), T, read), /stopped at its limit of 4 rows/);
  assert.match((await stats(await accessToken(key))).headers.get('content-type'), /text\/html/);
});

test('the page says its numbers are an upper bound', async () => {
  const html = await (await stats(await accessToken(key))).text();
  assert.match(html, /upper bound/);
  assert.match(html, /link previews and mail scanners/);
});

test('every value the page prints is escaped, though each comes from a fixed set', async () => {
  const db = store([{ day: '2026-11-12', path: '/install', ref: '<script>"&\'', client: 'fetcher', n: 1 }]);
  const html = await (await stats(await accessToken(key), { environment: env(db) })).text();
  assert.ok(!html.includes('<script>'));
  assert.ok(html.includes('&lt;script&gt;&quot;&amp;&#39;'));
  const csv = await (await stats(await accessToken(key), { environment: env(db), path: '/stats?format=csv' })).text();
  assert.ok(csv.includes('"<script>""&\'"'));
});

test('a source a spreadsheet would read as a formula is kept as text in the CSV', async () => {
  const db = store([
    { day: '2026-11-12', path: '/install', ref: '-x', client: 'fetcher', n: 1 },
    { day: '2026-11-12', path: '/install', ref: '-1', client: 'fetcher', n: 2 },
  ]);
  const csv = await (await stats(await accessToken(key), { environment: env(db), path: '/stats?format=csv' })).text();
  assert.ok(csv.includes(`"'-x"`));
  assert.ok(csv.includes(`"'-1"`));
});

test('a store that cannot be read answers 503 with a fixed text', async () => {
  const response = await stats(await accessToken(key), { environment: { COUNTS: fakeDb({ failing: true }), ACCESS_TEAM: TEAM, ACCESS_AUD: AUD } });
  assert.equal(response.status, 503);
  assert.equal(await response.text(), 'The counts could not be read.\n');
});
