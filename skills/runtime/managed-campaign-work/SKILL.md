---
name: managed-campaign-work
description: Use when an interactive agent session should turn an outbound objective into durable campaign work that Jason performs - creating a campaign, adding the people it is about, submitting managed provider operations and AI role work, and following what the runtime does with them.
metadata:
  status: verified
---

# Managed campaign work

Jason owns campaign state; a conversation does not. Anything decided in a session that is not committed through
the Jason CLI has not happened: it will not survive the session, will not be dispatched, will not be approved by
anybody, and will leave no history. This skill is the managed path — how to put work where the runtime can
perform it, and what to do while it waits.

**Status: verified** against Claude Code 2.1.278. It covers the operations this build publishes and nothing else.

## What you are responsible for, and what the runtime is

You decide *what* should happen: which campaign, which people, which operation, with which arguments. The runtime
decides *whether and when* it happens: it validates the arguments against the operation's published contract,
refuses work it cannot route, waits for a person where a person is required, retries what is worth retrying, and
records every attempt. Do not re-implement any of that in a session — if the runtime refuses something, read the
code it answered with rather than working around it.

**The runtime owns scheduling and resumption.** Once a work item exists, the runtime decides when it runs, runs
it, retries it if that is what its failure deserves, and picks it up again after a restart — **nothing else wakes
the work**. Not this session, not a file you keep, not a reminder you set yourself. So a session's job ends when
the work is committed and the person has been told what to expect; there is nothing to keep in the conversation
afterwards, and nothing that has to be running for the work to go on.

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

Setting one up is `operating-the-installation`, which is where plugins, routes, profiles and updates live.

## The work itself

A campaign, the people it is about, and then one work item per thing to be done.

```
jason campaign create --name "Q3 LatAm founders"
jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file contacts.json --match-by email
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**The campaign's own people are `campaign list-contacts`.** `contact list` answers with everyone this runtime
knows and takes no campaign at all, so `contact list --campaign …` is the spelling to expect yourself to reach
for and the one that costs you a turn on `Unrecognized command or argument`.

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
the provider. Read the operation's own document before you invent a field: it is the same document the runtime
enforces, and it is published with the project's source, under `docs/contracts/operations/`. **No verb prints
one**, so on a machine with no copy of the source you cannot read it from here — ask the operator for it rather
than guessing a field, because a guess is refused at `workitem.create` anyway.

Work that needs a model rather than a provider is an `ai_role` item, named for the role that should do it:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role researcher --context '{"account":"Analytical Engines","question":"which of the two sites is the target"}'
```

Then follow the work:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

### Which host runs AI work, and what that means for work you create

An `ai_role` item runs on an agent host the operator installed, named by an **execution profile**. You
rarely choose one: work you create inherits the profile of the run that created it, so a chain of work
keeps running where it started. `jason workitem get` shows that as a lineage line.

Two consequences worth knowing.

**Work you create from inside a launched run is that run's.** You do not have to say so — the runtime
tells the command line which attempt it is — and that is what the inheritance is read from. Naming a
different actor on such a call breaks the chain, so do not.

**If the runtime cannot tell where your work should run, it stops rather than guessing.** An item that
fails with `lineage_resolution_unsupported`, `profile_not_found`, `profile_disabled`,
`host_not_available` or `role_skill_invalid` is a configuration problem, not a refusal by a model:
nothing was started and nothing was spent. The message names what to change. Fix it and ask for the
work again — a failed item is finished, and finished items are not reopened.

**If you are the launched role**, two rules travel with you. Call home with the plain command word the
launch envelope names, exactly as it is — the word, its verb and its arguments, as one command.
Anything built around it, such as a redirect or a chain, is neither promised to run nor promised to be
refused, and you have no file-writing tool, so whatever you want to keep goes inside the command
itself. And answer in the shape the item asked for — where a work item declares a `result_format`, a
completion that does not satisfy it is refused, however well it reads.

### A role's notes are its memory, not the truth

A role can keep one note per campaign — what it looked at, what it ruled out, what is still open — and
read it again on its next run. The launch envelope names where it is; the note itself is read when it
is wanted:

```
jason rolenote get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD researcher
jason rolenote set cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD researcher --note '{"gatekeeper":"the switchboard hangs up after six"}'
```

A note is written **whole** — there is no patch, so read it, add to it and write all of it back — and
it is at most 64 KiB of JSON object. A campaign nobody has written about yet answers with an empty
note rather than an error.

**It is not authoritative.** It is one role's own account, in its own words, from whenever it was
written. Where a note disagrees with what the runtime says now, the runtime is right. Read a note for
where to look first; check what it claims before you repeat it. Campaign context is the campaign's
shared knowledge and a different thing — do not write one into the other.

## Three things that will happen to work you created

Each of them is a skill of its own, because each is a different moment and a person arrives at it saying
something different. What belongs here is only that they exist.

**It may stop and wait for a person.** An operation whose contract says a person must confirm it parks at the
claim: the item's status becomes `awaiting_approval`, no attempt is made, and nothing is sent to the provider.
The decision is a person's — a session must not make it, and the runtime refuses a decision that does not name
one. Presenting what is waiting, and recording what the person says about it, is **`approvals-and-questions`**.

**It may stop with a code.** A work item that failed carries an error, and the code is the answer: it says
whether this is somebody's configuration, your arguments, a provider that was unreachable, or a person who has
not answered yet. Reading one, telling the states apart and naming the repair that is safe is
**`troubleshooting-jason`**.

**The effect may happen somewhere else.** If you or somebody else sends, enrols or replies by hand — through
the provider's own CLI, or because the managed path was not available — Jason does not know, and until it is
told its view of the world is wrong and it will act on that view: a second enrolment for somebody who already
had one, an approval question about work that is already done. Telling it is **`reporting-outside-effects`**,
and it is owed as soon as you know.

The chronicle is the record of all of it:

```
jason journal list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

## What this skill does not cover

The roles that perform background AI work and how each of them reasons; anything a campaign manager does with
a check-in; and installing plugins, writing routes or updating the installation. Those are other roles' jobs or
other skills, and guessing at them here would be worse than saying so.
