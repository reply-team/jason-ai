# `audience.search` — find people matching a definition, and make them ours

**Version 1.** The machine-readable contract is [`audience.search.json`](audience.search.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Find up to this many people matching this definition, and make them ours." The spending act of the audience family:
one search of the provider's database, charged per person found and taken, its people revealed — names, titles,
companies, and the addresses, numbers and profiles they are reached at. It is how a campaign gets people from the
provider: a new audience from a definition, or more of one it already searched under, a top-up.

A person approves it once per work item (`confirm_once`), reading the count, the definition and the options before
anything is spent. After the first search under an audience, the campaign's allowance — a standing approval a person sets
for one audience, capped in the people its searches ask for — may let a top-up go without the question; one it does not
cover waits as the first did, its preview saying why ([`docs/work-execution.md`](../../work-execution.md), "Standing
approvals"). Nobody picks among candidates: where a provider finds people first and reveals them after, both
happen inside the plugin, and every person the search returns is revealed.

The contract asks no provider to leave people already known out of a search: not every provider's search takes a list
of people to skip, and those that do take different things — their own identifiers, addresses, profile links. So a
search may find, and be charged for, a person the campaign already holds, and a top-up at a provider that does not skip
the people it found before may find them again. A provider whose search can take such a list takes it under
`args.provider`, the object every operation reserves for the provider and its skills, which the runtime hands to the
plugin as written ([the input a plugin receives](../README.md#the-input-a-plugin-receives)). That a campaign never
holds the same person twice is the runtime's to keep, whichever provider searched, and it keeps it when the people a
search found become the campaign's: a person it already holds — by the identifier the provider gave, an address or a
LinkedIn profile — stays the one member they are, never a second contact
([`docs/campaign-manager.md`](../../campaign-manager.md) §10).

A search buys records and reaches nobody, so its reach is `control`. It runs through an open provider block of its
account rather than waiting on it — a block over the account's sends does not hold its searches — and a `blocked`
answer ends it, final. That answer opens no block of the account and is counted on none — only the `blocked` answer
of held work does that — and the next search is not held by it either: it is claimed, and the provider answers it on
its own. That is the intent: a block guards the account's sends, a search sends nothing, and a search that waited on
one would hold a purchase behind a send's trouble. A look at the same audience, free and approved by nobody, is
[`audience.preview`](audience.preview.md).

## The definition and the audience

The definition is the provider's, not Jason's: the filters its search takes, in the shape its plugin declares under
`audience_definition` in its manifest ([`docs/plugins.md`](../../plugins.md), §3). This document holds it to being an
object when the work item is written; the route is resolved at the claim, and only then is the definition held to the
routed plugin's declared schema — after the arguments are checked and before a person is asked to approve. A
definition off the schema fails the claim `definition_invalid`, with a pointer for each place it fails, and nothing
is spent. A rule of the provider's that the schema cannot express is the plugin's to refuse, as `definition_rejected`.
`jason operation describe audience.search` answers both schemas for a campaign.

Exactly one of `args.audience` and `args.definition` is given: both at once are refused when the work item is written,
and so is neither. A definition starts a new audience; a number names one of the campaign's audiences, and is searched
by the definition that audience was made from; a number the campaign holds no audience under fails the claim
`audience_unknown`. Either way the runtime composes the definition under `definition` at the root of the input, and the
plugin reads it there and nowhere else.

A search the claim lets through, to run now or to wait for a person, runs under one of the campaign's audiences: the
claim finds the audience its definition already is — the same content, its members in any order — or makes the
campaign's next one, numbered 1, 2, 3 in that order, and records it on the work item under `context.audience`. A search
the claim refuses makes none. The plugin is told none of this: it reads the definition at the root and the arguments
as they were written, a top-up's number among them ([`docs/campaign-manager.md`](../../campaign-manager.md) §10).

## The input you receive

```json
{
  "args": {
    "definition": {
      "industries": [ { "value": "Logistics", "type": "include" } ],
      "job_titles": [ { "value": "Head of Operations", "type": "include" } ],
      "company_sizes": ["51-200"]
    },
    "count": 250,
    "people_per_company": 2,
    "find_phones": false
  },
  "definition": {
    "industries": [ { "value": "Logistics", "type": "include" } ],
    "job_titles": [ { "value": "Head of Operations", "type": "include" } ],
    "company_sizes": ["51-200"]
  },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

- `count` is an upper bound, 1 to 1000, never a promise: the search stops at it, or sooner where the source runs out.
- `people_per_company`, `include_without_email` and `find_phones` read as 1, false and false where they are absent.
  Phones may cost extra: the document's `cost_basis` says so.

## What to return

Two answers, the plugin's choice, as a send answers `sent` or `accepted`:

```json
{ "items": [ { "status": "accepted", "reference": "search-5521", "found": null, "people": [] } ] }
```

with `"external_ids": { "search": "search-5521" }` beside it: the provider took the search and is running it. Its
people are read by [`audience.search_status`](audience.search_status.md) and it is stopped by
[`audience.search_cancel`](audience.search_cancel.md), each naming this work item; the runtime composes the reference
for them from this attempt, so nobody types one.

```json
{
  "items": [
    {
      "status": "finished",
      "reference": null,
      "found": 1,
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
      ]
    }
  ]
}
```

with no identifier beside it: the search ended inside the call and every person it found is in the result. A
provider that searches inside the call always answers so, and a status read of its search is never made: one naming it
is refused at the claim `search_reference_absent`, since there is nothing left to read.

- `finished` is said only where every person found fits the result; otherwise the answer is `accepted`, and the
  people are read a page at a time.
- `reference` is the one under `external_ids.search`: both on `accepted`, neither on `finished`. It is kept on the
  attempt and in the item's result and pinned to nothing.
- Each person is in the one shape every provider answers in: the provider's identifier, the names, the title, the
  company with its name, domain, size and industry, the location as the provider spells it, the channels as present,
  and the provider's own record beside them under `raw` — JSON text, `JSON.stringify` of it, trimmed to fit 16 KiB, or
  null. The schemas bound every value a result may hold, and an object of the provider's own keys has no bound they
  can state, so the record travels as text. Jason reads nothing in it: it keeps it, parsed, beside a person it makes.

A provider already running a search of the same definition for the account fails `search_in_progress`, permanent; an
account with no credits to start one fails `insufficient_credits`, permanent, where the plugin cannot read the answer
as a provider block; and a plan or role without searching fails `feature_unavailable`. Nothing is spent by any of them.

## What Jason does with the answer

Either answer gives the search a row Jason follows it by. After `accepted`, Jason reads the search's people itself, on a
cadence, with [`audience.search_status`](audience.search_status.md), and lands each page as it arrives. After `finished`,
the people in the answer land at once, as one page, and the search ends there. Landing makes each person a member of the
campaign — one Jason already holds, found by the identifier pinned to them, an address or a LinkedIn profile, or one made
from what the provider said of them — and pins the provider's identifier to them under `contact`
([`docs/campaign-manager.md`](../../campaign-manager.md) §10).

## Repeating after a lost answer

A search spends, so a lost answer may have started one. `repeat_after_ambiguous` is `after_recovery_read`: the repeat
reads the provider's searches started since `context.unsettled_since` first, finds this request's by the work item's
key where the provider keeps one for a search, or else by its definition and options, and answers it `accepted` with
its reference, whatever state it is in. Only where none matches does the repeat start a search.

The runtime holds such a repeat to the attempt that may have started the search, as it holds a repeated send: the
package by its id and digest, the account, the arguments and the input that attempt was composed with
([routing](../../routing.md) §7, row 23). A route moved to another account, a package replaced, or a count or a
definition edited while the item waited would make the recovery read look in the wrong place, or for another request,
and start a second search that spends while the first goes on unseen; so the repeat is refused, `provider_answer_lost`,
ambiguous and final, and a person finds out at the provider whether the first attempt started a search.

## The preview a person reads

```text
Search the provider for up to 250 people matching the definition below, for Jason's campaign Q3 LatAm founders: a metered search, charged per person found.
```

The definition is highlighted whole beside the line, with the count, the audience's number and the options, so the
person approving reads what will be searched and what it may cost.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from audience.search.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `irreversible` | — |
| `approval` | `confirm_once` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the provider's searches started since `context.unsettled_since`: one of them may be this request's, and repeating spends again. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `metered` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked`, provider contact `not_used` |
| in a paused campaign | `waits` |
| in an archived campaign | `cancelled` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | `Search the provider for up to {/args/count} people matching the definition below, for Jason's campaign {/campaign/name}: a metered search, charged per person found.`; highlighted: `/args/count`, `/definition`, `/args/audience`, `/args/people_per_company`, `/args/include_without_email`, `/args/find_phones` |
| pinned identifiers | `search` names a work_item |
| recovery read | The provider's searches of this account started at or after `context.unsettled_since`: the one under this work item's key, where the provider keeps a key for a search, or else the one of the same definition and options. A search found is this request's, and is answered `accepted` with its reference whatever state it is in; a new search is started only where none matches. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | B1, B3, B6, B7, C2, C3 |

<!-- END GENERATED properties -->
