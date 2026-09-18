# Work execution

How Jason turns intent into work that actually runs: what a work item is, how the dispatcher decides
what to start, what an executor is handed when it is launched, and how it reports back.

This document is the contract for anyone writing an agent host against Jason. The launch envelope and
the executor operations are public surface; the rest explains the rules that surround them.

Provider operations run: an item that names a canonical operation is held to that operation's contract
when it is written, routed to a plugin at the claim, performed in a separate process and answered.
Where an item is routed, and every reason a claim can refuse one, is
[docs/routing.md](routing.md).

## What a work item is

A **work item** is one unit of work inside a campaign. The campaign is mandatory: work always belongs
to a campaign, and a campaign's chronicle carries everything its work did. A contact is optional — a
lot of work is about the campaign rather than about one person — and when it is given, that person
must be a member of the campaign and not excluded.

There are two kinds:

- **`ai_role`** — work a role does. It names a `role` (`researcher`, `copywriter`, …), carries a
  `context` object that is the brief, and may declare a `result_format` — a schema, in the dialect
  `docs/contracts/` publishes — describing what the answer
  should look like. The runtime never reads the meaning of either; it hands them to the role's entry
  command and records what comes back.
- **`provider_op`** — work a provider performs. It names an `operation` — one of the canonical
  operations this build publishes a contract for (`campaign.get`, `list_membership.add`,
  `campaign.enroll`) — and carries that operation's arguments under `input`, the one reserved key of
  the context. Which plugin performs it, and in which provider account, is a route
  ([docs/routing.md](routing.md)); the runtime composes what the plugin is given and never hands over
  the rest of the context.

Everything else on an item is scheduling, not meaning: `priority`, `not_before`, `due_at`, and the
three per-item overrides `timeout_seconds`, `heartbeat_seconds` and `max_attempts`.

An **attempt** is one try at a work item. It records the context exactly as it stood when the work was
claimed, how it was launched, when it started, when it was last heard from, and how it ended. The
attempt's public id (`att_…`) is the fencing token an executor must present to report anything.

### What a provider operation is held to when it is written

The canonical operations are published as one machine-readable document each, under
[docs/contracts/](contracts/README.md), and the runtime enforces the very documents published there.
A `provider_op` item is measured against its operation's contract when it is created, and again
whenever a patch rewrites its arguments — so a wrong argument is a refused request rather than an
attempt that failed on it much later, with a provider already called. `workitem.create` and
`workitem.update` answer `validation_failed` with one detail per problem:

| Field | Code | When |
|---|---|---|
| `operation` | `unknown` | the name is well formed, but this build publishes no contract for it. The message names the operations it does publish |
| `context.input` | `required` | the operation declares required arguments and the item carries none. An absent key and a JSON `null` read alike — neither is an argument |
| `context.input<pointer>` | `invalid` | the arguments do not satisfy the `args` sub-schema of the operation's input, one detail per failure with the JSON pointer of the offending place (`context.input/channel`), all of them reported at once |

What is measured here is the caller's own arguments — the `args` property of the operation's input
schema — and not the whole composed input. The rest of that input is the runtime's to write, at
claim: the contact, the campaign and the idempotency key. So a rule the operation states across the
whole of it, such as a root `anyOf`, is not a creation-time check: at the claim the composed document
is measured against the operation's whole input schema, and that is where such a rule is enforced —
as `input_invalid` on the attempt rather than as a refused request.

`workitem.update` re-reads the arguments only when the patch names the reserved key — `set` writing
`input`, or `unset` naming it — and measures them against the item's **own** operation, which is not
patchable. An `ai_role` item is untouched by all of this: its context is a brief, and nothing in the
runtime reads the meaning of a brief.

## Lifecycle

```text
created ──► scheduled ──► processing ──► succeeded
              │  │            │   └────► failed        (the executor reported a failure, or gave up)
              │  │            │
              │  └────────────┴───────► created        (the attempt was lost or interrupted)
              │
              └──────────────────────► failed          (nothing can run this: no_route, role_not_launchable)

created ──► awaiting_approval ──► created              (a person approved it)
                   └───────────► failed                (a person rejected it)

created ──► expired ──► created                        (by moving due_at into the future)

created | awaiting_approval | scheduled | processing | expired ──► cancelled
created | awaiting_approval ──► expired                (the due date passed before anyone answered)
```

- **`created`** — waiting. The only state from which work is claimed.
- **`awaiting_approval`** — the operation this item names needs a person's approval, and the claim
  parked it for one rather than performing it. No attempt was made: an attempt is a run, and nothing
  has run. It leaves this state when a person answers, when the work is cancelled, or when its due
  date passes. `docs/routing.md` §7 is the check that puts it here; `approval.list` is where it waits.
- **`scheduled`** — claimed, with an attempt row and a lease, on its way to a handler. It lasts
  milliseconds.
- **`processing`** — a handler is running the attempt.
- **`succeeded`**, **`failed`**, **`cancelled`** — finished for good. `finished_at` is set and the
  result is frozen, in the service and by a database trigger.
- **`expired`** — the due date passed before anyone claimed it, or before anyone answered the
  decision it was waiting for. Not final: moving `due_at` into the future through `workitem.update`
  returns it to `created` — where, if its operation still needs a person's approval, the next claim
  parks it again, because the decision it needed was never given.

Nothing else in the runtime decides whether a move is legal; one transition table does, and every
state change writes one journal entry.

## Eligibility

An item is **eligible** — the dispatcher would claim it right now — when all five of these hold:

1. its status is `created` — work `awaiting_approval` is not eligible, and no number of scans makes
   it so;
2. its campaign is `active` (a draft campaign is a backlog, a paused one simply stops);
3. `not_before` is unset or already past;
4. `due_at` is unset or still ahead;
5. `retry_after` is unset or already past.

`eligible` is never stored. It is computed from one shared expression that the item list, the claim
query and the DTO all use, so `workitem.list --eligible` tells you exactly what the next scan would
consider.

## The claim rule

Each tick, the dispatcher runs one scan in three short steps: **expire**, **enforce**, **claim**.

Claiming happens inside an immediate transaction, so two scans — or two runtimes — cannot both take
the same item; the second waits, finds it taken and moves on. Within one scan the dispatcher takes:

- only eligible items;
- **at most one item per campaign**, and none at all from a campaign that already has an item
  `scheduled` or `processing` — a campaign runs one thing at a time, so a busy campaign cannot starve
  the others;
- the most important first: `priority` descending, then oldest first;
- never more than the free handler slots, so a queued item cannot sit watching its own lease expire.

Work the runtime cannot perform is failed inside the same transaction rather than skipped: a
`provider_op` fails with whichever of the twelve pre-flight reasons applies — `no_route` when nothing
routes it at all — and an `ai_role` whose role has no entry command fails with `role_not_launchable`.
A silent skip would leave the item looking claimable forever.

## Approvals

An operation's contract says what approval it needs. `campaign.get` and `list_membership.add` publish
`auto`, which is the one approval a runtime may give itself. `campaign.enroll` publishes
`confirm_once`, and work naming it is **parked** at the claim rather than run: the item moves to
`awaiting_approval` and an `apr_` row records what is being approved.

**What the gate reads is the published value, and never the condition beside it.** A conditional
property always states the dangerous reading in `value` — `campaign.enroll` is `confirm_once` with a
condition that only decides whether a preview is *mandatory* — and Jason builds a preview every time,
so no condition is ever evaluated and none can soften the gate.

**What is approved is a subject, not an item.** The row holds the operation and its contract version,
the work item, the composed input the plugin would have received, the plugin that would perform it and
the account's binding identity, together with the canonical `sha256:` of that document. Before a later
claim runs the work it computes the subject again and compares the hash: an input edited after the
decision no longer matches, so the item is parked again with the reason `input_changed` rather than
run under a decision nobody made about it. The plugin's version and the package's digest are
deliberately not part of the subject — they are provenance, recorded on the attempt that runs, and
including them would supersede every pending decision the moment an operator reloaded a package.

**Beside the subject is the preview**, assembled at the claim from the operation's own sentence about
itself, the dangerous readings of what it reaches, undoes and costs, the campaign, the person it would
reach and where, and the account. It is stored rather than re-assembled, so what a person reads is
what the claim saw.

**A park makes no attempt.** An attempt is a run; `max_attempts` counts attempts; an item parked three
times that had spent three of its attempts would have been given up on for being patient. Everything
else the pre-flight decides is a refusal and keeps exactly what it always had: an attempt, its
provenance, the coded failure, and one against the budget.

**Only a person decides.** `approval.approve` and `approval.reject` require an actor of type `human`
with a name; a role or an attempt is refused with `approval_not_human`, and an absent actor with
`actor_required`. What the runtime cannot do is tell a person from a process holding that person's own
command line — that is the operator's trust to give. What it guarantees is that nothing inside it can
approve anything, and that every decision names who made it.

- **approve** releases the item to the next scan with its due date, priority and place untouched.
- **reject** ends the item as `failed`, with `approval_rejected` and the person's reason as its last
  error, and no attempt invented to carry it.
- **cancelling the work, or its due date passing**, ends the decision with it: a live approval about
  work that can never run is the one row that would make `approval.list` untrue.
- **a second decision on the same approval** writes nothing and answers `approval_not_pending`: both
  rows are moved in one transaction, each guarded by the status it was read at.

Journal: `workitem_awaiting_approval`, `approval_requested`, `approval_approved`, `approval_rejected`,
`approval_superseded`, `approval_cancelled`. Those lines carry identifiers only — the approval, the
work item, the campaign, the operation, the subject hash, the actor and the reason. The subject and the
preview live on the row and in what `approval.get` answers, which is where somebody entitled to read
them reads them.

## Leases and heartbeats

The lease is the whole budget of one attempt: `lock_until = claimed_at + timeout_seconds`. It is never
extended — a heartbeat proves liveness, it does not buy time. When the lease runs out the attempt is
lost with `lease_expired`.

The heartbeat is the fast detector. While an item is `processing` and `heartbeat_seconds` is not 0, an
attempt is lost with `heartbeat_missed` when nothing has been heard for **twice** the interval: one
interval for the beat to arrive, one of grace. The clock runs from whichever came last — the attempt
starting, its last heartbeat, or this runtime starting. That last term is the post-restart grace: a
runtime that has just come up cannot hold an executor to beats it was never there to hear.

`heartbeat_seconds: 0` turns the check off for an item; the lease alone then decides.

The per-item overrides and the kind defaults are read every time they are needed, so changing an item's
`heartbeat_seconds` or `max_attempts` while an attempt is running takes effect at the next scan; the lease
is the exception — `lock_until` stays where the claim put it until the attempt ends.

When an attempt is lost and the child process is still this runtime's, it is stopped: a zombie's report
would be refused by the fencing anyway, and letting it run on only burns tokens.

## Attempts, retries and the inbox

A lost or rejected attempt fails with a code. The runtime — not the executor — decides whether that
code is worth another try:

| Retriable | Not retriable |
|---|---|
| `lease_expired`, `heartbeat_missed`, `executor_exited`, and an executor-reported `rate_limited`, `provider_unavailable`, `timeout` or `transient` | `role_not_launchable`, `no_route`, `executor_launch_failed`, and every other reported code |

A retriable failure below `max_attempts` returns the item to `created` with
`retry_after = now + Dispatcher:RetryDelaySeconds × attempt_count`, and the next scan that finds it
eligible hands it out again. At the limit, or on a non-retriable failure, the item becomes `failed`
with the last error visible on the item.

That table is the rule for **agent** work, where the failure happened inside this machine. A
`provider_op` attempt is answered differently, because its work happened at somebody else's system:
the failure carries one of four **classes** — `transient`, `permanent`, `validation`, `ambiguous` —
and the class plus the operation's own `repeat_after_ambiguous` rule decide, not the code table. Every
way such an attempt can end with nobody answering for it — the invoker's timeout, `lease_expired`,
`heartbeat_missed`, a command that could not be started (`executor_launch_failed`) and one that was
stopped (`executor_exited`) — is `ambiguous`, because a missing answer says nothing about whether the
provider acted. An attempt a restart finds still `scheduled` is not one of them: it never started, so nobody
was asked anything. A shape error in an answer that did arrive
(`result_invalid`) is `ambiguous` and never repeated: the next attempt would run the same code over
the same answer. A cancellation stays a cancellation in both kinds.

`attempt_count` counts **failed** attempts — the ones that count toward the limit. Work that succeeds
first time shows `attempt_count: 0` with one entry in `attempts`. An attempt that was `interrupted` by
a restart, and any attempt `cancelled` by a caller, never count.

There is no separate inbox entity: what needs a human or a manager role is
`jason workitem list --status failed --status expired`.

## Launching an executor

An `ai_role` item is run by starting an **agent host** as a child process. Which host, and with which
arguments, comes from the item's **execution profile** — resolved at the claim from the item, its
campaign, its role, what it inherited and the configured default, in that order.
[docs/execution-profiles.md](execution-profiles.md) is the contract for all of it.

Where no level names a profile, the role's **entry command** runs the work as it always has: the
role's own `entry_command` if it has one, otherwise `Roles:DefaultEntryCommand` from settings,
otherwise the attempt fails with `role_not_launchable`. The attempt records which of the two
happened, so an absent profile name is a fact rather than an inference.

### The agent pre-flight, in the order it is checked

Every one of these is decided **before a child process exists**, and the first that fails is the one
the attempt records. The attempt is kept either way, with the context it was claimed with and the
provenance as far as the decision got. None of them is retried: nothing about the work changes
between two scans, so an item put back would be refused for the same reason for as long as the queue
existed. It is the same rule the twelve provider checks follow.

| # | Code | What it means, and what to change |
|---|---|---|
| 1 | `lineage_resolution_unsupported` | a run created this work and no profile can be inherited from it. Name one on the item, its campaign or its role |
| 2 | `profile_not_found` | the profile a level named does not exist. The message says which level named it |
| 3 | `profile_disabled` | it exists and is out of service. Enable it, or name another |
| 4 | `host_not_available` | the profile's program is not on this machine. Install it, or point the profile somewhere else |
| 5 | `role_skill_invalid` | the role's skill could not be given to it — misnamed, or past `Roles:MaxSkillBytes` |
| 6 | `role_not_launchable` | no profile anywhere, and the role has no entry command either |

The child is started in a per-attempt **work directory**, `~/.jason/work/<wi_…>/<att_…>/`, which the
launcher creates. Its standard output and standard error are written there as `stdout.log` and
`stderr.log`. Nothing is cleaned up in this version.

Both files stop growing past `Roles:MaxStdoutBytes` and end with the line

```text
[jason] The transcript passed 1048576 bytes and is cut here; Roles:MaxStdoutBytes says how much is kept. The child kept running, and no outcome depends on this file.
```

after which the attempt's launch record says `stdout_truncated: true`. The pipes are drained to end
of file regardless — a pipe nobody empties blocks the child writing into it. A cut transcript never
changes an outcome: the runtime reads no result from standard output, only from `workitem.set_result`
and `workitem.complete`.

The launcher puts two things in that directory before the child starts:

- `.claude/settings.json`, carrying the **deny** rules of the execution profile that runs the attempt
  and nothing else. It never carries an allow list: a directory the runtime created is not a workspace
  the host trusts, and an allow entry there is ignored without being reported, so what the agent *may*
  do travels on the command line instead.
- `.claude/skills/<role>/`, a copy of `~/.jason/skills/roles/<role>/` when the role has one. Files
  only; a link is neither copied nor followed. A skill that cannot be given to the role fails the
  attempt with `role_skill_invalid` before the child starts — whether because its `SKILL.md` names
  something other than the role, which a host answers by ignoring the skill without saying so, or
  because it is past `Roles:MaxSkillBytes`. Either way the role would do the job untaught, at the
  price of a real launch, with only a log line to show for it. A role with no skill directory
  launches normally; nothing was configured, so nothing is missing, and its brief travels in the
  envelope either way.

Its environment is the runtime's own plus four values and not one more:

- `JASON_DATA_DIR`, pointing at the data directory;
- `JASON_ATTEMPT_ID` and `JASON_WORK_ITEM_ID`, both non-secret values the envelope already carries, so
  that an agent reporting through the CLI is its attempt without having to say so;
- the directory this build of Jason runs from, at the front of `PATH`, so the bare command word in
  `runtime.cli_command` reaches this runtime rather than whatever else answers for that name.

**The capability token is never on the command line, never in the environment, and never stored**:
every occurrence of it is replaced with `[redacted]` before anything the child wrote is put in a file
or in an attempt's error trace.

### The launch envelope

One JSON object is written to the child's standard input, which is then closed:

```json
{
  "envelope_version": 3,
  "attempt_id": "att_…",
  "attempt_number": 1,
  "work_item_id": "wi_…",
  "campaign_id": "cmp_…",
  "contact_id": "cnt_…",
  "kind": "ai_role",
  "role": "researcher",
  "execution_profile": null,
  "context": { "…": "the brief, exactly as it stood when the work was claimed" },
  "result_format": { "type": "object", "required": ["findings"], "…": "a schema, or null" },
  "timeout_seconds": 3600,
  "heartbeat_seconds": 120,
  "lock_until": "2026-09-14T13:00:00.000Z",
  "work_dir": "…/.jason/work/wi_…/att_…",
  "runtime": { "descriptor_file": "…/.jason/run/runtime.json", "api_version": "v1", "cli_command": "jason" },
  "role_memory": { "campaign_id": "cmp_…", "role": "researcher" }
}
```

`context` is a snapshot: an edit made while the attempt runs belongs to the next attempt, not this one.

`runtime.cli_command` is the bare word that reaches this runtime, so an agent never has to guess it.

`role_memory` is the address of the role's own note for this campaign, and never the note. A document
copied in here would be what the role believed when the attempt was launched, arriving beside the
brief as though it were current; read through the API at the moment it is wanted, it is plainly a
document with an age. It is null for work that is not a role — which is true by
construction rather than by a decision taken at launch: the only command that writes an envelope is the
one that runs `ai_role` work. What a note is worth — and why a
launched role's memory lives in the runtime's store at all — is in the role-notes section of
[execution-profiles.md](execution-profiles.md).

Every change to this envelope is additive — `cli_command` took it to version 2 and `role_memory` to
version 3 — so a host written against an earlier version keeps working: it reads the fields it knows
and ignores the rest.

### Finding the API

The envelope does not carry the base URL or the token. The child reads them from the **descriptor
file** it names — and it must read that file **before every call**, not once at startup. The token is
per runtime instance: a child that outlives a restart finds the new instance and the new token by
re-reading, and finishes the work it was launched for. A child that reports through the `jason` CLI
gets this for free, because that is what the CLI does.

## Reporting back

Three operations, all fenced by the attempt id. An attempt id that is unknown, belongs to another item,
or is no longer the item's running attempt is answered with `409 stale_attempt`, `retryable: false` —
one code, because the remedy is always the same: stop working on it. All three record that the executor
is alive.

| Operation | Request | Answers |
|---|---|---|
| `workitem.heartbeat` | `work_item_id`, `attempt_id` | `{work_item_id, attempt_id, lock_until, heartbeat_due_by}` — `heartbeat_due_by` is null when the item has no heartbeat |
| `workitem.set_result` | `work_item_id`, `attempt_id`, `result` (any JSON) | the work item |
| `workitem.complete` | `work_item_id`, `attempt_id`, `status: succeeded\|failed`, `result?`, `error?: {code, message, details?}` | the work item |

**An answer is held to the shape that was asked for.** Where the item declares a `result_format`, a
completion whose result does not satisfy it is refused with `result_invalid` (400, not retryable) and
the item stays `processing`, so the executor still holds its attempt and can answer again; one that
gives up instead ends as `executor_exited`. What is judged is the result that will stand — the one
the completion carries, or the one an earlier `set_result` left — so an executor cannot check in with
a paragraph and then finish empty-handed. A **failed** completion is never judged against the shape:
a failure has an error to report and no result to measure.

`result_format` was an opaque value in earlier versions and is now a schema, validated when the item
is written. A shape nothing can check is a request an executor may answer with persuasion.

`set_result` replaces the whole result and works only while the item is `processing`, so an executor
that is killed halfway leaves behind what it had rather than nothing. It is deliberately **not** held
to the `result_format`: half an answer cannot be expected to have the shape of a whole one, and a
rule that refused checkpoints would only stop executors from making them. `complete {status: succeeded}`
may carry a final result, which replaces it again. `complete {status: failed}` requires an `error`,
whose `code` goes through the retriable rule set above. A result larger than 1 MiB is refused with
`result_too_large`.

Reporting through the API is the only way an executor changes state. A child that exits without ever
calling `complete` loses its attempt with `executor_exited` — the exit code and the tail of its standard
error, about 4 KiB of it, are kept on the attempt as the trace, with the token taken out.

A process that reported its result and then stayed around is stopped once `Dispatcher:ExitGraceSeconds`
have passed since its attempt finished: the attempt is over, so nothing it does now can be recorded.

**A settings file that does not validate is not the executor's problem.** `settings.json` can be edited
while the runtime runs, and an edit the validator refuses costs the edit and not the runtime: the last
settings that did validate stay in force, and every part of the runtime that reads them — the scan, the
lease sweep, the claim, the handler, and these three operations — reads them the same way, so a child
goes on heartbeating and reporting while a person repairs the file. Only a runtime that has never read
settings the validator accepted has nothing to work from; `workitem.heartbeat` and a **failed**
`workitem.complete` then answer `503 settings_unreadable`, `retryable: true`, and the whole remedy is
to repair the file and call again. A successful `workitem.complete` and `workitem.cancel` read no
settings at all and are never refused for this reason.

### Error codes

| Code | Where | Meaning |
|---|---|---|
| `work_item_not_found` | 404 | no item with that id |
| `workitem_terminal` | 409 | the item is finished; finished items are not changed |
| `stale_attempt` | 409 | the attempt id is not the item's current running attempt |
| `contact_not_member` | 409 | the contact is not an active member of the campaign |
| `campaign_archived` | 409 | an archived campaign takes no new work |
| `result_too_large` | 400 | the result exceeds 1 MiB |
| `result_invalid` | 400 | the result does not satisfy the `result_format` the item declared |
| `role_exists` | 409 | a role of that name already exists |
| `settings_unreadable` | 503 | the settings are invalid and none have validated since the runtime started; repair the file and call again |
| `lease_expired` | attempt | the whole budget of the attempt was spent |
| `heartbeat_missed` | attempt | nothing was heard for twice the heartbeat interval |
| `executor_exited` | attempt | the child ended without reporting |
| `executor_launch_failed` | attempt | the entry command could not be started at all |
| `role_not_launchable` | attempt | the role has no entry command and no default is configured |
| the twelve pre-flight codes | attempt | a `provider_op` item the claim refused, `no_route` among them — see below |

## Provider operations

A `provider_op` item is a kind of work item, not a second execution engine: it is claimed under the
same lease, runs in the same handler pool, and ends through the same routine. Four things about it
differ from agent work.

**Its arguments live under one reserved context key.** `context.input` carries the operation's
arguments and nothing else in the context is an argument. The runtime **composes** what the plugin
receives from the item — the arguments, the contact cut to the projection the operation declares, the
campaign, the identifiers that plugin itself pinned, and the work item's own id as the idempotency key
— and validates the whole composed document against the operation's input schema at the claim. A
plugin never sees a work item's context.

**Everything that can stop it is decided at the claim, before a child process exists.** Twelve checks,
in this order, and the first that answers is the one that decides:

```text
operation_unknown → no_route → plugin_not_loaded → plugin_unavailable → plugin_operation_unsupported →
contract_incompatible → binding_invalid → contact_required → no_channel_value → suppressed →
input_invalid → approval_required
```

The first eleven are refusals and all of them are final: ten are `permanent` and `input_invalid` is
`validation`, because it is the one a planner can fix by editing the item. The attempt is kept with its
context snapshot either way — a silent skip would leave the item looking claimable forever — and
nothing ever falls back to another plugin. The twelfth is not a refusal: `approval_required` parks the
item for a person (§Approvals above), and it is asked last so that nobody is asked to approve work that
would have been refused anyway. [docs/routing.md](routing.md) says what each one means and what to
change.

**A failure's class decides the retry, not the code table.** A provider attempt ends with one of four
classes: `transient` comes back, `permanent` and `validation` are final, and `ambiguous` — the
provider may already have acted — is repeated only where the operation's own contract says a repeat is
`safe` or is answerable `after_recovery_read`. An answer that arrived and does not satisfy the
operation's output schema is `result_invalid`: ambiguous, and never repeated.

**What ran is pinned to the attempt.** Every provider attempt carries a `provenance` record, written at
the claim from the decision that was just made and completed when the invocation ends: the plugin, its
version and content digest, the operation and contract version, the plugin and route snapshot ids, the
route scope, the binding's identity (a hash, never the value), the invocation id, what the run cost,
the identifiers the plugin returned, and — after a `result_invalid` — the answer that was refused.
Nothing rewrites it afterwards. `jason workitem get <id> --human` prints it as a block per attempt.

## Roles

The roles registry says which roles exist and, when a role is launchable, what command starts an agent
host for it. Nine roles ship with the runtime: `manager`, `planner`, `researcher`, `copywriter`,
`personalizer`, `critic`, `responder`, `analyst`, `deliverability-specialist`. They are seeded with no
entry command, so one line of settings makes the whole roster launchable through a single generic host:

```json
{ "Roles": { "DefaultEntryCommand": ["my-agent-host", "--serve"] } }
```

`jason role add <name> --entry-command …` adds a role of your own with a command of its own, which
wins over the default. A role is identified by its name (`^[a-z][a-z0-9-]{0,63}$`); items and actors
reference roles by name. There is no update or remove operation in this version.

## Settings

All of these live under `~/.jason/config/settings.json` (or as `JASON_Dispatcher__TickSeconds`-style
environment variables), and every value is validated when the runtime starts — a setting out of range
is a refusal to start, not a silently ignored line. Every dispatcher and roles setting is then re-read
on each tick, so an edit applies without a restart. The exceptions are the two that are decided once:
**`Dispatcher:MaxParallel`**, which sizes the handler pool, and **`Dispatcher:Enabled`**, which decides
whether this runtime has a dispatch loop at all.

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Dispatcher:Enabled` | `true` | — | whether this runtime dispatches at all |
| `Dispatcher:TickSeconds` | `10` | 1..3600 | how often a scan runs |
| `Dispatcher:MaxParallel` | `4` | 1..64 | how many attempts run at once (applied at start) |
| `Dispatcher:DrainSeconds` | `10` | 0..20 | how long a shutdown waits for running handlers |
| `Dispatcher:RetryDelaySeconds` | `60` | 0..86400 | multiplied by the failure count to set `retry_after`; 0 retries at the next scan |
| `Dispatcher:ExitGraceSeconds` | `30` | 0..600 | how long a process may linger after its attempt was completed through the API before the dispatcher stops it |
| `Dispatcher:AiRole:TimeoutSeconds` | `3600` | 30..86400 | the budget of one `ai_role` attempt |
| `Dispatcher:AiRole:HeartbeatSeconds` | `120` | 0, or 10..3600 | how often an `ai_role` executor must prove it is alive |
| `Dispatcher:AiRole:MaxAttempts` | `3` | 1..10 | how many failures an `ai_role` item is worth |
| `Dispatcher:ProviderOp:TimeoutSeconds` | `600` | 30..86400 | the budget of one `provider_op` attempt; never below the slowest published operation's `timeout_ms` plus `Plugins:Invoker:KillGraceMs` |
| `Dispatcher:ProviderOp:HeartbeatSeconds` | `0` | 0, or 10..3600 | 0: a provider call is short, the lease suffices |
| `Dispatcher:ProviderOp:MaxAttempts` | `3` | 1..10 | how many failures a `provider_op` item is worth |
| `Roles:DefaultEntryCommand` | `[]` | — | the command used for any role without one of its own |

An item may override the three per-kind numbers for itself: `timeout_seconds` (30..86400),
`heartbeat_seconds` (0, or 10..3600) and `max_attempts` (1..10). A `provider_op` item has one rule
more: its `timeout_seconds` may not be shorter than the operation's own `timeout_ms` plus
`Plugins:Invoker:KillGraceMs`, rounded up to whole seconds. A child is given the operation's budget
and never what is left of the lease, so a shorter lease could only end with the lease gone and the
provider's answer unknown — **capped by the plugin's own ceiling where that is smaller**, which is
`limits.timeout_ms` from its manifest or `Plugins:Limits:TimeoutMs` when it declares none, so a plugin
that implements a slow operation must declare a ceiling at least as large as that operation's contract
(`docs/routing.md` §9 has the whole rule). `workitem.create` and `workitem.update` refuse such an item
rather than raising the number quietly: a lease silently changed is one the planner still believes.

`jason runtime status --human` shows what the loop is doing:

```text
Dispatcher: running · tick 10 s · 2/4 attempts · last scan 2026-09-14 12:00:03 UTC
```

## Stopping and restarting

A shutdown stops claiming immediately, then waits up to `Dispatcher:DrainSeconds` for handlers to see
their children finish. Children still running are **not** killed: their leases are still good, and a
survivor completes against the next runtime by re-reading the descriptor.

On start the runtime releases what was only `scheduled` when it died, whatever kind of work it is. The
handler commits the item `processing` and the attempt started before it launches anything, so an attempt
still `scheduled` ran nothing and asked nobody — not even a provider: it is marked `interrupted`, which
does not count against the limit, and the item goes back to `created`. Items that were `processing` are
left alone: their executor may still be alive, and the lease and heartbeat rules decide soon enough —
and for a `provider_op` those rules are the ambiguous ones, so an operation that may never be repeated
waits for a person rather than being handed out again.

## Not here yet

- **Approvals beyond one decision about one item.** There are no standing approvals, no bulk
  decisions, no expiry windows and no anomaly rules; nothing notifies anybody, so a person finds out
  what is waiting by asking (`jason approval list`). What exists is the gate itself, below.
- **Session resume.** A host that is interrupted is retried from the start, not nudged to continue.
  The session id is minted per attempt and recorded, which is what a nudge would need, but nothing
  uses it yet.
- **Lineage from work the runtime creates itself.** Lineage is read from the actor that created a work
  item, and only an attempt carries a profile to hand down. Dispatcher-created and event-triggered
  work — neither of which exists yet — must name the attempt that caused it explicitly when it
  arrives, or the chain it belongs to is erased at that step.
- **Anything above one hop.** Lineage is materialized once, from the run that created the item. There
  is no resolver walking a graph, and no branch selection.
- **Backoff.** The retry delay is linear in the failure count; the tick and the one-item-per-campaign
  rule do the rest of the pacing.
- **Work-directory retention.** Nothing is ever cleaned up.
