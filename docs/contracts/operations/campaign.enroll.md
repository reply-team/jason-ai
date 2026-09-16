# `campaign.enroll` — put one person into a provider campaign

**Version 1.** The machine-readable contract is [`campaign.enroll.json`](campaign.enroll.json); this page explains
it. Where the two seem to differ, the document is right and this page is a bug.

> **This version never runs it**, and the reason is now the approval alone. Routing, the composed input and the
> answer check are all in place — the other two published operations run through them — but `campaign.enroll`
> requires a person's approval, nothing in the runtime can ask for one, and a dispatcher may not stand in for the
> person who approves. Every item naming it fails at the claim with `approval_required`, having reached no
> provider. The contract is published complete so that the gate is the only thing left to add — and so that a
> plugin author can implement and test the operation now.

## What it is for

"Put these people into the Q3 campaign", with an explicit collision policy, an explicit start position and an
explicit first-touch timing. Enrolling into a campaign that is not live is bookkeeping; enrolling into one that is
live **is a send**, and the result says which it was.

None of `collision`, `start` or `first_touch` may be defaulted. A default here is a silent decision about what a
real person receives and when, so an input that leaves one out is refused rather than filled in.

## The provider-contact precondition

The same as `list_membership.add`: work by `contacts[0].external_ids.contact` when that pin is present, otherwise
create the provider's contact from the value of the channel named by `args.channel` and return its identifier in
`external_ids.contact`. A person is never matched by address again once a pin exists.

## The input you receive

```json
{
  "args": {
    "campaign": { "external_id": "c_7714" },
    "channel": "email",
    "collision": "skip",
    "start": { "position": "step", "step": 3 },
    "first_touch": "next_open_window"
  },
  "contacts": [ { "id": "cnt_…", "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ], "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

- `collision` — `skip` leaves an existing participation alone; `refuse` fails rather than deciding for the caller.
- `start.position` — `first_step`, or `step` with the step number. `step` without a number is refused.
- `first_touch` — `authored_delay`, `next_open_window` or `immediately`.

The campaign is named by `args.campaign.external_id` for the first link, or by `campaign.external_ids.campaign`
once it is pinned, exactly as in `campaign.get`.

## What to return

```json
{
  "items": [ { "contact_id": "cnt_…", "status": "enrolled", "external_ids": { "contact": "p_88421" } } ],
  "campaign_live": false
}
```

`campaign_live` is the campaign's state at the moment of the enrollment, because that is what decides whether this
was a send. `status` is `enrolled` or `already_enrolled`; a refusal by the provider is a failed outcome with its
class. An enrollment identifier, if the provider issues one, belongs in the result and not in `external_ids`: a pin
is keyed by contact and kind, and it could not hold one per campaign.

## Repeating after a lost answer

An enrollment into a live campaign is a send, so a blind repeat sends the same person twice and is billed twice.
When the attempt number is above one, read the per-item outcome of the prior run under the idempotency key and the
campaign's live state first, and answer from that reading when the effect already happened.

Return the identifiers you learned on the failure as well — `host.fail({ external_ids: { contact: … } })`. They are
recorded by the same rule as on a success, and after a lost answer they are the only trace of what the attempt did:
without the pin the next attempt has nobody to read the prior outcome for.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from campaign.enroll.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `act` | conditional — bookkeeping while the campaign is not live, `act` the moment it is; resolved by the preview, never assumed. |
| `reversibility` | `irreversible` | conditional — compensatable until the first touch goes, irreversible after. |
| `approval` | `confirm_once` | conditional — confirm_once before go-live; confirm_once with a mandatory preview once live. One call over a set is one decision, and the preview is the approval artefact — it names the population, it does not count it. |
| `before_repeating` | the per-item outcome of the prior run under this key, and the campaign's live state — re-running blind into a live campaign sends twice. | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `required` | — |
| `cost` | `metered` | conditional — none while the campaign is not live; metered once it is. |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `required`, channel `from_args` |
| contact projection | `id`, `first_name`, `last_name`, `company`, `title`, `time_zone`; channels: `consumed` |
| precondition | Ensure the provider's own contact exists before enrolling it: work by `contacts[0].external_ids.contact` when that pin is present, otherwise create the contact from the value of the channel named by `args.channel`, and return the provider's identifier under `external_ids.contact`. |
| pinned identifiers | `contact` names a contact; `campaign` names a campaign |
| recovery read | The per-item outcome of the prior run under this idempotency key, and the campaign's live state. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 300000 ms |
| invariants | A7, D1, D4, E2, F2, G1, G2, G7, G8 |

<!-- END GENERATED properties -->
