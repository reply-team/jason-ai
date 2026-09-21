---
name: performance-analysis
description: >
  An honest read on outreach performance: account and per-campaign figures over a stated
  window, a diagnosis that separates deliverability from copy from targeting, and one or two
  high-impact recommendations. Use when the user asks how campaigns are performing, what is
  underperforming, what to fix or pause, or wants outreach analytics.
metadata:
  version: 3.0.0
  pack: ai-sdr-core
  category: strategy
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [analytics, diagnosis, optimization, evidence]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations]
    recommends: [sending-guardrails, campaign-launch, inbox-triage, approval-boundaries]
---

# Performance analysis

> **Partly an expert skeleton.** The diagnostic method is complete. The benchmark numbers
> below are explicitly heuristics carried over from the prototype and are marked
> `TODO(expert)` — they need validation against real outbound data, by the experts who own this
> skill, before it leaves `draft`. Treat them as rules of thumb, never as law. They are also this
> skill's own bands and nothing more: the contract returns no benchmark, industry average or
> portable score at all (`M7`), so a band here can never be confused with a figure something
> handed back.

## Purpose

Turn raw figures into a diagnosis and one or two concrete actions. Read-only by default:
nothing changes until the user approves a change. The value is not the numbers — the user can
see those — it is correctly attributing a problem to deliverability, copy or targeting,
because each has a different fix.

A number is not evidence until it carries its measurement envelope — when it was computed, the
window and timezone, window-or-cohort basis, the denominator's name **and its count**, and how
the fact was detected (`M1`). Half of the work below is refusing to diagnose on a figure that
arrives without one.

## When to use / when NOT to use

- Use for performance questions, optimisation, and pause-or-fix decisions.
- Do NOT use to launch or change campaigns — that is `campaign-launch`.
- Do NOT use to work replies — that is `inbox-triage`.

## Prerequisites

- Enough volume to say anything. Below roughly a hundred delivered messages, say the sample
  is too small and stop there rather than dressing noise as insight. The read itself will say
  so too: a rate carries its denominator count and a typed insufficient-volume flag with the
  threshold used (`M4`), and that flag is the answer, not a caveat to write around.
- Operations used, all `read`, all `auto`, and none of them writes anything (`M8`):
  `engagement.summarize`, `funnel.summarize`, `metric.describe`, `metric.compare`,
  `response_time.summarize`, `opportunity.summarize`, `engagement_event.list`, `sender.health`,
  `sender.summarize`. Measurement is commonly rate-limited harder than everything else —
  sequential, cached, never polled.

**There is one measurement read, not one per question.** The `metrics.*` family is retired.
There is no account-overview operation, no per-campaign operation and no channel-efficiency
operation to look for: it is `engagement.summarize`, and **the grouping is an argument** —
`engagement.summarize(scope, window, group_by=…)`. Anyone hunting for a per-campaign metrics
name will not find one, and inventing one is how a name that exists in a single playbook gets
born. The four purpose-built reads beside it are genuinely different questions, not groupings:
`funnel.summarize` for the whole ladder stage by stage with each stage's declared denominator,
`opportunity.summarize` for what came out of the far end and under which attribution model,
`response_time.summarize` for how fast we actually pick things up, and `metric.compare` for two
arms side by side.

**Which groupings a measure actually carries is a read, not an assumption.** The contract does
not publish a fixed list of group-by keys; `metric.describe` returns them per measure, along
with what the measure was mapped from and how far it can be trusted. Ask it before planning an
analysis around a grouping — including channel.

## Planning guidance

**Anchor the window first.** Every figure is meaningless without the period it covers; agree
last week, last month or a specific range with the user before pulling anything.

**Define the measure before reading it.** `metric.describe` first, because "reply rate" has no
shared definition and the commonest way an investigation ends is discovering that the two
figures being compared are two different measures. Cohort versus window is arithmetic, not
preference (`M3`): a cohort three days into a fourteen-day cadence has had two of seven touches
and is not comparable to a mature one.

**Compare campaigns to each other**, not only to published benchmarks. The user's own
best-performing campaign is a far better yardstick than an industry average, because it shares
their audience, domain and voice — and because a benchmark is one of the things the contract
deliberately declines to hand back (`M7`), so the comparison has to be built from the user's own
arms whether or not anyone wanted it that way.

**The three causes are three groupings of one read.** Deliverability is the sender axis
(`sender.health`, `sender.summarize`). Targeting is `engagement.summarize` grouped by
provenance, verification verdict, enrichment source or import batch — those are group-by keys
precisely so this axis is diagnosable rather than blamed on the copy (`M6`). Copy is the same
read grouped by step. Reaching for a different operation per cause is the wrong instinct.

**Order is not preference.** Each check can invalidate the question the next one would answer,
so running them out of order produces a confident wrong answer. `sdr-operations` carries the
full worked ordering as the chain *Diagnosing a drop in reply rate*; read it when the question
is a drop rather than a routine read.

## Execution guidance

1. **Define the measure** (`metric.describe`) — what it means here, what it was mapped from,
   what it can be grouped by, how far to trust it.
2. **Account overview** (`engagement.summarize(scope, window)`) for the agreed window. Read the
   did-not-happen counts first — deferred, cap-skipped, blackout-skipped, refused, throttled are
   first-class facts by reason (`M5`). Fewer replies is often fewer sends, and the rate may not
   have moved at all.
3. **Per campaign** — the same operation with a grouping
   (`engagement.summarize(group_by=campaign)`), then look closer at the outliers, best and
   worst. Per-channel and per-step are the same call again with a different key.
4. **Check the denominator and the volume flag before diagnosing anything** (`M4`). This step
   ends more investigations than any other, and ending one honestly is a result.
5. **Diagnose.** Heuristic bands for cold outbound — TODO(expert): validate every number
   here: delivered ≥ ~95%, opened ~40–60%, replied ~2–8%, interested ~10–30% of replies.
   - **Deliverability problem** — bounces above ~3%, or delivered below ~95%. The cause is
     list quality or sender health, not the message. Check `sender.health` and
     `sender.summarize` — each failure class, complaint rate and deferral carries its own
     publisher, denominator and window, and no composite "health score" is manufactured from
     them (`F6`) — then go to `sending-guardrails`.
   - **Copy problem** — delivery and opens healthy, replies below ~1%. The plumbing works and
     the message is not earning a response. Confirm it with
     `engagement.summarize(group_by=step)` — which step lost the replies — before believing it.
   - **Targeting problem** — replies exist but almost nobody is interested. The message is
     reaching people it should not reach: an audience mismatch, so go back to
     `audience-building`. Prove it rather than asserting it, with
     `engagement.summarize(group_by=provenance)`, then by verification verdict, enrichment
     source or import batch (`M6`). One call usually settles it: replies from the June import
     at 3.4% against a purchased file at 0.9% is the whole answer.
   - **Caveat on opens.** Open tracking is distorted by privacy proxies that fetch images
     automatically or block them entirely, so open rate is the weakest signal in the set.
     Never diagnose on opens alone; prefer delivered and replied. `metric.describe` is the
     operation that carries this — what the open measure was mapped from, its detection method
     and its trust — and it exists so the discount is read rather than remembered. Note what is
     not on offer at all: there is no inbox-placement rate and no "did they read it" measure,
     because neither is measurable (`M2`), and a placement test is typed as simulated placement,
     never as an inbox rate.
     - TODO(expert): how much to discount open rate, and whether to report it at all.
   - Correlation is not causation. When the data is thin, say so — an honest "we cannot tell
     yet" is worth more than a confident wrong answer.
6. **Widen only if the question asks for it.** `funnel.summarize` when the question is where
   the ladder leaks rather than how one campaign is doing; `opportunity.summarize` when it is
   what the outreach was actually worth, with `attribution.explain` for why an outcome was
   credited where it was; `response_time.summarize` when the loss is speed of follow-up, which
   no copy fix reaches. Where the user wants the rows behind a number, that is
   `engagement_event.list`.
7. **Comparing two arms is `metric.compare`.** It returns the counts, the interval around each
   rate, the sample size the comparison needs, an underpowered flag and the number of reads
   taken — and it never names a winner (`M7`). Report it that way. Re-reading until a difference
   appears is exactly the failure an agent on an hourly schedule will produce.
8. **Recommend one or two actions**, highest impact first: pause the worst performer
   (reversible, cheap), rewrite an opener modelled on a campaign that is working, clean the
   bad addresses out of a list, spread volume across another sender.
9. **Only on explicit approval, act** — with one deliberate exception. Stopping is protective:
   `campaign.pause` and `sender.pause` derive `auto` and never wait for permission, because
   waiting is the destructive choice when something is actively going wrong. Restarting is not
   symmetric: `campaign.resume` and `sender.resume` are `confirm_once`, and that asymmetry is
   deliberate rather than an oversight. Everything else — starting, removing, changing content —
   inherits the gates in `campaign-launch`.

**What no operation covers.** Nothing in the contract records a diagnosis or a recommendation;
those are this skill's output and they live in the report, kept by whichever runtime the user
has. And no operation returns a benchmark, an industry average, a portable score or a named
winning variant — that is a refusal on the record (`M7`), not an absence to be worked around by
quoting one from elsewhere as though it came from the data.

## Validation

Every recommendation quotes the actual figures **with their envelope** — window, basis,
denominator count, as-of (`M1`) — and a rate offered without its denominator count is not a
finding. Where a comparison spans two readings, confirm the detection method did not change
inside the window; a change of method is a mandatory caveat, not a footnote. After any approved
change, re-read the affected campaign's state (`campaign.get`) to confirm the change took
effect. If findings confirm or contradict guidance in another skill, that is evidence worth
recording — see the reporting conventions of whichever runtime the user has.

## Reporting

A compact table: campaign / delivered / opened % / replied % / interested, each rate with its
denominator count. One or two sentences of diagnosis per problem campaign, naming which of the
three causes it is and why. Actions taken with identifiers. A single highest-impact next step.
Always the window and the basis.

Say *not measurable* where that is the honest answer (`M2`) rather than substituting the
nearest available proxy. Where the user wants the underlying rows, `engagement_event.list`;
where they want a file, `export.request` is `confirm_once` and raised on purpose, because an
extract moves personal data out of the system that enforces its handling rules.

## Failure modes

- **Over-reading a small sample.** The commonest way analysis does harm: a recommendation
  built on twelve sends sends the user off in the wrong direction.
- **Diagnosing on open rate**, which is the least trustworthy figure available.
- **Reporting numbers without a window**, which makes them unusable and uncomparable.
- **Quoting a rate without its denominator count** (`M4`), which turns a direction into a
  claim.
- **Hunting for an operation per question.** The retired `metrics.*` family had one name per
  grouping; this contract has one read and a grouping argument. Looking for the missing name
  and inventing it is worse than asking `metric.describe` what the grouping keys are.
- **Reading a drop in reply count as a drop in reply rate** without checking the
  did-not-happen counts first (`M5`).
- **Naming a winning variant** because one arm is ahead on the screen (`M7`).
- **Parallelising measurement reads** and hitting rate limits, then retrying harder.
- **Recommending five things.** Two get done; five get ignored.
- **Treating a benchmark as a target.** These bands are rules of thumb pending validation, and
  a campaign below one is not automatically broken.

## Safety

Read-only until the user approves a change. Pausing is the protective direction and the
recommended first move when something is actively going wrong: it derives `auto` and does not
wait, while resuming is `confirm_once`. Never bulk-delete contacts as an "optimisation" — that
destroys data to improve a percentage. An export moves personal data out of the system and is
gated for that reason, not for its cost. Full rules: `approval-boundaries`.

## Related skills

- `sdr-operations` — the measurement family, the group-by discipline, and the worked chain for
  diagnosing a drop in reply rate.
- `approval-boundaries` — why stopping is `auto` and restarting is not.
- `sending-guardrails` — where a deliverability diagnosis goes next.
- `audience-building` — where a targeting diagnosis goes next.
- `campaign-launch` — applying fixes that involve new or changed campaigns.

## Changelog

- 3.0.0 (2026-08-10): migrated to contract 2.0.0. The retired `metrics.*` family is replaced by
  `engagement.summarize` **with the grouping as an argument** — the account-overview,
  per-campaign and channel-efficiency reads were three names and are now one call with three
  groupings, said explicitly so the missing names are not reinvented. Names
  `funnel.summarize`, `metric.compare`, `metric.describe`, `response_time.summarize`,
  `opportunity.summarize`, `engagement_event.list` and `sender.summarize`; `metric.describe` now
  carries the open-rate discount, which was previously prose with nowhere to live. Corrects the
  approval model: pausing derives `auto` under the protective floor and is no longer described
  as confirmed, while resuming is `confirm_once`. Adds the measurement envelope, the
  denominator-count rule, the did-not-happen counts and the refusal to name a winner as cited
  invariants rather than restated advice, and states what the contract does not cover. Every
  benchmark band and all three expert markers are unchanged.
- 2.0.0 (2026-07-30): renamed from `analyze-performance` and rewritten vendor-neutral for the
  `ai-sdr-core` pack — Reply reporting endpoints and scopes moved to
  `reply-operations-mapping`. Diagnostic method and heuristic bands preserved; open-rate
  caveat added; every benchmark explicitly marked for expert validation.
- 1.0.0 (2026-07-27): ported from the prototype workflow as `analyze-performance`.
