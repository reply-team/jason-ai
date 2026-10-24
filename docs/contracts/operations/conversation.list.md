# `conversation.list` — the conversation threads of a person or a campaign

**Version 1.** The machine-readable contract is [`conversation.list.json`](conversation.list.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"What came back?", answered by the provider's inbox: one page of conversation threads, the one that moved last first.
It is how a reply reaches Jason at all — Jason reads no mailbox of its own — so a responder asked about a reply finds
it in the brief, in such an item's result, or nowhere.

It reads one of two scopes:

- **One person's threads**, across the whole account, when the work item names a person. The campaign is context
  only: a person who wrote back about something else has still written back.
- **One campaign's threads**, when it does not.

## The input you receive

```json
{
  "args": { "limit": 20, "offset": 0, "source": "inbound", "since": "2026-10-01T00:00:00Z" },
  "contacts": [ { "id": "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", "external_ids": { "contact": "48211" } } ],
  "campaign": {
    "id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
    "name": "Q3 LatAm founders",
    "status": "active",
    "external_ids": { "campaign": "c_7714" }
  }
}
```

- `contacts` is there only when the work item names a person, and then it carries the person's identifier and pins
  and nothing else — no name, no address, no channel. A read about a person works by the pin —
  `preflight.provider_contact` is `pinned_only` — so a person with no pin fails `contact_not_linked` before anything is
  called: a read creates nobody. The runtime decides this first, at the claim and before a child exists: a read about
  somebody the routed plugin has pinned nobody for fails `contact_not_linked` there. A plugin handed such an input
  anyway still refuses it the same way.
- Without a person, the campaign is named as in `campaign.get`: by `args.campaign.external_id` the first time, or by
  `campaign.external_ids.campaign` once it is pinned. An input naming neither a person nor a campaign is refused
  before your code runs.
- `campaign` is optional when a person is named. A work item always sends its own, as context; a read Jason makes on
  a person's request, outside any work item, sends none, because a person's threads are the account's.
- `source` is `inbound` (threads the other side wrote into) or `all`, and it is required.
- `since`, when present, keeps only threads that moved at or after it.
- `thread_channel`, `email` or `linkedin`, keeps only threads on that channel; `category`, by the identifier the reads
  report under `vendor.category.id`, keeps only threads filed under it. Both are filters on the threads, never a channel
  the call consumes, and both are applied by the provider before it pages, so `offset` counts the threads that pass
  them. A filter this read applied after reading could not say where the next page starts, and none is.

## What to return

```json
{
  "items": [
    {
      "external_id": "t_99812",
      "channel": "email",
      "subject": "Re: Q3 pilot",
      "preview": "Thanks — could we talk on Thursday?",
      "last_activity_at": "2026-10-03T07:41:09.000Z",
      "unread": true,
      "contact": { "external_id": "48211", "name": "Marta Alvarez" },
      "campaign_external_id": "c_7714",
      "has_meeting_intent": true,
      "vendor": { "is_last_message_planned": false, "state": "needs_attention", "category": { "id": "3", "name": "Interested" } }
    }
  ],
  "has_more": false,
  "vendor": { "ordering": "last_activity_desc" }
}
```

- `preview` is at most 500 characters, cut where a character ends; null when the provider shows none.
- `contact` is the person as the provider names them: their identifier (null when the provider no longer holds the
  person) and a display name. It is null when the thread names nobody. No address travels.
- `unread` is reported on every thread. This version takes no unread-only filter: a page filtered after reading could
  not say where the next one starts.
- Return the campaign's identifier under `external_ids.campaign` only when this read made the first link. A read
  never pins a person.

## Pages

A page holds at most `limit` threads, starting at `offset` in the provider's own order, and `has_more` says whether
more follow. A page may come back **shorter than `limit` with `has_more: true`**: the plugin stops before the result
would pass 768 KiB, counted the way the runtime counts a result. So the next page starts at `offset` plus the number
of threads this page returned.

## When it fails

Use the codes the document declares. `contact_not_linked` is permanent and comes before any call. `feature_unavailable`
says the account's plan or role does not include the inbox, which signing in again would not change. A campaign the
provider does not hold answers an empty page rather than a failure, so this operation declares no `*_not_found`. A
person whose pin the provider no longer holds is not an empty page: the provider refuses the request with a
validation problem that names no code of its own, which arrives as `provider_call_failed`. A lost answer may simply
be asked again.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from conversation.list.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `read` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | nothing at stake; page with the returned cursor | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `optional`, channel `none`, suppression `checked`, provider contact `pinned_only` |
| in a paused campaign | `waits` |
| contact projection | `id`; channels: `none` |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | H10 |

<!-- END GENERATED properties -->
