// The counts page: per UTC day and source, the install page's reads that are not bots as the headline, the classes
// beside it, and desktop downloads per system. Every value printed is escaped, though each comes from a fixed set or a
// source restricted to letters, digits and hyphens.

import { dayOf } from './classify.mjs';

const DAYS = 90;

const COLUMNS = [
  ['installs', 'Install page, not bots'],
  ['installs_fetcher', 'of which fetchers'],
  ['installs_browser', 'of which browsers'],
  ['windows', 'Windows'],
  ['mac', 'Mac, Apple silicon'],
  ['mac_intel', 'Mac, Intel'],
  ['linux_deb', 'Linux, deb'],
  ['linux_appimage', 'Linux, AppImage'],
  ['bots', 'Bots, every path'],
];

const DESKTOP_COLUMN = {
  '/desktop/windows': 'windows',
  '/desktop/mac': 'mac',
  '/desktop/mac-intel': 'mac_intel',
  '/desktop/linux-deb': 'linux_deb',
  '/desktop/linux-appimage': 'linux_appimage',
};

/** The first day the page shows: today and the 89 before it. */
export function firstDay(now) {
  return dayOf(new Date(now.getTime() - (DAYS - 1) * 24 * 60 * 60 * 1000));
}

/** How many sources the page keeps by name; every other is folded into one row. */
export const KEPT_SOURCES = 200;

/** The row a folded source is counted in: in parentheses, which a source can never hold. */
export const FOLDED_SOURCE = '(rest)';

/** At most this many rows are read, newest days first, whatever was written. */
export const ROW_LIMIT = 20000;

/**
 * The window's counts, the sources with the most requests by name and every other source folded into one row, so a flood
 * of made-up sources costs the page one row; then the read's own limit as a backstop.
 */
const READ = `WITH totals AS (SELECT ref, SUM(n) AS total FROM counts WHERE day >= ?1 GROUP BY ref),
kept AS (SELECT ref FROM totals ORDER BY total DESC, ref LIMIT ?2)
SELECT day, path, CASE WHEN ref IN (SELECT ref FROM kept) THEN ref ELSE '${FOLDED_SOURCE}' END AS source, client, SUM(n) AS n
FROM counts WHERE day >= ?1
GROUP BY day, path, source, client
ORDER BY day DESC, path, source, client
LIMIT ?3`;

const SOURCES = 'SELECT COUNT(DISTINCT ref) AS sources FROM counts WHERE day >= ?1';

/** The rows of the window, how many sources were folded, and whether the read stopped at its limit. */
export async function readCounts(db, now, { kept = KEPT_SOURCES, limit = ROW_LIMIT } = {}) {
  const first = firstDay(now);
  // One row more than the limit is read, so a read of exactly the limit is not taken for one that stopped.
  const { results = [] } = await db.prepare(READ).bind(first, kept, limit + 1).all();
  const rows = results ?? [];
  const { results: [sources = { sources: 0 }] = [] } = await db.prepare(SOURCES).bind(first).all();
  return { rows: rows.slice(0, limit), folded: Math.max(0, Number(sources.sources) - kept), truncated: rows.length > limit, limit };
}

/** One line per day and source, newest day first, then the most installs. */
export function tabulate(rows) {
  const lines = new Map();
  for (const { day, path, source, client, n } of rows) {
    const key = JSON.stringify([day, source]);
    if (!lines.has(key)) lines.set(key, { day: String(day), source: String(source), ...Object.fromEntries(COLUMNS.map(([name]) => [name, 0])) });
    const line = lines.get(key);
    const count = Number(n) || 0;
    if (client === 'bot') {
      line.bots += count;
    } else if (path === '/install') {
      line.installs += count;
      if (client === 'fetcher') line.installs_fetcher += count;
      if (client === 'browser') line.installs_browser += count;
    } else if (DESKTOP_COLUMN[path]) {
      line[DESKTOP_COLUMN[path]] += count;
    }
  }
  return [...lines.values()].sort((a, b) => (a.day === b.day ? b.installs - a.installs || (a.source < b.source ? -1 : 1) : a.day < b.day ? 1 : -1));
}

const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

export function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ESCAPES[character]);
}

const TAB = String.fromCharCode(9);
const CR = String.fromCharCode(13);

/**
 * A CSV field: bare when it is a plain word or number, quoted otherwise; one a spreadsheet would read as a formula is
 * kept as text by a leading apostrophe.
 */
function csvField(value) {
  const text = String(value);
  if (/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(text)) return text;
  const inert = /^[=+@-]/.test(text) || text.startsWith(TAB) || text.startsWith(CR) ? `'${text}` : text;
  return `"${inert.replace(/"/g, '""')}"`;
}

/** The source of the CSV's last row when the read stopped at its limit: in parentheses, which a source can never hold. */
export const TRUNCATED_SOURCE = '(truncated)';

/** The CSV, with a last row saying the read stopped at its limit when it did; the folded sources go in a header. */
export function renderCsv(lines, { truncated = false } = {}) {
  const header = ['day', 'source', ...COLUMNS.map(([name]) => name)];
  const body = lines.map((line) => header.map((name) => csvField(line[name])).join(','));
  const stop = truncated ? [['', csvField(TRUNCATED_SOURCE), ...COLUMNS.map(() => '')].join(',')] : [];
  return [header.join(','), ...body, ...stop].join('\n') + '\n';
}

/** What the page says of the read: the folded sources, and the limit when the read stopped at it. */
function aboutTheRead({ folded = 0, truncated = false, limit = ROW_LIMIT } = {}) {
  const fold =
    folded === 0
      ? 'No source was folded.'
      : folded === 1
        ? `1 source with fewer requests is folded into the row ${FOLDED_SOURCE}.`
        : `${folded} sources with fewer requests are folded into the row ${FOLDED_SOURCE}.`;
  const stop = truncated ? ` The read stopped at its limit of ${limit} rows, so the oldest days shown are incomplete.` : '';
  return `The ${KEPT_SOURCES} sources with the most requests in the window are shown by name. ${fold}${stop}`;
}

export function renderHtml(lines, now, read = {}) {
  const head = ['Day (UTC)', 'Source', ...COLUMNS.map(([, title]) => title)].map((title) => `<th>${escapeHtml(title)}</th>`).join('');
  const body = lines
    .map((line) => `<tr><td>${escapeHtml(line.day)}</td><td>${escapeHtml(line.source)}</td>${COLUMNS.map(([name]) => `<td>${escapeHtml(line[name])}</td>`).join('')}</tr>`)
    .join('\n');
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Downloads by source</title>
<style>
body { font: 14px/1.4 system-ui, sans-serif; margin: 16px; color: #1a1a1a; background: #fff; }
table { border-collapse: collapse; }
th, td { padding: 4px 8px; border-bottom: 1px solid #ddd; text-align: right; white-space: nowrap; }
th:nth-child(-n+2), td:nth-child(-n+2) { text-align: left; }
td:nth-child(3) { font-weight: 600; }
</style>
</head>
<body>
<h1>Downloads by source</h1>
<p>The last ${DAYS} days to ${escapeHtml(dayOf(now))}, per UTC day and the source a link named. The install page's reads that are not bots are the headline; fetchers and browsers are beside it. Bots are counted apart and in no other column.</p>
<p>Every number is an upper bound: link previews and mail scanners that present themselves as browsers or fetchers are counted with them.</p>
<p>${escapeHtml(aboutTheRead(read))}</p>
<table>
<thead><tr>${head}</tr></thead>
<tbody>
${body}
</tbody>
</table>
</body>
</html>
`;
}
