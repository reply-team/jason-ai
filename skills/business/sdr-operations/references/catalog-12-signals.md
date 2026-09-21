<!-- Generated. Do not edit by hand. -->

# Family 12 — Signals and triggers

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The conversation, the queue and the day* · 10 operations · contract 2.0.0

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
| `signal.list` | "What has happened out there that I should know about" — someone changed job, someone was on the pricing page, funding was announced, usage jumped, someone engaged publicly. | `read` | `reversible` | `auto` | nothing at stake, but each result carries when it was observed and how long it stays valid. | `metered`<br>*conditional:* metered where the observations come from a purchased feed; none for observations we produce ourselves.<br>*basis:* per result returned, where the observations come from a purchased feed<br>*meter:* `signal` | `required`<br>*conditional:* required where the results are metered; none where we produce the observations ourselves. | `not_applicable` | `J1`, `J2`, `M7` |
| `signal.get` | "One observation in full, including what it was derived from". | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `J1` |
| `signal.acknowledge` | "Handled — stop showing me this". | `control` | `reversible` | `auto` | whether this observation is already acknowledged and by whom. | `none` | `required` | `required` | `J2` |
| `signal_policy.define` | "When two things fire on the same person at once, which one wins, and does it interrupt what is running, add to it, or wait its turn?" | `control` | `reversible` | `auto` | the policy in force; written as a whole policy, so replaying it is harmless. | `none` | `none` | `not_applicable` | `A5`, `J3` |
| `signal_policy.get` | "What ordering is actually in force right now?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `J3` |
| `trigger.create` | "A standing rule: when this happens, produce this work". | `control` | `reversible` | `auto` | the rules already in force for this observation type and scope — a duplicated standing rule silently doubles everything it produces for as long as nobody notices. | `none` | `required` | `required` | `A5`, `F2`, `J1`, `J2`, `J4` |
| `trigger.update` | "Change the rule, including switching it off without losing it". | `control` | `reversible` | `auto` | the rule's current definition and whether it is enabled. | `none` | `none` | `not_applicable` | `J6` |
| `trigger.list` | "What standing rules are running, what do they watch, and what do they produce?" — in full. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `trigger.delete` | "Remove the rule entirely". | `control` | `irreversible` | `confirm_once` | whether the rule still exists; an already-deleted rule is a success on retry. | `none` | `required` | `required` | `J6` |
| `trigger_run.list` | "What did my rules actually do last night" — which fired, on whom, what work came out, and what was held back by policy. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `J5` |
