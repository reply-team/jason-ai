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
`jason ui open` does, and a link Jason does not know opens nothing ([notifications.md](notifications.md), §7). Where
Jason Desktop is installed for your account, the same click opens the desktop's Needs you instead
([desktop.md](desktop.md), Links).
`jason://attention/approvals/<id>/prompt` and `jason://attention/questions/<id>/prompt`, the toast's Copy prompt button,
put that item's prompt on the clipboard and open nothing: the text of [Copy prompt](#copy-prompt) below.
`jason://attention/approvals/<id>/session` and `jason://attention/questions/<id>/session` start an agent session on
that item, as `jason session open` does ([Open an AI session](#open-an-ai-session) below). Windows learns these links when
the first toast is shown; before that a browser has no program for them. The `jason:` scheme is the runtime's and its
command line's; Jason Desktop's own links use another, `jason-desktop:`, which opens a page of the desktop's window and
never touches `jason:` ([`desktop/README.md`](../desktop/README.md), Links).

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

A browser you paired keeps its token in its own profile, which a command of a Codex chat may read; revoke a browser you
no longer use. The desktop's own window keeps its token in memory only and pairs afresh at each start. The first start of
a desktop that kept it on disk before revokes every UI token once, so a browser you paired must be paired again once.

## The port

The runtime listens on `127.0.0.1:52766` by default, so the bookmark is stable. When that port is taken it takes the
next free one above it and `jason runtime status --human` says so; `Runtime:Port` in `settings.json` picks another, and
`0` lets the OS choose, as before.

## Attention

What waits for you: approvals and questions, **Pending** or **Decided** (`jason approval list`, `jason decision list`).
Opening one shows what the runtime recorded about it, and an approval's panel says why it is there, who it reaches, from
which account and on which channel, and through which provider. An approval to send a message shows it under "The message as
written", before the raw request, which stays folded: an email's sender, recipient, subject and body in that order, with the
body's line breaks; a LinkedIn message's text; a template as written, then the runtime's own example of it for this person,
labelled as an example the provider fills in. It is all drawn as text, so markup in a message appears as the characters it
is made of. One sentence under it says what is not shown: the sender's signature kept at the provider, and the
provider's tracking, which are added when it sends. A reply in a thread shows who it goes to, the channel and the text.
A message written as text (not a template) and a reply can be edited there, a message's subject and body or a reply's
body and nothing else: **Edit message**, then **Save**, which is `jason workitem update` of the message's input with
you as the actor and a reason the page words. Saving approves nothing: the old card is gone, the page says "Jason asks
again with the new text when it next takes this work" and shows the new approval when it arrives, and Approve belongs
to that one; if it does not arrive while the page waits, the sentence stays true and the next refresh shows it. If the
text changed while you were editing, Save writes nothing: the page says so, shows the new text and keeps yours in the
box. A card is checked against its work item before it offers **Approve**, and again at the press: the approval's
subject, the arguments the runtime parked, is compared, as whole documents and whatever the order of their keys,
with the arguments the work item holds now, and the person with the item's own. Where they differ, whether the text
was changed by the AI in a chat or in some other way, Approve is not offered and one sentence says the text changed
and Jason will ask again. The AI in a chat may go on editing a parked message when you ask it to, and nothing is sent
without a new approval. The runtime's own sentence is shown for a refused edit, and what you typed stays in the box.
A row above the list narrows it to **All**, **Approvals**
or **Questions**, each option with how many of its kind are listed, the chosen one ticked so that it is told apart by
more than its colour, and "Shown: N" saying how many the list shows. The filter belongs to the screen alone: it is never
stored, never part of the page's address and never sent to the runtime, and it goes back to All when nothing at all is
listed, so whatever arrives next is shown. After a decision, the next item opened is chosen among the items the filter
shows. Approving, rejecting and answering are the person's, here as on the command line, and so are the two buttons that
brief an agent on one item, [Copy prompt](#copy-prompt) and [Open an AI session](#open-an-ai-session).

## Agents

The page calls a campaign with its chat an agent: **Agents** in the sidebar, **Add to agent**, "Find an agent". It is the
harness's campaign and nothing else. Only the words on the screen change; the commands, the API fields, the addresses
and the ids keep `campaign`, and so does the rest of this page, which describes them.

Every campaign, its people counted two ways: **Summary** is where they stand now (`jason campaign summary`), and
**Activity** what happened to them today, in the last 7 days or in the last 30 days, the days of this browser's time
zone (`jason campaign activity`). A person may be in several columns, and each column's name says on hover what it
counts. A campaign's numbers, in the table and once it is open, its People tab and its Results leave archived people
out (`--exclude-archived-contacts`), as People does while Archived is not ticked; its Inbox and What happened still
show what happened with them. A search finds a campaign by its name, and the archived ones appear on request
(`jason campaign list --query`, `--include-archived`); the view, the period and both filters are part of the page's
address. Every number opens its campaign with exactly the people it counts (`jason campaign list-contacts --summary`,
or `--activity` with the period),
each with why: a person waiting for you opens the approval or the question in place, as on Attention, anybody else
opens the person's panel in place, with the page's address left as the campaign's, and a failure shows the reason the
runtime recorded, with nothing on the page that retries it. Opening a campaign shows its status,
its people and the provider campaign it is linked to; **Add people** finds people and adds them (`jason campaign
add-contacts`). A campaign opens on its **Sequence**, which puts what is planned, what waits for you and what is running
above the now line, and ends with **What happened**, the campaign's journal (`jason journal list --campaign`),
narrowed by chips to replies, work, decisions or changes; its **People** are listed by the same counts with a search, Removed among them
(`--summary removed`), each row saying where the person stands in this campaign, its Stage word from `standing`; in Activity the tab is **People Activity**, and everything it lists is the period's, All being
everyone something happened to (`--activity people`); its **Inbox** shows its latest fifty reply lines in the order
Jason observed them, the latest first (`jason journal list --campaign --kind inbound_reply_observed`), each reply at the
moment the provider says its thread moved, the line's own moment only when it names none; the ten people Jason saw
reply last, each with their threads asked of the plugin their line names (`jason contact threads`), and a reply tied to nobody as its
channel and moment. It is read when the tab opens and when you refresh it, by its own Refresh, which it has whether
it is loading, empty or failed, or by the toolbar's while the tab is open. Its **Results** are four figures as the runtime counts them,
its people now and who was contacted, replied and opted out in the period (`jason campaign summary`, `jason campaign
activity`), with nothing divided on the page. A read of the Inbox or the Results that failed says so over that
tab of that campaign alone, never as loading, and is gone once you leave the tab. Its **Settings** are its record, its
context as the roles read it and what each provider calls it, and nothing else: the campaign's moves are in its header,
beside **Add people**, so they are within reach from every tab, each offered only where its status allows it and each
opening a dialog with an optional reason. **Pause** stops Jason releasing an active
campaign's work while what is running finishes; the provider's own sequence keeps sending until a role pauses it there.
**Resume** releases a paused campaign's work again, and **Start** a draft's for the first time (`jason campaign pause`,
`jason campaign start`, which resumes a paused one, with your reason). An address naming a campaign's Details opens its Settings, and one naming
any other tab it no longer has opens its Sequence. Shown inside Jason Desktop, a campaign's tabs begin with **Chat**, which
opens the campaign's one conversation, made the first time and kept by the desktop with what it showed ([desktop.md](desktop.md)), and moves
nowhere; the Agents list's Chat opens the same one. The desktop's window opens on **Start**, which asks who you want
to reach; what you send there starts a set-up chat, about no campaign yet, and no chat is made before you send. The
sidebar's first item is the **Feed**, the desktop's main chat; below **New chat** it lists the other conversations it
keeps, newest first and the open one marked, a campaign's by its name and a set-up chat by its first words; a
conversation is kept, and listed, from its first turn, so a chat never sent to leaves nothing behind. What happens in
the agents arrives in the Feed as one line each (a reply, an approval or a question waiting, an agent created,
started or paused, work that stopped, a provider block holding work), with the campaign's name opening that campaign's
conversation, and an approval or a question opening Needs you. A line carries no one's words and never starts a turn; the AI hears the lines that came with your
next message there, as records. One whose turn still runs opens running, with Stop. A set-up chat whose turn made one
campaign, with no other turn beside it, becomes that campaign's conversation, and one that made more, or ran beside
another, offers what its last turn made to pick from. **Start afresh** begins the AI's next turn without its memory
of the conversation and keeps what is shown; **Remove** asks first, then takes the conversation off this computer, and
the AI host keeps its own transcript of it. What the desktop refuses is said in its own words. The browser has no chat, and no Chat
tab.

## Performance

The whole operation over time, from the same counts as the Agents page's Activity: what happened to people today, in the last
7, 30 or 90 days, across every campaign not archived or the ones chosen, each person counted once however many campaigns
count them (`jason campaign activity` and its `total`), archived people left out of every number, chart and list
(`--exclude-archived-contacts`). Two charts show contacted and replied people by the hour today,
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
--query`, `jason contact list --query`: any part of a name, company, title, email, phone, profile link or custom
field's value), a kind, a role, and one day on Planned and Done: the day work is due, or the day it finished, stepped a
day at a time. They are part of the page's address, so a bookmark or a link from another screen keeps them. Opening an
item shows what the runtime recorded about it: its facts, its attempts, its input and its history; a failure is shown as
the error the runtime recorded, with its class and the attempts it used. Open work can take a new priority, start and
due time, or be cancelled with a reason, through `jason workitem update` and `jason workitem cancel`; work waiting for
your approval can be approved or rejected right there, as on Attention.

## People

Everyone Jason knows, as a table of Name, Company, Channels, Stage, Campaign and Last activity, the person something
last happened to first (`jason contact list --order activity`, two hundred at a time, more on request), with a search, a
campaign filter and the archived on request, all part of the page's address. Under the search sit an All chip and a
chip for each live count of a campaign's summary, Removed apart (`jason contact list --summary`): a chip keeps the
people that count counts. A chip shows its number whenever there is no search (`jason campaign summary`, one campaign's
own when the list is narrowed to it); while Archived is not ticked the page asks it to leave archived people out
(`--exclude-archived-contacts`), as the list does, so a number is the people its chip lists. Beside a search a chip shows
its word alone: a count knows no search.
Stage is where the person stands in the campaign the row shows, each count in its one word with the runtime's own
definition on hover (`standing` on each campaign of `jason contact get` and `jason contact list`). A person in several
campaigns shows the one the list is narrowed to, else the one their latest activity is in, else their newest not
excluded, with "+N" naming the others; under a chip, the first of those that holds the count; a person on no campaign
shows dashes. While a new list is on its way the rows stay as they were fetched. Last activity is the newest journal
line about the person, of any kind. Opening a person
shows where they are and their local time, the campaigns they are on, an excluded one outlined, and **Add to agent**
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
  folder, readable by your account alone, which removes itself and its folder when it runs. Terminal's shell starts
  without the CLI's environment, so where `jason` runs with `JASON_DATA_DIR` set the script exports that variable, and
  nothing else, for the host's own `jason` commands to reach the same installation. This is composed and checked
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

### At a folder, with no item

The same verb opens a session with no item: a new terminal window at a folder, running your own agent host or a plain
shell, with no first input. It needs no approval or question and decides nothing.

```sh
jason session open
jason session open --campaign <campaign-id>
jason session open --project <project-id-or-slug> --host codex
jason session open --shell
jason session open --here --host claude_code --resume <conversation-id>
```

- **The folder.** `--campaign` opens at the campaign's recorded folder, else its project's, else the harness root;
  `--project`, named by its id (`prj_…`) or its slug, at the project's folder, else the harness root. With neither, the
  project is taken from the folder the command runs in, as every verb that takes a project and is given none takes it
  ([projects.md](projects.md), §6): the nearest marker above it names a project, or a campaign whose project it is, never
  the campaign itself, and the line on stderr says so; it then opens at that project's folder, else the harness root.
  Outside every harness folder, or where the marker is not in the folder the runtime records, it opens at the harness
  root. Of the runtime it asks only `campaign.get`, `project.get` and `system.info`, and nothing is written there or into
  any folder. The harness root counts only once it is a folder with the harness's marker; there is no fall back to your
  home folder. `--here` opens at the folder the command runs in, as it is: no marker is read there, the runtime is asked
  for no folder, and the folder only has to be there. It does not go with `--campaign` or `--project`.
- **The program.** `--host` names one of the host words; without it the host is the one the named preference
  `agent.interactive_host` names (`claude_code` while it is unset), read with `preference.list`; `--shell` opens the
  system's plain shell instead: Windows PowerShell from the system folder on Windows (never one found on the search
  path), your login shell on macOS, and on Linux the program `SHELL` names by its absolute path, else `/bin/sh`. Nothing
  follows the program on its command line but what `--resume` adds. `--host` and `--shell` do not go together, nor
  `--campaign` and `--project`, nor any of the six with `--approval` or `--question`.
- **Continuing a conversation.** `--resume <id>` has the host continue its own conversation of that id, the one the host
  itself named, instead of starting a new one: Claude Code is started as `claude --resume <id>`, Codex as
  `codex resume <id>`, each in the words its own `--help` gives, and nothing else is added. The id is a letter or digit
  followed by at most 199 letters, digits, dots, underscores, colons or hyphens; anything else is a usage error. It takes
  the host `--host` names, else the preference's, and does not go with `--shell`. Another host is refused with
  `host_cannot_resume` as soon as the host is known, before its program or a terminal is looked for. What the session
  then may do is that host's own permission settings' to decide; Jason copies nothing of the conversation and writes no
  file for it.
- **The terminal** is the one an item's session uses, on each system as above, and the data directory reaches it the
  same way: on Windows and Linux the terminal inherits the CLI's environment, `JASON_DATA_DIR` included, and on macOS the
  script exports `JASON_DATA_DIR` alone. So `jason` in that window works with the installation the command was given.
  On macOS this form, like an item's, is composed and checked by tests here and has not been watched on a Mac; `--shell`
  there also rests on Terminal giving the script `SHELL`, your login shell, in its environment, with `/bin/sh` where it
  does not.
- **What it prints.** One object: `kind` (`folder`), `folder` (`level`, one of `campaign`, `project`, `harness_root` and
  `here`, and `path`), `host` (the host word, or null for a shell), `shell` (true when the plain shell was started),
  `program` (the path of the program started, as in an item's answer: the host's, or the shell's, `/bin/sh` on macOS),
  with `--resume` also `resume` (the id), then `terminal` and `started`; with `--dry-run` also `argv`, the host's words
  and the id among it, and on macOS `script`, where they are quoted as every word of it is. A refusal of its own is the API's error envelope on
  stdout, said again on stderr, with exit 1: `folder_not_recorded` (nothing in the chain has a folder, and the harness
  root is not a folder with its marker; the line says what stands there), `folder_missing` (the recorded folder, or with
  `--here` the folder the command runs in, is not on this machine), `preference_unreadable`, `host_unknown` (the
  preference names a word this `jason` does not know), `host_cannot_resume` (`--resume` was given for a host Jason knows
  no words to continue a conversation of), `host_not_found` (the host's program is not on the search path),
  `shell_not_found`, `terminal_not_found`,
  `terminal_start_failed` and `runtime_error` (an answer of the runtime's that cannot be read). A request the runtime
  took and did not answer in time is the one envelope, `request_timed_out`, and no second. A campaign or project the runtime does not know is the runtime's own refusal,
  `campaign_not_found` or `project_not_found`, printed as it came; no runtime is exit 3; flags that do not go together,
  an empty value, a host word this `jason` does not know or an id `--resume` does not take are a usage error, exit 2.

## Settings and Chat in the browser

**Settings** holds what this browser keeps for itself and the runtime never hears of: the theme, light, dark or the
system's, in the order Light, Dark, System. A new installation meets Light; a browser that already kept a choice, or
never chose and follows the system, keeps that. Inside Jason Desktop the desktop keeps the choice, so its starting page
follows it.

**Chat** says that the chat with the AI is part of Jason Desktop and links to the releases page where it is
downloaded, and says that everything here also works from a session with your AI in a terminal, with the skills Jason
deployed. The browser has no chat and starts no AI: the runtime starts no process for a screen, and a browser tab cannot ask you
before the AI acts.

## Accounts

One screen for what you have connected. **Provider** is the Reply connection of the desktop's Connect panel, drawn by
the same code and acting through the same acts of the desktop: adding an account and reconnecting one go straight to
the provider's link, with no box; pausing and resuming, unblocking, changing a daily limit and deleting each ask in a
box of the desktop's own; a connection link is opened or copied by the desktop and never shown in or kept by the page. In a browser the page says in one sentence that these
acts are the desktop's and offers none. **Notifications** shows, read only, what `system.info` says of the channels: whether
notifications are on, and for each channel whether it is on and what it did with the last notification (delivered, failed
with its code and class, not attempted with its reason, or nothing sent yet). The text a failure carried is not drawn.
The page says in one sentence where they are changed: under `Notifications:*` in the installation's settings file, or by
asking your AI, whose `operating-the-installation` skill covers it. The page writes no setting and adds no provider.

## Inside Jason Desktop

[Jason Desktop](desktop.md) mounts the same build with a second shell, the console, chosen once as the page loads by the
object the desktop puts on the page, `window.jasonDesktop`. In it the chat is home, in a dock with panels beside it:
**Needs you** (this page's Attention), **Agents** and **Activity**, which follow the journal as it is written, and
**Connect**, where the agent hosts found on the machine, the model keys, the Reply connection and the notification
channels are set up. An empty Needs you card says you are caught up and offers **Open your agents**. The browser's
other screens, People, Performance, Work items, System, Accounts and Settings, open in a page panel of the same dock, and every
screen's facts are the Runtime API's, so anything the console shows is a verb you could type. The console reaches the
desktop only through the object the desktop puts on the page and the runtime only through the API and the journal stream
(`jason journal watch` is the same stream); what the desktop does besides, its chat, its tray, its update, is on
[its own page](desktop.md).

## What the page may do

The page holds its token in the browser's storage for the runtime's origin and sends it on every call, as the command
line sends the run token. A request from any other origin is refused by the runtime, and the browser never sends the
token to another site. The token has the same rights as the run token: there is one level of trust, the machine's user.
