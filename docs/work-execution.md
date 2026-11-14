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
  operations this build publishes a contract for — the documents under
  `docs/contracts/operations/` — and carries that operation's arguments under `input`, the one reserved key of
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
| `context.input<pointer>` | `invalid` | the arguments do not satisfy the `args` sub-schema of the operation's input, one detail per failure with the JSON pointer of the offending place (`context.input/channel`), all of them reported at once. The one key every operation reserves, `provider`, is held only to [its own rule](contracts/README.md#the-input-a-plugin-receives) |

A pattern that did not finish matching the arguments, twice, is none of these: a timeout says nothing about the
value, so nothing is written and the request is answered `validation_timed_out`, 503, `retryable: true`, with a
`pattern_timed_out` detail at each place it happened — send it again. Arguments wrong somewhere else are still
refused as above, and the refusal names only what is wrong.

What is measured here is the caller's own arguments — the `args` property of the operation's input
schema, with the document's shared definitions beside it so that a reference resolves — and not the whole composed input. The rest of that input is the runtime's to write, at
claim: the contact, the campaign and the idempotency key. So a rule the operation states across the
whole of it, such as a root `anyOf`, is not a creation-time check: at the claim the composed document
is measured against the operation's whole input schema, and that is where such a rule is enforced —
as `input_invalid` on the attempt rather than as a refused request.

`workitem.update` re-reads the arguments only when the patch names the reserved key — `set` writing
`input`, or `unset` naming it — and measures them against the item's **own** operation, which is not
patchable. A patch that changes the arguments of an item awaiting a person's approval withdraws that
question, and the claim asks again about the new arguments ("Approvals" below); a refused patch changes
nothing. An `ai_role` item is untouched by all of this: its context is a brief, and nothing in the
runtime reads the meaning of a brief.

## Creating work twice

A caller whose `workitem.create` timed out cannot tell a request that never arrived from an answer that
never came back, and sending it again would create the work twice. So `workitem.create` — and
`campaign.create` the same way — take an optional `idempotency_key`, a key the caller chooses:

- **The same request again** answers 200 with the item the first one made, as it is now, and writes
  nothing: no second item and no second `workitem_created` line.
- **A different request under the same key** is refused with `idempotency_key_reused`, a 409 with
  `retryable: false`, whose message names the item the key already belongs to. Sending it again cannot
  make the two requests one; a different request needs a key of its own.
- **No key, no change.** A create without one behaves exactly as it always has.

**"The same request" is what it asks for, not how it was spelled.** The actor, the reason and the key are
left out. Field order, white space, escapes, a field left out against one sent as `null`, white space the
runtime trims from a name, an instant written in another offset and a number written another way (`1`,
`1.0`, `1e0`) do not make two requests; any value that differs does — including a field sent as `0`
against one left out, because what was sent is compared, not what the runtime would default it to. One
exception keeps the comparison cheap: a number whose exponent is too long for a 64-bit integer is compared
as it was written.

**The key belongs to the item it created.** It is stored on the item's row and is unique within its
campaign; a campaign's key is unique in the installation. It is not an entry in a cache, so it does not
expire: a repeat a month later is answered the way one a second later is. A repeat is answered even when
what the first request met has changed since — the campaign archived, the person taken out of it, a
settings edit that would now refuse the same body — because the item was made, and the repeat is about
that. A request that is not a repeat meets every check a request without a key does.

The key is 1 to 128 characters once the white space around it is trimmed, and holds no control character
and no half of a surrogate pair; anything else is refused as `validation_failed` with a detail on
`idempotency_key`. No refusal quotes the key, and it never reaches the log or the journal.

**Nothing makes a key up.** `jason workitem create` and `jason campaign create` send one only when
`--idempotency-key` is given: a key the caller did not choose is one its own retry could not repeat. The
practice is to give each create a key that could be repeated, and after a timeout or a lost answer to send
the same command with the same key.

**Three keys, kept apart.** This one is a caller's key for a create. A provider operation's invocation
carries a key of its own — the work item's id, the same on every attempt — which the runtime composes into
what the plugin receives (see [Provider operations](#provider-operations)). And `report.submit` takes a reporter's key for a
report ([reports.md](reports.md)). None of them stands in for another.

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

processing ──► waiting ──► created                     (what it waited for happened, its deadline came, or somebody resumed it)

created | processing ──► waiting ──► created           (a provider block held it: the block cleared, or let it through as its trial)

created ──► waiting ──► created                        (a limit held it at the claim, with no attempt of its own: its moment came, or somebody resumed it)

(new) ──► waiting ──► created                          (created waiting on other work with wait_on: that work ended, the ceiling came, or somebody resumed it)

created | awaiting_approval | scheduled | processing | waiting | expired ──► cancelled
created | awaiting_approval | waiting ──► expired      (the due date passed before anyone claimed or answered it, or while it waited)
```

- **`created`** — on the queue. The only state from which work is claimed.
- **`awaiting_approval`** — the operation this item names needs a person's approval, and the claim
  parked it for one rather than performing it. No attempt was made: an attempt is a run, and nothing
  has run. It leaves this state when a person answers, when the work is cancelled, or when its due
  date passes. `docs/routing.md` §7 is the check that puts it here; `approval.list` is where it waits.
- **`scheduled`** — claimed, with an attempt row and a lease, on its way to a handler. It lasts
  milliseconds.
- **`processing`** — a handler is running the attempt.
- **`waiting`** — a run ended by waiting rather than finishing: until other work items end, all of
  them or any one, until a moment comes, or both, whichever happens first. Its attempt is `yielded` —
  over, and not counted against `max_attempts` — and the wait is a row of its own with a deadline: the
  moment it named, or `Dispatcher:AiRole:MaxWaitSeconds` after it opened when it named none. The
  bounds are held where the wait is written, and refused rather than bent: a moment later than that
  ceiling from now with `wait_too_long`, a wait on more than 100 items with `wait_target_invalid`, and
  a wait of an item that has already waited `Dispatcher:AiRole:MaxWaits` times with
  `wait_limit_reached`; a refused wait changes nothing, and the run still holds its item. A waiting
  item holds no lease and keeps its campaign free, so the items it waits for can run. The scan's
  **resume** step returns it to `created` once the wait is over, writing `workitem_resumed` with why;
  its next run is a new attempt, told why in the envelope's `resumption`. Anyone named may end a wait
  before it is over with `workitem.resume` — the line is theirs and carries their reason, and an item
  that is not waiting is refused with `workitem_not_waiting`. Cancelling or expiring the item closes the wait with it, and nothing
  resumes work that was stopped. Work a provider block holds waits here too, with no run of its own behind the
  wait: the claim parks it before any attempt exists, or the attempt that met the block ends `blocked`. Its wait
  names the block rather than items or a moment, is bounded by the item's `due_at` alone, and is released with
  `block_cleared` or `block_trial` (§Provider blocks). A responder its campaign's daily limit holds waits here as well,
  held at the claim with no attempt of its own, until the next 00:00Z (§The claim rule). An item created with
  `wait_on` is written waiting from its first moment, on the items of its campaign the create names (at most 100, each
  once; another campaign's item or one that does not exist is refused at `wait_on[i]`): it is never claimed before
  every one of them has ended, in any final state, or `Dispatcher:AiRole:MaxWaitSeconds` has passed, and spends no
  launch waiting. The wait only orders: once it is over the item is ordinary queued work, and the claim decides about it
  as about any other. A send created waiting on the work that reviews it is how a review lands before the send is
  claimed (§Doubts about a send); when the wait's ceiling releases such a send while its review still runs, the claim
  does not wait for it: the person is asked without the review, and a review that comes after the park is refused
  `not_reviewable`.
- **`succeeded`**, **`failed`**, **`cancelled`** — finished for good. `finished_at` is set and the
  result is frozen, in the service and by a database trigger.
- **`expired`** — the due date passed before anyone claimed it, before anyone answered the
  decision it was waiting for, or while it waited on other work. Not final: moving `due_at` into the future through `workitem.update`
  returns it to `created` — where, if its operation still needs a person's approval, the next claim
  parks it again, because the decision it needed was never given.

Nothing else in the runtime decides whether a move is legal; one transition table does, and every
state change writes one journal entry.

## Eligibility

An item is **eligible** — the dispatcher would claim it right now — when all five of these hold:

1. its status is `created` — work `awaiting_approval` is not eligible, and no number of scans makes
   it so;
2. its campaign is `active` (a draft campaign is a backlog, a paused one stops) — or `paused`, for a
   `provider_op` item whose operation declares `dispatch.in_paused_campaign: runs`
   ([docs/contracts/README.md](contracts/README.md)): an operation whose not running is the harm, which must still
   reach the provider after somebody paused Jason's campaign first. Today those are `campaign.pause`,
   `optout.record`, `message.cancel` and `audience.search_cancel` — or `archived`, for one whose operation declares
   `dispatch.in_archived_campaign: runs`, today `audience.search_cancel` alone: archiving is how a person stops a
   campaign, so the archive cancels the campaign's open work but such a stop, and the next scan makes one stop for each
   search of the campaign still in flight, which ends it `cancelled`;
3. `not_before` is unset or already past;
4. `due_at` is unset or still ahead;
5. `retry_after` is unset or already past.

`eligible` is never stored. It is computed from one shared expression that the item list and the DTO
use, and that the claim query states again in SQL — a window function the expression cannot carry — held to
it by a test across every campaign state, so `workitem.list --eligible` tells you exactly what the next
scan would consider.

## The claim rule

Each tick, the dispatcher runs one scan in twelve short steps: **expire**, **enforce**, **events**,
**sync**, **poll**, **respond**, **reserve**, **summon**, **resume**, **claim**, **notify**, **subscribe**. Events hands one read of an account's events to a
reader that runs off the scan, never as a work item ([docs/events.md](events.md) is the contract). Sync reads
each live campaign's conversations and says, in the chronicle, each thread that moved
([docs/inbound-sync.md](inbound-sync.md) is the contract). Poll follows each search a live campaign's audience is filled
by: one `audience.search_status` read of a search a cadence after its last, or at once where the last page carried
people and said more are readable now, waiting longer after reads that failed, never while a read or a stop of it is
under way, and once its reads have landed nobody for the stall bound it ends the search as stalled and reads it no more.
A search the provider holds is read on the slower cadence of a held search and stalls only at its own, longer bound; and
for a search of a campaign archived while it ran, poll makes the one stop the archive owes it
([docs/campaign-manager.md](campaign-manager.md) §10). Respond reads those lines, whichever writer said them, and
for a thread the campaign holds by its own pin queues a `conversation.get` of the thread and a responder created
waiting on it, or records the move on the thread's open responder ([docs/inbound-sync.md](inbound-sync.md) §1). Reserve
counts, for an active campaign that set a threshold, the people nobody has started on yet, when a line that can change
the number was written since its last look, and says once per crossing that the reserve fell under the threshold or
recovered ([docs/campaign-manager.md](campaign-manager.md) §9); it runs before summon, so a low reserve summons the
review in the same scan, and creates no work. Summon
is the campaign-manager loop — where a campaign that something happened to, or that has not been looked at
for long enough, gets a check-in put on the queue ([docs/campaign-manager.md](campaign-manager.md) is the
contract). All four run before the claim, so a read, a responder or a check-in created this tick can be claimed this
tick, poll runs before summon, so a search it ends summons the review in the same scan, and sync runs before respond and summon, so a reply it says wakes the responder and the manager in the same scan;
respond runs before summon, so the responder is queued ahead of the check-in the same line wakes. Resume releases the
waiting work whose wait is over: every item it named has ended — `succeeded`, `failed`, `cancelled` or
`expired`; an item retried and back in `created`, or itself waiting, has not — or any one has, or its
deadline has come. It reads statuses and the clock, never an event, so items that ended before the wait
was written, or a deadline that passed while the runtime was down, release it at the next scan. It
releases at most 500 waits a scan: a cleared block's waits are released over as many scans as the bound
needs, oldest first. It runs after enforce, so an item whose last attempt was lost for good releases
what waits on it in the same scan, and before the claim, so what it releases can be claimed in that
scan. Notify comes after the dispatch ([docs/notifications.md](notifications.md) is the contract), and subscribe last:
it offers the subscription runner the journal kinds whose chains have a line to take, so a chain sees what every step
before it wrote, and starts nothing while the dispatcher drains ([docs/plugins.md](plugins.md)).

Claiming happens inside an immediate transaction, so two scans — or two runtimes — cannot both take
the same item; the second waits, finds it taken and moves on. Within one scan the dispatcher takes:

- only eligible items;
- **at most one item per campaign**, and none at all from a campaign that already has an item
  `scheduled` or `processing` — a campaign runs one thing at a time, so a busy campaign cannot starve
  the others; an item `waiting` does not make its campaign busy;
- the most important first: `priority` descending, then oldest first;
- never more than the free handler slots, so a queued item cannot sit watching its own lease expire.

Work the runtime cannot perform is failed inside the same transaction rather than skipped: a
`provider_op` fails with whichever of the twenty-eight pre-flight reasons applies — `no_route` when nothing
routes it at all — and an `ai_role` whose role has no entry command fails with `role_not_launchable`.
A silent skip would leave the item looking claimable forever.

Work of a role is held, not failed, when `Launches:MaxPerDay:<role>` names the role and the item's campaign has already
started that many runs of it since 00:00Z: a failure would wake the manager, and a responder's reply would reach it by a
second path. The day is UTC, since a campaign has no time zone. The claim counts the attempts of the campaign's `ai_role`
items of the item's role that started since 00:00Z — an attempt the claim refused never started and spends nothing, and
an item queued by hand counts like one the runtime made — and holds the item in the same transaction: it moves to
`waiting` with no attempt, its wait is the next 00:00Z, its `last_error` is `launches_spent`, and one
`workitem_waiting` line says `held_by` with the code, the limit and the role. A held item does not make its campaign busy, so the
campaign's other work is claimed at the next scan. There are three ways out. At 00:00Z the resume step releases it and
the claim counts the new day as usual, so a day already spent by then holds it again. `workitem.resume` lets that one
item go past the limit under the person's name: the claim takes it at its next turn and its run counts like any other.
Every run counts, a resumption included, so a role whose run yielded can be held too; let go, its next run is told about
the wait its own run ended in, since the hold's wait has no attempt (§The launch envelope). Raising the setting
applies at the next scan to work not yet held, while an item already held keeps its moment. A held item keeps its due
date and expires by it like any waiting item, and a held manager check-in is still the campaign's one check-in
([docs/campaign-manager.md](campaign-manager.md) §1).

**The daily ceiling of automatic sends.** While a send mode is automatic, `Sending:AutomaticMaxPerDay` is the most sends
a campaign releases in a UTC day without asking a person. Where the automatic mode would release a send, the claim first
counts, in the same transaction, the sends the mode released from the item's campaign since 00:00Z: the rows born
approved with the reason `automatic_mode` and decided since then, whatever became of each. A retry runs under the row it
was released under and is not counted again, and a send a person approves runs under a row of another reason and counts for nothing. At
the ceiling the send parks for the person instead: its row's reason is `daily_ceiling`, and its preview carries
`daily_ceiling` with three numbers only, `limit`, `released` and `since`, the moment the count began. The ceiling never
slips, so a standing approval that covers the send does not let it go either, and the standing approval's answer in the
preview says `daily_ceiling`. The ceiling is asked only where the mode would release: a send the mode parks for another
reason keeps that reason, and where no mode is automatic nothing is counted and nothing changes. A send parked at the
ceiling is one more approval waiting, notified as any is, and it is the person's from then on: a new day does not
release it automatically.

## Approvals

An operation's contract says what approval it needs. Most published operations publish `auto`, which is
the one approval a runtime may give itself. `campaign.enroll`, `campaign.activate`, `enrollment.stop`,
`contact.delete` and `audience.search` publish `confirm_once`, `conversation.reply`, `social_invitation.send` and `message.send` publish
`confirm_each`, and work naming any of them is **parked** at the claim rather than run: the item moves to
`awaiting_approval` and an `apr_` row records what is being approved. The two words ask the same of a person in this
version: one work item is one person, so each reply, each invitation and each message is already its own decision.

**What the gate reads is the published value, and never the condition beside it.** A conditional
property always states the dangerous reading in `value` — `campaign.enroll` is `confirm_once` with a
condition that only decides whether a preview is *mandatory* — and Jason builds a preview every time,
so no condition is ever evaluated and none can soften the gate.

**What is approved is a subject, not an item.** The row holds the operation and its contract version,
the work item, the composed input the plugin would have received, the plugin that would perform it and
the account's binding identity, together with the canonical `sha256:` of that document. Before a later
claim runs the work it computes the subject again and compares the hash: an input edited after the
decision no longer matches, so the item is parked again with the reason `input_changed` rather than
run under a decision nobody made about it. A pin that the item's own earlier attempt learned is such a change
too: the next claim composes the input with it, so a person decides once more on what the plugin now receives,
and that costs one more decision, never a performance nobody approved. The plugin's version and the package's digest are
deliberately not part of the subject — they are provenance, recorded on the attempt that runs, and
including them would supersede every pending decision the moment an operator reloaded a package.

**Beside the subject is the preview**, assembled at the claim from the operation's own sentence about
itself, the dangerous readings of what it reaches, undoes and costs, the campaign, the person it would
reach and where, and the account — and, where the operation declares one, a summary rendered from the
composed input with the values it highlights, which `jason approval get --human` prints first
([docs/contracts/README.md](contracts/README.md), "The preview a person reads"). Neither is part of the
subject. It is stored rather than re-assembled, so what a person reads is what the claim saw.

**A park makes no attempt.** An attempt is a run; `max_attempts` counts attempts; an item parked three
times that had spent three of its attempts would have been given up on for being patient. Everything
else the pre-flight decides is a refusal and keeps exactly what it always had: an attempt, its
provenance, the coded failure, and one against the budget.

**Only a person decides.** `approval.approve` and `approval.reject` require an actor of type `human`
with a name; a role or an attempt is refused with `approval_not_human`, and an absent actor with
`actor_required`. What the runtime cannot do is tell a person from a process holding that person's own
command line — that is the operator's trust to give. What it guarantees is that nothing inside it can
approve anything, and that every decision names who made it. An approval, an answer, a preference or a campaign's send
mode sent from inside a run is refused with `decided_inside_a_run` whoever it names, since the CLI names its attempt on
every request it sends from there: a higher bar against a role deciding its own work, not a boundary.

An approval is about one operation on one work item, and it is not the only thing a person is asked. A
role that cannot decide *what to do next* raises a question instead — a decision, which outlives the
attempt that asked it and whose answer wakes the review that carries the work on.
[docs/campaign-manager.md](campaign-manager.md) §6 is the contract for that.

- **approve** releases the item to the next scan with its due date, priority and place untouched.
- **reject** ends the item as `failed`, with `approval_rejected` and the person's reason as its last
  error, and no attempt invented to carry it.
- **cancelling the work, or its due date passing**, ends the decision with it: a live approval about
  work that can never run is the one row that would make `approval.list` untrue.
- **a second decision on the same approval** writes nothing and answers `approval_not_pending`: both
  rows are moved in one transaction, each guarded by the status it was read at.
- **an edit of the parked item's input** withdraws the question. A `workitem.update` whose patch leaves
  `context.input` different from what the item held supersedes the pending approval, with the editor as its actor and
  `input_changed` as its reason, and moves the item from `awaiting_approval` to `created`, in the save that writes the
  patch; an edit that leaves the input as it was touches nothing of the approval. Nothing is asked at the update: Jason
  asks again when it next takes the work, at once where dispatch runs and when the campaign runs again where it is
  paused, under what the item now says and with the reason `approval_required` that any first question carries. A
  standing approval, the campaign's allowance or a send mode never lets such an item go: they answer only work nobody
  was ever asked about. An approval or a rejection that names the withdrawn question afterwards answers `approval_not_pending` and
  approves nothing, whichever request arrived first, by the same guards: an edit that read the item before an approval
  committed writes nothing and answers `concurrent_update`, and sent again it meets work that is approved and has not
  run, whose approval the next claim supersedes as above.

Journal: `workitem_awaiting_approval`, `approval_requested`, `approval_approved`, `approval_rejected`,
`approval_superseded`, `approval_cancelled`, `approval_standing_applied`, `approval_automatic_applied`. Those lines
carry identifiers only — the approval, the work item, the campaign, the operation, the subject hash, the actor and
the reason. The subject and the
preview live on the row and in what `approval.get` answers, which is where somebody entitled to read
them reads them.

Every approval answer — `approval.get`, each item of `approval.list`, and what `approval.approve` and `approval.reject`
answer — also carries `send_modes_unset`: the keys of the two send-mode preferences, `send_mode.cold` and
`send_mode.warm`, that nobody has set yet, in key order, and an empty list once both are. It is the installation's open
question, read when the answer is made, the same on every approval whatever it is about and however it stands: nothing is
stored about having asked it, and a key unset again is back in the list. While a key is unset, its mode is every message.

### Standing approvals

A person may decide in advance for one kind of reply, and for the searches under one of a campaign's audiences (the
allowance, below). `approval.set_standing` names a campaign and a verdict's
category — the word a `reply.record` verdict carries, compared exactly — and says that a drafted `conversation.reply`
answering such a verdict goes without being asked each time; `operations` may name `message.send` too, and is
`conversation.reply` alone when absent. `expires_at` and `max_releases` (1 to 10000) are optional limits, and there is
none by default. Only a named person sets one or takes it back (`approval.unset_standing`), any named person: a role or
an attempt is refused with `approval_not_human`, a person who gave no name with `actor_required`, and an archived
campaign with `campaign_archived`. Each change is a reserved line of the campaign's chronicle, `standing_approval_set`
or `standing_approval_unset`, keyed by the category; the newest line for a campaign and a category is what stands, a set
equal to the one in force writes nothing, and any other set replaces it whole, its count starting again.
`approval.list_standing` answers each category whose newest line is a set, with how many drafts it let go and whether it
is still in force — an archived campaign's too, shown not in force.

A draft names the verdict it answers in the reserved context key `reply_verdict`, the verdict line's `jrn_` id.
`workitem.create` checks it, and `workitem.update` whenever a patch names it or `input`: the work is a
`conversation.reply` or a `message.send`, and the line is a `reply_classified` verdict of the same campaign about the
thread the reply answers in — or, for a message, about the same person. Anything else is refused at
`context.reply_verdict`.

**It is asked once, at the claim, where nothing has been decided about the work yet.** Work that would park and has no
approval row of its own is covered when, in this order: its operation is one a standing approval can cover; the
verdict it names is a verdict of its campaign; no later verdict on the same reply replaced it; the verdict's reaction is
`draft`; the verdict was recorded by a run of the campaign's work that the runtime gave that reply's plugin and thread
(its `inbound_reply`; a responder a manager queued by hand carries none, so its verdicts fail `verdict_not_authentic`
and its drafts are asked), or by a person who gave their name outside the desktop's chat, or carries a person's answer — a verdict whose
`reply.record` named an answered decision, and whose line therefore names the person as `answered_by`; a run's verdict
covers only a draft a run of that same work created, while a person's, or one carrying a person's answer, covers
whoever drafted it; the draft's context was not changed after it was created; the
verdict's plugin is the one the route resolved, and its thread the one the reply answers in (for a message, its person
the work's person); the newest standing-approval line for the campaign and the verdict's category is a set, made outside the chat; it names
the operation; it has not expired; and fewer drafts were let go under it since it was set than its ceiling. A category
is the verdict's word exactly: `interested` covers neither `very_interested` nor `Interested`. The decision behind
`answered_by` was checked when the line was written — answered, by a named person, choosing the verdict's category,
raised by the attempt that recorded a verdict on the same reply — and the claim trusts the line for that; it reads no
decision again.

Covered, the work runs in the same claim. Its `apr_` row is written `approved`, with the reason `standing_approval`,
decided by the person who set the standing approval; an `approval_standing_applied` line by the dispatcher, keyed by the
set line's id, names the row, the operation, the subject hash, the standing approval, the verdict and the category.
Nobody is asked, so nothing is notified. Not covered, the work parks as any reply does, and its preview carries
`standing_approval`: the verdict, its category, and the first condition that failed — `not_coverable`,
`verdict_unknown`, `verdict_superseded`, `reaction_not_draft`, `verdict_not_authentic`, `not_the_verdicts_work`,
`edited`, `plugin_differs`, `thread_differs`, `no_standing_approval`, `operation_not_covered`, `expired` or
`ceiling_reached` — or, where every condition holds, `doubted`: a doubt stands about the send (§Doubts about a send),
and a doubt is the person's to answer, so no standing approval releases it; or `sent_from_chat`: the send was written
in the desktop's chat, which always waits for the person (§The send mode at the claim); or `daily_ceiling`: the
automatic mode would have released the send and its campaign has reached `Sending:AutomaticMaxPerDay` for the day,
which never slips (§The claim rule). A reply or a message that names no verdict is not asked about.

**A standing approval decides once.** Its row is an approval like any other: a retry of the same subject runs under it
even after the standing approval was taken back, and a subject that changed parks with `input_changed` for a person;
the standing approval is not asked again. A question already put to a person stays theirs, whatever is set later.
Whatever the pre-flight refuses, the do-not-contact register among it, is refused before a standing approval is asked,
and work released under one is still held by an open block of its account.

**The allowance: a standing approval for the campaign's searches.** A search spends the provider's credits, so the first
search under an audience, its probe, parks for a person as `audience.search`'s `confirm_once` says; after it, the person
may let the campaign's top-ups go without a question until a ceiling. `approval.set_standing` with `operations`
`audience.search` alone and no category sets it: `audience` names one of the campaign's audiences by its number
(`audience_unknown` where the campaign holds none under it), `max_people` (1 to 100000) is the ceiling in people and is
required, and `max_per_search` (1 to 1000) and `expires_at` are optional limits. `max_releases` is a reply's limit and is
refused on an allowance, as `audience`, `max_people` and `max_per_search` are on a category of reply, and `audience.search`
is never a category. Its lines are `standing_approval_set` and `standing_approval_unset` keyed by the operation, so a
campaign holds one allowance: a set equal to the one in force writes nothing, and any other retires the one before it,
an unset of it and then the set, both the person's. `approval.unset_standing` names `operation` `audience.search` instead
of a category to take it back. Only a named person moves it, as any standing approval.

**Approving a search moves it, in the same act.** `approval.approve` of a search may carry `allow`: `max_people`, and
optionally `max_per_search` and `expires_at`, which set the allowance for that search's audience, retiring the one before
it ("approve and allow up to N more people"). Without `allow`, approving a search under an audience other than the one the
allowance names retires the allowance: an unset by the person, with the reason they gave, saved with the approval.
Approving a search under the audience the allowance names leaves it as it is, and nothing else moves it: no role's act,
no verdict, and no probe that waits for a person, so a top-up under the allowance while a new audience's probe waits is
inside what the person allowed. An audience the allowance moved away from may be allowed again; none is the latest.
The allowance is a permission, not an order: the runtime lets through any top-up it covers, whoever created it, and no
role moves it. The manager's skill uses it only for an audience whose latest verdict is `working`; after the analyst's
`replace` or `stop` it creates no top-up of that audience and leaves the reserve low until the person acts, and the person
is asked at once. That rule is the manager's alone, kept in its skill: the runtime does not enforce it, since enforcing
it would let a verdict move the allowance, and a person's own top-up under the allowance still goes.
`allow` on a rejection, or on an approval of other work, is refused at `allow`, and in an archived campaign, whose
standing approvals no longer change, with `campaign_archived`; an approval there moves no allowance.

**At the claim**, a search that would park and has no approval row of its own is covered when, in this order: the
campaign's newest line for `audience.search` is a set; it names the audience the claim has just recorded on the search
(`context.audience`); it has not expired; the search's `count` is at most its `max_per_search`; and the counts of the
searches released under it since it was set, with this one's, come to at most its `max_people`. The gate counts people by
the count a search asks for, at the claim; what the provider charges is the provider's to count. Covered, the search runs
in the same claim as a covered reply does, and its `approval_standing_applied` line names the audience and the count, with
`verdict` and `category` null. Not covered, it parks, and its preview's `standing_approval` says what the allowance is —
`standing` (the line that set it), `audience`, `expires_at`, `max_people`, `max_per_search` and `people` (the counts
released under it), each null where there is none — the search's `count`, and the first condition that failed:
`no_standing_approval`, `another_audience`, `expired`, `over_per_search` or `ceiling_reached`. A new audience's probe parks
by construction, since no allowance names an audience before the claim of its first search made it.
`approval.list_standing` answers the allowance beside the categories, with `released` (the searches it let go) and
`people` (the people they asked for), and shows it not in force once `people` reaches `max_people`.

### Doubts about a send

A **send to a person** — provider work whose operation's `approval` is `confirm_each`, today `message.send`,
`conversation.reply` and `social_invitation.send` — may carry a doubt, which goes to the person on the send's
approval. A doubt is a code from one closed list, published as data under `$defs/code` of the
[`workitem_reviewed` kind's document](contracts/journal/workitem_reviewed.json) and read from there
(`jason journal kinds` shows it), and may carry one sentence: one line, at most 280 characters.

**The creator's own doubt** is the reserved context key `ask_person`, exactly `{"code": "<code>"}`, given at
`workitem.create` and only on such a send, answers included (`not_applicable` at `context.ask_person` otherwise). It is
set when the work is created and never moved: a `workitem.update` whose `set` or `unset` names the key is refused at that
field, whoever sends it. Its sentence is the request field `ask_person_note`, beside the context and refused without the
key; a `note` member inside the key is refused and pointed there. The sentence is part of a keyed create's digest.

**A review of the send** is `workitem.review`, written as the reserved line
[`workitem_reviewed`](contracts/journal/workitem_reviewed.json): `passed`, or `doubts` with a code and an optional
sentence (`note`). It is bound to the text it read by the **review hash**, the canonical hash of the operation, the work
item and its context's `input` as stored, which `workitem.get` answers as `review_hash` on such a send. Nothing but an
edit of the input moves the hash — a pin, a rename, the campaign's status or a route leave it as it is — and an edit
voids every review made before it: the line under the old hash no longer stands, the doubt's sentence is cleared in the
edit's own save, and a review sent with the old hash is refused `review_stale`. Only a send still `created` or `waiting`
is reviewed (`not_reviewable`), so a review lands before the person is asked or not at all. A review is refused
`not_reviewable` exactly where the claim would not read it: where an approval stands for the send as it would now go,
since the claim would run it under the decision already made and read no review. The one exception fails closed: where
the runtime cannot yet tell whether the claim would read the review, because an approval is live and the pre-flight
stops the send at a check that comes before the approval's (its plugin unavailable, say), so no subject can be composed
to hold the approval to, the review is refused too, and may be made again once the pre-flight lets the send reach the
approval; a provider block, the one check after the approval's, does not stop a review from being judged. A doubt
accepted then leaves the send with no approval to run under, whatever changes after; a pass leaves a person's approval
as it stood, and the claim may run the send under that approval without reading the pass. The doubt does so by
superseding, in the review's own save, the approved row still live, which no longer stands for the send as it would now
go (`approval_superseded`, under the reviewer's name). Who reviews: a running attempt of the same campaign's role work,
other than every attempt of the work that created the send (a retry of the composer is the same author), or a person by
name who did not create it (`review_by_creator`); a person's own send goes to them as an approval, their own explicit
yes. Whoever wrote the send's input since it was made is one of its authors and does not review it either, read from the
send's own `workitem_context_updated` lines for `input`: a person by name, or any attempt of a work that did
(`review_by_editor`). An edit by an actor the runtime cannot hold to anybody, under no name or as a role outside a run,
has an author no run can be told apart from, so from then on only a person by name reviews the send, and a run is
refused `review_by_editor`. Inside a run a review speaks as its run: an actor other than the attempt the request's
header names is refused; and a send created inside a run is created under that run's name, since its creator is who a
review is told apart from: a create naming a person, another attempt or nobody is refused
`created_as_another_inside_a_run`. A patch of such a send's input inside a run is the same door: one naming anybody but
the run is refused `updated_as_another_inside_a_run`. The verb takes no `reason`, since a reason is journaled and the
line never carries the doubt's sentence. Which review stands for the send is read from the send's own lines under its
current hash: the newest doubt, over any pass of the same text, or else the newest pass, or none. A doubt is answered
only by the person's decision on the approval, never by a later pass. The order between a send and its review is the
runtime's: the send is created waiting on the review's work (`wait_on`, §Lifecycle), so it is not claimed before that
work has ended; whatever way it ended, the claim then decides as ever. A doubt parks the send for the person ahead of
any standing approval: where one would cover it, the send parks all the same, its preview's `standing_approval` saying
`doubted`, and no row is born approved; a pass is no doubt and leaves the standing approval to decide.

An edit made under no name, or as `role:<name>` outside a run, names no author a review could tell apart and is never
matched as an editor of a send: only a person who gave their name and an attempt are, so such an edit stops nobody
reviewing the send.

**The sentence is kept out of every record that lasts.** Each is a person's or an agent's text, so it is stored on the
item's own row (`work_items.ask_person_note`, `work_items.review_note`, the newest doubting review's), outside the
context: no journal line, no attempt's snapshot, no read of
the item (`workitem.get`, `workitem.list`) and no log line carries it, and a diagnostic bundle, which reads the item
pages for their metadata, never sees it. When the claim parks the send, both are moved onto the approval's preview and
cleared from the row in the park's own save; from then on `approval.get` and `approval.list` are the reads that return
them. A send that ends for good before a park — cancelled, failed or succeeded — takes both off the row as it ends; an
expired one keeps them, since a reopen leads to a park. The preview's `doubts` is a list, one entry per doubt that
stands: `source` (`ask_person` or `review`), `by` (the actor that doubted), `role` (the reviewing run's role, or null),
`code`, `note` (the sentence, or null) and `line` (the `workitem_reviewed` line, or null for `ask_person`). A send
parked again after an edit or an expiry carries the creator's `ask_person` sentence over from its earlier approval's
preview, since the key is never taken back; a review's is not carried, because the text it was about no longer stands.

### The send mode at the claim

A send to a person may go without being asked where a person said so for the kind of contact it
reaches: the send mode, `automatic` or `every_message`, for a cold contact and for a warm one, set for the installation
by the named preferences `send_mode.cold` and `send_mode.warm`, and for one campaign by `campaign.set_send_mode`, whose
own mode is the one in force where it sets one. Unset, a mode is `every_message`, and every such send parks as above. A
mode releases only in the name of the person who set it: a mode whose line names no person by name releases nothing.
A contact is **warm** in a campaign once a verdict that stands there judged a reply of theirs a person's answer, and
**cold** before; while a reply of theirs the campaign's own read saw waits for a verdict, or was judged without saying
what kind of reply it is, they are neither, and nothing goes to them automatically.

**It is asked at the claim, where nothing has been decided about the work yet**, in the same place and on the same
guard as a standing approval, and in this order:

1. **Is a mode in force?** The send is a send to a person and names a contact, and a mode of `automatic`
   set by a named person stands for its campaign, for cold or for warm. Where none does, nothing below is asked, warmth
   is not read, and the send parks as it always did.
2. **The contact's warmth**, read from the campaign's chronicle. Where the mode for the contact's warmth is
   `every_message`, the send parks as it always did.
3. **Written in the chat?** A send created, or whose input was edited, by a call the desktop's chat made parks for the
   person: `sent_from_chat`.
4. **A doubt** stands about the send (§Doubts about a send), whoever raised it: `doubted`.
5. **The contact waits on a verdict**: `awaiting_judgement` where a reply of theirs has not been judged since it moved,
   `awaiting_nature` where it was judged without saying what kind of reply it is.
6. **An answer goes only to a person's reply nobody escalated.** A `conversation.reply`, or any send that names the
   verdict it answers (`reply_verdict`), is held to the verdict that stands on the reply it answers: the one it names, or
   else the newest on its thread; none, `answers_no_verdict`; a verdict that does not say what kind of reply it was,
   `awaiting_nature`; a reply that is not a person's — an automatic reply, a bounce or a request to stop —
   `reply_not_from_a_person`; a verdict whose reaction is `escalate`, `escalated` — and `escalated` too where a verdict
   that stands on the same thread, through the same plugin, about a reply at or after the one answered, says escalate,
   or where either moment is not known: the conversation was marked for the person, whichever reply the answer names.
7. **A review that passed the text as it now reads.** A pass under the send's review hash counts where a person by name
   wrote it, outside a run and outside the desktop's chat, or where the critic the runtime created for this send did:
   the `critic` work the runtime makes for a send created under an automatic mode, which it creates under its own
   actor, names the send and its operation in the reserved context key `send_review` (`{"work_item_id", "operation"}`),
   and whose context was changed since by nobody but the runtime's own actors: a change by a person, a role or the
   critic's own attempt disqualifies it. Any such pass counts, whatever was reviewed after it, since a doubt was asked
   before. A counting pass only under an earlier hash is
   `edited_after_review`; none at all, `not_reviewed`. Another critic's pass does not count: one somebody queued by
   hand, or the runtime's critic of another send. Nor does a pass by another role, on a line a run wrote in a person's
   name, or naming an attempt other than its own; each is shown on the preview and releases nothing.
8. **The daily ceiling.** The campaign has had fewer sends released by the mode since 00:00Z than
   `Sending:AutomaticMaxPerDay`; at it, `daily_ceiling`, and the preview carries `daily_ceiling` with the limit, the
   count and since when (§The claim rule).
9. **Released.** The work runs in the same claim, under an `apr_` row written `approved`, with the reason
   `automatic_mode`, requested and decided at the claim's moment, decided by the person who set the mode in force.

A park of steps 3 to 8 makes no attempt and waits in `approval.list` as any approval does; its row's reason is the code
above, and its preview carries `send_mode`, the facts and never the code: the `mode`, its `source` (`preference` or
`campaign`) and the contact's `temperature` (`cold` or `warm`), all three null while the contact waits on a verdict,
and the `review` line the step read — the pass, the pass under the earlier text, or another role's — or null. Where
no mode is in force the reason is what it always was. The parks of steps 5 to 7 **fall through to a standing
approval**: where one covers the send, it runs under the standing approval's row as before. A send written in the chat,
a doubted one and one at the daily ceiling do not: they park even where a standing approval would have covered it, and
its preview's `standing_approval` says `sent_from_chat`, `doubted` or `daily_ceiling`. A send written in the desktop's chat always waits for the person,
whatever the send mode, `every_message` included.

**A release decides once, but warmth is read again.** A retry of the same subject runs under the row the mode wrote, and
reads no review again, so a review sent then is refused `not_reviewable`. It reads the contact's warmth once more: where
a reply of theirs landed since and waits for a verdict, the row is superseded and the send parks for the person with
`awaiting_judgement` or `awaiting_nature`. A subject that changed parks with `input_changed`, as under any decision.
Whatever the pre-flight refuses is refused before the mode is asked, and work released under it is still held by an
open block of its account.

**The chat's mark.** Every request a confined call of the CLI sends (`JASON_CONFINED`, which the desktop's chat sets on
every turn) carries the header `Jason-Confined`. The runtime records it on the work it creates under it, as
`confined: true` on the `workitem_created` line, and on the first edit of its input made under it, as a
`workitem_updated` line keyed `confined`; the claim reads the mark through the item's own lines, and nothing takes it
back. A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, so a program
that leaves the header off is not marked.

**A line written under the mark is the chat's, never a person's own word**, as a line that carries an attempt is the
run's. Besides a send's lines, the mark is recorded, as `confined: true`, on a verdict (`reply_classified`), on a
review (`workitem_reviewed`) and on a standing approval's set line (`standing_approval_set`) written under the header.
A verdict so marked lifts no opt-out, and under a standing approval it vouches for no draft as a person's verdict
(`verdict_not_authentic`) unless it carries a person's answered decision, which is read as the decision records it; a
review so marked is no person's pass, and a set line so marked sets nothing (`no_standing_approval`).

Journal: `approval_automatic_applied`, by the dispatcher, keyed by the temperature (`cold` or `warm`), names the row, the
operation, the subject hash, the mode's source, the temperature and the review line, and carries no text. Nobody is
asked, so nothing is notified.

### Send modes

A send to a person goes under one of two send modes, `automatic` or `every_message`: one for a contact who has not
answered in the campaign, cold, and one for a contact who has, warm. The mode in force is the campaign's own where it
sets one, else the installation's preference `send_mode.cold` or `send_mode.warm`, else `every_message`
([`send_mode.cold`](contracts/preferences/send_mode.cold.md),
[`send_mode.warm`](contracts/preferences/send_mode.warm.md)). It is read when a send is claimed, never frozen when the
work is made.

**Cold, warm or waiting.** Where a contact stands in a campaign is read from that campaign's own chronicle alone — its
`inbound_reply_observed` lines and its `reply_classified` verdicts — and from the responders the runtime made for a
thread. Nothing is stored for it, another campaign's verdicts are never read, and nothing here reads a category, a
reaction or the words of a reply.

- **Warm** is a verdict that stands in the campaign (no later verdict under its key), whose `nature` is `person`, whose
  body names this contact, on a thread the campaign's own read saw move. Once warm, always warm: no moment is read.
- **Waiting** is a reply of the contact's, seen by the campaign's own read, that no standing verdict has judged since it
  moved. A contact waits for a judgement, or, where the only verdicts that would judge it state no nature, for the
  nature. A waiting reply outranks warmth: a warm contact's new reply may be a request to stop, so it is judged before
  anything else is said of the contact. A provider event's `reply_detected` line is not a reply that waits.
- A verdict **judges** a reply when it stands, states a nature and is about a message at or after the reply's move. A
  responder's verdict does too, where the responder read the whole thread after the reply moved: the read was a
  `conversation.get` of the reply's thread that succeeded and whose page was all of it, nothing more to come, and the
  responder that waited on it ran after it and succeeded. Every order here is the journal's own, never a clock.

**Where the wait fails closed.** What does not prove a reply judged leaves the contact waiting:

- On a thread longer than one page the responder's read proves nothing, since its result says there is more: the contact
  stays waiting.
- A read still open when its wait ran out, or one that failed, was cancelled or expired, leaves the contact waiting: the
  responder saw no transcript and judges nothing. So does a responder with no read, because no route named the plugin
  that saw the thread, or the read refused its identifier.
- A reply that reaches the thread while its responder is already waiting or running is added to that responder, which
  does not judge it, whatever it then does. The contact waits until a verdict about that reply is recorded, or until the
  next responder the runtime creates on that thread succeeds: it reads the whole thread, so it clears every earlier
  reply of it at once. In practice this heals at the thread's next move.
- A responder or a read whose context anyone but the runtime changed judges nothing, since `workitem.update` may rewrite
  any key of an open item's context.
- A reply the campaign heard of only through the contact's pin makes nobody wait, and a verdict on its thread warms
  nobody: the contact stays as they were.
- A move on a thread that names no contact makes wait only a contact a standing verdict names on that thread, and only a
  responder that names that contact judges it: the responder made for the unnamed move is about nobody and clears
  nothing, so the contact waits for a verdict about a message at or after the move, or for a later responder that names
  them.
- A contact whose last reply was judged by a verdict recorded before 0.42.0 waits for its nature, since such a verdict
  states none, until a verdict with a nature is recorded on that thread.

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
the failure carries one of five **classes** — `transient`, `permanent`, `validation`, `ambiguous`, `blocked` —
and the class plus the operation's own `repeat_after_ambiguous` rule decide, not the code table. Every
way such an attempt can end with nobody answering for it — the invoker's timeout, `lease_expired`,
`heartbeat_missed`, a command that could not be started (`executor_launch_failed`) and one that was
stopped (`executor_exited`) — is `ambiguous`, because a missing answer says nothing about whether the
provider acted. An attempt a restart finds still `scheduled` is not one of them: it never started, so nobody
was asked anything. A shape error in an answer that did arrive, or a failure whose code
the operation does not declare under that class (`result_invalid`), is `ambiguous` and never repeated:
the next attempt would run the same code over the same answer. A cancellation stays a cancellation in both kinds.

`attempt_count` counts **failed** attempts — the ones that count toward the limit. Work that succeeds
first time shows `attempt_count: 0` with one entry in `attempts`. An attempt that was `interrupted` by
a restart, an attempt that `yielded` to wait, an attempt that ended `blocked` — the provider refused it for a block
and did nothing — and any attempt `cancelled` by a caller, never count.

There is no separate inbox entity: what needs a human or a manager role is
`jason workitem list --status failed --status expired`. A listing takes an order and a window on the time it orders
by: `--order due` lists soonest first with undated work last (`jason workitem list --status created --order due` is
what is still to run), `--order finished` lists newest first and leaves open work out
(`jason workitem list --order finished --since 2026-10-16T00:00:00Z` is what finished since that moment), and
`--since` and `--until` (`now` is the command line's own clock) keep the items whose ordered time falls inside, an item
without that time left out. `jason workitem count` takes the same filters and answers how many items the listing would
return, without paging through them. `--block blk_…` (`provider_block_id`) keeps the items a provider block holds: those
whose open wait names it (§Provider blocks).

## A campaign's people, counted

`jason campaign summary` (`campaign.summary`) answers, for each campaign named or every campaign not archived, in every
project, where its people stand now; `jason campaign activity` (`campaign.activity`) answers what happened to them
between `--since`, included, and `--until`, not included. Each count is a number of people, never of work items: a
person may be in several counts at once, so the counts need not add up. Both answers also carry `total`: the same counts
across every campaign asked, each person counted once however many of those campaigns count them. The total's `out`
counts a suppressed person in a campaign with no sequence too, whose own `sequence` and `out` the CLI prints as `-`, as
it prints the total's when no campaign asked has a sequence. With `--human` the CLI names each campaign by
its name, with its id beside it: the answer names campaigns by id alone, so the CLI reads their names from the campaign
listing (`campaign.list`, archived campaigns included). The JSON output is the answer as it came.
`jason campaign list-contacts` (`campaign.list_contacts`) lists the people of the campaigns named, or of every campaign
not archived, each person once with the campaigns that list them under `counted`; with `--summary <count>`, or with
`--activity <count>` and the same period, it lists exactly
the people one count counts, each with why it counts them in each of those campaigns: the approval or question waiting,
how their latest work ended, or when in the period it happened, the latest first. Under `--activity people` the why is
the latest of what happened to them, its `text` naming the count it was. Work about a person is work in that campaign
whose person they are, and a line about them a journal line of that campaign that names them. A provider's event is a
line of each campaign it reaches ([docs/events.md](events.md) §3): one that no campaign's provider campaign places, such
as the reply to a direct email or a message outside a sequence, reaches every campaign not archived that the person is a
member of, so one reply counts in each of them and once in `total`.
`--audience <number>` (`audience`) keeps `campaign summary`, `campaign activity`, `campaign activity-by-period` (below)
and `campaign list-contacts` to one of the campaign's audiences
([docs/campaign-manager.md](campaign-manager.md) §10): the memberships a search landed under it, removed since or not,
and no other, each count by its own definition. An audience is one campaign's, so the read names that campaign alone;
one nobody landed under answers zeros, and a number the campaign holds none under is refused, `audience_unknown`. A
member added any other way is in no audience.
`--exclude-archived-contacts` (`exclude_archived_contacts`) on the same four leaves out the people whose own record is
archived: archiving a person cancels their open work and keeps their memberships, so without it an archived member is
counted and listed as before. With it on every read, a count and the list it opens still agree, now and in a period,
and one campaign's list holds the people `contact.list` lists for that campaign without `--include-archived`.

Now:

- `people` — Everyone in the campaign; removed people are not counted.
- `waiting` — An approval or a question about their work is waiting for you. A question counts for the people whose
  work raised it or whose work it names; a question about the whole campaign counts for nobody.
- `failed` — Their latest work failed or expired, and nothing else is planned for them. A rejected approval is a
  person's decision and a cancel is one too: neither is a failure, and either, finishing after a failure, is the newer
  finish, so the person is no longer `failed`.
- `working` — Work about them is planned or running. Work waiting for an approval is `waiting`'s.
- `new` — Nothing has been done or planned for them yet: no work about them was created by a person, a role, an
  attempt or a plugin. Work the runtime made about them by itself, the read of their thread and the responder a reply
  woke, does not count. A campaign's reserve is this count ([docs/campaign-manager.md](campaign-manager.md) §9).
- `idle` — Their work finished without failing, nothing is planned, and they were never put into the sequence.
- `sequence` — They were put into the provider's sequence and are not out of it.
- `out` — They replied, opted out, were stopped in the sequence, or hard-bounced. A reply is one the inbound read or
  the provider's events observed; opted out means on the do-not-contact register now; a soft bounce takes nobody out.
- `removed` — They were taken out of the campaign. They count in nothing else now, and stay listed, so a re-import
  never adds them back.

`sequence_linked` is false while the campaign's provider campaign is not pinned: until it is, nobody can be in the
provider's sequence.

The same definitions read for one person say where they stand: every answer that carries a person's campaigns
(`jason contact get`, `jason contact list`, `jason campaign list-contacts`) gives each membership a `standing`, the counts
above that count the person in that campaign now, in this order; a removed membership stands in `removed` alone, and one
no count counts stands in none. `jason contact list --summary <count>` (`contact.list` with `summary`) lists the people
one count counts in at least one campaign not archived, or in the one `--campaign` names, in either of its orders. A
count, in each campaign's row and in `total`, counts an archived person who is still a member unless
`--exclude-archived-contacts` (`exclude_archived_contacts`) leaves them out; with it, a count agrees with `contact.list`
without `--include-archived`.

In a period, removed people included, so a past period does not change when somebody is taken out today:

- `people` — Something happened to them in the period: they are in at least one of the period's counts.
- `added` — Added to the campaign, or added back after a removal, in the period.
- `worked_on` — Work about them finished in the period, whether it succeeded or failed.
- `contacted` — Something was sent to them, or they were put into the provider's sequence, in the period. That is a
  `campaign.enroll`, `message.send`, `conversation.reply` or `social_invitation.send` about them that succeeded, even
  an enrollment into a provider campaign not live yet; research never counts.
- `replied` — Their first reply in this campaign came in the period. A reply is dated by the moment the provider gives
  it, so a reply read again later, or a follow-up in the same thread, never counts them a second time.
- `opted_out` — One of their addresses was put on the do-not-contact register in the period. It is read from the
  journal, so an entry lifted since still counts.
- `removed` — They were taken out of the campaign in the period, whether added back since or not.

The same counts over time: `jason campaign activity-by-period` (`campaign.activity_by_period`) takes the campaigns named
or every campaign not archived, one count's name, a period of `hour`, `day` or `week` (Monday to Sunday) and a window,
and answers that count for each bucket of the window as if the bucket were the period, people counted once within it
across the campaigns. A person counted on two days stands in both buckets and once in `campaign.activity`'s `total` over
the window, so the buckets may add up to more.
The buckets are cut in the IANA zone the request names, UTC when it names none, and `jason campaign activity-by-period`
names this machine's zone unless `--zone` names another; a day is the zone's day, 23 or 25 hours long where its clocks
change; the first and the last are clipped to the window. A window cut into more than 744
buckets is refused, and so is one within eight days of either end of the calendar, where a zone's hours and weeks cannot
be cut.

## Launching an executor

An `ai_role` item is run by starting an **agent host** as a child process. Which host, and with which
arguments, comes from the item's **execution profile** — resolved at the claim from the item, its
campaign, its role, what it inherited and the configured default, in that order.
[docs/execution-profiles.md](execution-profiles.md) is the contract for all of it.

Where no level names a profile, the role's **entry command** runs the work as it always has: the
role's own `entry_command` if it has one, otherwise `Roles:DefaultEntryCommand` from settings,
otherwise the attempt fails with `role_not_launchable`. The attempt records which of the two
happened, so an absent profile name is a fact rather than an inference.

An agent attempt's `provenance.agent` is written at the claim and never rewritten: the level that chose the
profile, its id, name and revision, the revision it inherited where lineage chose, the host, the program, the
whole argument list as launched, the host version the profile was verified against, the session id where the host
takes one from the runtime, and `tools` — the tools that revision granted, in the API's words and empty where it
granted none, beside the revision they came from. A launch the machine refused keeps as much of it as the claim had decided, the grant
included. An attempt run by a role's own entry command names no profile and no grant.

### The agent pre-flight, in the order it is checked

Every one of these is decided **before a child process exists**, except `host_not_available` where a host that ran
answered that it is not signed in: that ends the attempt after the child ran, with the host's fixed sentence and no
byte of what the host wrote, where the host's row in [execution-profiles.md §12](execution-profiles.md#12-the-hosts)
names the signal Jason reads. Otherwise the first that fails is the one the attempt records. The attempt is kept either way, with the context it was claimed with and the
provenance as far as the decision got. None of them is retried: nothing about the work changes
between two scans, so an item put back would be refused for the same reason for as long as the queue
existed. It is the same rule the twenty-eight provider checks follow.

| # | Code | What it means, and what to change |
|---|---|---|
| 1 | `lineage_resolution_unsupported` | a run created this work and no profile can be inherited from it. Name one on the item, its campaign or its role |
| 2 | `profile_not_found` | the profile a level named does not exist. The message says which level named it |
| 3 | `profile_disabled` | it exists and is out of service. Enable it, or name another |
| 4 | `profile_revision_refused` | the profile's current revision holds a word this build's rules for its host refuse: an argument the host now reserves, say, in a revision written under an older build. The message names the first refused field and its code, never the value. Read the profile (`jason profile get`; its `problems` say what to change) and update it |
| 5 | `host_not_available` | the profile's program is not on this machine, or is a script a shell would have to interpret; or the host refuses to start here by its own check of the machine (a folder its sandbox needs, a policy folder it would read), or names a host this build does not register, or has an installation that could not be read. Install it, or point the profile at the program itself, or follow what the sentence names. The same code ends an attempt whose host ran and answered that it is not signed in: the person signs in to it on this machine |
| 6 | `role_skill_invalid` | the role's skill could not be given to it — misnamed, or past `Roles:MaxSkillBytes` |
| 6 | `role_skill_unreadable` | the role's skill was being replaced by a deployment and could not be read consistently while this launch waited |
| 7 | `role_not_launchable` | no profile anywhere, and the role has no entry command either |

The child is started in a per-attempt **work directory**, `~/.jason/work/<wi_…>/<att_…>/`, which the
launcher creates. Its standard output and standard error are written there as `stdout.log` and
`stderr.log`, both read from the child as UTF-8 on every platform, whatever the console's code page,
and written as UTF-8 without a byte-order mark. Nothing is cleaned up in this version.

Both files stop growing past `Roles:MaxStdoutBytes` and end with the line

```text
[jason] The transcript passed 1048576 bytes and is cut here; Roles:MaxStdoutBytes says how much is kept. The child kept running, and no outcome depends on this file.
```

after which the attempt's launch record says `stdout_truncated: true`. The pipes are drained to end
of file regardless — a pipe nobody empties blocks the child writing into it. A cut transcript never
changes an outcome: the runtime reads no result from standard output, only from `workitem.set_result`
and `workitem.complete`.

The launcher puts in that directory, before the child starts, the files the host's row of section 12 and its section name
([execution-profiles.md](execution-profiles.md#12-the-hosts)), and nothing else: the host's settings for the attempt,
where the host reads them from its work directory rather than its command line, and the role's skill.

- The settings, on the `claude_code` host `.claude/settings.json`, carrying the **deny** rules of the execution profile that runs the attempt,
  a deny of each web tool its revision does not grant ([execution-profiles.md](execution-profiles.md) §7),
  and three exclusions, `"claudeMdExcludes": ["**/CLAUDE.md", "**/.claude/rules/**", "**/AGENTS.md"]`,
  and nothing else: **a launched role loads no `CLAUDE.md`, no rules file and no `AGENTS.md` of the
  host's own accord** — not the operator's own user-level ones, which reach a session whatever its
  setting sources say, and none above the work directory ([execution-profiles.md](execution-profiles.md)
  §7 says which of that was measured). It never carries an allow list: a directory the runtime created
  is not a workspace the host trusts, and an allow entry there is ignored without being
  reported, so what the agent *may* do travels on the command line instead. That directory is also the
  **only** place the session's settings come from: the composed command reads no user-scope settings and
  loads no MCP server, so a launched role inherits neither the operator's plugins and hooks nor their
  providers ([docs/execution-profiles.md](execution-profiles.md) §7 says what a session is and is not
  given, including the two things that surprise people: every built-in tool is listed, and of the ones a
  role reached for the mode refused everything but reading in the work directory, the files the rules
  name and what the host counts as read-only, so what the allow rules grant is a floor and not a ceiling;
  and the role has no file-writing tool at all).
- The role's skill, a copy of `~/.jason/skills/roles/<role>/` when the role has one, in the folder that row names
  (`.claude/skills/<role>/` on `claude_code`). Files
  only; a link is neither copied nor followed. A skill that cannot be given to the role fails the
  attempt with `role_skill_invalid` before the child starts — because its `SKILL.md` is a link, cannot
  be read, gives itself no name or names something other than the role, which a host answers by
  ignoring the skill without saying so, or because it is past `Roles:MaxSkillBytes`;
  [release-and-update.md](release-and-update.md#and-what-it-says-about-the-skills-it-will-teach-from)
  lists each by its code. Any of them, and the role would do the job untaught, at the price of a real
  launch, with only a log line to show for it. A role with no skill directory
  launches normally; nothing was configured, so nothing is missing, and its brief travels in the
  envelope either way.

Its environment is the runtime's own, less the runtime's configuration, plus the runtime's four and the constants a host's row of the host table declares, never a secret and never a value taken from a person. The runtime's configuration is every variable that starts with `JASON_` and holds the double underscore between a section and a key, compared without regard to case: any setting can be given that way, a plugin's secret under `Plugins:Config` (`JASON_Plugins__Config__<id>__<field>`) among them, and an agent host that prints its environment would put it in the transcript the person keeps. A name the active plugin snapshot grants to any plugin under `Plugins:Grants:<id>:Env` is taken out too, since it carries that plugin's credential, unless it is one of the machine configuration names every plugin host is given ([plugins.md](plugins.md)), a proxy say. Each is taken out before the four are written; the runtime's own single-word `JASON_` names are not configuration, and every other variable the runtime's environment holds stays. This keeps those values out of a dump of the agent's environment and is not a wall: the agent runs as the person and can read what the person can. The four:

- `JASON_DATA_DIR`, pointing at the data directory;
- `JASON_ATTEMPT_ID` and `JASON_WORK_ITEM_ID`, both non-secret values the envelope already carries, so
  that an agent reporting through the CLI is its attempt without having to say so; and the CLI names
  the attempt to the runtime on every request in the `Jason-Attempt` header, whatever actor it claims,
  so the verbs that are a person's own word refuse it (`decided_inside_a_run`);
- the directory this build of Jason runs from, at the front of `PATH`, so the bare command word in
  `runtime.cli_command` reaches this runtime rather than whatever else answers for that name.

A host's constants are added after the four and are the same for every attempt of that host. None is named as one of
the four or starts with `JASON_`, and a host that declares such a name is refused when the child is made. A host
that declares constants names each one in its row of the host table in
[execution-profiles.md](execution-profiles.md#12-the-hosts); Gemini CLI's, for one, keep a signed-out run from waiting
for a person.

One value is taken away: `JASON_UPDATE_LAUNCH`, the mark an update puts on its own start of the runtime
([release-and-update.md](release-and-update.md#when-it-stops-halfway)). The runtime drops it from its own
environment before it starts anything, and the launch removes it again, so no agent carries it.

**The capability token is never on the command line, never in the environment, and never stored**:
every occurrence of it is replaced with `[redacted]` before anything the child wrote is put in a file
or in an attempt's error trace. A plugin handler's narrower capability follows its own rule
([plugins.md](plugins.md), §10).

### The launch envelope

One JSON object is written to the child's standard input, which is then closed:

```json
{
  "envelope_version": 5,
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
  "role_memory": { "campaign_id": "cmp_…", "role": "researcher", "project_id": "prj_…" },
  "memory_files": {
    "client": "…/Jason/AGENTS.md",
    "project": "…/Jason/projects/acme-corp/AGENTS.md",
    "campaign": "…/Jason/projects/acme-corp/campaigns/q3-marketing-leads/AGENTS.md"
  },
  "resumption": {
    "previous_attempt_id": "att_…",
    "resumption_number": 1,
    "waited_since": "2026-09-11T10:00:00.000Z",
    "woke_because": "children_ended",
    "items": [
      {
        "work_item_id": "wi_…",
        "kind": "ai_role",
        "role": "researcher",
        "operation": null,
        "status": "succeeded",
        "finished_at": "2026-09-12T09:30:00.000Z",
        "last_error_code": null,
        "result": { "…": "the item's result, while the results fit" },
        "result_omitted": false
      }
    ]
  }
}
```

`context` is a snapshot: an edit made while the attempt runs belongs to the next attempt, not this one.

`runtime.cli_command` is the bare word that reaches this runtime, so an agent never has to guess it.

`role_memory` is the address of the role's own notes, and never a note: the campaign, the campaign's
project and the role name its note at each of the three levels — the campaign's, the project's, and the
client's, which names neither. A document copied in here would be what the role believed when the
attempt was launched, arriving beside the brief as though it were current; read through the API at the
moment it is wanted, it is plainly a document with an age. It is null for work that is not a role —
which is true by construction rather than by a decision taken at launch: the only command that writes an
envelope is the one that runs `ai_role` work. What a note is worth — and why a launched role's memory
lives in the runtime's store at all — is in the role-notes section of
[execution-profiles.md](execution-profiles.md).

`memory_files` are the paths of the person's three memory files — the `AGENTS.md` of the client at the
harness root, of the campaign's project and of the campaign ([projects.md](projects.md) says where they
are kept) — and never their words, for the reason `role_memory` is an address. The host reads them by
path; the runtime never reads one. **What Jason composes is a read of the files `memory_files` names and nothing wider outside its work directory, where
the host's form can hold it**: each host holds this rule in its own form
([execution-profiles.md](execution-profiles.md) §12 and the host's own section), and on some the session's read is wider
than the list, as the sections of those hosts say: Jason confines nothing Cursor CLI reads, Codex on Windows runs in a
sandbox that may let a role read more, and Gemini CLI, below, is given a folder. A profile is composed with a read
for each path that is not null: a rule for the file, never its folder, on every host but Gemini CLI.
Gemini CLI is given the folder of each level and reads it whole. Antigravity CLI is given its rule as a file rule too, but
through a workspace hook the launch writes rather than a permission rule: the hook approves a view of exactly the files
named (§12 gives each host's form, and each host's section the paths that get no rule because the host would read them as
a pattern or split them at a comma or a parenthesis; the envelope still names such a path, and the role's read of it is refused). Which paths are given
follows one rule: a path is given when Jason knows the folder is its own — the recorded `folder_path` for a
project and a campaign, the root marker for the root, which no record holds; no existence check on a recorded
path, a stale one is the host's to find and `project link` re-records it; the object itself is never null at 4; additive over 3; the
reading order is the skill's, never the runtime's. A level whose folder Jason does not know is null: a
project or a campaign with no folder recorded, a root that is not there or carries no root marker at
the claim. Each path is absolute, as the platform writes it. The paths are decided **once, at the claim**,
where the command that may read them is composed and the attempt's provenance is written, and the envelope
names them as the claim decided them — for a profile launch and for a role's own entry command alike. So
the files a role is told of are the files the claim decided and its rules were composed from: a file that got
no rule, and every file of a role run by its own entry command, which is composed no rules at all, is named all
the same, with no rule to read it by — under a profile its read is refused — and the envelope never knows better
than what the provenance recorded.

`resumption` is null on every run that does not follow a wait, and on the first run of an item created already
waiting — a responder the respond step wrote waiting on the read of its thread ([inbound sync](inbound-sync.md) §1) —
since no attempt waited: that run reads what it waited on by the identifiers its context carries, with
`jason workitem get`. A run that does follow a wait — the item's last run
ended with `workitem.wait`, and the item's latest wait that an attempt ended in was released rather than closed some
other way, a wait the claim held it in at a limit being passed over — is told about that wait here, composed **at the
claim** from the rows the wait left: `previous_attempt_id` is the attempt that waited, `resumption_number`
counts the item's released waits that an attempt ended in, up to this one, `waited_since` is when the wait opened, and
`woke_because` is the reason the wait's release recorded, verbatim — `children_ended`, `moment_reached`,
`wait_ceiling` or `resumed_by_actor`. A role's wait is released for one of those four: neither `block_cleared`
nor `block_trial` occurs for a role's wait, since a provider block holds provider operations, and their relaunch
carries no `resumption`. `items` are the items
the wait named, in the order it named them, each with its status and its result. The results travel
within 1 MiB per wait: they are given in that order while the running size of their JSON fits, and from
the first one that does not fit, every later result is left out and marked `result_omitted` — the id
and the status are always there, and `workitem.get` reads a result left out. The first attempt the claim
hands out after a release is recorded on the wait as the one it resumed; an attempt that retries after
it is told the same, because the wait it follows is still the last one.

A role reads `resumption` the way it reads the rest of the envelope, because it is part of it: no new
channel, no second message. The command that runs `ai_role` work writes the whole envelope to the
child's standard input and composes no prompt from its parts, on every host. On a host that reads its
prompt from standard input — every host of that matrix does, and its `Takes the brief` column says how — that
standard input is the prompt, so the role's first input says whether it was woken and why. On every host
`resumption` reaches the role inside that envelope, beside every other field, never in a prompt Jason composed. A
host that reads a stream of lines rather than plain text is given the same envelope as the content of one line of
that stream, by its own framing, and nothing else is written: the host's section of
[execution-profiles.md](execution-profiles.md) §12 says which.

Every change to this envelope is additive — `cli_command` took it to version 2, `role_memory` to
version 3, `memory_files`, with `role_memory`'s `project_id`, to version 4, and `resumption` to
version 5 — so a host written against an earlier version keeps working: it reads the fields it knows
and ignores the rest.

### Finding the API

The envelope does not carry the base URL or the token. The child reads them from the **descriptor
file** it names — and it must read that file **before every call**, not once at startup. The token is
per runtime instance: a child that outlives a restart finds the new instance and the new token by
re-reading, and finishes the work it was launched for. A child that reports through the `jason` CLI
gets this for free, because that is what the CLI does.

## Reporting back

Four operations, all fenced by the attempt id. An attempt id that is unknown, belongs to another item,
or is no longer the item's running attempt is answered with `409 stale_attempt`, `retryable: false` —
one code, because the remedy is always the same: stop working on it. All four record that the executor
is alive.

| Operation | Request | Answers |
|---|---|---|
| `workitem.heartbeat` | `work_item_id`, `attempt_id` | `{work_item_id, attempt_id, lock_until, heartbeat_due_by}` — `heartbeat_due_by` is null when the item has no heartbeat |
| `workitem.set_result` | `work_item_id`, `attempt_id`, `result` (any JSON) | the work item |
| `workitem.complete` | `work_item_id`, `attempt_id`, `status: succeeded\|failed`, `result?`, `error?: {code, message, details?}` | the work item |
| `workitem.wait` | `work_item_id`, `attempt_id`, `on?: [wi_…]` (at most 100), `mode?: all\|any` (`all` when items are named), `until?`, `reason?` | the work item, `waiting`, with its `wait` block |

**An answer is held to the shape that was asked for.** Where the item declares a `result_format`, a
completion whose result does not satisfy it is refused with `result_invalid` (400, not retryable) and
the item stays `processing`, so the executor still holds its attempt and can answer again; one that
gives up instead ends as `executor_exited`. A pattern of that schema that did not finish matching the
result, twice, refuses nothing: the completion is answered `validation_timed_out`, 503, `retryable: true`,
nothing is recorded, and the executor still holds its attempt to send it again. What is judged is the result that will stand — the one
the completion carries, or the one an earlier `set_result` left — so an executor cannot check in with
a paragraph and then finish empty-handed. A **failed** completion is never judged against the shape:
a failure has an error to report and no result to measure.

`result_format` was an opaque value in earlier versions and is now a schema, validated when the item
is written. A shape nothing can check is a request an executor may answer with persuasion.

**A wait ends the attempt without finishing the item.** It names the items to wait for, a moment, or
both, and the item is `waiting` until the first of them comes (see Lifecycle). It is refused in one
order, and a refused wait changes nothing: first the fence, `stale_attempt`; then a manager check-in,
which ends by finishing, and work that is not a role, with `wait_not_allowed`; then the items it names —
no more than 100, each raised by this item's own attempts, in the same campaign, and none of them a
decision, or `wait_target_invalid`, whose message names the first item refused and why; then a moment already past, refused as the caller's mistake with
`validation_failed` on `until`; and last the settings and the bounds they set — `wait_too_long`, then
`wait_limit_reached`. A wait of the wrong shape — neither
items nor a moment, a mode with no items, a blank id, or an item named twice — is `validation_failed`
before any of these. Ending a wait is
`workitem.resume`, which is not an executor's operation: any named actor may call it — `actor` is
required, and a request without one is `validation_failed` — with a reason that the `workitem_resumed`
line keeps. It releases the wait and returns the item to `created` in one
write, through the same routine as the scan's resume step.

A waiting item is read with its `wait` block: `attempt_id`, `on` — each named item's `work_item_id` and
`status`, in the order the wait named them — `mode`, `until`, `deadline` and `opened_at`. A listing
carries the same block with `on_count` and `ended_count` — how many items the wait names, and how many
of them have ended — in place of `on`, so a page stays small; `workitem.get` has the
items. Any item that is not waiting has no block. Both shapes carry `blocked_by`: null for a role's wait, and for
an item parked on a provider block the block's `provider_block_id`, its `scope` — `account` or `sender` — and its
`reason`, with `on` empty and `mode`, `until` and `deadline` null. The link where a person clears the block never
travels there; it is on the item's `last_error.block`: the error of the attempt that met the block, its trace left on
the attempt, or, for an item the claim parked, which no attempt met the block for, the block as the runtime keeps it,
under the code `provider_blocked` and the class `blocked`.

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
lease sweep, the claim, the handler, and these four operations — reads them the same way, so a child
goes on heartbeating and reporting while a person repairs the file. Only a runtime that has never read
settings the validator accepted has nothing to work from; `workitem.heartbeat`, `workitem.wait` and a
**failed** `workitem.complete` then answer `503 settings_unreadable`, `retryable: true`, and the whole remedy is
to repair the file and call again. A successful `workitem.complete` and `workitem.cancel` read no
settings at all and are never refused for this reason.

### Error codes

| Code | Where | Meaning |
|---|---|---|
| `work_item_not_found` | 404 | no item with that id |
| `workitem_terminal` | 409 | the item is finished; finished items are not changed |
| `stale_attempt` | 409 | the attempt id is not the item's current running attempt |
| `contact_not_member` | 409 | the contact is not an active member of the campaign |
| `campaign_archived` | 409 | an archived campaign takes no new work, but a search's stop |
| `idempotency_key_reused` | 409 | a different request already created something under this key; it is final, and the message names what the key belongs to |
| `result_too_large` | 400 | the result exceeds 1 MiB |
| `result_invalid` | 400 | the result does not satisfy the `result_format` the item declared |
| `role_exists` | 409 | a role of that name already exists |
| `wait_not_allowed` | 400 | this item may not wait: a manager check-in ends by finishing, and only a role's work waits |
| `wait_target_invalid` | 400 | an item named is not one this item's own attempts raised in its campaign, is a decision, or the wait names more than 100 |
| `wait_too_long` | 400 | the moment is later than `Dispatcher:AiRole:MaxWaitSeconds` from now |
| `wait_limit_reached` | 409 | the item has already waited `Dispatcher:AiRole:MaxWaits` times; finish instead |
| `workitem_not_waiting` | 409 | only a waiting item can be resumed; the message says where the item is |
| `block_not_found` | 404 | no provider block with that id |
| `block_not_open` | 409 | only an open block can be cleared; this one was cleared already, by a person or by a call that got past it |
| `block_not_human` | 400 | a block is cleared on a person's word; a role or an attempt cannot clear one |
| `actor_required` | 400 | the actor names no person: a block clear, like a decision, records whose word it is |
| `settings_unreadable` | 503 | the settings are invalid and none have validated since the runtime started; repair the file and call again |
| `internal_error` | 500 | the runtime failed to perform the operation, and `retryable: true` — except where what failed is a value stored in the database that this runtime cannot read, written by another version of Jason or changed outside it: then the message names its column, never its value, and `retryable: false`, since every read of it fails the same way until it is restored or repaired |
| `validation_timed_out` | 503, and attempt | a pattern a value is measured against did not finish matching in time, twice, so nothing was decided and nothing changed: send the request again. At the claim the attempt ends `transient`; over a plugin's answer, `ambiguous` |
| `lease_expired` | attempt | the whole budget of the attempt was spent |
| `heartbeat_missed` | attempt | nothing was heard for twice the heartbeat interval |
| `executor_exited` | attempt | the child ended without reporting |
| `executor_launch_failed` | attempt | the entry command could not be started at all |
| `role_not_launchable` | attempt | the role has no entry command and no default is configured |
| `launches_spent` | wait | the item's campaign has started `Launches:MaxPerDay:<role>` runs of its role since 00:00Z; the claim holds it, with no attempt, until the next 00:00Z (§The claim rule) |
| `responder_launches_spent` | wait | the same hold as 0.30.0 wrote it, for a responder; nothing writes it now and an item that carries it reads as held |
| `profile_revision_refused` | attempt | the profile's current revision holds a word this build's rules for its host refuse |
| the twenty-eight pre-flight codes | attempt | a `provider_op` item the claim refused, `no_route` among them — see below |

## Provider operations

A `provider_op` item is a kind of work item, not a second execution engine: it is claimed under the
same lease, runs in the same handler pool, and ends through the same routine. Four things about it
differ from agent work.

**Its arguments live under one reserved context key.** `context.input` carries the operation's
arguments and nothing else in the context is an argument. The key is matched by its exact spelling: `Input` is a
context key like any other, and what it holds is not an argument. An item that carries `Input` alone, for an
operation that declares required arguments, carries no arguments at all, so `workitem.create` refuses it with
`validation_failed` and a `required` detail on `context.input`. The runtime **composes** what the plugin
receives from the item — the arguments, the contact cut to the projection the operation declares, the
campaign, the identifiers that plugin itself pinned, and the work item's own id as the idempotency key; for a read or a
take-back of a send, the send the arguments name with its reference, for a status read or a stop of a search, the search
with its reference, and for a search, the audience's `definition` at the root
([docs/contracts/README.md](contracts/README.md)) — and validates the whole composed document against the operation's
input schema at the claim, and a definition, which is the provider's own shape, against the schema the routed plugin
declares for one. A plugin never sees a work item's context. A search the claim does not refuse runs under one of the
campaign's audiences, which the claim finds or makes and records on the item under `context.audience`
([campaign manager](campaign-manager.md) §10).

**Everything that can stop it is decided at the claim, before a child process exists.** Twenty-eight checks, in this order, and the first that answers is the one that decides. A check's number is its name and never moves, so the twenty-seventh, added after the rest, is asked after the twenty-third, and the twenty-eighth, added after it, right after the tenth:

```text
operation_unknown → no_route → plugin_not_loaded → plugin_unavailable → plugin_operation_unsupported →
contract_incompatible → binding_invalid → contact_required → no_channel_value → suppressed → opted_out →
contact_not_linked → send_unknown → send_reference_absent → send_route_mismatch → search_unknown →
search_reference_absent → search_route_mismatch → audience_unknown → input_invalid → definition_invalid →
argument_unsupported → template_not_offered → provider_answer_lost → thread_route_mismatch →
search_unfinished → approval_required → provider_blocked
```

The first twenty-five are refusals and all of them are final: twenty-two are `permanent`, `input_invalid` and
`definition_invalid` are `validation`, because they are the ones a planner can fix by editing the item, and `provider_answer_lost` is
`ambiguous`, because it refuses to repeat a send, or a metered act settled by a recovery read, whose earlier attempt
lost its answer, either under a plugin whose manifest does not say it settles the send by the work item's key, or where
the routed package — by its id and digest for a send, by its id alone for a metered act — or its binding is not the one
the unsettled attempt ran under, or the input
composed now is not the one it was composed with, and so keeps what that attempt left unknown. The attempt is kept with its
context snapshot either way — a silent skip would leave the item looking claimable forever — and
nothing ever falls back to another plugin. The twenty-seventh, `thread_route_mismatch`, is the last of those refusals
to be asked: work on a thread the campaign saw at another plugin than the item is routed to, refused before
the three that wait or park, so nobody is asked to approve, and no block holds, work that could only be refused.
`opted_out`, added after it, is asked right after `suppressed`, under the register's own gate and over the same
operations: the register is by value, and this lock is by the person, refusing work about a contact for whom an opt-out
verdict stands in any campaign, whatever value the work would reach them at — one they gained after the verdict
included. It is asked only where the register found no entry, so the register answers first wherever it can. The
verdict itself registers every value the contact had, in the same save, by the runtime's reserved actor
`system:opt_out`, with the verdict as the reason; the lock is lifted only by a person's own later verdict on the same
reply in that verdict's campaign, under their name, outside any run, stating a nature other than `opt_out` — never by a
role's or a run's, never by a person named from inside a run, never by a carried answer alone, and never by
`suppression.remove` ([runtime API](runtime-api.md), `reply.record`).
The twenty-fourth is not a refusal: `search_unfinished` keeps a search
waiting behind another search of its campaign that has not ended, with no attempt, and asks again a search cadence
later, the held cadence where the provider holds that search, so a campaign runs one search at a time and nobody is asked to approve a spend that cannot start now. The
twenty-fifth is not a refusal either: `approval_required` parks the
item for a person (§Approvals above), and it is asked after every refusal so that nobody is asked to approve work
that would have been refused anyway. The twenty-sixth, `provider_blocked`, is not a refusal either: it parks work an
open provider block holds — the operations whose document says `dispatch.under_provider_block: waits`, nothing else —
as `waiting`, with no attempt, until the block clears or a trial gets past it (§Provider blocks below). [docs/routing.md](routing.md) says what each one means and what to
change.

Three of those checks measure a document against a schema — the binding against the plugin's (7), the
composed input against the operation's (19), the composed definition against the plugin's (20) — and one answer they
can give is not a refusal. Where a
pattern of that schema did not finish matching, twice, nothing was found wrong and nothing has run, so
the attempt ends `validation_timed_out`, `transient`, and the dispatcher retries it under the ordinary
retry rule — never `input_invalid` or `binding_invalid`, nor `definition_invalid`, which would send a planner to edit an
item that may be right.

**A failure's class decides the retry, not the code table.** A provider attempt ends with one of five
classes: `transient` comes back, `permanent` and `validation` are final, `blocked` — the provider refused the
account or a sender and did nothing — parks work a block holds on that block, with the attempt `blocked` and never
counted, and is final with its block kept on the item's `last_error` for any other operation (§Provider blocks
below), and `ambiguous` — the
provider may already have acted — is repeated only where the operation's own contract says a repeat is
`safe` or is answerable `after_recovery_read` — and where that recovery read rests on the work item's key
(`recovery_read.by_key`, the two sends), only under a package whose manifest lists the operation under `settles_by_key`,
read for the package the attempt ran under, so a package replaced since has the end final; and while the item carries an
unsettled send — an earlier attempt of it started and ended ambiguous, whatever ended after it — a repeat is claimed
only under the package, by its id and digest, and the binding the latest such attempt ran under, and with the input
that attempt was composed with — its arguments, and the whole composed input by the `input_digest` its provenance keeps
(below): a route, a binding or a package changed in between ends it `provider_answer_lost` at the claim, since the key
settles a send only at the provider and the account that took it, and so does an item edited since, or a person or a
campaign whose part of the input changed since, since the key settles only the request it went with and a provider
holding it answers a repeat with the earlier send. A metered act settled by a recovery read — `audience.search`,
`campaign.enroll`, `campaign.activate` — is held the same way but for the package's digest: its recovery read finds
the earlier act at the account whichever version of the package made it, so an update of the package in between lets
the repeat through.
While the item carries one, every refusal of the claim from check 2 to check 22 keeps its code, and so does check 27, and is recorded
`ambiguous` and final, its message saying that an earlier attempt of the item may have reached the person
(`docs/routing.md`). An answer that arrived and does not satisfy the
operation's output schema is `result_invalid`: ambiguous, and never repeated; so is a failure whose code
the operation does not declare, or declares under another class. An answer a pattern of
that schema could not finish measuring, twice, is a lost answer instead: `validation_timed_out`,
ambiguous, and the operation's own repeat rule decides, because the plugin has run and its effect may
have landed — calling it transient would run a completed write again blind. The unmeasured answer is
kept on the attempt's trace for a person, and nothing in it is pinned.

**What ran is pinned to the attempt.** Every provider attempt carries a `provenance` record, written at
the claim from the decision that was just made and completed when the invocation ends: the plugin, its
version and content digest, the operation and contract version, the plugin and route snapshot ids, the
route scope, the binding's identity (a hash, never the value), the invocation id, what the run cost,
the identifiers the plugin returned, whether the provider found or created what they name where the plugin said
(`external_id_origins`), and — after a `result_invalid` — the answer that was refused. Nothing rewrites it afterwards.
Where the claim let the attempt run, it also names the input it composed: `input_digest` is the canonical `sha256:`
of that document, keys in ordinal order, with everything in the composed input but the campaign's status — the
arguments, the send, the person with the projected fields, the consumed channel's value and the pins, the campaign
with its name and pins, and the work item's key — so a pause and a resume change nothing in it. A digest names the
input and carries none of it, no address included. A repeat after a lost answer is made only where the input composed
for it has the same name once the pins the compared attempt and every later attempt of the item recorded, from the
answers they ended with, are left out, since the input it was composed with could not carry them and every later
attempt ran only because its claim found it the same request; an attempt claimed before attempts named their input
has no `input_digest`, and its repeat is compared on its arguments alone. `jason workitem get <id> --human` prints
the record as a block per attempt.

**A later attempt is told since when an earlier one is unsettled.** The plugin's `context` carries
`unsettled_since`: the moment the earliest earlier attempt of the item that may have acted was claimed. It is absent
where none did, and a plugin reads absent and null alike. An attempt may have acted when it ended `ambiguous`: the plugin said so, or no answer of the protocol came
back — a timeout, a crash, a host failure, a lease that ran out or a restart. A provider's own answer of any other
class settled its attempt. The claim reads the moment from the item's attempts and writes it beside `attempt_number`.
It is never in the composed input, so what a person approved does not change with it. A plugin whose provider keeps an
idempotency key for a bounded time counts that time from it ([docs/plugins.md](plugins.md) §7).

**A later attempt is told which send an earlier one may have made.** Where the operation declares a `send` identifier
for the work item itself, the plugin's `context` also carries `unsettled_reference`: the reference the latest earlier
attempt that may have acted returned for it, at or after `unsettled_since`. It is absent where none returned one, and a
plugin reads absent and null alike. The claim reads it from the identifiers each attempt's provenance kept as returned,
and it is never in the composed input either. It is a provider identifier and nothing else. A plugin reads the send by
it before it repeats anything, since a read never makes a send twice ([docs/plugins.md](plugins.md) §7).

**A send ends when the provider has taken it.** Its item succeeds `sent`, where the provider sent it inside the call,
or `accepted`, where it took it for now or for the moment `send_after` names; the fact that an accepted send went is
learned later, never from this item.

## Provider blocks

A `blocked` failure is the provider saying no to the account, or to one sending account under it, rather than to
the work: its money or plan, an allowance it counts, a credential it no longer accepts, a channel disconnected or
paused. The provider did nothing, and nothing the item says would change its answer, so the runtime keeps the block —
per plugin and binding identity, the account's or one sender's — and holds the work it concerns until it clears.

**What a block holds.** A block holds the work of every operation whose document says
`dispatch.under_provider_block: waits` ([the contracts](contracts/README.md)): every `reach: act` operation, which a
document cannot let run through a block, and beside them `campaign.pause` and `optout.record`. Every other `control`
operation and every read says `runs`, goes through an open block and fails on its own, if it fails at all: a block is
about what the provider refuses to let reach a person or protect them, a control operation may still succeed, and a
read tells the truth. A take-back of a send, `message.cancel`, is one of them: the moment of the send it takes back passes
while it would wait for a trial, so it runs, and its `blocked` answer ends it, final and never repeated behind the
block, a protective action that failed ([notifications.md](notifications.md)). An account's block covers every sending account under it. A sender's block holds the work whose
composed input names that sender at `/args/sender/external_id`; an item that met a sender's block and names no
sender parks alone, on that block.

**Two ways in, one wait.** An attempt of held work that answers `blocked` ends `blocked` — over, and never counted
toward `max_attempts` — and its item waits on the block the answer names, which that attempt opens where none of its
grain is open and which is seen again where one is: counted, with the latest reason, link and `clears_at`. And the
claim's twenty-sixth check parks held work an open block already holds, before an attempt exists
([docs/routing.md](routing.md) §7). Either way the item moves to `waiting` on the same wait row a role's wait uses,
naming the block with no `mode`, `until` or `deadline`; the item's `due_at` bounds it, an item past its due date
expires, and cancelling the item closes the wait. A person's approval stays as they gave it: approved work the block
holds parks after its approval and runs under that decision when it is let through — unless what it would run
changed meanwhile, a pin the refused attempt learned among such changes, and then the person decides once more
(§Approvals).

**The trial.** A block is tried rather than waited out. Its next trial is due at the moment the provider said it
clears by itself where that is ahead, a day at most — so a block that says it clears next month is still tried daily —
and otherwise at the cadence, `Dispatcher:Blocks:TrialSeconds` after it was opened or last seen. When it is due, the
scan's resume step lets one item the block holds back into the queue as its trial, releasing its wait with
`block_trial`: acts first; where none can run, one held control, whose answer decides nothing but `blocked`, since
only an act can say whether the provider still refuses what reaches a person. Among them, the highest priority, then
the oldest, and never a read, since a read is never held. One trial is in flight per block, from its release until it ends: while it is claimed, running or awaiting
a decision, or in the queue where the claim could take it. A trial left in the queue of a campaign paused since, which
the claim would not take, holds nothing, and the next trial due is another item the block holds; the claim reads the
same rule. A trial that waits for a person's decision holds the block's one trial until they decide: approved, it runs
as the trial; refused, it ends, leaves the live set, and the next trial due is another item the block holds. Approved
work let through as a trial asks the person again where the attempt the block refused learned a pin, since a learned
pin is a change to the input the person approved (§Approvals, what is approved is a subject). A block that is due but
holds nothing that could run now lets the next held item the claim meets through as its trial instead. What a trial
of `reach: act` work answers decides the block:

- **it succeeded, or failed `permanent` or `validation`** — the provider judged the work, so the call got past the
  block: the block is cleared by that attempt, and the next resume step releases every wait on it with
  `block_cleared`. Any call of held `act` work the provider judged on the work clears its open block, a trial or not,
  and a trial clears the block that let it through whether or not its input names a sender;
- **it answered `blocked` again** — the trial parks once more on the block, which is seen again and its next trial
  put off; nobody is told twice;
- **anything else** — `transient`, `ambiguous`, an answer outside the contract, the host's own ending, or a code
  reserved beside every operation, which no provider judgment of the work stands behind (`provider_input_invalid`, which
  a package answers before any call, [the input a plugin receives](contracts/README.md#the-input-a-plugin-receives);
  `capability_not_offered`, the provider's answer that it does not offer what was asked yet, [a capability the provider
  does not offer yet](contracts/README.md#a-capability-the-provider-does-not-offer-yet)) — says nothing about the block:
  it stays open, its next trial is due a cadence from now, and the item follows its own retry rule.

A held control's answer clears no block, a trial or not: its success, or the provider's judgement of it, says nothing
about the block, which stays open with its next trial a cadence from now where the control was the trial; its
`blocked` parks it once more, as above.

While an account's block of a binding is open, the blocks of its senders hold no trial of their own and an item that
names a blocked sender parks on the account's block: the account's trial answers first, and it is drawn from the items
its senders' blocks hold as well as its own. Two things follow. When the account's block clears, a sender's block
still open gets its own trial from then on. And an item released by that clear whose sender is still blocked parks
again at its next claim, on the sender's block — with no new `provider_blocked` line where its campaign was already
told about that block.

**What is said.** The campaign is told once per block: `provider_blocked`, keyed by the block's id, written when the
first of its items parks on that block — the block's id, scope, reason and `clears_at`, the item that parked, and for
a sender's block the sender's identifier. It is a default manager trigger ([campaign manager](campaign-manager.md)):
what reaches people has stopped, and whether to tell somebody or change course is the manager's. When the block
clears, every campaign that was told is told so, once, `provider_block_cleared`, naming the attempt that got past it —
the first, where two got past it at once — or, where a person cleared it, written as theirs, with their reason and no
attempt; that line is not a trigger, since the work runs again by itself. Each parked
item writes its own `workitem_waiting`, whose `blocked_by` names the block, and its release writes `workitem_resumed`
with `block_cleared` or `block_trial`. The link where a person clears a block is kept on the block, which `block.get` and `block.list` answer, and on the
`last_error.block` of every item it parks, where a person reads it, and nowhere else: never in a log, a journal line,
a notification or a `wait` block. A person is pointed at the block too, once, by the notification that counts it
([notifications.md](notifications.md)): how many blocks are open and how many work items they hold, and nothing else.

**A person's clear.** A block also clears on a person's word. A clear is a person's statement that the cause is gone,
not a mute: `block.clear` clears an open block now, under the actor named and by no attempt, and tells every campaign
that was told, once, as above. The actor is a person with an id, as for an approval: a role or an attempt is refused
with `block_not_human` — an agent cannot clear a block — and an absent actor, or one with no id, with `actor_required`.
The block's read, the campaigns told and the write are one transaction that holds the write lock from the read, so no
campaign is told the block holds its work, and no trial is set on it, between what the clear reads and what it writes.
It releases nothing itself: the next resume step releases every wait on the block with `block_cleared`, as it does
after a call got past it, at most 500 a scan. Where the provider refuses again, the account or sender is
blocked again as a new block with a new id, its campaigns are told again and the manager looks again. Clearing an
account's block while a sender's block under it stays open follows the rule above: what names that sender parks again
at its next claim, on the sender's block, which gets its own trial from then on. A block cleared already — by a person,
or by a call that got past it first — is refused with `block_not_open`, and that clear stands; a call that read the block
open before a person's clear and writes after it writes nothing, and the person's clear stands.

| Operation | Request | Answers |
|---|---|---|
| `block.list` | `statuses?: [open\|cleared]` (`[open]` when absent), `plugin_id?`, `limit?`, `cursor?` | a page of blocks, newest opened first |
| `block.get` | `block_id` | the block, or `block_not_found` |
| `block.clear` | `block_id`, `actor`, `reason?` | the block, `cleared`, or `block_not_found`, `block_not_open`, `block_not_human` or `actor_required` |

A block answers its id, `plugin_id`, `binding_identity`, `scope`, `sender_external_id`, `reason`, `link`, `clears_at`,
`status`, `opened_at`, `last_seen_at`, `seen_count`, `next_trial_at`, `trial_work_item_id`, `opened_by_attempt_id`,
`held_count` — the open waits parked on this block itself; an account's count leaves out what its senders' blocks
hold — and `cleared_at` with `cleared_by_attempt_id` or `cleared_by_actor`.
`jason workitem list --block <id>` names the items it holds.

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
whether this runtime has a dispatch loop at all. A live edit never stops the runtime: a refused edit keeps the
last good settings and is logged by path and type, and a start with a malformed or mistyped file still fails,
saying where — on stderr under `jason runtime run`, and after "It said:" in what `jason runtime start` answers:
the line and byte of a malformed file, the setting's path and expected type for a
mistyped one.

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Dispatcher:Enabled` | `true` | — | whether this runtime dispatches at all |
| `Dispatcher:TickSeconds` | `10` | 1..3600 | how often a scan runs |
| `Dispatcher:MaxParallel` | `4` | 1..64 | how many attempts run at once (applied at start) |
| `Dispatcher:DrainSeconds` | `10` | 0..20 | how long a shutdown waits for running handlers |
| `Dispatcher:RetryDelaySeconds` | `60` | 0..86400 | multiplied by the failure count to set `retry_after`; 0 retries at the next scan |
| `Dispatcher:ExitGraceSeconds` | `30` | 0..600 | how long a process may linger after its attempt was completed through the API before the dispatcher stops it |
| `Dispatcher:EventsCadenceSeconds` | `600` | 60..86400 | how long after one read of an account's events starts the next is due ([docs/events.md](events.md)) |
| `Dispatcher:SearchCadenceSeconds` | `300` | 60..86400 | how long after the dispatcher's last read of a search's people the next is due, and after the search began, its first; doubled for each of its reads in a row that failed, never past a day ([docs/campaign-manager.md](campaign-manager.md) §10) |
| `Dispatcher:SearchStallSeconds` | `3600` | 60..604800 | how long the reads of a search the provider does not hold may land nobody before the dispatcher calls it stalled and stops reading it; refused below `Dispatcher:SearchCadenceSeconds`, since every search whose first read lands nobody would be stalled at once ([docs/campaign-manager.md](campaign-manager.md) §10) |
| `Dispatcher:SearchHeldCadenceSeconds` | `3600` | 60..86400 | how long after the dispatcher's last read of a search the provider holds — `paused`, `paused_for_credits`, `paused_for_limit` — the next is due: it spends nothing until a person acts at the provider, and the ordinary cadence comes back once it moves ([docs/campaign-manager.md](campaign-manager.md) §10) |
| `Dispatcher:SearchHeldStallSeconds` | `604800` | 60..604800 | how long a search may stay held at the provider before the dispatcher calls it stalled, its state named, and stops reading it, so no search is read for ever; refused below `Dispatcher:SearchHeldCadenceSeconds` ([docs/campaign-manager.md](campaign-manager.md) §10) |
| `Dispatcher:Blocks:TrialSeconds` | `900` | 60..86400 | how long after a provider block was opened or last seen, or after a trial that said nothing about it, its next trial is due (§Provider blocks) |
| `Dispatcher:AiRole:TimeoutSeconds` | `3600` | 30..86400 | the budget of one `ai_role` attempt |
| `Dispatcher:AiRole:HeartbeatSeconds` | `120` | 0, or 10..3600 | how often an `ai_role` executor must prove it is alive |
| `Dispatcher:AiRole:MaxAttempts` | `3` | 1..10 | how many failures an `ai_role` item is worth |
| `Dispatcher:AiRole:MaxWaitSeconds` | `1209600` | 60..2592000 | the deadline of a wait that names no moment, counted from when it opens (fourteen days), and the latest moment a wait may name: a later one is refused, `wait_too_long`, never moved to the ceiling |
| `Dispatcher:AiRole:MaxWaits` | `20` | 1..100 | how many times one `ai_role` item may wait; the next wait is refused, `wait_limit_reached`, and the run has to finish or fail instead |
| `Dispatcher:ProviderOp:TimeoutSeconds` | `600` | 30..86400 | the budget of one `provider_op` attempt; never below the slowest published operation's `timeout_ms` plus `Plugins:Invoker:KillGraceMs` |
| `Dispatcher:ProviderOp:HeartbeatSeconds` | `0` | 0, or 10..3600 | 0: a provider call is short, the lease suffices |
| `Dispatcher:ProviderOp:MaxAttempts` | `3` | 1..10 | how many failures a `provider_op` item is worth |
| `Roles:DefaultEntryCommand` | `[]` | — | the command used for any role without one of its own |
| `Responder:OnReply` | `true` | — | whether a reply about a campaign's own thread wakes its responder; off, the respond step still moves past the lines and creates nothing |
| `Responder:Priority` | `50` | -1000..1000 | the priority of the thread's read and the responder; the inbound read's `conversation.list` items sit ten above it |
| `Responder:TimeoutSeconds` | `900` | 30..86400 | how long one responder run may take |
| `Responder:MaxAttempts` | `1` | 1..10 | how many failures a responder is worth; a second run would draft a second answer |
| `Responder:MaxAgeSeconds` | `259200` | 3600..2592000 | how old a reply may be, by the thread's last activity, and still wake a responder (three days) |
| `Launches:MaxPerDay:<role>` | `responder: 25` | 1..1000 | how many runs of a role one campaign may start in a UTC day, counted from 00:00Z; a role the map does not name has no limit, and the responder's number can be changed and not removed, a thousand being as high as it goes; the claim holds a role at its limit until the next 00:00Z (§The claim rule) |
| `Sending:AutomaticMaxPerDay` | `100` | 1..1000 | the most sends the automatic mode releases from one campaign in a UTC day, counted since 00:00Z by the releases it wrote, whatever became of each; at the ceiling the next send it would release waits for a person, even where a standing approval covers it (§The claim rule); there is no 0 and no unlimited: a person who wants no automatic sends sets the mode to `every_message` |

`Responder:MaxLaunchesPerDay`, the key the responder's limit had through 0.30.0, is refused wherever the configuration finds
it: a start stops and says where the number went, nothing is carried over for you, and a live edit that names it keeps
the last good limits, logged by the path and never by what is written there.

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
Dispatcher: running | tick 10 s | 2/4 attempts | last scan 2026-09-14 12:00:03 UTC | 0 summoned
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

Work the runtime creates itself is no longer on this list. The dispatcher creates a reply's read and responder as
root work ([docs/inbound-sync.md](inbound-sync.md) §1), and a manager's check-in,
which names its cause explicitly: lineage comes from the attempt the causing line names, or from the
item where it names one and no attempt. Root work is what is left — a review the cadence asked for, and
a review caused by a line that names neither, which a report about a campaign rather than about a work
item is. [docs/campaign-manager.md](campaign-manager.md) §7 has the three cases.

- **Approvals beyond one decision about one item and a standing approval for a kind of reply.** There are no bulk
  decisions and no anomaly rules. A parked approval and a raised question are notified
  as a pointer ([notifications.md](notifications.md)); pull is still the floor: a person finds out what is
  waiting, and decides, by asking (`jason approval list`). What exists is the gate itself, below.
- **Session resume.** A host that is interrupted is retried from the start, not nudged to continue.
  The session id is minted per attempt and recorded where the host takes one, which is what a nudge would
  need, but nothing uses it yet.
- **Anything above one hop.** Lineage is materialized once, from the run that created the item. There
  is no resolver walking a graph, and no branch selection.
- **Backoff.** The retry delay is linear in the failure count; the tick and the one-item-per-campaign
  rule do the rest of the pacing.
- **Work-directory retention.** Nothing is ever cleaned up.
