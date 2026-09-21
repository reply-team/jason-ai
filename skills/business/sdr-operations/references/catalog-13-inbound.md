<!-- Generated. Do not edit by hand. -->

# Family 13 — Inbound

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The conversation, the queue and the day* · 11 operations · contract 2.0.0

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
| `inbound_lead.record` † | "Somebody raised their hand — record it now, and tell me who they are". | `control` | `irreversible`<br>*conditional:* an arrival happened and cannot un-happen, and its time is evidence for the response clock and often for consent; where it created a contact, that part is compensatable by `contact.delete`. | `auto`<br>*protective floor:* the protective floor — recording an event that already occurred outside our control is not a decision, and delaying it burns the only thing this motion is measured on. | whether an arrival with this key, or this source and source reference, is already recorded — submission notifications retry, and a doubly-recorded arrival routes twice and restarts a clock that was already running. | `none` | `required` | `required` | `A3`, `A9`, `A17`, `B1`, `K1`, `K2`, `K4`, `K5`, `K7` |
| `inbound_lead.list` † | "What has come in and not been worked, oldest first" — with how long each has been waiting and how long is left. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `H10`, `K1`, `K8` |
| `inbound_lead.get` | "Everything about this one" — what they told us, who they turned out to be, what we already have running against them, who owns it and what the clock says. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `inbound_lead.route`<br>*questions:* 2 | "Send it to the right owner by the rule" — territory, who already owns the account, round-robin, whoever is actually available. | `control` | `reversible` | `auto` | the current owner, and whether routing has already run — re-routing a lead someone is working moves it out from under them. | `none` | `required` | `required` | `H11`, `K3` |
| `inbound_lead.claim`<br>*questions:* 2 | "Mine — taking it off the shared queue". | `control` | `reversible` | `auto` | the current owner; a lead already owned by somebody else refuses rather than overwriting. | `none` | `required` | `required` | `K3` |
| `inbound_lead.release` | "I cannot work this — put it back". | `control` | `reversible` | `auto` | the current owner | `none` | `required` | `required` | `K3` |
| `inbound_lead.triage` | "What kind of request is this actually?" — a sales enquiry, a support problem, a billing question, a job application, someone selling to us, a student, an existing customer wanting more, or noise. | `control` | `reversible` | `auto` | the disposition already recorded on this arrival. | `none` | `required` | `required` | `A22`, `K6` |
| `inbound_lead.close` | "Done with this arrival, and here is what became of it" — a meeting, a handoff, answered and nothing more, routed elsewhere, unreachable, or nothing. | `control` | `reversible` | `auto` | the arrival's state and recorded outcome. | `none` | `required` | `required` | `K5` |
| `inbound_lead.reopen` | "That was closed too early". | `control` | `reversible` | `auto` | the arrival's state | `none` | `required` | `required` | `K5` |
| `response_policy.define`<br>*questions:* 5 | "State how fast a human has to respond, by source and by what kind of request it is — and what happens when the target is missed". | `control` | `reversible` | `auto` | the policy in force for this subject and source; written as a whole policy, so replaying it is harmless. | `none` | `none` | `not_applicable` | `A4`, `A20`, `E7`, `K8`, `K9` |
| `response_policy.get` | "What are we actually holding ourselves to?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
