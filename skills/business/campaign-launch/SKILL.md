---
name: campaign-launch
description: >
  Prepare and launch an outreach campaign with every precondition checked and the user's
  explicit approval of what will actually be sent. Covers sending capability, step content,
  schedule, the test send, validation, enrolment and the go-live gate. Use when the user wants
  to create or start a campaign, add contacts to one, or begin sending — including when they
  call it a sequence, a cadence or a drip.
metadata:
  version: 3.0.0
  pack: ai-sdr-core
  category: strategy
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [campaigns, launch, sending, cadence]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations, approval-boundaries]
    recommends: [campaign-planning, audience-building, sending-guardrails, linkedin-guardrails, performance-analysis]
---

# Campaign launch

> **Partly an expert skeleton.** The launch procedure and its gates are complete. Message
> strategy — cadence, channel mix, what makes an opener work — is marked `TODO(expert)` and
> needs validation by outbound experts before this skill leaves `draft`.

## Purpose

Launching is the moment automation starts contacting real people using the user's domain and
accounts. This skill makes that moment deliberate: capability verified, content the user has
actually read, timing understood, the message received once by somebody who owns the address,
and an explicit go-ahead against a preview that names who will receive what.

## When to use / when NOT to use

- Use to prepare or start a campaign, and to enrol contacts into one.
- Do NOT use to turn a business goal into a plan — that is `campaign-planning`.
- Do NOT use to build the audience first — that is `audience-building`.
- Do NOT use to analyse a running campaign — that is `performance-analysis`.
- Do NOT build a one-person campaign to send one message. That is `message.send` — one person,
  one channel per call, `confirm_each`. **Earlier versions of this skill called this a contract
  gap; it is not one any more.** A one-person campaign built to work around an absence that has
  since been closed costs more and says less than the send it imitates.

## Prerequisites

- An audience that exists, is defined, and is **frozen as a list** — a segment is criteria, a
  list is a frozen fact with a date on it, and outreach runs against frozen membership (D1).
  See `audience-building`.
- Sending capability that is healthy — read with `sender.health`, never assumed.
- Deliverability sanity for meaningful volume or a fresh sender (`sending-guardrails`); the same
  for social steps (`linkedin-guardrails`).
- The operations this skill composes: `campaign.create`, `campaign.clone`, `step.add`,
  `variant.add`, `template.create`, `template.publish`, `schedule.set`, `pacing.set`,
  `sender_binding.set`, `reply_policy.set`, `message_element.set`, `sender.health`,
  `capacity.estimate`, `message.draft`, `content_policy.check`, `message_compliance.check`,
  `campaign.test`, `campaign.validate`, `outreach.precheck`, `audience.screen`,
  `operation.preview`, `campaign.enroll`, `stoprule.set`, `campaign.activate`, `campaign.get`,
  `step.list`, `schedule.resolve`, `enrollment.list`, `engagement.summarize`.

## Planning guidance

Settle with the user before building anything:

1. **Reuse or create.** A campaign that already performs is usually a better starting point than
   a new one. `campaign.clone` copies structure, steps, variants, schedule, pacing and reply
   policy, and copies **no** enrolments, history or results — which is what makes reuse safe.
   Either way the campaign is a draft: nothing about authoring implies sending (E1).
2. **Which sender, which schedule.** Volume spread across the sending window, in the recipients'
   working hours rather than the user's. Whose timezone and whose holidays is a policy with an
   ordered fallback, not a value (E8), and legal quiet hours resolve in the recipient's local
   time whatever the operator would prefer (A20).
3. **Which contacts** — the frozen list, or the criteria plus the list they were drawn from. The
   count matters, but the count is not what the user approves; the preview names them.
4. **The content, written with the user.** Never invent outreach copy silently and never send
   copy the user has not read. Drafting together is part of the work, not a preliminary.
   - TODO(expert): what makes a first message earn a reply — structure, length,
     personalisation depth, and which openers to avoid.
5. **Cadence and channel mix.** A step is channel × execution mode × kind, and the three are
   independent (E9); every interval carries a unit, a stated basis — calendar days, working days
   or sending days — and whose calendar (E7).
   - TODO(expert): step count and intervals by segment and channel; when a channel should
     lead versus follow; how long to keep following up before stopping.
   - The contract answers none of that on purpose. Cadence length, touch count and days between
     are inputs to `step.add`, per campaign, because no credible cross-vendor standard exists —
     the contract ships the shape and no number. The marker above stays until an outbound expert
     supplies one; it is not a lookup that somebody forgot to do.

**The user's word for it is an input, not the concept.** "Sequence", "cadence", "drip", "play" —
`term.resolve` maps the word onto the concept and the plan is then written as `campaign`.
`sequence` is retired rather than aliased: a plan step that names it names something that does
not exist.

## Execution guidance

**The order is not in this skill, and must not be copied into it.** Chain 1 in the
`sdr-operations` reference `references/chains.md` — *Launching outreach to two hundred people
from a raw file* — is the launch chain, with the decision, the gate and the prevented failure at
each of its 26 steps. Read it there and follow it there. An order written in two places drifts,
and the copy that drifts is always the one further from the contract.

What follows is only what this skill adds: the judgement the chain deliberately leaves out, and
the steps a launch plan most often drops.

### Sending capability is a hard stop, not a warning

`sender.health` first. No healthy sender is a **hard stop** — the user must connect one. Never
attempt to send anyway, and never route around it. A capacity budget reporting `remaining:
unknown` is a real answer and is not the same fact as ample remaining (A6). Where authentication
fails, `authentication_requirement.list` names what has to be published — **publishing it is not
ours to do.** It happens in a system this contract has no reach into, usually owned by another
team, and handing that errand over with the requirement named is the correct end of this branch,
not a failure of the skill.

### The steps a launch plan usually omits

Each exists to prevent one specific failure. A step whose failure cannot be named is a step
somebody added because it felt careful; these three are not that.

| Step | The failure it prevents |
|---|---|
| `campaign.validate`, before activation | Turning on a campaign that cannot run: missing or unpublished content, steps no sender can serve, a schedule that never opens, and a campaign with **no terminal condition** — a programme that never ends is a defect, not a long campaign (E4). It also returns the count already enrolled, projected first-24-hour volume and projected metered cost. Validation is a **report**; the gate is a human reading it. |
| `campaign.test`, to an address the workspace can demonstrate it owns | Discovering an unresolved merge field, a mangled footer or a dead unsubscribe link *after* two hundred strangers received it. Receive step 1 yourself and click the unsubscribe link. It is a **real send** consuming real capacity, excluded from every measurement denominator and never counted against a recipient's frequency cap (E5). `campaign.validate` carries open question 12 — whether the test is a hard precondition of a passing validation or a strong recommendation is unsettled; say so rather than deciding it silently inside a plan. |
| `outreach.precheck`, before enrolment | Enrolling somebody we may not contact at all — suppressed, no lawful basis, a privacy notice owed, outside their quiet hours, over a frequency cap. **Every failed gate is returned, never only the first** (A8), and `deny_with_remedy` is a different answer from `deny`: a notice owed is normally satisfied by rendering the notice into the outreach message, not by sending a standalone one (A18). |

A fourth is not optional either: `audience.screen` runs **twice** — once when the list is frozen
and again immediately before enrolment (D4). On a launch assembled over a week, somebody always
opted out in between.

And none of those passes is a permission. A pass reserves nothing and does not survive: the gates
are evaluated again immediately before the act, and a batch prepared against an earlier check is
re-screened at dispatch (A7).

### Content is checked as the artefact that actually goes

`message.draft` renders step 1 against a named real contact — merge fields resolved, fallbacks
applied or reported missing, footer, identification block and unsubscribe mechanism in place.
`content_policy.check`, `message_compliance.check` and `disclosure.check` all bind that rendered
message, never the template behind it (E14). `message_element.set` is where the mandatory
elements live, and the stop artefacts are **separate** artefacts — a passing header check is not
a working unsubscribe (F7).

Complex step editing is no longer a contract gap. `variant.add` and its siblings cover variant
experiments, and multi-channel work is expressed by the step's own channel and execution mode
(E9). Where a step genuinely cannot be expressed, name the gap and hand that part to the user in
their own interface — but check first, because the two gaps this skill used to declare are both
closed.

### Enrolment carries three decisions with no default

`campaign.enroll` takes an explicit **collision policy**, an explicit **start position** and an
explicit **first-touch timing**; none of the three may be defaulted (G7). Reconcile every
per-item outcome — created, reactivated, skipped, each with its reason — and explain each skip. A
partial success reported as one verdict is a defect (N2).

**Enrolling into a live campaign is itself sending** (G2): the reach is bookkeeping while the
campaign is not live and `act` the moment it is, and which case you are in is resolved by
`operation.preview`, never assumed.

There is no operation that sets an enrolment's position, and that absence is a refusal rather
than an oversight (G8): "start them again from step 1" is `enrollment.stop` with a reason plus a
fresh `campaign.enroll`, which keeps the ended participation as history instead of asserting one
that never happened.

### The go-live gate

Read this before quoting any older description of it, because the previous version of this skill
was wrong about it. `campaign.activate` is **`confirm_once` with a mandatory preview**. It is not
"the strictest level" of a three-level scale — that scale, and the level it called exact-text,
no longer exist. Approval is now derived from reversibility by reach, and this cell derives one
human decision carried by an artefact.

- **Magnitude, not count of calls.** Activating a campaign holding N enrolments is an act of
  magnitude N (E2). One call, one decision — and the decision is about a population.
- **The preview is the approval artefact, and it names the population rather than counting it.**
  Who, identified individually where the set is small enough to read and otherwise by the exact
  criteria plus the frozen list with its freeze date; what would reach them, as the rendered
  message; when the first touch lands and in whose calendar; what it consumes, including
  `unknown` remaining; and whether the effect resolves to a send at all. Pacing usually makes
  projected first-24-hour volume far smaller than the enrolled count, and that number belongs in
  front of the user too.
- **This is not a weakening.** The old gate asked for a string to be matched, which a user can
  satisfy without having understood anything; the new one puts the people and the message in
  front of them. A preview naming who receives what can be refused on a ground the user can
  actually see — not that list, not that opening line, not at 06:00 in their timezone. A prompt
  nobody reads cannot be refused on any ground at all. **"200 contacts" is not a preview**, and
  offering one is how a `confirm_once` gate becomes decorative.
- **E2 is what keeps it honest.** Enrolling into a draft and then activating must **never** be a
  cheaper approval than enrolling into a live campaign. If the two-step route ever costs fewer
  approvals, somebody has built a way to send to a population without ever approving that
  population — and the edit that does it will look like a simplification.
- A preview is not a reservation. One that has gone stale is re-run, not re-used (A7).

The derivation itself, what a valid confirmation looks like and the release-versus-composition
distinction are in `approval-boundaries`. Do not restate them here.

### Before walking away

Confirm the state actually flipped by reading it back, and set what stops the work by itself:
`stoprule.set` over bounce rate, complaint rate, negative-reply rate, opt-out rate, error rate,
spend velocity, sender health, domain reputation and remaining budget (L8). A stop rule set after
the first batch is a stop rule that did not cover it.

## Validation

Re-read after every mutation. A launch is not confirmed by the absence of an error — it is
confirmed by reading the state back.

- `step.list` and `campaign.get`: steps present and in order, sender bound, schedule attached,
  pacing set, reply policy set. A campaign that silently lacks a schedule is a classic surprise,
  and `schedule.resolve` shows the actual calendar day by day, which is where a window that never
  opens becomes visible.
- `campaign.validate`: no blocking finding, and a terminal condition exists (E4).
- `enrollment.list`: the enrolled count matches the population the preview named — not merely a
  count that looks plausible.
- `campaign.get` after activation: live, with a first-touch ledger consistent with the preview.
- The next morning, `engagement.summarize` — and **read the did-not-happen counts first** (M5):
  deferred, cap-skipped, blackout-skipped, refused by the gate, throttled by capacity. Without
  them, "nothing errored" and "sixty of two hundred were never attempted" look identical.

## Reporting

Campaign identifier and name, steps created, the test send and its result, the validation
findings, contacts enrolled and skipped with a reason per item, sender and schedule used, the
preview that carried the go-live approval, and the idempotency key each act went out under — the
key is what lets a lost result be recovered through `invocation.get` rather than re-run.

Then the next checkpoint: when to run `performance-analysis` once enough volume has accumulated.
Every rate carries its denominator count and a typed insufficient-volume flag with the threshold
used (M4), so "too early to say" is a reportable answer rather than a guess.

## Failure modes

- **Launching without verified sending capability.** Produces silent non-delivery or a
  reputation hit.
- **Sending copy the user has not read.** The one mistake that cannot be undone.
- **Counting instead of naming at the go-live gate.** A number the user can neither check nor
  refuse turns the gate into a reflex.
- **The draft-then-activate arbitrage** — enrolling into a draft to avoid the gate on enrolling
  into a live campaign. The magnitude did not change; only the prompt did (E2).
- **Assuming enrolment is harmless** when the campaign is already live (G2).
- **Partial enrolment reported as success**, hiding the contacts that never made it (N2).
- **Skipping the test send** because the copy was read carefully. Reading a template is not
  receiving a message, and every check binds the rendered message (E14).
- **Treating a passed precheck as permission to send later.** A pass reserves nothing (A7).
- **A missing schedule** discovered after launch, when messages went out at the wrong time.
- **Volume that ignores sender maturity** — see `sending-guardrails`; a burned domain outlasts
  the campaign.

## Safety

Activating a campaign, enrolling contacts into a live one, resuming a paused campaign and
lifting a freeze are all **releases of sending**, and each is approved against a preview that
names its backlog (E3). Pausing is the cheap direction: `campaign.pause` and `enrollment.pause`
are `auto` and never wait for permission, while `campaign.resume`, `campaign.unfreeze` and
`enrollment.resume` each wait. That asymmetry is deliberate rather than an oversight to be tidied
up (L6).

`campaign.archive` is refused while active enrolments remain unless those are first stopped with
a reason — retiring a campaign is its own act with its own confirmation, never a tail-end step of
a launch. A single message to one named person is `message.send` at `confirm_each`, which is a
different gate from anything on this page. Full rules: `approval-boundaries`.

## Related skills

- `campaign-planning` — turns a business goal into the plan this skill executes.
- `audience-building` — the audience must exist and be frozen first.
- `sending-guardrails` · `linkedin-guardrails` — the limits that constrain volume and pacing.
- `performance-analysis` — the follow-up once the campaign runs.
- `approval-boundaries` — the derivation behind the go-live gate, and what a preview must
  contain.
- `sdr-operations` — the operations named here, the invariants cited by ID, and chain 1 for the
  order.

## Changelog

- 3.0.0 (2026-08-10): breaking, against the 325-operation contract. Every operation this skill
  named was renamed: `sequence.create` → `campaign.create`, `sequence.add-step` → `step.add`,
  `sequence.assign-sender` → `sender_binding.set`, `sequence.assign-schedule` → `schedule.set`,
  `enrollment.create` → `campaign.enroll`, `sequence.activate` → `campaign.activate`,
  `sequence.state` → `campaign.get` with `step.list` and `enrollment.list`. **The six-step launch
  procedure is removed and replaced by a pointer to chain 1 of the contract's worked chains**, so
  the ordering is stated in one place; what stays here is the judgement the chain leaves out.
  Adds the three steps the old procedure omitted, each with the failure it prevents:
  `campaign.validate`, `campaign.test` and `outreach.precheck`, plus the second
  `audience.screen`. **The go-live gate is corrected**: it was described as exact-text approval,
  "the strictest level", and that level no longer exists — `campaign.activate` is `confirm_once`
  with a mandatory preview naming the population, an act of magnitude N, with E2 stated as the
  rule that stops draft-then-activate becoming the cheaper route. **Both gaps this skill declared
  are closed**: one-off individual messages are `message.send`, and variant experiments and
  multi-channel steps are `variant.add` and the step's own channel and execution mode. Notes that
  the contract ships no cadence figures at all, which is why the message-strategy markers remain
  open rather than being resolvable by lookup.
- 2.0.0 (2026-07-30): renamed from `launch-outreach` and rewritten vendor-neutral for the
  `ai-sdr-core` pack — Reply endpoints, scopes and CLI calls moved to
  `reply-operations-mapping`. Safety rules now defer to `approval-boundaries` rather than
  restating them; message-strategy gaps marked for expert validation.
- 1.0.0 (2026-07-27): ported from the prototype workflow as `launch-outreach`.
