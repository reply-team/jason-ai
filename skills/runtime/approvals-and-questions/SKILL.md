---
name: approvals-and-questions
description: Use when a person asks what Jason is waiting for them to decide, or arrives because a notification said approvals or questions are waiting, or tells you to decide something Jason has parked - list the approvals and the questions waiting on a person, present each in their own words, and record their decision as theirs.
metadata:
  status: draft
---

# Approvals and questions

Two different things in Jason stop and wait for a person, and nothing inside the runtime will answer
either of them. Your job at this moment is narrow, and it does not widen because the person is busy or
because the answer looks obvious: find what is waiting, put it in front of them in words they can
answer, and record what they say as theirs. You **never decide on a person's behalf** — not when you
are confident, not when they told you earlier to handle things, not when work is held up by it. The
runtime refuses a decision that does not name a person, and that refusal is the floor of this rule
rather than the whole of it.

**Status: draft.** No agent host has read this text in the state it ships in. Its commands parse against this
build, because every skill's do. It covers the two waiting things this build publishes and nothing else.

## The two things that wait, and how they differ

**An approval (`apr_…`) is about one operation on one work item.** The item reached the claim, its
operation is not one a dispatcher may perform unasked, and it parked there: its status is
`awaiting_approval`, no attempt was made, and nothing was sent to anybody. Nothing has happened yet,
and the question in front of the person is whether it should happen at all.

**A decision (`dec_…`) is a question a running role raised** because it could not work out what to do
next and would not guess. The attempt that asked it ended there; the question outlives that attempt,
which is why it is a row and not something held in a conversation. Its answer wakes the review that
carries the work on: the campaign manager's next check-in will create the work the answer implies, and the session that
recorded the answer creates nothing: if it did, the campaign would hold the same work twice. Something is half done here, and the
question is what it should become.

The difference decides how you present them. An approval asks *shall this exact thing be done*, and
the runtime has already assembled everything needed to answer. A decision asks whatever the role
needed to know, in the role's own words, sometimes with answers it named itself.

## Reading what is waiting

Both lists answer with what is pending unless you ask for something else, because that is the question
a person opens this with — what am I holding up?

```
jason approval list --human
jason decision list --human
```

Each list is a table of identifiers, dates and states: enough to say how much is waiting, never enough
to decide anything. Before you present one, read it:

```
jason approval get apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason decision get dec_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

Both lists take `--status` when the person is asking about something already decided rather than about
what is still open, and both take `--campaign` and `--work-item` to narrow to one piece of work.

## When a notification brought them here

A person may arrive because Jason told them something is waiting: a toast on their desktop, or a message from a
notification plugin on their phone. A notification says how many approvals and questions wait, and may add that
protective actions failed in campaigns it names by id. The approvals and questions are yours here: list both —
`jason approval list` and `jason decision list` — and present them as this skill says; a failed action is not a
decision, and `troubleshooting-jason` is where it is read. The notification itself names no item, so do not guess which one it meant,
and the counts may already be smaller: something may have been answered since it was sent.

A person may instead arrive with a prompt that names one item — an approval id or a question id — from the UI's Copy
prompt button or from `jason approval get <approval-id> --prompt`, or the prompt may be the first message of a session
`jason session open` or the UI's Open an AI session started for them. Read that one first, as this skill says, and
present it with what you found; the lists still show the rest, and arriving with a prompt decides nothing either.

Nothing is approved by reacting to a notification: an approval is a person's decision, recorded as theirs. Dismissing
a toast, or reading the message, decides nothing; what decides is the person's word on that item, once they have read
what it is.

## Presenting one in the person's own words

`approval get` answers with a preview that was assembled at the claim and stored beside the decision:
what the operation would do, who would be reached and at which address, what it would cost, what could
be undone afterwards, and which account it would act through. It is stored rather than put together
again at read time, so what a person reads is what the claim saw.

Where the preview carries a `summary`, quote it first and word for word — it names this approval's own person,
campaign and numbers; the `intent` beside it is the operation's generic sentence.

Where the preview carries an `example`, present it as one reading of the template and say that what is approved is the
template as written: the example is the runtime's rendering for one person, and the provider's rendering may differ.

Give the person all of it. The reach, the cost and what can be undone are the three lines a decision
actually turns on, and a summary that drops them to be shorter has dropped the part worth reading.
Where one of them is marked conditional, say so and give the detail beside it: that is the operation's
own wording about when the reading is less bad than it looks, and settling it for them is deciding.

**Never invent anything the preview does not carry.** Do not estimate a cost the contract left open,
do not predict what the provider will do with the input, do not reassure. If the person asks something
the preview does not answer, the honest reply is that Jason does not know it either, and where they
might go to find out.

A decision is presented the same way and with even less licence, because the text is somebody else's.
Give the question as the role wrote it. Give the options as labels, spelled as they were named, and do
not add one nobody offered — the label is what gets recorded, so an option you made up would be
recorded as though it were a quotation. Where the question names things to read first, offer to read
them rather than answering from memory.

## Recording what they decide

Three verbs, and every one of them names the person:

```
jason approval approve apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --actor human:ada --reason "checked the list"
jason approval reject apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --actor human:ada --reason "wrong audience"
jason decision answer dec_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --answer "pause it" --option pause --actor human:ada
```

`--actor human:<their id>` is not optional here, although the option is shared by every verb and
optional on most of them. An actor of any other type is refused with `approval_not_human`, and an
actor with no name at all with `actor_required`. Leaving the option off inside a launched run is worse
than useless: the command line then claims the attempt it was launched for, and that is refused too.

What you put in `--reason`, and in `--answer`, is **their word on that item** — their sentence carried
across, not your account of what they probably meant. `--answer` is required and is what the campaign
manager reads at its next check-in, so a one-word answer to a question that
deserved two sentences is a decision half recorded.

**There is no bulk verb, and there should not be.** A person who says "approve the first two" has
decided two things, and two things are **one call each**: its own identifier, its own reason, its own
line in the chronicle. If you are not sure which two they meant, ask before you type either.

What each verb does:

- **`approve`** lets the item back into the queue for the next scan, with its due date, its priority
  and its place untouched. It does not run the work; it stops the work being held.
- **`reject`** ends the item as `failed`, carrying `approval_rejected` and the person's reason as its
  last error. That is an accountable ending rather than a breakage, and a finished item is not
  reopened — the work would have to be asked for again.
- A second decision on the same approval answers `approval_not_pending`, and so does one whose work
  has moved on without it. Read it again rather than typing it again.

An approval can also end without anybody deciding it: cancelling the work ends it, and so does the
work item's due date passing. So a question that has left the list was not necessarily answered, and
must not be reported to anybody as agreed to.

## An opt-out the person says was not one

A reply judged a request to stop locks its contact: in every campaign, the claim refuses every provider operation about
them that the do-not-contact register gates, on any value they hold now or gain later. No run lifts that lock, whatever
the person answered: a responder that guessed `opt_out` and asked, and the manager that recorded their answer, both
leave it standing, and the manager's summary says so. Only the person's own verdict on that reply lifts it, recorded
under their name, from outside any run, with a nature other than `opt_out`, and you record it only when they have said,
in their own words, that the reply was not a request to stop and what it was. Take the reply's `plugin`, `thread`,
`message_id`, `message_at` and `contact_id` from the opt-out's line, the category they name with the reaction its row in
the responder's `categories.json` gives, and the nature from what they say the reply is: `person` for a person writing
for themselves, `automatic` for a notice a machine sent, `bounce` for a failed delivery.

```
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind reply_classified
jason reply record cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --plugin mailbox --thread thr-1042 --message msg-77 --message-at 2026-05-04T09:30:00Z --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category interested --confidence 1 --reaction draft --nature person --actor human:ada --reason "not a request to stop, in their own words: they want to hear more"
```

`--actor human:<their id>` is what makes it theirs: a verdict with no name, a role's, or one typed inside a launched run
lifts nothing. It lifts the lock and does nothing else: it drafts and sends nothing, and the register's entries stay
until each is taken off, which is theirs too: `jason suppression remove --channel email --value <the address> --actor
human:<their id> --reason "<their words>"`. In the chat of Jason Desktop you can do neither: the chat refuses that
verb, and you cannot lift the lock there either; give the person the `reply record` line, and the `suppression remove`
line where one is needed, to type in a terminal.

## A standing approval is a person's decision made in advance

A person may decide ahead of time, for one category of reply in one campaign, that a drafted answer to such a reply
goes without being asked each time. It is theirs in the same way an approval is: it is recorded under their name, and
it is set only on their explicit word. **"Handle these" is not one**, and neither is a person's patience with the
queue; the rule at the top of this skill stands. Set one only when the person has named the campaign and the category
of reply, and you have asked who is saying it.

```
jason approval set-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category meeting_requested --expires-at 2026-12-01T00:00:00Z --max-releases 20 --actor human:ada --reason "the answers to a meeting request are always the same"
jason approval list-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason approval unset-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category meeting_requested --actor human:ada --reason "the offer changed"
```

- **The category** is the word a verdict on a reply carries, compared exactly: `interested` covers neither
  `very_interested` nor `Interested`. The base categories are the responder's `categories.json`, not copied here, and
  `approval-boundaries` says which are worth setting one on and which never.
- **The operations** it covers are `conversation.reply` when none is named; name `message.send` with `--operation` only
  where the person says a message to the same person outside the thread is covered too, and repeat the option for both.
- **The limits are the person's to choose, and none is the default.** `--expires-at` is when it stops, and
  `--max-releases`, from 1 to 10000, is how many drafts it may let go. Offer both. Setting one again replaces what was
  set and starts its count over; taking one back leaves the drafts already let go as they were.
- **`--actor human:<their id>` is required**, as with the three verbs above. A role or an attempt is refused with
  `approval_not_human`, and no name with `actor_required`.
- **A search is approved and allowed in one act**: `jason approval approve <apr_…> --allow <people> --actor human:<their id>`
  approves it and lets the campaign's later searches under its audience ask for up to that many more people without a
  question, only on the person's word for the number; `list-standing` shows how many they have asked for. The section
  below says when to offer it.

**What it releases is a draft nobody has been asked about, and nothing else.** A reply parked before the standing
approval was set still waits for the person, however long ago, and setting one releases none of it; it stays theirs
even after its question expired and the item was reopened. Say so when they set one with replies already waiting, and
present those as this skill says. A draft the runtime does not find covered parks as any other does, and its preview
says which condition failed.

**Where to see what went out without them.** Each draft a standing approval let go is an `approved` approval whose
reason is `standing_approval`, decided under the name of the person who set it, and a line `approval_standing_applied`
in the campaign's chronicle. `list-standing` also says how many each one let go and whether it is still in force.

```
jason approval list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status approved
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind approval_standing_applied
```

### A search, and the allowance for the ones after it

A search spends the provider's money, so the first search under an audience, its probe, always waits for the person.
Present it as any approval, and add what the preview does not carry: the planner's proposal, under `proposal` in the
search's context, which says how many people the definition matched, why this many, what it costs, the threshold and
the allowance it suggests. Read it from the work item the approval names, and give it as the planner wrote it:

```
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

The price is the provider's: give the planner's figure as the planner's, and where there is none, say that the
provider charges per person found and Jason does not know its price. The threshold the proposal names is the campaign's
own field, set only on the person's word, under their name, and once the probe's people have landed: set while nobody
is on the campaign yet, it says at once that the reserve is low, and wakes the manager for nothing.

```
jason campaign update cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --audience-threshold 5 --actor human:ada --reason "a fifth of the probe, as proposed"
```

**The allowance comes once the audience works.** After the probe, the manager tops the audience up when its people run
low, and a top-up the allowance does not cover waits for the person as the probe did, its preview's
`standing_approval` saying why: `no_standing_approval`, `another_audience`, `expired`, `over_per_search` or
`ceiling_reached`. That is the moment to offer the allowance, never before the audience has been judged: as the planner
suggests it, top-ups the size of the probe with a ceiling of ten probes' worth of people, and the numbers are the
person's to change. They
give it by approving with `--allow`, or set it on its own for an audience they name by its number:

```
jason approval approve apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --allow 250 --allow-per-search 25 --actor human:ada --reason "the audience works; top-ups of 25 up to 250"
jason approval set-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation audience.search --audience 2 --max-people 250 --max-per-search 25 --actor human:ada --reason "top-ups of 25 up to 250"
jason approval unset-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation audience.search --actor human:ada --reason "no more people for now"
```

`--allow` counts people beyond the search being approved. A campaign holds one allowance: a new one replaces the old,
and approving a search under another audience without `--allow`, a new audience's probe among them, takes the old one
back in the same act. Say so before they approve such a search, because it stops the top-ups of the audience the
campaign is working now. No act but the person's moves the allowance: no role, no verdict and no probe that waits.

## The two-modes question, asked until it is answered

Everything that reaches a real person waits for a person, one item at a time, until the person has said otherwise. They
say it once for each of two kinds of contact: **cold**, someone who has not answered in the campaign, and **warm**,
someone who has. For each, the choice is **every message waits**, which is what an unset key means, or **goes
automatically**, which lets sends to that kind of contact run without being asked. Each approval answer carries
`send_modes_unset`, the keys nobody has answered yet; the installation's status says the modes in force in its `send_modes` check.
While that list is not empty, put the question where the person is already deciding, once per sitting and after the
approval in front of them, in these words or near them: "Jason can send to people who have not answered by itself, and
to people who have, if you say so; until you choose, a message to them waits for you. For people who have not answered,
should every message wait, or go automatically? And the same for people who have?" Ask only about the kinds
`send_modes_unset` names. Say what each answer means:

- **Every message waits** changes nothing, and it is a fine answer: they keep seeing each text before it goes.
- **Goes automatically** lets what Jason would send to that kind of contact go without a question. It never
  reaches anyone who asked not to be contacted, and it touches no operation that is not a message to a person. A
  message still waits for the person where its creator or a reviewer doubted it, where neither the critic nor a person
  passed its text, where the contact has a reply not yet judged, where it answers an automatic reply, a bounce or a
  request to stop, or a conversation marked for the person, where it was written in Jason's chat, and once the
  campaign has sent its day's limit automatically. It is read when the send is claimed, so a change applies to the
  next send and leaves what already waits where it is.
- **Only for one campaign** is a third answer, and a person's too: `jason campaign set-send-mode <cmp_…> --cold
  automatic --actor human:<their id>` sets the campaign's own mode, which is the one in force there.

In the chat of Jason Desktop you cannot set a mode: the chat refuses `preference set`, `preference unset` and `campaign
set-send-mode`. There the approval's card asks the question and the person answers on it. Elsewhere you may set it on
their word, as below, or give the person the command to type in a terminal instead, as `operating-the-installation`
does for `jason project link`.

Offer "remember this" with the answer: where they have said how they want it, offer to keep it so they are not asked
again, which is exactly setting the key. A "goes automatically" they do not want kept changes nothing: only a key that
is set is a mode, so every message of that kind still waits, and the question comes again at the next sitting. The
answer is theirs and is set by their own act, under their name:

```
jason preference set send_mode.cold --value '{"mode":"automatic"}' --actor human:ada
jason preference set send_mode.warm --value '{"mode":"every_message"}' --actor human:ada
```

When a person chooses **goes automatically**, say once, as a fact and never as a condition of setting it, that Jason
then sends messages in their name without them reading each one first, and that their name and tone, set in Settings
("Your name and tone"), help those messages sound like them. The mode is on at once either way.

**Never set one on your own, and never by a role.** "Handle these", a patient person and a long queue are not an answer;
a request from inside a run is refused whatever name it carries. A person who does not answer is not asked again in the
same sitting, and the mode stays what it is. Where they say they changed their mind,
`jason preference unset send_mode.cold --actor human:ada` puts every message of that kind back to waiting.

## What an approval covers, and what changes it

An approval is about a **subject**: the operation and the version of its contract, the work item, the
whole input the plugin would receive, the plugin itself, and the account binding it would act through.
That document is hashed, and the hash is what the claim compares before it runs anything. Everything
in it is something a change to which makes this a different decision — a different argument reaches a
different person, a different binding acts through a different account.

So if the work item's input is edited after the approval was given, the hash no longer matches, and
the item parks a second time with the reason `input_changed`. That is the guarantee working rather
than a fault: nothing runs but what was approved. It does mean you should not tidy up an item's input
while it waits, or after it has been approved, unless you mean to ask the person again — because that
is what you would be doing.

## The limit of what the runtime can check

The runtime guarantees that nothing inside it can approve anything and that every decision names who
made it, but it **cannot tell a person from a process holding that person's own command line** — that
is the operator's trust to give.

Read that as what it is: the last check is you. Typing `--actor human:ada` when Ada has not said so
produces a record that is wrong in the one place a record has to be reliable, and no guard and no
reviewer will catch it afterwards. Ask, wait for the answer, then type it. If you cannot reach the
person, the right outcome is that the work stays parked and you say so.
