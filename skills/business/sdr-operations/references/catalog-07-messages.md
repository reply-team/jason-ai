<!-- Generated. Do not edit by hand. -->

# Family 7 — Messages

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The programme* · 5 operations · contract 2.0.0

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
| `message.draft` † | "Show me exactly what this person would receive" — the real bytes: merge fields resolved against this contact, fallbacks applied or reported missing, footer, identification block and unsubscribe mechanism in place. Persists nothing sendable and queues nothing | `read` | `reversible` | `auto` | nothing at stake — but where drafting is metered, recover a lost draft rather than re-drafting it. | `metered`<br>*conditional:* metered against the content-generation allowance where the draft is generated rather than merged; `none` for a pure render.<br>*basis:* per call, where the draft is generated rather than merged<br>*meter:* `content_generation` | `required`<br>*conditional:* required where drafting is metered. | `required`<br>*conditional:* when a collection of recipients is given. | `E14` |
| `message.send` † | "Send this message to this person" — one person, one channel per call, with that channel's own required elements and cost | `act` | `irreversible` | `confirm_each` | read first: no send recorded for this person on this thread or step in the window — a timed-out send may already have succeeded. | `metered`<br>*conditional:* metered against the message allowance on channels that meter sending; `none` on channels that do not.<br>*basis:* per message, on channels that meter sending<br>*meter:* `message` | `required` | `not_applicable` | `A5`, `A7`, `A14`, `A21`, `A22`, `H4`, `H5` |
| `message.schedule` | "Send this on the 14th" — the Q3 they asked for, the day they come back from leave, the morning after the event | `act` | `irreversible`<br>*conditional:* compensatable while pending; irreversible the moment it sends. | `confirm_each` | the pending scheduled sends for this person on this thread | `metered`<br>*basis:* the message allowance at the time it fires, where the channel meters sending<br>*meter:* `message` | `required` | `required`<br>*conditional:* when a collection is given. | `A7`, `H4` |
| `scheduled_message.list` | "What is queued to go out, to whom, and when?" — by person, campaign, sender, or everything | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `K4` |
| `scheduled_message.cancel` | "Do not send that after all" | `control` | `compensatable` | `auto` | the pending list — an already-cancelled item is a success, not a failure. | `none` | `required` | `required` | `I5`, `L6` |
