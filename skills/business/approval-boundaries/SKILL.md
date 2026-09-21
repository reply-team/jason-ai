---
name: approval-boundaries
description: >
  Where an agent must stop and ask the user before acting, how an operation's approval class
  is derived from its reversibility and its reach, what a valid confirmation looks like, and
  the one case where acting first is correct. Covers previews, retries and idempotency keys,
  unattended runs, standing approvals and anomaly stop-rules. Use before any operation that
  reaches a real person, when deciding whether an approval still applies, or when configuring
  an agent to run without a human present.
metadata:
  version: 2.0.0
  pack: ai-sdr-core
  category: protection
  maturity: draft
  status: active
  owner: skills-maintainers
  tags: [safety, approval, derivation, idempotency, unattended, anomalies]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [sending-guardrails, linkedin-guardrails]
---

# Approval boundaries

## Purpose

Automated outbound acts on real people, using the user's domain, accounts and reputation.
This skill defines the boundary between what an agent may do on its own and what requires
the user to say yes — once, explicitly, for a specific action.

Approval is no longer a value an agent looks up. It is **derived** from two properties the
contract already states: what an operation can undo, and how far it reaches. This skill is
where that derivation is explained and applied, and where the rules that sit on top of it
live — what a preview must contain, what counts as a confirmation, what may be done without
one, and what changes when nobody is present. It exists as one skill rather than a paragraph
repeated in every workflow, because a rule that lives in eleven places drifts in eleven
directions.

## When to use / when NOT to use

- Use before performing any operation whose reach is `act`, before any call that carries a
  collection, when resuming interrupted work, and when setting up an agent to run unattended.
- Use when an operation's declared approval differs from what the derivation gives — the
  departure carries a reason, and the reason is what you act on.
- Use when you are unsure whether an earlier approval still covers what you are about to do.
  The answer is usually no; this skill says when.
- Do NOT use it to look up provider mechanics — scoped credentials and their enforcement
  belong to the adapter pack.
- Do NOT restate the derivation inside another skill. Two copies of a gate is how one of them
  ends up wrong; read it here.
- Do NOT treat it as optional because the user seems in a hurry. Speed is not consent, and
  speed never lowers an approval class (K9).

## Prerequisites

From `sdr-operations`, for every operation in the plan:

- **`reach`** — `read` · `control` · `act`. What it changes: nothing, state we own, or the
  outside world.
- **`reversibility`** — `reversible` · `compensatable` · `irreversible`. `compensatable`
  means the state can be restored and the consequence cannot.
- the **declared `approval`**, together with `approval_departs` and, where it departs, the
  `approval_reason` stated at the operation.
- **`before_repeating`** — the observable state to read before running it again.
- **`idempotency_key`** — whether the call takes a caller-supplied key.

Where a property is conditional, the contract states the dangerous value and the detail that
resolves it. Reason with the dangerous value until a call resolves it. Absence of a property
is read as the most dangerous value, everywhere, with no tier where that is suspended (N3).

## Planning guidance

A plan is written so that every approval gate is visible *before* execution starts, not
discovered during it. When decomposing work:

- Put each `act` operation in its own step, so the approval has a precise subject.
- Keep one call that carries a collection as **one** step with one preview. One call over a
  set is one decision; splitting it into two hundred prompts is not two hundred times safer.
- Mark protective operations explicitly as protective, so a later reader understands why they
  carry no gate.
- Where a property is conditional — `campaign.enroll` is bookkeeping into a draft campaign and
  an act the moment the campaign is live — the plan names the call that resolves it, and that
  call is `operation.preview`, never an assumption. A preview never acts and never spends (N4).
- Resolve authority at plan time, not at execution time: `plan.validate` and `capability.list`
  (N5). Discovering a refusal at step 40 of 200 is a design failure.
- For an unattended run, read `autonomy.get` first and plan against what it returns.

## Execution guidance

### Approval is derived from reversibility by reach

<!-- BEGIN GENERATED approval-derivation -->

<!-- Generated. Do not edit by hand. -->

| reach ↓ · reversibility → | `reversible` | `compensatable` | `irreversible` |
|---|---|---|---|
| **`read`** | `auto` | `auto` | `auto` |
| **`control`** | `auto` | `auto` † | `confirm_once` |
| **`act`** | `confirm_once` ‡ | `confirm_once` ‡ | `confirm_each`, or `confirm_once` ‡ where one call is one decision over a set |

† **widened to `confirm_once`** where the call carries a collection or a pattern, or may
overwrite state someone else owns.
‡ **with a mandatory preview**, and the preview names the population rather than counting it.

<!-- END GENERATED approval-derivation -->

**Policy may raise the class; nothing may lower it.** If your reasoning arrives at a smaller
gate than the table gives, you have found either a defect in the contract or a misreading of
the operation. Neither is fixed at execution time.

### What each class means in the room

- **`auto`** — perform it. Do not ask, do not pause, do not offer the user a choice they did
  not need to make. Report what you did afterwards. Asking here is the defect, not the caution.
- **`confirm_once`** — state the operation, its target, what changes and what it consumes, then
  wait for an explicit yes **to that description**. One yes covers the whole call, including a
  call carrying a collection. Where the class comes with a preview, the preview is what is shown
  and the preview is what is approved.
- **`confirm_each`** — one human decision per item, because each item is its own act against a
  named person. Show the artefact that will actually reach them: the rendered message, the
  recipient, the channel, the sending identity. `message.draft` produces that artefact, and the
  rendered message — not the template behind it — is what every check and every review binds to
  (E14). Then wait.

### A departure names its reason

An operation whose declared approval differs from the derivation says so at the operation, with
`approval_departs` set and an `approval_reason` stated. Read the reason before you act on the
class, because the reason is what tells you what the raise protects: `sender_limit.set` is
raised from `auto` because one call rewrites limits somebody else may have declared, and that
reasoning does not travel to the next `control` × `reversible` operation that happens to look
similar.

**An unexplained raise is as much a defect as an unexplained lowering.** A gate nobody can
justify still costs a prompt, and 900 prompts is habituation, not safety — the user stops
reading, and every real gate downstream is spent. Report an unjustified raise as a defect in
the contract rather than obeying it silently. See *over-gating* under **Failure modes**; it is
the same failure arriving from the other side.

### A release is `confirm_once`; a composition is `confirm_each`

The distinction is not size. It is whether one call is one decision.

- **A release** turns on, resumes, or lets go a body of work that already exists. One human
  decision covers it, and the preview is the approval artefact. `campaign.enroll` and
  `campaign.activate` are releases: putting 200 people into a campaign is one decision about a
  population, and activating a campaign holding 200 enrolments is one act of magnitude 200
  (E2). Every release of suppressed sending — `campaign.resume`, `campaign.unfreeze`,
  `outreach.release` — names its backlog in the preview (E3).
- **A composition** puts distinct words in front of one named person. `message.send` is a
  composition: one person, one channel, one call, each its own act. `confirm_each`.

Getting this backwards inverts E2 in both directions at once. Treating a release as a
composition makes enrolling into a draft and activating afterwards a *cheaper* approval than
enrolling into a live campaign, which is precisely the arbitrage E2 forbids. Treating a
composition as a release turns a queue of individual sends into one bulk yes.

### The preview names the population; it does not count it

**This is the load-bearing sentence in this skill.** With enrolment into a live campaign
sitting at `confirm_once`, everything the derivation gives up in prompts it buys back here.

**"200 contacts" is not a preview.** It is a number the user can neither check nor refuse on
any ground, and approving it is a reflex rather than a decision. A preview names:

- **who** — identified individually where the set is small enough to read, and otherwise by the
  exact criteria plus the frozen list they were drawn from, with that list's name and its freeze
  date. A list is a frozen fact with a date on it, and outreach runs against frozen membership
  (D1);
- **what** would reach them, as the rendered artefact;
- **when** the first touch lands, in whose calendar;
- **what it consumes** — the meter and the allowance, including `unknown` remaining, which is
  not the same fact as ample remaining (A6);
- **whether the effect resolves to a send at all**, which is the whole point of the call for a
  conditional operation.

`operation.preview` produces it. If the preview cannot name the population, the call does not
go — the answer to an unnameable population is to fix the audience, not to approve the count.

A preview is not a reservation. A pass from a permission check is advisory and the binding
refusal is at send time; a batch prepared against an earlier preview is re-screened at dispatch
(A7). A preview that has gone stale is re-run, not re-used.

### What counts as a valid confirmation

- **Explicit.** A clear yes to the specific thing described. Silence is not consent, neither is
  "sounds good" to an earlier different question, nor the absence of an objection.
- **Current.** Given in this conversation, about this action, after seeing what it does — for a
  previewed call, after seeing the preview.
- **Specific.** An approval covers the action described and nothing else. Approving one call
  over 214 contacts does not approve the next call. Approving `campaign.activate` does not
  approve a later `step.update` to the live campaign, which is its own act whose magnitude is
  the enrolments it moves.
- **Not self-granted.** An agent may not approve on the user's behalf because it judges the
  action safe, because the user approved something similar before, or because an approved plan
  contained the step. An approved *plan* authorises reaching the gate; it does not pass through
  it. An agent may transmit a human's decision and may never author one (L4).
- **About the right gate.** Approving content and approving a send are different gates with
  different lifetimes (L3). `template.publish` and `approved_content.submit` approve content;
  neither approves any send. Where a human resolves an item directly through
  `approval.resolve`, that resolution *is* the confirmation — and rejecting a queued draft is
  never a no-op, because it can end the person's participation and must say what else it ended
  (L5).

### The protective floor: acting first

An operation whose **non-performance is itself the harm** derives `auto` whatever its
reversibility. Waiting for permission to stop is the destructive choice. This is not an
exception this skill invents to cover an awkward case — it is a floor the contract derives, and
the operations that sit on it are declared `auto` in the contract itself (L6).

The protective set, named:

- **`campaign.pause`** — stop this campaign. It never requires a lookup first and never waits.
- **`enrollment.pause`** — hold this person here.
- **`sender.pause`** — stop this capability sending. The first move in every incident on every
  channel.
- **`outreach.hold`** — the kill switch over a person, a company or a domain. It reaches
  everything queued against them — enrolments, tasks, pending approvals, scheduled sends,
  un-accepted handoffs, outstanding meeting proposals — and reports what it could not reach
  (I5).
- **`scheduled_message.cancel`** — pull back what has not gone yet. Part of every stop branch,
  and the one step in it that keeps a queued message from arriving after the person has asked to
  be left alone.
- **`approved_content.retire`** — take approved copy out of use. Withdrawing permission to send
  something never waits; granting it does.
- **a firing `stoprule.set` rule** — the work stops itself with nobody present, and the firing
  record names the trigger value, the operations halted and every item left unprocessed (L8).
- **`sender_limit.set` downward** — reducing what a capability may send is protective in shape,
  and the contract still declares the operation `confirm_once`, raised, because one call rewrites
  limits somebody else may have declared. That is the edge of the floor, and it is exactly why
  the first move in an incident is `sender.pause` and not a lower limit: the pause is `auto`,
  the limit is not.

Recording a fact that is already true belongs here too, for the same reason: `optout.record`,
`restriction.record`, `inbound_lead.record`, `privacy_request.create` and `deletion_feed.report`
derive `auto` because recording an event that already happened outside our control — or meeting a
deadline that runs whether we act or not — is not a decision, and delaying it burns the only thing
that motion is measured on (K2). Recording that somebody said stop is never a judgement about what
they meant (A2).

Those five are **the whole of the floor**, and the list is checked rather than remembered: each one
carries `protective_floor: true` in the contract, the build refuses that marker on any operation the
derivation table already gives `auto`, and it refuses a lowering that does not carry it. If you
believe a sixth operation belongs here, the contract is where that argument is settled.

`suppression.add` sits here **only for a single verified identifier**. Called with a domain, an
account pattern or a collection it is `confirm_once`, because suppressing a domain is a
commercial decision and not a protective one — somebody is choosing to stop reaching a whole
company, which is nothing like honouring one person's stop. The floor covers the shape the call
actually carries, never the operation's name: read the argument before concluding the gate.

**The restart direction always waits** (L6). `campaign.resume`, `sender.resume`,
`outreach.release`, `stoprule.clear` and `suppression.remove` are each declared above `auto`, and
resume refuses from any state other than held (G5). `enrollment.resume` belongs to the same rule
with the same conditional shape as its pause side: `auto` while the campaign is not live, because
resuming reaches nobody while nothing is sending, and `confirm_once` with a mandatory preview
naming who resumes the moment it is. Nothing that increases
activity, sends anything, or deletes anything is ever covered by the floor.

### Retries, resumption and idempotency

There are two mechanisms, and the order between them matters.

1. **The key prevents the duplicate.** Every operation with reach `act`, every write carrying a
   collection, every write creating a durable object and every metered call — including metered
   reads — takes a **caller-supplied idempotency key**. Generate it before the first attempt,
   not after the first failure: a replay under the same key is the same call, not a second one.
   Two shapes take no key and say so, because replaying them is harmless by construction: an
   absolute-value policy setter ("the ceiling is 400", never "add 200"), and an operation naming
   a terminal state, which a repeat can only arrive at again — `job.cancel` is the example, and
   its `key: none` is the rule holding rather than an exception to it.
2. **The read detects one the key could not prevent.** `before_repeating` names the observable
   state to read before running an operation again — a different key, a different session or a
   different agent leaves the key with nothing to match. `campaign.enroll` reads the per-item
   outcome of the prior run under this key *and* the campaign's live state, because re-running
   blind into a live campaign sends twice. `message.send` reads whether a send is already
   recorded for this person on this thread or step in the window.

Prevention comes first because detection has a window: between the read and the write, the
world moves. Use both; rely on the key.

**A lost connection is not a failure.** `invocation.get` recovers the outcome of a call by the
key that was sent, and distinguishes `never_seen` from `seen_and_failed` — two answers that must
never be collapsed, because a timed-out send may already have succeeded. The recovery is that
lookup, never a blind retry. An operation whose result cannot be recovered after a lost
connection does not ship (N1).

The approval consequences:

- A retry of an approved call with **identical** parameters under the **same key** needs no new
  approval. Any change to target, population, content or count makes it a new action needing a
  new approval and a new key.
- A resumed session inherits no approvals. Work that was mid-flight and unapproved stops at its
  gate and waits, exactly as it would have the first time.
- Where a call carried a collection, the per-item ledger is the record of what happened, with a
  reason per item (N2). A partial success reported as one verdict is a defect — and re-running
  against "it failed" repeats every item that actually succeeded.

### Unattended operation

When no human is present — a daemon, a scheduler, a background session — the boundary tightens
rather than relaxing. The contract has operations for this; point at them rather than inventing
a mechanism the reader has to reconstruct.

**Before the run.** `autonomy.get` returns what may run without asking in this scope on this
channel: the mode, the queue timeout and what a timeout means, the unattended action cap, the
escalation target, per-channel asymmetry, and **where the gate lives**. When the gate location is
nowhere and the run is unattended, every operation above `auto` refuses — double-prompting and
no-prompting are both defects, and only a declared location prevents both. All five oversight
models are expressible and none substitutes for another (L1). `autonomy.set` changes it, and is
itself raised to `confirm_once`, because the acts a loosened setting permits are not reversible
even though the setting is. Check the escalation target is a person who is still there: a run
whose escalation target is invalid must not start (H12).

**When the run hits a gate.** `approval.request` puts the item in front of a named human and the
run waits. `approval.list` says what is waiting and how long it has waited; `approval.get` shows
one item exactly as it will go out; `approval.resolve` approves or rejects it, and approving is
the send. An approval that times out inside an unattended run **resolves as refuse-and-halt** —
it never proceeds, the item stays pending for a human, and the run stops (L2).

**When the agent cannot or must not proceed at all**, `escalation.raise` hands the work to a
named human. It grants nothing (L4). `escalation.list` shows what has been handed over and not
picked up; `escalation.resolve` records how it ended and whether the work returns to the agent.

**Pre-authorisation is bounded**, conversationally and mechanically. A decision the user made in
advance about a bounded scope — this campaign, this list, this volume, this window — is not a
blanket permission; and the credential the run holds is scoped to only what the workflow needs,
so a misbehaving agent *cannot* exceed the boundary even where its reasoning fails. The adapter
pack documents how the scoping is expressed.

**A gate hit always leaves a record.** A silent stall is indistinguishable from a crash.
`approval.request` and `escalation.raise` are that record while the run is alive;
`stoprule_firing.list` and `stoprule_firing.get` are what the morning reads to find out why
sending is off, and what was left unprocessed (L8).

### Standing approval

A **standing approval** is a recorded human decision that names the artefact, names the actor,
carries a **ceiling** and carries an **expiry**. It is how a time-critical act runs with nobody
awake, and it is the mechanism to reach for when a response deadline falls outside working
hours.

**It is not a lowered approval class.** This is the obvious thing to abuse, because a standing
approval looks exactly like the mechanism for "this may now run unattended". The declared floor
still applies to everything the record does not name: a standing approval for one named artefact
up to a ceiling of 50 does not cover the 51st, does not cover a different artefact, and covers
nothing at all once it has expired. Read the ceiling and the expiry before relying on one, and
record which standing approval authorised each act — an act whose authority cannot be named
afterwards was unauthorised. Where responding fast requires acting with no human present, that
is an explicit, recorded autonomy grant and not an inference from urgency (K9).

### The disclosure link

An agent will not infer this connection, so it is stated here once. **A recorded human editorial
review is what the disclosure decision reads.** `editorial_review.record` binds a named human,
the time, and the difference from the draft to the message that actually went out;
`disclosure.check` reads that record when deciding whether machine generation must be disclosed
on this channel for this outreach, and `disclosure.record` records that it was. Machine-drafted
outreach carries either the review bound to the message actually sent, or the required
disclosure, or it is refused (A21).

What this means at a gate:

- The `confirm_each` approval a human gives on a rendered message is the moment the review
  exists. Record it with `editorial_review.record` at that moment, or the evidence is gone and
  the send is left carrying the disclosure obligation instead.
- A review of a template is not a review of a message (E14). `template.publish` does not produce
  one.
- `editorial_review.get` answers the question afterwards, and `none` is a first-class answer —
  the one an auditor most needs.

**The rule lives with the operations, not here.** Do not restate what must be disclosed, in what
form, or where; `disclosure.check` answers that for the channel and the outreach in front of you,
and nothing in this skill substitutes for making the call.

### Anomaly stop-rules

Predictability means the agent stops on surprise rather than pressing on:

- A partial failure is not a success. Read the per-item ledger, stop, report counts and reasons,
  and ask.
- A result an order of magnitude from expectation — 2000 people matched, not 200 — stops before
  the write, not after. `operation.preview` is where that becomes visible while it is still free.
- Repeated identical failures stop after the first retry, and that retry is `invocation.get`, not
  another attempt. A loop of failing calls is never the answer.
- If the signals that justified the plan no longer hold, the plan is stale — stop and re-plan
  rather than executing a plan whose premise is gone.
- Stopping has three granularities and all three exist (L7): `job.cancel` stops this run;
  `outreach.hold`, `campaign.pause` and `sender.pause` stop outbound over a scope while reads
  stay alive; `stoprule.set` stops the work automatically on a named threshold. A budget ceiling
  set through `budget.set` is a hard stop, not an alert — an alert that stops nothing is a
  different feature (C5).

## Validation

The boundary held if, after a run, all of the following are true:

- every operation with reach `act` traces to an explicit approval that named it, or to a standing
  approval whose named artefact, ceiling and expiry all covered it;
- every `confirm_once` over a set traces to a preview that **named** its population;
- every act performed without approval either reduced activity or recorded a fact that was
  already true, **and derived `auto` for the arguments it was actually called with** — the floor
  covers a shape, not a name, so "it was a suppression" does not clear a domain-wide one;
- every departure from the derivation that was relied on carried its stated reason;
- every gate an unattended run hit left a record, and every call that could not be confirmed as
  completed was resolved through `invocation.get` rather than repeated.

If any is untrue, that is a defect to report, not a detail to omit.

## Reporting

Record, for each gated step: what was proposed, the preview where one carried the approval, what
the user approved, when, and the idempotency key the call went out under. For protective acts
taken without approval: the signal, the threshold it crossed, the operation performed, and when
the user was told. For an unattended run: which gate stopped it, where the record is, and what
was left unprocessed. A report that shows approvals is what makes an autonomous run auditable.

## Failure modes

- **Approval drift** — treating a plan approval as covering its `act` steps. The commonest and
  most consequential mistake.
- **Counting instead of naming** — offering "200 contacts" as a preview. The commonest way a
  `confirm_once` gate becomes decorative, and the one this skill exists to prevent.
- **Batch creep** — an approval for one preview quietly applied to a population that was
  re-materialised afterwards. A preview is not a reservation.
- **Release/composition inversion** — enrolling into a draft and activating later, to avoid the
  gate on enrolling into a live campaign. The magnitude did not change; only the prompt did.
- **Politeness as consent** — reading agreement into a friendly reply that did not answer the
  question asked.
- **Silent gating** — stopping to wait without telling the user, so the work looks finished.
- **Over-gating** — asking permission to read, or to pause something that is burning. Excessive
  confirmation trains users to approve without reading, which destroys the value of every real
  gate. An unexplained raise produces exactly this, one operation at a time.
- **Class by analogy** — copying a declared class from an operation that looks similar instead of
  deriving it and reading the departure reason.
- **Blind retry** — re-issuing a call after a timeout instead of recovering its outcome by key.
- **Standing approval as a licence** — treating a bounded, expiring decision about one named
  artefact as a general lowering of the class.

## Safety

This skill *is* the safety layer for the operation contract, and it ships inside `ai-sdr-core`
for exactly that reason: guardrails must not be an optional extra that a selective install can
leave out. It states no threshold and no product mechanism of its own — the classes it applies
are derived in the contract, and where this skill and `sdr-operations` disagree the contract
wins and the disagreement is a defect to fix here. Weakening a rule here weakens every workflow
that relies on it, so such changes need review by the owning group, not a quick edit.

## Related skills

- `sdr-operations` — the reach, reversibility and derivation this skill applies, the invariants
  cited above by ID, and worked chains that show every gate in sequence.
- `sending-guardrails` · `linkedin-guardrails` — the domain thresholds whose breach is what
  triggers protective action.

## Changelog

- 2.0.0 (2026-08-10): breaking, against the 325-operation contract. The three hand-assigned
  approval levels are replaced by the derivation from reversibility by reach, with its widened
  cells, its protective floor and the rule that a departure names its reason at the operation.
  Adds the release-versus-composition distinction and the requirement that a preview names its
  population rather than counting it. The protective exception is re-expressed in contract
  operations instead of prose descriptions. Retries now state both mechanisms — the idempotency
  key that prevents a duplicate and the `before_repeating` read that detects one — with
  `invocation.get` as the recovery. Unattended operation is wired to `autonomy.get`,
  `autonomy.set`, the approval queue and the escalation operations, and gains the standing
  approval as a bounded, expiring record rather than a lowered class. Adds the link between a
  recorded editorial review and the disclosure decision.
- 1.0.0 (2026-07-30): initial version — three approval levels, validity rules for a
  confirmation, the protective-action exception, retry/resumption semantics, unattended
  pre-authorisation and anomaly stop-rules. Consolidates the per-skill Safety sections that
  previously restated these rules separately.
