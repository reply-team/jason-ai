<!-- Generated. Do not edit by hand. -->

# Family 21 — Introspection and runtime

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The organisation around the work* · 15 operations · contract 2.0.0

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
| `operation.search` † | "Find me the operation for this" — the door to everything not sitting in front of you. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `operation.describe` | "Show me one operation in full" — what it does, what it refuses to do, its five properties, worked examples, and the operations it must not be confused with. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `operation.preview` | "Dry run this exact call" — who would be reached and how many, when the first touch would land, what allowance it would consume, and whether the effect resolves to a send. | `read` | `reversible` | `auto` | nothing at stake; a preview that cannot be produced without acting must refuse, and may never act. | `none`<br>*conditional:* it never spends the allowance it projects. | `none` | `required`<br>*conditional:* when previewing a call that takes a collection. | `C3`, `E2`, `G2`, `N4` |
| `plan.validate` | "Check a whole plan before any of it runs" — authority for every step, ordering, preconditions, live bindings, budget headroom and capacity. | `read` | `reversible` | `auto` | nothing at stake; re-validate after any change to authority, autonomy, budget or the people named. | `none` | `none` | `required`<br>*conditional:* one verdict per planned step. | `C5`, `H12`, `L2`, `N4`, `N5` |
| `contract.describe` | "Which version of this contract is in force, what was renamed from what, the derivation table and absence-defaults it binds you to, and how much of this account's vocabulary it covers" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `capability.list` † | "What is this credential actually allowed to do?" — granted, denied, or unknown, one answer per capability. | `read` | `reversible` | `auto` | nothing at stake; authority changes when the credential changes, so re-read at the start of any unattended run. | `none` | `none` | `required` | `A6`, `N5` |
| `vocabulary.list` † | "What values is this account allowed to use?" — stages, dispositions, reply categories, enrolment statuses, meeting outcomes, step intents, exit reasons, content-policy rule classes, and the properties a branch condition may be written over. | `read` | `reversible` | `auto` | nothing at stake; `unknown` is a legal answer for a kind this account has not declared, and a write of an undeclared value is a guess. | `none` | `none` | `required` | `A6`, `E12`, `H2` |
| `schema.describe` | "Which fields exist on this kind of record, and which of them can be written" — custom fields included. Returns no values. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `channel.describe` | "A channel's own rules" — whether automation is sanctioned there at all, how its message allowance works, how long a reply window stays open, what recording consent it expects. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `E12`, `E15`, `H8` |
| `adapter.describe` | "How much, how fast, inline or queued" — request ceilings, batch ceilings, page sizes, and the scope and lifetime of an idempotency key. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `N7` |
| `adapter.verify` | "Prove by test, not by assertion, which operations this installation actually fulfils, how faithfully, and where a promise is composed out of several smaller acts." | `read` | `reversible` | `auto` | the last verification record and its timestamp | `metered`<br>*basis:* request allowance, per call made during the run<br>*meter:* `request` | `required`<br>*conditional:* so an interrupted run is not paid for twice. | `required`<br>*conditional:* one verdict per operation. | `N6` |
| `job.get` | "How far has this queued work got, and what happened to each item?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `job.cancel` | "Stop queued work. Items already done stay done" | `control` | `irreversible`<br>*conditional:* cancelling does not undo the items already processed. | `confirm_once` | the job's state and its per-item ledger | `none` | `none`<br>*conditional:* the operation names a terminal state, so a repeat is harmless.<br>*exempt:* `terminal_state` | `required`<br>*conditional:* processed / cancelled / already-terminal per item. | — |
| `invocation.get` | "I lost the result of a call — show me what actually happened, by the key I sent, instead of running it again." | `read` | `reversible` | `auto` | nothing at stake; if the key is unknown the answer is `never_seen`, which is different from `seen_and_failed` and must never be collapsed with it. | `none` | `none`<br>*conditional:* it is keyed on one; that is its whole purpose. | `required`<br>*conditional:* it returns the original call's per-item ledger. | — |
| `term.resolve` | "Someone said a word I do not use — a colleague's shorthand, an inherited spreadsheet header, another system's label. Which concept is it?" | `read` | `reversible` | `auto` | nothing at stake; the answer is resolved, ambiguous or unknown — never a guess. | `none` | `none` | `required` | — |
