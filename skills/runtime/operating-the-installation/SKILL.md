---
name: operating-the-installation
description: Use when a person asks about the Jason installation itself rather than about campaign work - whether the runtime is up, starting and stopping it, having it start at logon, which plugins and routes exist, which journal kinds a subscribing plugin is handed and where each stands, which accounts' events it reads and reading a span of one again, which agent hosts are registered, whether notifications reach the person and turning them off, whether a newer version has been released, opening the browser UI or unpairing a browser, and installing Jason or taking it off again.
metadata:
  status: draft
---

# Operating the installation

This skill is about the installation, not about the work it performs. A person reaches it when they ask whether
Jason is up, why nothing is happening, what is installed, where work would go, or whether they are on the
current version. Campaign work itself is `managed-campaign-work`.

**Status: draft.** No agent host has read this text in the state it ships in. Its commands parse against this
build, because every skill's do. It covers what this build does and nothing else.

Two things are true throughout and are easy to forget. **Nothing is registered until somebody asks**: Jason
starts because you started it, or because somebody registered it to start at logon. And **no provider ships
configured**: a fresh installation performs no provider work at all until an operator has installed a plugin and
written a route.

## Is it running

```
jason runtime status --human
```

That asks the runtime for `system.info` and prints what it says: the version, the data directory it is using,
how long it has been up, the migrations applied, and — once the runtime has successfully asked the release
feed — an `update` section saying whether a newer version exists.

If nothing answers, the command exits 3 and says the runtime is unreachable. That is a runtime that is not
running, not a runtime that is broken; start it. If it answers but reports a data directory you did not expect,
something is running under a different `JASON_DATA_DIR` than the one you are typing against, and the two will
disagree about everything else you ask.

## Showing the person the UI

```
jason ui open
jason ui open --no-open
jason ui tokens
jason ui revoke <token-id>
```

`jason ui open` answers with a link into the browser UI and opens it in the person's default browser; `--no-open`
prints the link alone, which is what you want from a session: hand the person the `url` and say it works once, within
a minute. After that pairing, a bookmark of the page keeps working. `--approval <id>` and `--question <id>` open on one
item.

If it fails with `ui_not_installed`, no UI build is in place, and the message names the fix: `jason ui install` places
this release's build as `ui/default`, which is also the repair `jason status` prints. Run it, then `jason ui open`
again. A build from source has no published UI, and the command says so: then say that a build is made from `ui/` as
its README says and unzipped into `ui/custom`, and do not build anything yourself.

`jason ui tokens` lists the paired browsers; `jason ui revoke <token-id>` unpairs one, `--all` every one. Revoke when
the person says a browser should no longer have access. `jason status`'s `ui_module` check says which build is served,
names `jason ui install` where none is or the default one is another release's, and never decides readiness.

`jason session open --approval <id>` (or `--question <id>`) opens a new terminal on the person's desktop running their
own agent host on that one item, so run it only when they ask for a session on it. Which host it starts is the named
preference `agent.interactive_host`, a person's own word like every preference:
`jason preference set agent.interactive_host --value-file host.json --actor human:<their-name>`, with `host.json`
holding `{"host":"codex"}`, and `unset` goes back to `claude_code`.
`--dry-run` prints what would start and starts nothing.
With no item, `jason session open` opens a new terminal at a folder with no first input: `--campaign <id>` for a
campaign's folder, `--project <id or slug>` for a project's, neither for the harness root; `--here` opens it at the
folder the command runs in; `--host <host>` names the host, or `--shell` opens a plain shell there. `--resume <id>`
continues the host's own conversation of that id instead of starting a new one, for `claude_code` and `codex` only; the
id is the one the host named, never one you make up. It too opens a window on the person's desktop, so run it only when they
ask for one.

## Starting and stopping it

```
jason runtime start
jason runtime stop
jason runtime restart
```

`jason runtime start` launches the runtime in the background and prints the instance it ended up talking to —
which may be one that was already running, because starting a second runtime on the same data directory is not
something it will do. `jason runtime stop` asks that instance to shut down and returns only once it is gone, so
when the command comes back the process is really finished rather than on its way out. `jason runtime restart`
is the two in order, so it is a stop as well: the next paragraph holds for it.

**Ask before any stop you run yourself.** Before you run `jason runtime stop` or `jason runtime restart`, whatever
brought you to it, ask the person in words, say what a stop does, and run it only on their yes. That holds when the
restart was their own request, and when the runtime looks idle: work can be claimed between the status you read and
the stop. What a stop does: new work stops being claimed, what is in flight gets up to ten seconds to finish, nothing
is cancelled, and a provider operation already under way may still act, ending at its own lease.

`jason runtime run` keeps the runtime in the **foreground** instead, and it is **not** the normal way to run
it — the normal way is `jason runtime start`. You would type `run` when something else owns the process: your
own service manager (systemd, launchd, a supervisor) that expects to hold a child of its own, or a second
runtime you want to watch in a terminal against a different data directory.

```
jason runtime run --detached --data-dir /srv/jason-data
```

That form — detached, on a named data directory — is what a logon registration runs.

## Having it start at logon

```
jason runtime autostart status
jason runtime autostart enable
jason runtime autostart disable
```

`enable` registers **the runtime itself** to start when this account logs on: a logon task on Windows, a
LaunchAgent on macOS, a `systemd --user` unit on Linux. `disable` takes that registration away, and `status`
says what is registered and whether the executable it names is still on disk. On macOS the registration also
carries the `PATH` of the terminal `enable` ran from, which the runtime started at logon then searches, and
`jason runtime autostart status` says it under `path`; an agent an earlier version registered carries none, and
launchd's default `/usr/bin:/bin:/usr/sbin:/sbin` holds no program a package manager installed.

**It is registration, not supervision.** Nothing watches the runtime afterwards: a runtime that stops stays
stopped until somebody starts it. There is no restart policy, and no second process whose job is to keep the
first one alive.

**The registration carries the data directory it was made under.** That is why it is a verb rather than a line
to copy — registering under a `JASON_DATA_DIR` you had exported, without saying so, would give you a runtime on
`~/.jason` at every logon and two runtimes disagreeing about which data is real. A second `enable` under a
different directory replaces the registration rather than adding one.

`enable` starts nothing now and `disable` stops nothing now; they change what happens at the *next* logon.
Starting and stopping the runtime today is still `jason runtime start` and `jason runtime stop`.

**`status` is how you check it.** It reads back what the operating system holds for this account. Leave the
registration to `enable`, `disable` and `status`: do not run, change or delete it with the operating system's own
tools (`schtasks` or the Task Scheduler, `launchctl`, `systemctl`), not even to test it. Running it by hand starts a
runtime now, which is not what the person asked for. If they want the runtime running now as well, that is
`jason runtime start`: say so, or ask.

### On Windows: from an ordinary prompt, and what it costs

**Run `enable` and `disable` yourself.** On Windows they work from an ordinary prompt, which is the one you
have, on an administrator's account and on a standard one, so there is nothing to hand the person. `status`
works from there too.

At their next sign-in the runtime starts in their own session, with their own account's rights. It shows no
window on Windows 11 24H2 and later; on an older Windows a console window opens at sign-in and closes by itself.
Tell them the one cost when you set it up: the runtime **ends when they sign out**, like any program left open,
and the next sign-in starts it again. Work that was in flight is left the way a crash leaves it: what had not
started is handed out again by the next runtime, and a provider operation that may already have acted waits for a
person to decide rather than being repeated, as `docs/work-execution.md` says under "Stopping and restarting".

Each account has its own task, named for it, so several people on one machine never meet each other's. What
0.1.0 registered was one task called `Jason` for the whole machine; `enable` and `disable` take it away only where it
is the person's own 0.1.0 task, the one that runs the runtime, and never touch anybody else's, or a task of that name
that runs anything else.

Only when `jason status` says the runtime runs in session 0, or the person tells you 0.1.0 had autostart enabled,
is the runtime that 0.1.0's task started likely to be holding the data directory. If `enable` or `disable` answers
`ERROR: Access is denied.`, follow its message first, as below. After `enable` or `disable` once, `jason runtime stop`
once ends it; ask first, as "Starting and stopping it" says. Then `jason runtime start` starts the runtime this
version runs, or the person signs in again with autostart enabled.

**If `enable` or `disable` answers `ERROR: Access is denied.`**, read the rest of its message to the person. It
names the task and the likely case: that task was registered from an elevated prompt — by an earlier version of
Jason, for a task named `Jason`, or by this version, run from an elevated prompt — and only an elevated prompt can
take such a task away. An elevated prompt is something a person opens, and **you cannot open one**, so do not retry
and do not try to elevate. On an administrator's own account, give them `jason runtime autostart disable` to run
once from an elevated prompt, and ask what it said. On a standard account an elevated prompt runs as somebody else,
the administrator whose password is typed, and a `disable` there removes that administrator's tasks, not theirs:
tell them to ask an administrator to delete the task by the name the message gives, and never run `jason` under
another account. Run `jason runtime autostart enable` yourself afterwards only if it was `enable` that was refused;
after a refused `disable`, the removal was the whole job. If `jason uninstall` stopped there, the same removal comes
first, then `jason uninstall` again. If the message says instead that the machine refused to register a logon task
for this account, their machine does not let their account register one, and `jason runtime start` still starts
the runtime by hand.

## Notifications

When Jason starts waiting for a person — an approval parked at a claim, or a question a role raised — when a
protective action fails, when it finds a span of an account's events it will not read, or when a provider block is
open, it tells them once, with a pointer: how many, and where to look. A pointer that says an account has events
Jason did not read sends you to `jason events sources`, as "The accounts whose events it reads" says. One that says
provider blocks hold work items sends you to `jason block list`, which shows each block, what it holds and where the
person clears it. The toast, the operating system's own notification, is on by default and needs nothing set up;
`jason runtime status --human` says on its `Notify:` line what each channel did last.
`Notifications:Enabled` set to `false` in `~/.jason/config/settings.json` turns all of it off, and
`Notifications:Toast` set to `false` turns off the toast alone. `docs/notifications.md` says when a burst is said, and
what a pointer never carries.

Naming a notification plugin in `settings.json` sends a pointer to a third party's server. Do it only when the person
asks, with the topic they choose. On the public ntfy server a topic is readable by anyone who knows its name, so tell
them that before they pick one; the plugin's own README is the procedure.

When someone says nothing ever tells them anything, read `jason status`'s `notifications` check and say what it found
rather than guessing. Its states mean:

- **`absent`, notifications off or no channel on** — say how to turn them on, and leave it to them.
- **`absent`, a toast switched off** — Windows has Jason's notifications switched off in Settings → System →
  Notifications. That is the person's own switch; tell them where it is.
- **`absent`, no desktop** — a machine with no session bus or no notification program shows no toast by design; a
  plugin still reaches them, if they want one.
- **`failed`, session 0** — the runtime has no desktop: a service or an SSH session runs there, and so does the runtime
  0.1.0's logon task started. For that one, the check names the acts that end it, in order; for its
  `jason runtime stop`, ask first, as "Starting and stopping it" says.
- **`failed`, a channel or a plugin** — the fact names the code or the reason; a plugin that was not attempted is a
  settings or grant problem the plugin's README explains.

Do not send a test notification through the operating system's own tools, and do not edit the registry: what Jason
shows, and what it leaves in the account, is the runtime's to write and `jason uninstall`'s to remove. Do not disturb
is invisible to Jason: a toast sent then goes to the notification center without a banner, and is recorded delivered.

## Where the person's files live

What the person tells Jason about their business in their own words is kept in plain files under one folder, the
harness root: `Jason` in the home directory, unless `Harness:Root` in `~/.jason/config/settings.json` names another
folder as an absolute path; `jason runtime status` says which root is in force. Move it only when the person asks:
move the folder, set `Harness:Root` to where it is now, and then link each project whose folder moved with it:
`jason project link` run inside that project's folder, or given the folder's absolute path with `--path`, finds the
project by the marker there. Nothing moves by itself, and a campaign created in a project not linked again is
refused until it is. In the chat of Jason Desktop the person types `jason project link`, like `jason role add`, in a
terminal: the chat refuses both, so say which command to run and in which folder rather than run it.

## The plugins it has loaded and where work goes

A plugin is a package of provider operations. A route says which plugin performs which operation for which
campaign. The two are separate: installing a plugin makes an operation *possible*, and only a route makes it
*happen* for a campaign.

```
jason plugin list --human
jason plugin reload --reason "installed my plugin"
```

`jason plugin list` prints the active snapshot: every plugin with its digest, the capabilities it was granted,
and any problems that make it unavailable. A plugin reported `unavailable` is usually environmental — the
program it needs is not on the `PATH` this runtime started with, which is why a plugin that works from your
terminal can be unavailable to a runtime that started at logon. `jason status` names each unavailable plugin
with its problem codes in its optional `plugin_availability` check, which never makes the installation not
ready: work routed to such a plugin fails at the claim, and a notification plugin delivers nothing; nothing
else stops. Beside it, the optional `plugin_grants` check names each plugin, an unavailable one included, that asks
for a capability and is granted nothing of it, by its id and the capability's key under `Plugins:Grants:<id>`; it
never decides `ready` either, and `jason plugin list --human` says what the plugin asks for, the key and the
settings file.

On macOS a runtime started at logon is a LaunchAgent, and launchd's default search path is
`/usr/bin:/bin:/usr/sbin:/sbin`, which holds no program a package manager installed; the registration records the
`PATH` of the terminal it was made from, and `jason runtime autostart status` says which it carries. The repair is to
register again from a terminal whose `PATH` has the program, with `jason runtime autostart enable`, then
`jason runtime restart`, which is a stop and asked first.

`jason plugin reload` reads every package again and swaps the snapshot, or keeps the old one and says what is
wrong. It is all or nothing: a package problem rejects the whole reload, and the runtime carries on with the
plugins it already had.

A provider on a fresh installation is connected by four acts, in this order, and steps 3 to 5 of
`docs/golden-path.md` say each in full:

1. Install the package: `jason plugin install reply`, the release's own, placed and loaded and granted nothing yet.
2. Grant it what it asks for, under `Plugins:Grants:<id>` in the settings file (`~/.jason/config/settings.json`).
3. Route its work, under `Routes` in the same file: the plugin, and the account its work goes through.
4. Reload: `jason plugin reload`, which reads the grant and the route together.

`jason plugin install --human` and `jason plugin list --human` name each capability a plugin asks for and is granted
nothing of, with the key that grants it and the settings file: that sentence is what to ask the person to allow. The
program a package runs comes first, the Reply CLI section of `docs/golden-path.md`. Where `npm install -g` is refused
with `EACCES`, npm's global prefix is a directory this account does not own: install it under a prefix of the
account's own, `npm install -g --prefix ~/.local reply-cli`, and put `~/.local/bin` on the search path the runtime
sees.

Those three verbs are the whole plugin vocabulary, and **there is no `jason plugin get`**. One plugin by name is
that listing read down to its row — `--human` for a person, or the default JSON for a session that is going to
pick the row out itself.

```
jason route list --human
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll --human
```

`route list` shows the global routes plus a campaign's. `route resolve` answers the only question that matters
before promising work: for *this* campaign and *this* operation, which plugin would perform it, or why nothing
would. A route can also be set per channel, for the operations that send on one (`message.send`, for instance):
`route list` shows those routes beside the operation's own, and `route resolve` answers for a channel when it is
given `--channel`, such as `--channel linkedin`. No provider ships configured, so nothing routed is the normal state of a fresh installation rather than a
fault — **writing a route is an operator's explicit act**. If the answer is that nothing is routed, say so
plainly instead of submitting work that will fail.

## The journal's kinds and the plugins that subscribe to them

The journal is the chronicle of everything the runtime wrote, and every line has a kind. `jason journal kinds` lists
them: each kind the runtime reserves for its own lines, as its document says it, with what it means, what it carries and whether a plugin may subscribe to it. Read it before
promising that a plugin can be handed some kind of line, and when a `jason journal list` line names a kind you do not
know.

```
jason journal kinds --human
jason subscription list --human
```

A plugin of `kind: subscriber` is handed, after the runtime wrote it, each line of the kinds its manifest declares
**and the person granted**. Declaring a kind is not being given it: the grant is `Plugins:Grants:<id>:Subscriptions` in
`~/.jason/config/settings.json` (`*` admits every declared kind and nothing more), and the order of the plugins in
one kind's chain is `Subscriptions:Order:<kind>`. Both are read at a start and at each `jason plugin reload`, so a
change is not in force until the person has reloaded. Writing them is the person's act, as writing a route is;
`docs/plugins.md` §16 and §17 say the rest, and `plugins/journal-forwarder` in the plugin marketplace is the smallest
complete subscriber.

A plugin may also ask to append lines to a file, and what it may write into is the person's to say, as well: the
directories are `Plugins:Grants:<id>:Files` in the same settings file, each an absolute path, read at a start and at
each `jason plugin reload`. A directory is refused when it is the home directory or a folder holding it, the top of a
drive, a share or the file system, the data directory or the harness root, or a folder inside or holding either of
those two; a folder inside the home directory is where a person grants, and it is allowed. A relative path, `~` and
`*` are refused too. Any refusal is `grant_path_refused` and rejects the whole reload, so the previous load stays;
at a start there is no previous load, so the runtime comes up with no plugin loaded until the setting is fixed and
`jason plugin reload` is run. `jason plugin list` says why: its `last_reload` has `activated` false and, in the
plugin's entry of `candidates`, a problem `grant_path_refused` at `grants.files[i]`; `--human` prints it under
"last reload rejected".
Give the plugin a directory of its own, never a folder another program runs from or reads its instructions from;
`docs/plugins.md` §9 says the rest, and `plugins/journal-forwarder` is the plugin that uses it.

`jason subscription list` answers where each subscribed kind stands: its chain, its cursor, its newest line and how many
of its lines are due, the chain in flight if any, and `waiting_on`, which names the members of the chain the current load
cannot run because they are `unavailable`. A kind waiting on a plugin is not failing: it stays at its line until a
reload makes that plugin available or takes it out of the chain, and it writes nothing meanwhile. With nothing
installed that subscribes, the list is empty and nothing is wrong. What went wrong inside a chain, and the rewind that
hands lines again, is `troubleshooting-jason`'s.

## The accounts whose events it reads

```
jason events sources --human
```

A plugin that reads its accounts' events — `jason plugin list` shows `events` beside its operations — has every account a
route binds read on the runtime's own cadence: that account is a **source**. `jason events sources` is their record:
whether a route binds the account now, its last read and how it ended, whether it is healthy — while it is, the inbound
read leaves the campaigns it serves to it — when it is read next, and, under `NOT READ`, the latest span its own reading
skipped. That column stays once a span asked for again has read it: the spans asked for again are listed below the
table with how each ended, so read them before you offer to read a span again. A source never shows its cursor, and you
do not need it.

A span not read is what an account was not read for longer than the thirty days a read reaches back: the runtime was off,
or no route bound the account. Jason says it once, in a notification and in the source's record. It is read only when
the person asks:

```
jason events backfill --source evs_01K7QN0000000000000000000A --from 2026-08-01T00:00:00Z --to 2026-09-01T00:00:00Z --reason "read the weeks the runtime was off"
```

Ask first, and run it on the person's yes, with their words as the reason. It reads and sends nothing, and leaves the
account's own reading where it stands. What it finds that a campaign was not told, and that happened after the campaign
went live, wakes that campaign's manager; what happened before is kept and says nothing. A span reaches at most 366
days back and ends no later than now; eight may wait for one account. Each span asked for is listed under its source in
`jason events sources`, with how it ended.

`jason events signal` is for a program the person runs to receive a provider's webhooks: it has the account read at the
next scan, never within a minute of its last read, and carries nothing of what the webhook said. Do not run it yourself
to see sooner what an account holds.

## The agent hosts it can launch

```
jason profile list --human
```

AI role work runs on an agent host the operator installed, described by an **execution profile**: what program
starts it, what it may not do, and the command word it calls home with. `profile list` shows the profiles that
can launch work; disabled ones are left out unless you ask for them.

This is what to read when role work is failing for reasons no model produced — `profile_not_found`,
`profile_disabled`, `profile_revision_refused` and `host_not_available` are all answered here rather than by asking for the work again.
Registering or editing a profile is an operator's act, and it names a program on this machine: do not invent one.

## Keeping it current

```
jason update check
jason update status --human
jason update apply
jason update rollback
```

`jason update check` asks the release feed whether a newer version exists and prints the answer. It downloads
and installs **nothing**, and it asks the feed rather than the runtime, so it answers on a machine whose runtime
will not start.

`jason update apply` performs the update: it downloads the release for this platform, proves its digest, stops the
runtime, replaces the executable and starts it again. `jason update rollback` puts back the version the update
replaced. `jason update status` says where an in-flight update stands, or what the last completed one recorded; it
needs no runtime either. From 0.20.0, Jason's own installation on 0.18.x or earlier updates through 0.19.0:
`jason update apply --version 0.19.0`, then `jason update apply`. Its own `jason update check` names 0.19.0 as
`install_first` and its apply as `upgrade_command`, and a plain `jason update apply` refuses with
`update_not_directly_applicable`, naming 0.19.0. A package manager's upgrade goes straight to the newest release.

After an update, `jason status` says whether the role skills still belong to this build: role skills a default
install took at another release's tag fail its `role_skills` check, which names both tags, and its repair is the
line it prints. From 0.12.0 on, `jason update apply` redeploys the skills this installation recorded as its last
step, once the update is complete; if that step's line says it could not, the update still stands and the fix is
the line it names. An update applied by a build before 0.12.0 has no such step, so it needs one
`jason skills update` after it. After the skills it places the release's UI, and an update applied by a build older
than that step places none, so `jason ui install` follows it; `jason status` names it until it has.

A skill file the person edited, or a file they put in a skill, is theirs: the skills verbs, `jason skills update`
and the install it repeats, keep it, write everything else around it, name each file they kept and succeed. The
skill holding it is then a blend of two hands, the person's and the release's, which the person reconciles. Only
`--force` overwrites it, and it is the person's to type, never yours to add.

**Installed by a package manager.** Where Homebrew or winget installed Jason, `jason update check` names the
manager's own upgrade, and `jason update apply` refuses the installation. The manager's upgrade is typed as three
lines, the runtime stopped first and started after, and only when the person asked for it, as for
`jason update apply`:

```sh
jason runtime stop
winget upgrade Reply.Jason
jason runtime start
```

with `brew upgrade jason` in the middle under Homebrew. If you run the stop, ask first, as "Starting and stopping it"
says; if the person types the lines, tell them what it does all the same. What a stop does: new work stops being
claimed, what is in flight gets up to ten seconds to finish, nothing is cancelled, and a provider operation already
under way may still act, ending at its own lease. On Windows winget cannot replace the executable while the
runtime runs: it answers Access is denied and keeps the old version. Under Homebrew the upgrade removes the
previous version's keg by default (unless
`HOMEBREW_NO_INSTALL_CLEANUP` is set), and a running runtime starts every plugin host from its own executable's
path, which is then gone: work after the upgrade fails until the runtime restarts, which `jason status`'s
`runtime_version` names.

**How to behave about a version you noticed.** When `jason runtime status` reports that a newer version exists,
you may mention it conversationally — one sentence, **at most once** per session — and then get on with what was
asked. **Never interrupt** the task the person actually came with in order to talk about a version.

And **never run `jason update apply` unless the person asked for it**. It replaces the executable that is
running and stops the runtime to do it, so work in flight is drained and everything pointing at Jason is briefly
pointing at a file being swapped. Noticing an update is yours; deciding to take it is theirs.

An update that stopped part-way is the exception, because the person already asked for it: `jason status` names the
update and the step it stopped at, and its repair is `jason update apply`, which finishes it and starts the runtime
itself. Before its swap nothing has been replaced, the optional `update` check says so, and `jason runtime start` starts
the installed build; from the swap on the `runtime` check fails, and `jason runtime start` refuses with
`update_in_progress` and starts nothing, as does the start a sign-in makes: only the update's own starts run then, the
apply's and the rollback's. `jason update rollback` is the other way on, and that choice is the person's.

`jason update apply` replaces each plugin package installed as an earlier release's with the new release's, and leaves
every other package under `~/.jason/plugins/` as it is; its `plugins:` lines say which. A package it could not replace,
or one a package manager's upgrade left, since that upgrade runs no apply, makes `jason status`'s optional
`plugin_package` check fail: the package installed is an earlier release's. Its fact names the installed version, the
version this release published, and the command that places it, `jason plugin install <id> --replace`, which swaps the
old package for the release's and asks the runtime to load it; replacing a package is a change to the installation, so
ask first. `jason update check` says it before the update: its `plugin_updates` names each package installed here that
the newer release publishes a later version of, exactly the ones the apply replaces, with both versions, and is `[]`
when nothing installed here that the record holds is behind. It never names a package the list does not know: built or
edited on that machine, or a version before its package's list begins, which for the Reply package is 0.3.2 and
for every other is 1.0.0; whether that one is behind is `jason status`'s to say, by its version.

## Installing it in the first place

Installing Jason is `docs/INSTALL.md` (<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md>),
followed in order, its §4 included: that section teaches the installation its role skills, which the install script
has already done as its last step unless it said it could not, and without them every role it launches runs
untaught. A person installs it with the two lines below. If they ask you to install it, follow
that page yourself, by its section for an agent: a package where the page names one for this platform and its
package manager is already there, otherwise the steps it gives by hand. Never pipe a download into a shell, and
ask them before every act it marks "Ask first". The two lines are the person's: where neither route is open to
you, hand them the lines to type.

```sh
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Each script works out the platform, verifies the download's SHA-256 before unpacking it, installs the executable
under the person's own account — `~/.local/bin` on macOS and Linux, `%LOCALAPPDATA%\Programs\jason` on Windows —
and puts that directory on the `PATH` unless `--no-modify-path` is passed. Then the executable it installed deploys
the role skills from the release's package of them; where that fails, Jason is still installed, one line says so,
and `jason status` names the line to run. Nothing needs
administrator rights.

Both lines install the latest release. If either answers 404, the release could not be reached: say that, as it
stands, and that there is nothing to build instead. Do not paste a line again in the hope of a different answer.

When `jason status` says `ready` after an install you made, tell the person where Jason keeps their files and what you ran, in the
lines the install guide's section for agents gives, then start the `quick-start` skill at once; do not ask what comes
next. A host that does not see a skill installed a minute ago reads its file at `<root>/quick-start/SKILL.md`, where
`<root>` is the one on the `Target:` line of `jason skills install` that names the `jason-runtime-skills` pack and the
host. The wiring check, `docs/golden-path.md`, is a synthetic walk to meet the system, which connects their provider
and proves it with their own address, and is not the real work; `quick-start` ends by saying it exists, and you take it
only when the person says so. A person who skips it has no provider connected: when the work first needs one, the Reply
CLI section and steps 3 to 5 of `docs/golden-path.md` connect it, without the walk's enrolment. What the person tells
you about their first campaign becomes the campaign's context when the campaign is created (`managed-campaign-work`),
and the work is planned with the `campaign-planning` skill and worked by Jason's roles.

The provider's own web app is no step of any Jason flow. A person opens it by a link for four things only —
connecting a LinkedIn account, connecting a mailbox by single sign-on, signing in, and creating the first API key —
and each is theirs to do: say what it is for, and wait until they say it is done. Never send them there for the work
itself.

## Moving the installation to another machine

Install Jason on the new machine first, the same version or a newer one. Then, on the old machine:

```sh
jason runtime stop
jason export
```

and on the new one, with the archive copied across:

```sh
jason import jason-export-20261109T100005Z.zip
jason skills install
jason runtime autostart enable
jason status
```

`jason export --stop` and `jason import --stop` stop a running runtime themselves; without the flag both refuse
while it runs and name `jason runtime stop`. **Ask first** before you run `jason runtime stop`, `jason export --stop`,
`jason import --stop` or `jason import --replace`: say in words what is stopped, and for `--replace`
that the installation already here is moved aside into `<data>/replaced-<stamp>/` and `<root>.replaced-<stamp>`,
and run it only on the person's yes, as "Starting and stopping it" says. The export starts the runtime again when it
was running; the import ends with it running, because it asks it to record each project folder where it now
stands, unless the runtime does not start: then the import exits 1 with the files in place, and names
`jason runtime start` and then `jason project link` for each project folder. Of the three
lines the import leaves to the person, `jason runtime autostart enable` is only for when they want it started at
logon and the old machine did not do so; when it did, the import has already registered it, or said that this
machine cannot.

**What travels:** the database, the settings file, the plugins, the role skills with their record, a custom UI,
the logs, and the harness root's tree — the person's memory files and every project's and campaign's folder.
Whether the runtime started at logon travels as a fact, and the import registers it again. **What does not:**
backups, the work directories, staged skills, the shipped UI, the copies an update keeps, anything an earlier
import moved aside, the skills deployed into agent hosts, and anything outside the data directory and the
harness root. The archive carries the settings file with everything in it, a token under `Plugins:Config`
included: tell the person to keep the archive as they keep that file.

The import refuses before it writes anything when the archive is damaged, names a path outside where it
places files, a newer Jason wrote it, or an installation is already here — a database, or a harness root with
a folder marker in it, which a Jason that has already run on the new machine has. `--replace` then moves what is
there aside, into `<data>/replaced-<stamp>/` and `<root>.replaced-<stamp>`, and deletes nothing: those are the
person's to remove once they are content. In the settings file every path under the old machine's home
directory, data directory or harness root is rewritten to the same place on this one, and each rewrite is said;
a path elsewhere that is not on this machine is said and left as written, for the person to correct.

## Taking it off again

```sh
jason uninstall --human --dry-run
jason uninstall
```

Show the dry run first: it prints exactly what would go and changes nothing.

**Skills go only by receipt.** Every root a deployment wrote into carries a record listing each path written
there, and the verb removes those skills and nothing else. **A skill somebody copied into a harness by hand has
no receipt, so it is removed by nobody, including this** — say so while you are there, rather than leaving
them to find it months later. A recorded file whose bytes they have changed since is reported and kept unless
`--force` says otherwise. The rest — the logon registration, the PATH entry where the installer wrote it, the
executable and what it unpacked, and on Windows what every other build of Jason unpacked — goes by rule, and the
dry run names each. On macOS and Linux the other builds' directories stay, counted in one line, for the person to
remove once nothing runs from them.

**The data directory is kept** unless `--purge-data` says so, and the verb names it in one line; with it, what
Jason keeps there goes and anything else in it stays. In the machine shape `--yes` has to be given in advance,
because there is nobody there to be asked. It exits 0 when everything it set out to remove is gone, and 1 when
it did not get there — it refused (a runtime that would not stop, a purge nobody confirmed), something it set
out to remove is still there, or it stopped part-way — and every step that did happen is listed either way.
`docs/INSTALL.md` §7 is the fuller account.

## What this skill does not cover

Campaign work, the roles that perform background AI work, reading the code a failed work item carries, and
anything only a person may decide. Those are `managed-campaign-work`, the role skills, `troubleshooting-jason`
and `approvals-and-questions`.
