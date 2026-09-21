---
name: sdr-operations
description: >
  The vendor-neutral contract of SDR business operations — 325 named atomic operations in 21
  families, from finding and verifying a person, through campaigns, steps, enrolment and
  conversations, to consent, oversight and measurement. Each one is fixed by five properties:
  what it reaches, whether it can be undone, what approval it needs, what to read before
  repeating it, and what it costs. Use when planning any outbound or inbound sales development
  work, when writing a plan another agent or person must execute, when deciding whether a step
  needs the user's approval, or before reaching for a provider-specific execution skill.
metadata:
  version: 2.0.0
  pack: ai-sdr-core
  category: operations
  maturity: draft
  status: active
  owner: skills-maintainers
  tags: [contract, operations, vocabulary, lexicon, vendor-neutral]
  tools: []
  api: []
  relations:
    recommends: [approval-boundaries, campaign-planning]
---

# SDR business operations

## Purpose

Outbound and inbound sales development decompose into a stable set of operations: find a person,
verify they are reachable, put them into a campaign, hold them, answer what comes back, record
what it meant, read the numbers, and stop when somebody says stop. This skill names those
operations and fixes their meaning, so that a plan survives a change of tool — it can be written
today, reviewed by somebody who has never seen an API, and executed tomorrow by a different agent
against a different product.

It is a **contract, not a playbook**. It says what an operation *is*: what it changes, whether a
real person is reached, whether it can be undone, what it costs, what to read before repeating it,
and whether the user must approve it first. It never says how to run good outreach. Whether this
campaign is worth running, who should receive it and what it should say is judgement, and
judgement lives in the playbook skills and in the person who owns the number.

Everything downstream reads these definitions. Playbooks compose the operations, the approval
rules read the approval property, a provider adapter implements them. All three have to mean the
same thing by `enrollment.pause`, which is why the meaning lives here and nowhere else.

The contract holds **325 operations in 21 families**, of which **30 are core** — the set an agent
keeps in front of it at all times. The other 295 are one lookup away, and *Execution guidance*
below is about how to reach them without reading the whole contract.

## When to use / when NOT to use

Use it:

- when writing or reading a plan, and when deciding what a step actually does;
- when you need to know whether an operation reaches a real person, whether it can be undone, or
  whether the user must approve it before it runs;
- when a word needs pinning down — someone's "cadence", "drip" or "play" has to become a concept
  before it can become a step (see *The register discipline*);
- when adding an operation: it is added to this contract first, and to an adapter second.

Two boundaries, and they are different boundaries.

**This layer says what an operation is; it never says how to run good outreach.** Sequencing
judgement, copy, targeting, how hard to push and when to stop pushing are not here. `references/chains.md`
shows eight jobs decomposed into operations, and even that is worked examples rather than advice.

**This layer never says what a particular product supports.** That is the adapter's job, and only
the adapter's. The contract describes the job an SDR organisation does, not any one product's
coverage of it, so an operation being defined here is *not* a promise that your provider performs
it. The adapter states, per operation, whether it is fulfilled, partially fulfilled or not
fulfilled, and `adapter.verify` proves that claim by test rather than by assertion.

**The old boundary is void.** Version 1.1.0 of this skill said the contract "starts once the
audience is decided" and had no operation for finding new people. That is no longer true. Family 1
is twelve discovery, enrichment and verification operations — `candidate.search`,
`candidate.promote`, `contact.enrich`, `email_address.verify` and the rest — and the same
correction applies to meeting booking, task queues, variant experiments and one-off direct
messages, all of which are now first-class. Any plan citing the old *Known gaps* list is out of
date; `references/non-operations.md` records what is genuinely and deliberately absent.

## Prerequisites

None. This skill is pure vocabulary and semantics — no credentials, no tools, no provider, no
installed adapter. It is readable before any of those exist, and that is the point of it.

## Planning guidance

**Name the operation before choosing the tool.** A plan step that reads `campaign.enroll` — 214
contacts into "Q3 SaaS founders", `confirm_once` with a preview — survives a change of provider,
can be reviewed by a person who has never seen the API, and can be resumed by a different agent
tomorrow. A step that reads "POST to the bulk endpoint" survives none of that.

Every operation carries five properties, and they are what decide how it may be executed.

| Property | Values | What it settles |
|---|---|---|
| **reach** | `read` · `control` · `act` | `read` changes nothing. `control` changes state we own. `act` reaches the outside world — a real person can receive something, or sending begins. |
| **reversibility** | `reversible` · `compensatable` · `irreversible` | `compensatable` means the state can be restored but the consequence cannot. It is named that way so it is not read as "mostly fine". |
| **approval** | `auto` · `confirm_once` · `confirm_each` | Derived from reversibility × reach, not authored. See the table below and `approval-boundaries`. |
| **before_repeating** | the observable state to read first | What makes a retry, a resumed run and an interrupted plan safe. |
| **cost** | `none` · `metered`, with a basis and a named meter | Money spent is a property of an operation. An agent that cannot see it cannot plan around it. A basis, never a price — prices are account facts. |

**Approval is derived, not chosen.** The cell is the whole rule:

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

Policy may raise the derived class. **Nothing may lower it.** Every raise is marked
`approval_departs: true` at the operation and states its reason in writing, so a hand-set value is
visibly a departure and is reviewed as one instead of passing for an ordinary field.

Three rules carry the safety of the whole model:

1. **Absence reads as the most dangerous value** (`N3`). A missing property is not a blank to be
   filled in with something sensible: it reads as reach `act`, reversibility `irreversible`,
   approval `confirm_each`, cost `metered`. An operation cannot become permissive by being
   incompletely written, which is the failure mode of every optional safety field ever shipped.
2. **Reach `act` is the line that matters.** Authoring is not sending: `campaign.create` is
   `control` and always produces a draft; `message.draft` is a `read`, because it persists nothing
   sendable. `campaign.activate` and `message.send` are `act`. The test is whether a real person
   *could* receive something, not whether one did — and **when unsure, treat it as `act`**.
3. **The protective floor.** An operation whose *non-performance* is itself the harm — stopping
   sending, recording that somebody said stop, opening a privacy request, writing down an event
   that already happened outside our control — derives `auto` whatever its reversibility. Waiting
   for permission to stop is the destructive choice. `optout.record` is `control` ×
   `irreversible`, which would derive `confirm_once`, and it is `auto` for exactly this reason.
   **The restart direction of such a pair is never `auto`**: `campaign.pause` is `auto` and
   `campaign.resume` is `confirm_once`, and that asymmetry is deliberate rather than an oversight
   to be tidied up.

**Invariant `E2`, in words, because it is the one most easily inverted by a well-meaning edit.**
Activating a campaign holding N enrolments is an act of magnitude N — not one decision because it
is one call. It follows that **enrolling into a draft and then activating must never be a cheaper
approval than enrolling into a live campaign.** Any edit that makes the two-step route cheaper has
built a way to send to a population without ever approving that population, and it will look like
a simplification when it is made. This paragraph is the one invariant this file restates; if it
ever disagrees with `references/invariants.md`, the invariants file is right and this file is the bug.

**Why a conditional property carries both a scalar and a detail.** Some operations genuinely change
class with state, and a machine and a reader need different things from that fact. `value` is
always the more dangerous side, and every gate — the derivation check, the idempotency-key rule,
the approval table — runs against it, so a conditional can describe the softer case without ever
softening the gate. `detail` is the sentence the agent reads to learn which case it is actually in,
and which call resolves it: `step.add` is `control` on a campaign that is not live and `act` on a
live one, with magnitude equal to the enrolments at or past the insertion point. A conditional
without a detail is an error, not a shorthand.

**What `accepts_collection` asserts.** `accepts_collection: false` is a positive statement that the
call carries exactly one object and therefore has no items to report on. It defaults to true —
absence reads as the dangerous value here too — so an operation that says nothing owes one outcome
per item, with the reason (`N2`). A partial success reported as a single verdict is a defect.

## Execution guidance

**Naming is `entity.verb`.** Two segments, a singular snake_case entity, a single-word imperative
verb, globally unique across all 325 names. The entity is the thing that changes, so it must be
nameable in its own right — list membership is its own entity (`list_membership.add`), not a verb
buried inside the contact's namespace, which is what the three-segment names of 1.x hid. Verbs mean
the same thing in every family: `get` reads one, `list` reads many, `define` and `set` write
policy, `record` writes down something that already happened outside our control.

### The register discipline

**The contract owns the concepts and the aliases it will accept on input. An adapter owns the words
a product displays.** Aliases in, labels out.

A user says "cadence", "drip", "play", "touch pattern". Those are inputs: `term.resolve` maps the
word onto a concept — resolved, ambiguous or unknown, never a guess — and the plan is then written
in the concept, `campaign`. What is shown back to a user is whatever the surface in front of them
calls it, and choosing that word is the adapter's business, not this contract's. The contract never
emits an alias.

The concepts, their preferred labels, the alternative labels accepted on input, the hidden labels
that are searchable but never displayed, and the definitions live in `lexicon/neutral.yaml`.

**The acceptance test:** delete every dialect pack and the contract still works — only the words
change. If something breaks, a concept leaked into a dialect and belongs back in the lexicon.

**Retired words are not aliases.** `sequence` and `metrics` are retired, declared as such in
`operations/families.yaml`, and a sentence written today that uses either of them as an entity is
naming something that does not exist. `sequence` became `campaign`; `metrics` collapsed into
`engagement`, with the grouping as an argument rather than a separate operation. They do not
quietly resolve, and they are not accepted on input either — the reader is told the word is retired
instead of having the token pass as ordinary prose. `contract.describe` states what was renamed
from what.

### The 30 core operations

<!-- BEGIN GENERATED core-operations -->

<!-- Generated. Do not edit by hand. -->

| Operation | Family | Reach | Approval | What it is for |
|---|---|---|---|---|
| `contact.search` | Contacts, identity and hygiene | `read` | `auto` | "Who do we already have that matches this?" |
| `contact.get` | Contacts, identity and hygiene | `read` | `auto` | "Show me this person's record" |
| `contact.create` | Contacts, identity and hygiene | `control` | `confirm_once`<br>*conditional:* auto for create-only over a single record; confirm_once for any collection and any policy that may update. | "Put these people in" |
| `campaign.list` | Campaign, schedule, windows and limits | `read` | `auto` | "What is running?" — every campaign with its state and health: fine, not progressing, degraded, blocked. |
| `campaign.get` | Campaign, schedule, windows and limits | `read` | `auto` | "Show me this campaign" — settings, senders, schedule and timezone policy, pacing, reply policy, declared successor, and the count of people in each state. |
| `campaign.pause` | Campaign, schedule, windows and limits | `control` | `auto` | "Stop this campaign" — incident response: never requires a lookup first and never waits for permission. |
| `message.draft` | Messages | `read` | `auto` | "Show me exactly what this person would receive" — the real bytes: merge fields resolved against this contact, fallbacks applied or reported missing, footer, identification block and unsubscribe mechanism in place. |
| `message.send` | Messages | `act` | `confirm_each` | "Send this message to this person" — one person, one channel per call, with that channel's own required elements and cost |
| `campaign.enroll` | Enrollment | `act`<br>*conditional:* bookkeeping while the campaign is not live, `act` the moment it is; resolved by the preview, never assumed. | `confirm_once`<br>*conditional:* confirm_once before go-live; confirm_once with a mandatory preview once live. One call over a set is one decision, and the preview is the approval artefact — it names the population, it does not count it. | "Put these fifty people into the Q3 campaign" — with an explicit collision policy, an explicit start position and an explicit first-touch timing: the authored delay, the next open window, or immediately. |
| `enrollment.list` | Enrollment | `read` | `auto` | "Who is in this campaign?" and "where is this person across everything?" — the same read from either end, and with historical participations included on request. |
| `enrollment.pause` | Enrollment | `control` | `auto` | "Hold this person here" — with a reason and an optional dated automatic resume. |
| `enrollment.stop` | Enrollment | `control` | `confirm_once` | "Take them out, and record why" — the reason is mandatory and must be able to name a different person as the cause. |
| `conversation.list` | Conversations and activity | `read` | `auto` | "What came back overnight, and what needs answering first?" — filterable by meaning, channel, owner, campaign and how long each has been waiting, ordered by what is most overdue |
| `conversation.get` | Conversations and activity | `read` | `auto` | "Read me the thread before I answer" — every message in order, who is on it, what we have decided it means, and what it is attached to |
| `conversation.classify` | Conversations and activity | `control` | `auto` | "What does this reply actually mean, in a value I can plan a branch on — and who decided it, from which message, and how sure were they?" |
| `task.list` | Task queue and touches | `read` | `auto` | "What do I owe today, in the order I should do it?" — everything due, including work a signal put there rather than a calendar |
| `task.complete` | Task queue and touches | `control` | `auto` | "I did this one" |
| `inbound_lead.record` | Inbound | `control` | `auto` | "Somebody raised their hand — record it now, and tell me who they are". |
| `inbound_lead.list` | Inbound | `read` | `auto` | "What has come in and not been worked, oldest first" — with how long each has been waiting and how long is left. |
| `outreach.precheck` | Consent, suppression and privacy | `read` | `auto` | "May I contact this person, on this channel, right now — and if not, when, and what would fix it?" |
| `suppression.add` | Consent, suppression and privacy | `control` | `confirm_once`<br>*conditional:* auto for a single verified identifier, which is protective; confirm_once for a domain, an account pattern or a collection. | "Never contact this again, and here is why" — a person, an address, a number, a profile, a domain or a whole account. |
| `optout.record` | Consent, suppression and privacy | `control` | `auto` | "They said stop" — records the act itself: what they said, where it arrived, when they did it, when we learned of it. |
| `sender.health` | Sending capability and deliverability | `read` | `auto` | "Can this sender send today, and if not why?" — connection, restrictions, warm-up position, capacity budgets, and the channel's automation sanction. |
| `approval.list` | Oversight, review and safety | `read` | `auto` | "What is waiting for a human to look at, and how long has it been waiting?" |
| `approval.resolve` | Oversight, review and safety | `act`<br>*conditional:* `act` on approve, `control` on reject. | `confirm_each`<br>*conditional:* confirm_each on approve, confirm_once on reject — and the resolution is the confirmation when a human performs it directly. | "Approve this, or reject it with a reason." Approving is the send. |
| `outreach.hold` | Oversight, review and safety | `control` | `auto` | "Stop everything for this person, this company or this domain, now" — the kill switch. |
| `engagement.summarize` | Measurement | `read` | `auto` | "How is this doing?" — one measure set over one scope, grouped the way the question actually needs. |
| `operation.search` | Introspection and runtime | `read` | `auto` | "Find me the operation for this" — the door to everything not sitting in front of you. |
| `capability.list` | Introspection and runtime | `read` | `auto` | "What is this credential actually allowed to do?" — granted, denied, or unknown, one answer per capability. |
| `vocabulary.list` | Introspection and runtime | `read` | `auto` | "What values is this account allowed to use?" — stages, dispositions, reply categories, enrolment statuses, meeting outcomes, step intents, exit reasons, content-policy rule classes, and the properties a branch condition may be written over. |

<!-- END GENERATED core-operations -->

### Finding the other 295

**Thirty operations are in front of you. The other 295 are one lookup away — and the lookup is the
skill.** To find an operation you do not already know: read `references/operation-index.md`, which
lists every operation in the contract by family with the first sentence of its intent, find the
name, then open the **one** family catalog that defines it (`references/catalog-NN-<slug>.md`) for
its full classification. **Never read all 21.** Reading four families to answer a question about one
is the same mistake as reading the whole contract, made more slowly.

At runtime the same lookup has operations of its own, and they are better than reading files where
they are available: `operation.search` finds the operation for a described job, `operation.describe`
returns one operation in full — its properties, what it refuses to do, and the operations it must
not be confused with — and `capability.list` says what this credential is actually allowed to do,
which is a different question from what the contract defines.

**Why the catalog is not presented whole.** A large set presented at once degrades selection: an
agent shown 325 operations chooses worse than one shown 30 and given a reliable way to find the
31st. The contract answers that with **retrieval**, not by shrinking the contract. Shrinking it
would trade a solvable attention problem for an unsolvable coverage problem — and the only
selection criterion available under pressure is what some adapter can already execute, which is
precisely how a contract ends up shaped like one product. Coverage and attention are different
problems and they get different fixes.

### The four references, and the question each one answers

- `references/invariants.md` — the 140 business rules the operations may never disagree with, in
  fourteen groups. Answers *why does this operation refuse, and in what order are the bars
  checked?* Cite a rule by its ID (`A5`, `E2`, `N8`); never restate it.
- `references/chains.md` — eight jobs written as the operations they decompose into, with the
  ordering, the gate at each step and the failure each step prevents. Answers *in what order?* Read
  the one chain that matches the job in front of you.
- `references/non-operations.md` — what the contract deliberately does not have, organised by what
  kind of thing each absence actually is: a business rule, an account policy value, a human errand,
  a refusal of principle, or a filter with a name. Answers *why is there no operation for X?* —
  read it before inventing a name.
- `references/open-questions.md` — the 28 forks where two readings are defensible, one was taken,
  and the other would change an operation rather than the prose around it. Answers *how settled is
  this?* An operation carrying `questions: [n]` is unsettled by fork n; say so rather than choosing
  silently.

**Gaps are stated, not improvised.** If a task needs something this contract does not define, name
the gap in the plan or the report. Do not invent an operation name: a name that exists only in one
plan is worse than an acknowledged gap, because no adapter implements it and the next agent will
not recognise it. Some absences are load-bearing statements in their own right — there is no
`consent.check`, because a permission status with no evidence behind it is exactly the answer that
cannot be used; `consent.prove` and `outreach.precheck` are what exist instead.

## Validation

An operation is correctly specified when someone can answer all six of these **without seeing any
product**: what changes · whether a real person is affected · whether it can be undone · what it
costs · what to read before repeating it · whether the user must approve it first. If any answer
requires knowing which product will run it, the specification has a hole, or the detail belongs in
the adapter.

A plan written against this contract is valid when **every step names a contract operation or an
explicit gap**, and **every step whose reach is `act` carries its approval gate**, with the preview
where the derivation demands one. A step naming an operation that does not resolve is not a
near-miss to be interpreted charitably; it is an unexecutable step.

## Reporting

Report the **operations performed with their per-item outcomes**, never the calls made.
`campaign.enroll` ×214 — 211 enrolled, 2 already in the campaign, 1 suppressed — is readable a month
later and comparable across providers; a transcript of requests is neither. Every operation
accepting a collection owes one outcome per item with the reason (`N2`), and a partial success
reported as a single verdict is a defect.

Record the **idempotency key** sent and the **identifiers returned**. The key is what lets a lost
result be recovered rather than re-run — `invocation.get` answers by key, `job.get` for queued work
— and the identifiers are what make the work resumable at all.

## Failure modes

- **Operation-shaped prose.** "Update the contacts" is not an operation; `contact.update` with a
  named field set is. Vague steps are where plans quietly go wrong.
- **Provider detail leaking in.** A step that mentions an endpoint, a payload field, a tool name or
  a product is knowledge in the wrong layer (`N8`). Move it to the adapter.
- **Assuming an operation exists** because it would be convenient. Look it up; 325 is well past
  what anybody remembers correctly.
- **Treating `act` as `control`** because nothing visibly broke in testing. The test is whether a
  real person *could* receive something.
- **Inventing a property value rather than reading it.** Absence is not a blank and it is not a
  licence to pick something reasonable — it is the dangerous value (`N3`). "It probably doesn't
  need approval" is how a `confirm_each` becomes an unattended send.
- **Answering an open question silently.** An operation carrying `questions: [n]` is unsettled on
  purpose. Choosing a side inside a plan, without saying so, converts a visible fork into an
  invisible assumption — and the next reader inherits the assumption without the argument.

## Safety

**This skill performs nothing.** It is safe to read at any time, in any state, before any
credential exists.

Its safety contribution is the **property model**: reach, reversibility and the derived approval
are what every playbook and every adapter read to know when to stop and ask. Weakening a
classification here weakens every guardrail downstream at once.

**Changing `reach`, `reversibility` or `approval` on an existing operation is a breaking change**,
not an edit — and it is made **in the operation fragments under `operations/`, never in prose**.
Prose cannot change the contract. Every normative table in this skill is generated from those
fragments, and **a table in this file that disagrees with a fragment is a bug in this file**, to be
fixed by regenerating rather than by editing the table. The same applies to a rule: it is changed in
`references/invariants.md`, in the one place, and everything that cites it follows.

## Related skills

- `approval-boundaries` — what a valid confirmation looks like, how long one lasts, and the narrow
  case where acting first is correct. It reads the approval property defined here; the vocabulary is
  shared and versioned together.
- `campaign-planning` — turns a business goal into a plan expressed in these operations.
- For provider execution: the **`reply-adapter`** pack maps these operations onto a real product's
  surface. Install it to make this contract executable — and note that **the adapter, not this
  contract, states what a provider can and cannot do**. A "not fulfilled" there is a fact about the
  product, never a reason to remove the operation from here.

## Changelog

- 2.0.0 (2026-08-10): breaking rewrite of the contract, not of the prose around it. **The property
  model changed**: `effect` (`read`/`write`/`act`) became `reach` (`read`/`control`/`act`) because
  `write` conflated a bookkeeping edit with a decision that leaves the building; `partial`
  reversibility became `compensatable`; the "idempotency check" became `before_repeating`, which is
  no longer confusable with the idempotency key; and `cost` was added as a fifth property. **The
  approval vocabulary changed** from `none` / `confirm` / `exact-text` to `auto` / `confirm_once` /
  `confirm_each`, and approval is now derived from reversibility × reach rather than authored, with
  every departure marked and justified at the operation. **`sequence` became `campaign`** and
  `metrics` collapsed into `engagement` with the grouping as an argument; both old words are retired
  rather than aliased. **Six families of 35 operations became 21 families of 325**, 30 of them core,
  and the contract now describes the job rather than one product's coverage of it. Every 1.x name
  either survives, is renamed or is split: `sequence.activate` → `campaign.activate`,
  `enrollment.create` → `campaign.enroll`, `conversation.read` → `conversation.get`,
  `sequence.add-step` → `step.add`, `sender.limits.update` → `sender_limit.set`; `contact.find`
  split into `contact.search`, `contact.resolve` and `candidate.search`; `contact.list.add` split
  into `list.create` and `list_membership.add`; `contact.suppress` split into `suppression.add` and
  `optout.record`; `metrics.sequence` became `engagement.summarize` with a grouping argument. The
  five properties, the invariants, the chains and the open questions are now generated or held in
  `operations/` and `references/`, and the hand-written `references/operation-catalog.md` is deleted
  as superseded. Maturity stays `draft`: 21 questions are open and no domain expert has signed off,
  and length is not evidence. See ADR-0007.
- 1.1.0 (2026-08-02): said explicitly that the contract begins once the audience is
  decided, and pointed the discovery gap at the routes in `audience-building` rather than
  leaving it a bare absence that a plan could only stop at.
- 1.0.0 (2026-07-30): initial contract — six operation families, the four properties
  (effect, reversibility, approval, idempotency check), naming rules.
