---
name: inbox-triage
description: >
  Work the inbound replies: surface the conversations that matter first, give the user
  compact thread context, draft responses together, send only what was explicitly approved,
  and record the outcome of each conversation. Use when the user asks about replies, inbox
  triage, responding to prospects, or finding the interested leads.
metadata:
  version: 3.0.0
  pack: ai-sdr-core
  category: strategy
  maturity: draft
  status: active
  owner: outbound-experts
  tags: [inbox, replies, triage, dispositions]
  tools: []
  api: []
  relations:
    depends-on: [sdr-operations, approval-boundaries]
    recommends: [performance-analysis, campaign-launch]
---

# Inbox triage

> **Partly an expert skeleton.** The triage procedure and the sending gate are complete.
> Reply strategy — how to answer an objection, when to push for a meeting, how long to keep
> a thread alive — is marked `TODO(expert)` and needs validation by outbound experts before
> this skill leaves `draft`.

## Purpose

A campaign that generates replies nobody works is a campaign that generated nothing. This
skill keeps the pipeline moving: find the conversations that deserve attention today, give
the user enough context to decide quickly, draft the response with them, send exactly what
they approved, and record what came of it so the next person or session knows.

## When to use / when NOT to use

- Use for reviewing replies, drafting and sending responses, routing or deferring a thread,
  and recording what came of each conversation.
- Do NOT use for bulk campaign changes — that is `campaign-launch` or `performance-analysis`.
- Do NOT use for the calendar hand-off after a prospect says yes. That is **no longer a gap in
  the contract**: `meeting.propose`, `meeting.book` and `handoff.create` exist, along with the
  rest of their families. What is missing is a playbook — no skill in this pack carries the
  judgement for proposing times, qualifying, or handing a person on. Take the thread as far as
  the classification, name the operations, and say that the judgement around them is
  unwritten rather than improvising it here.

## Prerequisites

- Conversations exist — i.e. something has been sent (`campaign-launch`).
- Operations used:
  - **find and read** — `conversation.list`, `conversation.get`;
  - **decide what it means** — `conversation.classify`;
  - **answer** — `message.draft`, then `message.send`;
  - **record what came of it** — `disposition.set`, and `activity.log` when the exchange
    happened somewhere we do not send from;
  - **move it through the queue** — `conversation.assign`, `conversation.snooze`,
    `conversation.close`, `conversation.reopen`;
  - **when the reply names somebody else** — `referral.record`.
- Everything above is `read` or `control` and derives `auto`, with two exceptions.
  `message.send` is reach `act` and irreversible, so it derives **`confirm_each`**.
  `referral.record` is raised to `confirm_once`, and the reason stated at the operation is
  what you act on.

**Why `confirm_each` and not `confirm_once`.** The distinction is not size, it is whether one
call is one decision. `confirm_once` covers a *release* — one decision over a body of work that
already exists, approved against a preview that names the population. A reply is a
**composition**: distinct words, written for this thread, going to one named person on one
channel. There is no population a preview could name, and no summary that can stand in for
reading the text, so the approval **is** the rendered text, once per message. Twelve replies
are twelve approvals, and collapsing them into one yes is the exact inversion
`approval-boundaries` exists to prevent.

## Planning guidance

Agree the scope before opening anything: everything, or a slice — one campaign, one meaning,
one owner, one time window? An inbox reviewed without a scope tends to be reviewed half-way.

**The queue arrives ordered** (`H10`). `conversation.list` returns threads by how overdue they
are, and that ordering is part of the result rather than something to reconstruct. Do not
re-sort by created date.

**Where the business wants a different order, filter — do not re-rank.** `conversation.list`
filters by meaning, channel, owner and campaign, so "meeting intent first" is a filtered read,
not a hand-sorted copy of the whole queue. The distinction matters because a re-ranked list
silently drops the waiting time that made a thread urgent.

- TODO(expert): the signals that reliably mark meeting intent versus polite interest, and the
  order of the remaining categories.
- TODO(expert): how quickly a reply must go out before the interest goes cold, per channel.

The account's own answer to the second one is a policy value rather than a contract value:
`response_policy.get` reads what this operator holds itself to, and `response_policy.define`
sets it. Read it before asserting any number.

## Execution guidance

1. **Get the shape of the work** (`conversation.list`). Counts by meaning and how long each has
   been waiting, so the user knows how big the job is before starting it.
2. **Present a triage list** — person, company, subject, current meaning, how long it has
   waited — in the order it arrived in. Let the user pick, or confirm working through in order.
3. **Open the conversation** (`conversation.get`). Present the exchange compactly: who said
   what, when. Note the **channel** and the **sending identity that owns the thread** — a reply
   goes out from that identity (`H5`), not from whichever sender has capacity.
4. **Decide what the reply means** (`conversation.classify`) — before drafting, because the
   meaning decides whether a reply is the right next act at all. Three cases are not replies:
   - **a request to stop** is not a meaning (`A2`). It goes to `optout.record` and
     `outreach.hold`, which are `auto` and go first; then `suppression.add`, which is `auto` for
     the person's own identifier and `confirm_once` for a domain or a pattern; then
     `enrollment.stop`, which is `confirm_once` and is asked for. Nothing is sent back (`H4`), and
     the protective half is never held up waiting for the gated half;
   - **an out-of-office or other automatic message** is answered by `message.schedule` for
     their stated return date, which is a new outbound message rather than a reply (`H4`);
   - **a reply naming somebody else** goes to `referral.record`, which is raised to
     `confirm_once` and never guesses an address.

   The branch each remaining meaning produces is written out as a worked chain in
   `sdr-operations`. Read it there rather than reconstructing it here.
5. **Draft with the user** (`message.draft`). Ground the draft in the actual thread and in the
   user's stored preferences — tone, sign-off, hard constraints — if those are available.
   Iterate. Then show the **final rendered text**, which is what the approval binds to.
   - TODO(expert): response patterns for the recurring cases — not interested, wrong person,
     not now, price objection, "send me info".
6. **Confirm and send** (`message.send`). The user approves the literal rendered text, to this
   person, on this channel — `confirm_each`, every message, no batching. One call carries one
   person and one channel, so **switching channel is an argument to the call**, and therefore a
   different decision needing its own approval rather than a silent fallback. Then verify the
   send landed on the thread and report it.
7. **Record what came of the participation** (`disposition.set`), from the account's declared
   vocabulary — `vocabulary.list` says what values this account allows. The disposition is a
   different axis from the reply's meaning and from the execution state (`G3`): the meaning was
   already recorded at step 4, and the disposition is the business outcome. Confirm it when it
   is ambiguous.
8. **Move the thread out of the queue.** `conversation.close` when it is worked;
   `conversation.snooze` when it should come back on a date; `conversation.assign` when it
   belongs in someone else's inbox, which routes it and promises nothing; `conversation.reopen`
   when it was closed too early. Closing carries no business outcome and stops no outreach
   (`H9`) — `disposition.set` and `enrollment.stop` are what do those two things.
9. **Log what happened elsewhere** (`activity.log`) — a reply that arrived in someone's personal
   mailbox, a call back, a message on a channel we do not send from. Logging records; it never
   sends and never advances a cadence (`H6`).
10. **Continue** through the remaining conversations as the user wants.

## Validation

The send is confirmed by the created message appearing in the thread — re-read with
`conversation.get` if in doubt, never assume, and recover a timed-out send through
`invocation.get` under the key it went out with rather than sending again. After
`disposition.set`, the participation carries the outcome; after `conversation.close`, the
thread is out of the queue and the cadence is unchanged, which is the correct result rather
than a bug. At the end, the count of conversations reviewed plus those still waiting adds up to
what you started with.

## Reporting

Conversations reviewed, replies sent (with recipients, channels and thread identifiers, and the
idempotency key each went out under), meanings classified, dispositions assigned, threads
closed, snoozed or assigned, and what is still waiting on attention. Then the useful part:
interested people with no meeting yet, and the follow-up that should exist for them —
`task.create` is how that follow-up becomes real rather than a line in a report.

## Failure modes

- **Batch-sending drafts.** Every message to a real person is its own `confirm_each`. There is
  no bulk mode for this, by design, and no standing approval covers "the replies" as a class.
- **Switching channel silently** when the original channel is unavailable. `message.send`
  carries one channel per call, so the switch is a visible argument change and a new decision;
  tell the user instead of falling back.
- **Answering from whichever sender has capacity.** Load-balancing is right nearly everywhere
  else and wrong here: a reply is sent from the identity that owns the thread (`H5`).
- **Replying to an automatic message** because the thread is open and it looks like a reply
  (`H4`).
- **Classifying a request to stop** as the nearest-looking meaning, because it keeps the
  reporting axis tidy (`A2`). It is an obligation. Recording it — `optout.record`, `outreach.hold`
  — is protective, derives `auto` and is never delayed for a confirmation. The rest of the branch
  is not: `suppression.add` over a domain or a pattern is `confirm_once`, and `enrollment.stop` is
  `confirm_once` with no conditional at all, because an enrolment's position is evidence of what
  the person actually received (`G8`). Perform the protective half immediately and ask for the
  other half — treating the whole branch as `auto` is how a stop turns into an unapproved
  domain-wide exclusion.
- **Treating do-not-contact as a disposition value.** Suppression is `suppression.add` and
  `optout.record`, not a label on a thread, and recording a disposition the user did not agree
  with is a separate mistake worth avoiding on its own.
- **Closing the thread to stop the cadence,** because closing feels like finishing. It stops
  nothing (`H9`).
- **Re-sorting the queue by timestamp** and burying the interested reply under twenty automated
  bounces — or re-ranking it by hand and losing the waiting time that made a thread urgent.
- **Losing thread context** by summarising away the specific thing the prospect asked.

## Safety

Sending a reply to a real prospect is `confirm_each`: the user confirms the exact rendered
text, for this person, on this channel, every time — no exceptions, no batching. Where the
draft was machine-generated, the moment the user approves that rendered text is the moment a
human editorial review exists; record it with `editorial_review.record` then, or the send
carries the disclosure obligation instead (`A21`).

Recording that somebody asked to stop is the opposite case and must not be reasoned about the
same way. It is protective, derives `auto`, and is never held back for a confirmation, never
inferred from silence, and never inferred from a lukewarm reply.

Conversations contain personal data; keep quotes to what the decision requires. Full rules:
`approval-boundaries`.

## Related skills

- `sdr-operations` — the conversation operations, their five properties, and the worked chain
  for a morning inbox and for a reply that names somebody else.
- `performance-analysis` — when reply volume or quality suggests a campaign problem rather
  than an inbox problem.
- `approval-boundaries` — where `confirm_each` comes from, why a composition is never covered
  by one yes, and what a valid confirmation looks like.

## Changelog

- 3.0.0 (2026-08-10): migrated onto contract 2.0.0. `conversation.read` → `conversation.get`;
  `conversation.reply` → `message.send`, which is a different family and the point of the move
  — a reply is a message, and switching channel is an argument change rather than a silent
  fallback. Both `exact-text` gates become `confirm_each`, with the composition-versus-release
  reasoning stated rather than assumed. Adds `conversation.assign`, `conversation.snooze`,
  `conversation.close`, `conversation.reopen`, `referral.record` and `activity.log`, and with
  them the rule that closing a thread stops no outreach. Classification and disposition are now
  two steps on two axes instead of one; a request to stop is no longer offered as a
  classification value. The meeting-booking gap is closed — family 11 exists; what is missing is
  a playbook, and the skill now says so. Reply-strategy gaps remain marked for expert
  validation.
- 2.0.0 (2026-07-30): renamed from `manage-replies` and rewritten vendor-neutral for the
  `ai-sdr-core` pack — Reply inbox endpoints and scopes moved to `reply-operations-mapping`.
  Reply-strategy gaps marked for expert validation; safety now defers to
  `approval-boundaries`.
- 1.0.0 (2026-07-27): ported from the prototype workflow as `manage-replies`.
