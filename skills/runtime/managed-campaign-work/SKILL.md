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

## When you did it yourself, outside Jason

Sometimes the effect happens somewhere else: you sent the email through the provider's own CLI because the
managed path was not available, or somebody asked you to do it by hand and you did. That is allowed — Jason
does not own every tool you have. What is not allowed is leaving Jason believing it never happened. Until you
say so, Jason's view of the world is wrong and it will act on that view: a second enrolment for somebody who
has already had one, an approval question about work that is already done. Report it as soon as you know, in
the session where you know it.

```
jason report submit --actor human:ada --effect email_sent --tool reply-cli --summary "Sent the intro by hand after the enrolment failed." --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll --provider reply --account team@example.com --occurred-at 2026-09-17T11:04:00Z --unknown observed_at --idempotency-key intro-marta-1
```

It is recorded as your word and as nothing else. Jason does not route it, verify it, retry it, or move any
work item because of it — it writes down what you said, who said it and when it arrived, and shows it where
somebody reading the campaign later will find it, including on the work item itself, listed apart from that
item's own attempts so that neither can be mistaken for the other.

Four things to get right:

- **Say what you do not know with `--unknown <field>`, rather than guessing it.** A guessed timestamp is
  worse than a missing one, because nobody reading it afterwards can tell it was a guess. `--uncertainty`
  takes prose for whatever the field names cannot carry.
- **`--account` names an identity — a mailbox, a workspace, a login. Never a credential.** Jason holds no
  credential anywhere, and a report is something people read.
- **Give `--idempotency-key` a value of your own.** If the submission fails halfway and you send it again,
  the same key answers with the same report instead of recording a second effect. Without a key, a resend of
  the same words is matched by its content and still answers with the first report.
- **Use a fresh key when the same effect genuinely happened twice.** Two sends really did reach that person,
  and a key each puts both on record. With no key, the second is taken for a repeat of the first and you
  will never see it again.

Read them back the way you read anything else:

```
jason report list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
```

What admission does and deliberately does not establish — and why nothing is held or reconsidered
automatically — is in `docs/reports.md`.

## What this skill does not cover

Background AI work and the roles that perform it; anything a campaign manager or planner does with an
approval; and installing plugins or writing routes. Those are other people's jobs or other skills, and
guessing at them here would be worse than saying so.
