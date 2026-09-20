---
name: approvals-and-questions
description: Use when a person asks what Jason is waiting for them to decide, or tells you to decide something Jason has parked - list the approvals and the questions waiting on a person, present each in their own words, and record their decision as theirs.
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

**Status: draft.** It covers the two waiting things this build publishes and nothing else.

## The two things that wait, and how they differ

**An approval (`apr_…`) is about one operation on one work item.** The item reached the claim, its
operation is not one a dispatcher may perform unasked, and it parked there: its status is
`awaiting_approval`, no attempt was made, and nothing was sent to anybody. Nothing has happened yet,
and the question in front of the person is whether it should happen at all.

**A decision (`dec_…`) is a question a running role raised** because it could not work out what to do
next and would not guess. The attempt that asked it ended there; the question outlives that attempt,
which is why it is a row and not something held in a conversation. Its answer wakes the review that
carries the work on. Something is half done here, and the question is what it should become.

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

## Presenting one in the person's own words

`approval get` answers with a preview that was assembled at the claim and stored beside the decision:
what the operation would do, who would be reached and at which address, what it would cost, what could
be undone afterwards, and which account it would act through. It is stored rather than put together
again at read time, so what a person reads is what the claim saw.

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
across, not your account of what they probably meant. `--answer` is required and is what the role
reads when the work comes back to it, so a one-word answer to a question that deserved two sentences
is a decision half recorded.

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
