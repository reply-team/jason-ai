# `audience.search_status` — how far a search has got, and the people it found since the last read

**Version 1.** The machine-readable contract is [`audience.search_status.json`](audience.search_status.json); this
page explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"How far has this search got, and who was found since I last asked?" An [`audience.search`](audience.search.md) the
provider took and is still running ends its work item `accepted`, with the provider's reference for the search, and
the item is done: its people are learned later. This is how they are learned — one page at a time, each read a work
item of its own naming the search, answering the search's state, the people found since the last read, and where the
next read starts. It spends nothing and needs nobody's approval.

## Naming the search

The caller names the work item, never the reference: `args.search` is the identifier of a work item of the same
campaign whose search succeeded `accepted`. The runtime looks the reference up at the claim — in the provenance of the
attempt that started the search — and composes it under `search`, with the cursor, as it composes the campaign. A
plugin reads the search by `search.reference` and by nothing the caller wrote. Three refusals come before anything is
run, each permanent:

- `search_unknown` — the campaign holds no work item under `args.search`, or that item is not an `audience.search`;
- `search_reference_absent` — that item has not succeeded, or its search came back with no reference: a search that
  answered `finished` inside its own call has nothing more to read;
- `search_route_mismatch` — no route active now names the plugin and binding the search was started with. A read is
  routed where the search ran, whatever `audience.search_status` itself resolves to; the reference is that provider
  account's, so it is read there or not at all.

## The cursor

This is the one canonical operation that pages by a cursor rather than an offset: a running search's people arrive
over time, and only the provider knows where the last read stopped. The cursor is the provider's own words for that
place, at most 2048 printable characters. The runtime composes `search.cursor`: null on a first read, which reads from
the first person found, and on a later one the `cursor` the last read answered, which the runtime keeps in the search's
row and never parses.

A page holds at most `args.limit` people, at most 100, those found after the cursor in the provider's own order, and
none of them comes back again after the cursor that follows it. The answer's `cursor` is where the next read starts —
the same one back when nothing moved — and `has_more` says whether more people are readable from it now. A page is held
to the two bounds an offset page is: it never holds more people than a caller may ask for, and one person always fits
under the 768 KiB a page is cut at, so a page may come back short of `limit` with `has_more: true`.

## The input you receive

```json
{
  "args": { "search": "wi_…", "limit": 100 },
  "search": { "work_item": "wi_…", "reference": "search-5521", "cursor": null },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } }
}
```

## What to return

```json
{
  "state": "in_progress",
  "found": 40,
  "people": [
    {
      "external_id": "p-90211",
      "first_name": "Lena",
      "last_name": "Novak",
      "title": "Head of Operations",
      "company": { "name": "Northwind Freight", "domain": "northwind-freight.example", "size": "51-200", "industry": "Logistics" },
      "location": "Porto, Portugal",
      "channels": [ { "channel": "email", "value": "lena.novak@northwind-freight.example" } ],
      "raw": "{\"id\":\"p-90211\",\"seniority\":\"head\"}"
    }
  ],
  "cursor": "page-2",
  "has_more": false,
  "vendor": { "status": "running" }
}
```

Each person is in the shape [`audience.search`](audience.search.md) answers in, with every channel the provider
revealed and its own record under `raw` as JSON text.

## The states

`state` is one of six words, and each says what the search means for whoever reads it next:

- `in_progress` — the provider is still searching. The search is read again.
- `finished` — the search reached the count it was asked for.
- `exhausted` — the source ran out before it did.
- `paused` — a person paused the search at the provider.
- `paused_for_credits` — the search stopped because the account's credits ran out.
- `paused_for_limit` — the search stopped because the account's plan holds no more contacts.

`finished` and `exhausted` are final: once a read answers either with `has_more` false, the search has nothing more
to give and is read no more. The three paused states hold the search until a person acts at the provider — resumes
it, buys credits, raises the plan — so the search may move again, and it is read again. A state the provider adds
later is the nearest of the six: one in which it still searches is `in_progress`, one that holds the search until a
person acts is `paused`, with the provider's own word under `vendor.status` either way. A search that is stopped, or
that stopped moving, is said by whoever stops it or watches it, not by the provider, so neither is a state here.

## Repeating after a lost answer

A read changes nothing, and the cursor moves only on an answer, so `repeat_after_ambiguous` is `safe` and the item is
simply handed out again, reading from the same place. The operation pins nothing: what the runtime makes of the people
it answers is the next section. A search the provider no longer holds, or one this account may not see, fails
`search_not_found`, permanent.

An answer longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`) is
`provider_answer_too_large`, permanent: asking again would cut it at the same place.

## What Jason does with the answer

A read that succeeds lands its page in the campaign, whoever made it, in the same change set that records its answer:
each person becomes a member of the campaign — found among the people Jason holds by the identifier pinned to them, by an
address, by a LinkedIn profile, or made from what the provider said of them — and the provider's identifier is pinned
to them under `contact`. The search's row takes the `cursor`, the `state`, `found` and `has_more`, and a read that answers
`finished` or `exhausted` with nothing more readable ends the search. A read that fails `search_not_found` ends it
`cancelled`, said once, by that read: the provider holds no search under the reference for the account the binding
signs in as — deleted there, or that account changed — so nothing would read it again, and the people it already
landed stay the campaign's. Jason makes these reads itself, on a cadence, for
each search it follows, and stops when the search ends or its reads land nobody for a bound
([`docs/campaign-manager.md`](../../campaign-manager.md) §10).

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.search_status.json. Edit the document; this block follows it. -->

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
| timeout | 60000 ms |
| invariants | none names this operation |

<!-- END GENERATED properties -->
