---
name: managed-campaign-work
description: Use when an interactive agent session should turn an outbound objective into durable campaign work that Jason performs — creating a campaign, adding the people it is about, submitting managed provider operations, and handling the approvals they wait on.
status: draft
---

# Managed campaign work

Jason owns campaign state; a conversation does not. Anything decided in a session that is not committed through
the Jason CLI has not happened: it will not survive the session, will not be dispatched, will not be approved by
anybody, and will leave no history. This skill is the managed path — how to put work where the runtime can
perform it, and what to do while it waits.

**Status: draft.** It covers the operations this build publishes and nothing else.

## What you are responsible for, and what the runtime is

You decide *what* should happen: which campaign, which people, which operation, with which arguments. The runtime
decides *whether and when* it happens: it validates the arguments against the operation's published contract,
refuses work it cannot route, waits for a person where a person is required, retries what is worth retrying, and
records every attempt. Do not re-implement any of that in a session — if the runtime refuses something, read the
code it answered with rather than working around it.

## Before you start: is this installation connected to a provider?

No provider ships configured. A fresh installation routes nothing anywhere, so provider work will fail with
`no_route` until somebody has installed a plugin and written a route. Check before you promise anything:

```
jason runtime status
jason plugin list
```

If `plugins` is empty, or the operation you need is not in a plugin's `operations`, stop and say so: installing a
package and writing a route is an operator's explicit act, not something to work around. To see where one
operation would go for one campaign:

```
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll
```

## The work itself

A campaign, the people it is about, and then one work item per thing to be done.

```
jason campaign create --name "Q3 LatAm founders"
jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file contacts.json --match-by email
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

`contacts.json` is a JSON array of people, each with their channels:

```json
[
  {
    "first_name": "Marta",
    "channels": [{ "channel": "email", "value": "marta@example.com" }]
  }
]
```

A managed provider operation is a work item whose `--operation` names a published contract and whose `--input`
carries that contract's arguments:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.enroll --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input "{\"campaign\":{\"external_id\":\"7\"},\"channel\":\"email\",\"collision\":\"skip\",\"start\":{\"position\":\"first_step\"},\"first_touch\":\"authored_delay\"}"
```

The arguments are validated when the item is created, so a refusal here is about what you asked for, not about
the provider. Read the operation's own document under `docs/contracts/operations/` before you invent a field:
it is the same document the runtime enforces.

Then follow the work:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

## When work waits for a person

An operation whose contract says a person must confirm it parks at the claim: the item's status becomes
`awaiting_approval`, no attempt is made, and nothing is sent to the provider. The decision is a person's — a
session must not make it, and the runtime refuses a decision that does not name one.

```
jason approval list
jason approval get apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

`approval get` shows what would happen if it were approved: the intent, who would be reached and where, what it
would cost, what could be undone, and which account it would act through. Show that to the person, in their
words, and let them answer:

```
jason approval approve apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --actor human:ada --reason "checked the list"
jason approval reject apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --actor human:ada --reason "wrong audience"
```

An approval covers exactly what it was shown. Change the work item's input afterwards and the runtime parks it
again with a fresh question, because what the person agreed to is no longer what would happen.

## When work stops

Read the code, then decide. Common ones: `no_route` (nothing says where this operation goes),
`plugin_unavailable` (the package is there but the program it needs is not), `input_invalid` (the arguments do
not satisfy the contract), `approval_required` (a person has not answered yet), `suppressed` (the person is on
the suppression list). Do not retry blindly: the runtime already retries what is worth retrying, and a failure it
called final is final for a reason.

An unanswered ending — a crash, a lost connection — is recorded as `ambiguous`, which means nobody can say
whether the provider acted. Where the operation's contract declares a recovery read, the next attempt asks the
provider what happened before it writes anything; where it does not, the work stops and a person decides.

The chronicle is the record of all of it:

```
jason journal list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

## What this skill does not cover

Reporting an effect you produced yourself, outside Jason, through some other tool; background AI work and the
roles that perform it; anything a campaign manager or planner does with an approval; and installing plugins or
writing routes. Those are other people's jobs or other skills, and guessing at them here would be worse than
saying so.
