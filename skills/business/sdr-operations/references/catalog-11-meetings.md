<!-- Generated. Do not edit by hand. -->

# Family 11 — Meetings, qualification and handoff

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The conversation, the queue and the day* · 17 operations · contract 2.0.0

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
| `meeting.propose`<br>*questions:* 28 | "Offer them a time" — concrete slots, or a way to pick their own; also how we counter when their slots do not work | `act` | `compensatable` | `confirm_each`<br>*departs:* raised: it puts words and specific times in front of a named person. | outstanding proposals to this person and the slots already offered — a repeat double-books the calendar and mails them twice. | `metered`<br>*conditional:* metered against the message allowance where the channel meters messages; `none` otherwise.<br>*basis:* per message, where the channel meters messages<br>*meter:* `message` | `required` | `not_applicable` | — |
| `meeting.book` | "Book it" — time, attendees, how to join, and whose calendar and whose meeting it is | `act`<br>*conditional:* `act` when the booking issues the invitation; `control` when it records a meeting invited elsewhere — resolved by the preview. | `compensatable` | `confirm_each`<br>*conditional:* confirm_each when it acts, auto when the preview resolves it to `control`.<br>*artefact:* preview<br>*departs:* confirm_each when the booking issues the invitation, because each invitation reaches one person's calendar and is its own decision; auto when the preview resolves it to recording a meeting invited elsewhere. Act × compensatable derives confirm_once, so the acting side sits deliberately above the table. | read first — existing meetings for this person and owner in the window; a booking that timed out may already have invited them. | `none` | `required` | `not_applicable` | `I1` |
| `meeting.confirm`<br>*questions:* 28 | "Confirm tomorrow's meetings and chase the ones nobody has answered" — a daily act, not an edge case | `act` | `compensatable` | `confirm_each`<br>*departs:* raised: it reaches named people. | whether a confirmation has already gone for this meeting and whether the attendee has already responded. | `metered`<br>*conditional:* metered against the message allowance where the channel meters messages; `none` otherwise.<br>*basis:* per message, where the channel meters messages<br>*meter:* `message` | `required` | `required` | — |
| `meeting.reschedule`<br>*questions:* 28 | "Move it, keeping it the same meeting with the same history" | `act` | `compensatable` | `confirm_each`<br>*departs:* raised: the attendee is told. | the meeting's current time and each attendee's response. | `none` | `required` | `not_applicable` | — |
| `meeting.cancel`<br>*questions:* 28 | "Call it off" — a booked meeting, or an outstanding proposal whose offered times we are withdrawing | `act` | `compensatable` | `confirm_each`<br>*departs:* raised: the attendee is told. | the meeting's or proposal's state; an already-cancelled one is a success on retry. | `none` | `required` | `not_applicable` | `I5` |
| `meeting_outcome.record` | "What actually happened at the appointed time" — held, no-show, cancelled beforehand, moved beforehand, and who did not turn up | `control` | `reversible` | `auto` | the outcome currently recorded | `none` | `required` | `required` | `H6`, `I1`, `I2` |
| `meeting.list` | "What is on the calendar" — mine or someone else's, coming up or already past, with outcomes | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `I2` |
| `meeting.get` | "One meeting in full" — attendees and what each answered, how to join, the owner, and what it came from | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `I2` |
| `qualification.record` | "Write down the evidence behind 'this is worth someone's time', in the account's own questions" | `control` | `compensatable` | `auto` | the qualification record already attached to this person or meeting, and its version — a correction is a new version and the earlier one stays. | `none` | `required` | `required` | `I4` |
| `handoff.create`<br>*questions:* 4 | "Pass this person on, with everything the receiver needs" — the qualification evidence, the meeting, the thread, and what was agreed as the next step | `control` | `compensatable` | `confirm_once`<br>*departs:* raised: it commits another person's time and starts their clock. | whether a handoff for this person is already open or already accepted. | `none` | `required` | `required` | `B13`, `H12` |
| `handoff.accept` | "I will take this, and I will contact them within this time" | `control` | `compensatable` | `auto` | the handoff's state; acceptance happens once and the commitment clock starts once. | `none` | `required` | `required` | `I1`, `I3` |
| `handoff.reject` | "I am not taking this, and here is exactly why" | `control` | `compensatable` | `auto` | the handoff's state | `none` | `required` | `required` | `I3` |
| `handoff.return` | "I accepted this and I cannot work it — giving it back, with the reason" | `control` | `compensatable` | `auto` | the handoff's state; only an accepted handoff can be returned. | `none` | `required` | `required` | — |
| `handoff.withdraw` | "Pull it back before anyone takes it" — they opted out, the meeting fell through, or it went to the wrong person | `control` | `compensatable` | `auto` | the handoff's state; an already-accepted handoff refuses withdrawal and must be returned by the receiver instead. | `none` | `required` | `required` | `I5` |
| `handoff.list` | "What has been handed to me and not answered, and what have I handed on that nobody has picked up?" — with the clock on each | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `I3` |
| `handoff.get` | "Everything I need to decide whether to accept" — the evidence, the thread, the meeting, the agreed next step, and who is asking | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `I4` |
| `opportunity.create` | "Record the conversion: this became a real deal in the pipeline" | `control` | `compensatable` | `confirm_once`<br>*departs:* raised: it is one of the four facts people are paid on, and an accidental one corrupts attainment for the period. | whether an open opportunity already exists for this account and this motion. | `none` | `required` | `not_applicable` | `I1` |
