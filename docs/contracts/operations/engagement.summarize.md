# `engagement.summarize` — the provider's counts for a campaign

**Version 1.** The machine-readable contract is [`engagement.summarize.json`](engagement.summarize.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"How is this doing?", answered by the provider's own counts for one campaign over one window. Jason records no
open, reply or delivery of its own, so these numbers are the only ones it can have about a campaign's outreach —
and each comes with the envelope that says what it counts.

## The input you receive

```json
{
  "args": { "window": { "preset": "last_month" }, "group_by": "channel" },
  "campaign": {
    "id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
    "name": "Q3 LatAm founders",
    "status": "active",
    "external_ids": { "campaign": "c_7714" }
  }
}
```

- `window` is either a span, `{ "from": …, "to": … }`, or one of the provider's presets: `last_week`, `last_month`,
  `last_year`, `all_time`. Both at once are refused, and so is neither.
- `group_by` is `channel`, the one grouping this version offers. It is required, so a caller states the grouping
  they asked for rather than inheriting one.

The campaign is named exactly as in `campaign.get`.

## What to return

```json
{
  "rows": [
    {
      "group": { "channel": "email" },
      "envelope": {
        "as_of": "2026-10-03T09:00:00.000Z",
        "window": { "preset": "last_month" },
        "basis": "provider_reported",
        "denominator": "unknown",
        "detection": "provider"
      },
      "measures": { "contacted": 120, "delivered": 117, "opened": 64, "replied": 9, "bounced": 3 },
      "vendor": { "overview": "emailOverview" }
    }
  ],
  "vendor": {}
}
```

**One row per group, each with its own envelope.** That is the catalogue's per-item rule for this operation: the
rows are the items, and a row's numbers mean nothing without its envelope.

- A row for each channel the provider reports, and none for a channel it does not.
- `measures` holds counts only. A count the provider does not report is **absent**, never 0: "nobody replied" and
  "the provider does not say" are different facts. A rate is never returned, even where the provider sends one —
  a rate is two counts and a denominator, and the denominator is exactly what the provider does not name.
- `as_of` is the plugin's clock when it asked. The provider does not say when it counted, so this is the closest
  honest moment.
- `denominator` is `unknown` unless the provider names the count its rates divide by.

The measures are the email counts (`contacted`, `delivered`, `opened`, `replied`, `interested`, `bounced`,
`opted_out`, `out_of_office`, `auto_replied`, `meetings_booked`) and the LinkedIn counts (`connections_sent`,
`connections_accepted`, `messages_sent`, `replied`, `inmails_sent`, `inmails_replied`, `profile_views`). A count the
provider reports beyond these is not returned in this version.

## When it fails

Use the codes the document declares. `window_invalid` is a validation failure: the provider refused the window
itself, so asking again with the same window cannot help. `campaign_not_found`, `feature_unavailable` and
`provider_answer_too_large` are permanent. A lost answer may simply be asked again.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from engagement.summarize.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `read` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | nothing at stake, but two runs are only the same number if as-of, window, basis, denominator and detection method all match. | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked` |
| in a paused campaign | `waits` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | E5, F6, M1, M2, M3, M4, M5, M6, M7 |

<!-- END GENERATED properties -->
