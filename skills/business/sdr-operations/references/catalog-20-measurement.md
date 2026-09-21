<!-- Generated. Do not edit by hand. -->

# Family 20 — Measurement

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The organisation around the work* · 10 operations · contract 2.0.0

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
| `engagement.summarize` † | "How is this doing?" — one measure set over one scope, grouped the way the question actually needs. | `read` | `reversible` | `auto` | nothing at stake, but two runs are only the same number if as-of, window, basis, denominator and detection method all match. | `none` | `none` | `required`<br>*conditional:* one row per group, each with its own envelope. | `E5`, `F6`, `M1`, `M2`, `M3`, `M4`, `M5`, `M6`, `M7` |
| `engagement_event.list` | "Show me the rows behind the number" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `A14`, `M2`, `M5` |
| `funnel.summarize` | "The whole ladder, stage by stage, with each stage's declared denominator and how well each stage can be known at all" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `I1`, `M1`, `M3` |
| `opportunity.summarize` | "What came out the far end, and what was it worth — under which attribution model, on whose authority?" | `read` | `reversible` | `auto` | nothing at stake — and these values move retroactively, so a figure is only meaningful with its as-of. | `none` | `none` | `required` | `M1` |
| `attribution.explain` | "Why was this outcome credited here, and what would a different model have said?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | — |
| `response_time.summarize`<br>*questions:* 5 | "How fast do we actually pick things up" — an inbound lead from arrival to first human touch, a reply from arrival to answer — by owner, route, source and hour of day. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `K1`, `K8`, `M1` |
| `metric.describe` | "What does this measure actually mean here, what was it mapped from, what can it be grouped by, and how much can it be trusted?" | `read` | `reversible` | `auto` | nothing at stake; read it before planning anything around a measure | `none` | `none` | `required` | `M1`, `M2` |
| `metric.compare`<br>*questions:* 13 | "Two arms, side by side" — the counts, the sample size this comparison needs, the interval around each rate, and whether there is enough data to say anything at all. | `read` | `reversible` | `auto` | nothing at stake — but re-reading and stopping when it looks good invalidates the result, so the intended sample size, the observed sample size and the number of reads are all returned. | `none` | `none` | `required` | `M4`, `M7` |
| `export.request`<br>*questions:* 24 | "Get me a bounded extract for reporting" — never the route for a subject access request, which is `privacy_request.fulfill`. | `control` | `reversible` | `confirm_once`<br>*departs:* raised because an extract moves personal data out of the system where its handling rules are enforced. | the state of the export already requested under this key | `metered`<br>*conditional:* metered where the extract consumes an allowance.<br>*basis:* per call<br>*meter:* `request` | `required`<br>*conditional:* a replayed export produces a second copy of personal data. | `not_applicable` | `A13` |
| `export.get`<br>*questions:* 24 | "Collect the extract" | `read` | `reversible` | `auto` | nothing at stake; the extract is stable once complete | `none` | `none` | `not_applicable` | — |
