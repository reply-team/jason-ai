# `notification.send` — put a pointer in front of a person

**Version 1.** The machine-readable contract is [`notification.send.json`](notification.send.json); this page explains
it. Where the two seem to differ, the document is right and this page is a bug.

> **A delivery contract, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no
> approval, preflight or repeat rule applies to it. A notification plugin is invoked for it only by the runtime's own
> notifier, when an operator has named that plugin in the `Notifications` section of the settings
> ([docs/notifications.md](../../notifications.md)).

## What it is for

When Jason starts waiting for a person — an approval parked at a claim, or a question a role raised — or a protective
action fails, the person should find out without asking. The runtime's notifier waits for a burst of these to settle and
then sends **one pointer**: how many approvals and questions are waiting, whether protective actions failed, and where
to look. A notification plugin carries that pointer to a service the person reads, such as an ntfy topic on their phone.

What the plugin receives is `title`, `message` and the two counts, `approvals` and `decisions`. The message may also say
that protective actions failed, and name up to three campaigns by public id — an opaque `cmp_…` that names nothing by
itself. Nothing else about the work travels: no campaign's name, no contact, no subject, no question's words, no error.
The person reads what waits in their own session, through their agent or `jason approval list` and
`jason decision list`.

## The input you receive

```json
{
  "title": "Jason",
  "message": "3 approvals and 1 question are waiting in Jason. Ask your agent what Jason is waiting for, or run jason approval list and jason decision list.",
  "counts": { "approvals": 3, "decisions": 1 }
}
```

`message` is one line, ready to send as it stands. `counts` is there for a service that shows numbers its own way.
The invocation's `context` carries the plugin's binding and no campaign, work item or attempt.

## What you answer

Success with `{ "receipt": "…" }` — the service's own identifier for the message, at most 256 characters — or with
`{}` where the service names nothing. A delivery pins no identifier, so an answer carrying `external_ids` is a shape
error, recorded as an ambiguous failure.

A failure is one of the three codes below, under its class. The runtime tries a **transient** failure once more,
thirty seconds later, and never repeats anything else: a pointer that may have arrived is not sent twice.

## What happens to the answer

The runtime records one `notification_sent` journal line per notification, with each channel's outcome:

- **`delivered`** — the plugin succeeded, with its receipt if it gave one;
- **`failed`** — with the code and its class; a server still unreachable after its one retry is `failed`;
- **`not_attempted`** — with a reason decided before any child started: `unavailable` (the plugin is not loaded, or
  cannot run here), `not_declared` (it is not a notification plugin, or declares no version of this contract), or
  `binding_invalid` (the binding in the settings does not satisfy the plugin's own schema).

The toast, the operating system's own notification, has a fourth outcome no plugin can answer, `silenced`: shown while
the person has switched Jason's notifications off. **None of these outcomes is ever a failure of the parked item, the
approval or the decision.** A notification is said about the work; nothing waits on it.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from notification.send.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| version | 1 |
| input | `title`, `message`, `counts` |
| output | `receipt`, or nothing |
| timeout | 15000 ms |

| Failure code | Class | When |
|---|---|---|
| `notification_unreachable` | `transient` | the service did not answer, or answered that it cannot take messages for now |
| `notification_refused` | `permanent` | the service answered that it will not take this message |
| `notification_unauthorized` | `permanent` | the service refused the credential, or needs one and none was granted |

<!-- END GENERATED properties -->

## Conformance

A plugin that performs this contract:

- sends what it is given and nothing more: the title, the message and the counts, never a field of its own making;
- answers `notification_unreachable` for a service that did not answer or is unavailable for now, so the runtime tries
  once more;
- answers `notification_refused` or `notification_unauthorized` for an answer that will not change by trying again;
- returns at most a receipt: the service's own identifier for the message, at most 256 characters;
- keeps its credential out of the binding, out of what it logs and out of what it returns: it reads it with `host.env`.

The fixtures under [`../fixtures/notification.send/`](../fixtures/notification.send/) are run by the repository's
tests against this document, so an example on this page and the contract cannot drift apart.
