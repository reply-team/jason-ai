---
name: audience-building
description: >
  Turn raw prospect data into a clean, deliberately-shaped audience: decide who belongs, source
  or import the people, state how two records become one person, check what is actually reachable,
  and freeze the result into a list a campaign can address. Use when the user wants to import
  contacts, find or build an audience, extend a list, define a segment, or asks what to do about
  duplicates and messy data.
metadata:
  version: 3.0.0
  pack: ai-sdr-core
  category: strategy
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [audience, icp, segmentation, lists, data-quality]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [campaign-launch, sending-guardrails, approval-boundaries]
---

# Audience building

> **Partly an expert skeleton.** The mechanics below are complete and safe to follow. The
> judgement calls — what makes a segment worth targeting, when a list is too dirty to send
> to — are marked `TODO(expert)` and need validation by outbound experts before this skill
> leaves `draft`.

## Purpose

Who you contact decides more about the outcome than what you write. This skill covers
turning a pile of data — a file, an export, a search result, a pasted block — into an audience
you can defend: a stated definition of who belongs, fields that mean what they claim, duplicates
resolved under a policy somebody wrote down rather than by accident, addresses checked before
they are sent to, and people frozen into a list a later campaign can address.

## When to use / when NOT to use

- Use for sourcing, imports, list building and extension, segment definition, verification, and
  any question about duplicates or data quality.
- Use *before* `campaign-launch` — the audience is a precondition for the launch, not a
  step inside it.
- **Sourcing is in scope.** Earlier versions of this skill said the contract had no way to find
  new people and offered four routes outside it. That premise is dead: the contract has twelve
  discovery, enrichment and verification operations, and *Where an audience comes from* under
  *Planning guidance* names which operation each route actually is.
- What is still NOT in scope is deciding **who is worth having**. The ICP is the user's call, and
  this skill refuses to infer one from whatever the data happens to contain.
- Do NOT use for judging campaign results — that is `performance-analysis`.

## Prerequisites

- Data in hand, or a route to it — see *Where an audience comes from*.
- The user's ICP if one exists — check their stored preferences and ICP notes before asking
  them to repeat it. The contract has `segment.define` for the audience definition, but the
  user's durable preferences live in the agent's workspace rather than in the contract; that
  boundary is real and not a gap to be filled with an invented operation.
- A ceiling wherever a route spends. `enrichment_policy.define` fixes the buying order, the
  predicate that stops the chain and the spend ceiling per run and per period;
  `budget.set` is what makes a ceiling a hard stop instead of an alert (C5).
- Operations this skill composes:
  - **sourcing** — `candidate.search`, `company.search`, `company.resolve`, `candidate.promote`,
    `inbound_lead.record`;
  - **ingest** — `source.inspect`, `import.apply`, `import.get`, `import.revert`,
    `contact.create`, `contact.update`;
  - **identity** — `identity_policy.define`, `identity_policy.get`, `contact.resolve`,
    `duplicate.list`, `duplicate.merge`, `duplicate.link`, `duplicate.reject`,
    `duplicate.unlink`;
  - **filling gaps and checking reachability** — `contact.enrich`, `email_address.verify`,
    `contact.normalize`, `field.define`, `contactability_policy.define`;
  - **shaping and freezing** — `segment.define`, `segment.preview`, `segment.materialize`,
    `list.create`, `list.get`, `list_membership.add`, `list_membership.remove`;
  - **signing it off** — `audience.assess`, `audience.screen`.

  See `sdr-operations` for their five properties. Approval is derived rather than chosen: the
  bulk writes and every metered call carry a gate, and `approval-boundaries` is where the
  derivation lives.

## Planning guidance

### Where an audience comes from

A user who says "I want to sell into LATAM" and has no list is asking a real question, and
"discovery is a gap" was never an answer to it — it is now not even true. There are five routes,
each one is a named operation, and the job is to choose between them rather than to invent one:

1. **A list the user already has.** A CRM export, a conference roster, past customers, a
   spreadsheet someone on the team maintains. `source.inspect` reads it and writes nothing;
   `import.apply` puts the people in. The most common case by far and the fastest to first
   value — ask for this first, every time, before proposing anything that spends. A sourcing
   tool the user already owns lands here too: a connected tool, an API or a data product they
   pay for produces rows, and rows are route 1 whatever produced them. Reaching into that tool
   is not an operation this contract has, and it does not need to be.
2. **Outward search.** `candidate.search` finds people in the wider world who match a
   description and whom we do not already hold; `company.search` does the same for companies,
   and `company.resolve` collapses four spellings of one company into one. Both searches are
   `read` and both are **metered** — and neither returns contact details, because buying is
   never a side effect of searching (C2). Turning a result into somebody we own is
   `candidate.promote`, and that is the call that spends.
3. **What we already hold, filled in.** The cheapest audience is often the one already in the
   database with three fields missing. `contact.search` finds the shape, `segment.preview` says
   how big it is before anything is spent, and `contact.enrich` buys the missing fields under
   the order and ceiling set by `enrichment_policy.define`. "Withheld by policy" is never folded
   into "not found", and "unknown" is never a negative — they lead to opposite next actions (B7).
4. **People who arrive on their own.** Somebody filled in a form, replied to a colleague, asked
   to be contacted. `inbound_lead.record` writes the arrival down immediately: it is protective,
   it needs no approval, and delaying it burns the only thing inbound work is measured on.
5. **Manual research.** Slow, and entirely legitimate for a short list of high-value accounts.
   Do not talk a user out of it when the list is twenty companies. It is a human errand with no
   operation of its own — what it produces re-enters at route 1.

**What the adapter decides, not this skill.** An operation existing in the contract is not a
promise that the provider in use performs it. Whether `candidate.search` or `contact.enrich` is
fulfilled, partially fulfilled or absent is a fact about the product, and the adapter skill for
that provider is what states it. Consult it rather than assuming, and treat a partial as a
partial when planning around it.

Whichever route, the ICP decision stays the user's and has to be explicit before sourcing
starts: who counts as a fit, which signals matter, what disqualifies. An audience assembled
without a stated ICP cannot be judged afterwards — when the campaign underperforms there is
no way to separate bad targeting from bad copy.

### Decide these with the user, before touching anything

1. **Who belongs.** State the segment in one sentence that could be checked against a row:
   "VP+ of engineering at 50–500 person B2B SaaS in EU/US". `segment.define` saves it, and every
   definition names the universe it is written against — a count is a count of what one universe
   happens to know on one day, never a market size, and counts from two universes are never
   added (D2). A segment nobody can check is a segment nobody can improve later.
   - TODO(expert): what makes a segment worth running versus too broad to learn from —
     minimum viable size, how many segments to run in parallel.
2. **Field mapping.** `source.inspect` proposes; it never writes. Inspect the actual headers
   rather than assuming them — encoding, date formats, unit ambiguity on revenue and headcount,
   header rows sitting inside the data. Identity is the email address; everything else is
   optional enrichment. Every detected column is either mapped or **individually named as
   discarded**, because nothing is dropped silently (B10); a column carrying something we have
   nowhere to put is `field.define`, not a shrug. Show the user a two- or three-row sample with
   the proposed mapping — silent field mismatches poison a database in a way that is expensive
   to unwind.
3. **How two records become one person.** This is two decisions, and older versions of this
   skill collapsed them into a single import argument:
   - **The identity policy** — `identity_policy.define`. How we tell whether two records are the
     same human, field by field; a generic similarity threshold is not an acceptable expression
     of one (B5). It is a stated, versioned policy object that names who set it, not a flag on a
     write, and records already written under the old policy are not re-decided when it changes.
     Read what is in force with `identity_policy.get` before defining anything new.
   - **The duplicate policy and the blank-value rule on the write itself** — stated on
     `import.apply` and on `contact.create` every single time, because there is no safe default
     and the contract refuses to invent one (B3). *Skip existing* never overwrites stored data
     and is the right answer unless the user has a reason. *Update existing* treats the incoming
     file as more authoritative than what is stored, which is a deliberate choice with real
     consequences.

   Whatever the policy says, `contact.resolve` may still answer **ambiguous** — a legitimate
   terminal answer that routes to a human and is never coerced into a match (B1). Decide who
   looks at those rows before the import runs, not after it.
4. **Destination, and whether it is criteria or a fact.** A segment is criteria; a list is a
   frozen fact with a date on it, and outreach runs against frozen membership or "who did we
   contact in June" becomes permanently unanswerable (D1). `segment.define` plus
   `segment.materialize` freezes a definition into a list; `list.create` plus
   `list_membership.add` builds one directly. Reusing beats proliferating; a dozen
   near-identical lists is how audiences become unmanageable. `list_membership.remove` takes
   people off again, and removing somebody from a list removes them from that list and nothing
   else (D3).
5. **Exclusions.** Anyone suppressed, anyone already in an active campaign, anyone contacted
   recently. Deciding this *now* is what prevents contacting the same person twice from two
   directions, and `audience.screen` is what enforces it against the frozen list.

### Duplicates already in the database

An import is not where existing duplicates get fixed, and asking it to do that is how one
becomes two problems. `duplicate.list` finds where we think we hold the same human twice and
**decides nothing**: a hygiene finding is a proposal, and applying it is always a separate
operation with its own approval and its own reversibility (B11).

- `duplicate.merge` — these records are one person. Irreversible, and `confirm_each` per cluster,
  because each cluster is a separate irreversible judgement about a different human.
- `duplicate.link` — same human, two records, and both have to stay.
- `duplicate.reject` — those two are not the same person; stop proposing the pair.
- `duplicate.unlink` — we were wrong about a link.

No merge, enrichment, refresh, normalisation, import or automated fix may clear an opt-out, a
permanent failure, a complaint or a legal hold: a merge unions negative state (B2). That rule is
what makes "clean up the duplicates" safe to say out loud.

## Execution guidance

1. **Inspect first — zero writes.** `source.inspect`: row count, headers, structural findings,
   and obvious junk — missing or malformed identities, empty rows, columns that are clearly
   mislabelled. Report what you found before proposing anything.
2. **Settle the inputs that have no default.** Duplicate policy, blank-value rule, owner, account
   binding, provenance, and the idempotency key generated *before* the first attempt. Provenance
   is required at creation rather than added afterwards (B6), and assignment to somebody who is
   not an active person is a refusal, not a write (B13).
3. **Preview it.** `operation.preview` against `import.apply` returns projected created /
   updated / skipped / quarantined counts, duplicate clusters, missing-required-field counts and
   any metered projection against its ceiling. A preview never acts and never spends (N4). A
   result an order of magnitude away from expectation stops here, while stopping is still free.
4. **Show the plan and get approval.** Source, row count, field mapping, identity and duplicate
   policy, destination, exclusions. `import.apply` is `confirm_once` **always**, including
   create-only, because the size is unbounded and the blast radius is the whole database — see
   `approval-boundaries`.
5. **Run it, and keep the key.** Large imports commonly complete asynchronously; the operation is
   done when the work is done, not when the request returns. A re-run without the key is a second
   import, not a retry, and a lost result is recovered by key rather than by a blind retry.
6. **Reconcile from the ledger.** `import.get` — `job.get` while it is still running — returns one
   outcome per row with its reason. Report created, updated, skipped, quarantined and failed
   counts plus the first few failure reasons verbatim. A high skip count under *skip existing*
   means duplicates were found: expected, not a fault. Never round a partial result up to
   success, and never re-run against "it failed", which repeats every row that actually
   succeeded. `import.revert` is there for an import that was simply wrong.
7. **Resolve the quarantined rows.** Ambiguous identity is a human's decision and stays one (B1).
   A row forced into a match to make the numbers tidy is a stranger's data written over an owned
   record.
8. **Verify separately, on purpose.** Creating and importing never buy and never verify (C4), so
   `email_address.verify` is its own pass with its own approval and its own meter. Keeping them
   apart is what stops an unbounded spend hiding inside the most-called write in the contract.
9. **Assess before freezing.** `audience.assess` is the readiness read, and it is the artefact a
   human signs.
10. **Freeze the population.** `list.create` plus `list_membership.add`, or `segment.define` plus
    `segment.materialize`. Verify membership by reading the list back with `list.get` and
    comparing counts against expectation.
11. **Screen it.** `audience.screen` removes everyone we must not contact and reports the
    removals by name so the list can be rebuilt (D3). This is the first of two screens; the
    second runs immediately before enrolment because exclusion state moves in between (D4), and
    `campaign-launch` owns that one. On a week-long build, somebody always opted out in the gap.

### Data quality

- An unreachable identity is worse than a missing row: it costs sender reputation.
  `email_address.verify` reports what the protocol said separately from what our policy advises,
  `accepts_all` is a terminal verdict that no amount of re-checking resolves, and verification
  never delivers anything to the person being verified (B8). Validate before sending, not
  after — see `sending-guardrails`.
- What to do with each verdict is `contactability_policy.define`, and it has to answer for
  accepts-all domains, unknown verdicts, role accounts, free mailboxes, disposable addresses and
  the re-verification staleness window. Contactability is a declared policy, never a default
  (D5).
- House formatting is `contact.normalize`, which keeps both the raw and the normalised value and
  declares the rule set that produced them rather than applying a silent default (B9).
- `audience.assess` is where "too dirty to send to" becomes a number somebody can argue with:
  completeness per required field, verification distribution and staleness, duplicate clusters,
  field age, formatting defects, exclusion hits, contactability hits, jurisdiction mix and
  contacts per account — every figure a count against a **stated threshold**, with an as-of. It
  reads only and never spends (D6). The operation reports; the thresholds are the expert's.
- Remove known-bad addresses with `list_membership.remove` rather than leaving them to bounce
  repeatedly.
- TODO(expert): acceptable bad-address ratio before an import should be rejected outright,
  and how often to re-validate an ageing list.

### Suppression and consent

People who asked not to be contacted stay excluded permanently, across every list and campaign.
`optout.record` writes down that they said stop and `suppression.add` is what keeps them out;
both are protective, and neither waits for permission. Suppression is never inferred from silence
and never reversed without the user saying so explicitly — the release direction,
`suppression.remove`, is gated precisely where the recording is not. Deleting a record is not
erasing a person and it never lifts an exclusion (B12).

`audience.screen` is how that reaches a list. `outreach.precheck` answers the per-person
question — may I contact this person, on this channel, right now, and if not what would fix
it — and it belongs to the launch rather than to this skill; the job here is to build an
audience it has as little to refuse as possible.

Where consent rules apply to a region or channel, they constrain the audience definition itself —
not a filter applied later. Provenance is what makes that arguable afterwards: `provenance.set`
records where a person's data came from and under what terms, and `consent.prove` answers with
evidence rather than with a bare status.

- TODO(expert): the consent and record-keeping requirements the audience definition must
  satisfy per region.

## Validation

The audience is sound when: the segment is stated in checkable terms and saved, with the universe
it was written against; the mapping from `source.inspect` was shown and approved with every
discarded column named individually; an identity policy is in force and the duplicate policy was
stated on the write rather than defaulted; every row has an outcome with a reason and the
ambiguous ones went to a human; verification ran as its own pass; `audience.assess` was run and
read against thresholds somebody agreed to; membership is frozen with a date on it; and
`audience.screen` has run, with its removals reported by name.

## Reporting

Segment definition as agreed, with its universe. Counts created / updated / skipped / quarantined
/ failed, with reasons. The idempotency key the import went out under and the batch identifier —
that pair is what makes the work recoverable and resumable at all. List and segment identifiers,
names and freeze dates. Exclusions applied and why, naming who was removed. Metered spend
actually consumed, including zero (C1). And anything about the data that should change the next
import — that is the part a future run actually benefits from.

## Failure modes

- **Importing before showing the mapping.** The damage is silent and discovered weeks later.
- **Defaulting the duplicate policy** instead of asking (B3). *Update existing* chosen by
  accident overwrites data the user cared about.
- **Collapsing the identity policy into the write's duplicate policy.** One is a versioned
  statement about how two records become one human; the other is what this particular write does
  when it finds a match. Treating them as one field is how an account ends up with no stated
  matching rule at all.
- **A duplicate-heavy result** treated as an error: under *skip existing* it is the policy
  working. Confirm the user did not actually want updates.
- **Coercing an ambiguous match** because the row has to go somewhere (B1). It does not; it goes
  to a human.
- **Letting the import verify or enrich "while it is in there."** One pass over the data feels
  efficient, and it is how an unbounded spend hides inside the most-called write in the
  contract (C4).
- **Treating a `duplicate.list` finding as a decision** (B11). It is a proposal, and merging is
  irreversible.
- **Treating a partial import as complete.** State N succeeded, M failed, and why.
- **Building a list nobody can define.** If the segment cannot be stated in one checkable
  sentence, the campaign built on it cannot be learned from.
- **Approving a count instead of a population.** "212 contacts" is a number the user can neither
  check nor refuse on any ground. Name who is in the audience, or fix the audience.
- **Answering "I have no list" with a refusal.** There are five routes and every one of them is a
  named operation. A user left at "that is a gap" has been given nothing they can act on — and
  the gap they were being told about no longer exists.

## Safety

Bulk creation and modification of contact data runs only after the user approved the exact plan;
`import.apply` is gated whatever its projected outcome turns out to be. Sourcing and enrichment
spend real money, and every purchase is bounded before it starts by a ceiling it can hit and a
per-item ledger it writes as it goes (C3) — an alert that stops nothing is not a ceiling (C5).
Deletion and suppression are separate, consequential decisions and are never bundled into an
import. Prospect data is personal data: keep it where the user expects it and do not copy it into
reports or logs beyond what the outcome requires.

## Related skills

- `sdr-operations` — the discovery, contact, identity, list and audience operations this skill
  composes, and the invariants cited above by ID.
- `campaign-launch` — the usual next step once the audience exists, and the owner of the second
  exclusion screen.
- `sending-guardrails` — list quality is a deliverability input, not just a data concern.
- `approval-boundaries` — the gate on bulk writes and on every call that spends.

## Changelog

- 3.0.0 (2026-08-10): breaking, against the 325-operation contract, and the reasoning changed
  further than the names did. *Where an audience comes from* rested on "the contract has no
  discovery operation, so here are four routes outside it"; that premise is false, so the routes
  became operations — `candidate.search` and `company.search` for outward search, `import.apply`
  for a file, `contact.enrich` for filling gaps in people we already hold, `inbound_lead.record`
  for people who arrive on their own, and manual research named as the human errand it is. The
  duplicate-policy discussion split in two: `identity_policy.define` is a stated, versioned
  policy object about how two records become one human, and the duplicate and blank-value rules
  stay as mandatory inputs on the write; existing duplicates became the `duplicate.*` family,
  where a finding is a proposal and merging is a separate irreversible act. Adds
  `audience.assess` and `audience.screen`, the fitness and exclusion checks this skill already
  described in prose with nothing behind them, and adds verification as its own pass because
  importing never buys and never verifies. Renames: `contact.find` → `contact.search`,
  `contact.list.create` → `list.create`, `contact.list.add` → `list_membership.add`,
  `contact.deduplicate` → `duplicate.list` plus `duplicate.merge` / `duplicate.link` /
  `duplicate.reject`. Expert markers are unchanged and still unvalidated.
- 2.1.0 (2026-08-02): added *Where an audience comes from*. The skill used to answer a user
  with no list by naming discovery as a contract gap and stopping there, which is honest and
  useless at the same time. Sourcing is still out of scope — the four routes to it are now
  in scope, and refusing without them is listed as a failure mode.
- 2.0.0 (2026-07-30): renamed from `import-prospects` and rewritten vendor-neutral for the
  `ai-sdr-core` pack — Reply endpoint and CLI detail moved to `reply-operations-mapping` in
  the adapter pack. Scope widened from "import a file" to "shape an audience": segment
  definition, exclusions, suppression and consent added; expert judgement calls marked.
- 1.0.0 (2026-07-27): ported from the prototype workflow as `import-prospects`.
