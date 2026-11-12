# The download address's short paths

A Cloudflare Worker in front of a few paths of the product's download address, `get.jasonharness.ai`: the install page
an agent is pointed at, one short link per desktop system, and a page of counts. Every other path of the address,
`/` and everything under `/releases/` included, is answered by the address's redirect to this repository's releases on
GitHub, and never reaches this code.

## What it answers

| Path | Answer |
| --- | --- |
| `https://get.jasonharness.ai/install` | 200, `text/plain; charset=utf-8` with `X-Content-Type-Options: nosniff`: the text of [`docs/INSTALL.md`](../docs/INSTALL.md) of `main`, read from one fixed address on `raw.githubusercontent.com` and kept at Cloudflare's edge for five minutes. Its relative links are made whole against that same address, so they lead to the files they name. When that address cannot be read, or answers anything but the guide (a body over 256 KB, or one that does not begin with the guide's first heading), a 302 to it. |
| `https://get.jasonharness.ai/desktop/windows` | 302 to the latest release's `jason-desktop-windows-x64-setup.exe` |
| `https://get.jasonharness.ai/desktop/mac` | 302 to the latest release's `jason-desktop-macos-arm64.dmg` (Apple silicon) |
| `https://get.jasonharness.ai/desktop/mac-intel` | 302 to the latest release's `jason-desktop-macos-x64.dmg` |
| `https://get.jasonharness.ai/desktop/linux-deb` | 302 to the latest release's `jason-desktop-linux-x64.deb` |
| `https://get.jasonharness.ai/desktop/linux-appimage` | 302 to the latest release's `jason-desktop-linux-x64.AppImage` |
| `/stats` | the counts, to a person Cloudflare Access let through (below) |

Each 302 goes to the whole address of the file under the address's own `/releases/latest/download/`, written as a
literal in [`src/paths.mjs`](src/paths.mjs); `DownloadPathsTests`, in the runtime's suite, holds those file names to
the list a release publishes, both ways, so a renamed file fails the build rather than a link someone was given. Only
GET and HEAD are answered (405 otherwise), any other path under the routes is 404, and nothing a request carries is
sent back: no header or body of an answer holds anything of the request but the fixed location. A link may carry its
source, `?ref=<source>`; the source never changes what a path answers.

## What it counts, and what it keeps

A GET of `/install` or of a desktop path moves one counter by one: the UTC day, the path, the source and a client class.
The source is the link's first `ref`, lowercased, when it is 1 to 40 letters, digits and hyphens; `(other)` when it is
anything else, `(none)` when there is none. Neither can be a source, so a link that says `ref=none` is counted as
`none`.

A `ref` names a channel or a campaign, never a recipient: `?ref=newsletter-november`, never a person's name, address
or contact id. The counts keep nothing of the person only as long as the links do; whoever makes a link keeps to this.

The client class is read from the User-Agent, and the User-Agent dropped, in this order: `fetcher` for an agent's own
fetcher by the name its maker gives it (`ChatGPT-User`, `Claude-User`, `Perplexity-User`, and an agent's command line),
even where its string also names a bot; `bot` for a crawler or a link preview; `fetcher` for a command line's or a
program's HTTP client by its own word (`curl`, `powershell` and the others in [`src/classify.mjs`](src/classify.mjs));
`browser` for anything else that says `Mozilla/`; and `fetcher` for the rest, a request with no User-Agent included.
HEAD is answered and never counted, and neither are `/stats` nor a refused request.

A day names at most 100 distinct sources, not counting `(none)` and `(other)`, which are never folded (`MAX_SOURCES_PER_DAY` in [`src/index.mjs`](src/index.mjs)). Once it has, a
request naming a source that day has not seen is counted under `(rest)`, the row /stats folds its rest into, and makes
no row of its own; a source already seen that day keeps counting under its own name, and the next UTC day starts again
from none. So a flood of made-up links costs the table a bounded number of rows. A real source stays in /stats by name
only if the day saw it before the cap: one that first appears after a flood that day is counted in `(rest)`, and has its
own row again from the next day. The decision is made in the same statement as the write, so concurrent requests cannot both pass the cap.

That is all that is stored. The Worker keeps no record of a request at all, only the counts: no address, cookie,
identifier, header or whole User-Agent, and Workers Logs are off. The product itself never adds a `ref` to a request,
and sends nothing for this.

The counts are one table in Cloudflare D1, [`schema.sql`](schema.sql), and each counted request is one
`INSERT … ON CONFLICT DO UPDATE` handed to `ctx.waitUntil` after the answer is made, so a store that is slow, full or
down never holds or fails a download or the page.

## The counts page

`/stats` shows the last 90 days, per UTC day and source: the install page's reads that are not bots as the headline,
with fetchers and browsers beside it; the desktop downloads per system, bots left out; and the bots of every path in a
column of their own. `/stats?format=csv` is the same as CSV, with a last row whose source is `(truncated)` when the
read stopped at its limit; both answers carry the number of folded sources in an `X-Folded-Sources` header. Every
number is an upper bound: link previews and mail scanners that present themselves as browsers or fetchers are counted
with them, and the page says so.

A source is whatever a link says, so the read is bounded: the 200 sources with the most requests in the window are
shown by name, every other is folded into one row, `(rest)`, and the page says how many it folded; the read stops at
20,000 rows, newest days first, and says so when it does.

It is behind a Cloudflare Access application on that path, with a policy for the company's work mail, and the Worker
checks Access's signed token itself as well: the `Cf-Access-Jwt-Assertion` header's signature against the team's
published keys (kept for an hour, and looked for again at most every five minutes when a token names a key they lack),
its audience, issuer, expiry and start. Without a valid token, without the team's keys, or without the two secrets
below, the answer is 403.

## Testing

```sh
cd get
node --test
```

Node's own runner, Node 22.13 or later (the first 22 to run `node:sqlite` without a flag), no dependencies:
[`fakes.mjs`](fakes.mjs) stands in for the D1 binding (over Node's own SQLite, with the table made from `schema.sql`,
so the write and the page's read run as written), the context, the outbound fetch, the clock and Access's keys. CI
runs the same command as a step of the desktop job. The tests also read
[`wrangler.template.toml`](wrangler.template.toml) and hold its routes to the three below.

## Routes

```text
get.jasonharness.ai/install*
get.jasonharness.ai/desktop/*
get.jasonharness.ai/stats*
```

Cloudflare matches a route against the whole URL, query string included, and its only operator is a trailing or
leading `*`, so each pattern ends in `*` to take `?ref=`. None of them can match `/` or a path under `/releases/`,
which every installation and update depends on; the tests match each pattern against those paths and against every
path above.

## Deployment

By hand, with a pinned wrangler, from the commit that was reviewed. Nothing in this folder names an account, a zone, a
database or an Access application by its identifier, and no workflow deploys it, so no Cloudflare token is among this
repository's secrets.

The account is on Workers Paid. Cloudflare (https://developers.cloudflare.com/workers/platform/limits/): "Accounts on
the Workers Free plan have a daily request limit of 100,000 requests, resetting at midnight UTC. When a Worker exceeds
this limit, Cloudflare returns **Error 1027**." That limit is shared by every Worker on the account, so on the free plan
anyone could spend it and leave these paths answering an error page for the rest of the day; Workers Paid has no daily
request limit. The routes are set to fail closed ("Returns a Cloudflare `1027` error page.") rather than open ("Bypasses
the Worker. Requests behave as if no Worker is configured."), so a request is never handed to whatever stands behind
the address in the Worker's place. The install scripts' own addresses, `/releases/latest/download/install.sh` and
`install.ps1`, and every other path under `/releases/`, never reach the Worker, whatever happens to it.

1. `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` in the environment (a token that may edit Workers, routes and D1
   on the zone).
2. Once: `npx wrangler@4.152.0 d1 create jason-download-counts`, then
   `npx wrangler@4.152.0 d1 execute jason-download-counts --remote --file schema.sql`.
3. Once: in Cloudflare Access, a self-hosted application on the counts path, `get.jasonharness.ai/stats`, with a policy
   for the company's work mail. Its Application Audience (AUD) tag is the secret `ACCESS_AUD` below.
4. Copy `wrangler.template.toml` to `wrangler.toml` (ignored by git) and add the database's `database_id` under
   `[[d1_databases]]`. The copy differs from the template by that one line and nothing else.
5. The application's audience tag and the team's name as secrets: `npx wrangler@4.152.0 secret put ACCESS_AUD`,
   `npx wrangler@4.152.0 secret put ACCESS_TEAM`. The team's name is used lowercased, as Access writes it.
6. `npx wrangler@4.152.0 deploy`, then set each of the three routes' failure mode to fail closed.
   Then make one GET of a counted path with a `ref` nobody uses, and look for that row in `/stats`. A statement the
   database refuses fails silently, because the count is left to run after the answer, so the row is the only sign the
   counting works; if it is not there, `npx wrangler@4.152.0 rollback`.
7. Check, before a release that names the address is published: `curl -sI https://get.jasonharness.ai/install`
   answers 200 with `content-type: text/plain; charset=utf-8`, and
   `curl -sI https://get.jasonharness.ai/releases/latest/download/manifest.json` still answers the redirect to this
   repository's releases on GitHub. The install prompt names the first address, so it must answer before the prompt is
   published.

A change under `src/` is live only once it is deployed. The install page is served only while it begins with the heading
in `src/paths.mjs`, so a release that retitles `docs/INSTALL.md` needs a deployment of that change, or `/install`
answers the redirect to the raw file from then on.

Redirect rules answer before a Worker does. No redirect rule may stand on the Worker's paths: a rule put on one of them
for a while is removed in the same step that routes the Worker. The rule for `/releases/` and the root stays as it is.
