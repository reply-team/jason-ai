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
travel in the arguments: the planner says what the provider should hold. Jason's own record of the person reaches the
provider only when the provider holds nobody for them, as the contact made for them before the values are set. A field
the arguments leave out stays as the provider holds it; a field given as `null` is cleared.

One work item changes one record, and the catalogue approves a single record automatically, so the update runs without
a person (`approval: auto`; [`divergence-from-l1.md`](../divergence-from-l1.md), "An update of one record"). It is not
run for a person whose consumed value the suppression register holds, and its work waits with its Jason campaign when
that campaign is paused.

An address is not one of the fields: it is a channel, and moving a person to another address is not an update of their
record. No field this version names is an opt-out, an owner or a stop, so no update can lift one (A1).

## Finding the person

The fields belong on the provider's own contact for the person, so an update finds that contact or makes it:
`preflight.provider_contact` is `find_or_create`, as for the other five operations that may create the person — the list
add, the enrolment, the opt-out and the two sends; the stop, the reply and the delete create nobody — and the call
projects the person as every operation that may create them does. The policy was `find_only` before, when a person the
provider held nobody for failed `contact_not_found`; that is a relaxation, so the document keeps its version (the
contracts [README](../README.md), "Versioning"). The composed input now carries the person's own fields too, but an
update is approved automatically, so no approval parked before this change is parked once more as `input_changed`: none
was ever parked.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Work by it.
2. Otherwise look the person up by the value of the channel named by `args.channel`. One contact found is the person.
   Nobody found means the provider has never held them: create the contact from that value and the person's own
   fields, as an enrollment or a list add would, and set the arguments' values on it. Return the identifier in
   `external_ids.contact` either way; the runtime pins it, and the next call takes path 1.

The update is itself the operation that needs the contact, so making one here copies nothing to the provider ahead of
the work that uses it, which is all the contact proxy rule's fourth part asks. [`docs/plugins.md`](../../plugins.md) §7
says how the lookup compares and when it is refused.

## The input you receive

```json
{
  "args": {
    "channel": "email",
    "fields": { "title": "Chief Revenue Officer", "last_name": null },
    "custom_fields": [ { "name": "lead_source", "value": "Webinar" } ]
  },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "last_name": "Alvarez", "company": null, "title": null, "time_zone": null, "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

`args` names `fields`, `custom_fields`, or both. A value is a string that is not blank, or `null` to clear the field. At
most 50 custom fields travel in one call, and one name may appear once. The contact carries its identifier, the
person's own fields — what a contact is created from when the provider holds nobody for them, never values to set on
one it holds — the one channel named by `args.channel`, and the pin.

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "updated", "external_ids": { "contact": "p_88421" } } ] }
```

`status` is `updated`. A refusal by the provider is a **failed outcome with its class**: a custom field the account
defines none of is `field_not_found`, a value the provider refuses is `invalid_field_value`, and in both nothing is
changed. `limit_reached` is the provider's account full: it will hold no further contact, and the person had to be
created, so nothing was changed.

## Repeating after a lost answer

An update whose answer was lost may already have made the contact and changed the fields. The update sets each field it
names outright, so a repeat worked by the contact you pinned on the failure lands on the same values and answers
`updated`; with no pin learned, the person is looked up by the consumed value first, so a contact the first attempt
made is found rather than made again. Return the identifier you learned on the failure — `host.fail({ external_ids: {
contact: … } })` — so the next attempt has a pin to work by.

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
| pre-flight | contact `required`, channel `from_args`, suppression `checked`, provider contact `find_or_create` |
| in a paused campaign | `waits` |
| under a provider block | `runs` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | Every custom field the arguments name is one the provider's account defines. |
| preview a person reads | none |
| pinned identifiers | `contact` names a contact |
| recovery read | With a pin learned on the failure, nothing more: the update sets each field it names outright, so a repeat worked by that contact lands on the same values. With no pin learned, the person is looked up by the consumed value first, so the contact the first attempt may have made is found by that read and never created again; one is created only when the provider still holds nobody under the value. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | A1 |

<!-- END GENERATED properties -->
