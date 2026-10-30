---
name: managed-campaign-work
description: Use when a person wants Jason to start outbound work for them, such as getting meetings or running a campaign, and asks what to do first, and when an interactive agent session should turn an outbound objective into durable campaign work that Jason performs - creating a campaign, adding the people it is about, submitting managed provider operations and AI role work, and following what the runtime does with them; and when a person wants Jason to answer somebody who wrote back - answering a conversation in its own thread, filing, closing or reopening it - or says stop it or not yet about work Jason holds - stopping or holding work, from cancelling one work item to pausing the provider's campaign.
metadata:
  status: draft
---

# Managed campaign work

Jason owns campaign state; a conversation does not. Anything decided in a session that is not committed through
the Jason CLI has not happened: it will not survive the session, will not be dispatched, will not be approved by
anybody, and will leave no history. This skill is the managed path — how to put work where the runtime can
perform it, and what to do while it waits.

**Status: draft.** No agent host has read this text in the state it ships in. It covers the operations this build
publishes and nothing else.

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
package and writing a route is an operator's explicit act, not something to work around. That stop is for work that
reaches a provider: a project, a campaign and the people it is about need none, so they are made with no plugin
loaded, and the stop comes at the first work item that would reach one. To see where one operation would go for one
campaign:

```
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll
```

Setting one up is `operating-the-installation`, which is where plugins, routes, profiles and updates live.

The wiring check, `docs/golden-path.md`, connects a provider and proves it with the person's own address; it is
not campaign work. It is a synthetic walk the person takes or skips when the installation becomes ready, and asking
them once is `operating-the-installation`'s. The real work begins with the person's brief — what they tell you about
their first campaign, in their own words — and a campaign planned with the business pack's `campaign-planning` skill,
and this skill is how the brief and that plan become work the runtime and Jason's roles perform.

## What the person has already written down

Some of what the person wants was written down before this conversation, and you read it before any work is
created, in one order, the later winning where two say different things: the client's named preferences, who
messages are written as and how the company sounds; then the person's memory file for the client, then the
project's, then the campaign's; and last what they tell you now. The client's file never overrides a preference on
the preference's own subject: where the two disagree, the preference wins, and you tell the person so they can make
the two agree.

A memory file is the `AGENTS.md` in a level's folder. The campaign's folder is the `folder_path` that
`jason campaign get` answers with, the project's is the one `jason project get` answers with, and the client's is
the harness root, which `jason runtime status` reports as the root in force; where it names none, `Jason` in the home
directory. The root's `AGENTS.md` is the client's only where it carries its marker, the `.jason/marker.json` that says
the folder is the root: a folder without one is not Jason's, and its file is not read.

```
jason preference list
jason campaign get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason project get prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

Your host may have given you some of those files when this session started, and only some. Claude Code in print
mode, started in a campaign's folder, keeps that folder's file and drops, without asking, the ones that lie outside
it, the project's and the client's; Codex, by its documentation and not by a measurement, outside a git repository
reads only its own folder's file of these. So read by path every one of the three you were not given. They are the
person's standing guidance, not facts: where one disagrees with what the runtime records, the record is right, and no
role's note outranks them.

## The work itself

A project for what is sold, a campaign under it, the people the campaign is about, and then one work item per thing
to be done.

```
jason project list --query payroll
jason project create --name "Payroll for Latin America"
jason campaign create --name "Q3 LatAm founders" --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --context '{"offer":"payroll for startups hiring in Latin America","audience":"founders of seed-stage startups in Mexico and Colombia","their_words":"Our best customers came through their accountants. We sell to founders, never to HR."}' --idempotency-key q3-latam
jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file contacts.json --match-by email
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**What is sold is the project.** A project gathers the campaigns of one product, so it is named from what the person
sells, in their words, and looked for before it is made: `jason project list --query` with a word of that name answers
whether this product has one already. Make it only when there is none. A create refused with `project_exists` names, in
its details, the project that already has the slug: read it with `jason project get`, and where its name is this
product's, it is the one to use, since the first create's answer was lost; where it is another product's, the two
names make the same slug, so give the new project one of its own with `--slug`. The campaign then goes under it, named
with `--project` and the project's id: `Default` holds the campaigns made without one and is never the home of a named
product's first campaign, unless the person renamed it to that product's name, which makes it that product's project
and the one `jason project list --query` finds. The project a command could take from the folder it runs in is no
answer for that first create.

**`jason campaign list` follows the folder you are in.** Run inside a project's folder, it lists that project's
campaigns and no others, and it says so on stderr, where a command that reads JSON may never look. A campaign that is
not in the list is therefore not necessarily absent: it may belong to another project. Before you tell the person that
a campaign does not exist, list across projects with `jason campaign list --all-projects`; `--project` names one
other project by its id, so `--project all` answers `project_not_found`, and it cannot be combined with `--all-projects`.
The four reads of where people stand, `campaign summary`, `campaign activity`, `campaign activity-by-period` and
`campaign list-contacts`, are different: given no campaign they answer for every campaign not archived, in every
project, whatever folder they run in. So one folder can show a campaign through `campaign summary` that `campaign
list` does not list.

```
jason campaign list --all-projects
jason campaign list --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**The brief becomes the campaign.** What the person told you about the campaign — the product, the audience, how it
sells, in their own words — is the campaign's context, given at `jason campaign create` with `--context`, before any
contact is added and before anything is sent. It is the campaign's shared knowledge: a role that reads the campaign
(`jason campaign get`) reads it whole, and a role that does not works from its own work item's context alone. So its
keys are yours to name for what they hold, and none is reserved. Keep the person's own words beside what you made of
them, and put a link or a file in as what it says, briefly, with where it is, rather than pasted whole. This
`jason campaign create` makes Jason's own campaign; it is not the provider operation `campaign.create`, which makes a
campaign at a provider.

**A change of direction goes into the context as well.** On a campaign that already exists, what the person
changes about it is written with `jason campaign update-context`: `--set` writes the top-level keys it names,
`--unset` removes a key, once for each, and every key it does not name stays as it was.

```
jason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set '{"audience":"founders of seed-stage startups in Mexico, Colombia and Chile"}' --reason "the person widened the audience"
```

**Give every create a key you could send again.** `--idempotency-key` names that one create, in words you
choose. If its answer never comes back — a timeout, a session that ended mid-command — send the same command with
the same key: it answers with what the first one made, and nothing is made twice. A different request under a key
you already used is refused with `idempotency_key_reused`; give it a key of its own. Nothing makes a key up for you.

**The campaign's own people are `campaign list-contacts`**, each with where they stand in it: enrolled, paused,
finished or excluded. `contact list --campaign cmp_…` lists the same people who are not excluded, as contact records.
**`enrolled` here is Jason's own membership and nothing more.** It says the person is on this campaign in Jason, and
it is the state a person has the moment they are added. It does not say the person was put into a sequence at the
provider; that is a `campaign.enroll` work item that succeeded, and the provider's own list is read with
`enrollment.list`. A campaign with no provider sequence has members, never enrolments: say "on the campaign" or
"members", and never that they are enrolled at the provider, however `list-contacts` spells the state.
To find one person among everyone the runtime knows, give `contact list` any part of what they are known by — a
name, a company, an email, a phone however it is spaced, a LinkedIn link:

```
jason contact list --query "marta@"
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
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.enroll --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input "{\"campaign\":{\"external_id\":\"7\"},\"channel\":\"email\",\"collision\":\"skip\",\"start\":{\"position\":\"first_step\"},\"first_touch\":\"authored_delay\"}" --idempotency-key q3-latam-enrol-marta
```

The arguments are validated when the item is created, so a refusal here is about what you asked for, not about
the provider. Read the operation's own document before you invent a field: it is the same document the runtime
enforces, and it is published with the project's source, under `docs/contracts/operations/`. **No verb prints
one**, so on a machine with no copy of the source you cannot read it from here — ask the operator for it rather
than guessing a field, because a guess is refused at `workitem.create` anyway.

Work that needs a model rather than a provider is an `ai_role` item, named for the role that should do it:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role researcher --context '{"account":"Analytical Engines","question":"which of the two sites is the target"}' --idempotency-key q3-latam-research-ae
```

Then follow the work:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**An item that is `waiting` is neither stuck nor finished.** A run of it raised other work and ended by waiting for
it, or for a moment, and the runtime starts it again, by itself, when that has happened. While it waits it holds no
slot, and the work it waits for runs. When the person asks what it is waiting for, read them its `wait` block from
`workitem get`: `on` is the items it waits for, each with its status now; `mode` is `all` or `any`, whether every one
of them must end or the first is enough, and null for a wait on a moment alone; `until` is the moment it named, if it
named one; `deadline` is when the wait ends whatever has happened; and `opened_at` is when it began. With `--human`
the same block reads as "Waiting for:", "Wait opened:" and "Wait ends by:". A `workitem list` carries the block too,
with `on_count` and `ended_count` — how many items it waits for, and how many of them have ended — in place of `on`;
`get` has the items. Do not create its work a second time. Where the person decides it has waited enough — the replies
that are in are enough to decide on — they end the wait, and the item is started again with what is there:

```
jason --actor human:ada workitem resume wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "enough replies are in to decide"
```

**What is blocked.** An item can also wait on a provider block: its `wait` names the block under `blocked_by` and
waits on no item and no moment, because the provider refused the account, or one sender under it, rather than the
work. When the person asks what is blocked, `jason block list` shows each open block, the `held_count` of items it
holds and the `link` where they clear it at the provider, and `workitem list --block` names the items one holds:

```
jason block list --human
jason workitem list --block blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

The block releases its items by itself, so do not resume, cancel or create them again.

Only a waiting item can be resumed; to stop it altogether is `jason workitem cancel`, below.

What is planned, soonest first and undated work last, rather than a walk through every page; and how many items
match, with any filter `list` takes, rather than paging to count them:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status created --order due
jason workitem count --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

What has happened to one person, newest first — changes to their record, the work done for them, the decisions about
it and the replies Jason noticed — and everyone listed by what last happened to them, the most recent first:

```
jason journal list --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason contact list --order activity
```

Where the campaign's people stand now — waiting on a person, failed, working, new, idle, in the provider's sequence
or out of it — what happened to them in a period, and exactly who is behind one of those numbers, each with why. The
counts are of people, so one person may be in several:

```
jason campaign summary cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign activity cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-10-09T00:00:00Z --until now
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --summary failed
```

Name several campaigns, or none for every campaign not archived, in every project, and each answer also carries
`total`: the same counts across them, a person counted once however many of them count the person; `list-contacts`
then lists each person once, with the campaigns that count them. How one count moved over a window, an hour, a day
or a week at a time:

```
jason campaign activity-by-period --count replied --period day --since 2026-10-09T00:00:00Z --until now
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

A role keeps a note at each of three levels — its craft for all of this installation's work, a project's, and a
campaign's, where it keeps what it looked at, what it ruled out and what is still open — and reads them again on
its next run, the client's first and the campaign's last, the lower winning where two differ. The launch envelope
names where they are; a note itself is read when it is wanted:

```
jason rolenote get --role researcher
jason rolenote get --role researcher --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason rolenote get --role researcher --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason rolenote set --role researcher --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --note '{"gatekeeper":"the switchboard hangs up after six"}'
```

A note is written **whole** — there is no patch, so read it, add to it and write all of it back — and
it is at most 64 KiB of JSON object. A level nobody has written at yet answers with an empty note rather
than an error.

**It is not authoritative.** It is one role's own account, in its own words, from whenever it was
written. Where a note disagrees with what the runtime says now, the runtime is right, and no note outranks
what the person has written down or tells you. Read a note for where to look first; check what it claims
before you repeat it. Campaign context is the campaign's shared knowledge and a different thing — do not
write one into the other.

## Three things that will happen to work you created

Each of them is a skill of its own, because each is a different moment and a person arrives at it saying
something different. What belongs here is only that they exist.

**It may stop and wait for a person.** An operation whose contract says a person must confirm it parks at the
claim: the item's status becomes `awaiting_approval`, no attempt is made, and nothing is sent to the provider.
The decision is a person's — a session must not make it, and the runtime refuses a decision that does not name
one. Presenting what is waiting, and recording what the person says about it, is **`approvals-and-questions`**.

**A question a role raised is answered by recording the answer, and the campaign's manager acts on it.** When the
person answers a question a launched role raised, you record it with `jason decision answer` and the person as
`--actor`, and you stop there. The role that asked has already ended: the answer releases the campaign manager's next
check-in, and the manager creates the work the answer implies. If you create it as well, the campaign holds the same
work twice. That check-in runs only for an active campaign and at the next scan, and a check-in already open does not
consume the answer, so read `jason campaign get` for the status. If the campaign is not active, tell the person that
nothing acts on the answer until it is. If it is active and no new work has appeared yet, say the check-in has not run
yet and create nothing in its place. Read the work items the check-in makes before you offer any, and create work
yourself only where the person asks for something the answer did not cover.

**It may stop with a code.** A work item that failed carries an error, and the code is the answer: it says whether
this is somebody's configuration, your arguments, a provider that was unreachable, a provider that does not offer
what you asked for yet, or a person who has not answered yet. Reading one, telling the states apart and naming the
repair that is safe is **`troubleshooting-jason`**.

**The effect may happen somewhere else.** If you or somebody else sends, enrols or replies by hand — through
the provider's own CLI, or because the managed path was not available — Jason does not know, and until it is
told its view of the world is wrong and it will act on that view: a second enrolment for somebody who already
had one, an approval question about work that is already done. Telling it is **`reporting-outside-effects`**,
and it is owed as soon as you know.

The chronicle is the record of all of it:

```
jason journal list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

## Answering a conversation

Somebody wrote back, and the person wants Jason to answer them, file the thread, set it aside or put it back. Each
of those is a managed provider operation, a work item like the enrolment above, with a document of its own:
`docs/contracts/operations/conversation.reply.md`, `docs/contracts/operations/conversation.classify.md`,
`docs/contracts/operations/conversation.close.md` and `docs/contracts/operations/conversation.reopen.md`. Each says,
once, what the operation reaches and whether that can be undone, the approval it needs and the arguments its
`--input` carries; read the one you need before you type its line.

**The campaign has to be active** for an answer to run and for Jason's read of the campaign's threads to notice a
reply: each of the four documents says what its work does in a paused campaign, and a paused campaign's threads are
not read. After a pause, `jason campaign start` comes first.

**Read the thread before you act on it.** A reply the runtime noticed is an `inbound_reply_observed` line in the
chronicle, or a `provider_event_observed` line of kind `reply_detected` where the provider's own events reported it,
so list both kinds. Either line names the thread, its channel and the person (`contact_id`) but never what anybody
wrote. So when the person asks you to answer one, read the thread first and tell them what you read — who wrote last,
when, and what they said — before anything is drafted.

```
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind inbound_reply_observed --kind provider_event_observed
jason contact threads cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason contact thread cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD t_99812
```

`jason contact thread` reads the thread from the provider now and records nothing. It and `jason contact threads`
refuse with `no_route` when no current route reaches the plugin that learned the person's identifier, and take
`--plugin` for a person several providers know. A `conversation.get` work item reads the thread as work instead, and
keeps what it read as the item's result (`docs/contracts/operations/conversation.get.md`).

**A line that names no person is read by its thread.** Its `contact_id` is null when no contact, or more than one, is
pinned at that plugin with the identifier the provider gave, so there is nobody to name to `jason contact thread`.
Read it with a `conversation.get` work item in the line's campaign, which needs only the thread. A reply then needs
the person the thread is with: find them with `jason contact list --query`, or have the person name them, and create
the reply in that same campaign.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.get --input '{"thread":{"external_id":"t_99812"},"limit":20,"offset":0}' --reason "the reply Jason noticed names no person" --idempotency-key q3-latam-read-t99812
```

**A reply goes into the thread the person wrote in, and each send is approved on its own.** `conversation.reply`
answers inside the thread it names and reaches nobody else: never a new thread, and never a message to somebody
outside one, which is another operation and not how a reply is answered. It reaches a person, so it parks for a
person's approval, and one approval is one send: one work item is one reply to one person, its `body` exactly the
text the person read with you.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.reply --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input '{"channel":"email","thread":{"external_id":"t_99812"},"body":"Thursday at 10 works. I will send an invitation."}' --reason "the person's answer to Marta's reply" --idempotency-key q3-latam-reply-marta-t99812
```

Recording their decision on it is `approvals-and-questions`. Two replies are two work items and two approvals, never
one item for both.

**Closing and reopening a thread reach no person.** `conversation.close` sets it aside as worked, and
`conversation.reopen` puts it back among those that need attention. **Filing it may reach one.**
`conversation.classify` files it under one of the provider's own categories, as the provider names them, and
a category the provider reserves carries that provider's own meaning with it, which filing there asks for too: its
document's section "The provider's own meaning on its reserved names" says what that can be, and a campaign that must
own every send files under categories of its own, and lists them in the campaign's context under
`filing_categories`, written as the provider names them, when you set it up: the responder Jason launches for
a reply files a thread under one of those and under nothing else. The `Interested` below is only an example: whether a
category of that name is reserved at the provider, and what filing there sets off, is that provider's. None of the three is a way
to stop writing to somebody: that is `optout.record` or `enrollment.stop`, each with a document of its own.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.classify --input '{"thread":{"external_id":"t_99812"},"category":{"name":"Interested"}}' --reason "Marta asked for a call" --idempotency-key q3-latam-classify-t99812
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.close --input '{"thread":{"external_id":"t_99812"}}' --reason "the call is booked" --idempotency-key q3-latam-close-t99812
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.reopen --input '{"thread":{"external_id":"t_99812"}}' --reason "Marta wrote again" --idempotency-key q3-latam-reopen-t99812
```

## Stopping or holding work

A person who says "stop it" or "not yet" means one of four acts, and each stops something different. Two of them
share a name: `jason campaign pause` stops Jason's own work and leaves the provider sending; only a work item naming
the operation `campaign.pause` stops the provider's campaign, so when the provider has to stop, that work item is the
one to create.

**`jason workitem cancel` ends one work item for good.** A running attempt is stopped, and an approval the item was
parked on ends with it. It touches no provider: what an attempt already did there stays done, and taking it back is
work of its own.

**`jason workitem update` holds or reorders one work item without ending it.** `--not-before` keeps it from starting
before the moment it names, and `--priority` moves it ahead of the campaign's other work or behind it, the higher
first. Neither stops an attempt already running, and neither changes any other item.

**`jason campaign pause` pauses Jason's campaign.** Nothing more of it is claimed until `jason campaign start`
resumes it — what is already running finishes — except the provider operations whose documents say they run in a
paused campaign, the provider's pause among them. It does not stop the provider's sequence: what the provider was
sending, it goes on sending.

**The provider operation `campaign.pause` pauses the provider's campaign**, which stops it sending. It is an act at
the provider, made as a work item and approved as its document says, like any other: its document,
`docs/contracts/operations/campaign.pause.md`, gives it `auto` approval, so nobody is asked when it is claimed, and the
person's word in this conversation is the only one it gets. Create it when they say so, and not before. It runs even
while Jason's campaign is paused. The pause printed below gives no identifier of the provider's campaign, so it works
for a campaign linked there, whose pin names it, and fails at the claim for one that is not; its document says how the
first pause names the provider's campaign.

**When the wrong copy already went out**, stop the provider first, and free the campaign for that before anything
else: a campaign runs one work item at a time, and while one of its items is scheduled or running, nothing more of it
is claimed, the provider's pause included, whatever its `--priority`. So cancel the item that is running or scheduled
to send the copy, then create the provider's `campaign.pause` with a high `--priority`, then pause Jason's campaign so
that nothing more of Jason's follows, and tell the person the provider has stopped only once that work item has
succeeded. Cancelling a provider operation that was already running leaves one thing unknown — whether it reached the
provider before it was stopped — so do not tell the person it did not go out; and a reply that went out cannot be
taken back. Starting the provider's campaign again later is `campaign.activate`, with a document of its own.

```
jason workitem cancel wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "the person stopped this send"
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.pause --priority 100 --input '{"reason":{"code":"content","note":"the wrong copy went out in step 2"}}' --reason "the person asked the provider to stop sending" --idempotency-key q3-latam-pause-wrong-copy
jason campaign pause cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "the wrong copy went out in step 2"
jason workitem update wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --not-before 2026-11-02T09:00:00Z --reason "the person said not before Monday"
jason workitem update wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --priority 10 --reason "the person wants this one first"
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "the corrected copy is in place"
```

## What this skill does not cover

The roles that perform background AI work and how each of them reasons; anything a campaign manager does with
a check-in; and installing plugins, writing routes or updating the installation. Those are other roles' jobs or
other skills, and guessing at them here would be worse than saying so.

A check-in is the launched manager's. A session that keeps a campaign moving asks the person and commits what they
decide, as above, and `approvals-and-questions` and `troubleshooting-jason` cover what is waiting and what stopped.
