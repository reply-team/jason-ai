# `contact.update` — change a person's fields on the provider's contact for them

**Version 1.** The machine-readable contract is [`contact.update.json`](contact.update.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, routed to a plugin at the claim, and performed against a real provider account — the
> fields of a contact it holds are changed. It reaches a plugin only where a route sends it: without one the item fails
> at the claim with `no_route`.

## What it is for

"Change these fields on these people." The fields are the provider's own record of a person — the first name, the last
name, the company, the title — and the custom fields its account defines, named as the account names them. The values
travel in the arguments: the planner says what the provider should hold, and nothing of Jason's own record is copied
there by this operation. A field the arguments leave out stays as the provider holds it; a field given as `null` is
cleared.

One work item changes one record, and the catalogue approves a single record automatically, so the update runs without
a person (`approval: auto`; [`divergence-from-l1.md`](../divergence-from-l1.md), "An update of one record"). It is not
run for a person whose consumed value the suppression register holds, and its work waits with its Jason campaign when
that campaign is paused.

An address is not one of the fields: it is a channel, and moving a person to another address is not an update of their
record. No field this version names is an opt-out, an owner or a stop, so no update can lift one (A1).

## Finding the person

The fields belong on the provider's own contact for the person, and an update changes a contact the provider holds; it
makes none: `preflight.provider_contact` is `find_only`.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Work by it.
2. Otherwise look the person up by the value of the channel named by `args.channel`. One contact found is the person:
   return its identifier in `external_ids.contact`, and the runtime pins it, so the next call takes path 1. Nobody found
   is `contact_not_found`, and nobody is created: the person is enrolled or put on a list first, then updated.

Creating a provider contact only so that the fields have somewhere to go would copy Jason's contact to the provider
ahead of any operation that needs it, which the contact proxy rule's fourth part forbids: a contact is made by the
operation that puts the person somewhere or lands a stop on them. [`docs/plugins.md`](../../plugins.md) §7 says how the
lookup compares and when it is refused.

## The input you receive

```json
{
  "args": {
    "channel": "email",
    "fields": { "title": "Chief Revenue Officer", "last_name": null },
    "custom_fields": [ { "name": "lead_source", "value": "Webinar" } ]
  },
  "contacts": [ { "id": "cnt_…", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

`args` names `fields`, `custom_fields`, or both. A value is a string that is not blank, or `null` to clear the field. At
most 50 custom fields travel in one call, and one name may appear once. The contact carries its identifier, the one
channel named by `args.channel` and the pin, which is all a lookup needs: nothing else of Jason's record of the person
reaches the plugin.

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "updated", "external_ids": { "contact": "p_88421" } } ] }
```

`status` is `updated`. A refusal by the provider is a **failed outcome with its class**: a custom field the account
defines none of is `field_not_found`, a value the provider refuses is `invalid_field_value`, and in both nothing is
changed.

## Repeating after a lost answer

An update whose answer was lost may already have changed the fields. The update sets each field it names outright, so
a repeat worked by the contact you pinned on the failure lands on the same values and answers `updated`; with no pin
learned, the person is found again by the consumed value, as the first attempt found them. Return the identifier you
learned on the failure — `host.fail({ external_ids: { contact: … } })` — so the next attempt has a pin to work by.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from contact.update.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `compensatable` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the current field values, and the per-item ledger under this key. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_only` |
| in a paused campaign | `waits` |
| contact projection | `id`; channels: `consumed` |
| precondition | Every custom field the arguments name is one the provider's account defines. |
| preview a person reads | none |
| pinned identifiers | `contact` names a contact |
| recovery read | With a pin learned on the failure, nothing more: the update sets each field it names outright, so a repeat worked by that contact lands on the same values. With no pin learned, the person is found again by the consumed value, as the first attempt found them, and nobody is created. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | A1 |

<!-- END GENERATED properties -->
