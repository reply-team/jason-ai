# `audience.filter_values` — the values one of the provider's filters may take, matching some text

**Version 1.** The machine-readable contract is [`audience.filter_values.json`](audience.filter_values.json); this
page explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it, as a read on request.** A person or an agent asks for it for a campaign —
> `jason audience filter-values` — and the runtime routes it by the campaign's route for the operation, holds the
> route to the claim's checks about the route, the plugin and its binding, performs it in a plugin-host process, and
> measures the answer against the result schema. Nothing is recorded about it. A work item may name it too, and is
> then measured against the argument schema when it is written, routed at the claim and answered the same way. Without
> a route it is refused with `no_route`.

## What it is for

"What values may this filter take, starting with this text?" An audience's definition is written in the provider's
own words — its industries, its job functions, its places — and a value the provider does not know is a search it
refuses. This is the typeahead: the first values the provider offers for one of its filters, matching what a person or
an agent has typed so far, each written exactly as a definition takes it.

The filters it answers for are the plugin's own. Its manifest's `audience_definition` names, under `keys`, the
typeahead each property of the definition pairs with ([`docs/plugins.md`](../../plugins.md), §3), and
`jason operation describe audience.search` answers them for a campaign. A name the plugin does not pair with a
typeahead, or one the provider does not know, fails `filter_unknown`. The read is optional per provider: one whose API
offers no values answers the reserved `capability_not_offered`, or leaves the operation out of its manifest.

## The input you receive

```json
{
  "args": { "filter": "industries", "query": "logis", "limit": 20 },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } }
}
```

- `filter` is a typeahead's name, as the plugin's declaration writes it.
- `query` is the text so far, at most 200 characters; empty asks for the first values the provider offers.
- `limit` is at most 100; fewer may come back.

## What to return

```json
{ "values": ["Logistics", "Logistics and Supply Chain"] }
```

The first `limit` values that match, in the provider's own order, none twice. It is not a page: there is no next one,
and a narrower `query` finds the rest.

## Repeating after a lost answer

A read changes nothing, so `repeat_after_ambiguous` is `safe`. Nothing is pinned.

An answer longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`) is
`provider_answer_too_large`, permanent: asking again would cut it at the same place.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.filter_values.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `read` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | nothing at stake | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked`, provider contact `not_used` |
| in a paused campaign | `waits` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 30000 ms |
| invariants | A6, E12, H2 |

<!-- END GENERATED properties -->
