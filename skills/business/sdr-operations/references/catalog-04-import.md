<!-- Generated. Do not edit by hand. -->

# Family 4 — Import

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*People and audience* · 5 operations · contract 2.0.0

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
| `source.inspect` | "Look at this file and tell me what is in it and how it would land" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `B9`, `B10` |
| `import.apply`<br>*questions:* 18, 19 | "Take this file, this mapping and these rules, and put the people in" | `control` | `compensatable` | `confirm_once` | the batch record for this key: its state and its per-row ledger. A re-run without the key is a second import, not a retry. | `none` | `required` | `required` | `A1`, `A9`, `A17`, `B1`, `B2`, `B3`, `B4`, `B6`, `B9`, `B10`, `C4` |
| `import.list` | "What imports have we run?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `M6` |
| `import.get` | "Show me that import in full" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `import.revert`<br>*questions:* 3 | "That import was wrong — undo it" | `control` | `compensatable` | `confirm_once` | the batch's revert state and the per-row revert ledger under this key. | `none` | `required` | `required` | — |
