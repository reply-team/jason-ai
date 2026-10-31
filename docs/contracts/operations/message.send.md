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

**A message is repeated after a lost answer only under its own key.** `repeat_after_ambiguous` is
`after_recovery_read`: the catalogue's reading before a repeat is "no send recorded for this person … in the window",
and no read of a thread tells this work item's message from another one to the same person. The work item's idempotency key is what makes the repeat a read: every attempt makes its send under it, and no other
call of the attempt carries it, so a provider that keeps such keys answers a repeat of a send it already took with its
first answer — the same reference — and sends nothing twice, and a send the provider answered with a failure is never
repeated, since the provider keeps no key for it and a repeat would be a new send. Before the repeat the plugin reads whether the provider keeps keys for the send at all; where it does not,
the repeat fails `send_not_settled` with nothing sent, and the item ends for a person, because guessing wrong is a second
message in somebody's inbox. A repeat that meets the first attempt's send still being finished under the key fails `provider_unavailable`,
transient, and is made again later. The provider keeps the key for a bounded time only. `recovery_read.by_key` says the
repeat rests on the key: the key is the work item's idempotency key, under which every attempt makes its send, and a
lost answer is settled by reading the send an earlier attempt returned, by its reference where one came back, and then
by the same send made again under the key, so the runtime repeats a lost answer only under a plugin whose manifest
lists `message.send` under `settles_by_key` ([`docs/plugins.md`](../../plugins.md) §3), and under any other it is final.

## Finding the person

`preflight.provider_contact` is `find_or_create`, as for `campaign.enroll`: a pin, once there is one, is worked by;
without one the person is looked up first, created only when the provider holds nobody under the consumed value, and
the provider's identifier comes back in `external_ids.contact`. A message goes to the provider's contact for the
person, so a person Jason holds is not refused for being new to the provider: the contact is made from the consumed
value and the person's own fields, which come with the call for that: the call projects the person as every operation
that may create them does. [`docs/plugins.md`](../../plugins.md) §7, "The contact proxy rule", says how the lookup
compares. The policy was `find_only` before, when a person the provider held nobody for failed `contact_not_found`;
that is a relaxation, so the document keeps its version (the contracts [README](../README.md), "Versioning"). Because
the composed input now carries the person's own fields, an approval parked before this change is parked once more, as
`input_changed`, when its work item is next claimed. A person created for a message the provider then refuses — a
sender's spent allowance, say — stays created there, and the failure carries their identifier; nothing was sent.

A contact the provider already held is read before anything is sent:

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
    "message": { "format": "email", "subject": "The Lisbon meetup", "body": "Hi Marta,\nthanks for the chat at the meetup." },
    "send_after": "2026-11-03T09:00:00-05:00"
  },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "last_name": "Alvarez", "company": null, "title": null, "time_zone": null, "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
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
- `message.compose` is `text`, or absent, for a message that is final: it goes out exactly as written. It is `template`
  for a message the provider fills in for the person — an email, a LinkedIn message or an InMail; a voice message is
  always final. The subject, where the format takes one, and the text are then written in
  [the template standard](../README.md#the-template-standard), refused when the work item is written if they are not,
  and the plugin writes them in its provider's own syntax, construct by construct and name by name. `sender.*` is the
  sending account's identity at the provider, not the `sender.identity` preference. The provider renders a value it
  fills in as it renders that value anywhere else, so whatever it makes of markup in a field, it makes of it in the
  message. The text as the provider filled it in is read afterwards from `message.status`, as its `rendered_subject`
  and `rendered_body`, where the provider gives it. The bounds hold a template as written, so a subject that renders empty for a person, as
  `{{#if contact.company}}{{contact.company}}{{/if}}` for a person with no company, passes them: it is the example's to
  show and the provider's to refuse or send. A plugin receives a template only on a channel its manifest lists under
  `templates`; on any other channel the claim refuses it, `template_not_offered`, before a child exists. Until Jason
  0.22.0 only an email could be a template. The Reply package
  refuses any `{{` in a final message, as it did before 0.10.0; from 0.10.0 it writes a template in Reply's syntax, and
  refuses before anything is called what it cannot write there.

`send_after` is optional: the moment the message should go out after, with its offset — a moment without one names no
moment anywhere in particular and is refused when the work item is written. It is written as `YYYY-MM-DDTHH:mm:ss`, with
a fraction of one to three digits where it names one, then `Z` or an offset `+hh:mm` or `-hh:mm`, with `T` and `Z` in
capitals. ECMA-262's own form fixes the fraction at exactly three digits; the pattern also takes one or two, which Jint,
the engine a plugin runs in, reads the same way. Any other, a leap second among them, is refused when the work item is
written, and a plugin given a moment it cannot read fails final with nothing sent,
never as a moment already past. It is an argument a provider may offer on
some channels and not on others (`channel_arguments`), so a plugin takes it only on the channels its manifest lists,
and the claim refuses it, `argument_unsupported`, on any other: such a message goes at that moment or not at all, and never
now instead. A plugin whose provider turns out not to offer it, read before anything is sent, fails
`capability_not_offered` naming `send_after`. A moment already past is no moment: the message goes as one that names none.

## What to return

```json
{
  "items": [
    {
      "contact_id": "cnt_…",
      "status": "accepted",
      "accepted_for": "2026-11-03T14:00:00Z",
      "reference": "email-1002",
      "cancelable": true,
      "channel": "email",
      "format": "email",
      "sender_external_id": "12",
      "message": { "external_id": null, "sent_at": null },
      "external_ids": { "contact": "p_88421", "send": "email-1002" }
    }
  ]
}
```

`status` is `sent` where the provider sent the message inside the call, and `accepted` where it took it, to send as soon
as it can, at the moment `send_after` names, or at a time of its own choosing: whether and when it went is learned
later, and never by this work item. Either one is the item done. `accepted_for`, `reference` and `cancelable` may be
absent, and read as null, null and false: a plugin written before them answers `sent` without them, and stays valid.
Either status may carry each of them, where the provider gave it. `accepted_for` is the moment the provider took it to
send at, in UTC, or null where it named none: it sends as soon as it can, or holds the message for a time of its own
choosing that it only estimates, which `cancelable` true tells from a send on its way. `reference` is the provider's
reference for the send it took, by which it reads and cancels it, or null where it gives none; it comes back under
`external_ids.send` too, the one value in both places, and a result that says one without the other, or two that differ,
is `result_invalid`. The reference pins no contact and no campaign: one work item is one send, so it is kept on the
attempt that learned it and in the item's result. `cancelable` says whether the provider holds the send waiting for its
moment, so it can still be cancelled by its reference. `message.sent_at` is null unless the provider gives a moment
known to be UTC — always for a message taken for a later moment. A refusal is a failed outcome with its class, never an
item.

## When it fails

Every refusal the document declares says nothing was sent, except `provider_answer_lost`, and a `provider_call_failed`
whose message says the provider answered the send it took with a failure, where the plugin's row says that whether
it went is a person's to find out at the provider. `send_not_settled` is a
repeat the plugin could not make a read: nothing was sent by it, and a person looks at whether the first one went.
`message_invalid` is a final subject or text the provider would read as markup of its own — a placeholder it fills in for each person — so what
would go out is not what a person approved. `template_unsupported` is a template the plugin cannot write in its
provider's syntax — a construct its reading of the standard refuses, a custom field under a key the provider keeps
for a field of its own, or a name the provider has no field for on this channel — and `template_unrendered` one the
provider could not fill in for this person, a name with no value and no fallback among the causes, the provider's words
in the message. A send the provider does not make within the call — held for a later moment, or queued by the provider
behind the account's other sends — is filled in when it goes, and a template it cannot fill fails it then, which
`message.status` shows as `failed` with the provider's reason — not `template_unrendered` at the send. `not_connected` is a LinkedIn message or
voice message to a person the sender's account is not connected with: invite them with `social_invitation.send` first,
or send an InMail.
`insufficient_credits` is an InMail with no credits left. `sender_limit_reached` is the account's daily or weekly
allowance spent, which one in the details; the message is proposed again once it renews. `limit_reached` is the
provider's account full: it will hold no further contact, and the person had to be created.

`sender_unavailable`, `sender_limit_reached`, `insufficient_credits` and `unauthorized` stand for an answer the plugin
cannot read as a provider block. Where it can — the provider refused for a block and did nothing — the code is
`provider_blocked`, with the block that says what is blocked, why and where a person clears it
([the provider block](../block/provider_block.md)).

## What a person reads before deciding

> Send marta@example.com a message by email, from the provider's account 12 (ann@example.com), outside any campaign —
> subject: The Lisbon meetup; text: Hi Marta, thanks for the chat at the meetup.; send after: 2026-11-03T09:00:00-05:00

The summary names the person by the value the message consumes, the format, and the account by its identifier and
address, and carries the subject, the whole text and the moment as the arguments name it; a format without a subject,
or a message without a moment, shows a dash there. It is not part of
what is approved: what is approved is the composed input, not the summary. A template is carried as written, and the
preview adds one `example` of it ([the preview a person reads](../README.md#the-preview-a-person-reads)): the subject
and the text rendered for this person from the composed input, with the names the input does not hold — a custom
field, the sending account's names, and on LinkedIn the person's email address — left for the provider, shown as their
fallback or as `[name]`, and listed under `unfilled`. The fixtures
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
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_or_create` |
| arguments a plugin takes per channel | `send_after`, only on a channel the routed plugin's manifest lists it for |
| channels its message may be a template on | `email`, `linkedin`, each only where the routed plugin's manifest lists it under `templates` |
| in a paused campaign | `waits` |
| under a provider block | `waits` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | The person's contact at the provider is reached at the consumed value — on LinkedIn, it carries no other profile than the one consumed — and the provider does not hold them as opted out; a contact that carries no LinkedIn profile is given the consumed one before a LinkedIn message is sent. |
| preview a person reads | `Send {/contacts/0/channels/0/value} a message by {/args/message/format}, from the provider's account {/args/sender/external_id} ({/args/sender/address}), outside any campaign — subject: {/args/message/subject}; text: {/args/message/body}; send after: {/args/send_after}`; highlighted: `/contacts/0/channels/0/value`, `/args/channel`, `/args/sender`, `/args/message`, `/args/message/compose`, `/args/send_after` |
| pinned identifiers | `contact` names a contact; `send` names a work_item |
| recovery read | The provider's record of the send under this work item's idempotency key, read by making the same send again under the same key: the provider answers a send it already took with what it answered then — the same reference — and sends nothing twice, and a send the provider answered with a failure is never repeated, since the provider keeps no key for it and a repeat would be a new send. Before the repeat the plugin reads whether the provider keeps such keys at all; where it does not, the repeat fails `send_not_settled` and nothing is sent. |
| after an ambiguous end | `after_recovery_read`, by the work item's key — a later attempt performs the recovery read above and answers from it when the effect already happened, and only under a plugin whose manifest lists the operation under `settles_by_key` |
| timeout | 120000 ms |
| invariants | A5, A7, A14, A21, A22, H4, H5 |

<!-- END GENERATED properties -->
