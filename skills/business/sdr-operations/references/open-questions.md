# Open questions

Twenty-eight forks in this contract that research could not settle. Each is a place where two
defensible readings exist, we took one, and taking the other would change an operation — not the
prose around it, the operation.

Everything else in the contract is either settled or deliberately left to the account as a declared
policy with no default. These twenty-one are neither. They are decisions where domain judgement
beats further reading, and where we have written down a position we are prepared to reverse.

**Nobody outside the authoring team has reviewed any of this.** Every answer below is ours. Each
one is labelled *our answer today* for that reason, and the label is not modesty — it is the status.
A position argued by the people who wrote the contract, against no practitioner and no production
run, is a hypothesis with a paper trail. That is the whole value of this file: it makes the
unratified parts of the contract findable instead of invisible, so that the first expert to read it
can spend their attention on the twenty-one things that are actually in doubt.

## Two markers, and they are not the same thing

This repository now carries two ways of saying "this is not finished". Conflating them would block
a future clean-up of either, because they are cleared by different people doing different work.

| Marker | What it marks | What resolves it | Where it lives |
|---|---|---|---|
| `TODO(expert):` | An unvalidated number or playbook — a sending cap, a ramp curve, a response window, a pacing rule. The shape is right; the value is a guess. | Evidence from real runs, or an expert's judgement on a value. | Playbook and guardrail skills, in prose. |
| an open question `n` | A fork in the contract we cannot decide alone. `n` is the number in this file and nowhere else. | A decision. It then changes an operation fragment — a property, an option, sometimes a verb that exists or stops existing. | A `## n.` heading in this file, and `questions: [n]` on the operations in `operations/` that the fork leaves unsettled — printed on their catalog rows, and checked: a question carried by no operation fails the build. |

A `TODO(expert)` is answered with a number. An open question is answered with a choice, and the
answer has to be written back into the contract before it means anything.

**The `TODO(expert)` markers already in this repository are a different thing and are untouched by
this work.** Seven skills carry **25** of them — an unvalidated value each, `TODO(expert):` followed
by what has to be validated — plus the callout at the top of each of those skills saying the skill
holds some, which is what makes the count read differently depending on what you grep for: 33 lines
mention the marker, 25 are markers. None of them is an open question, none is renumbered, and
clearing one has no bearing on any fork below. They are listed nowhere here on purpose.

## How a fragment points at this file

An operation left unsettled by one of these forks carries the question number in its fragment:

```yaml
questions: [4]
```

The numbering here is therefore load-bearing. **It must never shift.** A question that is settled
keeps its number and gains its answer; it is not removed and the ones after it do not move up. If a
new fork is found it takes number 22.

## How to read an entry

**The fork** — what the contract says today, and what the second reading is.
**The options** — each with what it costs. The cost column is the argument; the option names are
just labels. `(current)` marks the one the contract implements.
**Our answer today** — the provisional position, always in those words.
**Why** — the reasoning, where we have one worth stating.
**Why we are unsure** — we do not know that our answer is right.
**Why we are asking** — we think our reasoning holds, but it depends on a fact about how the work is
actually done that we cannot get from outside a real team. The two are different admissions and the
distinction is kept deliberately.

Questions 1–7 come from the contract's own list. Questions 8–15 surfaced while tracing end-to-end
chains through it, where a rule that reads cleanly in isolation turns out to have two defensible
readings under pressure. Questions 16–19 came out of working the families one at a time. Questions
20–21 were raised in the review before handing the contract over.

## The index

| # | The fork | Where a decision lands |
|---|---|---|
| 1 | Skipping one automatic touch has no direct expression | `enrollment.pause`, `enrollment.resume`, possibly a new verb |
| 2 | Does routing an inbound arrival transfer durable ownership | `inbound_lead.route`, `inbound_lead.claim`, `ownership.assign` |
| 3 | Is an import undo real practice | `import.revert`, `import.apply` |
| 4 | Four operations hand work to a human | `conversation.assign`, `escalation.raise`, `handoff.create`, `approval.request` |
| 5 | Where the first-response clock stops | The response-time measure; no operation gained or lost |
| 6 | Does a referred person inherit the referrer's collision slot | Referral handling, `collision.check` |
| 7 | Does `phone_number.verify` belong as an operation | `phone_number.verify`, `contact.enrich` |
| 8 | Does a permanent delivery failure suppress the person or the identifier | The permanent-failure suppression rule |
| 9 | Are pause and freeze two verbs a practitioner uses | `campaign.pause`, `campaign.freeze` |
| 10 | What the kill switch acts on at account scope | `outreach.hold`, `account_membership.list` |
| 11 | Should the frequency cap exempt answers to inbound requests | `frequency_policy.define` |
| 12 | Is the test send a precondition or a recommendation | `campaign.validate` |
| 13 | Does refusing to name a winner survive practice | `metric.compare` |
| 14 | Where `interested_later` lands | `cooldown.set`, the classification vocabulary |
| 15 | On a departure, rebind live threads or keep the mailbox alive | `book.transfer`, `sender.disconnect`, possibly a missing operation |
| 16 | Same-day step grouping | Step composition, `step.list` |
| 17 | The accepts-all default | The contactability policy |
| 18 | Quarantine throughput on large imports | `import.apply` |
| 19 | The blank-value rule, per field | The four writes that take a blank-value rule |
| 20 | Should telephony be in the core set | The core set: `call.log`, `call.place` |
| 21 | The task queue's cross-class ranking | `task.list`, possibly a new policy pair |
| 22 | `escalation.resolve` is a restart direction that derives `auto` | `escalation.resolve`, the resolving actor |
| 23 | The deletion feed transmits outward from the protective floor | `deletion_feed.report` |
| 24 | Nothing in the export pair says personal data left | `export.request`, `export.get` |
| 25 | Two collection writes take no key and give no reason | `stoprule.clear`, `campaign.freeze` |
| 26 | One confirmation lifts a collection of legal exclusions | `suppression.remove` |
| 27 | A standing paid watch has no lifecycle | `employment.check`, possibly three new operations |
| 28 | Outbound meeting operations are compensatable | `meeting.propose`, `meeting.confirm`, `meeting.reschedule`, `meeting.cancel` |

---

## 1. "Skip this person's next automatic touch" has no direct expression

**The fork.** No operation sets an enrolment's position, deliberately. The near needs are served:
enter at a chosen position on enrolment; disable a step for everyone; skip a queued *manual* touch
through the task queue; start somebody over with a stop-and-re-enrol that preserves the history.
What is not served is the everyday *"they just replied on another channel — don't send them
Tuesday's email, but keep them in the cadence."*

| Option | Cost |
|---|---|
| (a) Leave it unserved (current) | The everyday case is handled by pause-and-remember-to-resume, which strands people when nobody remembers |
| (b) A dated per-person skip of the next automatic touch | A new verb adjacent to `enrollment.pause`, chosen by phrasing under time pressure; and it edges toward a position setter |
| (c) Say plainly that the honest answer is pause and resume | Costs nothing to build; costs a stranded-person risk if the resume is never found — which is the failure the pause existed to prevent |

**Our answer today:** (c), on the grounds that `enrollment.pause` accepts a **dated automatic
resume**, which covers the case without a new verb. **Why we are unsure:** a dated pause is a
different mental object from "skip Tuesday", and if practitioners think in the latter they will
reach for the wrong operation.

## 2. Does routing an inbound arrival also transfer durable ownership of the contact?

**The fork.** This contract says no: `inbound_lead.route` and `inbound_lead.claim` decide who works
**the arrival**; `ownership.assign` changes the **contact's or account's durable owner**.

| Option | Cost |
|---|---|
| (a) Never (current) | Two calls where operating models expect one. Books and queues quietly disagree, and the disagreement surfaces at commission time |
| (b) Always | Changing one binding silently is the exact defect the ownership rules exist to prevent, and a claim made to answer a question in four minutes permanently re-owns an account somebody else built |
| (c) Only where the contact is currently unowned | Two behaviours from one call, which means the caller has to know the ownership state to predict the effect |

**Our answer today:** (a). **Why we are unsure:** in many operating models whoever picks up the lead
*is* the owner of record from that moment, and requiring two calls will produce books that disagree
with queues in exactly the way (a) was meant to prevent.

## 3. Is an import undo real practice?

**The fork.** `import.revert` is a judgement call, labelled as one, and it forces a real obligation
on the largest bulk write in the contract: `import.apply` must retain the prior value of **every**
field it overwrites, or refuse any policy that updates.

| Option | Cost |
|---|---|
| (a) Keep `import.revert` (current) | The retention obligation on every updating import, forever — a storage and complexity cost on the most-called bulk write |
| (b) Drop it; the remedy is a corrective second import | "Compensatable" on `import.apply` becomes a claim with no operation behind it, and a forty-thousand-row mistake has no defined recovery |

**Our answer today:** (a). **Why we are unsure:** we do not know whether a forty-thousand-row
rollback is something practitioners actually do. If the real remedy is always a corrective second
import, we are paying a permanent obligation for an operation nobody runs.

## 4. Four operations hand work to a human. Is that the practitioner's mental model?

**The fork.** `conversation.assign` moves an inbox owner and promises nothing. `escalation.raise`
says the agent cannot or must not proceed. `handoff.create` passes a qualified prospect with
evidence and starts an acceptance clock. `approval.request` asks may-I. The boundaries are written
into all four — but the *description* is what actually decides which one gets chosen under time
pressure.

| Option | Cost |
|---|---|
| (a) Four distinct acts (current) | Four verbs to choose between at the moment somebody is stuck, which is when choosing badly is most likely |
| (b) Collapse assign and escalate | Loses the distinction between "you own this inbox now" and "I have stopped and you own the decision" — and an escalation grants nothing while an assignment implies the work continues |
| (c) Collapse escalate and request-approval | Loses the rule that an approval is a grant and an escalation is not; merging them lets an agent read a resolved escalation as permission |

**Our answer today:** (a). **Why we are unsure:** this is a question about how the job is described
in the room, and only somebody who has watched it can say whether two of these are one act in
practice.

## 5. Where does the first-response clock stop?

**The fork.** This contract stops it at the **first recorded touch** to the person after the
arrival, on any channel, including one logged after the fact from a phone — which is why there is no
separate acknowledgement operation.

| Option | Cost |
|---|---|
| (a) First recorded touch (current) | A dial that rang out counts, and a logged-after-the-fact touch counts. The number flatters |
| (b) First genuine two-way contact | Measures something materially different; the organisation will look far slower, and the measure now depends on the other person's behaviour rather than ours |

**Our answer today:** (a), because a call that did not connect **is a touch, not an error**, and
because (b) makes an internal service level depend on somebody outside the organisation. **Why we
are unsure:** these are two different numbers and a manager is held to exactly one of them.

## 6. Should a referred person inherit the referrer's collision slot at the account?

**The fork.** This contract says yes: a referral is a **successor touch**, so a referral chain
cannot quietly become four simultaneous touches at one company.

| Option | Cost |
|---|---|
| (a) Successor touch, inherits the slot (current) | At a large enterprise account, a legitimate second contact is blocked by a slot that logically belongs to a finished conversation |
| (b) Claims a new slot, `collision.check` decides case by case | A three-deep referral chain at one company becomes three or four people in outreach simultaneously, from one original email |

**Our answer today:** (a). **Why we are unsure:** the right answer probably depends on account size,
and the contract has no notion of account size. A rule that is correct at fifty employees may be
wrong at fifty thousand.

## 7. Does `phone_number.verify` belong as an operation?

**The fork.** It is included for symmetry with address verification, and because line type changes
calling rules, messaging permission and consent routing — which an SDR who dials cannot be asked to
guess.

| Option | Cost |
|---|---|
| (a) A standalone verification operation (current) | A second metered verification verb whose evidence base is thinner than the address one |
| (b) Line type is a purchased field on `contact.enrich` | Loses the verdict-plus-advice shape that makes the address version usable, and buries a compliance-relevant fact inside a general enrichment ledger |

**Our answer today:** (a). **Why we are unsure:** the evidence behind it *is* thinner, and (b) is a
coherent reading. Somebody who dials for a living can settle it in one sentence.

## 8. Does a permanent delivery failure suppress the *person* or only the *identifier*?

**The fork.** The suppression rule says a permanent failure "suppresses the person everywhere" and
produces an operational entry "scoped to the identifier that failed". Those two clauses can be read
as agreeing (the identifier is blocked in every campaign) or disagreeing (the human is blocked on
every identifier we hold for them). An implementer will pick one.

| Option | Cost |
|---|---|
| (a) Identifier-scoped: the failed address is blocked everywhere; other identifiers for the same human are unaffected | A rep "fixes" a dead address by guessing a new pattern at the same dead domain, re-sends, and the failure recurs — repeatedly, because nothing at the person level says stop |
| (b) Person-scoped: one permanent failure removes the human from all outreach | One bad row in a purchased file permanently removes a real prospect — and because the entry is `operational`, no evidence artefact is needed to create it |

**Our answer today:** (a), with the person's contactability downgraded and a hygiene finding raised,
so the human is visible without being excluded. **Why:** the failure is a fact about a mailbox, not
about a human, and "everywhere" is about **across campaigns**, not across identifiers. **Why we are
asking:** the current wording genuinely supports the other reading, and it is the kind of ambiguity
that only shows up six months into an implementation.

## 9. Are pause and freeze two verbs a practitioner actually uses?

**The fork.** `campaign.freeze` carries a **mandatory expiry and resumes itself**; `campaign.pause`
carries none and never expires.

| Option | Cost |
|---|---|
| (a) Both verbs (current) | Two verbs distinguished only by whether an expiry is required, chosen under time pressure by phrasing — which is exactly the defect this catalog exists to prevent |
| (b) One verb with an optional expiry | The expiry becomes defaultable, and the "frozen for the conference in March, still frozen in July" failure comes back |
| (c) Freeze only, with an explicit "no expiry" value | The incident verb now requires a decision about duration at the moment somebody is trying to stop the bleeding — against the rule that a protective act must never require anything extra |

**Our answer today:** (a). **Why we are unsure:** this is a vocabulary question about how the room
talks, and if practitioners say "pause it until the conference is over", the distinction we drew is
in the wrong place.

## 10. At account scope, should the kill switch act on domain-matched people, or only report them?

**The fork.** `account_membership.list` states a **basis** per row — `attached`, `domain_matched`,
`name_matched`, `none` — and `outreach.hold` at account scope reports residual risk from that basis.
It does not say which bases it *acts* on.

| Option | Cost |
|---|---|
| (a) Act on `attached` + `domain_matched`, report `name_matched` | At a company sharing a domain with a subsidiary or a reseller, a legitimate separate motion is stopped and somebody has to unwind it |
| (b) Act on `attached` only, report the rest | The colleague nobody attached keeps receiving prospecting emails the day after the deal closed — which is the exact failure the account scope was created to prevent |
| (c) Make it an input on the call | The caller must decide correctly during an incident, which is when they are least able to |

**Our answer today:** (a). **Why:** the cost of over-stopping is a lookup and a release; the cost of
under-stopping is a prospecting email to a brand-new customer. **Why we are asking:** it depends
entirely on how their account model is populated in practice, which we cannot know from outside.

## 11. Should the frequency cap exempt answers to inbound requests by default?

**The fork.** `frequency_policy.define` takes "which message classes count and which are exempt" as
an input with **no default**. The gate cannot evaluate a cap that has not been declared.

| Option | Cost |
|---|---|
| (a) No default (current) | An account that has not decided has an unusable cap, and the first-run experience is a refusal with a remedy nobody expected |
| (b) Default: answers to requests are exempt | A person who fills in three forms in a day receives three answers **plus** whatever the cadence owed them, and the brand-protection purpose of the cap is defeated in the one case where the person is most engaged |
| (c) Default: everything counts | An inbound question goes unanswered because a cold cadence used up the cap — the failure the answer-the-inbound rule exists to prevent |

**Our answer today:** (a), with a strongly worded note that answers-to-requests are normally exempt,
so the first person to define a policy is not guessing. **Why we are asking:** shipping a default
here is a real decision with a real failure mode on each side, and it is a brand judgement rather
than a legal one.

## 12. Is the test send a hard precondition of validation, or a strong recommendation?

**The fork.** A passing test send is currently a **precondition of a passing `campaign.validate`**.

| Option | Cost |
|---|---|
| (a) Hard precondition (current) | A team cloning a proven campaign across twenty segment variants pays a real send, real capacity and a human's attention twenty times — and will route around it |
| (b) Waived for a clone whose steps and content versions are unchanged | The merge is what breaks, and the merge differs per segment. A clone with identical content and a different audience can still render an empty company name |
| (c) A recommendation with a recorded override | The override becomes the default path within a week, and the rule becomes decoration |

**Our answer today:** (a), with (c) available as an **explicitly recorded** override rather than a
silent skip — so the exception is visible in the validation record. **Why we are asking:** a rule
people route around is worse than no rule, and only somebody launching twenty campaigns a week knows
whether this one gets routed around.

## 13. Does refusing to name a winning variant survive contact with practice?

**The fork.** `metric.compare` returns counts, the interval around each rate, the sample size
needed, an underpowered flag and the number of reads taken. **It never names a winner.**

| Option | Cost |
|---|---|
| (a) Never name a winner (current) | A team that cannot reach ~13,900 sends per arm never gets an answer from the tool, picks by feel anyway, and now does so with no record of the decision |
| (b) Name a winner with a mandatory confidence statement | An agent that re-reads hourly and stops when it "sees a winner" is wrong roughly a quarter of the time; and a winner is the one output an agent will strip of its caveats when summarising for a busy human |
| (c) Return an explicit `underpowered_lead` verdict that is typed as not-a-decision | Softer than (a), and the typed verdict is harder to strip than a prose caveat — but it will still be read as a winner |

**Our answer today:** (a). **Why:** the arithmetic is not negotiable, and the failure mode of (b) is
silent. **Why we are asking:** if (a) means the operation is never used, it has failed differently
and (c) becomes the honest compromise.

## 14. Where should `interested_later` land — a cooldown, or a nurture programme?

**The fork.** The contract routes `interested_later` to `cooldown.set` — a person-scoped dated
commitment the pre-send gates read. Without it, a shipped classification value changes nothing.

| Option | Cost |
|---|---|
| (a) Cooldown, plus a task at the cooldown date (current) | The person hears nothing for two quarters, and "come back in Q4" becomes a diary entry that depends on somebody working the task |
| (b) Cooldown **and** enrolment into a separate low-frequency programme | "Not now" is answered with "here is a different campaign", which is what the person was declining. It also needs its own basis and its own frequency accounting |

**Our answer today:** (a). **Why:** the contract can already express (b) — stop with a reason, enrol
into the nurture campaign — so nothing is lost by making (a) the default advice. **Why we are
asking:** which of these is the *default* is a practitioner call, and it changes what the classifier
is worth.

## 15. On a departure, should live threads be rebound, or should the mailbox stay alive?

**The fork.** `book.transfer` is declared `act` because it rebinds the sending identity on live
threads and **the prospect sees a new name arrive mid-conversation**. `sender.disconnect` is refused
while live enrolments or open conversations depend on the capability unless a rebind target is
supplied.

| Option | Cost |
|---|---|
| (a) Rebind, with the preview naming everybody who will see the change (current) | Eighty-eight prospects see a new name mid-thread. Threading continuity is spent and cannot be recovered by transferring back |
| (b) Keep the departed mailbox alive, monitored by the receiver, for a notice period | The reply path depends on a credential belonging to somebody who left — an authorisation problem — and the receiver answers prospects as somebody who no longer works there |

**Our answer today:** (a). **Why:** (b) has an agent, or a colleague, writing as a departed
employee. **Why we are asking, and this is the important part:** many teams do exactly (b) for a
notice period, and **the contract cannot currently express "this mailbox is monitored and read-only;
replies go out under the new name."** If that is real practice, this is not a wrong default — it is
a **missing operation**, and it is the strongest candidate for one that we found.

## 16. Same-day step grouping

**The fork.** Real cadences pair a call and an email on the same day, repeatedly and deliberately.
One surveyed platform models day-groups as first-class; most express it only as a near-zero delay.
The contract currently expresses it as two steps with a zero delay.

| Option | Cost |
|---|---|
| (a) Two steps with a zero delay (current) | "These two land on the same day" is an emergent fact, not a declared one — a schedule shift, a window edge or a pacing change can split the pair without violating anything |
| (b) A declared day-group | A structural concept added for one pattern, which every consumer of `step.list` must then understand |

**Our answer today:** (a). **Why:** composition covers the common case, and no rule currently
depends on the pairing being declared. **Why we are asking:** if the pairing is deliberate practice,
only a declared fact protects it.

## 17. The accepts-all default

**The fork.** Whether mail is sent to accepts-all addresses is a contactability policy the contract
refuses to default.

| Option | Cost |
|---|---|
| (a) No shipped default; the operator declares a position before sending (current) | Every new account must make a deliverability-literate decision before its first send |
| (b) A recommended starting position with the reasoning attached | A shipped recommendation becomes an unexamined norm, and the right answer genuinely differs by domain mix and risk tolerance |

**Our answer today:** (a). **Why:** the cost of a wrong default is bounce-driven reputation damage
that the account pays, not the contract. **Why we are asking:** an operator who has run both
positions knows whether the forced decision is discipline or just friction.

## 18. Quarantine throughput on large imports

**The fork.** `import.apply` sends ambiguous rows to a human queue. On a 40,000-row import that can
be 800 rows.

| Option | Cost |
|---|---|
| (a) A human queue for every ambiguous row (current) | At volume the queue stalls the import's value — and in practice somebody bulk-approves without reading, which is the mass-prompt failure in a different costume |
| (b) A stated policy for bulk quarantine resolution — batch rules, sampling, thresholds | Wrongly merged or wrongly created humans are the costliest hygiene failure, and a bulk rule makes them silently |

**Our answer today:** (a). **Why:** identity is the one place this contract consistently refuses to
guess. **Why we are asking:** only somebody who has run a 40,000-row import knows whether the queue
is real practice or a fiction.

## 19. The blank-value rule, per field

**The fork.** The blank-value rule — what an incoming blank does to an existing value — is a
mandatory input on four operations, declared once per write.

| Option | Cost |
|---|---|
| (a) One rule per write (current) | A mixed intent — clear the job title, keep the phone number — takes two writes |
| (b) A per-field rule | The largest writes in the contract grow their widest input, and the preview must render a per-field matrix to stay honest |

**Our answer today:** (a). **Why:** the mixed case looked rare in research. **Why we are asking:**
whether it is actually rare is exactly what an operator knows and we do not.

## 20. Should telephony be in the core set?

**The fork.** The core set caps at 30 and contains no telephony operation, yet the activity mix we
measured makes phone the largest class — 44 of 112 daily touches, 56 dials on phone-centric teams —
and every non-connect is a loggable touch, so `call.log` is written on the order of forty times a
day on a calling team, with `call.place` beside it. As the set stands, the highest-volume touch
class pays a lookup per dial — the failure the set's first membership rule exists to prevent.

| Option | Cost |
|---|---|
| (a) The current thirty, telephony a lookup away (current) | On phone-centric teams the most repeated act of the day is not in front of the agent, against the set's own first rule |
| (b) Add `call.log` (and possibly `call.place`), trading out lower-frequency slots | The cap forces something out — every current member has a stated argument, and the trade is a judgement about whose day this agent works |

**Our answer today:** (a) — by omission rather than decision; review flagged that this exclusion,
unlike every other one, was never argued. **Why we are asking:** whether this agent's day is
email-first or phone-first decides it, and that is a fact about your team, not about the domain.

## 21. The task queue's cross-class ranking has no declared policy

**The fork.** `task.list` arrives ordered and priority is part of the result — but the rule that
ranks work classes against each other (the inbound clock against cadence-due touches against
signal-driven tasks) is not something the account declares or an agent can read. Every other
behaviour-shaping rule in this contract is a declared policy with a define/get pair — frequency,
identity, contactability, assignment, workload, response, signal precedence. This one is an output
guarantee with nothing declarable behind it.

| Option | Cost |
|---|---|
| (a) The ordering is the implementation's — guaranteed, but opaque (current) | An agent asked "why is this first?" cannot answer — the defect `frequency_policy.get` exists to prevent — and which work outranks which is a management decision the operator never gets to make |
| (b) A prioritisation policy pair (define/get), with `task.list` rows carrying the reason for their rank | One more policy surface, and a badly declared policy becomes the operator's fault |

**Our answer today:** (a). **Why we are asking:** this is precisely "a default we set that should be
the operator's choice" — the feedback category this contract asks for by name.

## 22. `escalation.resolve` is the restart direction of a protective pair and derives `auto`

**The fork.** `escalation.raise` is protective — "I cannot or must not proceed; a named human owns
this now" — and derives `auto`, correctly. Its resolution is declared `control` × `reversible` ×
`auto`, which the derivation table gives freely. But the contract also states, without
qualification, that the restart direction of every protective pair is never `auto`, and every other
pair obeys it: pause/resume, hold/release, set/clear. So an agent that escalated *because it must
not proceed* can clear the gate itself, record that the work returns to the agent, and carry on. The
resolving human's identity is not required anywhere, which leaves L4 — an agent may transmit a
human's decision and may never author one — unenforceable at exactly the point it matters.

| Option | Cost |
|---|---|
| (a) `auto`, as declared (current) | The one bypass path available using nothing but operations the contract hands the agent. An unattended run can escalate and un-escalate without a human ever seeing it |
| (b) `confirm_once`, raised, with the deciding actor required — the shape `outreach.release` and `editorial_review.record` already use | One more gate on a path a human is usually driving anyway, and a resolution recorded by a human directly is a confirmation already |
| (c) Keep `auto` but require the resolving actor and extend `before_repeating` to name who resolved it | Cheaper, and it makes the record auditable without adding a prompt — but an agent may still write its own name |

**Our answer today:** (a), transcribed as declared. **Why we are asking:** the guide's own restart
rule and its own appendix row disagree here, and only the author can say which was meant. If it is
(b) or (c), the fix is a property change plus a required argument, not a wording change.

## 23. `deletion_feed.report` sits on the protective floor while it transmits outward

**The fork.** The floor is for operations whose non-performance is itself the harm, and the other
four members record a fact into our own store — somebody said stop, the platform restricted us,
somebody arrived, somebody asked. This one **sends something to a third party, per identifier**, to
meet a reporting deadline. It is declared `control` × `irreversible` × `auto`, protective.

| Option | Cost |
|---|---|
| (a) On the floor, `auto`, because the deadline is the obligation (current) | An over-broad or wrong report is an irreversible disclosure, which its own reversibility concedes; A13 says a `legal_obligation` exclusion blocks disclosure and transfer, not only sending. Nothing bounds the payload |
| (b) `act` + `confirm_once` — one report over a batch is one decision over a set, which the table admits | A gate on a deadline-driven obligation, which is the failure the floor exists to prevent, and the deadline does not wait for a human |
| (c) Keep `auto` and state what bounds the payload — which identifiers, which register, which window | The reason field carries the safety instead of the class, and a reason is not enforced |

**Our answer today:** (a), transcribed as declared, and the floor marker makes the membership
checkable rather than remembered. **Why we are asking:** every other outbound transmission in the
contract is `act` (`restriction.appeal`, `approved_content.submit`,
`messaging_registration.submit`), so either this one is different for a reason worth stating, or its
reach is wrong.

## 24. Nothing in the export pair says personal data left the system

**The fork.** `export.request` is declared `control` × `reversible`, `confirm_once` raised, and its
own raise reason says "an extract moves personal data out of the system where its handling rules are
enforced" — while `reversible` means a second call puts the world back. `export.get` is `read` ×
`reversible` × `auto` with no key. So whichever of the two is the moment of disclosure, that moment
is expressible nowhere in the family.

| Option | Cost |
|---|---|
| (a) As declared (current) | A guardrail reasoning about blast radius off `reversibility` is told an extract of personal data is undoable. `privacy_request.fulfill` gets the same shape right — `act` when it discloses, `irreversible`, `confirm_each` |
| (b) `export.request` becomes `irreversible`, with `act` or a conditional reach naming the disclosing half | A property change on a reporting operation people use constantly, and the gate stays `confirm_once` either way |
| (c) The disclosure is `export.get` — it gets the gate and the key, and `request` stays as it is | Collection is where the file actually leaves, which is arguably more honest, but it gates a read |

**Our answer today:** (a), transcribed as declared. **Why we are asking:** A13 binds this operation
precisely because of that disclosure, and the two rows as written cannot both be right.

## 25. Two collection writes take no idempotency key and give no reason

**The fork.** A collection write takes a key, and two shapes are exempt because a repeat is harmless
by construction: an absolute-valued setter, and an operation naming a terminal state. Four
operations claim one of those exemptions in the source's own words. Two do not — `stoprule.clear`
and `campaign.freeze` — and both are safety switches: clearing a stop rule leaves work running
unprotected, and `campaign.freeze`'s own note says freezing twice with different expiries "is a
change, not a repeat", which is the opposite of harmless.

| Option | Cost |
|---|---|
| (a) No key, no reason — carried as `key_exempt: unstated` (current) | A lost connection during "clear these stop rules" leaves the safety configuration in an unknown state, and `invocation.get` cannot answer because no key was sent |
| (b) Both take a key, like `campaign.pause` — which is `campaign.freeze`'s own twin and declares exactly this case | Two rows change; nothing else does. The cost is that every caller now generates a key for a call they may think of as a toggle |
| (c) Name the exemption that applies, if one does | Cheapest if the answer is "clearing is absolute-valued" — but freeze carries an expiry, so it is not obviously either shape |

**Our answer today:** (a), and the gap is declared rather than hidden: the catalogs print
`exempt: unstated`, which is the contract admitting it has no reason rather than implying one.
**Why we are asking:** these are the two operations where an unrecoverable partial run costs most.

## 26. One confirmation lifts a whole collection of legal exclusions

**The fork.** `suppression.remove` accepts a collection, reports per item, and is `confirm_once`. So
one yes authorises N lifts of what the contract calls the only lift — each of which needs its own
evidence, and whose own `before_repeating` says "the sends made during a wrongful lift cannot be
restored by any second act".

| Option | Cost |
|---|---|
| (a) `confirm_once` over the collection (current) | The gradient runs backwards against its own neighbours: `duplicate.merge`, also `control` × `irreversible`, raises to `confirm_each`, and `optout.confirm` — which sends one harmless email — is `confirm_each` over a single object. Merging two records gets a confirmation per item; lifting a legal opt-out gets one per batch |
| (b) `confirm_each`, raised, with the evidence per item | Slow by design, which is arguably the point, and the operation is rare |
| (c) `accepts_collection: false` — one lift, one call, one piece of evidence | Removes the batch case entirely rather than gating it, and a bulk correction after a mistaken import then has no route |

**Our answer today:** (a), transcribed as declared. **Why we are asking:** this is the one place in
the contract where the confirmation gradient runs opposite to the consequence, and whether bulk
lifting is real practice decides between (b) and (c).

## 27. A standing watch is charged for and has no lifecycle

**The fork.** `employment.check` is declared `read` × `auto`, and its basis is "per record checked,
**or per record watched per period**" against the `monitoring` allowance — which the register defines
as standing watch on a record for employer change. So one `read` can enrol a record in a recurring
paid subscription, and no operation anywhere starts, lists or cancels a watch. Its
`before_repeating` names only the last recorded check, which cannot tell a caller a watch already
exists, so repeating it double-subscribes — unattended, with no gate, because a `read` never has one.

| Option | Cost |
|---|---|
| (a) The watch lives inside the check, as the basis says (current) | Durable state we own and pay for, created by an operation the contract classes as changing nothing. Nothing can list it, nothing can stop it, and a plan cannot ask what it is already paying for |
| (b) A `control` operation for the watch with a list and a cancel — the shape every other durable thing in the contract has | Three operations for a capability not every provider has, and `employment.check` goes back to being a plain metered read |
| (c) Drop "or per record watched per period" and say the watch is out of scope | Honest and cheap, and it removes the only use of the `monitoring` meter from the contract — which is itself an argument that the meter was added for something the operations do not express |

**Our answer today:** (a), transcribed as declared. **Why we are asking:** this is the only operation
in twenty-one families that touches the `monitoring` meter, and whichever way it is settled the
register and the family have to agree about whether standing watches exist here at all.

## 28. Four outbound meeting operations are compensatable where a delivered message is irreversible

**The fork.** `meeting.propose`, `meeting.confirm`, `meeting.reschedule` and `meeting.cancel` are all
`act` × **compensatable** × `confirm_each`, raised. Every one of them mails a named person —
`meeting.propose`'s own `before_repeating` says a repeat "double-books the calendar and mails them
twice". Elsewhere the contract is firm that a delivered message cannot be undone: `message.send` is
irreversible, and so is a test send to an address you own. The source argues its side explicitly at
`meeting.cancel` — compensatable, because a new meeting restores the situation and not this one — so
this is two considered answers to the same question rather than an oversight.

| Option | Cost |
|---|---|
| (a) Compensatable, because the *meeting* can be restored (current) | Every guardrail reasoning about blast radius reads `reversibility` directly, and here it is told four operations that already put words in front of a prospect can be put back. The `confirm_each` on all four then rests on the raise annotation rather than on the table: drop the raise and `act` × `compensatable` derives `confirm_once` |
| (b) The outbound leg is irreversible, conditional where it only records a meeting invited elsewhere — the shape `meeting.book` already uses | `confirm_each` derives instead of being asserted, and the departure can go. Four rows change, and "reversibility" starts answering about the message rather than about the calendar |
| (c) Keep both readings by splitting the property's subject explicitly: the calendar entry is compensatable, the notification is not | Truthful and awkward — it means `reversibility` is about two different objects on one row, which the property does not currently allow |

**Our answer today:** (a), transcribed as declared, with the raise carrying the gate. **Why we are
asking:** the answer decides whether `reversibility` describes the state we hold or the effect on the
person, and every guardrail downstream reads it as the latter.
