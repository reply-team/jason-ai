# `enrollment.stop` — take one person out of a provider campaign

**Version 1.** The machine-readable contract is [`enrollment.stop.json`](enrollment.stop.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, parked for a person at the claim, and — once a person approves it — performed against a
> real provider account. It reaches a plugin only where a route sends it: without one the item fails at the claim with
> `no_route` ([docs/routing.md](../../routing.md)).

## What it is for

"Take them out, and record why." The reason is mandatory and may name a different person as the cause. A person
approves the stop, and it is not refused for a person the suppression register holds
(`preflight.suppression: not_applicable`): a stop only takes somebody out, and those are the people it is most often
for. Its work waits with its Jason campaign when that campaign is paused.

## Finding the person

A stop creates nobody at the provider.

1. If `contacts[0].external_ids.contact` is present, that is the provider's identifier for this person. Work by it.
2. Otherwise look the person up by the value of the channel named by `args.channel`, compared exactly and without
   regard to case. One contact found is the person, and its identifier comes back in `external_ids.contact`. Nobody
   found means the provider has never held them, so they take part in nothing: answer `already_stopped`. More than
   one found is `contact_not_unique` — picking one would be guessing — and nothing is done.

A provider's search may match by resemblance — an address that merely contains this one — so the comparison is
yours, and it is exact. Read the lookup to its end: a lookup you cannot finish says nothing about a second match, so
it is refused, and never taken for nobody (which would answer `already_stopped` over a person still being written
to) or for one.

A person enrolled at the provider under another address is not found this way, and answers `already_stopped`: they
take part in nothing under this identity, which `enrollment.list` shows.

## The input you receive

```json
{
  "args": { "channel": "email", "reason": { "code": "not_interested", "note": "replied that the timing is wrong" } },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "last_name": "Alvarez", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

## What to return

```json
{ "items": [ { "contact_id": "cnt_…", "status": "stopped", "external_ids": { "contact": "p_88421" } } ] }
```

`status` is `stopped`, or `already_stopped` when the person takes no part — absent, finished, or never seen. A pin the
provider no longer holds reads the same way: the provider answers the two alike. The person is taken out, never
marked finished, so their history stays and they are not counted as having completed the campaign. The reason is
Jason's record.

## Repeating after a lost answer

A removal whose answer was lost may already have removed. The runtime hands the item back; the next attempt reads
whether the person still takes part, and answers `already_stopped` when they do not.

## What a person reads before deciding

The runtime renders this summary from the composed input when it parks the work for a person:

> Take Marta Alvarez (marta@example.com) out of campaign Q3 LatAm founders at the provider, reason not_interested.
> Their history stays; nothing further is sent to them from this campaign.

It highlights the whole reason, the address, the campaign's name, the provider's identifier for it once Jason holds
one, and the campaign as the arguments name it, so that whoever decides sees which of the provider's campaigns the
person leaves, on a first link as well as after it. The fixtures
[`fixtures/enrollment.stop/preview-*.json`](../fixtures/enrollment.stop) show each case.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from enrollment.stop.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `irreversible` | — |
| `approval` | `confirm_once` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the participation is absent or finished — either reads as a completed stop. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args`, suppression `not_applicable` |
| in a paused campaign | `waits` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | Find the provider's own contact without creating one: by `contacts[0].external_ids.contact` when that pin is present, otherwise by the value of the channel named by `args.channel`, compared exactly and without regard to case. A person the provider does not hold takes part in nothing, which is a completed stop. |
| preview a person reads | `Take {/contacts/0/first_name} {/contacts/0/last_name} ({/contacts/0/channels/0/value}) out of campaign {/campaign/name} at the provider, reason {/args/reason/code}. Their history stays; nothing further is sent to them from this campaign.`; highlighted: `/args/reason`, `/contacts/0/channels/0/value`, `/campaign/name`, `/campaign/external_ids/campaign`, `/args/campaign` |
| pinned identifiers | `contact` names a contact; `campaign` names a campaign |
| recovery read | Whether this person still takes part in the campaign. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | G1, G3, G6, G8 |

<!-- END GENERATED properties -->
