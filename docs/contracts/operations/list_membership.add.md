# `list_membership.add` — put one person on a provider list

**Version 1.** The machine-readable contract is [`list_membership.add.json`](list_membership.add.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument
> schema below when it is written, routed to a plugin at the claim, and performed against a real provider account
> — a contact may be created and a list may be changed. It reaches a plugin only where a route sends it: without
> one the item fails at the claim with `no_route`.

## What it is for

"Put these people on this list." Jason has no list entity of its own, so the list is the provider's and the planner
names it. The operation controls state at the provider, it is reversible, and it needs no approval — but it takes
an idempotency key, because it writes, and because the contact it may have to create is the kind of thing that
gets billed.

## Finding the person

A list holds the provider's contacts, not Jason's, so before adding anyone the plugin finds the provider's own contact
for the person: `preflight.provider_contact` is `find_or_create`.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Work by it.
   Do not look the person up by address again — a pin exists precisely so that the answer stops depending on how
   the address is written.
2. Otherwise look the person up by the value of the channel named by `args.channel`, and create the contact from that
   value only when the provider holds nobody under it. Return the identifier in `external_ids.contact` either way; the
   runtime pins it, and the next call takes path 1.

[`docs/plugins.md`](../../plugins.md) §7, "The contact proxy rule", says how the lookup compares and when it is refused.

## The input you receive

```json
{
  "args": { "list": { "external_id": "L-1129" }, "channel": "email" },
  "contacts": [
    {
      "id": "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
      "first_name": "Marta", "last_name": "Alvarez",
      "company": "Puerto Analytics", "title": "Head of Growth", "time_zone": "America/Bogota",
      "channels": [{ "channel": "email", "value": "marta@example.com", "primary": true }],
      "external_ids": { "contact": "p_88421" }
    }
  ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRF"
}
```

`contacts` carries exactly one person, in the collection shape the vendor-neutral contract uses: one work item
addresses one contact in this version, and lifting that later needs no change of shape. The contact carries only
the fields the operation consumes and **only the one channel named by `args.channel`** — not the person's whole
reachability. `external_ids` holds this plugin's own pins and nobody else's.

`idempotency_key` is the work item's own identifier. It is stable across every attempt, which is what makes a
ledger under this key answerable.

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "added", "external_ids": { "contact": "p_88421" } } ] }
```

One item per person, with the reason — a partial success reported as a single verdict is a defect. `contact_id` is
the identifier the call gave you, so the caller can match the answer to the person. `status` is `added`, or
`already_member` where the provider says the person was on the list already; a provider that cannot tell answers
`added` for both. A refusal by the provider is a **failed outcome with its class**, not an item status.

## Repeating after a lost answer

If an attempt ends without an answer, the add may already have happened. The runtime will hand the item back with
the next attempt number and **the same idempotency key**; it does not reason about what happened. You do:

- Read the membership of the pinned contact in the list, and the ledger entry under the idempotency key.
- If either says the effect already happened, answer from that reading — `already_member` — and write nothing.
- Or, where the provider's add is idempotent (a second add of the same contact leaves one membership), repeat the add
  worked by the contact you pinned on the failure, and answer `added`: the effect is one membership either way; with
  no pin learned, the contact the first attempt may have made is found by a read first, never created again from the
  person's fields.

Return the contact identifier you learned on the failure as well — `host.fail({ external_ids: { contact: … } })`.
It is recorded by the same rule as on a success, and after a lost answer it is the only trace of the contact the
attempt ensured: without it the next attempt has no pin to read the membership of.

That obligation is why this operation is repeatable at all. A duplicate add is harmless at most providers, but the
contact creation it implies may be metered, and a second contact is a merge nobody asked for.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from list_membership.add.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | current membership for these people and the per-item ledger under this key — or none, where the provider's add is idempotent and the repeat works by the contact pinned on the failure. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_or_create` |
| in a paused campaign | `waits` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `contact` names a contact |
| recovery read | The membership of the pinned contact in the list named by `args.list.external_id`, and the ledger entry under this idempotency key. Where the provider's add is idempotent (a second add of the same contact leaves one membership), a repeat worked by the contact pinned on the failure needs no read; with no pin learned, the contact the first attempt may have made is found by a read first, never created again from the person's fields. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | D3 |

<!-- END GENERATED properties -->
