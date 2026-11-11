# The download address's short paths

A Cloudflare Worker in front of a few paths of the product's download address, `get.jasonharness.ai`: the install page
an agent is pointed at, one short link per desktop system, and a page of counts. Every other path of the address,
`/` and everything under `/releases/` included, is answered by the address's redirect to this repository's releases on
GitHub, and never reaches this code.

## What it answers

| Path | Answer |
| --- | --- |
| `https://get.jasonharness.ai/install` | 200, `text/plain; charset=utf-8` with `X-Content-Type-Options: nosniff`: the text of [`docs/INSTALL.md`](../docs/INSTALL.md) of `main`, read from one fixed address on `raw.githubusercontent.com` and kept at Cloudflare's edge for five minutes. Its relative links are made whole against that same address, so they lead to the files they name. When that address cannot be read, a 302 to it. |
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
The source is the link's first `ref`, lowercased, when it is 1 to 40 letters, digits and hyphens; `other` when it is
anything else, `none` when there is none. The client class is read from the User-Agent and the User-Agent dropped: `bot`
for a crawler or a link preview, `fetcher` for an agent's or a command line's fetcher by its own word (`curl`,
`powershell`, `claude-user` and the others in [`src/classify.mjs`](src/classify.mjs)) or a request with no User-Agent,
`browser` for anything else that says `Mozilla/`, `fetcher` for the rest. HEAD is answered and never counted, and
neither are `/stats` nor a refused request.

That is all that is stored. The Worker keeps no record of a request at all, only the counts: no address, cookie,
identifier, header or whole User-Agent, and Workers Logs are off. The product itself never adds a `ref` to a request,
and sends nothing for this.

The counts are one table in Cloudflare D1, [`schema.sql`](schema.sql), and each counted request is one
`INSERT … ON CONFLICT DO UPDATE` handed to `ctx.waitUntil` after the answer is made, so a store that is slow, full or
down never holds or fails a download or the page.

## The counts page

`/stats` shows the last 90 days, per UTC day and source: the install page's reads that are not bots as the headline,
with fetchers and browsers beside it; the desktop downloads per system, bots left out; and the bots of every path in a
column of their own. `/stats?format=csv` is the same as CSV.

It is behind a Cloudflare Access application on that path, with a policy for the company's work mail, and the Worker
checks Access's signed token itself as well: the `Cf-Access-Jwt-Assertion` header's signature against the team's
published keys (kept for an hour), its audience, issuer, expiry and start. Without a valid token, or without the two
secrets below, the answer is 403.

## Testing

```sh
cd get
node --test
```

Node's own runner, Node 22 or later, no dependencies: [`fakes.mjs`](fakes.mjs) stands in for the D1 binding, the
context, the outbound fetch, the clock and Access's keys. CI runs the same command as a step of the desktop job. The
tests also read [`wrangler.template.toml`](wrangler.template.toml) and hold its routes to the three below.

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

1. `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` in the environment (a token that may edit Workers, routes and D1
   on the zone).
2. Once: `npx wrangler@4.152.0 d1 create jason-download-counts`, then
   `npx wrangler@4.152.0 d1 execute jason-download-counts --remote --file schema.sql`.
3. Copy `wrangler.template.toml` to `wrangler.toml` (ignored by git) and add the database's `database_id` under
   `[[d1_databases]]`.
4. The Access application's audience tag and the team's name as secrets:
   `npx wrangler@4.152.0 secret put ACCESS_AUD`, `npx wrangler@4.152.0 secret put ACCESS_TEAM`.
5. `npx wrangler@4.152.0 deploy`.

Redirect rules answer before a Worker does. No redirect rule may stand on the Worker's paths: a rule put on one of them
for a while is removed in the same step that routes the Worker. The rule for `/releases/` and the root stays as it is.
