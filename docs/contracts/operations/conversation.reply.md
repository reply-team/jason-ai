# `conversation.reply` — answer the person on a thread, in that thread

**Version 1.** The machine-readable contract is [`conversation.reply.json`](conversation.reply.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **A dispatcher never runs this one on its own.** Its approval is `confirm_each`, so an item naming it is parked at
> the claim for a person to decide: `approval_required`, no attempt, and a record of exactly what would be sent.
> Approving releases it to the next scan, and nothing runs but the subject that was approved. Each reply is its own
> decision: one work item is one reply to one person.

## What it is for

"Answer the person who wrote, in the thread they wrote in." It is the level-one catalogue's one send verb, `message.send`, restricted
to a send bound to a thread that already exists: the thread fixes who is reached and the identity that answers, so
the call names neither a recipient nor a sender, and nobody else can be put on it — no copy, no blind copy. A message
to somebody outside a thread is a different operation.

**A reply is never repeated after a lost answer.** `repeat_after_ambiguous` is `never`: an attempt that may have sent
and heard nothing back ends for a person, because no read tells this work item's reply from another one sent to the
same person in the meantime, and guessing wrong is a second message in somebody's inbox.

## Finding the person through the thread

The work item names a person, and the thread names one too; they have to be the same person, or nothing is sent.
`preflight.provider_contact` is `find_only`: the person is found through the thread, and nobody is created. Read the
thread first:

- when `contacts[0].external_ids.contact` is present, the thread's person must be the one the provider holds under it;
- and in every case the thread's address on the channel named by `args.channel` — the email address, the LinkedIn
  profile — must be that channel's value under the key the runtime gives a value for its suppression register: an
  email address without regard to case; a profile link by the profile's name after `/in/`, decoded once, composed and
  lowercased, whatever host of linkedin.com's, scheme, slashes, query or fragment it carries — so
  `uk.linkedin.com/in/Jane/` and `https://www.linkedin.com/in/jane` are one person, and a link the runtime keys as no
  profile is nobody. That is what makes the register's check, made at the claim for that value, a check for exactly the
  person reached.

A thread on another channel fails `channel_mismatch`, a thread of somebody else `thread_contact_mismatch`, both
before anything is sent. Nobody is created at the provider: the person is found through the thread, and their
identifier is returned under `external_ids.contact` — on a failure too, once the thread's person was found to be
this one, so an attempt that lost its answer leaves a trace of who it was about. A thread of somebody else pins
nothing: its person is not this one.

## The input you receive

```json
{
  "args": {
    "channel": "email",
    "thread": { "external_id": "t_99812" },
    "body": "Thursday at 10 works. I will send an invitation.",
    "reply_to_message": { "external_id": "<m99812@mail.example.com>" }
  },
  "contacts": [ { "id": "cnt_…", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

- `channel` is `email` or `linkedin`: the contact's channel the reply consumes, and the one the thread has to be on.
- `body` is the text, exactly as the person approving it read it: at most 12000 characters on email, the most
  `conversation.get` reads back of one message, and at most 8000 on LinkedIn, the most a LinkedIn message holds. A text
  of nothing but white space is refused when the work item is written, before anybody is asked about an empty reply.
- `reply_to_message` names one message of an email thread to answer, by the identifier `conversation.get` returned;
  without it the reply answers the thread's latest message. A LinkedIn reply cannot name one, and is refused before
  your code runs if it does.

## What to return

```json
{
  "items": [
    {
      "contact_id": "cnt_…",
      "status": "sent",
      "thread_external_id": "t_99812",
      "channel": "email",
      "message": { "external_id": "<r1@mail.example.com>", "sent_at": "2026-10-03T08:02:11.000Z" },
      "external_ids": { "contact": "p_88421" }
    }
  ]
}
```

`message.external_id` and `message.sent_at` are each the provider's, or null when it gives none. A refusal — before
sending or after — is a failed outcome with its class, never an item.

## What a reply leaves behind

Whether a provider's list moves a thread for a reply Jason sends is the provider's matter, and its plugin's README says
which. Where it does, inbound sync says it as a reply observed once: a provider's list of threads need not say who wrote
a thread's latest message, so the thread a reply went into is listed as one that moved, and the runtime's inbound sync
writes its `inbound_reply_observed` line for it, once, as it would for an answer. What the thread holds is read before
anything is decided about it, and a thread whose latest message is the reply this operation sent is nothing to answer.
Where it does not, no line comes for Jason's own reply, and nothing wakes the manager for it.

## When it fails

Every refusal the document declares says nothing was sent, except one: `provider_answer_lost`, where the provider was
called and no answer came back. `delivery_refused` is the provider taking the reply and saying it could not deliver
it; its own reason belongs in the details. `message_invalid` is text the provider would read as markup of its own — a
placeholder it fills in for each person — so that what would go out is not what a person approved.

## What a person reads before deciding

> Reply in the provider's thread t_99812 to marta@example.com by email, from the account that owns the thread and to
> nobody else: Thursday at 10 works. I will send an invitation.

The summary names the person by the address the reply consumes, and carries the whole text; a line break inside it
renders as a space, so the summary is always one line. It is not part of what is approved: the subject carries the
composed input, not the summary. The fixtures [`fixtures/conversation.reply/preview-*.json`](../fixtures/conversation.reply)
show each case.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from conversation.reply.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `act` | — |
| `reversibility` | `irreversible` | — |
| `approval` | `confirm_each` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | read first: no send recorded for this person on this thread or step in the window — a timed-out send may already have succeeded. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `metered` | conditional — metered against the message allowance on channels that meter sending; `none` on channels that do not. |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_only` |
| in a paused campaign | `waits` |
| under a provider block | `waits` |
| contact projection | `id`; channels: `consumed` |
| precondition | The thread's person is this person: the one pinned, where there is a pin, and in every case the one whose address on the channel named by `args.channel` is that channel's value as the runtime keys it for its suppression register; otherwise nothing is sent. |
| preview a person reads | `Reply in the provider's thread {/args/thread/external_id} to {/contacts/0/channels/0/value} by {/args/channel}, from the account that owns the thread and to nobody else: {/args/body}`; highlighted: `/contacts/0/channels/0/value`, `/args/channel`, `/args/thread`, `/args/reply_to_message`, `/args/body` |
| pinned identifiers | `contact` names a contact |
| recovery read | none |
| after an ambiguous end | `never` — the item ends for a person to look at |
| timeout | 120000 ms |
| invariants | A5, A7, A14, A21, A22, H4, H5 |

<!-- END GENERATED properties -->
