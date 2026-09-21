---
name: campaign-planning
description: >
  Turn a vague outbound goal into an executable plan: clarify the objective until success is
  observable, surface the constraints that shape it, compose the work from known operations
  and playbooks, and decide the checkpoints. Use when the user states a business goal — book
  meetings, launch a campaign, grow pipeline — rather than a single concrete action.
metadata:
  version: 2.2.0
  pack: ai-sdr-core
  category: strategy
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [planning, decomposition, strategy, checkpoints]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [approval-boundaries, audience-building, campaign-launch, inbox-triage, performance-analysis, sending-guardrails, linkedin-guardrails, durable-work]
---

# Campaign planning

> **Partly an expert skeleton.** The planning method is complete. The strategic judgement —
> which play fits which situation, how to sequence experiments — is marked `TODO(expert)` and
> needs validation by outbound experts before this skill leaves `draft`.

## Purpose

Outbound is a loop, not a single prompt: goal → plan → work → measure → reassess → repeat.
This skill turns a business objective into the first two of those, in terms concrete enough
that someone else — another agent, another session, a colleague — could execute the result
without asking what was meant.

## When to use / when NOT to use

- Use when the request is a *goal* ("get me ten meetings with SaaS founders this quarter")
  rather than an *action* ("import this file").
- Do NOT use for single concrete actions — route straight to the matching skill.
- This skill produces a plan; it does not persist one. Where plans, work items and progress
  physically live is the job of a runtime — `durable-work`, if the `agentic-runtime` pack is
  installed. This skill works without it, and says so rather than assuming a workspace exists.

## Prerequisites

- A goal the user actually cares about, and whatever context they already have — ICP,
  preferences, past results.
- The operation contract (`sdr-operations`), because a plan is expressed in operations.
- A credential, if the plan is to be checked against a real installation. The constraint reads
  in step 2 need one; the plan can still be written without one, provided it states which
  constraints it assumed rather than presenting an assumption as a fact.

## Planning guidance

**Compose from what exists before inventing.** Ask which available skills already cover parts
of this goal, and assemble those. Where nothing covers a part, record the gap explicitly
rather than quietly improvising new methodology — an acknowledged gap improves the repository;
an invented method silently competes with it.

The same rule holds one level down, for operations. **Every step names an operation the
contract defines, or names the gap.** An operation name that exists only in this plan is worse
than an acknowledged absence: no adapter implements it, and the next reader has no way to tell
an invention from a name they simply have not met.

1. **Clarify until success is observable.** Target outcome, a number, a deadline, and the
   constraints: volume limits, brand voice, channels allowed, who must approve what. "More
   pipeline" is not a goal; "ten booked calls with EU SaaS VPs of Eng by 30 September" is.
   - **Settle where the audience will come from here, not later.** A plan whose first work
     item is "find the people", with no named source, is not executable — it stalls on its
     own first step. Ask whether the user has a list, has a sourcing tool of their own, or
     needs the options laid out: `audience-building` → *Where an audience comes from*.
   - Where the objective is somebody's number for the period, it already has a home:
     `goal.get` states the target and the unit the account actually pays on. Plan against that
     number rather than inventing a parallel one nobody is measured against.
2. **Surface the constraints that shape the plan** before designing it. They come from three
   places, and every read here is free — `auto`, changes nothing, costs nothing.
   - **What this login may actually do.** `capability.list` answers per capability: granted,
     denied, or *unknown* — and `unknown` never reads as allowed (`A6`). Authority resolves at
     plan time, not at execution time (`N5`): a plan that assumes an authority it never read
     discovers the refusal at step 40 of 200, with 39 acts done and no defined resumption.
   - **What ceilings this installation works within.** `adapter.describe` states the request
     and batch ceilings, the page sizes, and the scope and lifetime of an idempotency key.
     Limits belong to the installation's own description and never to an operation's
     definition (`N7`), so a plan that hard-codes a batch size has invented a fact. Two reads
     complete the picture: `capacity.estimate` — can the volume this plan implies physically
     run over the horizon, against windows, blackouts, pacing and existing load — and
     `budget.get` for what allowance is left on each meter, where an `unknown` remaining is
     not the same fact as an ample one.
   - **What the domain constrains** — sender maturity and volume ceilings
     (`sending-guardrails`), channel limits (`linkedin-guardrails`), and the approval
     boundaries the user expects (`approval-boundaries`). If the work will run with nobody
     present, read `autonomy.get` while designing it rather than discovering at 02:00 that
     the gate lives nowhere.

   Constraints discovered mid-execution force replanning; constraints known up front shape a
   plan that works.
3. **Compose the strategy** in dependency order. The common backbone is
   `audience-building` → `campaign-launch` → `inbox-triage` as a loop, with
   `performance-analysis` at checkpoints.
   - TODO(expert): which plays fit which situation — new segment versus proven one, cold
     versus warm, single-channel versus multi-channel — and how to sequence experiments so
     each one teaches something.
   - Write the steps in operation names. Thirty core operations sit in front of you in
     `sdr-operations`; the rest are one lookup away through its operation index, or through
     `operation.search` and `operation.describe` at run time. Look a name up rather than
     recalling it — the contract is well past what anyone remembers correctly — and read the
     one family that defines it, not the whole catalog. Where a job already has a worked
     decomposition, `sdr-operations` carries eight of them as end-to-end chains; read the one
     that matches, because the ordering and the gates in it are normative and the rest is
     illustration.
4. **Decompose into work items** that are small, observable and independently resumable. Each
   one states its objective, how you will know it is done, and which operations it performs.
   Anything whose reach is `act` carries an approval gate, named in the plan.
   - **The class is derived, not chosen.** It follows from the operation's reversibility and
     its reach; `approval-boundaries` holds the derivation, and this skill deliberately does
     not restate it, because two copies of a gate is how one of them ends up wrong.
   - Where reach or approval is **conditional** — `campaign.enroll` is bookkeeping into a
     draft campaign and an act the moment that campaign is live — the plan names
     `operation.preview` as the call that resolves it, never an assumption. A preview never
     acts and never spends (`N4`), and it is also where a result an order of magnitude off the
     expectation becomes visible while it is still free.
5. **Decide the checkpoints and the replan triggers.** When to measure, and what would make
   the plan wrong: an assumption refuted, priorities changed, results contradicting the
   premise. A plan with no replan trigger gets followed past the point where it stopped
   making sense.
   - A checkpoint reads `engagement.summarize` over the scope in question, grouped the way the
     question actually needs — the grouping is an argument to one operation, not a different
     operation per grouping.
   - A trigger that exists only in the plan's prose stops nothing when nobody is reading it.
     Where the trigger is a threshold, `stoprule.set` is what makes the work stop by itself,
     and a ceiling set through `budget.set` is a hard stop rather than an alert (`C5`).
6. **Validate the plan before any of it runs.** `plan.validate` returns one verdict per planned
   step — authority, ordering, preconditions, live bindings, budget headroom, capacity — and it
   is the cheapest moment to learn that a later step will refuse. Re-validate after anything
   that moves authority, autonomy, budget or the people named. **Validating is not approving:**
   it says the plan *can* run, never that the user wants it to.

**Where the contract stops, and this is a gap rather than a lookup you have not done yet.** The
contract can *check* a plan (`plan.validate`), but it defines no operation that creates, stores,
revises or supersedes one, and none that records a checkpoint against it. Say that plainly
rather than naming an operation that would be convenient. Persisting a plan is a runtime
concern and belongs to `durable-work`; where no runtime is installed the plan lives wherever the
user keeps it, and the plan text says which of the two is true.

## Execution guidance

Get the user's approval of the **plan text** before the first work item runs. That approval
authorises reaching the gates inside the plan; it does not pass through them — each `act` step
still stops and asks when it is reached (see `approval-boundaries`). Treating a plan approval
as covering its `act` steps is the commonest and most consequential mistake in this whole
workflow.

Then work item by item, in dependency order, via the referenced skills. Record identifiers as
soon as they exist, together with the idempotency key each act went out under, so that an
interruption costs one step rather than the whole run: the key is what lets a lost result be
recovered by lookup instead of re-run.

**Replanning preserves completed work.** When the plan changes, the finished parts stay
finished: supersede the plan rather than rewriting history. If a runtime is available it
defines how (`durable-work`); if not, the same principle applies to however the user is
keeping track — a new revision, not an edit over the old one.

## Validation

The plan is sound when: success criteria are observable; every work item has completion
criteria; every step whose reach is `act` carries the approval gate the derivation gives it,
with a preview where one is required; each step names an operation that resolves against the
contract or states an explicit gap; the constraints in step 2 were read rather than assumed;
`plan.validate` returns a verdict for every step and no step comes back unauthorised; and the
user has approved the plan text.

## Reporting

The plan itself is the first artefact. After that, report per checkpoint against the plan:
what was completed, what deviated and why, which assumptions held. Deviations are the
interesting part — a report that only confirms the plan teaches nothing.

Report the **operations performed with their per-item outcomes**, never the calls made — that
is what is still readable a month later and still comparable after a change of provider. A
partial result reported as one verdict is a defect, not a rounding.

## Failure modes

- **Planning a goal that was never clarified.** Everything downstream inherits the vagueness.
- **A plan with no approval gates** on steps that contact real people.
- **Inventing methodology** when an existing skill covers it, producing two competing
  approaches in the same repository.
- **Inventing an operation name** because the plan needed one and looking it up felt slow. The
  step is unexecutable, and it fails at run time rather than at review time.
- **Planning against authority nobody read** — assuming a capability, a ceiling or a batch
  size instead of reading `capability.list` and `adapter.describe`. The plan then discovers
  its limits mid-run, which is the one moment when replanning is most expensive.
- **Reading `unknown` as permission.** A capability that could not be determined and an
  allowance nobody publishes are answers in their own right, and neither of them is a yes.
- **Work items too large to resume** — "run the campaign" is not a work item.
- **No replan trigger**, so the plan is followed after its premise has been refuted.
- **Assuming a workspace exists.** If nothing durable is available, say what the user must
  keep track of themselves instead of silently relying on it.

## Safety

Planning itself changes nothing and is always safe. Every operation this skill performs in its
own right — the constraint reads of step 2, and `plan.validate` at step 6 — has reach `read`,
derives `auto` and costs nothing, so the whole plan can be checked before any part of it is
approved. The plan's responsibility is to make every irreversible or outward-facing step
visible as a gate *before* execution starts, so the user is never surprised by what a plan does
— that is inherited from the safety sections of the skills being composed, and defined in full
in `approval-boundaries`.

## Related skills

- `sdr-operations` — the vocabulary a plan is written in, the five properties each operation
  carries, the index that finds the ones not in front of you, and the worked chains.
- `approval-boundaries` — where the gates in a plan come from, what a preview must contain,
  and what changes when the plan runs with nobody present.
- `audience-building` · `campaign-launch` · `inbox-triage` · `performance-analysis` — the
  usual building blocks.
- `durable-work` (in `agentic-runtime`, optional) — where plans and work items physically
  live, and how progress survives a session ending.

## Changelog

- 2.2.0 (2026-08-10): planned against the 325-operation contract. The plan now resolves its
  constraints as reads before it is designed — `capability.list` for what this login may do,
  `adapter.describe` for the ceilings this installation works within, with `capacity.estimate`
  and `budget.get` alongside them — because authority resolves at plan time and a plan that
  assumes it finds out at step 40 of 200. Adds a validation step: `plan.validate` gives a
  verdict per planned step before any of it runs, and validating is explicitly not approving.
  A conditional gate is now resolved by `operation.preview` and never by assumption, a
  checkpoint reads `engagement.summarize` with the grouping as an argument, and a threshold
  replan trigger is made real by `stoprule.set` rather than left in prose. Corrects the
  property name: a step's `effect` is now its **reach**, and its approval class is derived
  from reversibility by reach rather than chosen — the derivation stays in
  `approval-boundaries` and is not restated here. States as an explicit gap that the contract
  can check a plan but has no operation that stores, revises or supersedes one.
- 2.1.0 (2026-08-02): step 1 now requires the audience source to be settled during
  clarification. A plan that opens with an unnamed "find the people" stalls immediately,
  which is how a goal stated in one sentence turned into a refusal one step later.
- 2.0.0 (2026-07-30): renamed from `outbound-campaign-planning` and rewritten for the
  `ai-sdr-core` pack. The durable-work and workspace mechanics moved out to `durable-work` in
  `agentic-runtime`; the Reply CLI dependency is gone, so planning now works with any
  provider and with no runtime at all. Constraint-surfacing and replan triggers strengthened.
- 1.0.0 (2026-07-27): initial version as `outbound-campaign-planning`.
