# `audience.availability` — whether this account can search now, and what it has left

**Version 1.** The machine-readable contract is [`audience.availability.json`](audience.availability.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it, as a read on request.** A person or an agent asks for it for a campaign —
> `jason audience availability` — and the runtime routes it by the campaign's route for the operation, holds the route
> to the claim's checks about the route, the plugin and its binding, performs it in a plugin-host process, and
> measures the answer against the result schema. Nothing is recorded about it. A work item may name it too, and is
> then measured against the argument schema when it is written, routed at the claim and answered the same way. Without
> a route it is refused with `no_route`.

## What it is for

"Can this account search now, and what is left?" A search can be refused for three reasons outside its definition: the
account's credits are spent, its plan does not include searching or holds no more contacts, or the user the plugin
signs in as may not search. This read asks first, so an agent can tell a person why before asking them to approve a
search that would fail, and can say what the credits left will buy.

Jason gates nothing on it. A search is still refused by the provider when it starts, as every operation is, and the
answer here is the provider's as far as it can give one before a search starts.

## The input you receive

```json
{
  "args": {},
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } }
}
```

Nothing is named: the route's binding names the account.

## What to return

```json
{
  "available": false,
  "reason": "no_credits",
  "detail": "The account has no search credits left this month.",
  "credits": { "balance": 0, "price_per_person": 1, "currency": "credits" },
  "headroom": 1800,
  "vendor": { "status": "out_of_credits" }
}
```

- `reason` says why not where `available` is false: `no_credits`; `not_permitted`, the user the plugin signs in as may
  not search, such as a member whose role is restricted; `not_included`, the plan does not include searching or holds
  no more contacts; `other`, with the provider's own word under `vendor.status`. Null where `available` is true.
  `detail` is the provider's own words, or null.
- `credits` — the balance, the price of one person found and taken, and the unit both are counted in — and `headroom`,
  how many more people the plan lets the account hold, are said where the provider can say them. A provider that can
  say nothing beyond whether the account may search answers `available` with both null, never a number it does not
  know.
- A user who may not search is an answer, `available: false` with `not_permitted`; `unauthorized` says the plugin's
  credential cannot read even this.

## Repeating after a lost answer

A read changes nothing, so `repeat_after_ambiguous` is `safe`. Nothing is pinned.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.availability.json. Edit the document; this block follows it. -->

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
| invariants | A6, N5 |

<!-- END GENERATED properties -->
