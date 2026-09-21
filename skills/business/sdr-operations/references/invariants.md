# Invariants

The business rules the whole contract rests on — **140 rules in fourteen groups** — deduplicated
across every family and stated at the level where each one actually binds. They are what makes the
operations a contract rather than a list: an operation may be added, renamed or retired, but it
never gets to disagree with a rule here.

**Stated once, here, and nowhere else.** A family fragment does not repeat the rules its operations
are bound by. A generated catalog does not paraphrase them. A provider adapter does not re-derive
them. When a rule has to change it is changed in this file, in the one place, and everything that
reads it follows. Restating a rule next to an operation is exactly how two copies of the same rule
end up quietly disagreeing.

**The group letters and the numbers are stable identifiers.** `A5`, `G7`, `M2` are cited from plans,
from reviews and from adapter documentation, so they are never renumbered. A new rule takes the next
free number in its group; a retired rule leaves its number vacant rather than letting a later rule
inherit a citation that was about something else. The letters mark where a rule binds and do not
track the family numbering.

**Each rule names the operations that enforce it, read it, or would break it.** That list is the
source of the per-operation binding: the rules an operation carries are derived by inverting these
lists, not written out a second time on each fragment. An operation named here that does not exist
in the contract is a build failure — deliberately so, because a rule pointing at an operation nobody
ever wrote is the kind of ghost reference that survives for months unnoticed. A few rules name no
operations at all: they bind the contract as a whole, or they are enforced by an absence rather than
by a call.

---

## A. Permission, the person's own stop, and the evidence

**A1.** **No operation anywhere clears an opt-out, a permanent delivery failure, a complaint or a
legal hold by editing a field.** The only lift is `suppression.remove`, which demands an evidence
reference whose required kind depends on the reason class, and is refusable while a retention floor
applies.
*Enforced by:* `contact.update`, `duplicate.merge`, `contact.enrich`, `contact.normalize`, `import.apply`, `suppression.remove`

**A2.** **A request to stop is an obligation, not a meaning.** It never sits on the axis an agent
uses to label how a reply felt, because a relabel would then lift a legal duty.
*Enforced by:* `conversation.classify`, `optout.record`, `suppression.add`

**A3.** **The legal clock runs from the person's own act, not from our arrival time.** Both are
recorded, in two fields. Where the person's own event time is genuinely unknown it is recorded as
unknown and the clock runs from the earliest moment we could have known.
*Enforced by:* `optout.record`, `optout.poll`, `inbound_lead.record`, `privacy_request.create`

**A4.** **Every deadline is computed from the applicable regime and the operator's own service level,
and the effective one is the strictest applicable.** No deadline is a constant in this contract.
*Enforced by:* `optout.record`, `privacy_request.create`, `privacy_request.update`, `deletion_feed.report`, `response_policy.define`

**A5.** **Suppression precedence is absolute and ordered**: identity → suppression → lawful basis →
notice owed → external register → channel permission → conversation window → message elements →
oversight → quiet hours and blackouts → frequency → cooldown → capacity. **A signal may raise a
ceiling; it may never lower a bar.**
*Enforced by:* `outreach.precheck`, `signal_policy.define`, `trigger.create`, `message.send`

**A6.** **`unknown` never reads as `allow`.** An external register that could not be reached, a basis
that could not be determined, a jurisdiction that could not be resolved, a remaining allowance nobody
publishes — each yields `allow_at_risk` with the unknown named, or a denial.
*Enforced by:* `outreach.precheck`, `dnc_registry.check`, `sender.health`, `sender.reserve`, `budget.get`, `capability.list`, `vocabulary.list`, `capacity.estimate`

**A7.** **A pass from the permission check is advisory; the binding refusal is at send time.** A pass
reserves nothing and does not survive; the gates are evaluated again immediately before the act, and
a batch prepared against an earlier check is re-screened at dispatch.
*Enforced by:* `outreach.precheck`, `message.send`, `message.schedule`, `campaign.enroll`, `outreach.release`

**A8.** **Every failed gate is returned, never only the first.**
*Enforced by:* `outreach.precheck`

**A9.** **A suppression entry is a distinct retention domain from the person record.** Erasure
**minimises** it — an irreversible hash of each identifier, the reason class, the timestamps, nothing
else — and every write path still matches against the minimised entry.
*Enforced by:* `privacy_request.fulfill`, `suppression.check`, `outreach.precheck`, `contact.create`, `import.apply`, `inbound_lead.record`

**A10.** **Erasure is partial by design, the retention floor set is enumerated, and the result is per
store and per identifier** — `erased` / `minimised` / `retained_under_floor` (naming the floor) /
`unreachable` (naming the store). A single "done" is a lie in every real system.
*Enforced by:* `privacy_request.fulfill`, `retention.apply`

**A11.** **Evidence requirements and retention floors apply to the `legal_obligation` reason class
only.** Applying them to commercial exclusions makes every churned customer permanently
unmarketable, which is a bug, not a compliance outcome.
*Enforced by:* `suppression.add`, `suppression.remove`

**A12.** **An exclusion binds a brand or legal entity, never a mailbox, a sender or a campaign**, and
its channel and purpose scope default to the widest, because widening later is free and narrowing
later is a breach.
*Enforced by:* `suppression.add`, `outreach.precheck`

**A13.** **A `legal_obligation` exclusion blocks disclosure and transfer of that identifier, not only
sending.** An export containing an opted-out address is a breach even though nothing was sent.
*Enforced by:* `export.request`, `privacy_request.fulfill`, `deletion_feed.report`

**A14.** **A permanent delivery failure suppresses the identifier that failed everywhere, and it is
never retried.** It produces an operational suppression entry scoped to that identifier — not a note
in the campaign where it happened, and not an exclusion of the person's other identifiers: the
person's contactability is downgraded and a hygiene finding is raised, so the human stays visible
without being excluded. *(Provisional pending open question 8.)*
*Enforced by:* `engagement_event.list`, `suppression.add`, `message.send`, `reply_policy.set`

**A15.** **Permission does not travel with a person; exclusion does.** On a job change, person-level
suppression follows the human and permission tied to the old address does not move to the new
employer.
*Enforced by:* `job_change.record`, `consent.record`, `suppression.add`, `outreach.precheck`

**A16.** **A referral establishes provenance, never a basis.** A referred person is evaluated under
exactly the same lawful basis as any cold contact, and no evidence bundle may cite a stranger's
message.
*Enforced by:* `referral.record`, `provenance.set`, `consent.record`, `consent.prove`, `outreach.precheck`

**A17.** **Provenance values and consent bases are two different enumerations.** Third-party referral
and self-identified inbound are **provenance**, never consent.
*Enforced by:* `provenance.set`, `contact.create`, `import.apply`, `inbound_lead.record`, `consent.record`, `outreach.precheck`

**A18.** **The privacy notice is owed wherever the data did not come from the person**, no later than
the first contact, and its absence is a deny-with-remedy rather than a warning. It is satisfied
either by a rendered outreach message carrying it or by a standalone delivery.
*Enforced by:* `outreach.precheck`, `notice.send`, `provenance.set`

**A19.** **A basis is not a boolean.** Kind, capture artefact, collecting legal entity, jurisdiction,
channels, purposes, subject-matter scope and expiry, or it is not evidence. `consent.prove` must be
able to answer `unprovable`.
*Enforced by:* `consent.record`, `consent.revalidate`, `consent.prove`

**A20.** **Quiet hours are law and no operator policy may widen them; blackouts are operator policy
and may be overridden with a recorded reason.** Quiet hours resolve in the recipient's local time and
bind a two-minute callback exactly as they bind a scheduled send.
*Enforced by:* `outreach.precheck`, `send_window.resolve`, `blackout.define`, `blackout.remove`, `response_policy.define`

**A21.** **Machine-drafted outreach carries either a human editorial review bound to the message
actually sent, or the required disclosure, or it is refused.**
*Enforced by:* `outreach.precheck`, `editorial_review.record`, `editorial_review.get`, `disclosure.check`, `disclosure.record`, `message.send`

**A22.** **Answering a request somebody made is a different message class from unsolicited outreach,
and the gate is told which one it is evaluating.** A legal exclusion still refuses; a marketing
preference does not turn an answer to their own question into marketing.
*Enforced by:* `outreach.precheck`, `message.send`, `inbound_lead.triage`

## B. Identity, data honesty and hygiene

**B1.** **Identity resolution returns matched / new / ambiguous. Never a binary.** `ambiguous` is a
legitimate terminal answer that routes to a human and is never coerced.
*Enforced by:* `contact.resolve`, `company.resolve`, `contact.create`, `candidate.promote`, `import.apply`, `inbound_lead.record`

**B2.** **A merge unions negative state.** No merge, enrichment, refresh, normalisation, import or
automated fix may clear an opt-out, a permanent failure, a complaint or a legal hold.
*Enforced by:* `duplicate.merge`, `contact.enrich`, `contact.normalize`, `import.apply`, `identity_policy.define`

**B3.** **Duplicate policy and blank-value rule are stated on every write that can match. There is no
safe default and the contract refuses to invent one.**
*Enforced by:* `contact.create`, `candidate.promote`, `import.apply`

**B4.** **Every write that can match records the evidence it matched on**, so a wrong merge can be
explained even where it cannot be undone.
*Enforced by:* `contact.resolve`, `duplicate.list`, `duplicate.merge`, `import.apply`

**B5.** **Matching methods are field-aware.** A generic similarity threshold is not an acceptable
expression of an identity policy.
*Enforced by:* `identity_policy.define`, `duplicate.list`

**B6.** **Every field obtained from anywhere but the person carries its source and its retrieval
time**, and provenance is a required input at creation, not something added later.
*Enforced by:* `contact.enrich`, `company.enrich`, `contact.create`, `import.apply`, `provenance.set`, `candidate.promote`

**B7.** **"Withheld by policy" is never folded into "not found", and "unknown" is never a negative.**
They lead to opposite next actions.
*Enforced by:* `contact.enrich`, `company.enrich`, `candidate.promote`, `candidate.get`

**B8.** **Verification reports what the protocol said, separately from what our policy advises**, and
`accepts_all` is a terminal verdict that no amount of re-checking resolves. **Verification never
delivers a message to the person being verified.**
*Enforced by:* `email_address.verify`, `phone_number.verify`, `contactability_policy.define`

**B9.** **Both the raw and the normalised value are kept**, and the normalisation rule set is declared
and inspectable rather than a silent default.
*Enforced by:* `contact.normalize`, `import.apply`, `source.inspect`, `identity_policy.define`

**B10.** **Nothing is dropped silently.** A criterion the universe cannot evaluate is refused or
reported; a column that cannot be mapped is named individually and discarding it is a decision
somebody made.
*Enforced by:* `contact.search`, `candidate.search`, `segment.preview`, `source.inspect`, `import.apply`

**B11.** **A hygiene finding is a proposal.** Applying it is always a separate operation with its own
approval and its own reversibility.
*Enforced by:* `duplicate.list`, `duplicate.merge`, `duplicate.link`, `duplicate.reject`, `audience.assess`, `audience.screen`

**B12.** **Deleting a record is not erasing a person, and it never lifts an exclusion.**
*Enforced by:* `contact.delete`, `contact.restore`, `privacy_request.fulfill`, `suppression.add`

**B13.** **Acting on a record never silently makes the actor its owner**, and assignment to a person
who is not active is a refusal, not a write.
*Enforced by:* `contact.create`, `ownership.get`, `ownership.assign`, `task.create`, `task.reassign`, `handoff.create`, `autonomy.set`

## C. Money

**C1.** **Every metered operation returns what it actually consumed, including zero.**

**C2.** **Searching never returns contact details, and buying is never a side effect of searching or
of freezing an audience.**
*Enforced by:* `candidate.search`, `segment.materialize`, `segment.get`, `candidate.promote`

**C3.** **Every purchase is bounded before it starts**, by a ceiling it can hit and a per-item ledger
it writes as it goes.
*Enforced by:* `enrichment_policy.define`, `contact.enrich`, `company.enrich`, `candidate.promote`, `budget.set`, `operation.preview`

**C4.** **Creating and importing never buy and never verify.** Keeping them apart is what stops an
unbounded spend hiding inside the most-called write in the contract.
*Enforced by:* `contact.create`, `import.apply`

**C5.** **Budget is a hard stop, not an alert.** An alert that stops nothing is a different feature.
*Enforced by:* `budget.set`, `budget.get`, `stoprule.set`, `plan.validate`

## D. Audiences and lists

**D1.** **A segment is criteria; a list is a frozen fact with a date on it.** Outreach runs against a
frozen membership, or "who did we contact in June" becomes permanently unanswerable.
*Enforced by:* `segment.define`, `segment.materialize`, `list.get`, `campaign.enroll`

**D2.** **Every definition names its universe**, and a count is a count of what one universe happens
to know on one day. It is never a market size, counts from different universes are never added, and
the contract refuses to compute an obtainable share.
*Enforced by:* `segment.define`, `segment.preview`

**D3.** **Membership is correctable in both directions.** Anything that can be filled can be
un-filled, and removing somebody from a list removes them from that list and nothing else.
*Enforced by:* `list_membership.add`, `list_membership.remove`

**D4.** **Exclusion screening runs twice** — after materialisation and again immediately before
enrolment — because exclusion state moves in between.
*Enforced by:* `audience.screen`, `campaign.enroll`, `buying_group.enroll`

**D5.** **Contactability is a declared policy, never a default**, and the accepts-all case is decided
explicitly.
*Enforced by:* `contactability_policy.define`, `email_address.verify`, `audience.assess`

**D6.** **The readiness report never spends money.** It reports the freshness of what we hold; buying
fresher facts is a different, approved, metered operation.
*Enforced by:* `audience.assess`

## E. The programme, its steps and its content

**E1.** **A campaign is never live on creation, and nothing about authoring implies sending.** Going
live is one named act with one confirmation.
*Enforced by:* `campaign.create`, `campaign.clone`, `campaign.activate`

**E2.** **Activating a campaign holding N enrolments is an act of magnitude N.** Enrolling into a
draft and then activating must never be a cheaper approval than enrolling into a live campaign.
*Enforced by:* `campaign.activate`, `campaign.validate`, `operation.preview`, `campaign.enroll`

**E3.** **Every release of suppressed sending is an act, and its preview names the backlog** and
states whether it is compressed or the whole calendar shifts.
*Enforced by:* `campaign.activate`, `campaign.resume`, `campaign.unfreeze`, `blackout.remove`, `outreach.release`

**E4.** **A campaign with no terminal condition fails validation.** A programme that never ends is a
defect, not a long campaign.
*Enforced by:* `campaign.validate`

**E5.** **A test send is a precondition of a passing validation**, goes only to addresses the
workspace can demonstrate it owns, never enters a measurement denominator, never counts against a
recipient's frequency cap, and is a real send that consumes real capacity.
*Enforced by:* `campaign.test`, `campaign.validate`, `engagement.summarize`

**E6.** **A freeze carries a mandatory expiry and resumes itself; a pause does not and never
expires.**
*Enforced by:* `campaign.freeze`, `campaign.unfreeze`, `campaign.pause`, `campaign.resume`

**E7.** **Every duration and every date carries a unit, a stated basis — calendar days, working days
or sending days — and whose calendar.**
*Enforced by:* `step.add`, `schedule.set`, `schedule.resolve`, `task.reschedule`, `response_policy.define`, `pace.get`, `buying_group.enroll`

**E8.** **Whose timezone and whose holidays is a policy with an ordered fallback, never a value**, and
region identifiers only — abbreviations do not survive daylight saving. The recipient's region being
unknown is the normal case.
*Enforced by:* `schedule.set`, `send_window.resolve`, `blackout.define`

**E9.** **A step is channel × execution mode × kind, and the three are independent.** A contract that
models only "an email step" cannot transcribe a real cadence.
*Enforced by:* `step.add`, `step.list`

**E10.** **Steps have positions, and a position is stable identity.** Inserting or reordering never
destroys a step's variants, its template binding or its measurement history.
*Enforced by:* `step.reorder`, `step.add`, `variant.add`

**E11.** **A structural edit to a live campaign is an act whose magnitude is the enrolments it
moves**, and it requires an explicit statement of what happens to those enrolments. There is no
default.
*Enforced by:* `step.add`, `step.update`, `step.remove`, `step.reorder`, variant.*

**E12.** **Branching is a declared capability**, and a branch declares its evaluation window and its
early-satisfaction behaviour. The contract never flattens a branch into a linear step and calls it
equivalent.
*Enforced by:* `step.add`, `vocabulary.list`, `channel.describe`

**E13.** **A published content version is immutable**, and retiring one is refused while live
campaigns bind it unless a replacement is named. Otherwise "what did we actually send in May" is
unanswerable.
*Enforced by:* `template.update`, `template.publish`, `template.retire`, `template.get`

**E14.** **The rendered message is the artefact everything binds to** — the content-policy check, the
channel-compliance check, the disclosure check and the human editorial review. A review of a template
is not a review of a message.
*Enforced by:* `message.draft`, `content_policy.check`, `message_compliance.check`, `disclosure.check`, `editorial_review.record`, `approval.get`

**E15.** **Automation is not available on every channel.** An automatic execution mode on a channel
that does not sanction automation is refused at authoring time, not discovered at send time, and
there is **one** source of that fact.
*Enforced by:* `channel.describe`, `sender.health`, `outreach.precheck`, `step.add`

## F. Sending, capacity and the wire

**F1.** **Campaign pacing may only lower the effective rate.** The sending capability's own limit is
the ceiling and pacing is a governor beneath it.
*Enforced by:* `pacing.set`, `sender_limit.set`, `capacity.estimate`

**F2.** **Frequency caps count across every campaign, every channel and every sender**, and a breach
is a deferral with a date, never an error.
*Enforced by:* `frequency_policy.define`, `outreach.precheck`, `campaign.enroll`, `trigger.create`

**F3.** **Capacity is a set of nested budgets with an explicit scope, window type and unit — never a
single daily number** — and `unknown` remaining is a first-class answer. Two capabilities do not
necessarily give twice the capacity, because some budgets are scoped above the sender.
*Enforced by:* `sender.health`, `sender.reserve`, `capacity.estimate`, `budget.get`

**F4.** **A recorded restriction invalidates the current warm-up position.** Resume requires a ramp
restart or a recorded override with an actor and a reason, and the recurrence count survives
clearance, because recovery time scales with recurrence.
*Enforced by:* `restriction.record`, `sender.resume`, `warmup_plan.set`, `sender.health`

**F5.** **No operation is documented as remediation unless the platform declares it as such**, and
`undisclosed` is a legitimate value for a restriction's cause.
*Enforced by:* `restriction.appeal`, `social_invitation.withdraw`, `approved_content.retire`, `sender.health`

**F6.** **Every rate carries its publisher, its denominator basis, its window and its as-of.** A bare
number is not a legal value, and no composite reputation score is ever manufactured.
*Enforced by:* `domain_reputation.get`, `domain_compliance.check`, `sender.summarize`, `engagement.summarize`

**F7.** **Every channel's required stop mechanism is present, as separate artefacts, before anything
is sent on it.** On email that is three — a header, a machine-actionable endpoint that actually
honours a request, and a visible link — each separately required, configured and checked. On channels
that carry none of these, it is the stop mechanism the channel itself mandates — a stop keyword, a
platform opt-out — held to the same separately-required, separately-checked rule.
*Enforced by:* `message_element.set`, `domain_compliance.check`, `message_compliance.check`, `optout.poll`

**F8.** **A sending capability may not be disconnected, nor a domain retired, nor a pool dissolved,
while live enrolments or open conversations depend on it, unless a rebind target is supplied.** A
clean handover that destroys the reply path for every open conversation is worse than a refusal.
*Enforced by:* `sender.disconnect`, `sending_domain.retire`, `sender_pool.delete`, `book.transfer`

**F9.** **Rebinding a sending identity mid-conversation is visible to the prospect** and is never
filed as internal admin.
*Enforced by:* `sender_binding.set`, `book.transfer`

**F10.** **Rotation is not a cure.** Where a budget's scope is the pool, the organisation or the
recipient, rotating across senders moves the problem rather than solving it.
*Enforced by:* `sender_pool.update`, `sender.health`, `frequency_policy.define`

**F11.** **Warm-up is a state machine with a position, and its schedule is operator configuration.**
No ramp figure appears in this contract, because none is published for a mailbox by anybody.
*Enforced by:* `warmup_plan.set`, `sender.health`

**F12.** **A relationship state is a precondition, not a preference**, and looking at somebody can
itself be metered and can itself be visible to them.
*Enforced by:* `social_relationship.get`, `social_profile.view`, `conversation_window.get`, `outreach.precheck`

**F13.** **Creating a replacement account after a restriction is not an operation in this contract**,
on any channel. It is enforced by the absence, stated in the social and messaging family.

## G. Enrolment and participation

**G1.** **An enrolment is addressable by (person, campaign).** Nothing may require the caller to hold
an identifier to pause, resume or stop a participation. **One active participation, many
historical.**
*Enforced by:* `campaign.enroll`, `enrollment.list`, `enrollment.get`, `enrollment.pause`, `enrollment.stop`

**G2.** **Enrolling into a live campaign is a send; into a campaign that is not live it is
bookkeeping** — an expression over the campaign's state, resolved by preview and never assumed.
*Enforced by:* `campaign.enroll`, `buying_group.enroll`, `operation.preview`

**G3.** **State, blocked reason, exit reason and business outcome are four facts and four fields.** A
blank blocked reason reads as "nothing is wrong" and is the most expensive silence in the contract.
*Enforced by:* `enrollment.list`, `enrollment.get`, `enrollment.stop`, `disposition.set`

**G4.** **Pausing a campaign does not pause its enrolments**, and an enrolment that is not progressing
must say which of campaign paused, frozen, out of window, out of pacing or missing a sender applies.
*Enforced by:* `campaign.pause`, `campaign.freeze`, `enrollment.list`

**G5.** **Resume refuses from any state other than held.** Restarting a cadence to somebody who
replied, opted out or permanently failed is a compliance failure and a deliverability failure at
once.
*Enforced by:* `enrollment.resume`, `campaign.resume`, `sender.resume`, `outreach.release`

**G6.** **A stop reason is mandatory and may name a different person as the cause.** A colleague's
reply ending this person's participation is normal, not exotic.
*Enforced by:* `enrollment.stop`, `reply_policy.set`, `buying_group.enroll`

**G7.** **The collision policy, the start position and the first-touch timing are explicit on every
enrolment. None may be defaulted.**
*Enforced by:* `campaign.enroll`, `collision.check`

**G8.** **An enrolment's position is evidence of what this person actually received**, so no
operation sets it directly, and starting somebody over is a stop with a reason followed by a fresh
enrolment.
*Enforced by:* `enrollment.stop`, `campaign.enroll`, `enrollment.list`

## H. The conversation, the queue and the human boundary

**H1.** **Classifying a reply never changes participation state.** Where an installation couples them,
the coupling is reported in the result, never hidden.
*Enforced by:* `conversation.classify`, `enrollment.list`

**H2.** **The base reply meanings are the contract's, not the account's** — nine values, always
accepted, never removed or redefined by configuration, with the account's vocabulary mapped on top
and both reported.
*Enforced by:* `conversation.classify`, `vocabulary.list`

**H3.** **What a message says about itself is a fact; what it means is a judgement**, and the two are
carried separately with the judge's identity and confidence. **A person's decision beats a machine's
and both are kept.**
*Enforced by:* `conversation.get`, `conversation.classify`, `review.score`

**H4.** **Nothing automatic ever answers something automatic**, and no reply is ever sent to a request
to stop. A dated message for someone's return is a new outbound message, scheduled, not a reply.
*Enforced by:* `message.send`, `message.schedule`, `conversation.classify`

**H5.** **A reply is sent from the sending identity that owns the thread.**
*Enforced by:* `message.send`, `sender_binding.set`, `conversation.get`

**H6.** **Recording that something happened never makes it happen.** Completing a queued task sends
nothing; logging a call sends nothing and advances no cadence; logging an activity sends nothing;
recording a meeting outcome notifies nobody.
*Enforced by:* `task.complete`, `call.log`, `activity.log`, `meeting_outcome.record`

**H7.** **Skipped is not completed**, and a call that did not connect is a touch, not an error.
*Enforced by:* `task.skip`, `task.complete`, `call.log`

**H8.** **Recording a call is a per-call decision made before connecting, with the basis named for
every party**, and a recording without a valid basis is discarded with the discard recorded.
*Enforced by:* `call.place`, `call_recording.get`, `call_recording.discard`, `channel.describe`

**H9.** **A conversation carries no business outcome, and closing a thread is not a way to stop
outreach.**
*Enforced by:* `conversation.close`, `disposition.set`, `outreach.hold`

**H10.** **The queue arrives ordered.** Priority is part of the result, not something the caller
reconstructs.
*Enforced by:* `task.list`, `conversation.list`, `inbound_lead.list`

**H11.** **Human capacity is enforced, not advisory.** Where a ceiling is declared, assignment,
routing, reassignment and book transfer refuse or defer past it, per item, with a named outcome.
Where none is declared the answer is "none declared", never a soft number nobody honours.
*Enforced by:* `workload_policy.define`, `workload.get`, `ownership.assign`, `book.transfer`, `task.reassign`, `conversation.assign`, `inbound_lead.route`

**H12.** **A person becoming non-active invalidates every existing binding to them**, not merely
future writes — escalation target, pending approvals, review assignments, ownership, senders behind
live enrolments. A run whose escalation target is invalid must not start.
*Enforced by:* `workload.get`, `plan.validate`, `autonomy.set`, `ownership.assign`, `task.reassign`, `handoff.create`

**H13.** **Ownership is four independent bindings** — the record's owner, the sending capability
behind live enrolments, task assignment, conversation assignment — and an operation that changes one
declares that it does not change the others.
*Enforced by:* `ownership.assign`, `sender_binding.set`, `task.reassign`, `conversation.assign`, `book.transfer`

## I. Meetings, qualification and handoff

**I1.** **Booked, held, accepted and converted are four separately recordable facts, and none may be
derived from another.**
*Enforced by:* `meeting.book`, `meeting_outcome.record`, `handoff.accept`, `opportunity.create`, `funnel.summarize`

**I2.** **Invited is not accepted**, per attendee; **no-show is retractable**; and every meeting time
states the timezone of every attendee.
*Enforced by:* `meeting.get`, `meeting_outcome.record`, `meeting.list`

**I3.** **Acceptance carries a commitment to make contact inside a stated time; rejection carries a
reason or is refused.**
*Enforced by:* `handoff.accept`, `handoff.reject`, `handoff.list`

**I4.** **Qualification evidence is the account's own questions, carried whole, never scored and never
forced into a named framework.**
*Enforced by:* `qualification.record`, `handoff.get`

**I5.** **Whatever stops outreach to a person reaches everything queued against them** — enrolments,
tasks, pending approvals, scheduled sends, un-accepted handoffs and outstanding meeting proposals —
and **reports what it could not reach**.
*Enforced by:* `outreach.hold`, `handoff.withdraw`, `task.cancel`, `scheduled_message.cancel`, `meeting.cancel`, `approval.resolve`, `account_membership.list`

## J. Signals and standing rules

**J1.** **A signal is an observation, never a score** — source, observation time, confidence and
validity window, or it does not ship.
*Enforced by:* `signal.list`, `signal.get`, `trigger.create`

**J2.** **Acting on a stale observation is worse than not acting.** Every rule states a maximum age
and an observation past its validity window does not fire.
*Enforced by:* `trigger.create`, `signal.list`, `signal.acknowledge`

**J3.** **Competing signals need a stated order, and there is no default.**
*Enforced by:* `signal_policy.define`, `signal_policy.get`

**J4.** **Work produced by a standing rule is ordinary work.** Same queue, same permission gate, same
frequency cap. A trigger is not a way around anything.
*Enforced by:* `trigger.create`, `task.list`, `outreach.precheck`

**J5.** **A standing rule is auditable**: what fired, on whom, what it produced, and what it did
**not** produce because something suppressed it. Stop rules and standing work rules share one
firing-record shape.
*Enforced by:* `trigger_run.list`, `stoprule_firing.list`, `stoprule_firing.get`

**J6.** **Disabling a rule and deleting a rule are different acts.**
*Enforced by:* `trigger.update`, `trigger.delete`

## K. Inbound

**K1.** **An arrival is a fact with a time, and the time is the source's own event time.** Every clock
in this motion runs from it.
*Enforced by:* `inbound_lead.record`, `inbound_lead.list`, `response_time.summarize`

**K2.** **Recording an event that already happened outside our control is not a decision.** It derives
`auto`, exactly as recording an opt-out does; delaying it burns the only thing this motion is
measured on.
*Enforced by:* `inbound_lead.record`, `optout.record`, `restriction.record`

**K3.** **Nothing is worked before it is owned. A claim is contended**, and a routing rule that cannot
place a lead falls back and says so — it never drops it and never places it on somebody who is not
active or over capacity.
*Enforced by:* `inbound_lead.route`, `inbound_lead.claim`, `inbound_lead.release`, `assignment_policy.define`, `workload_policy.get`

**K4.** **An arrival that matches somebody with outbound in flight returns that work explicitly.** An
inbound request never silently lets a cold cadence keep running against the person making it.
*Enforced by:* `inbound_lead.record`, `enrollment.list`, `scheduled_message.list`

**K5.** **A duplicate arrival is one arrival**; a later request is a **new** arrival with a new clock,
and reopening a premature close preserves the original clock and its breach.
*Enforced by:* `inbound_lead.record`, `inbound_lead.close`, `inbound_lead.reopen`

**K6.** **Triage decides what the request is; disqualification decides what the person is.** Neither
implies the other.
*Enforced by:* `inbound_lead.triage`, `contact.disqualify`, `contact.requalify`

**K7.** **An inbound lead has not consented to everything.** What they asked about bounds what the
answer may be about; a form fill is not a subscription and never becomes a lawful basis for a
different programme.
*Enforced by:* `inbound_lead.record`, `consent.record`, `outreach.precheck`

**K8.** **The response target is declared, never assumed**, and the contract ships no benchmark
number.
*Enforced by:* `response_policy.define`, `assignment_policy.define`, `inbound_lead.list`, `response_time.summarize`

**K9.** **Speed never lowers an approval class.** Where responding fast requires acting with no human
present, that is an explicit, recorded autonomy grant.
*Enforced by:* `autonomy.set`, `outreach.precheck`, `response_policy.define`

## L. Oversight, autonomy and safety

**L1.** **All five oversight models are expressible and none is interchangeable with another** —
approval at send, per-action mode with a queue and a timeout, escalation to a named human,
channel-asymmetric autonomy, and gating outside this contract entirely with a declared gate location.
*Enforced by:* `autonomy.get`, `autonomy.set`, `approval.request`, `approval.list`, `approval.resolve`, `escalation.raise`

**L2.** **An approval timeout inside an unattended run resolves as refuse-and-halt.** It never
proceeds; the item stays pending for a human and the run stops.
*Enforced by:* `autonomy.set`, `approval.list`, `plan.validate`

**L3.** **Approving content and approving a send are different gates with different lifetimes.**
*Enforced by:* `template.publish`, `approved_content.submit`, `approval.resolve`

**L4.** **An escalation grants nothing.** An agent may transmit a human's decision but may never
author one.
*Enforced by:* `escalation.raise`, `approval.request`, `approval.resolve`

**L5.** **Rejecting a queued draft can end the person's participation, and the rejection says what
else it ended.** It is never a no-op.
*Enforced by:* `approval.resolve`, `enrollment.list`

**L6.** **A protective act never waits for permission; the restart direction always does.**
*Enforced by:* `outreach.hold`, `outreach.release`, `campaign.pause`, `campaign.resume`, `sender.pause`, `sender.resume`, `suppression.add`, `suppression.remove`, `scheduled_message.cancel`, `stoprule.set`, `stoprule.clear`, `approved_content.retire`

**L7.** **Stopping has three granularities and all three exist** — stop this run (`job.cancel`); stop
outbound over a scope while reads stay alive (`outreach.hold`, `campaign.pause`, `sender.pause`);
stop automatically on a named threshold (`stoprule.set`).

**L8.** **A stop rule that fires produces a record naming the trigger value, the operations halted and
every item left unprocessed** — unattended runs have no human present to remember. Sender health,
domain reputation and remaining budget are legitimate subjects.
*Enforced by:* `stoprule.set`, `stoprule_firing.list`, `stoprule_firing.get`, `escalation.raise`

**L9.** **Machine scores never overwrite human ones, scoring arithmetic is declared out loud, and
review visibility is a four-value policy rather than a boolean.**
*Enforced by:* `review.score`, `scorecard.define`, `review.get`

## M. Measurement

**M1.** **Every returned figure carries its measurement envelope**, and a figure without one is not
evidence: when it was computed; the window used with its timezone; window or cohort basis, and for a
cohort its key and the age measured to; numerator and denominator units; the denominator's name **and
its count**; the class of fact; the detection method; maturity; and where relevant the attribution
model and the identity and version of whatever classified it.
*Enforced by:* `engagement.summarize`, `funnel.summarize`, `opportunity.summarize`, `response_time.summarize`, `metric.describe`

**M2.** **Six classes of fact exist and the contract says which one every number is** — own record,
counterparty-reported, sender-inferred, inbound fact, judgement, external record — and the seventh
honest answer is **not measurable**. Whether a message landed in the inbox or the spam folder, and
whether a human actually read it, are not measurable.
*Enforced by:* `engagement.summarize`, `engagement_event.list`, `metric.describe`, `placement_test.get`

**M3.** **Cohort versus window is arithmetic, not preference**, and the basis is mandatory on every
figure; a cohort row carries its age and completeness so an immature row is never compared to a
mature one.
*Enforced by:* `engagement.summarize`, `funnel.summarize`

**M4.** **A rate never travels without its denominator count**, and insufficient volume is a typed
flag with the threshold used, not a caveat in prose.
*Enforced by:* `engagement.summarize`, `metric.compare`, `sender.summarize`

**M5.** **Sends that did not happen are counted as first-class facts, by reason** — deferred,
cap-skipped, blackout-skipped, refused by the gate, throttled by capacity.
*Enforced by:* `engagement.summarize`, `engagement_event.list`

**M6.** **Targeting is a measurement dimension.** Provenance, enrichment source, verification verdict
and import batch are group-by keys, or the most common cause of a reply-rate collapse is
undiagnosable and gets blamed on the copy.
*Enforced by:* `engagement.summarize`, `provenance.set`, `contact.enrich`, `email_address.verify`, `import.list`

**M7.** **No benchmark, industry average or portable score is ever returned**, and **no winning
variant is ever named** — counts, intervals, required sample size and an underpowered flag, and the
human decides.
*Enforced by:* `metric.compare`, `engagement.summarize`, `pace.get`, `signal.list`, `buying_group.get`

**M8.** **Measurement never writes.** Nothing in that family changes a record, a state or a
classification. Test sends and placement tests are excluded from every denominator.

## N. The contract itself

**N1.** **An operation whose result cannot be recovered after a lost connection does not ship.** Every
act, every collection write, every durable-object write and every metered call takes a
caller-supplied key, and `invocation.get` finds the outcome afterwards — distinguishing `never_seen`
from `seen_and_failed`.

**N2.** **Any operation accepting a collection returns a result per item, with the reason.** A partial
success reported as one verdict is a defect.

**N3.** **Absence of a property is read as the most dangerous value**, so no operation leaves one
blank — and there is no tier of this contract where that rule is suspended.

**N4.** **A preview never acts and never spends.** Where the only way to find out what a call would do
is to do it, the preview refuses.
*Enforced by:* `operation.preview`, `plan.validate`

**N5.** **Authority resolves at plan time, not at execution time.** Discovering a refusal at step 40
of 200 is a design failure.
*Enforced by:* `plan.validate`, `capability.list`, `autonomy.get`

**N6.** **A conformance run may never reach a person.**
*Enforced by:* `adapter.verify`

**N7.** **Limits, ceilings and page sizes live in the installation's own description, never in an
operation's definition.** The contract states meaning; the installation states its own limits.
*Enforced by:* `adapter.describe`

**N8.** **No product is named anywhere in this contract** — not in a name, an intent, a property, a
value or an example — and no decision in it rests on what a particular product happens to support.
