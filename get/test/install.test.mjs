// The install page: the text of docs/INSTALL.md as the one fixed address serves it, as plain text, with its relative
// links made whole against that address, the same for every request.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { GUIDE, fakeDb, fakeFetch, send } from '../fakes.mjs';
import { wholeLinks } from '../src/install.mjs';
import { INSTALL_PAGE } from '../src/paths.mjs';

const repository = new URL('../../', import.meta.url);
const installText = readFileSync(new URL('docs/INSTALL.md', repository), 'utf8');
const rawMain = 'https://raw.githubusercontent.com/reply-team/jason-ai/main/';
const page = (text) => () => new Response(text, { status: 200, headers: { 'content-type': 'text/plain; charset=utf-8' } });

test('the page is read from the one fixed address, which is the install page of main', async () => {
  assert.equal(INSTALL_PAGE, `${rawMain}docs/INSTALL.md`);
  const fetch = fakeFetch({ [INSTALL_PAGE]: page(GUIDE) });
  await send('/install?ref=x&url=https%3A%2F%2Fexample.com%2F', { fetch, headers: { referer: 'https://example.com/' } });
  assert.deepEqual(fetch.asked.map((ask) => ask.address), [INSTALL_PAGE]);
  assert.equal(fetch.asked[0].init.cf.cacheTtl, 300);
});

test('the page is plain text that is never sniffed, never cached on the way and never a download', async () => {
  const { response } = await send('/install', { fetch: fakeFetch({ [INSTALL_PAGE]: page(GUIDE) }) });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('content-type'), 'text/plain; charset=utf-8');
  assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.equal(response.headers.get('content-disposition'), null);
  assert.equal(await response.text(), GUIDE);
});

test('the page served is the same text whatever source the link names', async () => {
  const bodies = [];
  for (const query of ['', '?ref=linkedin', '?ref=Other-Thing', '?ref=%3Cb%3E', '?ref=a&ref=b&x=1']) {
    const { response } = await send(`/install${query}`, { fetch: fakeFetch({ [INSTALL_PAGE]: page(installText) }) });
    bodies.push(await response.text());
  }
  assert.ok(bodies.every((body) => body === bodies[0]));
});

test('every relative link of the real install page is made whole, to a file this repository holds', () => {
  const served = wholeLinks(installText);
  const relative = [...installText.matchAll(/\]\(([^)\s]+)\)/g)].map((match) => match[1]).filter((target) => !/^[a-z][a-z0-9+.-]*:|^#/i.test(target));
  assert.ok(relative.length >= 10, `the page has ${relative.length} relative links`);
  const targets = [...served.matchAll(/\]\(([^)\s]+)\)/g)].map((match) => match[1]);
  for (const target of targets) {
    assert.match(target, /^(https?:|mailto:|#)/, `${target} is still relative`);
    if (!target.startsWith(rawMain)) continue;
    const file = target.slice(rawMain.length).split('#')[0];
    assert.ok(existsSync(fileURLToPath(new URL(file, repository))), `${target} names no file of the tree`);
  }
  assert.equal(served.replace(/\]\([^)\s]+\)/g, ']()'), installText.replace(/\]\([^)\s]+\)/g, ']()'), 'only link targets change');
});

test('absolute links and links within the page are left as they are', () => {
  assert.equal(
    wholeLinks('[a](https://example.com/x) [b](#section) [c](mailto:x@example.com) [d](golden-path.md#step-3) [e](../LICENSE)'),
    `[a](https://example.com/x) [b](#section) [c](mailto:x@example.com) [d](${rawMain}docs/golden-path.md#step-3) [e](${rawMain}LICENSE)`,
  );
});

test('when the page cannot be read, the request goes to the fixed address itself, and is still counted', async () => {
  for (const answer of [new Error('unreachable'), () => new Response('gone', { status: 503 }), () => new Response('nothing', { status: 404 })]) {
    const db = fakeDb();
    const { response } = await send('/install?ref=news', { env: { COUNTS: db }, fetch: fakeFetch({ [INSTALL_PAGE]: answer }) });
    assert.equal(response.status, 302);
    assert.equal(response.headers.get('location'), INSTALL_PAGE);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.deepEqual(db.runs.map((run) => run.args.slice(1, 3)), [['/install', 'news']]);
  }
});

test('only the guide is served: a body over 256 KB, or one that does not begin with its first heading, is the redirect', async () => {
  const heading = GUIDE.split('\n')[0];
  const bodies = {
    'another page': 'Not the guide.\n',
    'a heading of another page': '# Another page\n',
    'the heading later on': `Preamble.\n${GUIDE}`,
    'the heading as a prefix of another': `${heading}s and more\n`,
    'too long': GUIDE + 'x'.repeat(256 * 1024),
    empty: '',
  };
  for (const [why, body] of Object.entries(bodies)) {
    const db = fakeDb();
    const { response } = await send('/install', { env: { COUNTS: db }, fetch: fakeFetch({ [INSTALL_PAGE]: page(body) }) });
    assert.equal(response.status, 302, why);
    assert.equal(response.headers.get('location'), INSTALL_PAGE, why);
    assert.equal(db.runs.length, 1, why);
  }
  const atTheBound = GUIDE + 'x'.repeat(256 * 1024 - new TextEncoder().encode(GUIDE).length);
  const { response } = await send('/install', { fetch: fakeFetch({ [INSTALL_PAGE]: page(atTheBound) }) });
  assert.equal(response.status, 200);
});

test("the guide's first heading is the constant the handler checks for", async () => {
  const { INSTALL_HEADING } = await import('../src/paths.mjs');
  assert.equal(INSTALL_HEADING, installText.split('\n')[0]);
});

test('the install page writes its links only in the one form the rewriter reads', () => {
  const lines = installText.split('\n');
  const findings = [];
  lines.forEach((line, index) => {
    const at = `docs/INSTALL.md:${index + 1}`;
    if (/\]\(\s*</.test(line)) findings.push(`${at} an angle-bracket destination`);
    if (/\]\([^)\s]+\s+["'(]/.test(line)) findings.push(`${at} a link with a title`);
    if (/^\s{0,3}\[[^\]]+\]:\s/.test(line)) findings.push(`${at} a reference definition`);
    if (/\]\(\//.test(line)) findings.push(`${at} a root-relative or scheme-relative target`);
    if (/\]\[[^\]]*\]/.test(line)) findings.push(`${at} a reference link`);
  });
  assert.deepEqual(findings, []);
});
