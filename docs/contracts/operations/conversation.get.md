# `conversation.get` — read one thread before answering it

**Version 1.** The machine-readable contract is [`conversation.get.json`](conversation.get.json); this page explains
it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Read me the thread before I answer": one thread's messages in order, who wrote each and to whom. The thread is named
by the identifier `conversation.list` returned for it.

## The input you receive

```json
{
  "args": { "thread": { "external_id": "t_99812" }, "limit": 20, "offset": 0 },
  "campaign": {
    "id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
    "name": "Q3 LatAm founders",
    "status": "active",
    "external_ids": { "campaign": "c_7714" }
  }
}
```

The thread names itself, so nothing else has to: the campaign is the work item's own, carried for context, and a read
Jason makes on a person's request, outside any work item, sends none. `limit` is at most 20 — see "Pages" for why the
page is short.

## What to return

```json
{
  "thread": {
    "external_id": "t_99812",
    "channel": "email",
    "subject": "Re: Q3 pilot",
    "contact": { "external_id": "48211", "name": "Marta Alvarez" },
    "campaign_external_id": "c_7714",
    "unread": true,
    "vendor": { "category": { "id": "3", "name": "Interested" }, "has_ai_draft": false }
  },
  "messages": [
    {
      "external_id": "<CAF1x@mail.example.com>",
      "direction": "outbound",
      "sent_at": "2026-09-29T08:00:00.000Z",
      "from": "sdr@vendor.example",
      "to": ["marta@example.com"],
      "cc": null,
      "bcc": null,
      "subject": "Q3 pilot",
      "body": "<p>Hi Marta, …</p>",
      "truncated": false,
      "vendor": { "state": "ok", "status_code": null, "ai_sent_type": null }
    }
  ],
  "has_more": false,
  "vendor": { "ordering": "oldest_first" }
}
```

- Messages come **oldest first**.
- `body` is the message as the provider holds it, markup included, cut at 12000 characters where a character ends;
  a cut message says `truncated: true`.
- `from` is an address, or a display name where the channel has none. `to`, `cc` and `bcc` hold at most ten entries
  each.
- `thread.subject` is the subject its messages carry, null where the channel has none.
- A drafted answer the provider holds is reported as `vendor.has_ai_draft` and never returned.

## Pages

A page holds at most `limit` messages, starting at `offset`, and `has_more` says whether more follow. A page may come
back **shorter than `limit` with `has_more: true`**: the plugin stops before the result would pass 768 KiB, counted
the way the runtime counts a result. So the next page starts at `offset` plus the number of messages this page
returned.

Why the page is short at all: the runtime counts six bytes for DEL, for every character outside ASCII and for each of
`<`, `>`, `&`, `'`, `"`, `+` and the backtick. Twenty messages of 12000 characters of Cyrillic, or of markup-heavy
HTML, are 1,440,000 bytes, a third past the 1,048,576 the runtime accepts as one result, so no maximum alone can
promise a page that fits. One message always fits: at the schema's worst it is about 290 KB.

## When it fails

Use the codes the document declares. `conversation_not_found` is permanent: the provider holds no such thread, or
not one this account may see. `provider_answer_too_large` is permanent too: some providers send a thread's whole
history in one answer, and a history longer than this installation lets a plugin read from a program
(`Plugins:Exec:OutputBytes`) would be cut the same way every time. A lost answer may simply be asked again.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from conversation.get.json. Edit the document; this block follows it. -->

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
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | H3, H5 |

<!-- END GENERATED properties -->
