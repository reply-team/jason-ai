# `conversation.classify` — file a thread under one of the provider's categories, with the provider's own meaning on its reserved names

**Version 1.** The machine-readable contract is [`conversation.classify.json`](conversation.classify.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is measured against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is measured against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"File this reply as interested." The thread is set to exactly one of the provider's own categories — the ones
`conversation.list` and `conversation.get` report under `vendor.category` — or to none. It is the provider's
vocabulary, not a neutral one: no meaning of Jason's own is assigned, and who decided it, from which message and how
sure they were stay with whoever asked for it. Filing a thread asks the provider to move no participation, no
enrollment and no read state.

## The provider's own meaning on its reserved names

A provider may reserve some of its categories and give them a meaning of its own: an event it sends out to whatever
listens, outreach it starts by its own hand. Filing a thread there asks for that meaning too, and taking the thread out
of the category again need not undo it. So a campaign that must own every send files under categories of its own. A
reserved category whose filing would stop the person — opt them out, end their participation — is not a meaning at all,
and is refused as `category_opts_out`: stopping a person is `optout.record`'s path.

## The input you receive

```json
{
  "args": { "thread": { "external_id": "t_99812" }, "category": { "name": "Interested" } },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

- `category` is required, and is one of three things: `{ "external_id": "3" }`, the identifier the reads report under
  `vendor.category.id`; `{ "name": "Interested" }`, which has to be the one category whose name equals it, compared
  exactly and without regard to case; or `null`, which takes the thread out of the category it is in. Naming both an
  identifier and a name is refused before your code runs.
- A category the provider does not hold fails `category_not_found`, and a name two of its categories carry
  `category_not_unique`, both before anything is filed.
- A category whose filing would stop the person at the provider — opt them out, end their participation — fails
  `category_opts_out`, however it was named, after the lookup and before anything is filed. Stopping a person is
  `optout.record`'s path, and a filing the provider reads as a stop is not undone by taking the thread out again.

## What to return

```json
{ "items": [ { "thread_external_id": "t_99812", "status": "classified", "category": { "external_id": "3", "name": "Interested" } } ] }
```

`status` is `classified` with the category the thread is under now, or `cleared` with `category: null`. Neither says
what the thread was under before; the reads report that.

## Repeating after a lost answer

Filing a thread sets it outright: a second performance under the same category leaves it exactly as the first did,
so `repeat_after_ambiguous` is `safe` and the item is simply handed out again. Nothing is pinned.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from conversation.classify.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the meaning currently on the thread, with who assigned it and when. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
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
| invariants | A2, H1, H2, H3, H4 |

<!-- END GENERATED properties -->
