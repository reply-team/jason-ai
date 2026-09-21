---
name: linkedin-guardrails
description: >
  Keep social accounts safe under automation: invitation pacing, messaging windows, metered
  allowances, daily limits and recovery when the platform restricts an account. Use before any
  LinkedIn outreach volume, when configuring account limits, or when an account shows a warning.
metadata:
  version: 0.3.0
  pack: ai-sdr-core
  category: protection
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [linkedin, limits, pacing, protection]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [campaign-launch, approval-boundaries, sending-guardrails]
---

# LinkedIn guardrails

> **Expert skeleton.** Structure and safety posture are final. Every pacing number and every
> recovery playbook is `TODO(expert)` and needs validation by the outbound experts who own this
> skill before it leaves `draft`.

## Purpose

A restricted social account can cost the user their professional network — an asset that is
not replaceable and not insured. Automation has to stay well inside platform behavioural
limits, with a wide margin rather than a narrow one. This skill encodes those constraints and
what to do when warnings appear.

**The skill names the rule; the channel supplies the value.** Allowances, tiers, whether
automation is sanctioned at all and how long a reply window stays open are properties of the
platform and the account, not of this skill. `channel.describe` is the one source of those
facts (`E15`), and a number written here instead would be wrong for somebody within a quarter.

## When to use / when NOT to use

- Use before enabling social steps in a campaign, when setting account limits, and on any
  account warning.
- Email-only campaigns do not need this skill.
- The specifics here are social-channel-shaped because that is the channel in use; the posture
  — wide margins, protective action first, manual recovery — generalises to any platform that
  polices automation.
- Do NOT restate the approval derivation here. Which class an operation carries, what a preview
  must name and how a standing approval is bounded are in `approval-boundaries`, once.

## Prerequisites

- A connected account and visibility of its state: `sender.list`, `sender.get`,
  `sender.health`.
- The channel's own rules: `channel.describe` — whether automation is sanctioned there at all,
  how its message allowance works, how long a reply window stays open.
- Where the account is metered: `social_credit.get` for messaging credits, `budget.get` for
  what is left on a meter in this scope and period.

## Planning guidance

Answer before enabling social steps:

- How old is the account, and what is its standing (`sender.get`, `sender.health`)?
- What does the platform itself allow at this tier — asked of `channel.describe`, never assumed
  from a figure somebody remembers.
- What limits are currently configured, **and who declared each**? That is the read
  `sender_limit.set` demands before it is run again, and it is the difference between adjusting
  a limit and silently overwriting somebody else's decision.
- Are those limits appropriate for the account's maturity rather than for the campaign's
  ambition?

Open expert questions:

- TODO(expert): recommended daily caps — connection requests, messages, InMails, profile views
  — by account maturity tier.
- TODO(expert): safe weekly invitation totals, and the acceptance-rate floor below which
  pausing is mandatory.

## Execution guidance

1. **Inspect the account** (`sender.health`). Connection, restrictions, warm-up position,
   capacity budgets and the channel's automation sanction, in one read. An open restriction or
   a disconnection is a **hard stop**. A remaining allowance that comes back `unknown` is not
   the same fact as an ample one and never reads as permission (`A6`).
2. **Read the channel's rules** (`channel.describe`). An automatic execution mode on a channel
   that does not sanction automation is refused when the step is authored, not discovered when
   it sends (`E15`).
3. **Set conservative limits** (`sender_limit.set` — how much, how fast, with what spacing: one
   decision, not three). Start below what the platform allows; the published limit is where
   enforcement begins, not where safety ends. It is `confirm_once` in **both** directions,
   including downward, because one call rewrites limits somebody else may have declared. Where
   the ceiling belongs to a meter rather than to a sender — profile reads, metered messages —
   it is `budget.set`, a hard stop the pre-flight check reads rather than an alert. Throttling
   one campaign below the sender's ceiling is `pacing.set`, and pacing may only lower (`F1`).
   - TODO(expert): concrete starting values and ramp rules.
4. **Check standing before inviting.** `social_relationship.get` says whether we are already
   connected, pending, or nowhere; `social_invitation.list` says what is still outstanding and
   how old it is; `outreach.precheck` says whether this person may be contacted on this channel
   at all. A relationship state is a precondition, not a preference (`F12`).
5. **Invite deliberately** (`social_invitation.send`). It is `confirm_each` — the note is
   content addressed to a named person — and it is metered on its own `invitation` allowance,
   charged per invitation sent. Invitations therefore do not compete with outreach for one
   allowance, and running out of one says nothing about the other: read both before planning a
   day's work. Where parallel work runs against the same account, `sender.reserve` claims a unit
   so two runs cannot spend it twice.
6. **Ask before writing** (`conversation_window.get`): may we write to this person freely right
   now, only with pre-approved content, or only by spending a credit? `social_credit.get` says
   how many credits are left, when they expire, and whether a reply gives one back. Where the
   platform gates the content rather than the sender, `approved_content.submit` and
   `approved_content.get` are that route. The send itself is `message.send` — one send verb for
   every channel, `confirm_each`, one person per call. A credit-metered message is not a second
   verb; it is the same act with a different cost.
7. **Treat looking as an act** (`social_profile.view`). It spends a counted allowance and on
   some channels the person can see that we did, which is why it resolves to `act` unless the
   operator has declared that this channel does not surface the view — and absence resolves to
   `act`. Bulk profile research is exactly the case a standing approval exists for: a named
   artefact, a ceiling and an expiry, recorded. See `approval-boundaries`.
8. **Pace like a person.** Activity spread through business hours, no bursts, no round-the-clock
   patterns. Automation is detected by rhythm as much as by volume — which is why spacing is
   part of the limit decision and not an afterthought to it.
9. **Watch acceptance.** Falling acceptance on invitations is the leading indicator of trouble —
   it degrades before the platform acts. `social_invitation.list` gives the outstanding set and
   its age. Where the floor must enforce itself with nobody present, `stoprule.set` names what
   is measured, over what window, at what threshold and what it halts, and
   `stoprule_firing.list` is what the morning reads.
   - TODO(expert): thresholds and cool-down durations.

### One gap, stated rather than invented

**No operation returns an invitation acceptance rate as a named measure.** Both sides of it are
reachable — `social_invitation.list` carries each pair's state and its age — but the contract
names no acceptance measure, so whether the account can produce one with the envelope every
figure owes (`M1`, `M4`) is a question for `metric.describe`, and `not measurable` is a
first-class answer there (`M2`). Until that is settled, the acceptance floor above is a human
review of the outstanding set rather than a threshold a stop rule can read by itself. Say which
of the two you did. Do not invent a name for the measure.

## Validation

After configuration: re-read the limits in force and who declared each (`sender.get`), and
re-read `sender.health` for restrictions, warm-up position and remaining budgets — with
`unknown` reported as unknown rather than rounded up to ample (`A6`).

Ongoing: outstanding invitations and their age (`social_invitation.list`), remaining credits
and their expiry (`social_credit.get`), remaining allowance on each meter (`budget.get`), and
whether usage is tracking well below the configured caps rather than pressing against them.

## Reporting

Volumes per action type — invitations sent and withdrawn, messages, profile views — each with
the meter it spent. Acceptance where it can be measured, with its denominator count (`M4`), and
where it cannot, the statement that it cannot rather than a bare number (`M2`). Any warning or
restriction observed, and every limit change with its reason and the limit it replaced. An
operation carrying a collection owes one outcome per item (`N2`).

Account incidents always produce a report — like deliverability, this is a category where the
thresholds can only be learned from real evidence.

## Failure modes

- **Account flagged or restricted** → `sender.pause` first, then `restriction.record`
  immediately. Recording an event that already happened outside our control is not a decision
  (`K2`), and both the invalidated warm-up position and the recurrence count hang off that
  record (`F4`). Recovery is manual, slow and not guaranteed; `restriction.appeal` is for where
  the platform declares a route, and it says plainly when there is none (`F5`).
- **Lowering a limit instead of pausing.** `sender.pause` is `auto` and is the first move in
  every incident on every channel. `sender_limit.set` downward is protective in shape and is
  still `confirm_once`, so reaching for it first costs a prompt and loses the minutes.
- **Pending invitation pile-up** → `social_invitation.withdraw` the stale ones rather than
  continuing to send. Withdrawing does not lift a restriction (`F5`), and a channel may declare
  a cooling-off before the same person may be invited again.
  - TODO(expert): aging threshold for withdrawal.
- **Resuming at the old volume the moment a block lifts.** `sender.resume` refuses while the
  warm-up position is invalid unless a ramp restart or a recorded override is supplied (`F4`).
  Platforms escalate on the frequency of restrictions, not their type.
- **Opening a replacement account after a restriction.** There is deliberately no operation for
  it, on any channel — it is a terms violation on its face, and the absence is the answer
  (`F13`).
- **Configuring limits from the campaign's needs** instead of the account's maturity.
- **Treating the platform's published limit as a target.**
- TODO(expert): recovery playbook and safe-return criteria after a restriction.

## Safety

Same protective-first rule as sending health: on an account warning, `sender.pause` runs
**first** and the user is told immediately. It is `auto` because waiting for permission to stop
is the destructive choice, and the restart direction always waits (`L6`) — `sender.resume` is
`confirm_once` and refuses from an invalid warm-up position. `restriction.record` is `auto` for
the same reason a stop is. Nothing that increases activity — raising a limit, resuming, sending
— is ever covered by that floor.

Anything composed for a named person is `confirm_each`: `message.send`, and an invitation
carrying a note, because a note is content addressed to somebody. A credit-metered direct
message is not a lesser act for having cost a credit — these channels are more intrusive than
email and less forgiving. Full rules: `approval-boundaries`.

## Related skills

- `sdr-operations` — the operations named above, their five properties, and the invariants
  cited here by ID.
- `approval-boundaries` — the derivation, the protective floor, and the standing approval that
  bulk profile viewing needs.
- `campaign-launch` — consults this skill before enabling social steps.
- `sending-guardrails` — the same posture on the email side. Siblings, not substitutes: a
  social account and a sending domain fail differently and recover differently.

## Changelog

- 0.3.0 (2026-08-10): rewritten against the 325-operation contract. `sender.limits.read` became
  `sender.get` and `sender.limits.update` became `sender_limit.set`, which is `confirm_once` in
  both directions — so the first move in an incident is `sender.pause`, which is `auto`. The
  social operations the skill previously described without naming are now named:
  `social_invitation.send`, `social_invitation.list`, `social_invitation.withdraw`,
  `social_profile.view`, `social_credit.get`, `conversation_window.get`, `restriction.record`
  and `restriction.appeal`, together with `social_relationship.get`, the metered reads
  `budget.get` and `budget.set`, and `stoprule.set` for the floor that has to enforce itself.
  Platform-specific values — the automation sanction, how an allowance works, how long a reply
  window stays open — are deferred to `channel.describe` instead of being asserted here. States
  one gap rather than inventing a name for it: the contract names no invitation-acceptance
  measure. No pacing number was validated or invented: every expert marker survives untouched.
- 0.2.0 (2026-07-30): renamed from `linkedin-safety` and made vendor-neutral for the
  `ai-sdr-core` pack — Reply endpoints moved to `reply-operations-mapping`. Framed as a
  channel instance of a general posture; safety defers to `approval-boundaries`. Still an
  expert skeleton: no pacing number has been validated.
- 0.1.0 (2026-07-27): expert skeleton — structure and safety posture.
