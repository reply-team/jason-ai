// The short paths of the product's download address: the install page, the desktop downloads and the counts page.
// Every other path of the address is answered by its redirect and never reaches this code (README.md, "Routes").
//
// What a counted request leaves behind is one counter moved by one: its UTC day, its path, the link's source and a
// coarse client class. No address, cookie, identifier or header of the request is kept, logged or sent back.

import { accessAllows } from './access.mjs';
import { clientOf, dayOf, sourceOf } from './classify.mjs';
import { readInstallPage } from './install.mjs';
import { DESKTOP, INSTALL_PAGE, INSTALL_PATH, STATS_PATH } from './paths.mjs';
import { readCounts, renderCsv, renderHtml, tabulate } from './stats.mjs';

/**
 * One statement per counted request. Cloudflare D1, https://developers.cloudflare.com/d1/platform/pricing/ :
 * "Rows written measure how many rows were written to D1 database. Write operations include `INSERT`, `UPDATE`, and
 * `DELETE`."
 */
const COUNT =
  'INSERT INTO counts (day, path, ref, client, n) VALUES (?1, ?2, ?3, ?4, 1) ON CONFLICT (day, path, ref, client) DO UPDATE SET n = n + 1';

const TEXT = 'text/plain; charset=utf-8';

function answer(status, body, headers = {}) {
  return new Response(body, {
    status,
    headers: { 'content-type': TEXT, 'x-content-type-options': 'nosniff', 'cache-control': 'no-store', ...headers },
  });
}

function redirect(location) {
  return new Response(null, { status: 302, headers: { location, 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' } });
}

/**
 * Leaves the count to run after the answer, so a store that is slow, full or down never holds or fails a download.
 * Cloudflare, https://developers.cloudflare.com/workers/runtime-apis/context/ :
 * "`ctx.waitUntil()` extends the lifetime of your Worker, allowing you to perform work without blocking returning a
 * response, and that may continue after a response is returned."
 * "An async call that is neither awaited nor passed to `ctx.waitUntil()` can be canceled when the invocation ends"
 */
function count(env, ctx, values) {
  const work = Promise.resolve()
    .then(() => env.COUNTS.prepare(COUNT).bind(...values).run())
    .catch(() => {});
  try {
    ctx.waitUntil(work);
  } catch {
    // The answer goes out whether or not the count can be left running.
  }
}

async function stats(request, env, url, deps) {
  if (!(await accessAllows(request, env, deps))) return answer(403, 'Forbidden.\n');
  const now = deps.now();
  let read;
  let lines;
  try {
    read = await readCounts(env.COUNTS, now);
    lines = tabulate(read.rows);
  } catch {
    return answer(503, 'The counts could not be read.\n');
  }
  if (url.searchParams.get('format') === 'csv') {
    return answer(200, renderCsv(lines, read), {
      'content-type': 'text/csv; charset=utf-8',
      'content-disposition': 'attachment; filename="downloads-by-source.csv"',
      'x-folded-sources': String(read.folded),
    });
  }
  return answer(200, renderHtml(lines, now, read), {
    'x-folded-sources': String(read.folded),
    'content-type': 'text/html; charset=utf-8',
    'content-security-policy': "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'",
    'referrer-policy': 'no-referrer',
  });
}

/** Answers one request. `deps` carries the outbound fetch and the clock, so a test can stand in for both. */
export async function handle(request, env, ctx, deps) {
  const url = new URL(request.url);
  const path = url.pathname;
  const known = path === INSTALL_PATH || path === STATS_PATH || Object.hasOwn(DESKTOP, path);
  if (!known) return answer(404, 'Not found.\n');
  if (request.method !== 'GET' && request.method !== 'HEAD') return answer(405, 'Method not allowed.\n', { allow: 'GET, HEAD' });

  let response;
  if (path === STATS_PATH) {
    response = await stats(request, env, url, deps);
  } else {
    if (path === INSTALL_PATH) {
      const text = await readInstallPage(deps.fetch);
      response = text === null ? redirect(INSTALL_PAGE) : answer(200, text);
    } else {
      response = redirect(DESKTOP[path]);
    }

    if (request.method === 'GET') {
      count(env, ctx, [dayOf(deps.now()), path, sourceOf(url.searchParams), clientOf(request.headers.get('user-agent'))]);
    }
  }

  return request.method === 'HEAD' ? new Response(null, { status: response.status, headers: response.headers }) : response;
}

export default {
  fetch(request, env, ctx) {
    return handle(request, env, ctx, { fetch: (input, init) => fetch(input, init), now: () => new Date() });
  },
};
