---
name: reporting-outside-effects
description: Use when an effect on a person happened outside Jason and Jason does not know - you or somebody else sent, enrolled or replied by hand or through another tool - so that it is recorded as what was done, by whom, and when, before Jason acts on a view of the world that is wrong.
metadata:
  status: draft
---

# Reporting effects that happened outside Jason

Jason knows about the work it performed itself. An effect produced anywhere else — a mail you sent by hand, a
person somebody enrolled through the provider's own CLI, a reply typed into a web app because the managed path
failed — leaves no trace here at all. This skill is how you tell it, and what telling it is worth.

**Status: draft.** It covers what this build records and nothing beyond it.

## Why this matters at all

Until Jason is told, its view of the world is wrong, and **it will act on that view**: a second enrolment for
somebody who has already had one, an approval question about work that is already done, a manager's review that
reads a person who has already answered as a person nobody has reached. None of that is fixed by being careful
in the conversation, because the conversation ends and the runtime goes on.

A report is **your word and nothing else**. Jason does not route it, does not verify it, does not retry it, and
**moves no work item** because of it. What it does is write down what you said, who said it and when it arrived,
and show it where somebody reading the campaign later will find it — including on the work item itself, listed
apart from that item's own attempts, so neither can be mistaken for the other.

That is a small promise. It is also the useful one: the next person, and the next session, can see that the mail
already went, and not send it again.

## What to report and when

Anything that reached a person and did not go through Jason: something sent, somebody enrolled, a reply
written, a person paused or unsubscribed. It does not matter whether you did it or watched somebody else do it,
or whether it was deliberate or a repair after something failed — what matters is that it happened and Jason
does not know.

Report it **as soon as you know, in the session where you know it.** There is nothing that will remind you
later, and an effect you meant to record is an effect nobody recorded.

```
jason report submit --actor human:ada --effect email_sent --tool reply-cli --summary "Sent the intro by hand after the enrolment failed." --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll --provider reply --account team@example.com --occurred-at 2026-09-17T11:04:00Z --unknown observed_at --idempotency-key intro-marta-1
```

`--effect` is free text in your own vocabulary — Jason does not interpret it — and `--tool` says what it was
done with: a CLI, a script, a person at a keyboard. `--summary` is the one sentence somebody reading the
campaign in a month actually needs. The campaign, contact and work item are optional and are checked only for
existing: an id with nothing behind it is refused as the typo it is, and a contact the campaign has never heard
of is admitted, because that is exactly the kind of fact a report exists to carry.

Who you are is not optional. Name yourself with `--actor`, as `human:<id>` or `role:<id>`; a report with no
author is not provenance, it is a rumour.

## The four things to get right

**1. Say what you do not know with `--unknown <field>` rather than guessing it.** A guessed timestamp is
worse than a missing one, because nobody reading it afterwards can tell it was a guess — it looks exactly like
a time somebody checked. `--unknown` repeats, and each one must name a real field of the report, such as
`occurred_at` or `observed_at`. For whatever the field names cannot carry, `--uncertainty` takes prose: say
that you are sure the mail went and unsure which mailbox sent it.

**2. `--account` names an identity — a mailbox, a workspace, a login — and never a credential.** Jason holds
no credential anywhere, and a report is something people read: it comes back in listings, on the work item, and
to whoever reads the campaign next. `--account team@example.com` is the whole shape of it.

**3. Give `--idempotency-key` a value of your own.** If the submission fails halfway and you send it again, the
same key answers with the same report instead of recording a second effect. Without a key you are not
unprotected — a resend of the same words is matched by its content and still answers with the first report —
but the key is the one that still holds when you fix a typo in the summary and send it again.

**4. Use a fresh key when the same effect genuinely happened twice.** Two sends really did reach that person,
and a key each puts both on record. With no key the second is taken for a repeat of the first and you will
never see it again — which is the one way an honest report can be quietly lost, and the only way to prevent it
is to have said so at the time.

## Reading them back

```
jason report list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason report get rpt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

The listing narrows by campaign, contact, work item, the operation the reporter named, and `--since`; it is
oldest first and pages like every other listing. It is deliberately thin — who reported what, with which tool,
about what — and `jason report get` has the whole assertion as it was submitted, word for word, because words
that were tidied up on the way in are no longer the words anybody said.

Every reading says out loud that it is unverified, and `--human` prints that sentence above the rendering
rather than leaving a reader to assume more. A work item's own view carries what has been reported about it
beside its attempts, and a filter naming a campaign that does not exist is refused rather than answered with an
empty page.

## What admission does and deliberately does not do

It gives the report an `rpt_…` id, stamps the moment it arrived, keeps the assertion exactly as it was sent,
and writes one line in the chronicle naming it. It also records two things it can check by itself: whether the
operation you named is one this installation publishes, and whether the contact was in the campaign when the
report landed. Nothing rewrites an admitted report afterwards — there is no correction and no withdrawal, and a
mistaken report is answered by a further report, so that both stand and a reader can see that they do.

Everything else is left exactly as it was:

- **No approval was asked or given.** Nobody was shown the action beforehand.
- **Nothing stopped the effect happening twice at the provider.** The keys above are about the record, not
  about the world.
- **No route was resolved.** The provider and account you name are your belief about what you used.
- **There is no attempt behind it.** No lease, no timeout, no retry policy, nothing supervised.
- **It moves no work item.** A report naming an item does not complete it, fail it, or spend one of its
  attempts; its status stays the dispatcher's business.
- **No provider identifier is pinned**, and nothing queued is held, cancelled or reconsidered.

That last absence is the deliberate one. Deciding that this reported effect is the same effect as that queued
one is reconciliation, it needs a design of its own, and this version does not have it. So do not report an
effect and then wait for Jason to act on it. If work should now be stopped, cancelled or approved differently,
say so to the person, and change the work through the verbs that change work.
