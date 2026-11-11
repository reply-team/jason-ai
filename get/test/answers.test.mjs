// What each path answers, and that nothing a request carries comes back in an answer.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { AUD, TEAM, accessKey, accessToken, certs, fakeDb, fakeFetch, send } from '../fakes.mjs';
import { DESKTOP, INSTALL_PAGE } from '../src/paths.mjs';

test('each desktop path is a 302 to its fixed address, never cached on the way', async () => {
  for (const [path, location] of Object.entries(DESKTOP)) {
    const { response } = await send(`${path}?ref=x`);
    assert.equal(response.status, 302);
    assert.equal(response.headers.get('location'), location);
    assert.equal(response.headers.get('cache-control'), 'no-store');
  }
});

test('a path outside the table is 404, whatever its method', async () => {
  for (const path of ['/', '/install/', '/install.sh', '/installer', '/Install', '/desktop/', '/desktop/linux', '/desktop/windows/', '/stats/', '/statsx', '/releases/latest/download/install.sh']) {
    for (const method of ['GET', 'POST']) {
      const { response } = await send(path, { method });
      assert.equal(response.status, 404, `${method} ${path}`);
      assert.equal(response.headers.get('content-type'), 'text/plain; charset=utf-8');
    }
  }
});

test('a method other than GET and HEAD on a path of the table is 405, saying which are allowed', async () => {
  for (const path of ['/install', '/desktop/mac', '/stats']) {
    for (const method of ['POST', 'PUT', 'DELETE', 'OPTIONS', 'PATCH']) {
      const { response } = await send(path, { method });
      assert.equal(response.status, 405, `${method} ${path}`);
      assert.equal(response.headers.get('allow'), 'GET, HEAD');
    }
  }
});

const MARK = 'echo-mark-7q';

/** Every header and the body of an answer, as one text. */
async function whole(response) {
  return [...response.headers].map(([name, value]) => `${name}: ${value}`).join('\n') + '\n\n' + (await response.text());
}

test('nothing a request carries reaches an answer but the fixed location', async () => {
  const key = await accessKey('k1');
  const token = await accessToken(key);
  const headers = {
    'user-agent': `curl/8.7.1 ${MARK}`,
    referer: `https://example.com/${MARK}`,
    cookie: `a=${MARK}`,
    'x-forwarded-for': '203.0.113.7',
    'cf-connecting-ip': '203.0.113.7',
    origin: `https://${MARK}.example.com`,
  };
  const query = `?ref=${MARK}&${MARK}=${MARK}&format=${MARK}`;
  const fetch = fakeFetch({ [INSTALL_PAGE]: () => new Response('# Install\n'), ...certs(key) });
  const env = { COUNTS: fakeDb({ rows: [] }), ACCESS_TEAM: TEAM, ACCESS_AUD: AUD };
  const requests = [
    ['GET', `/install${query}`],
    ['HEAD', `/install${query}`],
    ...Object.keys(DESKTOP).map((path) => ['GET', path + query]),
    ['GET', `/stats${query}`, { 'cf-access-jwt-assertion': token }],
    ['GET', `/stats${query}`],
    ['GET', `/${MARK}${query}`],
    ['POST', `/install${query}`],
  ];
  for (const [method, path, extra = {}] of requests) {
    const { response } = await send(path, { method, headers: { ...headers, ...extra }, env, fetch });
    const text = await whole(response);
    assert.ok(!text.includes(MARK), `${method} ${path} answered with something of the request:\n${text}`);
    assert.ok(!text.includes('203.0.113.7'), `${method} ${path} answered with the address`);
    assert.equal(response.headers.get('set-cookie'), null);
  }
});
