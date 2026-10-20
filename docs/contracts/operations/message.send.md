# `message.send` — send a person a message, outside any campaign, from one of the provider's accounts

**Version 1.** The machine-readable contract is [`message.send.json`](message.send.json); this page explains it. Where
the two seem to differ, the document is right and this page is a bug.

> **A dispatcher never runs this one on its own.** Its approval is `confirm_each`, so an item naming it is parked at
> the claim for a person to decide: `approval_required`, no attempt, and a record of exactly what would be sent.
> Approving releases it to the next scan, and nothing runs but the subject that was approved. Each message is its own
> decision: one work item is one message to one person.

## What it is for

"Send this message to this person." One message, to the address or the LinkedIn profile the work item consumes, from
one of the provider's accounts, outside any of the provider's campaigns and threads: an email, or on LinkedIn a message
to a connection, an InMail, or a voice message the provider speaks from the text. One document covers every channel, as
the catalogue has it — "one person, one channel per call" — and the channel's own elements are the arguments' branches:
an email and an InMail take a subject, the other two do not, and each format holds its own length. A reply in a thread
that already exists is `conversation.reply`; this one names its recipient and its sender itself.

**A message is never repeated after a lost answer.** `repeat_after_ambiguous` is `never`: the catalogue's reading before
a repeat is "no send recorded for this person … in the window", and no read tells this work item's message from
another one to the same person. An attempt that may have sent and heard nothing back ends for a person, because
guessing wrong is a second message in somebody's inbox.

## Finding the person

`preflight.provider_contact` is `find_only`: the person is found by the pin, or else by the consumed value, and nobody
is created — a person the provider holds nobody for is `contact_not_found`, and is put on a list or enrolled first.
Then the contact is read before anything is sent:

- one the provider holds as opted out fails `suppressed`: the providers mapped first do not refuse a message outside a
  campaign to such a person by themselves, so the read is what keeps a stop the provider holds;
- one reached at another address than the one consumed, or at none, fails `contact_channel_conflict`, and is not
  changed: an address is a channel, and nothing here changes one;
- on LinkedIn, one carrying another profile than the one consumed fails `contact_channel_conflict` and is not changed,
  and one carrying no profile at all is given the consumed one, and only that.

The address and the profile are compared as the runtime keys them for its suppression register, so the register's check
at the claim is a check for exactly the value reached.

## The input you receive

```json
{
  "args": {
    "channel": "email",
    "sender": { "external_id": "12", "address": "ann@example.com" },
    "message": { "format": "email", "subject": "The Lisbon meetup", "body": "Hi Marta,\nthanks for the chat at the meetup." }
  },
  "contacts": [ { "id": "cnt_…", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": {} },
  "idempotency_key": "wi_…"
}
```

- `channel` is `email` or `linkedin`: the address or the profile the message goes to.
- `sender` is an account `sender.list` returned on that channel: its identifier and its address. The address is what
  the person deciding reads, so the plugin holds the account to it before anything is sent: an account the provider
  holds under another address fails `sender_mismatch`. It is null only for an account the provider holds no address for.
- `message.format` is the channel's own: `email` on email; `linkedin_message`, `linkedin_inmail` or `linkedin_voice` on
  LinkedIn. A format of the other channel is refused when the work item is written.
- `message.subject` is one line, and only `email` (at most 998 characters) and `linkedin_inmail` (at most 200) take it;
  both require it.
- `message.body` is plain text, exactly as the person approving it reads it — for `linkedin_voice` the text the provider
  speaks: at most 12000 characters on an email, 8000 in a LinkedIn message, 1900 in an InMail and 1000 in a voice message.
  A plugin whose provider takes an email's body as markup writes the text as markup that shows exactly that text.

## What to return

```json
{
  "items": [
    {
      "contact_id": "cnt_…",
      "status": "sent",
      "channel": "email",
      "format": "email",
      "sender_external_id": "12",
      "message": { "external_id": "<60017.direct@mail.example.com>", "sent_at": null },
      "external_ids": { "contact": "p_88421" }
    }
  ]
}
```

`status` is `sent`: the provider took the message. `message.sent_at` is null unless the provider gives a moment known
to be UTC. A refusal is a failed outcome with its class, never an item.

## When it fails

Every refusal the document declares says nothing was sent, except `provider_answer_lost`. `message_invalid` is a
subject or a text the provider would read as markup of its own — a placeholder it fills in for each person — so what
would go out is not what a person approved. `not_connected` is a LinkedIn message or voice message to a person the
sender's account is not connected with: invite them with `social_invitation.send` first, or send an InMail.
`insufficient_credits` is an InMail with no credits left. `sender_limit_reached` is the account's daily or weekly
allowance spent, which one in the details; the message is proposed again once it renews.

## What a person reads before deciding

> Send marta@example.com a message by email, from the provider's account 12 (ann@example.com), outside any campaign —
> subject: The Lisbon meetup; text: Hi Marta, thanks for the chat at the meetup.

The summary names the person by the value the message consumes, the format, and the account by its identifier and
address, and carries the subject and the whole text; a format without a subject shows a dash there. It is not part of
what is approved: what is approved is the composed input, not the summary. The fixtures
[`fixtures/message.send/preview-*.json`](../fixtures/message.send) show each case.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from message.send.json. Edit the document; this block follows it. -->

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
| contact projection | `id`; channels: `consumed` |
| precondition | The person's contact at the provider is reached at the consumed value — on LinkedIn, it carries no other profile than the one consumed — and the provider does not hold them as opted out; a contact that carries no LinkedIn profile is given the consumed one before a LinkedIn message is sent. |
| preview a person reads | `Send {/contacts/0/channels/0/value} a message by {/args/message/format}, from the provider's account {/args/sender/external_id} ({/args/sender/address}), outside any campaign — subject: {/args/message/subject}; text: {/args/message/body}`; highlighted: `/contacts/0/channels/0/value`, `/args/channel`, `/args/sender`, `/args/message` |
| pinned identifiers | `contact` names a contact |
| recovery read | none |
| after an ambiguous end | `never` — the item ends for a person to look at |
| timeout | 120000 ms |
| invariants | A5, A7, A14, A21, A22, H4, H5 |

<!-- END GENERATED properties -->
