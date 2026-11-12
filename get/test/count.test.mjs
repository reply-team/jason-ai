// What is counted for a request, and that counting never decides the answer.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clientOf, sourceOf } from '../src/classify.mjs';
import { BASE, GUIDE, fakeDb, fakeFetch, send, sqliteDb } from '../fakes.mjs';
import { INSTALL_PAGE } from '../src/paths.mjs';
import { MAX_SOURCES_PER_DAY } from '../src/index.mjs';
import { FOLDED_SOURCE, readCounts, tabulate } from '../src/stats.mjs';

const page = () => new Response(GUIDE, { status: 200 });

test('a source is the first ref, lowercased, kept when it is 1 to 40 letters, digits and hyphens', () => {
  const of = (query) => sourceOf(new URLSearchParams(query));
  assert.equal(of(''), '(none)');
  assert.equal(of('ref='), '(none)');
  assert.equal(of('ref=linkedin'), 'linkedin');
  assert.equal(of('ref=LinkedIn-Ads-2026'), 'linkedin-ads-2026');
  assert.equal(of('ref=a&ref=b'), 'a');
  assert.equal(of(`ref=${'a'.repeat(40)}`), 'a'.repeat(40));
  assert.equal(of(`ref=${'a'.repeat(41)}`), '(other)');
  assert.equal(of('ref=news_letter'), '(other)');
  assert.equal(of('ref=%3Cscript%3E'), '(other)');
  assert.equal(of('ref=caf%C3%A9'), '(other)');
  assert.equal(of('ref=(none)'), '(other)');
  assert.equal(of('utm_source=x'), '(none)');
});

test('a source named none or other is a source of its own, never the reserved rows', () => {
  const of = (query) => sourceOf(new URLSearchParams(query));
  assert.equal(of('ref=none'), 'none');
  assert.equal(of('ref=other'), 'other');
  assert.notEqual(of('ref=none'), of(''));
  assert.notEqual(of('ref=other'), of('ref=bad_one'));
});

// The agents' own fetchers, by the strings their makers publish, fetched from these pages on 2026-11-11:
// - OpenAI, https://platform.openai.com/docs/bots : ChatGPT-User's full string, which ends in a URL with "bot" in it.
// - Perplexity, https://docs.perplexity.ai/guides/bots : Perplexity-User's example string ("the version number may change").
// - Anthropic, https://support.claude.com/en/articles/8896518-does-anthropic-crawl-data-from-the-web-and-how-can-site-owners-block-the-crawler :
//   names the agent "Claude-User" ("it may access websites using a Claude-User agent") and publishes no fuller string,
//   so the row is that name alone.
const published = [
  ['Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko); compatible; ChatGPT-User/1.0; +https://openai.com/bot', 'fetcher'],
  ['Mozilla/5.0 (compatible; Perplexity-User/1.0; +https://perplexity.ai/perplexity-user)', 'fetcher'],
  ['Claude-User', 'fetcher'],
];

test("an agent's own fetcher, by its maker's published string, is a fetcher even where the string names a bot", () => {
  for (const [agent, expected] of published) assert.equal(clientOf(agent), expected, agent);
});

test("a client is an agent's named fetcher first, then a bot, then a command line's fetcher, then a browser", () => {
  // Shapes of the rule rather than vendors' strings: each row is written to land on one branch.
  const rows = [
    [null, 'fetcher'],
    ['', 'fetcher'],
    ['Mozilla/5.0 (compatible; Claude-User/1.0; +https://example.com/bot)', 'fetcher'],
    ['claude-code/2.1.0 (+https://example.com/bot)', 'fetcher'],
    ['codex_cli_rs/0.40.0', 'fetcher'],
    ['Mozilla/5.0 (compatible; ExampleBot/2.1; +https://example.com/bot.html)', 'bot'],
    ['facebookexternalhit/1.1', 'bot'],
    ['Slackbot-LinkExpanding 1.0', 'bot'],
    ['WhatsApp/2.23', 'bot'],
    ['python-requests/2.32.3 examplebot', 'bot'],
    ['Go-http-client/2.0 (crawler)', 'bot'],
    ['curl/8.7.1', 'fetcher'],
    ['Wget/1.21.4', 'fetcher'],
    ['Mozilla/5.0 (Windows NT 10.0; en-US) PowerShell/7.5.0', 'fetcher'],
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
  assert.equal(
    db.runs[0].sql,
    [
      'INSERT INTO counts (day, path, ref, client, n)',
      'SELECT ?1, ?2,',
      '  CASE WHEN EXISTS (SELECT 1 FROM counts WHERE day = ?1 AND ref = ?3)',
      "         OR (SELECT COUNT(DISTINCT ref) FROM counts WHERE day = ?1 AND ref <> '(rest)') < ?5",
      "       THEN ?3 ELSE '(rest)' END,",
      '  ?4, 1',
      'WHERE true',
      'ON CONFLICT (day, path, ref, client) DO UPDATE SET n = n + 1',
    ].join(String.fromCharCode(10)),
  );
  assert.deepEqual(db.runs[0].args, ['2026-11-12', '/install', 'linkedin', 'fetcher', MAX_SOURCES_PER_DAY]);
});

test('a GET of a desktop path is counted once, and the day is the UTC day', async () => {
  const db = fakeDb();
  await send('/desktop/mac-intel', {
    env: { COUNTS: db },
    headers: { 'user-agent': 'Mozilla/5.0 (Macintosh) Safari/605.1.15' },
    now: new Date('2026-11-12T23:59:59-07:00'),
  });
  assert.deepEqual(db.runs.map((run) => run.args), [['2026-11-13', '/desktop/mac-intel', '(none)', 'browser', MAX_SOURCES_PER_DAY]]);
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
    assert.equal(await install.response.text(), GUIDE);
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

test('the one statement the handler writes moves a counter on a real table, and a new source is a row of its own', async () => {
  const db = sqliteDb();
  for (const path of ['/desktop/windows?ref=news', '/desktop/windows?ref=news', '/desktop/windows?ref=blog']) {
    await send(path, { env: { COUNTS: db }, headers: { 'user-agent': 'curl/8.7.1' } });
  }
  const rows = db.database.prepare('SELECT day, path, ref, client, n FROM counts ORDER BY ref').all().map((row) => ({ ...row }));
  assert.deepEqual(rows, [
    { day: '2026-11-12', path: '/desktop/windows', ref: 'blog', client: 'fetcher', n: 1 },
    { day: '2026-11-12', path: '/desktop/windows', ref: 'news', client: 'fetcher', n: 2 },
  ]);
});

const rowsOf = (db, day = '2026-11-12') =>
  db.database.prepare('SELECT ref, SUM(n) AS n FROM counts WHERE day = ? GROUP BY ref ORDER BY ref').all(day).map((row) => ({ ...row }));
const flood = async (db, count, { from = 0 } = {}) => {
  for (let i = from; i < from + count; i++) await send('/desktop/windows?ref=made-up-' + i, { env: { COUNTS: db } });
};

test('the first source past the day cap is counted in the rest row, and no row of its own is made', async () => {
  const db = sqliteDb();
  await flood(db, MAX_SOURCES_PER_DAY);
  assert.equal(rowsOf(db).length, MAX_SOURCES_PER_DAY);
  await send('/desktop/windows?ref=one-too-many', { env: { COUNTS: db } });
  await send('/install?ref=another-one', { env: { COUNTS: db }, fetch: fakeFetch({ [INSTALL_PAGE]: page }) });
  const rows = rowsOf(db);
  assert.equal(rows.length, MAX_SOURCES_PER_DAY + 1);
  assert.deepEqual(rows.find((row) => row.ref === FOLDED_SOURCE), { ref: FOLDED_SOURCE, n: 2 });
  assert.equal(rows.some((row) => row.ref === 'one-too-many' || row.ref === 'another-one'), false);
});

test('a source seen before the cap keeps counting under its own name after it', async () => {
  const db = sqliteDb();
  await flood(db, MAX_SOURCES_PER_DAY);
  await flood(db, 3, { from: MAX_SOURCES_PER_DAY });
  await send('/desktop/windows?ref=made-up-0', { env: { COUNTS: db } });
  await send('/desktop/windows?ref=made-up-0', { env: { COUNTS: db } });
  const rows = rowsOf(db);
  assert.deepEqual(rows.find((row) => row.ref === 'made-up-0'), { ref: 'made-up-0', n: 3 });
  assert.deepEqual(rows.find((row) => row.ref === FOLDED_SOURCE), { ref: FOLDED_SOURCE, n: 3 });
  assert.equal(rows.length, MAX_SOURCES_PER_DAY + 1);
});

test('a new UTC day starts with no sources counted', async () => {
  const db = sqliteDb();
  await flood(db, MAX_SOURCES_PER_DAY + 5);
  await send('/desktop/windows?ref=fresh', { env: { COUNTS: db }, now: new Date('2026-11-13T00:00:01Z') });
  assert.deepEqual(rowsOf(db, '2026-11-13'), [{ ref: 'fresh', n: 1 }]);
});

test('a source is one source of the day however many paths and client classes it is counted under', async () => {
  const db = sqliteDb();
  await flood(db, MAX_SOURCES_PER_DAY - 1);
  await send('/desktop/mac?ref=shared', { env: { COUNTS: db }, headers: { 'user-agent': 'curl/8.7.1' } });
  await send('/desktop/linux-deb?ref=shared', { env: { COUNTS: db }, headers: { 'user-agent': 'Mozilla/5.0 Firefox/132.0' } });
  assert.deepEqual(rowsOf(db).find((row) => row.ref === 'shared'), { ref: 'shared', n: 2 });
  assert.equal(rowsOf(db).some((row) => row.ref === FOLDED_SOURCE), false);
});

test('after a flood /stats still shows the real sources by name, and the flood costs one row', async () => {
  const db = sqliteDb();
  for (const ref of ['newsletter', 'blog']) for (let i = 0; i < 3; i++) await send('/desktop/windows?ref=' + ref, { env: { COUNTS: db } });
  await flood(db, MAX_SOURCES_PER_DAY * 3);
  await send('/desktop/windows?ref=newsletter', { env: { COUNTS: db } });
  const read = await readCounts(db, new Date('2026-11-12T10:00:00Z'));
  const sources = tabulate(read.rows).map((line) => line.source);
  assert.ok(sources.includes('newsletter') && sources.includes('blog'));
  assert.equal(read.rows.filter((row) => row.source === FOLDED_SOURCE).length, 1);
  assert.equal(db.database.prepare('SELECT COUNT(*) AS c FROM counts').get().c, MAX_SOURCES_PER_DAY + 1);
});
