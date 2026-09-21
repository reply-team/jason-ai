<!-- Generated. Do not edit by hand. -->

# Family 3 — Audience and lists

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*People and audience* · 16 operations · contract 2.0.0

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
| `segment.define` | "Save this definition of who we are going after" | `control` | `reversible` | `auto` | the current definition under this name and its version. | `none` | `none` | `not_applicable` | `D1`, `D2` |
| `segment.get` | "Show me what this segment actually says" — without evaluating it. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `C2` |
| `segment.list` | "What audience definitions do we have?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `segment.delete` | "Retire this definition" | `control` | `irreversible`<br>*conditional:* compensatable inside the restore window; lists already materialised are untouched. | `confirm_once` | whether it still exists and what still references it | `none` | `required` | `not_applicable` | — |
| `segment.preview` | "Before I spend anything: will this definition actually run, and how big is it?" | `read` | `reversible` | `auto` | the recorded preview under this key, with the time it was taken. | `metered`<br>*conditional:* metered per call only where the universe is external; none over our own contacts.<br>*basis:* per call, only where the universe is external<br>*meter:* `discovery` | `required` | `not_applicable` | `B10`, `D2` |
| `segment.materialize` | "Freeze this audience as it is right now and give it a name" | `control` | `compensatable` | `confirm_once` | whether a list already exists for this definition, version and as-of time, and the per-item ledger under this key. | `none` | `required` | `required` | `C2`, `D1` |
| `list.create` | "Make a named list" | `control` | `compensatable` | `auto` | whether a list of this name already exists | `none` | `required` | `not_applicable` | — |
| `list.list` | "What lists do we have?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `list.get` | "What is on this list, and where did it come from?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `D1` |
| `list_membership.add` | "Put these people on this list" | `control` | `reversible` | `auto` | current membership for these people, and the per-item ledger under this key. | `none` | `required` | `required` | `D3` |
| `list_membership.remove` | "Take these people off this list" | `control` | `reversible` | `auto` | current membership for these people, and the per-item ledger under this key. | `none` | `required` | `required` | `D3` |
| `list.delete` | "We are done with this list" | `control` | `irreversible`<br>*conditional:* compensatable inside the restore window. | `confirm_once` | whether it still exists and what still references it | `none` | `required` | `not_applicable` | — |
| `audience.assess` | "Is this set of people actually fit to work?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `B11`, `D5`, `D6` |
| `audience.screen` | "Re-check this list against everything we must not contact, and take them out" | `control` | `compensatable` | `auto` | current membership, and the per-item removal ledger under this key. | `none` | `required` | `required` | `B11`, `D4` |
| `contactability_policy.define`<br>*questions:* 17 | "Decide who we are willing to send to, before anyone asks" | `control` | `reversible` | `confirm_once`<br>*departs:* raised above the derived `auto`: it answers, per verdict and per flag, one of send, hold or never_send for every address the account will ever offer, and nothing in it has a default. | the policy in force, with its version and who set it | `none` | `none` | `not_applicable` | `B8`, `D5` |
| `contactability_policy.get` | "What are we currently willing to send to?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
