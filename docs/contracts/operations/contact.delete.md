# `contact.delete` — delete the provider's contact for one person

**Version 1.** The machine-readable contract is [`contact.delete.json`](contact.delete.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, parked for a person at the claim, and — once a person approves it — performed against a
> real provider account. It reaches a plugin only where a route sends it: without one the item fails at the claim with
> `no_route`.

## What it is for

"Take this person out of the working database." The provider's contact for the person is deleted, with the history the
provider keeps for them: they leave every campaign and every list there. It cannot be taken back, and it destroys
working history other operations key on, so a person approves it — the catalogue raises it to `confirm_once` for that
reason — and its work waits with its Jason campaign when that campaign is paused.

**A delete reaches the provider's record and nothing of Jason's.** Jason's contact for the person, their channels, the
identifier Jason pinned for them and the do-not-contact register stay exactly as they were. Deleting a record is not
erasing a person and never lifts an exclusion (B12): somebody Jason holds a stop for is still stopped. The claim asks the
register about the value the delete consumes (`preflight.suppression: checked`), and the delete of a person whose
consumed value Jason holds a stop for is refused there, as `suppressed`: the provider's opt-out flag lives on the
provider's contact, so deleting a stopped person's contact would erase the record of their stop, and a later import
could make them reachable again. The pin stays too, and names nobody afterwards: a later operation about the person
works by it and fails `contact_not_found` — a stop answers `already_stopped` — rather than finding them by address and
creating them again.

The provider's own stop is the other half. A person the provider holds as opted out is refused with
`contact_opted_out`, and nothing is deleted: at a provider whose stop lives on the contact, the contact is its only
record of that stop.

## Finding the person

A delete works by the pin alone: `preflight.provider_contact` is `pinned_only`. Nobody is matched by an address to be
destroyed.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Read the contact
   by it, then delete it by it.
2. Otherwise fail `contact_not_linked` before the provider is asked anything.

The runtime decides the second path first, at the claim and before anybody is asked to approve: a delete for somebody
the routed plugin has pinned nobody for fails `contact_not_linked` there, so nobody approves a delete that cannot run.
A plugin handed such an input anyway still refuses it the same way.

## The input you receive

```json
{
  "args": { "channel": "email" },
  "contacts": [ { "id": "cnt_…", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

The arguments name the channel and nothing else, so no identifier in them can point the delete at a contact the pin
does not name. The contact carries its identifier, the one channel and the pin, which is all a delete needs.

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "deleted" } ] }
```

`status` is `deleted`, or `already_deleted` when the contact was already gone: on any attempt after the first, since an
earlier attempt may have deleted it, or when the provider answers the delete as missing right after the read found it. The answer pins nothing — no
`external_ids` beside it — because the identifier it worked by names nobody afterwards. A failure carries none either.

## Repeating after a lost answer

A delete whose answer was lost may already have deleted. Every attempt after the first reads the contact by the pin
before it deletes anything: a contact the provider no longer holds answers `already_deleted`, because an earlier attempt
may be the one that deleted it and nothing tells the plugin which earlier attempts reached the delete; one it still
holds is checked for the stop again, then deleted. On a first attempt the same missing contact is `contact_not_found`:
nothing says this call is the one that removed it.

## What a person reads before deciding

The runtime renders this summary from the composed input when it parks the work for a person:

> Delete the provider's contact p_88421 for marta@example.com, with the history the provider keeps for them: they
> leave every campaign and list there. Jason's own record of the person and its do-not-contact register stay as they
> are.

It names the person by the value the delete consumes, and highlights that value and the provider's identifier, so
whoever decides sees which contact goes. With nobody pinned the identifier renders as a dash, though the claim refuses
such a delete before it is parked, so nobody is shown that preview to decide on. The fixtures
[`fixtures/contact.delete/preview-*.json`](../fixtures/contact.delete) show each case.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from contact.delete.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `irreversible` | conditional — compensatable inside the account's restore window; irreversible after it. |
| `approval` | `confirm_once` | — |
| `protective_floor` | `false` | raised: it destroys working history other operations key on. |
| `before_repeating` | the record's presence and its restore eligibility | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `pinned_only` |
| in a paused campaign | `waits` |
| under a provider block | `runs` |
| contact projection | `id`; channels: `consumed` |
| precondition | The provider does not hold the person as opted out, because deleting them would erase its only record of that stop. |
| preview a person reads | `Delete the provider's contact {/contacts/0/external_ids/contact} for {/contacts/0/channels/0/value}, with the history the provider keeps for them: they leave every campaign and list there. Jason's own record of the person and its do-not-contact register stay as they are.`; highlighted: `/contacts/0/channels/0/value`, `/contacts/0/external_ids/contact` |
| pinned identifiers | none — this operation pins nothing |
| recovery read | Whether the provider still holds a contact under the pin, and whether it holds them as opted out: a contact it no longer holds is the delete already done. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 60000 ms |
| invariants | B12 |

<!-- END GENERATED properties -->
