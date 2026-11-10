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

On Windows a toast opens the same page when clicked, through a `jason:` link: `jason://attention` for the list of what
waits, `jason://attention/approvals/<id>` and `jason://attention/questions/<id>` for one item. Each pairs as
`jason ui open` does, and a link Jason does not know opens nothing ([notifications.md](notifications.md), §7).
`jason://attention/approvals/<id>/prompt` and `jason://attention/questions/<id>/prompt`, the toast's Copy prompt button,
put that item's prompt on the clipboard and open nothing: the text of [Copy prompt](#copy-prompt) below.
`jason://attention/approvals/<id>/session` and `jason://attention/questions/<id>/session` start an agent session on
that item, as `jason session open` does ([Open an AI session](#open-an-ai-session) below). Windows learns these links when
the first toast is shown; before that a browser has no program for them.

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
its people and the provider campaign it is linked to; **Add people** finds people and adds them (`jason campaign
add-contacts`). A campaign opens on its **Sequence**, which puts what is planned, what waits for you and what is running
above the now line, and ends with **What happened**, the campaign's journal (`jason journal list --campaign`),
narrowed by chips to replies, work, decisions or changes; its **People** are listed by the same counts with a search, Removed among them
(`--summary removed`); in Activity the tab is **People Activity**, and everything it lists is the period's, All being
everyone something happened to (`--activity people`); its **Inbox** shows its latest fifty reply lines in the order
Jason observed them, the latest first (`jason journal list --campaign --kind inbound_reply_observed`), each reply at the
moment the provider says its thread moved, the line's own moment only when it names none; the ten people Jason saw
reply last, each with their threads asked of the plugin their line names (`jason contact threads`), and a reply tied to nobody as its
channel and moment. It is read when the tab opens and when you refresh it, by its own Refresh, which it has whether
it is loading, empty or failed, or by the toolbar's while the tab is open. Its **Results** are four figures as the runtime counts them,
its people now and who was contacted, replied and opted out in the period (`jason campaign summary`, `jason campaign
activity`), with nothing divided on the page. A read of the Inbox or the Results that failed says so over that
tab of that campaign alone, never as loading, and is gone once you leave the tab. Its **Settings** are its record, its context as the roles read
it and what each provider calls it, with the moves its status allows. **Pause** stops Jason releasing an active
campaign's work while what is running finishes; the provider's own sequence keeps sending until a role pauses it there.
**Resume** releases a paused campaign's work again, and **Start** a draft's for the first time (`jason campaign pause`,
`jason campaign start`, with your reason). An address naming a campaign's Details opens its Settings, and one naming
any other tab it no longer has opens its Sequence. Shown inside Jason Desktop, a campaign's tabs begin with **Chat**, which
brings forward the newest chat about the campaign the desktop still holds, opens one only when there is none, and
moves nowhere; the browser has no chat, and no Chat tab.

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

Everyone Jason knows, the person something last happened to first (`jason contact list --order activity`), grouped by
day, with a search, a campaign filter and the archived on request, all part of the page's address. Opening a person
shows where they are and their local time, the campaigns they are on, an excluded one outlined, and **Add to campaign**
(`jason campaign add-contacts`). The **Timeline** puts their open work above the now line and what happened to them
below it: the journal's lines about them (`jason journal list --contact`), narrowed by chips to replies, work, decisions
or changes, with their additions to and exclusions from campaigns in place. **Details** is their record: channels,
facts, custom fields, and what each provider calls them. A person a provider knows has their threads there, read from
the provider only when the section is opened (`jason contact threads`, and `jason contact thread` for one), outside any
work item: the runtime takes only the route's half of the claim, the plugin is loaded and available, it performs the
operation, and the route's binding passes its checks, and skips the rest of it, and with it the attempt, provenance, the
journal, pins, approval and retry; it has no bound of its own, running outside the handler pool `Dispatcher:MaxParallel`
sizes, one child each. The runtime passes a message's body through as the provider holds it; the page parses it where
nothing can run or load, and shows only its structure, emphasis and web or mail links.

## Copy prompt

The panel of an open approval or question on Attention has a **Copy prompt** button. It puts on the clipboard the text
that briefs an agent session on that one item, so a person can paste it into whichever agent host they use: what waits
and its id, the campaign's id and the folder recorded for it (the project's folder when the campaign has none, the
harness root when neither does), the skill `approvals-and-questions` and the line that installs it for the host, what to
read through the command line and what to bring back, and that the session decides nothing — approving, rejecting and
answering stay with the person, and so does a standing approval. The text holds nothing of the item itself, not what it
is about, a preview, the question's words or its options: the session reads the item live. The command line prints the
same words with `jason approval get <approval-id> --prompt` and `jason decision get <decision-id> --prompt`. The text
for an approval is [prompts/approval.txt](prompts/approval.txt) and for a question [prompts/question.txt](prompts/question.txt),
as the command line prints them for the recorded exchanges under `docs/api/fixtures/`; the page's test holds the button to
those two files, and the command line's test holds them to the command line.

## Open an AI session

Beside Copy prompt, the panel shows the command that starts your own agent host on that one item —
`jason session open --approval <approval-id>` or `jason session open --question <decision-id>` — to run in a terminal,
and on Windows an **Open an AI session** link that does the same when clicked. Either opens a new terminal window at the
folder Copy prompt names (your home folder when none is recorded), running the host the named preference
`agent.interactive_host` names (`claude_code` when it is unset), with the Copy prompt text as its first message.
Opening it decides nothing. Jason's brief asks the agent to propose only and not to approve, reject or answer; what it
may run is your host's own permission settings' to decide. An item that no longer waits starts nothing, and a host that
is not on the search path is named, never replaced by another. When a click opens nothing, the command beside it says
why: run it in a terminal and it prints the refusal and how to do the same by hand.

- **Windows** starts the console host, `conhost.exe` from the system folder, which hands the window to Windows Terminal
  where that is your default terminal; where your default terminal is the console host itself, you get a plain console
  window. The console host's window and its hand-off to Windows Terminal are not yet measured on a machine.
  The link works once a Windows toast has been shown, which is when Windows learns `jason:` links
  ([notifications.md](notifications.md), §7); until then the command beside it is the way.
- **macOS** opens Terminal through `open -a Terminal` on a script written to a folder of its own under the temporary
  folder, readable by your account alone, which removes itself and its folder when it runs. This is composed and checked
  by tests here; it has not been watched on a Mac.
- **Linux** starts the terminal the `TERMINAL` variable names, else `x-terminal-emulator`, and gives it the host in the
  form that program documents, decided by its file name: `kitty` and `foot` take the host's words after the program,
  `gnome-terminal` after `--`, `xfce4-terminal` and `mate-terminal` after `-x`, `wezterm` after
  `start --cwd <folder> --`. A name outside that table is given the host after `-e` as separate arguments, the form
  Debian Policy 11.8.3 requires of `x-terminal-emulator`. An emulator whose `-e` takes the command as one string and
  that offers no other form, `tilix` among them, cannot take the host that way: leave `TERMINAL` unset so
  `x-terminal-emulator` is used. A `TERMINAL` that holds whitespace, is a relative path, or names a program that is not
  found falls back to `x-terminal-emulator`, and the answer names the terminal that was used.

The page reads which system it runs on from the browser itself; the runtime is told nothing. `--dry-run` starts nothing
and prints the whole command, the brief included, and on macOS the script. Each host's own form for a first message is in
[execution-profiles.md](execution-profiles.md), §12.

## What the page may do

The page holds its token in the browser's storage for the runtime's origin and sends it on every call, as the command
line sends the run token. A request from any other origin is refused by the runtime, and the browser never sends the
token to another site. The token has the same rights as the run token: there is one level of trust, the machine's user.
