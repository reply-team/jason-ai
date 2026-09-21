<!-- Generated. Do not edit by hand. -->

# Family 2 — Contacts, identity and hygiene

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*People and audience* · 23 operations · contract 2.0.0

**Reading the table.** † marks a core operation. The five properties are *reach*
(`read` changes nothing · `control` changes state we own · `act` reaches the outside world),
*reversibility* (`reversible` · `compensatable` · `irreversible`), *approval*
(`auto` · `confirm_once` · `confirm_each`, derived from reversibility × reach), *before repeating*
(the observable state to read before running it again) and *cost* (`none` · `metered`, with its
basis and the meter it consumes). *Key* and *per-item* are the two standing obligations: an
idempotency key on every act, every metered call, every write accepting a collection and every
write creating a durable object. A write that takes no key says why in *exempt* — `absolute_valued`
(the same value written again), `terminal_state` (the same state arrived at again), or `unstated`,
which is the contract admitting it has no reason and not a claim that one exists. And one outcome
per item whenever a collection is carried. Where a property is conditional the cell holds the
dangerous reading, with the condition beneath it.

| Operation | Intent | Reach | Reversibility | Approval | Before repeating | Cost | Key | Per-item | Invariants |
|---|---|---|---|---|---|---|---|---|---|
| `contact.search` † | "Who do we already have that matches this?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `B10` |
| `contact.get` † | "Show me this person's record" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `contact.resolve` | "Here are some identity keys — is this somebody we already have?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `B1`, `B4` |
| `contact.create` †<br>*questions:* 19 | "Put these people in" | `control` | `irreversible`<br>*conditional:* compensatable where it created; irreversible where it updated. | `confirm_once`<br>*conditional:* auto for create-only over a single record; confirm_once for any collection and any policy that may update. | the per-item outcome ledger under this key: created / updated / matched / skipped / quarantined, with the reason. | `none` | `required` | `required` | `A9`, `A17`, `B1`, `B3`, `B6`, `B13`, `C4` |
| `contact.update` | "Change these fields on these people" | `control` | `compensatable` | `confirm_once`<br>*conditional:* auto for a single record; confirm_once for a collection. | the current field values, and the per-item ledger under this key. | `none` | `required` | `required` | `A1` |
| `contact.delete` | "Take this person out of the working database" | `control` | `irreversible`<br>*conditional:* compensatable inside the account's restore window; irreversible after it. | `confirm_once`<br>*departs:* raised: it destroys working history other operations key on. | the record's presence and its restore eligibility | `none` | `required` | `required` | `B12` |
| `contact.restore` | "Bring back the person we deleted" | `control` | `reversible` | `auto` | the record's presence | `none` | `required` | `required` | `B12` |
| `timeline.get` | "What has actually happened to this person?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `provenance.set` | "Record where this person's data came from, and under what terms" | `control` | `reversible` | `auto` | the provenance currently recorded, with its own recorded time. | `none` | `required` | `required` | `A16`, `A17`, `A18`, `B6`, `M6` |
| `job_change.record` | "This person has moved to a new employer — fork the record" | `control` | `irreversible` | `confirm_once` | the person's current employment record and whether a fork already exists. | `none` | `required` | `required` | `A15` |
| `cooldown.set`<br>*questions:* 14 | "Come back to this person in Q4, not before" | `control` | `reversible` | `auto` | the cooldown in force, its scope and its date | `none` | `required` | `required` | — |
| `contact.disqualify` | "This person is not a prospect, and here is why" | `control` | `compensatable` | `auto` | the current qualification state and its reason | `none` | `required` | `required` | `K6` |
| `contact.requalify` | "We were wrong, or the situation changed — they are a prospect again" | `control` | `reversible` | `auto` | the current qualification state, its reason and its date. | `none` | `required` | `required` | `K6` |
| `contact.normalize` | "Put these records into our house format, and keep what was there before" | `control` | `reversible` | `confirm_once`<br>*departs:* raised above the derived `auto` as a bulk write: one call rewrites values across every record in scope under the account's declared rule set. | the per-item ledger under this key, and the rule set that produced it. | `none` | `required` | `required` | `A1`, `B2`, `B9` |
| `identity_policy.define` | "Decide how we tell whether two records are the same human" | `control` | `reversible` | `confirm_once`<br>*departs:* raised above the derived `auto`: it carries seven decisions, none with a safe default, and records already written under the old policy are not re-decided. | the policy in force, with its version and who set it | `none` | `none` | `not_applicable` | `B2`, `B5`, `B9` |
| `identity_policy.get` | "What rules are we currently matching people under?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `duplicate.list` | "Where do we think we have the same human twice?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `B4`, `B5`, `B11` |
| `duplicate.merge` | "These records are one person — combine them" | `control` | `irreversible` | `confirm_each`<br>*departs:* raised above the derived `confirm_once`: each cluster is a separate irreversible judgement about a different human. | the per-cluster merge ledger under this key, and whether the losing records still exist. | `none` | `required` | `required` | `A1`, `B2`, `B4`, `B11` |
| `duplicate.link` | "Same human, two records, and both have to stay" | `control` | `reversible` | `auto` | the pair's current link state | `none` | `required` | `required` | `B11` |
| `duplicate.unlink` | "We were wrong — those are two different people" | `control` | `reversible` | `auto` | the pair's current link state | `none` | `required` | `required` | — |
| `duplicate.reject` | "Those two are not the same person — stop asking me" | `control` | `reversible` | `auto` | the pair's current decision state | `none` | `required` | `required` | `B11` |
| `field.define` | "This source carries something we have nowhere to put — make somewhere" | `control` | `compensatable` | `confirm_once` | whether a field of this name and type already exists. | `none` | `required` | `not_applicable` | — |
| `field.retire` | "We do not use this field any more" | `control` | `irreversible` | `confirm_once` | whether the field is still referenced by a mapping, a segment or a policy. | `none` | `required` | `not_applicable` | — |
