# The campaign-manager loop

Something happens to a campaign, or enough time passes without anything happening, and a manager is
launched to look at it. This document is the contract for that loop: what wakes a manager, how often
one is woken anyway, what it is told, and what all of that costs.

How an `ai_role` item is claimed, launched and ended is
[docs/work-execution.md](work-execution.md); which agent host runs it and what that session can reach
is [docs/execution-profiles.md](execution-profiles.md). The manager is a role like any other, and
nothing here is a second kind of work.

## 1. The check-in

**A check-in is the work item the runtime creates to have a campaign reviewed.** One word, used
everywhere: not a review item, not a manager task.

It is an ordinary `ai_role` item with `role: manager`, created by the runtime itself
(`created_by: {type: system, id: dispatcher}`). It is claimed by the same scan as everything else,
resolved to an execution profile the same way, held to the same lease, and ended through the same
routine. What makes it a check-in is who created it and what it is for, not any special handling.

**At most one is open per campaign.** Open means anything that is not finished, including waiting for
a person's approval. A user who has been away for a week comes back to one review, not a pile of
them — and this is a rule the database keeps, not one the summon merely intends: a unique index over
the open ones. Two writers both looking first would both see none and both insert.

**Only `active` campaigns are reviewed.** A draft campaign is a backlog, a paused one was stopped on
purpose, and an archived one is over.

## 2. What wakes a manager

The chronicle is the event log. Every fact worth reacting to is already written there, in the same
transaction as the change that caused it, by code no caller can bypass — so there is no second event
table and no second write path.

A scan reads a campaign's chronicle above a **watermark** and looks at one thing: the kind of each
line. These kinds summon a review:

| Kind | Why |
|---|---|
| `workitem_failed` | Failed work is the manager's inbox. A failure nobody interprets is the campaign quietly stopping. |
| `approval_rejected` | A person said no to a proposed effect. The decision is made; what is missing is the strategy that follows it. |
| `external_effect_reported` | Something happened to this campaign that the runtime did not do. |
| `decision_answered` | A person answered a question a role could not answer for itself. The role that asked has already ended, so this is what releases the work that follows. |

**And what is deliberately not in the list.** `workitem_succeeded` is out: a review after every
successful step is cost without judgment, and the work that needs looking at is the work that did not
go well. `workitem_expired` and `workitem_cancelled` are out: the cadence catches them soon enough.
Everything about profiles, plugins, routes and roles is operator maintenance, not campaign meaning.

**The rules are settings, never a skill.** `Manager:Triggers` is a list of chronicle kinds, and every
one of them must be a kind **the runtime writes itself**: a kind that never appears could never fire,
and a kind a caller can append through `journal.append` would let anything that can write a line
summon a manager by naming it. A kind outside that vocabulary is a refusal to start, not a rule that
quietly does nothing.

**The dispatcher reads no meaning.** It reads a kind, some identifiers and an actor. It never reads a
line's `old`, `new`, `key` or `reason`, and never a work item's context. The promotion from
"something happened" to "somebody should think about this" is a table lookup; the thinking is the
manager's.

**A manager never summons a manager.** A check-in whose host is missing fails at pre-flight like any
other item, and that failure is a line of exactly the kind that summons a review. So lines about a
check-in, and lines written by a check-in's attempt, are passed over. A failed review is caught by
the cadence and by nothing else, on purpose.

**Except a person's.** A line whose actor is a **person** is never passed over, whatever it is about.
The rule above exists for one thing — a review summoning its own successor for ever — and somebody
acting on a review's work is not that; it is the one outcome a review is meant to lead to. It is also
what makes escalation work at all: a person answering a review's own question writes a line about that
review's attempt, and that answer is exactly what should release the next review.

What the runtime cannot do here is tell a person from a process holding that person's own command
line. That is the limit an approval already has, and it is the operator's trust to give; the guarantee
is that nothing inside the runtime can give it. The verb that writes `decision_answered` refuses every
caller that does not claim to be a person, and `journal.append` refuses the kind outright.

## 3. The watermark, and what a review accounts for

The watermark is how far a campaign's chronicle has been accounted for.

- Lines that summon nothing are **consumed anyway**: read, passed over, done with.
- Lines that do summon a review are consumed **by that review**, along with everything else read in
  the same pass — the manager reads the whole chronicle when it runs, so ten failures in a burst are
  one review rather than ten.
- A line that arrives **while a review is open** is not consumed by it. A manager mid-run may have
  read the chronicle before that line was written, so it stays above the watermark and summons the
  next review as soon as the open one ends.
- A campaign that is not `active` consumes nothing. Its lines wait for it.
- One pass reads at most `Manager:MaxEntriesPerScan` lines, so a campaign with two thousand failures
  cannot hold a scan while they are counted. What is not read now is read next time.

An installation upgrading into this version starts level with its own chronicle: every campaign's
watermark is set to the last line that existed at the migration, so the first thing any manager is
asked about is something that happened afterwards.

## 4. The cadence

Work that simply sits there produces no line at all, so silence needs a clock.

`Manager:ReviewSeconds` is how often an active campaign is reviewed when nothing has happened to it —
five hours by default, and between five minutes and a week. A campaign may carry its own pace within
the same bounds (`jason campaign update <id> --review-seconds 600`, and
`--clear review_seconds` to put it back on the installation's). It is a field beside the campaign's
execution profile rather than a key in its context, because the dispatcher reads it and the
dispatcher must never read a context for meaning.

**The cadence is measured from an anchor**, which is set when a campaign goes live — the first time
and every later time — and moved every time the runtime creates a check-in. So:

- a campaign paused for a week and started again is reviewed a full interval after it wakes, rather
  than the instant it does;
- a review that failed does not stop the loop, because the anchor moved when the check-in was
  **created**, not when it finished;
- cancelling a check-in does not bring the next one forward. A person cancelling a review is saying
  "not this one", not "sooner".
- a campaign that has never been live has no anchor, and is never due rather than due at once.

**What a cadence costs.** One launch per active campaign per interval, on the operator's own agent
host and their own plan. Ten active campaigns at the default are about fifty launches a day; at the
floor of five minutes they are nearly three thousand. The floor exists because the number is easy to
type and the bill is somebody's.

## 5. What a check-in is told

The runtime writes the brief, and the brief is identifiers and constants:

```json
{
  "review_intent": "triggered",
  "trigger": "workitem_failed",
  "cause": {
    "journal_entry_id": "jrn_…",
    "work_item_id": "wi_…",
    "attempt_id": "att_…",
    "decision_id": "dec_…",
    "qualifying_count": 3
  },
  "allowed_operations": ["workitem.create", "workitem.update", "workitem.cancel",
                         "campaign.update_context", "journal.append", "rolenote.set",
                         "approval.list", "report.list", "decision.raise", "decision.list"],
  "escalation": "decision.raise"
}
```

A scheduled review carries `review_intent: "scheduled"` and names no trigger and no cause. Everything
else a review needs, it reads for itself through the CLI — the campaign, its work, its chronicle, its
contacts, and the role note that is the manager's own memory of this campaign.

**`cause.decision_id` is there when the answer to a question is what woke this review**, and absent
otherwise. One read is one review: a question answered behind a failure in the same pass is consumed
by the review the failure summoned, whose cause names the failure. So a manager reads the questions
answered since the last review for itself — `jason decision list --campaign <id> --status answered` —
rather than trusting the brief to name one.

**`escalation` is the verb to raise a question with**, in a key of its own beside a list that also
holds it. A role should not have to pick the one verb that changes what happens next out of eight.

**`allowed_operations` is guidance and not a permission.** Nothing in the runtime consults it. The
answer to a call outside that list is the answer anybody gets: deterministic guardrails still apply,
approvals still park what needs a person, and being the manager grants no authority at all. A second
permission system would be a second place for authority to be wrong.

**The answer has a shape.** A check-in carries a `result_format`, so a review reports something that
can be counted rather than prose:

```json
{
  "outcome": "acted | escalated | nothing",
  "summary": "at most 500 characters",
  "created_work_items": ["wi_…"],
  "cancelled_work_items": ["wi_…"],
  "decisions_raised": ["dec_…"]
}
```

`decisions_raised` is there so a person reading the review sees what is now waiting on them. Nothing
cross-checks that those are questions this attempt really raised: that would be validation reading
what a result means, and the shape is all a runtime can honestly hold a review to.

`outcome: "nothing"` is a good outcome. A manager that reads a campaign, sees a standing instruction
to leave it alone, and says so in a line of the chronicle has done its job.

**And its own budget.** `Manager:TimeoutSeconds` (900 by default), `Manager:MaxAttempts` (1) and
`Manager:Priority` (0) rather than the hour and three attempts an `ai_role` item gets by default: a
review either says something now or says nothing a second run would change, and the cadence comes
round anyway.

## 6. Lineage

A check-in belongs to the chain of the thing it is about, not to the dispatcher that created it.

- Where the causing line names an **attempt**, the check-in inherits that attempt's chain — its
  profile and revision where it pinned one, and otherwise whatever its own item carried.
- Where the line names a **work item** and no attempt — a person rejecting an approval — the check-in
  carries that item's record forward unchanged.
- Where it names neither, and for every scheduled review, the check-in is **root** work: nothing
  caused it that could have had a profile, so it resolves its profile from the campaign, the role or
  the configured default like any other root work.

Reading lineage from the creating actor instead would have made every review root work, and a review
of an attempt that ran on somebody's profile could have run on another without anybody saying so.

## 7. Settings

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Manager:Triggers` | `workitem_failed`, `approval_rejected`, `external_effect_reported`, `decision_answered` | each must be a kind the runtime writes | which chronicle kinds summon a review |
| `Manager:ReviewSeconds` | `18000` | 300..604800 | the cadence, unless a campaign names its own |
| `Manager:TimeoutSeconds` | `900` | 30..86400 | one check-in's budget |
| `Manager:MaxAttempts` | `1` | 1..10 | how many failures a check-in is worth |
| `Manager:Priority` | `0` | -1000..1000 | where a check-in sits in the queue |
| `Manager:MaxEntriesPerScan` | `500` | 50..10000 | how much chronicle one summon reads |

There is no `Manager:Enabled`: `Dispatcher:Enabled` already decides whether this runtime has a loop
at all, and a runtime with no dispatcher summons nothing.

**A narrowed list replaces the default rather than adding to it.** An empty array in the file cannot
be told from an absent key, so it reads as "unset" and the defaults apply.

**A narrowed list has to keep `decision_answered`.** Dropping any other kind narrows what a manager is
woken for; dropping this one breaks something, because a question a person has answered is what
releases the work that follows and the role that asked has already ended. Nothing refuses the
configuration — it is a legitimate thing to write — so it is said here instead.

## 8. Not here yet

- **Escalation.** A manager cannot yet raise a question for a person and have the answer release the
  next step. The `escalated` outcome is in the shape above because the vocabulary is fixed once, and
  nothing sets it in this version.
- **A manager skill.** The role is launchable and the brief is written, but the skill that teaches a
  manager how to review — decision boundaries, what belongs in campaign context rather than in a
  note, escalation etiquette — ships with escalation.
- **Notifications.** Nothing tells anybody a review happened. A person finds out by reading the
  chronicle or listing work.
- **Anomaly rules and reconciliation.** Neither exists; a review is the only thing that notices
  anything.
- **A second trigger vocabulary.** Reactive rules react to what the runtime already records. There is
  no inbound-reply classifier here to react to, and none is invented.
