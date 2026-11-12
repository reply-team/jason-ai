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
A check-in held at the day's limit (`Launches:MaxPerDay:manager`,
[docs/work-execution.md](work-execution.md) §The claim rule) is open too, though the index does not see a waiting
item: the summon reads its wait, so triggers while it is held summon no second check-in beside it; they summon the
next review once the held one has run, which the limit may hold to the next day.

**Only `active` campaigns are reviewed.** A draft campaign is a backlog, a paused one was stopped on
purpose, and an archived one is over. So a failure of work that runs in a paused campaign — the provider's own
pause, or the record that somebody said stop — is seen when the campaign resumes, or by whoever answers the
decision raised about it.

## 2. What wakes a manager

The chronicle is the event log. Every fact worth reacting to is already written there, in the same
transaction as the change that caused it, by code no caller can bypass — so there is no second write path.
The events store ([events](events.md)) is not a second one: it is the runtime's record of what a provider said
happened in an account, and an event reaches a manager only as a line in the chronicle, written with it.

A scan reads a campaign's chronicle above a **watermark** and looks at one thing: the kind of each
line. These kinds summon a review:

| Kind | Why |
|---|---|
| `workitem_failed` | Failed work is the manager's inbox. A failure nobody interprets is the campaign quietly stopping. |
| `approval_rejected` | A person said no to a proposed effect. The decision is made; what is missing is the strategy that follows it. |
| `external_effect_reported` | Something happened to this campaign that the runtime did not do. |
| `decision_answered` | A person answered a question a role could not answer for itself. The role that asked has already ended, so this is what releases the work that follows. |
| `inbound_reply_observed` | A thread in one of the campaign's conversations moved since the last read: somebody may have replied. The runtime's own read says so and interprets nothing. For the campaign's own thread the same line has already woken the responder, queued ahead of this review, which reads the reply and answers it; what the campaign as a whole calls for is the manager's ([inbound sync](inbound-sync.md)). |
| `provider_event_observed` | A provider said something happened that concerns the campaign — a reply it detected, an invitation accepted, a message that bounced — and the runtime found it by an identifier the campaign's own operations pinned, or by the person a member is. What it calls for is the manager's ([events](events.md)). |
| `provider_blocked` | A provider block holds the campaign's work: the account, or one sending account under it, is refused for its money, plan, allowance, credential or channel, and what reaches people has stopped until it clears. Said once per campaign and block. Whether to tell somebody, or change course, is the manager's; the block's clearing is not a trigger, because the work runs again by itself ([work execution](work-execution.md) §Provider blocks). |
| `audience_reserve_low` | The campaign's untouched people — the summary's `new` — fell under the threshold the campaign set with `--audience-threshold`. The runtime counts them and says so once per crossing; whether to top the audience up, and how, is the manager's (§9). |
| `audience_search_ended` | A search for the campaign's audience ended: it reached its count, its source ran out, its reads landed nobody for the stall bound, or a stop ended it, or a read or a stop found it gone (`search_not_found`). Said once per search. Whether the audience needs more, another search, or a person's word is the manager's (§10). |
| `audience_search_held` | The provider holds one of the campaign's searches — paused there, out of credits or at the plan's contact limit — so it spends nothing and finds nobody until a person acts at the provider. The runtime tells nobody directly; telling the person, by one question, is the manager's (§10). Said once per hold. |

**And what is deliberately not in the list.** `workitem_succeeded` is out: a review after every
successful step is cost without judgment, and the work that needs looking at is the work that did not
go well. `workitem_expired` and `workitem_cancelled` are out: the cadence catches them soon enough.
`provider_block_cleared` is out: what the block held runs again by itself, and the manager was told when it began.
`audience_reserve_recovered` is out: the manager was told when the reserve fell, and a reserve that recovered asks
nothing of it. `audience_people_landed` is out: people a search brings in are the reserve's to count, and the manager is
told when the reserve runs low and when the search ends.
`message_sent_observed` is out by default, and a campaign's installation may add it: a send the provider took for later
going out is the course the campaign planned, and it is worth a review only where the plan waits on that moment.
Everything about profiles, plugins, routes and roles is operator maintenance, not campaign meaning.

**The rules are settings, never a skill.** `Manager:Triggers` is a list of chronicle kinds, and every
one of them must be a kind **the runtime writes itself**: a kind that never appears could never fire,
and a kind a caller can append through `journal.append` would let anything that can write a line
summon a manager by naming it. A kind outside that vocabulary is a refusal to start, not a rule that
quietly does nothing. A kind the runtime writes about no campaign — `profile_created`, `project_created`,
`project_updated` among them — may be listed, and never summons anyone: a review is a campaign's, and such
a line names none.

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
                         "approval.list", "report.list", "decision.raise", "decision.list", "campaign.pause"],
  "escalation": "decision.raise"
}
```

A scheduled review carries `review_intent: "scheduled"` and names no trigger and no cause. Everything
else a review needs, it reads for itself through the CLI — the campaign, its work, its chronicle, its
contacts, and the role note that is the manager's own memory of this campaign.

**`cause.decision_id` names a question when the answer to one is what woke this review**, and is `null`
otherwise — like `work_item_id` and `attempt_id`, the brief writes every key it knows about and leaves
the ones it cannot fill as null. One read is one review: a question answered behind a failure in the same pass is consumed
by the review the failure summoned, whose cause names the failure. So a manager reads the questions
answered since the last review for itself — `jason decision list --campaign <id> --status answered` —
rather than trusting the brief to name one.

**`escalation` is the verb to raise a question with**, in a key of its own beside a list that also
holds it. A role should not have to pick the one verb that changes what happens next out of a list.

**`allowed_operations` is guidance and not a permission.** Nothing in the runtime consults it. The
answer to a call outside that list is the answer anybody gets: deterministic guardrails still apply,
approvals still park what needs a person, and being the manager grants no authority at all. A second
permission system would be a second place for authority to be wrong. The list names Runtime API verbs, so its
`campaign.pause` is Jason's own; the provider's pause is a `provider_op` item, made with `workitem.create`.

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

## 6. Escalation

A review may find something it cannot decide. It raises a **decision** — a question for a person, with a
`dec_` identifier of its own — and then ends its attempt, because an attempt has to end and the question
outliving it is the entire point.

**A decision is not an approval.** An approval is about one operation on one work item: it carries the
composed input, a canonical subject hash and a preview built from the operation's own contract, and the
work it parks is run again once somebody decides. A question has no operation, no input to hash, no
preview to build, and nothing waiting to be re-run — the attempt that asked is over. Bending approvals
around it would have broken the subject rule that makes them safe and put rows in `approval.list` that no
operation could describe.

### What it holds

The campaign, the asking work item and the **asking attempt**; the question in the role's own words;
optional named options; the causal references — what to read before deciding, as identifiers and never
copies, so a person opening it an hour later reads the rows as they stand; its status; and, once decided,
the answer, the chosen option, who decided and when.

A reference is `kind:id`, and the kinds are `work_item`, `attempt`, `journal_entry`, `report` and
`approval` — the entities that have a public identifier and a campaign. A role note is deliberately not
one of them: a note is addressed by its level — a campaign, a project, or neither for the client's — and a
role rather than by an id, and both are in hand wherever a question is read.

### The verbs

```
jason decision list --campaign <campaign-id> --human
jason decision get <decision-id> --human
jason decision get <decision-id> --prompt
jason decision raise <work-item-id> --attempt <attempt-id> --question "pause the sequence or continue at half volume?" --option pause --option "continue at half volume" --reference work_item:<work-item-id> --reason "three bounces in a day"
jason decision answer <decision-id> --answer "pause it" --option pause --actor human:you@example.com
```

**Raising is fenced by the attempt**, by the same guarded statement `workitem.set_result` stands behind:
the asker must be the run that owns the work item, or a role whose lease was lost could leave questions in
somebody's queue. That also means **raising a question counts as a heartbeat** — it is a fenced call like
any other, so a lease moves when one is asked.

**Answering is a person's.** A role, an attempt or the runtime's own actor is refused. What the runtime
cannot do is tell a person from a process holding that person's own command line; that is the limit an
approval already has, and it is the operator's trust to give.

Any launched role may raise a question, and the review released by the answer is always a **manager**
check-in. One live attempt may raise more than one question — nothing forbids it — but one question at a
time is what the manager's skill teaches, because a person who opens ten questions from one review answers
none of them.

### What retires one

A question is answered once. **Archiving a campaign cancels its open questions** in the same transaction,
because a live question about work that can never proceed is the row that would make `decision list`
untrue. **Cancelling a work item does not**: the attempt that asked was always going to end before the
answer arrived, so a question retired with its work item would be one nobody could ever answer.

### The bounds

| What | Limit |
|---|---|
| the question, in characters | 2000 |
| the answer, in characters | 2000 |
| named options | 10 |
| an option's label, in characters | 200 |
| an option's detail, in characters | 1000 |
| causal references | 50 |
| the reason on either call, in characters | 2000 |

The question and the answer are held to the 2000 characters every other free-text field in this runtime
is, and a listing carries the question whole — "what am I being asked?" is the only reason to open one.

### The codes

| Code | When |
|---|---|
| `stale_attempt` | the attempt raising the question is not the live one for that work item |
| `decision_not_found` | no question has that identifier; nothing deletes one, so this is a wrong id |
| `decision_not_pending` | it has been answered already, or its campaign was archived; the message says which |
| `decision_not_human` | a role or an attempt tried to answer, and an answer is a person's |
| `decided_inside_a_run` | the answer came from inside a run, whatever person it named: the CLI names its attempt on every request it sends from there, and a person answers outside the run that asked |
| `decision_option_unknown` | the chosen label is not one the question offered, or it offered none |
| `decision_reference_unresolved` | a reference names nothing in this campaign, so nobody could read it later |

An answer that names no person at all is `actor_required`, the same answer an unattributed approval gets.

### What the chronicle says, and what the summon reads

Answering writes `decision_answered`, naming the campaign, the asking work item and the asking attempt,
with the person as its actor — and carrying the decision's identifier where a **person** reads it.

The summon cannot read that identifier: what it is given is a projection with nowhere to put a line's
`old`, `new`, `key` or `reason`. So the decision row **remembers the chronicle line its answer wrote**, and
the cause resolves by that one indexed lookup. The alternative — a subject column on the append-only
chronicle, for one consumer — was refused.

Because the answered line names the escalating attempt, the review it summons inherits that attempt's
chain, and the work that follows belongs to the run that asked.

### What is not checked

A review's `decisions_raised` is not cross-checked against the questions that attempt really raised. That
would be validation reading what a result means, and the shape is all a runtime can honestly hold a review
to.

## 7. Lineage

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

## 8. Settings

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Manager:Triggers` | `workitem_failed`, `approval_rejected`, `external_effect_reported`, `decision_answered`, `inbound_reply_observed`, `provider_event_observed`, `provider_blocked`, `audience_reserve_low`, `audience_search_ended`, `audience_search_held` | at least one kind, each a kind the runtime writes | which chronicle kinds summon a review |
| `Manager:ReviewSeconds` | `18000` | 300..604800 | the cadence, unless a campaign names its own |
| `Manager:TimeoutSeconds` | `900` | 30..86400 | one check-in's budget |
| `Manager:MaxAttempts` | `1` | 1..10 | how many failures a check-in is worth |
| `Manager:Priority` | `0` | -1000..1000 | where a check-in sits in the queue |
| `Manager:MaxEntriesPerScan` | `500` | 50..10000 | how much chronicle one summon reads |

There is no `Manager:Enabled`: `Dispatcher:Enabled` already decides whether this runtime has a loop
at all, and a runtime with no dispatcher summons nothing.

How soon a reply wakes a manager is the inbound read's to say, not a manager setting: it reads each campaign's
conversations every `Inbound:SyncSeconds` (900 by default), and looks `Inbound:LookbackSeconds` (14400 by default)
behind its last read for a reply the provider took in late ([inbound sync](inbound-sync.md)). Whether the same reply
wakes the campaign's responder first is `Responder:OnReply`, which neither reads nor is read by `Manager:Triggers`
([work execution](work-execution.md), Settings).

**A narrowed list replaces the default rather than adding to it.** Leaving the key out keeps the
defaults; a list written with no kind in it — an empty array, `null`, an empty object, or an empty
`JASON_Manager__Triggers` — is refused, at a start and at a live edit, which keeps the list in force:
"Manager:Triggers is written but lists no trigger kind: leave it out for the defaults, or list the
kinds." It used to read as the defaults, the opposite of what whoever wrote it meant, without a word.
The key is read without regard to case, as every setting is. A list narrowed before
`inbound_reply_observed` existed does not hold it, so that installation is not woken by replies until
the kind is added; the same holds for `provider_event_observed`, `provider_blocked`, `audience_reserve_low`,
`audience_search_ended` and `audience_search_held`.

**A narrowed list has to keep `decision_answered`.** Dropping any other kind narrows what a manager is
woken for; dropping this one breaks something, because a question a person has answered is what
releases the work that follows and the role that asked has already ended. Nothing refuses the
configuration — it is a legitimate thing to write — so it is said here instead.

## 9. The reserve

A campaign can say how many people it wants to keep in reserve: members nobody has started on yet.
`jason campaign update <campaign-id> --audience-threshold 50` sets the number, between 1 and 100000, and
`--clear audience_threshold` takes it away. A campaign has none until one is set, and without one nobody counts. It is a
field beside the review pace rather than a key in the context for the same reason (§4): the dispatcher reads it.

**Untouched** is the summary's `new` ([work execution](work-execution.md) §A campaign's people, counted): a current
member with no work about them that a person, a role, an attempt or a plugin created. Work the runtime made about
somebody by itself, the read of a thread that moved and the responder it woke, does not touch them. The step counts by
the summary's own definition, so the number it says is the number `jason campaign summary` shows. A person on the
do-not-contact register lands and counts like any other: the register keeps a send from reaching them, not them from the
campaign, so a reserve may hold fewer people a send can reach than its number says.

**The reserve step** runs in every scan, for each active campaign that has a threshold, after the respond step and
before the summon, so a line saying the reserve is low summons a review in the same scan. It counts only when something
that can change the number was written since its last look: a member added or removed (`contacts_added`,
`contacts_removed`), a page of a search's people landed (`audience_people_landed`, §10), work created about somebody (`workitem_created`), or a setting of the campaign changed
(`campaign_updated`, the threshold among them). It keeps its place in the chronicle as a watermark on the campaign,
moved at every look that found new lines, so a scan with nothing new costs one read and counts nobody.

**Once per crossing, decided by the chronicle.** The step writes `audience_reserve_low` when the count is under the
threshold and the campaign's latest reserve line is not already a low one, and `audience_reserve_recovered` when the
count is back at or above the threshold after a low one. The latest of the two is where the reserve stands and nothing
else is kept, so a restart says nothing twice; a top-up that leaves the count still under says nothing more, and only a
recovery lets the next fall be said. Both lines are the dispatcher's (`system:dispatcher`), carry no key, and say
`new.untouched` and `new.threshold`: the count and the threshold at that moment. `audience_reserve_low` is a default
trigger (§2); `audience_reserve_recovered` is not.

**A threshold changed while the reserve is low** is one more change. Raised, the count is still under, and nothing is
said; lowered to or under the count, the reserve has recovered, and says so. A threshold cleared stops the counting and
leaves the chronicle as it stands; set again, the next look goes on from the latest line.

**What it never does.** It creates no work item, no search and no check-in: the review is the summon's, and whether and
how to top the audience up is the manager's. A paused campaign is not counted; what was written meanwhile waits above
its watermark for the next look.

## 10. The audiences

A campaign's audiences are the definitions its searches run under. An audience is made at the claim of the first
`audience.search` under its definition that the claim lets through, to run now or to wait for a person's approval: the
definition has passed the schema the routed plugin declares for one ([routing](routing.md) §7, `definition_invalid`). A
search the claim refuses makes none, and a preview never does. The campaign's context keeps the person's own words
about who belongs; nothing structured about an audience is kept there, and the dispatcher reads none of it.

**One row per definition**, in the table `audiences`: the campaign, a number, the definition as the claim held it, the
`sha256:` of the definition's canonical form, and when the claim that made it ran. The number is the runtime's, never a
writer's: 1, 2, 3 within the campaign, in the order the claim made them. The canonical form — every object's members in
ordinal key order, every array in its order, every value as written, compact UTF-8 — is the one that names a route's
binding and an approval's subject, so the same definition with its members in another order is the same audience, and a
definition the campaign searched under before, a reverted one, finds its old row and its old number. A row is never
changed or removed. An audience is its campaign's: no other campaign holds it, and a project holds none.

**A search names its audience one of two ways**, exactly one of `args.definition` and `args.audience`: a definition,
which is a new audience or finds the one it already is, or the number of one the campaign holds, a top-up, searched by
the definition that audience was made from. A number the campaign holds no audience under is refused at the claim,
`audience_unknown`, and so is a preview by it: `jason audience preview --campaign <campaign-id> --audience 2` reads the
campaign's audience 2 and previews it by its definition.

**The search carries its audience** under the reserved key `context.audience`, which the claim writes:

```json
{"id": "aud_…", "number": 2, "definition": {"industries": [{"value": "Logistics", "type": "include"}]}}
```

`definition` is the one the search is searched by, whichever way its arguments named the audience, so the search, and
the snapshot of its context the attempt that runs it keeps, says what was searched. The claim writes the key when it
finds or makes the audience and says so in one `workitem_context_updated` line by the dispatcher; the claim that runs a
search a person approved finds the same audience and writes nothing, and a claim that refuses the search writes nothing
either, so the key names the audience of the last claim that let it through. The key is the runtime's on a search: `workitem.create` and
`workitem.update` refuse a search that writes it or removes it (`validation_failed` at `context.audience`). On any other
work it is the caller's own.

**What a search may spend.** A search is `confirm_once`, so the first under an audience, its probe, waits for a person.
After it, the person's allowance — a standing approval for `audience.search`, named by one audience and capped in people
([work execution](work-execution.md), "Standing approvals") — lets the top-ups under that audience run without a
question until the people they ask for reach its ceiling. The allowance is the person's alone: only their acts set, move
or take it back, approving a search under another audience retires it in the same act, and no role's act holds or moves
it. The allowance is a permission, not an order, and the manager uses it carefully: only for an audience whose latest
verdict is `working`, and after a verdict of `replace` or `stop` it creates no top-up of that audience, even while the
allowance still names it, and leaves the reserve low until the person acts; they are asked at once, by the next
audience's probe or by the manager's question. The runtime does not enforce this — no role and no verdict moves the
allowance, and a top-up under it still goes, the person's own among them — it is the manager's skill that holds back
([work execution](work-execution.md) §Standing approvals). A manager tops an audience up as it always does; a top-up the
allowance does not cover waits for the person as the
probe did, its preview saying why.

### Following a search, and the people it lands

A search the provider takes ends its work item `accepted` with the provider's reference, and its people are learned
later ([`audience.search`](contracts/operations/audience.search.md)). From then on the runtime follows it itself, the way
it follows a campaign's conversations, and makes the people it finds the campaign's as they arrive. A search's live state
is a row of its own, never a scan step's memory or the search item's frozen result, so a runtime started again repeats
nothing.

**The search's row**, in the table `audience_searches`, made in the change set that ends the search's work item
`succeeded`, one per search: the campaign, the audience it ran under, the search's work item, the plugin it ran at, the
provider's reference, the cursor the next read starts from, the provider's latest word for it, whether the latest read
said more people are readable now, how many it was asked for, found and landed, when its reads began to land nobody, when
the dispatcher last read it, and how and when it ended. A search the provider finished inside its call gets its row
ended at once, and its people land from its own result.

**The poll**, a step of the dispatcher's scan after the inbound read: for each search still followed in an active
campaign with no read or stop of it under way — the dispatcher's or anybody's — one `audience.search_status` work item
naming it, by `system:dispatcher`, `Dispatcher:SearchCadenceSeconds` after the dispatcher made its last read of it, or
after the search began before its first — `Dispatcher:SearchHeldCadenceSeconds` for a search the provider holds; at once
where the last read carried people and said more are readable now, and a cadence later where it carried nobody. Each of the
dispatcher's reads in a row that ended without an answer doubles the wait, never past a day, as the inbound read waits;
each such read is a `workitem_failed` line, which wakes the review. The claim composes the read: the reference from the
search's own attempt, as for any read of a search ([routing](routing.md) §7), and the cursor the last read answered, from
the row. A paused campaign's searches are neither read nor ended; an archived campaign's are stopped, below.

**The landing**, in the change set that ends a read `succeeded` — whoever made the read — and that ends a search
answered `finished`: each person of the page once, by the provider's identifier. A person is found by the first key
they carry that finds anybody: that identifier pinned at the plugin under `contact`, then each address, then each
LinkedIn profile — so a profile the directory holds finds the person even when the address beside it is new; where two
contacts answer one key, the oldest live one. A person found is never changed, as an import never changes one: an
address the directory did not hold is not added to them; a person found
nowhere becomes a contact, with their names, title and company where the contact's rules take them, their channels, and
what the contact has no field for under `custom`: `location`, `company_domain`, `company_size`, `company_industry` and the
provider's own record as `provider_record`, left out where it would take the contact over its 64 KiB of `custom`. Each
is pinned under `contact` by the attempt whose answer carried the page, as any pin is
([routing](routing.md) §11): a different identifier later is recorded beside the pin. Each becomes a member of the
campaign, except a person the campaign already holds, who stays as they are, a person removed from it, who stays
removed, and a person the directory archived, who is never made again. Membership is local: nothing here enrols anyone
at the provider. For each membership a search made, one row in `audience_landings` — the membership, the audience, the
search, the read that carried the page, the moment — written once and never changed by a removal or an add-back;
nothing is added to the membership itself. `jason campaign list-contacts` names it as the member's `source`: the
audience's id and number, the search and when the person landed.

**What the chronicle says**, by `system:dispatcher`, each kind with its document under
[`contracts/journal/`](contracts/journal), facts and never a person's words:

- `audience_people_landed`, one for each page that carried anybody, keyed by the read and about the search: the audience,
  the provider's state, how many the page held, landed, made contacts of, found already members and skipped, and the
  people landed and made, by id. The reserve recounts on it (§9). Not a trigger. It is the one line that says how a page
  went; each pin the page writes keeps the line every pin has (`external_id_pinned`, or `external_id_diverged` beside a
  pin that stood), naming the person.
- `audience_search_ended`, once per search, keyed by and about the search: how it ended — `finished`, `exhausted`,
  `stalled` or `cancelled` — the provider's last word for it, what it was asked for, found and landed, and the work item
  whose answer ended it. A default trigger.
- `audience_search_held`, keyed by the read that first said so and about the search, when a read first reports that the
  provider holds it — `paused`, `paused_for_credits` or `paused_for_limit` — with the audience, the state, what it was
  asked for, found and landed; again only after the search moves and holds again. A default trigger: the runtime tells
  nobody directly, so the manager raises one question to the person saying what holds the search and what to do at the
  provider — add credits, lift the plan's limit, resume it — and that the runtime goes on following it.

**How a search ends.** A read that answers `finished` or `exhausted` with nothing more readable ends it as that; one that
answers either with more readable leaves it to the next read. A stop (`audience.search_cancel`) that stopped it, or found
it stopped by an earlier stop, or gone from the provider, ends it `cancelled`; one that found it ended by itself leaves it
to the reads, since what it found is still to be read. A read or a stop that fails `search_not_found` ends it `cancelled`
too, by that item: the provider holds no search under the reference for the account the binding signs in as — deleted
there, or that account changed — so nothing would read or stop it again. And a search whose reads have landed nobody for
`Dispatcher:SearchStallSeconds` — measured from the start of the quiet, the search's start, the last landing that made
somebody a member or the moment it moved again after a hold, to the moment the dispatcher made its latest read, once that
read is over — ends `stalled`, said by the dispatcher, and is read no more; never while a stop of it is under way. That
bound counts a provider still searching and a read that failed. **A search the provider holds** until a person acts there
(`paused`, `paused_for_credits`, `paused_for_limit`) spends nothing meanwhile, so it is said at once
(`audience_search_held`), read every `Dispatcher:SearchHeldCadenceSeconds` instead of every cadence, and called
stalled only once it has stayed held for `Dispatcher:SearchHeldStallSeconds`, a week unless set, measured from the moment
it was held; the line then names the hold, so a stall for credits reads as one. A search that moves again is read on the
ordinary cadence again, under the ordinary bound counted from the moment it moved, so a person who adds credits or
resumes it at the provider has its people landed. A search stalled while it was running is not followed again by the
poll: a read anybody makes still lands its page, and a stop still stops it. A search that ended stays as it ended: a
later read still lands what it carries, and nothing is said twice.

**One search at a time.** A campaign runs one search at a time: a search claimed while another of its campaign has not
ended — its row still followed, running or held, another search running now, or one open again after a start whose
answer was lost — waits behind it, with no attempt and
nobody asked to approve it, its last error naming the search in the way, and is claimed again a search cadence later,
the long cadence of a hold where the provider holds that search ([routing](routing.md) §7, `search_unfinished`). Two campaigns searching one definition are the provider's own refusal,
`search_in_progress`.

**Archiving stops the searches.** Archiving is how a person stops a campaign, and a search goes on spending at the
provider until it is stopped. So the archive cancels the campaign's open work but a search's stop, which runs in an
archived campaign as in a paused one (`dispatch.in_archived_campaign: runs`), and the next scan makes one stop for each of
the campaign's searches still in flight; each ends `cancelled`, and the provider's list for it is deleted. A search the
provider had already ended by itself ends as it ended, since nobody reads an archived campaign's searches again; through
Reply its list stays at the provider, with any people not yet read. A search whose start was still under way when the
campaign was archived has no row yet, so no stop is made for it: the archive cancels that attempt, and whether the
provider took the start is read there. A stop that fails is protective work, notified, and not made again; one that fails
`search_not_found` ends the search `cancelled` all the same, since there is nothing left to stop.

### Reading the audiences

`jason campaign audience <campaign-id>` (`campaign.audience`) says where the campaign's audience stands, from the records
above and nothing kept for the read: the untouched count and the threshold (§9); the allowance, with the searches it let
go and the people they asked for, as `jason approval list-standing` shows it; and the campaign's audiences, the newest
first, a page at a time. Each carries its number and id, when the claim that made it ran, its definition as that claim
held it, how many people its searches landed, removed since or not, how many searches the provider took under it, and the
latest of them, the newest first, ten unless `--search-limit` asks for up to a hundred: what each asked for, what the
provider said it found and last said of it, how many it landed, when it began, and how and when it ended. A search's
whole story stays in the chronicle (`audience_people_landed`, `audience_search_ended`). A search the provider has not
taken yet, a probe waiting for a person among them, has no row: `jason approval list` and `jason workitem list` show it. With `--human`, each definition is laid out by the schema the
plugin the campaign's route for searches declares, and shown as stored where that cannot be read.

The people reads take `--audience <number>`: `jason campaign summary`, `activity`, `activity-by-period` and
`list-contacts` then count and list only the people a search landed under that audience, each count by its own
definition ([work execution](work-execution.md), "A campaign's people, counted"), so one audience's people are judged on
their own: who was worked on, contacted, replied or opted out since they landed.

## 11. Not here yet

- **Notifications.** Nothing tells anybody a review happened. A person finds out by reading the
  chronicle or listing work.
- **Anomaly rules and reconciliation.** Neither exists; a review is the only thing that notices
  anything.
- **Knowing what a reply says.** The runtime records that a thread moved (`inbound_reply_observed`,
  [inbound sync](inbound-sync.md)), queues the read of the thread and the responder for a thread that is the
  campaign's own, and wakes the manager; it never reads or classifies the reply itself, and nothing tells a person
  one came. Reading it, and answering it, is the responder's; what it means for the campaign is the manager's.
- **Allowing in the browser.** The UI's approval panel approves or rejects a search; allowing more people under its
  audience is `jason approval approve --allow` or `jason approval set-standing` for now.
