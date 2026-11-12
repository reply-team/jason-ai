// The routes that send a request to this handler, read from the deployment's template, and the table of paths.
//
// Cloudflare, https://developers.cloudflare.com/workers/configuration/routing/routes/ :
// "The only supported operator is the wildcard ( `*`), which matches zero or more of any character."
// "Route pattern matching considers the entire request URL, including the query parameter string. Since route patterns
// may not contain query parameters, the only way to have a route pattern match URLs with query parameters is to
// terminate it with a wildcard, `*`."
// So each pattern ends in `*`, and none may match `/` or a path under `/releases/`, which the address's redirect answers
// for every installation and update there is.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DESKTOP, INSTALL_PATH, STATS_PATH } from '../src/paths.mjs';

const template = readFileSync(new URL('../wrangler.template.toml', import.meta.url), 'utf8');
const patterns = [...template.matchAll(/pattern\s*=\s*"([^"]+)"/g)].map((match) => match[1]);

/** Whether a pattern matches a request URL written without its scheme, by the documented wildcard rule. */
function matches(pattern, url) {
  const expression = pattern.split('*').map((part) => part.replace(/[.?+^$()[\]{}|\\/]/g, '\\$&')).join('.*');
  return new RegExp(`^${expression}$`).test(url);
}

const host = 'get.jasonharness.ai';

test('the routes are exactly the three the handler answers under', () => {
  assert.deepEqual(patterns, [`${host}/install*`, `${host}/desktop/*`, `${host}/stats*`]);
  for (const pattern of patterns) {
    assert.ok(pattern.startsWith(`${host}/`), `${pattern} names the host itself, with no wildcard before it`);
    assert.equal(pattern.indexOf('*'), pattern.length - 1, `${pattern} has its one wildcard at its end`);
  }
});

test('no route matches the root or a path the redirect answers', () => {
  const kept = [
    '/',
    '/?ref=x',
    '/releases/',
    '/releases/latest/download/install.sh',
    '/releases/latest/download/install.ps1',
    '/releases/latest/download/manifest.json',
    '/releases/latest/download/jason-desktop-windows-x64-setup.exe',
    '/releases/download/v0.35.0/manifest.json',
    '/releases/download/v0.35.0/install.sh?ref=x',
    '/releases/tag/v0.35.0',
  ];
  for (const path of kept) {
    for (const pattern of patterns) {
      assert.equal(matches(pattern, host + path), false, `${pattern} would take ${path}`);
    }
  }
});

test('every path the handler answers is reached by a route, with and without a source', () => {
  for (const path of [INSTALL_PATH, STATS_PATH, ...Object.keys(DESKTOP)]) {
    for (const url of [host + path, `${host}${path}?ref=x`]) {
      assert.ok(patterns.some((pattern) => matches(pattern, url)), `no route reaches ${url}`);
    }
  }
});

test('the template names no account, zone or database by its identifier', () => {
  assert.doesNotMatch(template, /^\s*(account_id|zone_id|database_id)\s*=/m);
  assert.doesNotMatch(template, /\b[0-9a-f]{32}\b/i);
});

test("each desktop path redirects to its own system's file of the latest release, by a whole literal address", () => {
  // The whole table, path by path: a swap of two rows (the Intel image sent to Apple silicon, say) passes a check of
  // the paths alone and a check of the file names alone.
  assert.deepEqual(
    { ...DESKTOP },
    {
      '/desktop/windows': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-windows-x64-setup.exe',
      '/desktop/mac': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-macos-arm64.dmg',
      '/desktop/mac-intel': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-macos-x64.dmg',
      '/desktop/linux-deb': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-linux-x64.deb',
      '/desktop/linux-appimage': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-linux-x64.AppImage',
    },
  );
});
