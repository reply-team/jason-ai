# `campaign.activate` — start a provider campaign sending

**Version 1.** The machine-readable contract is [`campaign.activate.json`](campaign.activate.json); this page explains
it. Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, parked for a person at the claim, and — once a person approves it — performed against a
> real provider account. **The provider's campaign starts sending.** It reaches a plugin only where a route sends it:
> without one the item fails at the claim with `no_route` ([docs/routing.md](../../routing.md)).

## What it is for

"Turn it on." Starting a campaign releases step 1 to everybody enrolled in it, so the decision is about a
population, and a person makes it. Enrolling into a draft campaign and then activating it is two decisions — the
second over the population; enrolling into a live campaign is one decision over one person. Activation is also how a
paused campaign resumes: the provider's start does both, and `was` says which it was.

## The input you receive

```json
{
  "args": { "expected": { "enrolled": 240 } },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

The campaign is named by its pin, or the first time by `args.campaign.external_id`. `args.expected.enrolled` is the
population the person approves, read beforehand with `enrollment.list` (`total`) or `campaign.get`.

## The population the person approved

Read the provider's enrolled count at execution. If it is not `args.expected.enrolled`, start nothing and fail
`population_changed`, with both numbers under `details.expected` and `details.actual`. The person approved *that*
population; a different one is a different decision. The planner reads the count again and creates a new work item,
which is parked again with a summary naming the new number — an approval binds a person to one composed input, so a
new input is a new decision.

The count is read, then the campaign is started; a person enrolled in between is a race the provider offers no way
to close.

## What to return

```json
{ "items": [ { "campaign_external_id": "c_7714", "status": "activated", "was": "draft", "enrolled": 240 } ] }
```

`status` is `activated`, with `was` saying whether it left `draft` or `paused`; or `already_live`, with `was: null`,
when the campaign was live before this call — which starts nothing.

## Repeating after a lost answer

An activation whose answer was lost may already be sending. The runtime hands the item back; the next attempt reads
the campaign's live state and its count first, and answers `already_live` when the start already happened.

## What a person reads before deciding

The runtime renders this summary from the composed input when it parks the work for a person, and prints it first in
`jason approval get --human`:

> Start sending campaign Q3 LatAm founders at the provider. People enrolled there: 240. Step 1 goes to each of them
> on the campaign's own schedule, and the cost is metered per message released. If the provider's count is not 240 at
> execution, nothing starts.

It highlights the number, the campaign's name, the provider's identifier for it once Jason holds one, and the campaign
as the arguments name it, so that on a first link, before any identifier is pinned, the person still sees which of the
provider's campaigns starts. The fixtures
[`fixtures/campaign.activate/preview-*.json`](../fixtures/campaign.activate) show each case.

## When it fails

A campaign the provider does not hold fails `campaign_not_found`. One it will not start — archived, or without
contacts, a connected sending account or sending hours — fails `campaign_not_activatable`, with the provider's own
code under `details.provider_code`. A count that moved fails `population_changed`. The shared failures are spelled as
every document spells them.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from campaign.activate.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `act` | conditional — `act`, with magnitude equal to the enrolled population; `control` only where the campaign holds no enrolments, and even then it arms the campaign so the next enrolment sends immediately. |
| `reversibility` | `irreversible` | — |
| `approval` | `confirm_once` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | the campaign's live state and its first-touch ledger — an activation whose result was lost may already be sending. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | conditional — when a collection is given. |
| `cost` | `metered` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked` |
| in a paused campaign | `waits` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | The campaign exists at the provider and is not archived, and the provider's enrolled count equals `args.expected.enrolled`. |
| preview a person reads | `Start sending campaign {/campaign/name} at the provider. People enrolled there: {/args/expected/enrolled}. Step 1 goes to each of them on the campaign's own schedule, and the cost is metered per message released. If the provider's count is not {/args/expected/enrolled} at execution, nothing starts.`; highlighted: `/args/expected/enrolled`, `/campaign/name`, `/campaign/external_ids/campaign`, `/args/campaign` |
| pinned identifiers | `campaign` names a campaign |
| recovery read | The campaign's live state and its enrolled count. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | E1, E2, E3 |

<!-- END GENERATED properties -->
