// The short paths of the product's download address: the install page, the desktop downloads and the counts page.
// Every other path of the address is answered by its redirect and never reaches this code (README.md, "Routes").
//
// What a counted request leaves behind is one counter moved by one: its UTC day, its path, the link's source and a
// coarse client class. No address, cookie, identifier or header of the request is kept, logged or sent back.

import { accessAllows } from './access.mjs';
import { NO_SOURCE, NOT_A_SOURCE, clientOf, dayOf, sourceOf } from './classify.mjs';
import { readInstallPage } from './install.mjs';
import { DESKTOP, INSTALL_PAGE, INSTALL_PATH, STATS_PATH } from './paths.mjs';
import { FOLDED_SOURCE, readCounts, renderCsv, renderHtml, tabulate } from './stats.mjs';

/**
 * How many distinct named sources one UTC day may name (the reserved (none) and (other) rows are not among them). A
 * request whose source the day has not seen, once the day has this many, is counted under the row /stats folds its rest
 * into, and makes no row of its own. Without a cap, a flood of made-up links grows the counts table without bound and
 * pushes real sources into /stats' fold. The cap is half the 200 sources /stats shows by name: a day's real channels
 * and campaigns are a few dozen at most, and 100 a day bounds a day's rows to 100 sources times the six paths and three
 * client classes, plus the fold's row.
 */
export const MAX_SOURCES_PER_DAY = 100;

/**
 * One statement per counted request, which decides the cap in the same write so two requests cannot both pass it. The
 * source is kept when it is one of the two reserved rows, which are no source and count toward no cap, when the day
 * already has a row for it, or when the day has fewer than ?5 named sources; otherwise it is the fold's row. Cloudflare
 * D1, https://developers.cloudflare.com/d1/platform/pricing/ : "Rows written measure how many rows were written to D1
 * database. Write operations include `INSERT`, `UPDATE`, and `DELETE`."
 */
const COUNT = `INSERT INTO counts (day, path, ref, client, n)
SELECT ?1, ?2,
  CASE WHEN ?3 IN ('${NO_SOURCE}', '${NOT_A_SOURCE}')
         OR EXISTS (SELECT 1 FROM counts WHERE day = ?1 AND ref = ?3)
         OR (SELECT COUNT(DISTINCT ref) FROM counts WHERE day = ?1 AND ref NOT IN ('${NO_SOURCE}', '${NOT_A_SOURCE}', '${FOLDED_SOURCE}')) < ?5
       THEN ?3 ELSE '${FOLDED_SOURCE}' END,
  ?4, 1
WHERE true
ON CONFLICT (day, path, ref, client) DO UPDATE SET n = n + 1`;

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
      count(env, ctx, [dayOf(deps.now()), path, sourceOf(url.searchParams), clientOf(request.headers.get('user-agent')), MAX_SOURCES_PER_DAY]);
    }
  }

  return request.method === 'HEAD' ? new Response(null, { status: response.status, headers: response.headers }) : response;
}

export default {
  fetch(request, env, ctx) {
    return handle(request, env, ctx, { fetch: (input, init) => fetch(input, init), now: () => new Date() });
  },
};
