# `audience.preview` — count and sample an audience before anything is spent on it

**Version 1.** The machine-readable contract is [`audience.preview.json`](audience.preview.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it, as a read on request.** A person or an agent asks for it for a campaign —
> `jason audience preview` — and the runtime routes it by the campaign's route for `audience.search`, the search it serves, holds the
> route to the claim's checks about the route, the plugin and its binding, performs it in a plugin-host process, and
> measures the answer against the result schema. Nothing is recorded about it, and it is never work: `workitem.create`
> refuses it, `on_request_only`, since a work item's result is kept for good. Without a route it is refused with
> `no_route`.

## What it is for

"How big is this audience, and who is in it?" An audience is written as the provider's own search filters —
industries, job titles, company sizes, places — and before anything is spent on one, a planner asks how many people it
holds and looks at a few of them. It is also how an audience the campaign already searched under is looked at again
before a top-up: by its number, to see how many it still holds.

The look is free by contract. It spends nothing and reveals nobody, so nobody is asked to approve it. A provider that
charges for a look leaves the operation out of its manifest, and one whose API has no look answers the reserved
`capability_not_offered`. The spending act is [`audience.search`](audience.search.md).

## The definition

The definition is the provider's, not Jason's: the filters its search takes, in the shape its plugin declares under
`audience_definition` in its manifest ([`docs/plugins.md`](../../plugins.md), §3). This document holds it to being an
object when the request is written. Once the route says which plugin answers, the runtime holds it to that plugin's
declared schema before the plugin is started: a definition off the schema is refused `definition_invalid`, with a
pointer for each place it fails, and the provider is asked nothing. A rule of the provider's that the schema cannot
express is the plugin's to refuse, as `definition_rejected`. `jason operation describe audience.preview` answers both
schemas for a campaign — the operation's arguments and the routed plugin's definition — so an agent writes a
definition the provider takes.

Exactly one of `args.audience` and `args.definition` is given: both at once are refused, and so is neither. A number
names one of the campaign's audiences, and is previewed by the definition that audience was made from; a number the
campaign holds no audience under is refused `audience_unknown`. Either way the runtime composes the definition under
`definition` at the root of the input, and the plugin reads it there and nowhere else. A preview makes no audience.

## The input you receive

```json
{
  "args": {
    "definition": { "industries": [ { "value": "Logistics", "type": "include" } ], "company_sizes": ["51-200"] },
    "people_per_company": 2
  },
  "definition": { "industries": [ { "value": "Logistics", "type": "include" } ], "company_sizes": ["51-200"] },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } }
}
```

`people_per_company` caps how many people come from any one company, as a search of the audience would take them;
absent, it reads as 1.

## What to return

```json
{
  "count": 12400,
  "companies": 3100,
  "sample": [
    {
      "external_id": "p-90211",
      "first_name": "Lena",
      "last_name": "Novak",
      "title": "Head of Operations",
      "company": { "name": "Northwind Freight", "domain": "northwind-freight.example", "size": "51-200", "industry": "Logistics" },
      "location": "Porto, Portugal",
      "raw": "{\"id\":\"p-90211\",\"seniority\":\"head\"}"
    }
  ]
}
```

- `count` and `companies` are the provider's own estimate, null where it gives none, and never a promise of what a
  search will find.
- `sample` holds at most 25 people, in the provider's own order, each in the shape every provider answers in. A sample
  reaches nobody: it carries no channels, and nothing in `raw` is an address, a number or a profile link.
- `raw` is the provider's record for the person as JSON text — `JSON.stringify` of it, trimmed to fit 16 KiB — or null.
  The schemas bound every value a result may hold, and an object of the provider's own keys has no bound they can
  state, so the record travels as text. Jason reads none of it.

## Repeating after a lost answer

A look changes nothing and spends nothing, so `repeat_after_ambiguous` is `safe`. Nothing is pinned.

A provider may answer a large definition as a job of its own that takes minutes; the plugin waits for it inside the
call, which is why this document's timeout is five minutes. A look that does not finish in time
fails `provider_unavailable`, transient. An answer longer than this installation lets a plugin read from a program
(`Plugins:Exec:OutputBytes`) is `provider_answer_too_large`, permanent: asking again would cut it at the same place.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.preview.json. Edit the document; this block follows it. -->

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
| in an archived campaign | `cancelled` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 300000 ms |
| invariants | B10, D2 |

<!-- END GENERATED properties -->
