<!-- Generated. Do not edit by hand. -->

# Family 19 — Accounts and buying groups

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The organisation around the work* · 12 operations · contract 2.0.0

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
| `account.get` | "Show me this company as something we own" — its state, its tier, who owns it, what is happening on it. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `account.search` | "Which of the companies we already hold match this?" — never queries an outside database and never costs anything. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `account.update` | "Change the company's state — in play, enrolled, nurture, blocked, customer, partner — or its tier, with a reason." | `control` | `reversible` | `auto` | the current state of each account | `none` | `required` | `required` | — |
| `account.claim` | "Reserve this company for my attention for a stated period" — a lease, not ownership, and it expires by itself. | `control` | `reversible` | `auto` | existing claims on the account and their expiry | `none` | `required`<br>*conditional:* a replayed claim must not silently extend the lease. | `required` | — |
| `account.release` | "Give the claim back before it expires." | `control` | `reversible` | `auto` | whether the claim is still live and still mine | `none` | `required` | `required` | — |
| `account_membership.create` | "Attach this person to this company deliberately, rather than letting a new company appear for every spelling of a name." | `control` | `reversible` | `auto` | the membership rows already on the account | `none` | `required` | `required` | — |
| `account_membership.list`<br>*questions:* 10 | "Who do we hold at this company, how do we know they belong to it, and which of them is being touched right now?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required`<br>*conditional:* every row states its basis. | `I5` |
| `account_membership.delete` | "Detach a person from this company." | `control` | `reversible` | `auto` | the membership rows on the account | `none` | `required` | `required` | — |
| `collision.check`<br>*questions:* 6 | "Who else is touching this company, and why?" — my other campaign, another person's campaign, or a live customer, support or opportunity motion. | `read` | `reversible` | `auto` | nothing at stake; re-check immediately before enrolling, because collisions appear between planning and execution. | `none` | `none` | `required` | `G7` |
| `buying_group.define` | "Name the people who decide together at this company, and what each of them does in the decision" — create-or-replace over the whole group; defining it with no members removes it. | `control` | `reversible` | `auto` | the group already defined on this account | `none` | `required` | `required` | — |
| `buying_group.get` | "Show me the group: who is in it, in what role, and where each of them stands." | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `M7` |
| `buying_group.enroll` | "Engage the group deliberately" — how many at once, staggered by how many working days, in what order, and what one person's reply does to the others. | `act`<br>*conditional:* bookkeeping while the campaign is not live, `act` the moment it is, with reach equal to the members it will contact; resolved by the same preview as `campaign.enroll`. | `irreversible`<br>*conditional:* compensatable until the first touch goes, irreversible after. | `confirm_once`<br>*conditional:* confirm_once before go-live; confirm_once with a mandatory preview once live.<br>*artefact:* preview | the per-member outcome from the previous run — enrolled, staggered to a future date, skipped, refused — and the group's current state. | `none`<br>*conditional:* `none` in itself; the sends it causes carry the channel's own. | `required` | `required`<br>*conditional:* one outcome per member with the reason. | `D4`, `E7`, `G2`, `G6` |
