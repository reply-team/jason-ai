// What is counted for a request, and that counting never decides the answer.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clientOf, sourceOf } from '../src/classify.mjs';
import { BASE, fakeDb, fakeFetch, send } from '../fakes.mjs';
import { INSTALL_PAGE } from '../src/paths.mjs';

const page = () => new Response('# Install\n', { status: 200 });

test('a source is the first ref, lowercased, kept when it is 1 to 40 letters, digits and hyphens', () => {
  const of = (query) => sourceOf(new URLSearchParams(query));
  assert.equal(of(''), 'none');
  assert.equal(of('ref='), 'none');
  assert.equal(of('ref=linkedin'), 'linkedin');
  assert.equal(of('ref=LinkedIn-Ads-2026'), 'linkedin-ads-2026');
  assert.equal(of('ref=a&ref=b'), 'a');
  assert.equal(of(`ref=${'a'.repeat(40)}`), 'a'.repeat(40));
  assert.equal(of(`ref=${'a'.repeat(41)}`), 'other');
  assert.equal(of('ref=news_letter'), 'other');
  assert.equal(of('ref=%3Cscript%3E'), 'other');
  assert.equal(of('ref=caf%C3%A9'), 'other');
  assert.equal(of('utm_source=x'), 'none');
});

test('a client is a bot first, then a fetcher by its own word, then a browser, and a fetcher without a word', () => {
  const rows = [
    [null, 'fetcher'],
    ['', 'fetcher'],
    ['Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)', 'bot'],
    ['facebookexternalhit/1.1', 'bot'],
    ['Slackbot-LinkExpanding 1.0', 'bot'],
    ['WhatsApp/2.23', 'bot'],
    ['Mozilla/5.0 (compatible; GPTBot/1.2)', 'bot'],
    ['Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko); compatible; Claude-User/1.0', 'fetcher'],
    ['Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; ChatGPT-User/1.0)', 'fetcher'],
    ['claude-code/2.1.0', 'fetcher'],
    ['codex_cli_rs/0.40.0', 'fetcher'],
    ['curl/8.7.1', 'fetcher'],
    ['Wget/1.21.4', 'fetcher'],
    ['Mozilla/5.0 (Windows NT 10.0; Microsoft Windows 10.0.26100; en-US) PowerShell/7.5.0', 'fetcher'],
    ['python-requests/2.32.3', 'fetcher'],
    ['node', 'fetcher'],
    ['undici', 'fetcher'],
    ['axios/1.7.2', 'fetcher'],
    ['Go-http-client/2.0', 'fetcher'],
    ['okhttp/4.12.0', 'fetcher'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15', 'browser'],
    ['Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0', 'browser'],
    ['SomethingElse/1.0', 'fetcher'],
  ];
  for (const [agent, expected] of rows) assert.equal(clientOf(agent), expected, String(agent));
});

test('a GET of the install page is counted once, by day, path, source and client', async () => {
  const db = fakeDb();
  await send('/install?ref=LinkedIn', { env: { COUNTS: db }, headers: { 'user-agent': 'curl/8.7.1' }, fetch: fakeFetch({ [INSTALL_PAGE]: page }) });
  assert.equal(db.runs.length, 1);
  assert.match(db.runs[0].sql, /^INSERT INTO counts \(day, path, ref, client, n\) VALUES \(\?1, \?2, \?3, \?4, 1\) ON CONFLICT/);
  assert.deepEqual(db.runs[0].args, ['2026-11-12', '/install', 'linkedin', 'fetcher']);
});

test('a GET of a desktop path is counted once, and the day is the UTC day', async () => {
  const db = fakeDb();
  await send('/desktop/mac-intel', {
    env: { COUNTS: db },
    headers: { 'user-agent': 'Mozilla/5.0 (Macintosh) Safari/605.1.15' },
    now: new Date('2026-11-12T23:59:59-07:00'),
  });
  assert.deepEqual(db.runs.map((run) => run.args), [['2026-11-13', '/desktop/mac-intel', 'none', 'browser']]);
});

test('HEAD is answered as GET is and never counted', async () => {
  const db = fakeDb();
  const desktop = await send('/desktop/windows', { method: 'HEAD', env: { COUNTS: db } });
  assert.equal(desktop.response.status, 302);
  const install = await send('/install', { method: 'HEAD', env: { COUNTS: db }, fetch: fakeFetch({ [INSTALL_PAGE]: page }) });
  assert.equal(install.response.status, 200);
  assert.equal(install.response.headers.get('content-type'), 'text/plain; charset=utf-8');
  assert.equal(await install.response.text(), '');
  assert.equal(db.runs.length, 0);
});

test('a store that throws, or is missing, still answers the download and the page', async () => {
  for (const env of [{ COUNTS: fakeDb({ failing: true }) }, {}]) {
    const desktop = await send('/desktop/linux-deb', { env });
    assert.equal(desktop.response.status, 302);
    assert.ok(desktop.ctx.pending.length <= 1);
    const install = await send('/install', { env, fetch: fakeFetch({ [INSTALL_PAGE]: page }) });
    assert.equal(install.response.status, 200);
    assert.equal(await install.response.text(), '# Install\n');
  }
});

test('the count is left to the context, after the answer, rather than awaited before it', async () => {
  let release;
  const slow = new Promise((resolve) => {
    release = resolve;
  });
  const db = fakeDb();
  const prepare = db.prepare;
  db.prepare = (sql) => {
    const statement = prepare(sql);
    return { bind: (...args) => ({ run: async () => { await slow; return statement.bind(...args).run(); } }) };
  };
  const { handle } = await import('../src/index.mjs');
  const pending = [];
  const response = await handle(new Request(BASE + '/desktop/windows'), { COUNTS: db }, { waitUntil: (promise) => pending.push(promise) }, {
    fetch: fakeFetch({}),
    now: () => new Date('2026-11-12T10:00:00Z'),
  });
  assert.equal(response.status, 302);
  assert.equal(pending.length, 1);
  assert.equal(db.runs.length, 0);
  release();
  await Promise.all(pending);
  assert.equal(db.runs.length, 1);
});

test('the stats page and refused requests are never counted', async () => {
  const db = fakeDb();
  await send('/stats', { env: { COUNTS: db } });
  await send('/install', { method: 'POST', env: { COUNTS: db } });
  await send('/nothing', { env: { COUNTS: db } });
  await send('/desktop/windows/', { env: { COUNTS: db } });
  assert.equal(db.runs.length, 0);
});
