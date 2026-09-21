<!-- Generated. Do not edit by hand. -->

# Family 10 — Task queue and touches

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The conversation, the queue and the day* · 12 operations · contract 2.0.0

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
| `task.list` †<br>*questions:* 21 | "What do I owe today, in the order I should do it?" — everything due, including work a signal put there rather than a calendar | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `H10`, `J4` |
| `task.get` | "Show me this one properly, including what it is actually asking me to send" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `task.create` | "Put work in someone's queue" — a call to make, a message to write, a person to research, a meeting to confirm | `control` | `reversible` | `auto` | whether a task with this key already exists for this person and this reason. | `none` | `required` | `required` | `B13` |
| `task.complete` † | "I did this one" | `control` | `compensatable` | `auto` | the task's state; an already-finished task is a success on retry. | `none` | `required` | `required` | `H6`, `H7` |
| `task.skip` | "I am not doing this one, and that is different from having done it" | `control` | `compensatable` | `auto` | the task's state | `none` | `required` | `required` | `H7` |
| `task.reschedule` | "The day overran — push these to tomorrow" | `control` | `reversible` | `auto` | each task's current due time | `none` | `required` | `required` | `E7` |
| `task.cancel` | "This work should not happen at all — drop it" | `control` | `compensatable` | `auto` | the task's state | `none` | `required` | `required` | `I5` |
| `task.reassign` | "Give this work to someone else" | `control` | `reversible` | `auto` | each task's current assignee | `none` | `required` | `required` | `B13`, `H11`, `H12`, `H13` |
| `call.place`<br>*questions:* 20 | "Dial this person now" | `act` | `irreversible` | `confirm_each` | read first — whether a call attempt to this person from this identity is already recorded in the window; a dial that timed out may already have connected. | `metered`<br>*basis:* per attempt and per connected minute<br>*meter:* `call` | `required` | `not_applicable` | `H8` |
| `call.log`<br>*questions:* 20 | "Record the call that happened: what came of it, how long we talked, and whether it was recorded with everyone's agreement" | `control` | `compensatable` | `auto` | whether a call with this key, or this person and start time, is already recorded. | `none` | `required` | `required` | `H6`, `H7` |
| `call_recording.get` | "Play me that call back, and show me the agreement that let us record it" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `H8` |
| `call_recording.discard` | "They did not agree to be recorded, or we should never have recorded it — destroy it and say why" | `control` | `irreversible` | `confirm_once` | whether a recording still exists for this call | `none` | `required` | `required` | `H8` |
