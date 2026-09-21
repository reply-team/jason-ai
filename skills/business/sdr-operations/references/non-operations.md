# What the contract deliberately does not have

An agent that cannot find an operation for something usually concludes the contract is incomplete.
Most of the time it is not: the thing being looked for is a rule, a policy value, a human errand, or
a refusal. This file is the answer to "why is there no operation for X", organised by *what kind of
thing X actually is*.

The general test the contract applies: **an operation is one named business act with one nameable
outcome, performed by calling it.** If the thing has no outcome of its own, or if performing it means
somebody doing something outside the system, or if it would be a filter with a name, it is not an
operation.

Every absence below carries its reason. An absence with no reason is a defect in this file, not a
silence to be filled by inventing a name.

**Rule codes** (`A5`, `H2`, `F6` …) name invariants in
[invariants.md](invariants.md). **Open question numbers** name the forks the contract has taken a
reversible position on; where one appears here, the absence is defended but not closed.

**Five things this file does not say are missing.** Prospect discovery (`candidate.search`,
`candidate.promote`, family 1), meeting booking (`meeting.book` and the rest of family 11), task
management (family 10), variant experiments (`variant.add` and its siblings in family 6) and one-off
direct messages (`message.send`, family 7) are all first-class operations. An earlier version of this
contract listed them as gaps; that list is void. If a plan cites it, the plan is out of date.

---

## 1. Business rules — true statements the contract enforces, that name no act

These govern *how operations behave*. Making any of them an operation would mean a caller could run
it, skip it, or run it in the wrong order — which is precisely what they exist to prevent.

| Not an operation | It is | Why it must not be one |
|---|---|---|
| The gate order (`A5`) | The internal sequence of `outreach.precheck` | An "apply precedence" operation would let a caller evaluate frequency before suppression. The order is load-bearing: bars 1–5 must be settled before any ceiling is even considered |
| The approval derivation table (reversibility × reach → approval) | Arithmetic over two declared properties | If it were callable it would be overridable. Policy may raise a class; nothing may lower it |
| The protective floor | A derivation rule on a class of operations | A "make this protective" call would be a way to mark an ordinary act `auto` |
| Absence-defaults (`N3`) | A reading rule for the document | It is how a reader interprets a blank, not something anybody does |
| "A merge unions negative state" (`B2`) | Behaviour inside `duplicate.merge` | A separate "union negative state" operation implies it could be skipped |
| "Permission does not travel; exclusion does" (`A15`) | Behaviour inside `job_change.record` | The fork is the act; the asymmetry is what the act means |
| The retention floor set (`A10`) | An enumeration `privacy_request.fulfill` obeys | An operation returning "what may I keep" invites an implementer to answer it differently each time. It is a fixed list of seven items |
| The nine base reply meanings (`H2`) | A fixed vocabulary | If it were configurable it would be configurable to zero, and no agent could plan a branch |
| "Quiet hours are law" (`A20`) | A constraint `send_window.resolve` reads | An operator can define blackouts, which is an operation. An operator cannot define quiet hours, which is why there is no operation for it |

## 2. Policy values that belong to the account, not to the contract

In every case the contract ships **the shape and no number**. There *is* an operation to set the
policy; there is no operation that returns the industry's answer, because there is no industry answer
that survives a look at its denominator.

| The number people look for | Where it lives | Why the contract ships none |
|---|---|---|
| Warm-up ramp figures | `warmup_plan.set` — operator configuration | **No ramp figure is published for a mailbox by anybody** (`F11`). Every figure in circulation is folklore with no publisher behind it |
| Bounce-rate threshold | `stoprule.set`, per account | The finding is categorical: **receiving providers publish no bounce-rate threshold at all.** Every published threshold belongs to a sending platform and is that platform's own account rule |
| Complaint-rate threshold | `stoprule.set`, per account | Published figures exist but are not comparable: one receiver's target is below 0.10% with a 0.30% ceiling, measured over authenticated mail delivered to *engaged recipients' inboxes*; a sending platform's is 0.1% review / 0.5% pause over a *representative volume of sends*. A single `complaint_rate` field with a single threshold would be actively misleading (`F6`) |
| Speed-to-lead target | `response_policy.define` | **`K8`.** The famous five-minute figure is from a 2007 study of web enquiries and is the origin of a norm, not a measured standard for any operator |
| Cadence length, number of touches, days between | `step.add`, per campaign | No credible cross-vendor standard exists; every template set differs |
| What "qualified" means | `qualification.record` — the account's own questions | **`I4`.** Carried whole, never scored, never forced into a named framework. Which framework to use is contested practice with no defensible cross-company answer |
| The quota unit | `goal.define` | The same job is paid on meetings booked, meetings held, meetings the sales team accepted, or opportunities created — four different numbers, and the market has not converged. A fixed unit list would make the contract wrong for most accounts |
| Duplicate policy and blank-value rule | Inputs on every matching write | **`B3`.** There is no safe default and the contract refuses to invent one. See open question 19 |
| What to do with accepts-all, unknown, role and free-mailbox addresses | `contactability_policy.define` | **`D5`.** Nothing here has a default, including the hardest question: may an address that has never been verified be enrolled at all. See open question 17 |
| Which company counts as the same company | `collision.check(collision_scope)` — mandatory, no default | Whether a subsidiary is the same company as its parent is a **commercial decision, not a data fact**. `company.resolve` reports it and never decides it |
| Signal precedence | `signal_policy.define` | **`J3`.** Competing signals need a stated order and there is no default |
| Which message classes count against a frequency cap | `frequency_policy.define` | Contested, and a shipped default would be a real decision. See open question 11 |

## 3. Human errands the contract records but cannot perform

Each of these is real work that appears in a chain. The contract's job is to name what must be true,
name who must do it, and hold the record afterwards.

| The errand | The nearest operation | Why the operation stops short |
|---|---|---|
| Publishing the records that make a sending domain authenticate | `authentication_requirement.list` | It lists what has to be published. **Publishing it is not ours to do** — it happens in a system this contract has no reach into, usually owned by a different team |
| Verifying an identity out of band | `restriction.record` (`remedy_kind: verify_identity_out_of_band`), `privacy_request.update` | The verification happens between a human and a platform, or a human and a subject. The contract records that it was required and that it happened |
| Writing and submitting an appeal | `restriction.appeal` | The operation submits; the words are written by a named human, usually with one chance. And it must be able to answer **`no_route_exists`** rather than pretending there is a remedy |
| Actually having the conversation | `call.place` dials; `call.log` records | The talking is not an operation. `call.log` records what came of it, and **records; it never sends and never advances a cadence** (`H6`) |
| Attending the meeting | `meeting.book`, `meeting_outcome.record` | Recording an outcome notifies nobody; moving or cancelling does |
| Hiring, activating and deactivating people | `actor.list`, `actor.get` | **Administering people is not in this contract.** The family reads their state and refuses to write against anyone who is not active |
| Signing off an audience before outreach begins | `audience.assess` | It produces the artefact a human signs. It reads only and never spends (`D6`) |
| Authoring an approval decision | `approval.resolve`, `escalation.raise` | **`L4`: an agent may transmit a human's decision but may never author one.** This is the boundary the whole oversight family exists to hold |
| Deciding which regime applies | the deadline derivation in `A4` | The contract computes from the applicable regime and the operator's service level and takes the strictest. Which regimes apply to this operator is a legal determination made outside it |

## 4. Refusals of principle — the absence is the statement

| What is missing | Why it is missing |
|---|---|
| Creating a replacement account after a restriction, on any channel | **`F13`.** It is a terms violation on its face. Naming it would present it to an agent as a remedy and make the contract complicit |
| Any operation that sets an enrolment's position | **`G8`.** The position is evidence of what this person actually received. Setting it asserts a history that did not happen, and corrupts the frequency record and the reply attribution at once. Starting somebody over is a stop with a reason plus a fresh enrolment, which keeps the ended participation as history. See open question 1 |
| Naming a winning variant | **`M7`.** Counts, intervals, required sample size and an underpowered flag; the human decides. See open question 13 |
| A composite reputation score | **`F6`.** There is no shared reputation scale across publishers, so a composite would be an invented quantity with nobody behind it |
| A benchmark, industry average or portable score | **`M7`.** Every candidate number either has no publisher, or has a denominator that makes it non-comparable |
| An obtainable share of a market | **`D2`.** A count is what one universe happens to know on one day; counts from different universes are never added |
| A portable score for influence, seniority or intent on buying-group members | **Buying-group roles are unscored labels.** A role is what somebody does in a decision, and compressing it to a number loses the only part that is actionable |
| An inbox-placement rate, or a "did they read it" measure | **`M2`.** Both are in the *not measurable* class. A placement test is typed as simulated placement and excluded from every denominator |
| Advice on how to sell | This is a contract, not a playbook. It names what the operations are and what rules they obey; it gives nobody advice on how to sell |

## 5. Readers that would be a filter with a name

The contract refuses a dedicated operation whose entire content is a filter over an existing one.
Each of these is a real request, answered by an existing operation with a parameter.

| The request | Answered by | Why not its own operation |
|---|---|---|
| "List the stale contacts" / "the unverified ones" / "everyone from Monday's import" | `contact.search` | Filtering by list membership, field age, verification verdict, import batch and provenance are **ordinary criteria**. A family of "list the stale ones" readers would need one operation per criterion |
| "Show me this person's finished participations" | `enrollment.list` with include-historical | One read from either end, with an explicit input. Two operations would be chosen by phrasing |
| "List this sender's restrictions" | `sender.health` | "Can this sender send today, and if not why" is **one** read, and it carries the full restriction records — cause or `undisclosed`, imposed time, expected clear time, remedy kind, recurrence count, appeal state |
| "Which campaigns use this template version?" | `template.get` | It already returns each version and its publication state, so it returns the live bindings too |
| "Check whether we have consent" | `consent.prove` or `outreach.precheck` | **There is no `consent.check` and no separate eligibility check.** A permission status with no evidence behind it is precisely the answer that cannot be used |
| "Acknowledge this inbound lead" | the first recorded touch | There is no separate acknowledgement operation because the clock stops at the **first recorded touch** on any channel, including one logged after the fact from a phone. See open question 5 |
| "Send an email" / "send a social message" / "send a text" | `message.send` | **One send verb.** A second verb in front of the highest-consequence act in the contract is the defect this catalog exists to prevent. Channel differences are inputs and outputs of one operation, never reasons for another |
| "What is the page size / rate limit / batch ceiling?" | `adapter.describe` | **`N7`.** The contract states meaning; the installation states its own limits. A ceiling in an operation's definition becomes a promise the next installation cannot keep |

---

## Telephony in the core set — a live tension, not a settled absence

Everything above is an absence the contract argues for. This one it does not.

Telephony is in the contract — `call.place` dials and `call.log` records — but **no telephony
operation is in the 30-operation core set**, and that exclusion was never argued. It sits badly with
the core-membership rule that puts the day's most repeated acts in front of the agent: on a calling
team the phone is the largest activity class, every non-connect is a loggable touch (`H7`), and
`call.log` is therefore written tens of times a day. As the set stands, the highest-frequency touch
class pays a lookup per dial — the exact failure core membership exists to prevent.

The fork is **open question 20**: keep the current thirty with telephony a lookup away, or add
`call.log` (and possibly `call.place`) and trade out lower-frequency members against a fixed cap. The
position held today is the first, by omission rather than decision. Whether this agent's day is
email-first or phone-first settles it, and that is a fact about a team rather than about the domain.

Until it is settled, do not read the exclusion as a judgement that telephony is peripheral. Plan
phone work with the same operations as everything else; the only thing the core set decides is what
an agent holds without looking it up.
