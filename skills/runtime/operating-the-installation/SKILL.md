---
name: operating-the-installation
description: Use when a person asks about the Jason installation itself rather than about campaign work - whether the runtime is up, starting and stopping it, having it start at logon, which plugins and routes exist, which agent hosts are registered, and whether a newer version has been released.
metadata:
  status: draft
---

# Operating the installation

This skill is about the installation, not about the work it performs. A person reaches it when they ask whether
Jason is up, why nothing is happening, what is installed, where work would go, or whether they are on the
current version. Campaign work itself is `managed-campaign-work`.

**Status: draft.** It covers what this build does and nothing else.

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
is the two in order.

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
says what is registered and whether the executable it names is still on disk.

**It is registration, not supervision.** Nothing watches the runtime afterwards: a runtime that stops stays
stopped until somebody starts it. There is no restart policy, and no second process whose job is to keep the
first one alive.

**The registration carries the data directory it was made under.** That is why it is a verb rather than a line
to copy — registering under a `JASON_DATA_DIR` you had exported, without saying so, would give you a runtime on
`~/.jason` at every logon and two runtimes disagreeing about which data is real. A second `enable` under a
different directory replaces the registration rather than adding one.

`enable` starts nothing now and `disable` stops nothing now; they change what happens at the *next* logon.
Starting and stopping the runtime today is still `jason runtime start` and `jason runtime stop`.

### On Windows, enable and disable are elevated acts

**`jason runtime autostart enable` and `jason runtime autostart disable` have to be run from an elevated
prompt.** Being signed in as an administrator is not enough — an unelevated prompt on an administrator account
is refused too, because the Task Scheduler will not accept this task's S4U logon from a medium-integrity
process. It answers `ERROR: Access is denied.` and registers nothing.

An elevated prompt is something a person opens, and **you cannot open one**. So when somebody on Windows asks
for autostart, do not try it and report the refusal as a fault: tell them it needs an elevated prompt, give them
the exact line to run there, and ask them what it said.

**Reading the registration needs no elevation.** `jason runtime autostart status` answers from an ordinary
prompt, so you can always find out what is registered — only the two verbs that change something need more.

**Standard Windows accounts are unsupported in this version.** Elevating from one does not help: the elevated
prompt runs as the administrator whose credentials were given, so it would register a logon task for that
account rather than for the person who asked. Such an account can still run the runtime with
`jason runtime start`, which registers nothing.

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
terminal can be unavailable to a runtime that started at logon.

`jason plugin reload` reads every package again and swaps the snapshot, or keeps the old one and says what is
wrong. It is all or nothing: a package problem rejects the whole reload, and the runtime carries on with the
plugins it already had.

```
jason route list --human
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll --human
```

`route list` shows the global routes plus a campaign's. `route resolve` answers the only question that matters
before promising work: for *this* campaign and *this* operation, which plugin would perform it, or why nothing
would. No provider ships configured, so nothing routed is the normal state of a fresh installation rather than a
fault — **writing a route is an operator's explicit act**. If the answer is that nothing is routed, say so
plainly instead of submitting work that will fail.

## The agent hosts it can launch

```
jason profile list --human
```

AI role work runs on an agent host the operator installed, described by an **execution profile**: what program
starts it, what it may not do, and the command word it calls home with. `profile list` shows the profiles that
can launch work; disabled ones are left out unless you ask for them.

This is what to read when role work is failing for reasons no model produced — `profile_not_found`,
`profile_disabled` and `host_not_available` are all answered here rather than by asking for the work again.
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

`jason update apply` performs the update: it downloads the release for this platform, proves its digest, stops
the runtime, replaces the executable and starts it again. `jason update rollback` puts back the version the
update replaced. `jason update status` says where an in-flight update stands, or what the last completed one
recorded; it needs no runtime either.

**How to behave about a version you noticed.** When `jason runtime status` reports that a newer version exists,
you may mention it conversationally — one sentence, **at most once** per session — and then get on with what was
asked. **Never interrupt** the task the person actually came with in order to talk about a version.

And **never run `jason update apply` unless the person asked for it**. It replaces the executable that is
running and stops the runtime to do it, so work in flight is drained and everything pointing at Jason is briefly
pointing at a file being swapped. Noticing an update is yours; deciding to take it is theirs.

## Installing it in the first place

Installing Jason is a person's job at a terminal rather than something to perform on their behalf, so what
belongs here is the two lines to hand them and the one fact that decides whether those lines work at all.

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh
```

```powershell
irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1 | iex
```

Each script works out the platform, verifies the download's SHA-256 before unpacking it, installs the executable
under the person's own account — `~/.local/bin` on macOS and Linux, `%LOCALAPPDATA%\Programs\jason` on Windows —
and puts that directory on the `PATH` unless `--no-modify-path` is passed. Nothing needs administrator rights.

**Both lines resolve to the latest release, and no release has been published yet, so they answer 404 until the
first release is published.** Until then the way to get a working Jason is to build it from source, which the
repository's `README.md` explains. Say that, rather than pasting a line you know will fail.

## What this skill does not cover

Campaign work, the roles that perform background AI work, reading the code a failed work item carries, and
anything only a person may decide. Those are `managed-campaign-work`, the role skills, `troubleshooting-jason`
and `approvals-and-questions`.
