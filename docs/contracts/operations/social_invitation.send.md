# `social_invitation.send` — invite a person to connect, from one of the provider's accounts

**Version 1.** The machine-readable contract is [`social_invitation.send.json`](social_invitation.send.json); this
page explains it. Where the two seem to differ, the document is right and this page is a bug.

> **A dispatcher never runs this one on its own.** Its approval is `confirm_each`, so an item naming it is parked at
> the claim for a person to decide: `approval_required`, no attempt, and a record of exactly what would be sent.
> Approving releases it to the next scan, and nothing runs but the subject that was approved. Each invitation is its
> own decision: one work item is one invitation to one person.

## What it is for

"Connect with them first, then talk." One connection request on LinkedIn, to the profile the work item consumes, from
one of the provider's accounts, with a note or without one. The catalogue approves it `confirm_each` and raises it for
the note — "the note is content addressed to a named person" — so the approval is per invitation, and the person
deciding reads the note exactly as it would go.

**An invitation is repeated after a lost answer only under its own key.** `repeat_after_ambiguous` is
`after_recovery_read`: the catalogue's reading before a repeat is the invitation's state for this pair, and the
providers mapped first publish no read of it. The work item's idempotency key is what makes the repeat a read: every attempt makes its send under it, and no other
call of the attempt carries it, so a provider that keeps such keys answers a repeat of a send it already took with its
first answer — the same reference — and sends nothing twice, and a send the provider answered with a failure is never
repeated, since the provider keeps no key for it and a repeat would be a new send. Before the repeat the plugin reads whether the provider keeps keys for the send at all; where it does not,
the repeat fails `send_not_settled` with nothing sent, and the item ends for a person, because guessing wrong is a second
invitation. A repeat that meets the first attempt's send still being finished under the key fails `provider_unavailable`,
transient, and is made again later. The provider keeps the key for a bounded time only. `recovery_read.by_key` says the
repeat rests on the key: the key is the work item's idempotency key, under which every attempt makes its send, and a
lost answer is settled by reading the send an earlier attempt returned, by its reference where one came back, and then
by the same send made again under the key, so the runtime repeats a lost answer only under a plugin whose manifest
lists `social_invitation.send` under `settles_by_key` ([`docs/plugins.md`](../../plugins.md) §3), and under any other it is final.

## Finding the person

`preflight.provider_contact` is `find_or_create`, as for `campaign.enroll`: a pin, once there is one, is worked by;
without one the person is looked up first, created only when the provider holds nobody under the consumed profile, and
the provider's identifier comes back in `external_ids.contact`. An invitation goes to the provider's contact for the
person, so a person Jason holds is not refused for being new to the provider: the contact is made from the consumed
profile and the person's own fields, which come with the call for that — a profile is no name, so a provider that
requires one is given what Jason holds, as every operation that may create the person projects it.
[`docs/plugins.md`](../../plugins.md) §7, "The contact proxy rule", says how the lookup compares. The policy was
`find_only` before, when a person the provider held nobody for failed `contact_not_found`; that is a relaxation, so the
document keeps its version (the contracts [README](../README.md), "Versioning"). Because the composed input now carries
the person's own fields, an approval parked before this change is parked once more, as `input_changed`, when its work
item is next claimed. A person created for an invitation the provider then refuses — a sender's spent allowance, say —
stays created there, and the failure carries their identifier; nothing was sent.

A contact the provider already held is read before anything is sent:

- one the provider holds as opted out fails `suppressed`: the provider does not refuse an invitation to such a person by
  itself, so the read is what keeps a stop the provider holds;
- one carrying another LinkedIn profile than the one consumed fails `contact_channel_conflict`, and is not changed;
- one carrying no profile at all is given the consumed one, and only that: a profile is filled where it is missing,
  never changed.

The profile is compared as the runtime keys it for its suppression register, so the register's check at the claim is a
check for exactly the profile reached.

## The input you receive

```json
{
  "args": {
    "channel": "linkedin",
    "sender": { "external_id": "34", "address": "https://www.linkedin.com/in/ann-owner" },
    "note": "Hi Marta, we met at the Lisbon meetup.",
    "send_after": "2026-11-03T14:00:00Z"
  },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "last_name": "Alvarez", "company": null, "title": null, "time_zone": null, "channels": [ { "channel": "linkedin", "value": "https://www.linkedin.com/in/marta-example", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": {} },
  "idempotency_key": "wi_…"
}
```

- `channel` is `linkedin`: the profile the invitation goes to.
- `sender` is an account `sender.list` returned: its identifier and its address. The address is what the person
  deciding reads, so the plugin holds the account to it before anything is sent: an account the provider holds under
  another address fails `sender_mismatch`. It is null only for an account the provider holds no address for.
- `note` is the note, or null for an invitation without one; the note is at most 200 characters.

`send_after` is optional: the moment the invitation should go out after, with its offset — a moment without one names no
moment anywhere in particular and is refused when the work item is written. It is written as `YYYY-MM-DDTHH:mm:ss`, with
a fraction of one to three digits where it names one, then `Z` or an offset `+hh:mm` or `-hh:mm`, with `T` and `Z` in
capitals. ECMA-262's own form fixes the fraction at exactly three digits; the pattern also takes one or two, which Jint,
the engine a plugin runs in, reads the same way. Any other, a leap second among them, is refused when the work item is
written, and a plugin given a moment it cannot read fails final with nothing sent,
never as a moment already past. It is an argument a provider may offer on
some channels and not on others (`channel_arguments`), so a plugin takes it only on the channels its manifest lists,
and the claim refuses it, `argument_unsupported`, on any other: such an invitation goes at that moment or not at all, and never
now instead. A plugin whose provider turns out not to offer it, read before anything is sent, fails
`capability_not_offered` naming `send_after`. A moment already past is no moment: the invitation goes as one that names none.

## What to return

```json
{
  "items": [
    {
      "contact_id": "cnt_…",
      "status": "accepted",
      "accepted_for": "2026-11-03T14:00:00Z",
      "reference": "linkedin-2002",
      "cancelable": true,
      "sender_external_id": "34",
      "invitation": { "external_id": null, "sent_at": null },
      "external_ids": { "contact": "p_88421", "send": "linkedin-2002" }
    }
  ]
}
```

`status` is `sent` where the provider sent the invitation inside the call, and `accepted` where it took it, to send
as soon as it can or at the moment `send_after` names: whether and when it went is learned later, and never by this work
item. Either one is the item done. It is `already_connected` when the person is a connection of the account already and
nothing was sent. `accepted_for`, `reference` and `cancelable` may be absent, and read as null, null and false: a plugin
written before them answers `sent` without them, and stays valid. Either `sent` or `accepted` may carry each of
them, where the provider gave it. `accepted_for` is the moment the provider took it to send at, in UTC, or null where it took it to send as soon as it
can, or nothing was sent. `reference` is the provider's reference for the send it took, by which it reads and cancels it, or null where it
gives none — always when nothing was sent; it comes back under `external_ids.send` too, the one value in both places, and a result that says one
without the other, or two that differ, is `result_invalid`. The reference pins no contact and no campaign: one work item
is one send, so it is kept on the attempt that learned it and in the item's result. `cancelable` says whether the
provider holds the send waiting for its moment, so it can still be cancelled by its reference. `invitation.sent_at` is null unless the provider gives a moment known to be UTC — always for
an invitation taken for a later moment. A refusal is a failed outcome with its class, never an item.

## When it fails

Every refusal the document declares says nothing was sent, except `provider_answer_lost`, and a `provider_call_failed`
whose message says the provider answered the send it took with a failure, where the plugin's row says that whether
it went is a person's to find out at the provider. `send_not_settled` is a
repeat the plugin could not make a read: nothing was sent by it, and a person looks at whether the first one went.
`message_invalid` is a note the
provider would read as markup of its own — a placeholder it fills in for each person — so what would go out is not what
a person approved. `sender_limit_reached` is the account's daily or weekly allowance spent, which one in the details;
the invitation is proposed again once it renews. `limit_reached` is the provider's account full: it will hold no further
contact, and the person had to be created.

`sender_unavailable`, `sender_limit_reached` and `unauthorized` stand for an answer the plugin cannot read as a provider
block. Where it can — the provider refused for a block and did nothing — the code is `provider_blocked`, with the block
that says what is blocked, why and where a person clears it ([the provider block](../block/provider_block.md)).

## What a person reads before deciding

> Invite https://www.linkedin.com/in/marta-example to connect on LinkedIn, from the provider's account 34
> (https://www.linkedin.com/in/ann-owner), with the note: Hi Marta, we met at the Lisbon meetup.; send after:
> 2026-11-03T14:00:00Z

The summary names the person by the profile the invitation consumes and the account by its identifier and address, and
carries the whole note and the moment as the arguments name it; without a note, or without a moment, it shows a dash
there. It is not part of what is approved: the subject carries the
composed input, not the summary. The fixtures [`fixtures/social_invitation.send/preview-*.json`](../fixtures/social_invitation.send)
show each case.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from social_invitation.send.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `act` | — |
| `reversibility` | `compensatable` | — |
| `approval` | `confirm_each` | — |
| `protective_floor` | `false` | raised: the note is content addressed to a named person. |
| `before_repeating` | the invitation state for this pair: none / pending / accepted / withdrawn / declined. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `metered` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_or_create` |
| arguments a plugin takes per channel | `send_after`, only on a channel the routed plugin's manifest lists it for |
| in a paused campaign | `waits` |
| under a provider block | `waits` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | The person's contact at the provider carries no other LinkedIn profile than the one consumed, and the provider does not hold them as opted out; a contact that carries no profile is given the consumed one before anything is sent. |
| preview a person reads | `Invite {/contacts/0/channels/0/value} to connect on LinkedIn, from the provider's account {/args/sender/external_id} ({/args/sender/address}), with the note: {/args/note}; send after: {/args/send_after}`; highlighted: `/contacts/0/channels/0/value`, `/args/sender`, `/args/note`, `/args/send_after` |
| pinned identifiers | `contact` names a contact; `send` names a work_item |
| recovery read | The provider's record of the send under this work item's idempotency key, read by making the same send again under the same key: the provider answers a send it already took with what it answered then — the same reference — and sends nothing twice, and a send the provider answered with a failure is never repeated, since the provider keeps no key for it and a repeat would be a new send. Before the repeat the plugin reads whether the provider keeps such keys at all; where it does not, the repeat fails `send_not_settled` and nothing is sent. |
| after an ambiguous end | `after_recovery_read`, by the work item's key — a later attempt performs the recovery read above and answers from it when the effect already happened, and only under a plugin whose manifest lists the operation under `settles_by_key` |
| timeout | 120000 ms |
| invariants | none names this operation |

<!-- END GENERATED properties -->
