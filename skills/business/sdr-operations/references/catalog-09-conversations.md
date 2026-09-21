<!-- Generated. Do not edit by hand. -->

# Family 9 — Conversations and activity

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The conversation, the queue and the day* · 9 operations · contract 2.0.0

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
| `conversation.list` † | "What came back overnight, and what needs answering first?" — filterable by meaning, channel, owner, campaign and how long each has been waiting, ordered by what is most overdue | `read` | `reversible` | `auto` | nothing at stake; page with the returned cursor | `none` | `none` | `not_applicable` | `H10` |
| `conversation.get` † | "Read me the thread before I answer" — every message in order, who is on it, what we have decided it means, and what it is attached to | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `H3`, `H5` |
| `conversation.classify` † | "What does this reply actually mean, in a value I can plan a branch on — and who decided it, from which message, and how sure were they?" | `control` | `reversible` | `auto` | the meaning currently on the thread, with who assigned it and when. | `none` | `required` | `required` | `A2`, `H1`, `H2`, `H3`, `H4` |
| `conversation.assign`<br>*questions:* 4 | "Give this thread to someone else's inbox" — internal routing only; nothing is promised and no clock starts | `control` | `reversible` | `auto` | the thread's current owner | `none` | `required` | `required` | `H11`, `H13` |
| `conversation.snooze` | "Not now — bring it back on this date" | `control` | `reversible` | `auto` | the thread's snooze-until time | `none` | `required` | `required` | — |
| `conversation.close` | "Done with this thread" — worked, not ignored | `control` | `reversible` | `auto` | the thread's state | `none` | `required` | `required` | `H9` |
| `conversation.reopen` | "I closed that too early — put it back in the queue" | `control` | `reversible` | `auto` | the thread's state; reopening an open thread is a no-op. | `none` | `required` | `required` | — |
| `referral.record`<br>*questions:* 6 | "I'm not the right person — talk to Steve in finance" — record who they named, where the name came from, and take this person out of the campaign as referred out | `control` | `compensatable` | `confirm_once`<br>*departs:* raised: it creates a person we will contact on the strength of a stranger's sentence. | whether a referral from this message is already recorded, and whether the named person already exists. | `none` | `required` | `required` | `A16` |
| `activity.log` | "Record something that happened outside the tool so the record is not a lie" — an email from a personal client, a hallway conversation, a message on a channel we do not send from, a note | `control` | `compensatable` | `auto` | whether an activity with this key, or this subject and time, is already recorded. | `none` | `required` | `required` | `H6` |
