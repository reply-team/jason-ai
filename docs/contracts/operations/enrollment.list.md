# `enrollment.list` — who is in a campaign at the provider

**Version 1.** The machine-readable contract is [`enrollment.list.json`](enrollment.list.json); this page explains
it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`
> ([docs/routing.md](../../routing.md)).

## What it is for

"Who is in this campaign?", answered by the provider: one page of the people its campaign holds, and where each of
them stands. The catalogue's operation reads from either end — the campaign's, or the person's ("where is this
person across everything?") — and this version serves the campaign's end only; reading from the person's end is a
later operation of its own.

It returns **no address**. A planner works by the pin the runtime holds, not by matching addresses, and a result is
stored with its work item: a page of addresses would become a second copy of the audience nobody meant to keep.
Names come back because a person reading the page has to recognise who is on it.

## The input you receive

```json
{
  "args": { "campaign": { "external_id": "c_7714" }, "limit": 100, "offset": 0 },
  "campaign": {
    "id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
    "name": "Q3 LatAm founders",
    "status": "active",
    "external_ids": {}
  }
}
```

The campaign is named exactly as in `campaign.get`: by `args.campaign.external_id` the first time, which returns
it under `external_ids.campaign`, or by `campaign.external_ids.campaign` once it is pinned. `limit` and `offset`
are both required — a page size nobody chose is a decision made on somebody's behalf.

## What to return

```json
{
  "total": 214,
  "items": [
    {
      "external_id": "48211",
      "first_name": "Marta",
      "last_name": "Alvarez",
      "status": "active",
      "current_step": "Follow-up 2",
      "added_at": "2026-10-02T06:15:42.170Z",
      "opted_out": false,
      "vendor": {
        "status_in_sequence": "active",
        "current_step_id": "9902",
        "call_status": "none",
        "meeting_status": "none",
        "email_disposition": null
      }
    }
  ],
  "has_more": true,
  "vendor": { "ordering": "added_desc" }
}
```

- `status` is `active`, `paused`, `finished`, or `other` for a state this vocabulary has no word for. The
  provider's own word is always under `vendor.status_in_sequence`, so a pause for an out-of-office reply reads
  `paused` here and says why there.
- `first_name` and `last_name` are null when the provider holds none; an empty string is a claim the provider did not
  make.
- `current_step` is the provider's own label for the step, not a number.
- `added_at` is in UTC, whatever offset the provider wrote it with.
- `total` is the provider's own count, or null when it refused to count. It is never the length of the page.
- `vendor` is a closed list. Nothing else of the provider's answer travels — not an address, not a company, not a
  title.

## Pages

A page holds at most `limit` people, starting at `offset` in the provider's own order — the most recently added
first, which `vendor.ordering` states — and `has_more` says whether more follow.

A page may come back **shorter than `limit` with `has_more: true`**. The plugin stops adding people before the result
would pass 768 KiB, counted the way the runtime counts a result: six bytes for DEL, for every character outside
ASCII, and for each of `<`, `>`, `&`, `'`, `"`, `+` and the backtick, and twelve for a character outside the Basic
Multilingual Plane. So **the next page starts at `offset` plus the number of people this page returned**, never at
`offset + limit`. One person always fits: the largest one the schema allows is far below the budget.

## When it fails

Use the codes the document declares. `campaign_not_found` is permanent: a campaign the provider does not hold is
not an empty page. `feature_unavailable` says the account's plan or role does not include this read, which signing
in again would not change. `provider_answer_too_large` says the provider's answer was longer than this installation
lets a plugin read from a program (`Plugins:Exec:OutputBytes`); asking again would cut it the same way, so it is
permanent — a smaller `limit` is the remedy. A lost answer may simply be asked again: a read changes nothing and costs
nothing.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from enrollment.list.json. Edit the document; this block follows it. -->

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
| pre-flight | contact `not_used`, channel `none`, suppression `checked` |
| in a paused campaign | `waits` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | G1, G3, G4, G8, H1, K4, L5 |

<!-- END GENERATED properties -->
