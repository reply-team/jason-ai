# `optout.record` — record that somebody said stop

**Version 1.** The machine-readable contract is [`optout.record.json`](optout.record.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, routed to a plugin at the claim, and performed against a real provider account — the
> provider stops reaching the person. It reaches a plugin only where a route sends it: without one the item fails at
> the claim with `no_route`.

## What it is for

"They said stop." It records the act itself — what they said, where it arrived, when they said it and when it was
learned — and it is the catalogue's **protective floor**: an operation whose not running is itself the harm. Waiting
for permission to stop is the destructive choice, so it runs `auto`. Nothing in Jason holds it back: the suppression
register does not refuse it (`preflight.suppression: not_applicable`), and its work runs while its Jason campaign is
paused (`dispatch.in_paused_campaign: runs`).

There are two records, by design. Jason's own register — `jason suppression add`, read by the claim — stops Jason's
reach at once; the responder role writes it before it creates this operation's work item. This operation records the
stop at the provider, whose flag the plugin
reads before every write. The four facts are Jason's: the provider holds a yes or a no.

## Finding the person

`preflight.provider_contact` is `find_or_create`, and for an opt-out the order is the point: the stop must land on the
contact the provider already holds, never on a second one made for the same address, which would leave the first one
reachable.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Work by it.
2. Otherwise look the person up by the value of the channel named by `args.channel`. One contact found is the person,
   and its identifier comes back in `external_ids.contact`. Nobody found means the provider has never held them:
   create the contact from that value. More than one found is `contact_not_unique` — picking one would be guessing —
   and nothing is done.

A lookup that cannot be read to its end is refused, never taken for nobody, which would create a second contact.
[`docs/plugins.md`](../../plugins.md) §7, "The contact proxy rule", says how the lookup compares.

## The input you receive

```json
{
  "args": {
    "channel": "email",
    "statement": "please take me off your list",
    "arrived_via": "reply",
    "said_at": "2026-10-03T08:14:00Z",
    "learned_at": "2026-10-03T08:20:00Z"
  },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": {} } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": {} },
  "idempotency_key": "wi_…"
}
```

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "recorded", "external_ids": { "contact": "p_88421" } } ] }
```

`status` is `recorded`, or `already_recorded` when the provider says it already held the person as opted out — in
which case nothing is written. A provider whose flag is set idempotently and that cannot tell answers `recorded` for
both.

## Repeating after a lost answer

If an attempt ends without an answer, the opt-out may already be recorded. The runtime hands the item back; the next
attempt reads the contact's flag first and answers `already_recorded` when it is set. Return the contact identifier
you learned on the failure as well, so the next attempt has a pin to read the flag of.

Or, where setting the provider's flag is idempotent (setting it again changes nothing), the next attempt sets it again,
worked by the contact you pinned on the failure, and answers `recorded`; with no pin learned, the contact the first
attempt may have made is found by a read first, never created again from the person's fields.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from optout.record.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `irreversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `true` | the protective floor — recording that somebody said stop is an operation whose non-performance is itself the harm, and waiting for permission to stop is the destructive choice. |
| `before_repeating` | the current opt-out state for this person and identifier, with its recorded event time — or none, where setting the provider's flag is idempotent and the repeat works by the contact pinned on the failure. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `not_applicable`, provider contact `find_or_create` |
| in a paused campaign | `runs` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | `contact` names a contact |
| recovery read | The contact's opt-out flag at the provider. Where setting the provider's flag is idempotent (setting it again changes nothing), a repeat worked by the contact pinned on the failure needs no read; with no pin learned, the contact the first attempt may have made is found by a read first, never created again from the person's fields. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | A2, A3, A4, K2 |

<!-- END GENERATED properties -->
