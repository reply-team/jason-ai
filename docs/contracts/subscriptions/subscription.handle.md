# `subscription.handle` — take one journal line after the runtime wrote it

**Version 1.** The machine-readable contract is [`subscription.handle.json`](subscription.handle.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **A subscription contract, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/), the delivery contract under [`../notifications/`](../notifications/) and the
> events contract under [`../events/`](../events/), and never among them: no work item names it, no route carries it,
> and no approval, preflight or repeat rule applies to it. A plugin of any kind is invoked for it only by the runtime's
> own subscription runner, for a journal kind its manifest declares under `subscriptions` and the person granted.
> How a plugin subscribes, and in what order the subscribers of one kind run, is in
> [`docs/plugins.md`](../../plugins.md).

## What it is for

A subscriber reacts to what Jason did, after Jason did it: a work item that failed, an approval asked for, a contact
added. For each line of a kind it subscribes to, the runtime hands the plugin that line once it is written, in the
journal's order, one line of a kind at a time. The subscribers of one kind form a chain, in the order the settings
give under `Subscriptions:Order:<kind>`: each handler receives the envelope the one before it returned, and the first
receives an empty object. Nothing a handler answers changes the line or what it records; the runtime never reads
inside the envelope.

## The input you receive

```json
{
  "line": {
    "id": "jrn_42",
    "ts": "2026-10-16T09:00:00.000Z",
    "actor": { "type": "system", "id": "runtime" },
    "kind": "workitem_failed",
    "campaign_id": "cmp_1",
    "work_item_id": "wi_9",
    "attempt_id": "att_3",
    "key": "att_3",
    "old": null,
    "new": { "code": "provider_unreachable", "message": "The provider did not answer.", "retriable": false, "class": "transient" },
    "reason": null,
    "contact_id": "cnt_5",
    "campaign_name": "Spring",
    "contact_name": "Okafor",
    "work_item_kind": "provider_op",
    "work_item_role": null,
    "work_item_operation": "campaign.enroll",
    "actor_role": null
  },
  "envelope": {},
  "attempt": 1
}
```

`line` is the journal line exactly as `journal.list` returns it, the names beside the identifiers included. It is
read when the handler is invoked, so a line handed out again carries the names as they stand then. The operation word the handler is invoked under is the line's `kind`, so one `main.js` tells the kinds it
subscribes to apart as a provider tells its operations apart. `attempt` counts how many times this plugin has been
handed this line, this time included.

## What you answer

Success with `{ "envelope": { … } }` — an object, the empty object included, handed as it stands to the next handler,
and kept with the chain's run when this handler is the last. Nothing else stands beside it, and a handler pins no
identifier, so an answer carrying `external_ids` is refused. An envelope longer, as compact JSON in UTF-8, than the
bound `Subscriptions:EnvelopeBytes` sets is refused too, against the handler that returned it.

A failure carries a code of the handler's own under one of the five classes. The runtime tries a **transient** failure
again, by the same handler with the envelope as it stood before it, after a wait that doubles each time, up to the
number `Subscriptions:TransientRetries` sets. Every other class — `permanent`, `validation`, `blocked`, `ambiguous` —
ends the chain at that handler, for that line: the handlers after it are not invoked.

## What happens to the answer

A chain that ends well writes no journal line. A chain that ends at a failure writes one `subscription_failed` line,
naming the kind, the line, the plugin, the class, the code and the attempts — never the envelope or the failure's
message — and the runtime moves on to the next line of the kind. A plugin that cannot run at all on this machine is not
a failure of the line: the kind waits at that line until a reload makes the plugin available or takes it out of the
chain.

A line may be handed out again — after the runtime restarts in the middle of a chain — with `attempt` above 1, so a
handler takes each line **at least once**, and an effect it causes is one it can tell it already caused.

## The properties


<!-- BEGIN GENERATED properties -->
<!-- Rendered from subscription.handle.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| version | 1 |
| input | `line`, `envelope`, `attempt` |
| output | `envelope` |
| timeout | 3600000 ms, lowered to the plugin's own limit |

| Failure code | Class | When |
|---|---|---|
| `subscription_answer_invalid` | `validation` | the runtime's record of an answer outside this contract: no envelope, an envelope that is not an object, a field beside it, or an identifier pinned |
| `subscription_envelope_too_large` | `validation` | the runtime's record of an envelope longer, as compact JSON in UTF-8, than the bound Subscriptions:EnvelopeBytes sets |

<!-- END GENERATED properties -->

## Conformance

A plugin that performs this contract:

- is invoked under the line's kind as its operation word, so one `main.js` tells the kinds it subscribes to apart as a
  provider tells its operations apart;
- answers `{"envelope": {...}}` and nothing else: an object, the empty object included, handed as it stands to the next
  handler; the runtime never reads inside it;
- pins nothing: an answer with `external_ids` is refused as `subscription_answer_invalid`;
- fails with codes of its own under one of the five classes; only `transient` is tried again, by the same handler with
  the envelope as it stood before it, and every other class ends the chain at that handler for that line;
- takes a line at least once: a line may be handed out again after a restart, with `attempt` above 1, so an effect a
  handler causes is one it can tell it already caused;
- reads and writes Jason only through `host.call`, by the families of the Runtime API its grant names; what it writes
  is not handed to another handler as a new line.

The fixtures under [`../fixtures/subscription.handle/`](../fixtures/subscription.handle/) are run by the repository's
tests against this document, so an example on this page and the contract cannot drift apart.
