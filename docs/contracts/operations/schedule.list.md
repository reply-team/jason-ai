# `schedule.list` — the provider's sending schedules, with the windows each one sets

**Version 1.** The machine-readable contract is [`schedule.list.json`](schedule.list.json); this page explains it. Where
the two seem to differ, the document is right and this page is a bug.

> **This version runs it.** A work item naming this operation is checked against the argument schema below when
> it is written, routed to a plugin at the claim, performed in a plugin-host process, and answered — and the
> answer is checked against the result schema before it becomes the item's result. It reaches a plugin only
> where a route sends it: without one the item fails at the claim with `no_route`.

## What it is for

"Which sending schedules are there, and when does each let a campaign send?" — the named schedules of the provider's
account, one page of them, each with its time zone and its windows by weekday as somebody authored them. **It lists
schedules and resolves no calendar:** nothing here says on which dates a campaign will send, what a holiday moved or
when one person will be reached. The document carries the vendor-neutral fields of `schedule.resolve`, whose intent
line, in the document, is the catalogue's own, because the catalogue reads a schedule as one campaign's calendar and
names no list of them; [`divergence-from-l1.md`](../divergence-from-l1.md) says so in the row "Named schedules".

A provider that holds schedules as named entities of the team, each assigned to the campaigns that run on it, needs
them listed before a campaign's schedule can be read or set. This read is that list.

## The input you receive

```json
{
  "args": { "limit": 20, "offset": 0 },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active", "external_ids": {} }
}
```

- `limit` and `offset` page the list as every paged read does; both are required. There is no filter: a provider
  holds few schedules, and the read lists them all.

## What to return

```json
{
  "items": [
    {
      "external_id": "41",
      "name": "Business hours",
      "time_zone": { "id": "Eastern Standard Time", "scheme": "windows" },
      "in_recipient_time_zone": false,
      "skips_holidays": true,
      "default": true,
      "windows": [
        { "weekday": "monday", "active": true, "ranges": [{ "from": "09:00", "to": "17:00" }] },
        { "weekday": "saturday", "active": false, "ranges": [] }
      ],
      "follow_up_windows": null,
      "vendor": { "status": "isDefaultForCurrentUser" }
    }
  ],
  "has_more": false,
  "vendor": { "ordering": "id_asc" }
}
```

- `time_zone` is the zone the windows are read in, as the provider spells it, with the scheme that spelling belongs
  to: `iana` (`America/New_York`) or `windows` (`Eastern Standard Time`). It is never converted, because one zone of
  one scheme can be several of the other. `in_recipient_time_zone` says the provider reads the windows in each
  person's own zone instead, where it knows one.
- `windows` are the weekdays the provider holds, each with whether it sends that day at all and its ranges, from and
  to, as `HH:MM` on a 24-hour clock. A window that runs to the end of the day ends at `24:00`; no window starts there.
  A weekday the provider holds nothing for is absent rather than invented as inactive.
- `follow_up_windows` is the second set of windows a provider may keep for a campaign's later steps, or null where
  the schedule's own windows serve every step.
- `default` says the schedule is the default of the user the plugin signs in as — the one that user's new campaigns
  start with.
- `skips_holidays` says the provider sends nothing on the holidays of the calendars linked to the schedule; the
  calendars are not listed.
- The provider's word for the schedule's state is under `vendor.status` and nowhere else: no provider publishes a
  vocabulary for it, so there is no neutral word to put beside it.
- A name the provider holds as empty is null.

A read pins nothing: the identifiers it lists are reported, never recorded.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from schedule.list.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `read` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | nothing at stake | — |
| `idempotency_key` | `none` | — |
| `per_item_results` | `required` | conditional — when a collection of enrolments is given. |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked`, provider contact `not_used` |
| in a paused campaign | `waits` |
| under a provider block | `runs` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | none |
| preview a person reads | none |
| pinned identifiers | none — this operation pins nothing |
| recovery read | none |
| after an ambiguous end | `safe` — repeating changes nothing and costs nothing, so the item is handed out again |
| timeout | 60000 ms |
| invariants | none names this operation |

<!-- END GENERATED properties -->
