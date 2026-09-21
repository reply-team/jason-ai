<!-- Generated. Do not edit by hand. -->

# Family 18 — The team: people, ownership, workload and targets

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
| `actor.list` | "Who is on our side of the table, and what state are they in: active, ramping, unavailable, gone?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `actor.get` | "One person: their state, their team, and the ceilings that apply to them" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `team.list` | "What groups do people belong to?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `workload.get` | "What is this person actually carrying" — open tasks, what is due today, live enrolments, and everything wired to them that would break if they left tomorrow. | `read` | `reversible` | `auto` | nothing at stake; read it before handing anyone more work, before an unattended run that names them, and before transferring a book. | `none` | `none` | `required` | `H11`, `H12` |
| `workload_policy.define` | "State how much one person may be holding at once" — open tasks, live enrolments, touches assigned per day — and whether a breach defers the work or refuses it. | `control` | `reversible` | `auto` | the policy in force for this scope | `none` | `none`<br>*exempt:* `absolute_valued` | `required` | `H11` |
| `workload_policy.get` | "What ceiling applies to this person, and how much headroom is left?" | `read` | `reversible` | `auto` | nothing at stake; where none is declared the answer is "none declared", which means unlimited and says so. | `none` | `none` | `not_applicable` | `K3` |
| `ownership.get` | "Who owns this person or this company — and would acting on it make me the owner?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `B13` |
| `ownership.assign` | "Give this contact or account to someone" — a named person, or whoever an assignment policy picks. | `control` | `reversible` | `auto` | the current owner of each subject | `none` | `required` | `required` | `B13`, `H11`, `H12`, `H13` |
| `assignment_policy.define` | "The rule that picks who gets the next piece of work" — a pool, a selection method and an availability test. | `control` | `reversible` | `auto` | the policy in force | `none` | `none` | `not_applicable` | `K3`, `K8` |
| `assignment_policy.get` | "What routing rule is in force here, and who would it pick right now?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `book.transfer`<br>*questions:* 15 | "Move someone's whole book of work to someone else, or split it across a policy" — ownership, the senders behind live threads, open tasks, open conversations, authored content. | `act` | `compensatable` | `confirm_once`<br>*artefact:* preview | the per-item transfer ledger from the previous run — a partially applied transfer repeated wholesale re-points threads that were already re-pointed. | `none` | `required` | `required` | `F8`, `F9`, `H11`, `H13` |
| `goal.define` | "Set the target" — the number, the unit this account actually pays on, the period, and the ramp for anyone not yet at full load. | `control` | `reversible` | `auto` | the goal in force for this subject and period | `none` | `none`<br>*exempt:* `absolute_valued` | `required` | — |
| `goal.get` | "What is this person's or team's target for this period, and in what unit?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `goal.list` | "What targets exist across the team this period?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `pace.get` | "Am I on track?" — target, achieved, in flight, working days gone and left, the run rate needed from here, and what makes the projection unreliable. | `read` | `reversible` | `auto` | nothing at stake; two pace numbers are only comparable if the unit, the period and the working-day calendar match. | `none` | `none` | `required` | `E7`, `M7` |
