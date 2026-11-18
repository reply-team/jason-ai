// The install page of the latest published release: its tag learnt from the fixed address of the latest release, from
// its redirect alone, and held to a release tag's shape; the guide read at that tag, and its links made whole there.
// Where the tag cannot be learnt, or is not a release's, the guide of main as before.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { GUIDE, fakeFetch, send } from '../fakes.mjs';
import { wholeLinks } from '../src/install.mjs';
import { INSTALL_PAGE, LATEST_RELEASE, installPageAt } from '../src/paths.mjs';

const raw = 'https://raw.githubusercontent.com/reply-team/jason-ai/';
const page = (text) => () => new Response(text, { status: 200, headers: { 'content-type': 'text/plain; charset=utf-8' } });
const moved = (location, status = 302) => () => new Response(null, { status, headers: { location } });
// A release's page sits beside the latest release's address, which is what GitHub's redirect names.
const tagPage = (tag) => LATEST_RELEASE.replace(/latest$/, `tag/${tag}`);
const released = `${GUIDE}See [the golden path](golden-path.md#step-3) and [the licence](../LICENSE).\n`;
const onMain = `${GUIDE}On main.\n`;

test('the latest release is learnt from one fixed address, and its guide is read at its tag', async () => {
  assert.equal(LATEST_RELEASE, 'https://github.com/reply-team/jason-ai/releases/latest');
  assert.equal(installPageAt('v0.48.5'), `${raw}v0.48.5/docs/INSTALL.md`);
  for (const status of [301, 302, 303, 307, 308]) {
    const fetch = fakeFetch({ [LATEST_RELEASE]: moved(tagPage('v0.48.5'), status), [installPageAt('v0.48.5')]: page(released), [INSTALL_PAGE]: page(onMain) });
    const { response } = await send('/install', { fetch });
    assert.equal(response.status, 200, `${status}`);
    assert.equal(await response.text(), wholeLinks(released, installPageAt('v0.48.5')), `${status}`);
    assert.deepEqual(fetch.asked.map((ask) => ask.address), [LATEST_RELEASE, installPageAt('v0.48.5')], `${status}`);
    assert.equal(fetch.asked[0].init.redirect, 'manual', 'the redirect is read, never followed');
    assert.equal(fetch.asked[1].init.cf.cacheTtl, 300, "the tag's page is kept at the edge as main's is");
  }
});

test("the tag's guide has its relative links made whole against the tag's own address", async () => {
  const fetch = fakeFetch({ [LATEST_RELEASE]: moved(tagPage('v12.0.3')), [installPageAt('v12.0.3')]: page(released) });
  const { response } = await send('/install', { fetch });
  const text = await response.text();
  assert.ok(text.includes(`](${raw}v12.0.3/docs/golden-path.md#step-3)`), text);
  assert.ok(text.includes(`](${raw}v12.0.3/LICENSE)`), text);
  assert.ok(!text.includes('/main/'), text);
});

test("main's guide is served where the latest release's tag cannot be learnt, or is not a release tag", async () => {
  const answers = {
    'the address unreachable': new Error('unreachable'),
    'an answer that is not a redirect': page('<html></html>'),
    'a redirect with no location': () => new Response(null, { status: 302 }),
    'a redirect to the releases list': moved('https://github.com/reply-team/jason-ai/releases'),
    'a pre-release': moved(tagPage('v0.49.0-rc.1')),
    'two numbers': moved(tagPage('v0.49')),
    'four numbers': moved(tagPage('v0.49.0.1')),
    'no v': moved(tagPage('0.49.0')),
    'a capital V': moved(tagPage('V0.49.0')),
    'a leading zero': moved(tagPage('v0.049.0')),
    'a query after the tag': moved(`${tagPage('v0.49.0')}?x=1`),
    'a path after the tag': moved(`${tagPage('v0.49.0')}/../../main`),
    'an encoded tag': moved(tagPage('v0.49.0%2F..')),
    'another repository': moved('https://github.com/someone/jason-ai/releases/tag/v0.49.0'),
    'another host': moved('https://example.com/reply-team/jason-ai/releases/tag/v0.49.0'),
    'plain http': moved(tagPage('v0.49.0').replace(/^https:/, 'http:')),
    'a relative location': moved('/reply-team/jason-ai/releases/tag/v0.49.0'),
    'a number too long to be a version': moved(tagPage(`v${'9'.repeat(40)}.0.0`)),
  };
  for (const [why, answer] of Object.entries(answers)) {
    const fetch = fakeFetch({ [LATEST_RELEASE]: answer, [INSTALL_PAGE]: page(onMain) });
    const { response } = await send('/install', { fetch });
    assert.equal(response.status, 200, why);
    assert.equal(await response.text(), wholeLinks(onMain), why);
    assert.deepEqual(fetch.asked.map((ask) => ask.address), [LATEST_RELEASE, INSTALL_PAGE], `${why}: only the two fixed addresses are read`);
  }
});

test("the guide's heading and size are held on both reads: a tag's page that is not the guide gives main's, and neither gives the redirect", async () => {
  const notTheGuide = { 'another page': 'Not the guide.\n', 'too long': GUIDE + 'x'.repeat(256 * 1024), 'not found': () => new Response('404', { status: 404 }) };
  for (const [why, body] of Object.entries(notTheGuide)) {
    const answer = typeof body === 'function' ? body : page(body);
    const fetch = fakeFetch({ [LATEST_RELEASE]: moved(tagPage('v0.48.5')), [installPageAt('v0.48.5')]: answer, [INSTALL_PAGE]: page(onMain) });
    const { response } = await send('/install', { fetch });
    assert.equal(response.status, 200, why);
    assert.equal(await response.text(), wholeLinks(onMain), why);

    const neither = fakeFetch({ [LATEST_RELEASE]: moved(tagPage('v0.48.5')), [installPageAt('v0.48.5')]: answer, [INSTALL_PAGE]: answer });
    const { response: redirected } = await send('/install', { fetch: neither });
    assert.equal(redirected.status, 302, why);
    assert.equal(redirected.headers.get('location'), INSTALL_PAGE, why);
  }
});

test('a tag that is not a release tag never names an address', () => {
  for (const tag of ['v0.49.0-rc.1', 'main', '../main', 'v1.2', '', 'v1.2.3/x']) {
    assert.throws(() => installPageAt(tag), /release tag/, tag);
  }
});
