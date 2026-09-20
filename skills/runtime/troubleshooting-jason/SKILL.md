---
name: troubleshooting-jason
description: Use when work in Jason has stopped, failed or seems stuck and a person wants to know why - read the runtime, the work item's error code and the chronicle, tell the nine states apart, and name the repair that is safe.
metadata:
  status: verified
---

# Troubleshooting Jason

Work that has stopped is not always work that has broken. Most of the time the runtime is doing what it was
told: waiting for a moment that has not arrived, waiting for a person who has not answered, or refusing work
it cannot route. This skill is how to tell those apart from a genuine failure, and how to name the one repair
that is safe rather than the first one that comes to mind.

**Status: draft.** It covers what this build records and nothing else.

## Look here first, in this order

Four readings before you form an opinion. Each one can end the investigation on its own, and each one makes
the next easier to read.

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

Three further readings answer the states a work item alone cannot tell you about: what is parked on a
person's decision, what question a role has asked and nobody has answered, and what somebody has told Jason
about an effect produced outside it.

```
jason approval list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason decision list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason report list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
```

## The nine states

Nine things "it is stuck" turns out to mean. Decide which one you are looking at before you say anything to
anybody: four of them are not failures at all, three of them nobody can fix from a session, and only two are
about the work you asked for.

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
to is not active. Only an active campaign's work is claimed, so a paused, archived or still-draft campaign
holds every one of its items at once.

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

### 5. A transient provider failure

**How you recognise it.** The failure was classed `transient`, and the item has been tried more than once.
The attempt count on the item says how often; the chronicle says when.

**What it means.** The provider was unreachable or busy, the runtime already knows, and it decides when to
try again and when to stop trying. There is nothing for you to do while attempts remain. Report how many
there have been and what the last one said, and leave it to run.

### 6. A permanent or validation problem

**How you recognise it.** `input_invalid`, or a failure the plugin classed `permanent` or `validation`.

Two spellings to expect yourself to get wrong while you look: the campaign's own people are
`campaign list-contacts`, because `contact list` takes no campaign; and these listings print JSON already, so
there is no `--json` — `--human` is the one that changes the rendering.

**What it means.** The arguments do not satisfy the operation's published contract, or the provider refused
them outright. This is final by design, and it is final because asking again with the same arguments would
fail in the same way. The operation's own document is what the runtime enforces, and it is published with the
project's source under `docs/contracts/operations/`; **no verb prints one**, so on a machine with no copy of
the source, ask the operator for it rather than guessing at the correction.

### 7. An attempt that was interrupted

**How you recognise it.** The failure was recorded as `ambiguous`.

**What it means.** It means exactly one thing: **nobody can say whether the provider acted**. The attempt was
cut off somewhere between sending and hearing back, so the effect may have happened, may have half happened,
or may never have started. Where the operation's contract declares a recovery read, the next attempt asks the
provider what actually happened before it writes anything. Where it does not, the work stops there and a
person decides — because the alternative is sending the same message to the same human being twice.

### 8. An effect reported from outside

**How you recognise it.** A report against the campaign says something was already done — through a
provider's own tools, or by hand, or because the managed path was not available.

**What it means.** It is somebody's word that an effect occurred, and nothing else: nothing was approved,
routed, supervised or verified. It moves no work item, closes nothing, and explains no failure. It is context
for the person reading, and it may be the reason a second attempt would be a duplicate. Telling Jason about
such an effect is `reporting-outside-effects`.

### 9. An outdated or unavailable plugin

**How you recognise it.** `plugin_unavailable`, or a plugin that lists problems of its own.

```
jason plugin list --human
```

**What it means.** The package is there and the program it needs is not — missing, not executable, or a
version that does not answer. Such a plugin loads as unavailable rather than rejecting the whole snapshot, so
everything else keeps working and only its operations stop. The listed problem names what is wrong on the
machine.

## The repairs that are safe

Five. Nothing else on this list, and nothing off it.

**A corrected new work item.** The ordinary repair for states 6 and 7: read the contract, fix the arguments,
ask again.

**Writing or fixing a route.** The repair for `no_route`, and an operator's act rather than a session's.

**Reloading the plugins**, once a package has actually changed on disk. Every package is read again and the
snapshot is swapped whole, or the old one is kept and the reason is printed.

```
jason plugin reload --reason "the package was updated"
```

**Restarting the runtime**, when the process itself is the problem rather than anything in it. Work that was
in flight is picked up again afterwards.

```
jason runtime restart
```

**Applying an update**, when the installed version is the known cause. That is `operating-the-installation`.

## What is not a repair

**Do not retry blindly.** The runtime already retries everything worth retrying, on its own schedule, and it
counts the attempts. A failure it called final was called final for a reason — repeating it by hand produces
the same failure, or worse, a second real effect on a real person.

**A failed item is finished.** It is not reopened and it cannot be retried by hand. If the work is still
wanted, it is new work — work still wanted is new work, asked for again as a fresh item with corrected
arguments, with its own id, its own attempts and its own line in the chronicle. An item that merely expired
is the one thing the runtime itself reopens: moving its `due_at` forward brings it back, because it never
ran.

**Deciding an approval, or answering a question on the person's behalf**, is not a repair either, however
obvious the answer looks. The runtime refuses a decision that does not name a person, and it is right to.

**Editing anything under the data directory by hand** — the database, the chronicle, a plugin package whose
digest was already computed — is not a repair. The chronicle is append-only and enforced as such three times
over, and a package whose contents no longer match its digest stops running rather than running differently.

## What a useful report carries when it is none of these

When the nine states do not explain it, the useful thing to produce is a description somebody else can act
on, not a theory. **There is no command that collects a diagnostic bundle** in this version — say so plainly
rather than implying one exists, and gather the five things by hand:

- what was asked for, in one sentence: which campaign, which operation or role, with which arguments;
- the work item id and its error code, exactly as printed;
- what `jason runtime status` said, and the version it reported;
- the relevant chronicle lines, in order, with their timestamps;
- what you have already ruled out, and how.

A guess about the cause is not one of them. Name what you observed; let whoever reads it decide what it
means.

Logs are JSON Lines written under `~/.jason/logs/`, and nothing reads them for you: no command searches them,
summarises them or attaches them to anything. They are there to be opened when the readings above have not
settled it. They never contain request bodies, prompts, work-item contexts or the capability token, so do not
send somebody to the logs expecting to find what was actually sent to a provider — it was never written
there.
