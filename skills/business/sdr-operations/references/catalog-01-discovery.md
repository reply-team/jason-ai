<!-- Generated. Do not edit by hand. -->

# Family 1 — Discovery, enrichment and verification

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*People and audience* · 12 operations · contract 2.0.0

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
| `candidate.search` | "Find me people out in the wider world who look like this and we do not already have" | `read` | `reversible` | `auto` | the recorded result under this key — repeating spends again and the database has moved. | `metered`<br>*basis:* per call or per result returned; may resolve to zero<br>*meter:* `discovery` | `required` | `not_applicable` | `B10`, `C2` |
| `candidate.get` | "Show me everything about this found person that I have already paid for" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `B7` |
| `candidate.promote`<br>*questions:* 19 | "Buy this person's details and make them ours" | `control` | `compensatable` | `confirm_once` | the per-item promotion ledger under this key: created / matched / ambiguous / withheld / failed, with the spend per item. | `metered`<br>*basis:* per person revealed, plus the enrichment allowance per field where fields are revealed one at a time<br>*meters:* `reveal`, `enrichment` | `required` | `required` | `B1`, `B3`, `B6`, `B7`, `C2`, `C3` |
| `company.search` | "Find me companies that look like this" | `read` | `reversible` | `auto` | the recorded result under this key | `metered`<br>*basis:* per call or per result returned<br>*meter:* `discovery` | `required` | `not_applicable` | — |
| `company.resolve` | "Four spellings of the same company came in — which company is this, really?" | `read` | `reversible` | `auto` | the recorded resolution under this key | `metered`<br>*conditional:* none where the question is satisfied from companies we already hold; metered per item resolved otherwise.<br>*basis:* per item resolved<br>*meter:* `discovery` | `required` | `required` | `B1` |
| `contact.enrich` | "Buy the fields we are missing on these people" | `control` | `compensatable` | `confirm_once` | the per-item ledger under this key: per person, per field, which source answered, what it returned, what it consumed. | `metered`<br>*basis:* per field found and per source attempted<br>*meter:* `enrichment` | `required` | `required` | `A1`, `B2`, `B6`, `B7`, `C3`, `M6` |
| `company.enrich` | "Buy the firmographics we are missing on these companies" | `control` | `compensatable` | `confirm_once` | the per-item enrichment ledger under this key | `metered`<br>*basis:* per field found and per source attempted<br>*meter:* `enrichment` | `required` | `required` | `B6`, `B7`, `C3` |
| `employment.check`<br>*questions:* 27 | "Has this person left the company we have them at?" | `read` | `reversible` | `auto` | the last recorded check for this person and its time — the answer moves slowly and re-asking daily is pure spend. | `metered`<br>*basis:* per record checked, or per record watched per period<br>*meter:* `monitoring` | `required` | `required` | — |
| `email_address.verify` | "Will this address actually accept mail, and what does our policy say to do about it?" | `read` | `reversible` | `auto` | the recorded verdict for this address under this key, with its check time and the staleness window in force. | `metered`<br>*basis:* per address checked<br>*meter:* `verification` | `required` | `required` | `B8`, `D5`, `M6` |
| `phone_number.verify`<br>*questions:* 7 | "Is this number reachable, and is it a mobile, a desk line or a virtual one?" | `read` | `reversible` | `auto` | the recorded verdict for this number under this key, with its check time. | `metered`<br>*basis:* per number checked<br>*meter:* `verification` | `required` | `required` | `B8` |
| `enrichment_policy.define` | "Decide once, in what order we try to buy data, when we stop, and how much we will spend" | `control` | `reversible` | `confirm_once`<br>*departs:* raised above the derived `auto`: it carries four decisions with no safe default — source order, the acceptance predicate that stops the chain, per-field granularity, and a budget ceiling per run and per period — and every later purchase is made under it. | the policy in force, with its version and who set it | `none` | `none` | `not_applicable` | `C3` |
| `enrichment_policy.get` | "What is our current buying order and spend ceiling?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
