# Notifications

When Jason starts waiting for a person, the person finds out without asking. This page says what notifies, when, what
a notification says, where it can appear, what happened to each one, and what it leaves behind on the machine. It is
the contract for the `Notifications` section of the settings, the toast, notification plugins and the
`notification_sent` journal line.

## 1. What notifies, and what does not

Five things, and only these:

- **an approval parked at a claim** — a provider operation that needs a person's yes before it runs
  ([work-execution.md](work-execution.md));
- **a question a role raised** — an AI role that could not answer something itself and asked
  ([campaign-manager.md](campaign-manager.md));
- **a protective action that failed** — work the catalogue runs even in a paused campaign, recording an opt-out,
  pausing a campaign or taking back a send, that ended failed or expired, in a campaign of any status. A paused campaign's manager is not
  summoned ([campaign-manager.md](campaign-manager.md)), so a pointer is how such a failure reaches a person;
- **an account whose events were not read** — a span older than the 30 days a read of an account's events reaches back,
  said once as an `events_gap_observed` line ([events.md](events.md) §5). It concerns no campaign, so no manager is
  woken by it;
- **an open provider block** — the provider refused a call for the account, or for one sending account under it, rather
  than for the work, and the runtime holds the work that block concerns until a trial gets past it
  ([work-execution.md](work-execution.md) §Provider blocks). It is said once, by block, from the first
  `provider_blocked` line it writes; a block cleared before the pointer is sent is not said. A protective action a block
  holds is parked, not failed, so it is said here and never as a protective action that failed. A block is said while it
  is open, whatever it holds, because it is still the person's to fix and the next item parks on it. The work items it
  holds are the work items parked on it, as `block.get`'s `held_count` counts them, so a pointer's count,
  `jason block get` and `jason workitem list --block` agree. Whether a block is the account's or one sending account's,
  and what lifts it, is read at `jason block list`; the pointer says only how many.

Nothing else notifies: not an answer, not an approval given or refused, not a review, not finished work, not other work
that failed, not a block that cleared, and not a protective action somebody cancelled — a cancellation is a decision. A
person who answers a question is not told that they answered it.

A notification is a pointer: how many approvals and questions are waiting, how many protective actions failed, how many
accounts have events Jason did not read, how many provider blocks hold how many work items, and where to look. It
decides nothing, and nothing is approved by reacting to it. An approval is a person's decision, recorded as theirs, made
where they read what they are approving — through their agent, or `jason approval list` and `jason approval approve`.

## 2. When

A burst of things that need a person — thirty parks ten seconds apart while a campaign's enrollments reach their
approval gate — is said **once**, when it has settled. At each dispatcher scan the runtime asks of the lines it has
read since the last notification:

- **(a)** has the newest one been quiet for the quiet window, `Notifications:QuietSeconds`; **or (b)** has the oldest
  one waited the cap, `Notifications:MaxWaitSeconds`, for a burst that never goes quiet;
- **and (c)** has the floor since the last notification that reached anybody passed, `Notifications:MinIntervalMinutes`;
- **and (d)** has a quiet window passed since this runtime started — the start grace, so a sign-in does not open with
  a pointer to everything that parked while nobody was there before the runtime has even looked.

The quiet window in force is the setting, but never less than two of the dispatcher's ticks: a window shorter than
the scan could not be seen to have passed. The floor allows at most 48 a day at the defaults that reach somebody; a
notification that reached nobody — failed, silenced or not attempted on every channel — does not start it, so the next
burst is sent once its own quiet window has passed. A person is usually told far fewer.

Everything here is computed at a dispatcher scan from what the journal records, never from a timer inside the runtime,
which ends at every sign-out. A sign-out in the middle of a quiet window loses nothing: the lines are still in the
journal, and the next runtime reads them again from the last notification it wrote.

Only one notification is being sent at a time. What parks while one is being sent waits for the next.

## 3. What it says

```text
Jason
3 approvals and 1 question are waiting in Jason.
Ask your agent what Jason is waiting for, or run jason approval list and jason decision list.
```

And where a protective action failed:

```text
Jason
1 protective action failed in Jason, in cmp_01K….
Ask your agent what failed, or run jason workitem list --status failed --status expired.
```

And where an account's events were not read:

```text
Jason
1 account has events Jason did not read.
Ask your agent what Jason did not read, or run jason events sources.
```

And where a provider block holds work:

```text
Jason
1 provider block holds 4 work items in Jason.
Ask your agent what is blocked, or run jason block list.
```

The title; how many approvals and questions are waiting **at the moment it is sent** — counted then, so a burst answered
before it was said says nothing at all; how many protective actions failed, how many accounts have events Jason did not
read, and how many provider blocks opened, **since the last notification that reached anybody** — counted from that
one's line, so a notification that reached nobody leaves them for the next; of those blocks, only the ones still open
when it is sent, with the work items they hold then; and where to look. Said beside anything else, a provider block or
an account whose events were not read makes where to look the commands of each thing alone, in the order they are said —
"Ask your agent, or run jason approval list, jason decision list, jason workitem list --status failed --status expired,
jason block list and jason events sources." — so that five things said at once still fit the message the delivery
contract takes; beside a provider block, failed protective work names one campaign and counts the rest. Past three
campaigns, the rest are counted: "… in cmp_…, cmp_…, cmp_… and 2 more." A count past 9999 is said as "10k+", so the
longest pointer fits whatever the counts are; the line keeps the counts themselves. That is every field.

**What never travels:** the campaign's name, a contact's name or address, what an approval is about or its preview, a
question's words or its options, the error a failed action ended with, the operation and its arguments, and any id of a
work item or attempt. A campaign's public id travels only in the sentence about failed protective actions, at most three
of them, to say where to look: an opaque `cmp_…` that names nothing by itself. Of an account whose events were not read,
nothing travels but how many such accounts there are. Of a provider block, nothing travels but how many blocks there are
and how many work items they hold: not its link, its reason, a sending account's identifier, the plugin or a campaign.
The pointer is made from five counts, those ids, and nothing else. A campaign's name is the plainest example of why: it
is often a customer's name or a deal, written by the person for themselves, and a notification is read by whoever can
see the screen, the phone or, on a public ntfy server, the topic.

## 4. Where it can appear

A toast — the operating system's own notification — comes only from a runtime in the person's own interactive
session: the logon task `jason runtime autostart enable` registers, a `jason runtime start` typed in that session, the
LaunchAgent on macOS. On Linux, a systemd user unit with a session bus and a notification daemon; without lingering,
such a unit exists only while the person is signed in.

- **It ends at sign-out**, with the runtime, and the next sign-in sends what is still waiting and was not said yet —
  once, after the start grace. A pointer already sent is not sent again at the next sign-in: there are no reminders.
- **A runtime with no desktop shows nothing**, and says so: a runtime in session 0 — where a 0.1.0 logon task started
  one — and a Linux runtime with no session bus, or with no notification daemon running and none the bus can start on
  demand, record the toast as `not_attempted`, `no_desktop`. A daemon the bus starts on demand — dunst, mako, MATE's —
  owns its name only after its first notification; listed among the names the bus can start, it is told, and started.
  `jason status` names session 0, what runs there — a service, an SSH session, a runtime 0.1.0's logon task started —
  and, for the last, the acts that end it, in order. A Linux runtime with `notify-send` and a session bus but no
  notification daemon and no `gdbus` cannot ask whether a daemon is there, so its toast is recorded `failed`,
  `toast_failed`; `Notifications:Toast` set to `false` turns it off. A probe that cannot answer — it fails, or
  outlives its bound — decides nothing, and the notification is sent; so one Linux toast can take three launches, each
  bounded at five seconds, fifteen in all, none of them on the scan.
- **Each account has its own runtime and its own toasts.** One person's approvals are never shown on another's
  desktop.
- **Do not disturb:** Windows' Do not disturb is invisible to Jason: the notification goes to the notification center
  without a banner, and Jason records it delivered.
- **Switched off:** Switched off in Settings → System → Notifications, the notification is not shown, and Jason
  records it silenced — not delivered and not failed. It is the person's own choice, and never reported as a fault.
- **macOS** shows it with `osascript`, which macOS attributes to the program that showed it. That command is composed
  and checked by tests here; it has not been watched on a Mac.

A notification plugin reaches wherever its service does — a phone following an ntfy topic, say — whatever session the
runtime runs in.

## 5. What happened to each one

Each notification is one `notification_sent` line in the journal, with no campaign, work item or attempt, so it never
wakes a manager:

```json
{
  "notification_id": "ntf_01K…",
  "counts": { "approvals": 3, "decisions": 1, "protective_failures": 0, "event_gaps": 0, "blocks": 0 },
  "covered_through": "jrn_01K…",
  "channels": [
    { "channel": "toast", "kind": "toast", "outcome": "delivered", "attempts": 1 },
    { "channel": "ntfy", "kind": "plugin", "outcome": "failed", "code": "notification_unreachable", "class": "transient", "attempts": 2 }
  ],
  "last_delivered_at": "2026-10-01T09:06:00.000Z",
  "delivered_through": "jrn_01K…"
}
```

`counts.protective_failures` is how many protective actions failed in the window this notification closed: after the
line `delivered_through` names — the line the last notification that reached anybody covered through — up to
`covered_through`. A notification that reached nobody carries `delivered_through` forward unchanged, as it carries
`last_delivered_at`, so the next one counts those failures again with any new ones. Neither key is sent to a channel:
`delivered_through` is a position in the journal, and the count reaches a channel only as words in the message. A
first start walks no history: a protective failure journaled before the first
start that ever wrote a line is not counted by a pointer — the work-item list is where it shows.

`counts.event_gaps` is how many accounts had events Jason did not read in the same window, counted by account however
many gap lines one has, carried forward by a notification that reached nobody the same way, and sent to a channel only
as words. An account whose reads keep failing is not counted while they fail: a failing read skips nothing, so no span went
unread. It shows in `jason events sources`, as how many reads in a row failed and how the last one did, and in the
runtime's log, read with `jason logs`, as a warning naming the source, the code and the class. A failure that outlasts the
30 days leaves a gap, which the first read that succeeds says, and which is counted like any other.

`counts.blocks` is how many provider blocks were first said in the same window — a block whose first `provider_blocked`
line came before it was counted by an earlier pointer — and were still open when this notification was sent, counted by
block however many campaigns were told about one. It is carried forward by a notification that reached nobody the same
way, and sent to a channel only as words, beside the work items those blocks held then, which the line does not keep.

A notification is a pointer. Each channel's outcome is one of `delivered`; `failed`, with its code and class — a
server still unreachable after its one retry is failed, and so is a delivery whose input the runtime's own half of the
delivery contract refuses before the plugin runs, `input_invalid` (`validation`); or `not_attempted`, with its reason,
for a channel that cannot reach anybody: `off`, `no_desktop`, `not_declared`, `unavailable` or `binding_invalid`. The
toast has a fourth, `silenced`, for a notification Windows did not show because the person switched Jason off. No
outcome is ever a failure of the parked item, the approval or the decision, and a delivery never reads, writes or
re-times any of them.

A binding or an input a pattern could not finish measuring is failed `validation_timed_out`, `transient` — nothing was
sent — and an answer it could not measure is failed `validation_timed_out`, `ambiguous`: never `binding_invalid`,
`input_invalid` or `result_invalid`, because a pattern that timed out says nothing about the value it was measuring.

`silenced` covers `disabled_for_application` (Jason switched off), `disabled_for_user` (every app switched off),
`disabled_by_group_policy` and `disabled_by_manifest`.

`attempts` is written for every outcome but `not_attempted` — `delivered`, `silenced` and `failed` each say how many
times the channel was tried.

A transient failure is tried once more, thirty seconds later, and nothing else is ever repeated: a pointer that may
have arrived is not sent twice. Three cases write no line — every channel off (nothing is read at all), nothing waiting
and no protective action failed by the time it is sent, and a runtime that stops while it sends, whose successor reads the same lines and sends once.
One case writes a line though nothing reached anybody: channels exist and none could, which moves the watermark so the
same lines are not said again — except failed protective work, whose window moves only when somebody was reached.

```sh
jason journal list --kind notification_sent
```

## 6. Where you see it

- `jason runtime status --human` says what each channel did last, on its `Notify:` line — including, before anything
  has been sent, a toast with no desktop.
- `jason status` has an optional `notifications` check: off is `absent`; a channel that failed, a named plugin that was
  not attempted, and a runtime in session 0 are `failed`, each saying why; a person's own setting — notifications
  switched off, a machine with no desktop by design — is never `failed`. Being optional, it never makes an
  installation unready.
- The runtime's log (`~/.jason/logs/`, compact JSON, one line per event) has a line for each failed channel. It keeps
  the message template and the values apart, so the line to look for is the one whose `"@mt"` is
  `"Notification channel {Channel} failed ({Code}, {Class})"`; the event's id and the logger's name come after the
  values:

  ```json
  {"@t":"2026-10-01T09:06:00.0000000Z","@mt":"Notification channel {Channel} failed ({Code}, {Class})","@l":"Warning","Channel":"ntfy","Code":"notification_unreachable","Class":"transient","EventId":{"Id":2,"Name":"ChannelFailed"},"SourceContext":"Jason.Runtime.Notifications.NotificationSender"}
  ```

## 7. The toast on Windows

- **A per-user key, nothing elevated.** At every toast the toast host ensures the key
  `HKCU\Software\Classes\AppUserModelId\ai.jason.runtime` with `DisplayName` `Jason` — written the first time, only
  read after that — from an ordinary prompt. That is the whole registration: no shortcut, no icon, no activator. It
  appears as "Jason" in Settings → System → Notifications.
- **Shown by a child of the same executable**, `jason toast-host`, which ends once it has shown the toast and read the
  Setting, and is the only place the WinRT interop runs: a fault in it ends that child, recorded as a failed toast,
  never the runtime. What a walk saw of it is in §10.
- **What Windows adds**: at the first toast, Windows writes two entries of its own for that AppUserModelID, under
  `…\Notifications\Settings\` and `…\PushNotifications\Backup\` in the same account.
- **What `jason uninstall` removes**: all three keys, after the runtime has stopped. **What stays**: the notifications
  already in the notification center, which are the person's to dismiss, and Windows' own database of them.

## 8. ntfy and other plugins

A notification plugin is a channel only when it is named, under `Notifications:Plugins:<id>`, with its `Binding` — or
with `"Enabled": true` for a plugin that needs none, because the settings file drops an empty object. It is granted
like any plugin, under `Plugins:Grants:<id>`, and a credential it needs is read from the runtime's environment — which,
for a runtime started at logon, is the account's logon environment rather than a shell's
([release-and-update.md](release-and-update.md) §8).

The reference plugin is [ntfy](../plugins/ntfy/README.md). Writing one is
[plugins.md](plugins.md) §14; what it is invoked for is the delivery contract
[`notification.send`](contracts/notifications/notification.send.md).

## 9. The settings

In `~/.jason/config/settings.json`, read at every scan, so an edit applies at the next one. A live edit never
stops the runtime: a refused edit keeps the last good settings and is logged by path and type, and a start
with a malformed or mistyped file still fails, saying where — on stderr under `jason runtime run`, and after
"It said:" in what `jason runtime start` answers: the line and byte of a malformed file,
the setting's path and expected type for a mistyped one.

| Setting | Default | What it decides |
|---|---|---|
| `Notifications:Enabled` | `true` | off means nothing is read, sent or written |
| `Notifications:Toast` | `true` | the operating system's own notification |
| `Notifications:QuietSeconds` | `60` | how long the newest line of a burst must be quiet, 1 to 3600 |
| `Notifications:MaxWaitSeconds` | `600` | how long the oldest line of a burst that never settles may wait, from `QuietSeconds` to 86400 |
| `Notifications:MinIntervalMinutes` | `30` | the least time between two notifications that reached anybody, 0 to 1440; 0 is no floor |
| `Notifications:Plugins:<id>` | none | a notification plugin named as a channel: `Enabled` (`true` unless written `false`) and its `Binding` |

## 10. Checked by hand so far

> **Checked by hand so far:** Windows, on 2026-10-01, on one machine, and Linux, on the same day, under WSL.
>
> **Windows, on 2026-10-01, on a standard account**, on Windows 11 24H2 (build 26100), with a development build of
> 0.2.0 (`0.2.0-dev`), the change as it stood when walked, installed from its own archive by `install.ps1` over 0.1.0,
> which came from 0.1.0's own release assets: installing 0.1.0 over the network was not measured while the repository
> is private. The runtime the next sign-in started, in the
> account's own session with no console, sent **one toast and one ntfy message per burst**, each carrying the counts
> and nothing else: five approvals parked ten seconds apart made one pointer, "5 approvals are waiting in Jason.", on
> the screen and on the topic. The two children it started for that pointer were each gone within a second of being
> seen, and no window came from Jason's process tree while a watcher that saw its own control window was looking.
> **A raised question was notified**; an approval approved and a question answered by hand were not, and nothing
> more was sent across three quiet windows. **A sign-out inside the quiet window sent nothing**, and the runtime the
> next sign-in started sent one pointer covering both parks once its start grace had passed. A server host the ntfy
> package was not granted was recorded `failed`, `http_host_not_allowed`, `permanent`, and `jason status` named it.
> **Switched off in Settings**, the toast was recorded `silenced` (`disabled_for_application`) and no banner
> appeared; the check stayed ok, with the Settings sentence, because ntfy still reached the person. Switched on
> again, the next pointer was shown. `jason uninstall` left none of the three keys; the notification center still
> listed Jason's notifications, which are the person's to dismiss.
>
> **Linux**, under WSL (Ubuntu 26.04, kernel 6.6), with a session bus and `gdbus` but no notification daemon and no
> `notify-send`: the runtime said at start that it had a desktop; a parked approval's toast was recorded
> `not_attempted`, `no_desktop`, "no notification daemon", and `jason status` said there was no desktop to show a
> notification on.
>
> **Not checked:** macOS, which is composed and asserted only; Windows before 24H2; a runtime in session 0, and the
> remedy for one that 0.1.0's logon task started; a toast actually shown on Linux, where the walk's machine had no
> notification daemon; and Do not disturb.
