---
entry: inbound_reply
metadata:
  status: draft
---

# The manager's entry: a reply

You were launched because the runtime's read of the campaign's conversations found a thread that moved: somebody may
have replied. This entry is followed first, and your skill (`SKILL.md`) is what it rests on: the review, the boundary,
how to ask, the line you leave and your answer are said there and not again here.

## Your note first

**Read first.** Your note for this campaign, before anything else: the `replies` you are already following — each
thread with the line that named it, the read and the responder you queued or deferred to, and whether the person's line
still waits on a verdict — and when you last reviewed. A thread already in `replies` is one you are part way through:
go on from where it stands, never from the start. A review that finds a recent one in the note covers only what changed
since then, and says so.

```
jason rolenote get --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**Leave there.** Each thread you touched in `replies`, with what you queued, deferred to or decided, and a person whose
line still waits on a verdict marked so; drop a thread once its line is ended or continued. And the moment of this
review as `last_review`. Keep every other key you found:

```
jason rolenote set --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --note '{"last_review":"2026-09-19T09:00:00Z","last_quick_look":"2026-09-19T03:00:00Z","last_deep_review":"2026-09-18T15:00:00Z","replies":[{"thread":"501","line":"jrn_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","contact":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","responder":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE","line_waits":"the verdict"}]}'
```

## When a reply woke you

`trigger` is `inbound_reply_observed` when the runtime's own read of the campaign's conversations found a thread that
moved: somebody may have replied. The line names the thread, when it moved, the provider's `unread` and
`has_meeting_intent`, and `contact_id` when exactly one person is pinned to it — never what anybody wrote, so the line
is where to look, not what was said. One read can say several threads, and only the first woke you; read every line
of the kind since your last review:

```
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind inbound_reply_observed --since 2026-09-18T09:00:00Z
```

**The runtime has usually raised the responder already.** For a thread the campaign holds by its own pin — a line with
no `attributed_by`, or with `campaign_pin` — and for a line found by the person's pin that says `campaign_own` `true`,
the runtime queues the read of the thread and the responder by itself, in the scan that wrote the line, so before you
create anything look for that item. A line found by the person's pin without `campaign_own` is yours, as below: the
runtime raises nothing for it. A responder it made is an `ai_role` item made by `system:dispatcher`, and its context's
`inbound_reply.moves` names each line it answers by the line's `jrn_` id:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder --status created --status scheduled --status processing --status waiting --status awaiting_approval
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder --order finished
```

- **An open responder whose `inbound_reply.moves` names the line:** you defer to it. Create no read and no responder for
  that line, and say in your note that you left it to the rule, with the item's id.
- **One that has ended and names the line.** If it succeeded, read its result (`outcome`, `verdict`,
  `created_work_items`) and never draft or act again on that line: a reply has one verdict and a verdict one draft, for
  you as for the responder. If it failed, was cancelled or expired, the failed inbox of your skill's review and the
  text below apply.
- **No responder names the line.** The rule left it to you: it is switched off, the line is older than it answers, the
  thread is the person's rather than this campaign's own, or no responder could be launched. The text below applies.

Knowing what a person wrote takes a read of the thread through the provider, and **not in this run**: a campaign runs
one item at a time, so an item you create is claimed only after your run has ended. Where no responder names the line,
queue the read — a `conversation.get` item naming the thread by the line's `thread` — and then the responder's item, at
the same priority and naming the read the first create answered with. Work at one priority runs oldest first, so the
responder runs after the read and reads the reply from its result:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.get --input '{"thread":{"external_id":"501"},"limit":20,"offset":0}' --reason "read the reply the inbound sync saw" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-read-thread-501
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --context '{"thread":"501","read":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"}' --reason "a reply to answer" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-respond-501
```

Where the line's `contact_id` is null, nobody is pinned to the thread: create the responder's item without
`--contact`, and the responder finds the person through the read. Where it finds none, or more than one, it drafts no
reply and says who wrote, and you raise one question at your next review. The same holds where the read does not show
the thread's latest message: the responder drafts no reply and says so, and the question is yours.

A responder you create this way carries no `inbound_reply`, so the runtime cannot take its verdict as its own: every
draft it makes is asked, whatever standing approvals stand.

Neither wakes you when it ends: a read that succeeded is not a trigger. Say in your note that you are watching both,
and decide at your next review, when what they found is there to read. A thread the line names can have moved for a
follow-up sent into it rather than for a reply; the read is how you tell. Whether the provider's list moves a thread
for a reply Jason sends is the provider's matter, and its plugin's README says which. Where it does, the line says it
like any other: when the thread's latest message is Jason's own — a `conversation.reply` item of this campaign
succeeded for that thread, and the line's `last_activity_at` is not after the `sent_at` its result names — nothing
happened, and there is nothing to read or answer. Without a `sent_at` in that result, the read tells. Where it does
not, no line comes for Jason's own reply, and nothing wakes you for it.

**The read itself is not yours to redo.** A `conversation.list` item made by `system:dispatcher` that failed is the
runtime's own read: it is tried again on its own, later each time, from its first page. Do not create one in its
place. What its error code says about the installation is still worth knowing, and asking about.

A line of this kind can also come from the provider's own record of the account, where the plugin reads its accounts'
events: it then carries an `event_id` and names no work item or attempt, because no run of this campaign found it.
Read it as any other. From that record, where the provider says who moved the thread, a move by the account — Jason's
own reply among them — says no line at all, whatever the provider's list does with that reply; where it cannot say,
the check against `sent_at` above applies to the line as it does to the inbound read's.

Such a line also says how it found this campaign, under `attributed_by`. `campaign_pin` is the provider campaign this
campaign's own operations pinned: the thread is this campaign's outreach. `contact_pin` is the person alone — the
provider named no campaign, or one that no campaign that is not archived is linked to, or more than one is — so the
thread may be one this campaign did not start: a direct email, a message outside any sequence, another campaign's
outreach. Where the line also says `campaign_own` `true`, the thread is this campaign's own: when the line was written,
it already was, or no campaign the reply was found in held it yet and this campaign was the only one, of those not
archived, that had written to the person through that plugin before the reply — a message, an invitation or an answer
that succeeded — and the reply named no provider campaign. The runtime raises its read and its responder as for
`campaign_pin`, so look for them as above. Without `campaign_own`, the thread is yours: queue the read alone, and only
where this campaign reads conversations at the plugin the line names under `plugin`: the read follows this campaign's
route, which may lead elsewhere, and this names where it goes:

```
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation conversation.get
```

Where it names another plugin, or none, queue nothing, say so in your note and raise one question instead. Raise the
responder at your next review, once the read shows whose thread it is; a thread the outreach of another campaign that is
not archived owns is that campaign's to answer, never yours. Where the thread is the outreach of no campaign that is
not archived, it may have reached every such campaign the person is in, and one answer is all they should get: before
you raise the responder for it, read the person's chronicle across campaigns, by the line's `contact_id`:

```
jason journal list --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

Where another campaign that is not archived has already raised the responder or replied in that thread, leave the thread
to it and say so in your note; otherwise raise the responder. Say in your note which it was.

## The person's line after a reply

**The runtime has already held the line.** In the scan that read the reply, a person who was an enrolled member of this
campaign became `paused`: their touches wait at the claim, a letter queued for a later moment or parked for an approval
included, and a letter the provider still holds for later has its `message.cancel` queued by the runtime. Create no
take-back of your own beside it. The hold cancels nothing else and never stops the campaign: a reply in their thread, the
read, the responder and an `optout.record` go on. What becomes of the line is yours, and you end it or continue it: the
reply's verdict says which. Read the latest `reply_classified` line for the thread, as your skill's review says:

- **A person wrote for themselves, or asked to stop** — nature `person` or `opt_out`: end their line in this campaign.
  Their conversation goes on: an answer in their thread is no touch, and ending the line never reaches it. Where they
  asked to stop, ending the line is the whole of your part: the register's entry and the provider's opt-out are the
  responder's, recorded with its verdict, or the person's own, and you put nothing on the register yourself.
- **A notice a machine sent** — `out_of_office` or `auto_reply`, nature `automatic`: the line goes on. Continue it, and
  move a touch still queued for them to the day the reply names, which the responder's `returns` list gives under
  `back_at`: give it that later `not_before`, or cancel it, with a reason, and the deep review creates the next touch
  once the day has come. Where none is queued, their next touch is the deep review's, once it is due, counting the
  touches they have had.
- **`not_now`** — a person, who asked for later: leave the line held until the day its `back_at` gives, and say so in
  your note; the first review on or after that day continues it as an out-of-office notice is continued.
- **A failed delivery** — nature `bounce`: end the line, with the bounce as the reason, unless the line goes on another
  channel of theirs, which you name in your line.

```
jason campaign finish-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "they replied (jrn_01JB6K8TQ2W9V4MZ0C3Y7H5NRD): the line ends and the conversation goes on"
jason campaign continue-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --reason "an out-of-office notice, back on 2026-09-25"
jason workitem update wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRF --not-before 2026-09-25T09:00:00Z --reason "the person is away until 2026-09-25"
```

Ending the line cancels the person's open touches in this campaign in the same act, so cancel none of them yourself
first. **Where no verdict stands yet**, the responder has not run: leave the line held, which keeps every touch from
going, and mark it in `replies` as waiting on the verdict. The next review of any kind reads the verdict and ends or
continues the line; the deep review accounts for every held person. An item already `processing` is past stopping: say
so in your line with its id.

**Where the thread's last message is Jason's own, the hold was for nothing.** On a provider without events, the read
can show that what moved the thread was a letter of this campaign's — a follow-up the provider sent later than Jason
recorded — and nothing the person wrote. The runtime held the person for it all the same, and nobody else continues
them. Continue the line, plan again what the hold took back — a letter the provider held for later, whose take-back the
runtime queued, is created again for its moment — and say so in your note, naming the thread and the letter:

```
jason campaign continue-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRE --reason "the thread moved for our own follow-up, not for a reply"
```

## A reply no responder judged

A reply that no responder judges is told to nobody: the person hears of a reply only once a role, inside a run, has
recorded a verdict that says a person wrote it. So where you read a reply that no responder judged — the runtime raised
none for its thread (`thread_not_campaign_own`) and you raise none, or the responder for it never ran — record the
verdict yourself, as the responder would: the category from what the reply says, `other` where none fits, the reaction
that category's row gives, your own confidence, and `--nature` from what the reply is. Take the reply's `plugin`,
`thread`, `message_id` and `message_at` from the read's result, and the person from the line:

```
jason reply record cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --plugin mailbox --thread thr-1042 --message msg-80 --message-at 2026-05-06T11:15:00Z --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category interested --confidence 0.8 --reaction draft --nature person --reason "a reply no responder judged, read from wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"
```

Draft nothing yourself: where the reaction asks for an answer, queue the responder for the thread as above, and it finds
your verdict standing. Where a responder judges the reply after all, its verdict is the one that stands, and you record
no second.
