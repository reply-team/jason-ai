# `conversation.close` — set a thread aside as worked

**Version 1.** The machine-readable contract is [`conversation.close.json`](conversation.close.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Done with this thread" — worked, not ignored. The thread leaves the queue of what needs attention, and nothing else
happens: closing a thread records no business outcome and stops no outreach. A person who asked to stop is recorded
by `optout.record`, and a person taken out of a campaign by `enrollment.stop`, never by closing what they wrote.

`conversation.reopen` is its counterpart, and puts the thread back.

## The input you receive

```json
{
  "args": { "thread": { "external_id": "t_99812" } },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

## What to return

```json
{ "items": [ { "thread_external_id": "t_99812", "status": "closed" } ] }
```

`status` is always `closed`: a provider that cannot say whether the thread was closed already answers the same word,
and so does one that can.

## Repeating after a lost answer

Closing a closed thread changes nothing, so `repeat_after_ambiguous` is `safe` and the item is simply handed out
again. Nothing is pinned.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from conversation.close.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the thread's state | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
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
| timeout | 60000 ms |
| invariants | H9 |

<!-- END GENERATED properties -->
