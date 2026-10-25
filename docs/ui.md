# The browser UI

Jason serves a small web UI from the runtime itself: what needs you (approvals and questions), what work is running,
planned and finished, the people it works with and what happened to each, and whether everything is running. It is one more client of the Runtime API, with the same rights as the command line, and nothing in the runtime
exists for it alone: everything the page does is a verb you could type.

## Opening it

```
jason ui open
```

The runtime answers with a link into the page and the command opens it in your default browser (`--no-open` prints it
instead, for an agent to hand to you). The link carries a ticket that pairs one browser, once, within 60 seconds;
after that the page is paired and a bookmark of `http://127.0.0.1:52766/ui/` keeps working, across runtime restarts.
`jason ui open --approval <id>` and `--question <id>` open on one item.

If nothing is installed, `jason ui open` says so with the code `ui_not_installed`, names `jason ui install` as the fix and the two directories below.

## Where the page comes from

The runtime serves `<data>/ui/custom/` when it holds a build (an `index.html` and a `manifest.json`), else
`<data>/ui/default/` when that holds a build. It serves those files and nothing else, under
`Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; base-uri 'none'`.

The default build is the release's: `jason ui install` downloads the release's `jason-ui.zip`, checks it against the
release's `checksums.txt`, and swaps it in as `ui/default/`. The install guide runs it, `jason update apply` runs it at
its end, and `jason status` names it where the default build is missing or another release's. A build from source
has no published UI to fetch.

To try a build that is not the installed one — a CI run's `jason-ui.zip`, or your own — unzip it into `~/.jason/ui/custom/`
and run `jason ui open`; `jason status` and `jason runtime status --human` say which build is served, with its version
and digest. Delete `custom/` to go back to the default.

## Paired browsers

```
jason ui tokens
jason ui revoke <token-id>
jason ui revoke --all
```

Each pairing is a token the runtime keeps as a hash, with who paired and when, and when it was last used. A token never
expires on its own; revoke it when a browser should no longer have it. A browser that `jason ui open` opens again ends
the token it held, so the list stays one row per browser. The System screen shows the same list, the browsers used most
recently first; one not used for a month is dimmed, since a private window that closed or a browser whose data was
cleared never comes back for its token, and one button revokes every browser but the one you are using.

## The port

The runtime listens on `127.0.0.1:52766` by default, so the bookmark is stable. When that port is taken it takes the
next free one above it and `jason runtime status --human` says so; `Runtime:Port` in `settings.json` picks another, and
`0` lets the OS choose, as before.

## Campaigns

Every campaign, its people counted two ways: **Summary** is where they stand now (`jason campaign summary`), and
**Activity** what happened to them today, in the last 7 days or in the last 30 days, the days of this browser's time
zone (`jason campaign activity`). A person may be in several columns, and each column's name says on hover what it
counts. A search finds a campaign by its name, and the archived ones appear on request (`jason campaign list --query`,
`--include-archived`); the view, the period and both filters are part of the page's address. Every number opens its
campaign with exactly the people it counts (`jason campaign list-contacts --summary`, or `--activity` with the period),
each with why: a person waiting for you opens the approval or the question in place, as on Attention, and a failure
shows the reason the runtime recorded, with nothing on the page that retries it. Opening a campaign shows its status,
its people and the provider campaign it is linked to. **Pause** stops Jason releasing its work while what is running
finishes; the provider's own sequence keeps sending until a role pauses it there. **Start** releases the work again
(`jason campaign pause`, `jason campaign start`, with your reason). **Add people** finds people and adds them (`jason
campaign add-contacts`). Its **Timeline** puts what is planned, what waits for you and what is running above the now
line, and the campaign's journal below it (`jason journal list --campaign`), narrowed by chips to replies, work,
decisions or changes; its **People** are listed by the same counts with a search, Removed among them
(`--summary removed`); in Activity the tab is **People Activity**, and everything it lists is the period's, All being
everyone something happened to (`--activity people`); its **Details** are its record, its context as the roles read
it, and what each provider calls it.

## Performance

The whole operation over time, from the same counts as Campaigns' Activity: what happened to people today, in the last
7, 30 or 90 days, across every campaign not archived or the ones chosen, each person counted once however many campaigns
count them (`jason campaign activity` and its `total`). Two charts show contacted and replied people by the hour today,
by the day over 7 or 30 days and by the week over 90, in this browser's time zone (`jason campaign activity-by-period`):
a person counted on two days stands in both bars and once in the number above them. A count's tile lists its people
under the tiles, the latest to whom it happened first, grouped by that day, each with the campaigns that count them and
what happened (`jason campaign list-contacts --activity` with the campaigns and the period); a bar lists that hour's,
day's or week's. Opening a person shows them beside the list as People does, with the same tabs. The period, the
campaigns, the count and the bar are part of the page's address.

## Work items

Three tabs over the same filters: **Now** is what is running and what waits for your approval, **Planned** is what
will run, soonest first and grouped by day, and **Done** is what finished, newest first, with a chip that narrows it to
failures. Each tab's number is `jason workitem count` for that tab's statuses and filters. The filters are a campaign
and a person, each a search that opens on the first few and asks the runtime as you type (`jason campaign list
--query`, `jason contact list --query`: any part of a name, company, title, email, phone or profile link), a kind, a
role, and one day on Planned and Done: the day work is due, or the day it finished, stepped a day at a time. They are part of the page's address,
so a bookmark or a link from another screen keeps them. Opening an item shows what the runtime recorded about it: its
facts, its attempts, its input and its history; a failure is shown as the error the runtime recorded, with its class
and the attempts it used. Open work can take a new priority, start and due time, or be cancelled with a reason,
through `jason workitem update` and `jason workitem cancel`; work waiting for your approval can be approved or rejected
right there, as on Attention.

## People

Everyone Jason knows, the person something last happened to first (`jason contact list --order activity`), grouped
by day, with a search, a campaign filter and the archived on request, all part of the page's address. Opening a person
shows where they are and their local time, the campaigns they are on, an excluded one outlined, and **Add to campaign**
(`jason campaign add-contacts`). The **Timeline** puts their open work above the now line and what happened to them
below it: the journal's lines about them (`jason journal list --contact`), narrowed by chips to replies, work,
decisions or changes, with their additions to and exclusions from campaigns in place. **Details** is their record:
channels, facts, custom fields, and what each provider calls them. A person a provider knows has their threads there,
read from the provider only when the section is opened (`jason contact threads`, and `jason contact thread` for one),
outside any work item: nothing about such a read is journaled, retried or pinned. The runtime passes a message's body
through as the provider holds it; the page parses it where nothing can run or load, and shows only its structure,
emphasis and web or mail links.

## What the page may do

The page holds its token in the browser's storage for the runtime's origin and sends it on every call, as the command
line sends the run token. A request from any other origin is refused by the runtime, and the browser never sends the
token to another site. The token has the same rights as the run token: there is one level of trust, the machine's user.
