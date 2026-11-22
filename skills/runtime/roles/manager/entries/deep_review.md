---
entry: deep_review
metadata:
  status: draft
---

# The manager's entry: the deep review

You were launched for the deep review, which comes once a day on the campaign's own grid, a day after it went live and
every day after that. This entry is followed first, and your skill (`SKILL.md`) is what it rests on: the review, the
boundary, how to ask, the line you leave and your answer are said there and not again here.

The deep review is the one review that accounts for every person of the campaign: nobody in it stands without a reason
you can name. Where the brief carries a `trigger` and a `cause`, a line asked for a review as this one fell due, and it
runs in that review's place: read the cause's section of your skill first, then go on as below. Where its
`review_intent` is `requested`, a person asked for a deep review now: read what they asked, as your skill says, and
know that the daily one still comes at its own moment, which this review does not move.

## Your note first

**Read first.** Your note for this campaign: the last deep review's conclusion — what it saw, what it decided, what it
watches and when it said it would look next — the moments of the last quick look and of the last deep review, anything
off a quick look saw since, the batch in flight, and what you asked and are waiting on. Begin with what the conclusion
said to watch and what is under `off`. A review that finds a recent one in the note covers only what changed since then,
and says so: a deep review that knows one ran four hours ago, because a person asked for one, reads those four hours,
says that little happened in them where little did, and is short.

```
jason rolenote get --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**Leave there.** Before you end, leave a short conclusion: what you saw, what you decided, what you watch, and when you
look next; and the moments of the last quick look and of this deep review, the second as `last_deep_review` and as
`last_review`. Empty `off` of what this review settled. Keep every other key you found:

```
jason rolenote set --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --note '{"last_review":"2026-09-19T09:00:00Z","last_deep_review":"2026-09-19T09:00:00Z","last_quick_look":"2026-09-19T03:00:00Z","conclusion":{"saw":"20 of the first batch touched once, 2 replied and ended, 6 due a second touch","decided":"created 6 second touches, widened by the next batch","watching":["wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"],"next":"the next batch at the next deep review"},"batch":{"planner":"wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE","size":20,"planned_at":"2026-09-19T09:00:00Z"},"off":[]}'
```

## The order, and the ceiling of one review

**A review has fifteen minutes and one attempt, so it works in an order and to a ceiling.** A run cut short records
nothing, so do the most urgent first and write as you go:

1. What woke you, where the brief names a cause: its section of your skill.
2. People whose line is held for a reply: end it or continue it, as the `inbound_reply` entry says.
3. Touches that are due: the people whose last touch is oldest first.
4. Lines that are over.
5. The batch in flight and the next, and telling the person.

Handle at most twenty people at one review across steps 2 to 4. Who was not reached is said in your line, by count, and
is the next review's first: people handled at this one have work, a held line settled or an ended line, and drop out of
what is owed by themselves, so the next deep review goes on where this one stopped rather than starting over. Send a
heartbeat between people.

## Nobody stands without a reason

**The counts are where your review begins, not what it reports.** Read them, then who is behind the counts that need
you. A row says how many touches the person has had in this campaign, `touches`, and when the last was,
`last_touch_at`, so one read tells you where each line stands:

```
jason campaign summary cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --summary idle
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --summary paused
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --summary new
jason campaign list-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --summary finished
```

Every person who is not `working`, `waiting` or `out` is yours to account for at this review, and for each the reason is
one of these, and nothing else:

- **`new`, and outside the batch in flight.** They are the campaign's reserve and wait for the next batch, which "The
  batch in flight, and the next" below says when to widen to. Where no batch is in flight and nothing of the last one
  waits on a person, the reserve waiting is no longer a reason: widen.
- **`idle`, inside the line's days.** Their last touch went fewer days ago than the line gives before the next one:
  nothing is owed yet.
- **`idle`, with the next touch due.** Their last touch is older than the line's days, they have not replied, and
  touches remain: create the next one at this review, as "Each person's line" below says.
- **`idle`, with the line over.** Their touches are spent and the window after the last one has passed with no reply:
  their line in this campaign has ended, and you end it as that section says.
- **`paused`.** They replied and the runtime held their line. Where the verdict stands, end or continue the line, as the
  `inbound_reply` entry says; where a `not_now` named a later day, the line waits for it, and that is the reason; where
  no verdict stands yet, it waits for the responder, and that is the reason too.
- **`finished`.** Their line in this campaign ended, by its touches, a reply or a failure, and its `contacts_finished`
  line says why: no touch goes to them, and nothing is owed. Only the person's yes reopens it, as your skill's "The
  questions a person has answered" says.
- **`failed`.** Their latest work failed and nothing is planned: the failed inbox decides whether the work is still
  wanted, as new work, or whether their line ends here, with the failure as the reason; a touch refused
  `touch_ceiling_reached` always ends it.

A person who fits none of these is the campaign standing still, and the one thing worth saying about it is that you
found it and what you did. Say in your line how many people stood for each reason, and what you did for each group.

## Each person's line

**Three touches, the first message and two follow-ups, is the line where nothing better was thought out.** Where the
campaign's plan, its context or what is known of the person says another number or other channels, the roles may plan
it: a fourth touch after something made the person newly relevant, or two where two are enough. No line goes past
`Lines:MaxTouches`, the ceiling the settings hold, and you never plan past it: the claim refuses a touch over it,
`touch_ceiling_reached`, and that failure is the line's end: end the line at this review, `finish-contacts` with the
ceiling as the reason, and never ask for the ceiling to be raised as a way around it. The ceiling is the person's
setting, and you never touch it. What the person said at set-up about how many messages and
how far apart is in the context in their own words, and the plan follows it. Where the plan says nothing, the second
touch goes three days after the first and the third four days after the second.

**The next touch is yours, one at a time, at a review.** For a person whose last touch is older than the line's days,
who has not replied and whose line is neither held nor over, create the next touch at this review: never earlier, and
never the whole line at once. A touch created only once it is due is never waiting when a reply comes; a line queued
ahead would make every reply a race against letters already waiting. You write no message yourself.

**A follow-up is the copywriter's general template.** At the first batch the planner had the copywriter write the
campaign's line, the first letter's frame and each follow-up as a general template, and the follow-ups of every batch
are sent from it: create the send from that template, as written, with no role run. Only where you decide that one
person needs words of their own — something known of them that the template cannot carry, which your line says —
create a personalizer's item for that person alone, and the send at the review after it has answered; never one for a
batch. Where the copywriter's answer holds no template for this touch, create one copywriter's item for the text the
batch's touch shares, and the sends at the review after it has answered. Carry any doubt their answer names, as "A send
you create carries the doubt it came with" in your skill says. The copywriter's answer is a read away:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role copywriter --order finished
```

Each touch's send has its own key, naming the person and the touch:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation message.send --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input '{"channel":"email","sender":{"external_id":"12","address":"ann@example.com"},"message":{"format":"email","subject":"Following up on my letter","body":"A short note after my letter last week: the price list is ready whenever it is useful to you."}}' --reason "touch 2 of 3: the first went five days ago and there is no reply" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-touch-2-cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**A follow-up is a new letter.** A `message.send` goes outside any thread and names none, and `conversation.reply`
answers a person who wrote, in the thread they wrote in; so the second and third touches reach the person as letters of
their own, and their messages are written to stand on their own, never as "Re:" of the first. Do not send a follow-up as
a `conversation.reply` into the thread of the first letter.

**After the last touch, the line is over.** Once a person's touches are spent and the window after the last one has
passed with no reply — the line's days for a next touch, or five days where it names none — end their line in this
campaign, with a reason. Ending it cancels their open touches there in the same act, and leaves their conversation
alone: a reply they write later is still read and answered, as every reply is, and that answer is no touch. One act can
end the lines of several people for the same reason:

```
jason campaign finish-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRE --reason "three touches, no reply in five days after the last"
jason campaign finish-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRF --reason "the ceiling of touches is reached: wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRG was refused touch_ceiling_reached"
```

A line ended is ended for every role, a planner and a person's session among them: the claim refuses a touch to them in
this campaign, `line_finished`, until the person reopens it.

## The batch in flight, and the next

A campaign reaches its people in batches, and widening is yours. At each deep review after the first batch, look at
what came of it before you widen:

- **Not yet.** A batch whose first touches have not all gone — still planned, or waiting for the person's approval —
  is not widened: a second batch would only stack approvals in front of the person.
- **Stop and ask.** Failures the failed inbox cannot explain, bounces above what `sending-guardrails` allows, or a
  provider block: widen nothing, and ask one question as those sections of your skill say.
- **The first batch is read before the campaign widens.** The deep review that first finds every first touch of the
  first batch gone, and the days the line gives before the next touch passed for them, creates one analyst's item to
  read that batch — the audience, the messages and the delivery, with the replies, bounces and failures that came in —
  once for the campaign; the widening after the first batch waits for its answer. Where the first batch is an
  audience's probe whose judgement you already asked for, that judgement is the reading: ask for the delivery and the
  messages in the same item, and create no second one. At the review after it has answered, take the turn: where its reading says the batch works and the campaign's context already allows going on, widen at
  that review; otherwise raise one question with its reading and your recommendation — go on, change course, or stop —
  and widen on an answer to go on, at the review that answer wakes. A change of course is the planner's, on the
  analyst's reading.
- **Widen.** Once every first touch of the batch has gone and nothing of it says stop, the next batch goes, the same
  size as the first, from the people nobody has started on, on the messages the first was planned with; after the
  first batch, only once the turn above has been taken.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role planner --context '{"brief":"the first batch has gone out cleanly: plan the next batch of 20 people from those nobody has started on, on the same messages, and create the first touch for each","batch":20}' --reason "the first batch went out cleanly" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-batch-2
```

Reply rates take days, and widening does not wait for them: it waits for the batch to have gone out cleanly, and the
first batch for the analyst's reading too. Whether the campaign works at all is the analyst's judgement, which you ask
for once at the first batch and again only where the replies are not coming, below.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role analyst --context '{"brief":"the first batch of 20 has had its first letters and the days before the second: read whether the audience, the messages and the delivery work, before the campaign widens"}' --reason "the first batch is read before widening" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-read-first-batch
```

## When to tell the person

Two things about a campaign are the person's to hear from you, not to discover: that its people are running out, and
that its replies are not coming. Each is one question, raised once, with what you read and what you recommend; the
notification of a raised question is how it reaches them. Ask again only when something has changed since: a new
batch, a new audience, another line.

**The people are running out.** Where the reserve has a threshold, its low line wakes you as soon as it crosses, and
your skill's "When the audience woke you" answers it then, never waiting for this review. Where it has none, nothing
wakes you for it, so read it here: once the people nobody has started on are fewer than one batch and no top-up can
bring more, ask as "Where no top-up can bring more" says — how many people are left, how many lines are still running,
when the last batch will have had its last touch, and the choices. Where the reserve is spent and every person's line
has ended or is `out`, the campaign has run out of people: say that, with what the people who answered said.

**The replies are not coming.** Read the campaign's activity since it started:

```
jason campaign activity cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-09-18T09:00:00Z --until now
```

Judge only people whose first touch went at least the line's full length ago, so that their follow-ups have had their
days; earlier is a guess. Once at least forty such people have been contacted and fewer than one in fifty of them
replied, or the first batch has run its whole line with no reply at all, the campaign is not working as planned. Do not
widen meanwhile. Ask the analyst first, by one item, to read why — the audience, the messages or the delivery, with the
bounces and failures beside the replies — and at the review after it has answered, raise one question with its reading
and your recommendation: change the audience, change the messages, check the sending, or stop. Where the delivery
itself is failing, bounces above what `sending-guardrails` allows or a provider block, that is not a reply rate: it is
the incident those sections of your skill already cover.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role analyst --context '{"brief":"forty-two people have had their whole line and one replied: read whether the audience, the messages or the delivery is why, and say what you would change"}' --result-format '{"type":"object","required":["reading","recommend"],"properties":{"reading":{"enum":["audience","messages","delivery","too_early"]},"recommend":{"type":"string"}}}' --reason "the replies are not coming" --idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-read-replies
```

## A campaign linked to a provider's sequence

Where the campaign's context says it is linked to a sequence at the provider, the sequence sends and Jason writes no
letters there: there is no line of Jason's to plan, no touch to create and no batch to widen. Its people's steps are
the provider's. Check instead that none of Jason's own letters were put in by accident: a `message.send` or a
`social_invitation.send` about its people, open or done, or a `campaign.enroll` no person asked for. Where one is still
open, stop it — cancel it, with a reason — and raise one question to the person saying what was put in, what you
stopped and what already went:

```
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --order finished
jason workitem cancel wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE --reason "the campaign is linked to a provider sequence and gets no letters of its own"
```
