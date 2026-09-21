<!-- Generated. Do not edit by hand. -->

# Family 8 — Enrollment

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The programme* · 7 operations · contract 2.0.0

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
| `campaign.enroll` † | "Put these fifty people into the Q3 campaign" — with an explicit collision policy, an explicit start position and an explicit first-touch timing: the authored delay, the next open window, or immediately. | `act`<br>*conditional:* bookkeeping while the campaign is not live, `act` the moment it is; resolved by the preview, never assumed. | `irreversible`<br>*conditional:* compensatable until the first touch goes, irreversible after. | `confirm_once`<br>*conditional:* confirm_once before go-live; confirm_once with a mandatory preview once live. One call over a set is one decision, and the preview is the approval artefact — it names the population, it does not count it.<br>*artefact:* preview | the per-item outcome of the prior run under this key, and the campaign's live state — re-running blind into a live campaign sends twice. | `metered`<br>*conditional:* none while the campaign is not live; metered once it is.<br>*basis:* per enrolled person, once the campaign is live<br>*meter:* `message` | `required` | `required` | `A7`, `D1`, `D4`, `E2`, `F2`, `G1`, `G2`, `G7`, `G8` |
| `enrollment.list` † | "Who is in this campaign?" and "where is this person across everything?" — the same read from either end, and with historical participations included on request. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `G1`, `G3`, `G4`, `G8`, `H1`, `K4`, `L5` |
| `enrollment.get` | "Where is this person in this campaign?" — state, position, why it is not progressing, exit reason, business outcome, the sending identity behind it, and when the next touch is due. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required`<br>*conditional:* when a collection of (person, campaign) pairs is given. | `G1`, `G3` |
| `enrollment.pause` †<br>*questions:* 1 | "Hold this person here" — with a reason and an optional dated automatic resume. | `control` | `reversible` | `auto` | the current state per person; only an active participation can be held, so per-item refusals are real answers rather than errors. | `none` | `required` | `required` | `G1` |
| `enrollment.resume`<br>*questions:* 1 | "Let them carry on" — refuses unless the participation is actually held. | `act`<br>*conditional:* `act` when the campaign is live; `control` when it is not. | `irreversible`<br>*conditional:* irreversible when live, reversible when not. | `confirm_once`<br>*conditional:* confirm_once with a mandatory preview naming who resumes while the campaign is live; auto while it is not.<br>*artefact:* preview | the current state per person, and the first-touch ledger since the attempt. | `metered`<br>*conditional:* metered while the campaign is live; none while it is not.<br>*basis:* per resumed person, while the campaign is live<br>*meter:* `message` | `required` | `required` | `G5` |
| `enrollment.stop` † | "Take them out, and record why" — the reason is mandatory and must be able to name a different person as the cause. | `control` | `irreversible` | `confirm_once` | the participation is absent or finished — either reads as a completed stop. | `none` | `required` | `required` | `G1`, `G3`, `G6`, `G8` |
| `disposition.set` | "What came of this participation?" — the business outcome, from the account's declared vocabulary, distinct from any reply's intent and from the execution state. | `control` | `reversible` | `auto` | the current disposition; last-write-wins, and a regression may be legitimately refused, which is a per-item answer. | `none` | `required`<br>*conditional:* when a collection is given. | `required` | `G3`, `H9` |
