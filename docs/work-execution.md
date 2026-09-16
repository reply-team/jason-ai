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
  `context` object that is the brief, and may declare a `result_format` describing what the answer
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

created ──► expired ──► created                        (by moving due_at into the future)

created | scheduled | processing | expired ──► cancelled
```

- **`created`** — waiting. The only state from which work is claimed.
- **`scheduled`** — claimed, with an attempt row and a lease, on its way to a handler. It lasts
  milliseconds.
- **`processing`** — a handler is running the attempt.
- **`succeeded`**, **`failed`**, **`cancelled`** — finished for good. `finished_at` is set and the
  result is frozen, in the service and by a database trigger.
- **`expired`** — the due date passed before anyone claimed it. Not final: moving `due_at` into the
  future through `workitem.update` returns it to `created`.

Nothing else in the runtime decides whether a move is legal; one transition table does, and every
state change writes one journal entry.

## Eligibility

An item is **eligible** — the dispatcher would claim it right now — when all five of these hold:

1. its status is `created`;
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

An `ai_role` item is run by starting the role's **entry command** as a child process. The command is
resolved as: the role's own `entry_command` if it has one, otherwise `Roles:DefaultEntryCommand` from
settings, otherwise the attempt fails with `role_not_launchable`.

The child is started in a per-attempt **work directory**, `~/.jason/work/<wi_…>/<att_…>/`, which the
launcher creates. Its standard output and standard error are written there as `stdout.log` and
`stderr.log`. Nothing is cleaned up in this version.

Its environment is the runtime's own plus `JASON_DATA_DIR`, pointing at the data directory. Nothing is
appended to the command's arguments. **The capability token is never on the command line, never in the
environment, and never stored**: every occurrence of it is replaced with `[redacted]` before anything
the child wrote is put in a file or in an attempt's error trace.

### The launch envelope

One JSON object is written to the child's standard input, which is then closed:

```json
{
  "envelope_version": 1,
  "attempt_id": "att_…",
  "attempt_number": 1,
  "work_item_id": "wi_…",
  "campaign_id": "cmp_…",
  "contact_id": "cnt_…",
  "kind": "ai_role",
  "role": "researcher",
  "execution_profile": null,
  "context": { "…": "the brief, exactly as it stood when the work was claimed" },
  "result_format": { "…": "the shape asked for, or null" },
  "timeout_seconds": 3600,
  "heartbeat_seconds": 120,
  "lock_until": "2026-09-14T13:00:00.000Z",
  "work_dir": "…/.jason/work/wi_…/att_…",
  "runtime": { "descriptor_file": "…/.jason/run/runtime.json", "api_version": "v1" }
}
```

`context` is a snapshot: an edit made while the attempt runs belongs to the next attempt, not this one.

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

`set_result` replaces the whole result and works only while the item is `processing`, so an executor
that is killed halfway leaves behind what it had rather than nothing. `complete {status: succeeded}`
may carry a final result, which replaces it again. `complete {status: failed}` requires an `error`,
whose `code` goes through the retriable rule set above. A result larger than 1 MiB is refused with
`result_too_large`.

Reporting through the API is the only way an executor changes state. A child that exits without ever
calling `complete` loses its attempt with `executor_exited` — the exit code and the tail of its standard
error, about 4 KiB of it, are kept on the attempt as the trace, with the token taken out.

A process that reported its result and then stayed around is stopped once `Dispatcher:ExitGraceSeconds`
have passed since its attempt finished: the attempt is over, so nothing it does now can be recorded.

### Error codes

| Code | Where | Meaning |
|---|---|---|
| `work_item_not_found` | 404 | no item with that id |
| `workitem_terminal` | 409 | the item is finished; finished items are not changed |
| `stale_attempt` | 409 | the attempt id is not the item's current running attempt |
| `contact_not_member` | 409 | the contact is not an active member of the campaign |
| `campaign_archived` | 409 | an archived campaign takes no new work |
| `result_too_large` | 400 | the result exceeds 1 MiB |
| `role_exists` | 409 | a role of that name already exists |
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

**Everything that can refuse it is decided at the claim, before a child process exists.** Twelve
checks, in this order, and the first that fails is the one the attempt records:

```text
operation_unknown → no_route → plugin_not_loaded → plugin_unavailable → plugin_operation_unsupported →
contract_incompatible → binding_invalid → approval_required → contact_required → no_channel_value →
suppressed → input_invalid
```

All twelve are final. Eleven are `permanent`; `input_invalid` is `validation`, because it is the one a
planner can fix by editing the item. The attempt is kept with its context snapshot either way — a
silent skip would leave the item looking claimable forever — and nothing ever falls back to another
plugin. [docs/routing.md](routing.md) says what each one means and what to change.

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
provider's answer unknown. `workitem.create` and `workitem.update` refuse such an item rather than
raising the number quietly: a lease silently changed is one the planner still believes.

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

- **Approvals.** Nothing pauses for a human decision yet, and an operation that requires one —
  `campaign.enroll` — therefore fails closed at the claim with `approval_required` every time.
- **Execution profiles.** `execution_profile` is recorded verbatim as an opaque string; nothing
  resolves it.
- **Session resume.** A host that is interrupted is retried from the start, not nudged to continue.
- **Backoff.** The retry delay is linear in the failure count; the tick and the one-item-per-campaign
  rule do the rest of the pacing.
- **Work-directory retention.** Nothing is ever cleaned up.
