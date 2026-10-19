# `campaign.pause` — stop a provider campaign

**Version 1.** The machine-readable contract is [`campaign.pause.json`](campaign.pause.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, routed to a plugin at the claim, and performed against a real provider account — the
> provider's campaign stops sending. It reaches a plugin only where a route sends it: without one the item fails at
> the claim with `no_route`.

## What it is for

"Stop this campaign." It is the incident response: a deliverability scare, a wrong message, an audience that should
never have been reached. It never requires a lookup first and never waits for permission, so it runs `auto`, and it
is the one operation besides `optout.record` whose work **runs while its Jason campaign is paused**
(`dispatch.in_paused_campaign: runs`): the provider's campaign must still stop after somebody paused Jason's first.

Two things are called "pause". `jason campaign pause` pauses **Jason's** campaign, which stops Jason's own work; this
operation pauses **the provider's** campaign, which stops the provider sending. They are two namespaces with the same
words ([../README.md](../README.md), "Two namespaces"). The manager's incident response does both, the provider's
first.

## The input you receive

```json
{
  "args": { "reason": { "code": "deliverability", "note": "three bounces from the new domain in an hour" } },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

The campaign is named by its pin, or the first time by `args.campaign.external_id`, as `campaign.get` names it.
`reason` is optional; when present it says why, and it is Jason's record — nothing in it reaches the provider.

## What to return

```json
{ "items": [ { "campaign_external_id": "c_7714", "status": "paused" } ] }
```

`status` is `paused`, or `already_paused` when the provider says the campaign was paused before this call — which
is not an error. A provider that answers a repeated pause as a pause answers `paused` both times.

## Repeating after a lost answer

The provider documents the pause as idempotent: it answers the same whether the campaign was just paused or already
was. So an attempt that ended without an answer is simply handed out again (`safe`), and the next one answers
`already_paused` if the first one landed, where the provider can tell, or `paused` where it answers a repeated pause
as a pause.

## When it fails

A campaign the provider does not hold fails `campaign_not_found`. One it will not pause — archived, or never started,
which a draft is — fails `campaign_not_pausable`, with the provider's own code under `details.provider_code` where it
gave one. The shared failures are spelled as every document spells them.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from campaign.pause.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the campaign's state; pausing a paused campaign is a no-op, not an error. | — |
| `idempotency_key` | `required` | conditional — when a collection is given. |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked`, provider contact `not_used` |
| in a paused campaign | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | E6, G4, L6 |

<!-- END GENERATED properties -->
