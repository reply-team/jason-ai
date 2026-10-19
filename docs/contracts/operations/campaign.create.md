# `campaign.create` — make the provider's campaign, as an empty draft

**Version 1.** The machine-readable contract is [`campaign.create.json`](campaign.create.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **This version runs it**, and it writes: a work item naming this operation is measured against the argument schema
> below when it is written, routed to a plugin at the claim, and performed against a real provider account — the
> provider's campaign is made, as a draft that holds nobody. It reaches a plugin only where a route sends it: without
> one the item fails at the claim with `no_route`.

## What it is for

"Start a new campaign" — always a draft; creating never sends and never arms anything. So a person never has to make
the provider's campaign by hand before Jason can work with it.

The campaign it makes is the provider's, not Jason's own (that one is made by `jason campaign create`): a draft that
holds nobody, has never been started, and has sent and armed nothing. Starting it is `campaign.activate`, a separate
act with its own confirmation. A provider that cannot hold a campaign with no steps may give it the least step it
needs to exist, one that reaches nobody even once the campaign runs — a task for its owner, never a message — and the
result says how many it added.

Two things are called "create". `jason campaign create` makes **Jason's** campaign, the record work items belong to;
this operation makes **the provider's** campaign for it, and links the two. They are two namespaces with the same
words ([../README.md](../README.md), "Two namespaces"). It runs `auto`, as the catalogue derives it: a draft that
holds nobody reaches nobody, and it is the person's own account's object.

## The input you receive

```json
{
  "args": { "name": "Q3 LatAm founders" },
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "draft", "external_ids": {} },
  "idempotency_key": "wi_…"
}
```

`args.name` is the name the provider's campaign is given, and nothing else is: no steps, no schedule, no sender —
each is a later operation's. Jason's campaign, when it already carries this plugin's campaign pin, is linked already,
and the plugin refuses it as `campaign_already_linked` before it calls the provider: a second create would make a
second provider's campaign beside the first link.

## What to return

```json
{ "items": [ { "campaign_external_id": "c_7714", "name": "Q3 LatAm founders", "status": "created", "state": "draft", "enrolled": 0, "steps_added": 1 } ] }
```

`state` is always `draft` and `enrolled` always `0`: the schema holds both as constants, so an answer that says
anything else is `result_invalid` rather than a provider's campaign Jason would believe in. The identifier goes beside
the result under `external_ids.campaign` too. That pin is the first link: every later operation for Jason's campaign
works by it and needs no identifier in its arguments.

## Repeating after a lost answer

The provider keeps no key on its campaigns, so the name the request carried is how a repeat finds what an earlier
attempt made. Every attempt lists the provider's campaigns first, every page, and compares each name with `args.name`
exactly and without regard to case. The plugin reads the whole list and compares the names itself rather than asking
the provider to filter by the name, because the name is an operation's argument, and an argument never reaches a
command line or a log — and a provider's own filter may match part of a name besides.

| Attempt | What the list holds under the name | What happens |
|---|---|---|
| first | a campaign | `campaign_name_taken`, and nothing is made |
| first | nothing | the campaign is made |
| after a lost answer | nothing | the campaign is made: the earlier attempt made nothing |
| after a lost answer | exactly one, a draft that holds nobody, created no earlier than the work item less ten minutes | it is this item's own: pinned, and answered `created` |
| after a lost answer | anything else — one created earlier, one that holds somebody or has been started or archived, more than one | `campaign_name_ambiguous`, and nothing is made: a person decides which is meant |

The moment a work item was created is the one its identifier carries, and the ten minutes allow for the two clocks. A
second campaign of a name that exists is never made, on any attempt, and one that exists is never adopted by surprise.

## When it fails

Jason's campaign linked already fails `campaign_already_linked`, and a name already taken fails
`campaign_name_taken`; both are `validation`, because the same input is refused every time. `campaign_name_ambiguous` is permanent: what the provider
holds has to be looked at by a person. A provider that answers the create with a campaign started or archived fails
`campaign_not_draft` — never expected, and said if it ever happens — with the campaign's identifier pinned on the
failure so a person can find it. The shared failures are spelled as every document spells them.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from campaign.create.json. Edit the document; this block follows it. -->

| Property | Value | Detail |
|---|---|---|
| `reach` | `control` | — |
| `reversibility` | `reversible` | — |
| `approval` | `auto` | — |
| `protective_floor` | `false` | — |
| `before_repeating` | list campaigns for one already created under this key | — |
| `idempotency_key` | `required` | — |
| `per_item_results` | `not_applicable` | — |
| `cost` | `none` | — |

| What Jason adds | Value |
|---|---|
| pre-flight | contact `not_used`, channel `none`, suppression `checked`, provider contact `not_used` |
| in a paused campaign | `waits` |
| contact projection | none — this operation never receives the work item's contact |
| precondition | Jason's campaign is not linked yet to a provider's campaign of this plugin's: `campaign.external_ids.campaign` is absent. A linked one is refused as `campaign_already_linked` before the provider is called, because a second create would make a second provider's campaign beside the first link. |
| preview a person reads | none |
| pinned identifiers | `campaign` names a campaign |
| recovery read | The provider's campaigns, every page, each name compared with `args.name` exactly and without regard to case. The provider's campaign an earlier attempt made is the only one of that name, a draft that holds nobody, created no earlier than the moment the work item's identifier carries less ten minutes for the two clocks; nothing of the name means nothing was made. |
| after an ambiguous end | `after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened |
| timeout | 120000 ms |
| invariants | E1 |

<!-- END GENERATED properties -->
