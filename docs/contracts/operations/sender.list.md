# `sender.list` — the provider's accounts a send can go from

**Version 1.** The machine-readable contract is [`sender.list.json`](sender.list.json); this page explains it. Where
the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is checked against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is checked against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"What can we send from?" — the mailboxes and social accounts of the provider's account, one page of them, each with
the identifier a send names it by, the address it sends from and whether it can send now. A send outside any campaign
names its sender by what this read returns.

**A colleague's account is never offered unasked.** A provider that shares accounts across a team lists a teammate's
mailbox beside the caller's own, and a message sent from it goes out under that person's name and address. So
`owners` is required: `own` lists the accounts of the user the plugin signs in to the provider as, and nothing else —
an account the provider names no owner for is left out too, since it may be anybody's — while `team` lists every
account, each marked `own`, `teammate` or `unknown`. Asking for `team`, reading the mark and naming that account in a
send is how a person sends from a colleague's account on purpose; the send's preview then says whose address it goes
from.

## The input you receive

```json
{
  "args": { "owners": "own", "sender_channel": "email", "limit": 20, "offset": 0 },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": {} }
}
```

- `owners` is `own` or `team`, as above.
- `sender_channel`, when given, is `email` or `linkedin`: only the accounts of that channel. It is named so because
  `channel` in an operation's arguments is always a contact's channel the operation consumes, and a list of senders
  consumes none.
- `limit` and `offset` page the list as every paged read does; both are required.

## What to return

```json
{
  "items": [
    {
      "external_id": "12",
      "channel": "email",
      "name": "Ann Owner",
      "address": "ann@example.com",
      "status": "active",
      "owner": "own",
      "vendor": { "status": "connected" }
    }
  ],
  "has_more": false,
  "channels": { "email": "listed", "linkedin": "not_asked" },
  "vendor": { "ordering": "email_then_linkedin" }
}
```

- `status` is `active`, `inactive` (turned off), `disconnected` (its connection to the channel lost) or `other`, with
  the provider's own word under `vendor.status` either way.
- `channels` says what became of each channel: `listed`; `not_included`, a channel the provider account's plan or role
  does not include, which holds no account a send could go from; or `not_asked`, left out by `sender_channel`. Without
  `sender_channel` each channel is asked for on its own, so a plan without LinkedIn still lists its mailboxes. Naming
  a channel the plan does not include fails `feature_unavailable` instead.
- A name or an address the provider holds as empty is null.

A read pins nothing: the identifiers it lists are named by the sends that follow.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from sender.list.json. Edit the document; this block follows it. -->

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
| invariants | none names this operation |

<!-- END GENERATED properties -->
