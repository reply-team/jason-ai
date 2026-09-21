---
name: sending-guardrails
description: >
  Protect sender reputation and inbox placement: domain authentication, warm-up, volume
  pacing, bounce interpretation and recovery. Use before launching significant email volume,
  when bounce rates rise, when replies collapse, or when a sending account or domain is new.
metadata:
  version: 0.3.0
  pack: ai-sdr-core
  category: protection
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [deliverability, reputation, warm-up, volume, protection]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [campaign-launch, performance-analysis, approval-boundaries]
---

# Sending guardrails

> **Expert skeleton.** The structure, the safety posture and the decision points are final.
> Every number and every playbook is `TODO(expert)` and must be validated by the deliverability
> experts who own this skill before it leaves `draft`. Visible gaps are deliberate;
> invented thresholds would be worse than none. What changed at 0.3.0 is that each missing
> number now names the operation it will be configured through — which is not the same as
> having the number, and is not progress towards `production`.

## Purpose

Deliverability is an asset that burns in days and rebuilds over months. This skill encodes the
constraints automated outreach must respect so it never damages the user's domain, and the
moves to make when the signals start degrading.

It states **which operation carries each constraint** and **no thresholds of its own**. That
split is deliberate: the operations are stable and vendor-neutral, the numbers are account
policy that an expert sets, and a figure written into prose here would be a number with nobody
behind it.

## When to use / when NOT to use

- Use before any significant send volume, for new domains and new sending accounts, and
  whenever bounce or engagement figures degrade.
- Use as a constraint on planning, not as a step in it: these limits shape what a campaign is
  allowed to be.
- Not a substitute for `performance-analysis` — that judges campaign *effectiveness*; this
  skill is about sending *health*. A campaign can be perfectly written and still be
  undeliverable.
- Not a place to look up an approval class. The derivation lives in `approval-boundaries` and
  is read there, not reconstructed here. What this skill adds is which action to reach for
  first, which is a different question from what it costs to ask.
- Not a statement of what any provider supports. An operation named here is a job an SDR
  organisation does; whether an installation fulfils it is the adapter's claim to make.

## Prerequisites

- Visibility of the sending capabilities — `sender.list`, `sender.get`, `sender.health` — and
  of the domains sent from, `sending_domain.list`. All four are `read` and `auto`; there is
  never a reason to ask before looking.
- The user, or whoever owns their DNS, for the authentication step.
  `authentication_requirement.list` returns what has to be published; **publishing it is not
  ours to do**, and that part cannot be automated away.

## Planning guidance

Answer before sending anything. If an answer is unknown, resolve it **before** volume, not
after. Each question has an operation behind it, and each of those is a `read`:

- **Is the sending domain authenticated — SPF, DKIM, DMARC present and aligned?**
  `domain_compliance.check` answers per publisher, per requirement, as pass, fail or unknown,
  and names the failing requirement. `authentication_requirement.list` says what must be
  published to clear it.
- **How old is the sending account, and how far into warm-up is it?** `sender.health` returns
  the warm-up position and whether a recorded restriction invalidated it (`F4`).
- **What daily volume is this capability actually ready for?** `sender.health` returns capacity
  as nested budgets, each with its scope, window and unit — never a single daily number (`F3`).
  A remaining allowance of `unknown` is a real answer and it does not read as room (`A6`).
- **Is the list clean — addresses verified, hard bounces already out?** `email_address.verify`
  gives the verdict, and `accepts_all` is terminal: no amount of re-checking resolves it and
  only a real send does (`B8`). What to do about accepts-all, role and free-mailbox addresses
  is a declared policy, never a default (`D5`).

Open expert questions:

- TODO(expert): recommended warm-up ramp — duration and daily volume curve — by account age.
- TODO(expert): safe daily ceilings per mailbox provider.
- TODO(expert): how many accounts to spread a given volume across, and when adding a domain is
  preferable to raising a limit.

These stay open on purpose, and each has a home to go to once an expert closes it. The ramp is
configured through `warmup_plan.set`, the ceilings through `sender_limit.set` — so an expert
answer becomes account configuration rather than a figure copied into a skill. `F11` is why the
first one cannot be shortcut: no ramp figure is published for a mailbox by anybody, and every
figure in circulation is folklore.

On the third question, two facts constrain the answer without supplying it. Volume is spread
with `sender_pool.create` and `sender_pool.update` — and `F10` stands over that: rotation is
not a cure, because where a budget's scope is the pool, the organisation or the recipient,
rotating across capabilities moves the problem. Two capabilities do not necessarily give twice
the capacity (`F3`).

## Execution guidance

1. **Capability health check** (`sender.health`, with `sender.get` for the configured pacing
   and message defaults). Connection state, restrictions with their cause or `undisclosed`,
   warm-up position, capacity budgets, and whether the channel sanctions automation at all. A
   disconnected or erroring capability is a **hard stop**, not a warning to work around.
2. **Authentication and required elements.** `sending_domain.register` declares a domain we
   send from and `sending_domain.list` says which ones those are with each one's compliance
   state as of when. `domain_compliance.check` then answers per publisher, per requirement, and
   `authentication_requirement.list` says what must be published to fix a failure — by the
   user, or by whoever owns the zone. Missing DKIM or DMARC must be fixed before volume.
   - A passing header check is not a passing compliance check. The channel's required stop
     mechanism is several separate artefacts — on email a header, a machine-actionable endpoint
     that actually honours a request, and a visible link — each separately required, configured
     and checked (`F7`). `message_element.set` configures them; `message_compliance.check`
     checks the **rendered** message, including the reputation of every domain it links to,
     because tracking domains and shortened links are reputation-bearing separately from the
     sending domain.
   - TODO(expert): recommended DMARC policy progression and what to monitor at each stage.
3. **List hygiene.** `email_address.verify` before sending; hard bounces come off the lists
   with `list_membership.remove` and, where the address must never be touched again,
   `suppression.add` — which is `auto` for a single verified identifier precisely because it is
   protective. Verification is metered per address checked, so it is planned rather than
   sprayed, and it never delivers anything to the person being verified (`B8`).
   - TODO(expert): validation thresholds, and re-validation cadence for an ageing list.
4. **Placement check, before volume rather than after.** `placement_test.create` sends to a
   monitored set of addresses; `placement_test.get` returns where it landed, per mailbox
   provider. Two properties govern how the result may be used: it is `act`, `confirm_each` and
   metered per test — it is a send and is gated as one — and its output is typed as **simulated
   placement, never our inbox rate** (`M2`). Test sends and placement tests are excluded from
   every denominator (`M8`). Read it as a check on configuration, not as a measure of the
   campaign.
5. **Pacing.** Spread volume across the sending window; avoid spikes. A burst that clears a
   provider's rate limit can still damage reputation. The capability's own ceiling is
   `sender_limit.set` — how much, how fast and with what spacing, one decision rather than
   three — and `pacing.set` is the governor beneath it for a single campaign. Pacing may only
   *lower* the effective rate; the capability's limit is the ceiling (`F1`). Where parallel work
   shares a capability, `sender.reserve` and `sender.release` stop two runs spending the same
   allowance twice.
   - TODO(expert): concrete ramp rules after a pause, for a new segment, and after a
     deliverability incident.
6. **Watch and react.** `sender.summarize` gives this capability's numbers over a window —
   failures by class, complaints with their publisher and denominator, deferrals, throttles.
   `domain_reputation.get` gives the named signals about the domain, each with its publisher,
   scale, window and denominator, including blocklist listings and the dataset each came from;
   it is metered where a signal comes from a paid dataset. `engagement.summarize` carries the
   grouping as an argument, so the trend by mailbox provider is one call — and reputation
   problems usually appear at one provider first. When the platform restricts a capability,
   including a restriction we only learned about from a refusal, `restriction.record` writes it
   down; it is `auto` (`K2`) and it is what invalidates the warm-up position (`F4`).
   - TODO(expert): the alert thresholds at which sending must pause automatically.
   - That last one has an operation waiting for it: `stoprule.set` declares what is measured,
     over what window, at what threshold, and what it halts — and `L8` names sender health,
     domain reputation and remaining budget as legitimate subjects. Two things it must be, not
     one: a rule that only warns is a different feature from one that stops (`C5`), and a
     firing leaves a record — the trigger value, the operations halted, every item left
     unprocessed — read afterwards through `stoprule_firing.list` and `stoprule_firing.get`.
     The threshold is still the expert's to set; the mechanism is not missing.

## Validation

After any change: failure classes and complaint signals from `sender.summarize`, the domain
signals from `domain_reputation.get`, and the engagement trend grouped by mailbox provider from
`engagement.summarize` — each over a stated window. Guardrails are working when volume grows
without those degrading, not when nothing has visibly broken yet.

Four rules decide whether a figure is evidence at all:

- Every rate carries its publisher, its denominator basis, its window and its as-of; a bare
  number is not a legal value (`F6`).
- A rate never travels without its denominator count and its typed insufficient-volume flag
  (`M4`). A move that sits inside the interval is not a signal.
- **No composite reputation score is ever manufactured** (`F6`). Complaint rate, failure rate
  and a reputation signal come from different publishers over different windows on different
  denominators, and a single "sender health score" would be an invented quantity.
- Sends that did not happen are first-class facts, by reason — deferred, cap-skipped,
  blackout-skipped, refused at the gate, throttled by capacity (`M5`). Without them, throttling
  reads as an improvement: the rate holds because the denominator collapsed.

`placement_test.get` is read against configuration, never quoted as an inbox rate (`M2`).

## Reporting

Report the operations performed with their per-item outcomes: the capabilities and domains
involved, volumes per capability, failure and complaint figures with their envelopes, and any
protective action taken together with the signal that triggered it and when the user was told.
Include any `restriction.record` written and any stop-rule firing, with what it left
unprocessed.

Deliverability incidents always produce a report: they are the highest-value evidence this
category has, and the only way the thresholds above eventually stop being `TODO(expert)`.

## Failure modes

- **Rising hard bounces mid-campaign** → `campaign.pause` on the affected campaign first,
  diagnose second. Pausing is reversible; a burned domain is not.
- **Fixing deliverability by rewriting copy.** If delivery is failing, the message is not the
  problem, and rewriting it wastes the window in which the domain could have been saved. The
  order in which the axes are eliminated is worked through in `sdr-operations`, in the chain
  for diagnosing a drop in reply rate — deliverability before targeting before copy, because
  each step can invalidate the question the next one would answer.
- **Raising a limit to hit a number.** Volume ceilings exist because of provider behaviour,
  not because of ambition.
- **Sending from a fresh account at full volume** — the single fastest way to burn a new domain.
- **Returning to pre-restriction volume the moment a block lifts.** Platforms escalate on the
  *frequency* of restrictions, not their type, and the recurrence count survives clearance
  because recovery time scales with it (`F4`). This is the standard way a temporary restriction
  becomes a permanent one.
- **Rotating to escape a budget.** Adding capabilities to a pool does not add capacity where the
  budget's scope sits above the sender (`F10`).
- **Quoting a placement test as an inbox rate.** It is the closest thing to the number everyone
  wants, and it is a different fact (`M2`).
- TODO(expert): blocklist recovery playbook — identification, delisting, cool-down.
  - **Gap, and it is a gap in the contract rather than only in this skill.** Identification is
    covered: `domain_reputation.get` reports a listing with its publisher and the dataset it
    came from. **Delisting is not an operation.** `restriction.appeal` is scoped to a platform
    that has restricted a *sending capability*, not to a third-party blocklist operator, and
    there is no other candidate — do not stretch it to fit. A delisting request is a human
    errand performed outside this contract: do it, then record what was asked and what came
    back, and name the absence in the report rather than an operation that does not exist.

## Safety

The protective floor applies here, and it is narrower than it first looks. When a signal crosses
a danger threshold the protective action is taken **first** and the user told immediately,
because inaction is the destructive choice. The derivation, and the protective set in full, are
in `approval-boundaries`; read them there rather than reconstructing them here.

What this skill adds is *which* action that is.

**The first move in an incident is `sender.pause`** — stop the capability sending — and
`campaign.pause` where the problem is campaign-shaped rather than sender-shaped. Both are
`auto`: neither requires a lookup first and neither waits for permission (`L6`).

**Lowering a limit is not that move, and it is not free.** `sender_limit.set` is `confirm_once`
in **both directions**. The contract raises it above the derivation for a stated reason — one
call declares how much a capability may send, how fast and with what spacing, over limits
somebody else may have declared — and **the raise is not conditional on which way the number
goes**. A reduction is protective in shape and still gated. That is the edge of the floor, and
it is exactly why the incident move is the pause: reaching for a lower limit buys a gate you did
not need and leaves sending running while you wait at it. Pause first, then set the limit with
the user. `warmup_plan.set` is raised for the same class of reason, because redefining the ramp
overwrites the state that governs whether the capability may send at all.

**The restart direction always waits** (`L6`). `sender.resume` is `confirm_once`, and it refuses
outright while the warm-up position is invalid unless a ramp restart is supplied or an override
is recorded with an actor and a reason (`F4`). `campaign.resume` carries the same asymmetry.
This is deliberate, not an oversight to be tidied up.

**Recording is not deciding.** `restriction.record` is `auto` under the floor: it writes down
something that already happened outside our control, and delaying it delays the invalidated
warm-up position and the recurrence count that both hang off the record (`K2`). The same holds
for `suppression.add` on a single verified identifier.

**A placement test is a send.** `placement_test.create` reaches `act`, is `confirm_each`, and is
metered per test. It goes to monitored addresses rather than prospects, and it is gated as the
send it is.

## Related skills

- `sdr-operations` — the operations named above, their five properties, the invariants cited
  here by ID, and the worked chain for diagnosing a drop in reply rate.
- `approval-boundaries` — the derivation, the protective floor and what a valid confirmation
  looks like. The thresholds whose breach triggers protective action are this skill's to
  supply; the gate is that skill's to state.
- `campaign-launch` — consults this skill before going live.
- `performance-analysis` — routes deliverability diagnoses here.

## Changelog

- 0.3.0 (2026-08-10): migrated to the 2.0.0 operation contract. `sender.limits.read` became
  `sender.get`, and the pacing step now names `sender_limit.set` (formerly
  `sender.limits.update`) with `pacing.set` as the governor beneath it (`F1`). **The 1.x safety
  rule that lowering a limit is protective and therefore ungated is void**: the contract
  declares `sender_limit.set` `confirm_once`, raised above the derivation for a stated reason,
  and the raise is not conditional on direction. The operational consequence replaces it — the
  first move in an incident is `sender.pause`, which is `auto`. Every threshold that previously
  had nothing behind it now names its operation: `warmup_plan.set` for the ramp,
  `sender_limit.set` for the ceilings, `stoprule.set` with `stoprule_firing.list` for the
  automatic-pause thresholds, `sender.summarize`, `domain_reputation.get` and `restriction.record`
  for the watch step, `sending_domain.register`, `sending_domain.list`,
  `authentication_requirement.list`, `domain_compliance.check`, `message_element.set` and
  `message_compliance.check` for authentication and required elements, and
  `placement_test.create` with `placement_test.get` for placement. Declares one gap: **no
  operation requests delisting from a third-party blocklist** — `domain_reputation.get` reports
  the listing and its dataset, and `restriction.appeal` is scoped to a platform restricting a
  sending capability, not to a blocklist operator. `sequence` is a retired word, so the bounce
  failure mode now pauses a campaign. Stays `0.x` and `draft` deliberately: no threshold in this
  skill has been validated, and naming the operation a number will live in is not the same as
  having the number.
- 0.2.0 (2026-07-30): renamed from `email-deliverability` and made vendor-neutral for the
  `ai-sdr-core` pack — Reply endpoints moved to `reply-operations-mapping`. Reframed as a
  constraint on planning rather than a workflow step; safety now defers to
  `approval-boundaries`. Still an expert skeleton: no threshold has been validated.
- 0.1.0 (2026-07-27): expert skeleton — structure and safety posture.
