---
name: troubleshooting-jason
description: Use when work in Jason has stopped, failed or seems stuck and a person wants to know why - read the runtime, the work item's error code and the chronicle, tell the eleven states apart, and name the repair that is safe; when none of them explains it, collect the installation's diagnostic file with jason doctor — including when the person says go ahead with the diagnostic file, write it, or collect the diagnostics.
metadata:
  status: draft
---

# Troubleshooting Jason

Work that has stopped is not always work that has broken. Most of the time the runtime is doing what it was
told: waiting for a moment that has not arrived, waiting for a person who has not answered, or refusing work
it cannot route. This skill is how to tell those apart from a genuine failure, and how to name the one repair
that is safe rather than the first one that comes to mind.

**Status: draft.** No agent host has read this text in the state it ships in. It covers what this build records and
nothing else.

## Look here first, in this order

**Before any `jason doctor` — the dry run included — run these four readings, in this order. A diagnostic file
is for what they cannot explain, and you cannot know that before you have read them.** Each one can end the
investigation on its own, and each one makes the next easier to read.

**Is the runtime answering at all?** If it is not, nothing anywhere is being claimed, and every item in the
installation looks stuck for the same reason.

```
jason runtime status --human
```

**The item itself** — its status, its error code, how many attempts it has had, and which profile it ran
under. The code is the answer; everything below is how to read it.

```
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
```

**The rest of the campaign**, because one item stopping and every item stopping are different problems with
different repairs.

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status failed --status expired
```

**The chronicle**, which is what happened and in which order — claims, attempts, outcomes, reloads. It is the
only reading that shows you a sequence rather than a snapshot.

```
jason journal list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-09-18T09:00:00Z
```

**When a notification brought them here.** A person may arrive because a notification said protective actions
failed in campaigns it names by id. The failed and expired work items of those campaigns are the subject: the
third reading above, once for each id it names, and without `--campaign` when it says there are more. Each item
it lists is then read as this skill reads a failed item. The four readings are binding here too, before anything
else; here the third comes before the second, because it is what names the items.

Three further readings answer the states a work item alone cannot tell you about: what is parked on a
person's decision, what question a role has asked and nobody has answered, and what somebody has told Jason
about an effect produced outside it.

```
jason approval list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason decision list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason report list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
```

## The eleven states

Eleven things "it is stuck" turns out to mean. Decide which one you are looking at before you say anything to
anybody: five of them are not failures at all, four of them nobody can fix from a session, and only two are
about the work you asked for.

**The codes named below are the common ones, not all of them.** The runtime publishes more than two dozen
attempt error codes, and one you do not find here is not a code that means nothing — read what it says, and
if it names something to change, that is the answer.

### 1. Waiting for its time

**How you recognise it.** The status is `created` and the dispatcher would not claim it: a `not_before` or a
`retry_after` is still in the future, or a `due_at` has already passed and the item can now only expire.

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status created --not-eligible
```

**What it means.** Nothing is wrong. The item is doing what it was asked to do, and the runtime will claim it
when the moment arrives. Say when that is, and stop.

### 2. Paused or archived

**How you recognise it.** The item looks perfectly healthy and is never claimed — and the campaign it belongs
to is not active. Only an active campaign's work is claimed, with one exception: a paused campaign still lets
through a `provider_op` item whose operation declares `dispatch.in_paused_campaign: runs` (`optout.record` and
`campaign.pause`). So an archived or still-draft campaign holds every one of its items at once, and a paused one
holds every item but theirs.

**What it means.** This is about the *campaign*, not the item, which is why reading the item over and over
tells you nothing. Somebody paused it, or it was never started. Say which campaign and in which status; the
person decides whether it should be running.

### 3. Awaiting a person

**How you recognise it.** The item's status is `awaiting_approval`, or there is a pending decision the
campaign is waiting on. No attempt was made and nothing reached a provider.

**What it means.** This is not a failure and there is nothing to repair. An operation whose contract says a
person must confirm it parks at the claim, and a question a role could not answer waits the same way. It
moves when a person decides, and a session must not decide for them. Presenting what is waiting is
`approvals-and-questions`.

### 4. Missing configuration

**How you recognise it.** The error code is one of `no_route`, `profile_not_found`, `profile_disabled`,
`host_not_available` or `role_skill_invalid`. To see where one operation would go for one campaign, and why
it goes nowhere:

```
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll --human
```

**What it means.** Nobody's work item will fix this. Somebody must configure something — install a plugin,
write a route, enable a profile, make an agent host available, correct a role's skill — and that is an
operator's deliberate act. Nothing was started and nothing was spent. Name what is missing and hand it over;
setting it up is `operating-the-installation`.

### 5. The person is on the do-not-contact register

**How you recognise it.** The error code is `suppressed`, on an operation that reaches somebody.

```
jason suppression list --human
```

**What it means.** That channel value is on the do-not-contact register, so this campaign does not reach them
there. Nothing was sent and nothing was attempted at the provider — the register is consulted before any of
that — and this is the register **working**. It is final by design: the item is finished, and a second item
for the same person on the same value would stop in exactly the same place.

**The repair is not yours.** Lifting a suppression is `jason suppression remove`, and it is a person's act on
another person's behalf — somebody asked not to be contacted, and a session that lifted that to get its work
moving would be the one failure in this whole document that reaches a real person. Say who is suppressed and
on which value, and let the person decide. If the campaign should reach them somewhere else, that is new work
on a channel they have.

### 6. A transient failure

**How you recognise it.** The failure was classed `transient`, and the item has been tried more than once.
The attempt count on the item says how often; the chronicle says when.

**What it means.** Usually the provider was unreachable or busy. Sometimes the moment was the runtime's own
and no provider was asked anything: at the claim, a schema check did not finish in time, and the attempt
ended `validation_timed_out` — which, despite its name, found nothing wrong with the item, so it is not
state 7. Either way the runtime already knows, retries it the same way, and decides when to try again and
when to stop trying. There is nothing for you to do while attempts remain. Report how many there have been
and what the last one said, and leave it to run. The same code classed `ambiguous` is state 8: there the
answer, not the item, went unmeasured, after the provider had acted.

### 7. A permanent or validation problem

**How you recognise it.** `input_invalid`, or a failure the plugin classed `permanent` or `validation`.

Three spellings to expect yourself to get wrong while you look. **A campaign id is an argument, not an
option**: it is `campaign list-contacts cmp_…`, and `campaign list-contacts --campaign cmp_…` is refused —
which is the one every session so far has reached for first. The campaign's own people, each with where they
stand in it, are that verb, and its `enrolled` is Jason's own membership, never enrolment at the provider;
`contact list --campaign cmp_…` lists the ones not excluded. And these listings print JSON already, so there is no `--json`;
`--human` is the option that changes the rendering.

**What it means.** The arguments do not satisfy the operation's published contract, or the provider refused
them outright. This is final by design, and it is final because asking again with the same arguments would
fail in the same way. The operation's own document is what the runtime enforces, and it is published with the
project's source under `docs/contracts/operations/`; **no verb prints one**, so on a machine with no copy of
the source, ask the operator for it rather than guessing at the correction.

**A failure classed `blocked` is final too, and its repair is elsewhere.** Its code is `provider_blocked`: the
provider refused the account, or one sender under it, rather than this item's arguments — money or a plan, an
allowance it counts, a key it no longer accepts, a channel disconnected or paused — and did nothing for the call.
`last_error.block` on the item says what is blocked (`scope`, and `sender_external_id` when it is one sender), why
(`reason`), where a person goes to clear it (`link`), and, where the provider said, when it may clear by itself
(`clears_at`). Read those to the person. The repair is theirs, at the provider — topping up, reconnecting, renewing a
key, or waiting for an allowance to renew — and never an edit of the item; once the block has cleared, the work is
proposed again. Work a block holds does not end here: it waits on the block, which is state 11.

### 8. An attempt that was interrupted

**How you recognise it.** The failure was recorded as `ambiguous`.

**What it means.** It means exactly one thing: **nobody can say whether the provider acted**. The attempt was
cut off somewhere between sending and hearing back, or its answer could not be measured, so the effect may
have happened, may have half happened, or may never have started. Where the operation's contract declares a
recovery read, the next attempt asks the provider what actually happened before it writes anything. Where it
does not, the work stops there and a person decides — because the alternative is sending the same message to
the same human being twice.

### 9. An effect reported from outside

**How you recognise it.** A report against the campaign says something was already done — through a
provider's own tools, or by hand, or because the managed path was not available.

**What it means.** It is somebody's word that an effect occurred, and nothing else: nothing was approved,
routed, supervised or verified. It moves no work item, closes nothing, and explains no failure. It is context
for the person reading, and it may be the reason a second attempt would be a duplicate. Telling Jason about
such an effect is `reporting-outside-effects`.

### 10. An outdated or unavailable plugin

**How you recognise it.** `plugin_unavailable`, or a plugin that lists problems of its own.

```
jason plugin list --human
```

**What it means.** The package is there and the program it needs is not — missing, not executable, or a
version that does not answer. Such a plugin loads as unavailable rather than rejecting the whole snapshot, so
everything else keeps working and only its operations stop. The listed problem names what is wrong on the
machine. `jason status` names such a plugin, with its problem codes, in its optional `plugin_availability`
check, and an unavailable notification plugin delivers nothing.

### 11. Waiting

**How you recognise it.** The status is `waiting`: a run of the item raised other work and ended by waiting for
it, for a moment, or both. Its `wait` block says on which items, whether all of them or any one, and until when.
A park on a provider block reads as this same `waiting` state with its block named, not as a twelfth.

```
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status waiting --human
```

**What it means.** Nothing is wrong, and the item is not holding anything up: while it waits it holds no slot,
and the work it waits for is claimed like any other. The runtime starts it again by itself when that work has
ended, when the moment it named comes, or when the wait's deadline does, whichever is first. If it seems to
wait too long, look at what it waits for — an item there may itself be in one of the states above. There is no
repair while the wait holds: do not cancel it to unstick it, and do not create its work again. Where the person
decides it has waited enough, they end the wait themselves, and the item is started again with what is there:

```
jason --actor human:ada workitem resume wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "the replies in are enough to decide"
```

**A park on a provider block has a repair, and it is the person's.** Its `wait` names the block under `blocked_by` —
`provider_block_id`, `scope` and `reason` — and waits on no item and no moment, and its `last_error` is
`provider_blocked`, whose `block` says what is blocked, why, the `link` where a person clears it and, where the
provider said, `clears_at`. `jason block list` shows every open block, how many items each holds and its link;
`block get` reads one block, and `workitem list --block` names the items it holds:

```
jason block list --human
jason block get blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason workitem list --block blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
```

The provider refused the account, or one sender under it, and not this item, so the person repairs it: they fix the
cause at the provider, through the link, and the block's trial then lets one item through; once the provider takes it,
the block clears and releases everything it held. Where the person knows the cause is gone, they clear the block
themselves, under their own name, and the next scan releases what it held:

```
jason --actor human:ada block clear blk_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "the card on the account was renewed"
```

A clear is their word that the cause is gone, not a mute, and where the provider still refuses, the next call opens a
new block. Do not cancel, recreate or resume a parked item: the block releases it by itself, and the same work, asked
again or resumed, parks on the same block.

## The repairs that are safe

Five. Nothing else on this list, and nothing off it.

**A corrected new work item.** The ordinary repair for state 7: read the contract, fix the arguments, ask
again. When state 6 runs out of attempts the item fails final the same way, and the repair is the same work
asked again as a new item once the provider is back; nothing in the arguments changes.

**Writing or fixing a route.** The repair for `no_route`, and an operator's act rather than a session's.

**Reloading the plugins**, once a package has actually changed on disk. Every package is read again and the
snapshot is swapped whole, or the old one is kept and the reason is printed.

```
jason plugin reload --reason "the package was updated"
```

**Restarting the runtime**, when the process itself is the problem rather than anything in it. What that does
to work in flight is narrower than it sounds: a shutdown stops claiming, gives handlers up to ten seconds, and
**does not kill children that are still running** — their leases are still good, and a survivor completes
against the next runtime. An attempt that was only **scheduled** is released back to `created` and claimed
again; an attempt already **processing** is left to the lease and the heartbeat to decide, and a `provider_op`
whose answer was lost that way ends **`ambiguous`**, which is state 8 and waits for a person. So a restart
recovers the queue; it does not un-lose an answer.

A restart is a stop you run yourself: ask first, as "Starting and stopping it" in `operating-the-installation` says.

```
jason runtime restart
```

**Applying an update**, when the installed version is the known cause. That is `operating-the-installation`.

## What is not a repair

**Do not retry blindly.** The runtime already retries everything worth retrying, on its own schedule, and it
counts the attempts. A failure it called final was called final for a reason — repeating it by hand produces
the same failure, or worse, a second real effect on a real person.

**A failed item is finished.** It is not reopened and it cannot be retried by hand. If the work is still
wanted, it is new work — work still wanted is new work, asked for again as a fresh item, its arguments
corrected where they were wrong, with its own id, its own attempts and its own line in the chronicle. An
item that merely expired is the one thing the runtime itself reopens: moving its `due_at` forward brings it
back, because it never ran.

**Deciding an approval, or answering a question on the person's behalf**, is not a repair either, however
obvious the answer looks. The runtime refuses a decision that does not name a person, and it is right to.

**Editing anything under the data directory by hand** — the database, the chronicle, a plugin package whose
digest was already computed — is not a repair. The chronicle is append-only and enforced as such three times
over, and a package whose contents no longer match its digest stops running rather than running differently.

## What a useful report carries when it is none of these

When the eleven states do not explain it, the useful thing to produce is a description somebody else can act
on, not a theory — and `jason doctor` collects the installation's half of it into one file, whether or not
the runtime answers.

```
jason doctor --dry-run
jason doctor
```

**Run the dry run first and show the person what it lists**, because the file is theirs to share or not. It
writes nothing; it lists what a plain run would write, and where. **Then ask the person one question — may I
write the file? — and stop. Offer no alternatives; a report written instead, or beside it, is not what they
were asked about.** The bundle holds the versions; whether the
runtime answered and what it says about itself; the shape of the settings — keys, types and lengths, with a
value only for the settings its own list names; the work items counted by kind and status, and the newest
failures by their codes; recent errors and a bounded slice of the logs; and the chronicle's metadata — how
many entries of each kind and when, never what they say.

What it leaves out is just as deliberate. What the runtime says about itself travels with a few free-text
fields withheld, and the bundle names each one. The home directory, a data directory outside it — this one,
or another the runtime reports — and the names of the account and the machine are rewritten before anything
is written. It never carries the capability token, a request body or a prompt, and it reaches nothing beyond
this machine. It writes one file and prints where; **sending it anywhere is the person's act, never yours.**
`--include-journal` adds what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a
project link recorded, campaign and
work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the
do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with, the
errors attempts ended with, provider identifiers, the effect, tool and operation a report names, the failure
text and receipts notification channels reported, entries people and agents wrote themselves, the names actors
gave, and the reasons they typed — so use it only when the person asks for it, and pass on the warning it
prints.

The bundle is not the report. It carries what the installation knows; the report needs four things it
cannot know:

- what was asked for, in one sentence: which campaign, which operation or role, with which arguments;
- the work item id and its error code, exactly as printed;
- the relevant chronicle lines, in order, with their timestamps;
- what you have already ruled out, and how.

A guess about the cause is not one of them. Name what you observed; let whoever reads it decide what it
means.

Logs are JSON Lines written under `~/.jason/logs/`, and `jason logs` reads them without asking the runtime,
so it answers the same when the runtime is stopped or will not start. It filters by level, campaign, work
item and time, and returns the newest hundred events that match, oldest first, unless `--limit` says
otherwise:

```
jason logs --level warning --since 2026-09-18T09:00:00Z
jason logs --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

Read them when the readings above have not settled it. They never contain request bodies, prompts, work-item
contexts or the capability token, so do not send somebody to the logs expecting to find what was actually
sent to a provider — it was never written there.
