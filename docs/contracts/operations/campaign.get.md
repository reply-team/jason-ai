# `campaign.get` — read one campaign from the provider

**Version 1.** The machine-readable contract is [`campaign.get.json`](campaign.get.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version never runs it.** No `provider_op` work item reaches a plugin at all yet: every one of them fails
> at the claim with `no_route`. What runs is the check on the work item itself — an operation no contract is
> published for is refused when the item is written, and the arguments it carries are measured against this
> operation's argument schema. The contract is published complete so that routing is the only thing left to add —
> and so that a plugin author can implement and test the operation now.

## What it is for

"Show me this campaign": the settings, the state and the count of people in each state, as the provider holds them.
It is the only read among the three operations this version publishes, and it is the operation that **links** a
Jason campaign to the provider's own — the first successful call returns the provider's identifier, the runtime
will pin it, and every later operation on that campaign works by the pin rather than by a name anybody typed
twice.

Because it reads, it changes nothing, it costs nothing and it needs no idempotency key. It is the one operation in
this version whose answer may simply be asked for again when an attempt ends without one.

## The input you receive

```json
{
  "args": { "campaign": { "external_id": "c_7714" } },
  "campaign": {
    "id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
    "name": "Q3 LatAm founders",
    "status": "draft",
    "external_ids": {}
  }
}
```

`args` is what the caller wrote under the work item's `context.input`, and it is always present — an operation
whose arguments are all optional still receives `args: {}`. `campaign` is Jason's own record of the campaign the
work item belongs to, with `external_ids` filtered to **this plugin's own pins**: another provider's identifier is
never visible to you.

A campaign has to be named one way or the other. Either `args.campaign.external_id` carries the provider's
identifier — that is the first link, made deliberately by someone who verified it — or `campaign.external_ids
.campaign` already holds it. An input with neither is refused before your code runs.

## What to return

```json
{
  "campaign": {
    "external_id": "c_7714",
    "name": "Q3 LatAm founders",
    "status": "live",
    "counts": { "enrolled": 120, "active": 44, "finished": 76 }
  },
  "vendor": { "sending_profile": "q3-latam", "throttle_per_day": 80 }
}
```

`status` is the neutral vocabulary: `draft`, `live`, `paused`, `archived`, or `other` when the provider's own state
has no word here — in which case put the provider's word under `vendor`. `counts` may be empty: "the provider
reports no counts" and "the provider reports zero" are different facts and a caller plans differently on each.
`vendor` is yours: the provider's own answer, untranslated and untyped, so nothing is lost while the neutral part
stays strict.

Return the provider's campaign identifier in `external_ids.campaign`. That is what makes the link, and the runtime
keeps it.

## When it fails

Use the codes the document declares. The class is what the runtime reads: `transient` may be repeated,
`permanent` and `validation` are final, and `ambiguous` says the provider may already have acted — which, for a
read, costs nothing, so this operation allows the repeat.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from campaign.get.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `read` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `before_repeating` | nothing at stake | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | none names this operation |

<!-- END GENERATED properties -->
