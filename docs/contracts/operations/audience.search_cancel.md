# `audience.search_cancel` — stop a search the provider is running; what it found stays found

**Version 1.** The machine-readable contract is [`audience.search_cancel.json`](audience.search_cancel.json); this
page explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Stop this search." A search the provider is running keeps spending, per person it finds, after its campaign is
paused, after its count stopped mattering, after somebody changed their mind. This stops it: one search, named by the
work item that started it. What it found stays found — the people it already returned are kept, and the provider's
charge for them stands.

It reaches nobody and only stops spending, so it carries the catalogue's protective floor: it runs `auto`, where the
catalogue's `job.cancel` asks for `confirm_once`, because waiting for permission to stop a search that spends is the
destructive choice. For the same reason nothing in Jason holds it back. It names nobody, so the do-not-contact register
has nothing to refuse; it runs while its campaign is paused, which is when it is wanted most, and in a campaign archived
while the search ran, whose searches the dispatcher stops this way; and under an open block
of the account it does not wait — a `blocked` answer ends it, final, a protective action that failed.

## Naming the search

As for [`audience.search_status`](audience.search_status.md): the caller names the work item in `args.search`, and the
runtime composes the reference under `search` at the claim, refusing `search_unknown`, `search_reference_absent` or
`search_route_mismatch` before anything is run, and routing the stop where the search ran, whatever
`audience.search_cancel` itself resolves to. A plugin stops the search by `search.reference` and by nothing the
caller wrote. A search that answered `finished` inside its own call has nothing to stop.

## The input you receive

```json
{
  "args": { "search": "wi_…" },
  "search": { "work_item": "wi_…", "reference": "search-5521" },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "paused", "external_ids": { "campaign": "c_7714" } }
}
```

## What to return

```json
{ "items": [ { "status": "cancelled", "ended_as": null } ] }
```

- `cancelled` — this call stopped a search that was running or paused.
- `already_ended` — the search had ended before the call reached it, which is a success too: a cancel is never refused
  for coming too late. `ended_as` says how, where the provider says: `finished` or `exhausted`, as
  `audience.search_status` says them, or `cancelled` where an earlier cancel stopped it; null where it does not say.

## Repeating after a lost answer

A cancel whose answer was lost may already have stopped the search, so `repeat_after_ambiguous` is
`after_recovery_read`: the repeat reads the search's state by its reference first, and a search that ended answers
`already_ended`. A provider that removes what it cancels holds nothing under the reference afterwards, so a reference it
does not hold fails `search_not_found` on a first attempt and answers `already_ended`, with `ended_as` null, on any later
attempt — an earlier attempt may be the one that removed the search, and nothing tells the plugin which earlier attempts
reached the cancel. Nothing is pinned.

## What Jason does with the answer

`cancelled`, and `already_ended` with `ended_as` `cancelled` or null, end the search Jason follows as `cancelled`, said once;
the people it already landed stay the campaign's. `already_ended` with `finished` or `exhausted` leaves the search to its
reads: it ended by itself, and what it found is still to be read
([`docs/campaign-manager.md`](../../campaign-manager.md) §10).

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.search_cancel.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `irreversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `true` | the protective floor — a running search keeps spending after its campaign stops, so waiting for permission to stop it is the destructive choice. |
| `before_repeating` | the search's state: an ended search answers `already_ended`, which is a success. | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `not_applicable`, provider contact `not_used` |
| in a paused campaign | `runs` |
| in an archived campaign | `runs` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | The search's state at the provider, by `search.reference`: a search that ended is the cancel done, and answers `already_ended`; one still running or paused is cancelled. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 60000 ms |
| invariants | L7 |

<!-- END GENERATED properties -->
