# `message.cancel` — stop a send the provider still holds for its moment

**Version 1.** The machine-readable contract is [`message.cancel.json`](message.cancel.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Do not send that after all." A `message.send` or a `social_invitation.send` the provider took for a later moment waits
there, and this takes it back before it goes: one send, named by the work item that made it. It is how a send approved
yesterday is stopped today, when the person has answered meanwhile, asked to stop, or the campaign was paused.

It reaches nobody, so it needs nobody's approval, and it is not refused for a person on the do-not-contact register: a
send to somebody who has just asked to stop is the one most worth cancelling. For the same reason it runs while its
campaign is paused, as the provider's own pause and the record of a stop do. Under an open block of the account, the
cancel does not wait on it, as a stop and a pause do: the moment of the send it takes back passes while it would wait,
so it runs, and a `blocked` answer ends it, final, a protective action that failed.

A send that went cannot be cancelled. Sending again is the compensation of a cancel, which is why the catalogue calls
it compensatable rather than reversible.

## Naming the send

As for [`message.status`](message.status.md): the caller names the work item in `args.send`, and the runtime composes
the reference under `send` at the claim, refusing `send_unknown`, `send_reference_absent` or `send_route_mismatch`
before anything is run. A plugin cancels by `send.reference` and by nothing the caller wrote.

## The input you receive

```json
{
  "args": { "send": "wi_…" },
  "send": { "work_item": "wi_…", "operation": "message.send", "reference": "email-1001" },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "paused", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

## What to return

```json
{ "items": [ { "state": "cancelled", "cancelled_at": "2026-11-02T09:15:00Z" } ] }
```

`state` is always `cancelled`: a send an earlier call already cancelled answers the same. A send that went, or failed,
before the cancel reached it fails `send_not_pending`, permanent, with `state` in the details saying which — it is never
reported as cancelled. A send the provider attempted and lost track of is `failed` here, as the provider holds it, and
`message.status` tells it as `unknown`, since it may have gone.

## Repeating after a lost answer

Cancelling a cancelled send changes nothing, and a send that went meanwhile answers `send_not_pending`, so
`repeat_after_ambiguous` is `safe` and the item is simply handed out again. Nothing is pinned.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from message.cancel.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `compensatable` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the pending list — an already-cancelled item is a success, not a failure. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `not_applicable`, provider contact `not_used` |
| in a paused campaign | `runs` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | I5, L6 |

<!-- END GENERATED properties -->
