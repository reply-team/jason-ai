# `message.status` — read one send by the reference the provider gave it

**Version 1.** The machine-readable contract is [`message.status.json`](message.status.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Did it go?" A `message.send` or a `social_invitation.send` the provider took for later ends `accepted`, with the
provider's reference for the send, and the work item is done: whether and when the send went is learned later. This is
how it is learned. One read names one send, by the work item that made it, and answers what the provider holds now —
waiting, gone, cancelled before it went, failed, or lost track of — with the moment it went and the message it became.

The catalogue's question is wider, "What is queued to go out, to whom, and when?", by person, campaign, sender or
everything. The providers mapped first list no pending sends at all; they read one by its reference, so this version
reads one ([the divergence](../divergence-from-l1.md)).

## Naming the send

The caller names the work item, never the reference: `args.send` is the identifier of a work item of the same campaign
whose send succeeded. The runtime looks the reference up at the claim — in the provenance of the attempt that made the
send — and composes it under `send`, as it composes the campaign. A plugin reads the send by `send.reference` and by
nothing the caller wrote. Three refusals come before anything is run, each permanent:

- `send_unknown` — the campaign holds no work item under `args.send`, or that item is not a `message.send` or a
  `social_invitation.send`;
- `send_reference_absent` — that item has not succeeded, or its send came back with no reference: the provider kept
  nothing to read it by;
- `send_route_mismatch` — this item is routed to another plugin, or under another binding, than the send was made with.
  The reference is that provider account's, so it is read there or not at all. The package's version is not compared:
  a reference outlives it.

## The input you receive

```json
{
  "args": { "send": "wi_…" },
  "send": { "work_item": "wi_…", "operation": "message.send", "reference": "email-1001" },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } }
}
```

## What to return

```json
{
  "state": "sent",
  "channel": "email",
  "cancelable": false,
  "accepted_for": "2026-11-03T14:00:00Z",
  "sent_at": "2026-11-03T14:00:12Z",
  "message_id": "msg-7001",
  "rendered_subject": null,
  "rendered_body": null,
  "failure_reason": null,
  "failure_code": null,
  "cancelled_at": null
}
```

- `state` is one of five words. A provider state that still holds the send — one it is sending now, or one it adds later
  — is `queued`. `sent`, `cancelled`, `failed` and `unknown` are final. `failed` says the send did not go; `unknown` says
  the provider attempted it and lost track of it, so it may have gone, and it is never `cancelable`. Before a new send to
  that person, read their thread: whether it went is learned there, never by sending again.
- `cancelable` is true only for a queued send the provider is not already sending: that is the send `message.cancel` can
  still stop.
- `failure_reason` is the provider's own sentence for a send that failed, as it gives it; `failure_code` is the
  provider's own code for a failure, as it gives it and uninterpreted, or null where it gives none.
- `rendered_subject` and `rendered_body` are the message as it went, where the provider keeps that copy, and null
  otherwise.

## What the runtime does with a send that went

A read that answers `sent` is the one place the runtime says, in the journal, that a send went. It says so for any send
a read finds gone, once — one the provider took for later, or one that went inside its own call, since the read adds the
moment and the message's identifier — in the campaign's journal: a reserved line, `message_sent_observed`, about the
work item that made the send, keyed by it, carrying the plugin, the reference, the channel, the moment and the message's
identifier — never the text. A second read that finds the same send gone says nothing more. The send's own result stays
as it was frozen, whatever it said: a finished item's result is never rewritten. The line is a trigger a campaign's
manager may opt into; it is not a default one.

## Repeating after a lost answer

A read changes nothing, so `repeat_after_ambiguous` is `safe` and the item is simply handed out again. Nothing is
pinned.

An answer longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`) is
`provider_answer_too_large`, permanent: asking again would cut it at the same place.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from message.status.json. Edit the document; this block follows it. -->

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
| timeout | 60000 ms |
| invariants | K4 |

<!-- END GENERATED properties -->
