<!-- Generated. Do not edit by hand. -->

# Operation index

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

Every operation in the contract, by family, with the first sentence of its intent. † marks a core operation — the 30 an agent keeps in front of it at all times. The full classification of each one is in its family catalog.

**Coverage.** 21 of 21 families, 325 of 325 operations, 30 of 30 core.

## 01 — Discovery, enrichment and verification

*People and audience* · [catalog-01-discovery.md](catalog-01-discovery.md)

- `candidate.search` — "Find me people out in the wider world who look like this and we do not already have"
- `candidate.get` — "Show me everything about this found person that I have already paid for"
- `candidate.promote` — "Buy this person's details and make them ours"
- `company.search` — "Find me companies that look like this"
- `company.resolve` — "Four spellings of the same company came in — which company is this, really?"
- `contact.enrich` — "Buy the fields we are missing on these people"
- `company.enrich` — "Buy the firmographics we are missing on these companies"
- `employment.check` — "Has this person left the company we have them at?"
- `email_address.verify` — "Will this address actually accept mail, and what does our policy say to do about it?"
- `phone_number.verify` — "Is this number reachable, and is it a mobile, a desk line or a virtual one?"
- `enrichment_policy.define` — "Decide once, in what order we try to buy data, when we stop, and how much we will spend"
- `enrichment_policy.get` — "What is our current buying order and spend ceiling?"

## 02 — Contacts, identity and hygiene

*People and audience* · [catalog-02-contacts.md](catalog-02-contacts.md)

- `contact.search` † — "Who do we already have that matches this?"
- `contact.get` † — "Show me this person's record"
- `contact.resolve` — "Here are some identity keys — is this somebody we already have?"
- `contact.create` † — "Put these people in"
- `contact.update` — "Change these fields on these people"
- `contact.delete` — "Take this person out of the working database"
- `contact.restore` — "Bring back the person we deleted"
- `timeline.get` — "What has actually happened to this person?"
- `provenance.set` — "Record where this person's data came from, and under what terms"
- `job_change.record` — "This person has moved to a new employer — fork the record"
- `cooldown.set` — "Come back to this person in Q4, not before"
- `contact.disqualify` — "This person is not a prospect, and here is why"
- `contact.requalify` — "We were wrong, or the situation changed — they are a prospect again"
- `contact.normalize` — "Put these records into our house format, and keep what was there before"
- `identity_policy.define` — "Decide how we tell whether two records are the same human"
- `identity_policy.get` — "What rules are we currently matching people under?"
- `duplicate.list` — "Where do we think we have the same human twice?"
- `duplicate.merge` — "These records are one person — combine them"
- `duplicate.link` — "Same human, two records, and both have to stay"
- `duplicate.unlink` — "We were wrong — those are two different people"
- `duplicate.reject` — "Those two are not the same person — stop asking me"
- `field.define` — "This source carries something we have nowhere to put — make somewhere"
- `field.retire` — "We do not use this field any more"

## 03 — Audience and lists

*People and audience* · [catalog-03-audiences.md](catalog-03-audiences.md)

- `segment.define` — "Save this definition of who we are going after"
- `segment.get` — "Show me what this segment actually says" — without evaluating it.
- `segment.list` — "What audience definitions do we have?"
- `segment.delete` — "Retire this definition"
- `segment.preview` — "Before I spend anything: will this definition actually run, and how big is it?"
- `segment.materialize` — "Freeze this audience as it is right now and give it a name"
- `list.create` — "Make a named list"
- `list.list` — "What lists do we have?"
- `list.get` — "What is on this list, and where did it come from?"
- `list_membership.add` — "Put these people on this list"
- `list_membership.remove` — "Take these people off this list"
- `list.delete` — "We are done with this list"
- `audience.assess` — "Is this set of people actually fit to work?"
- `audience.screen` — "Re-check this list against everything we must not contact, and take them out"
- `contactability_policy.define` — "Decide who we are willing to send to, before anyone asks"
- `contactability_policy.get` — "What are we currently willing to send to?"

## 04 — Import

*People and audience* · [catalog-04-import.md](catalog-04-import.md)

- `source.inspect` — "Look at this file and tell me what is in it and how it would land"
- `import.apply` — "Take this file, this mapping and these rules, and put the people in"
- `import.list` — "What imports have we run?"
- `import.get` — "Show me that import in full"
- `import.revert` — "That import was wrong — undo it"

## 05 — Campaign, schedule, windows and limits

*The programme* · [catalog-05-campaigns.md](catalog-05-campaigns.md)

- `campaign.list` † — "What is running?" — every campaign with its state and health: fine, not progressing, degraded, blocked.
- `campaign.get` † — "Show me this campaign" — settings, senders, schedule and timezone policy, pacing, reply policy, declared successor, and the count of people in each state.
- `campaign.create` — "Start a new campaign" — always a draft; creating never sends and never arms anything.
- `campaign.clone` — "Build this one from the one that worked" — structure, steps, variants, schedule, pacing and reply policy; never enrolments, history or results.
- `campaign.update` — "Change the campaign's own settings" — name, owner, description, personalisation tier, declared successor, and the exit rules that are not reply events.
- `reply_policy.set` — "What happens when they answer?" — one decision per event class, plus the window inside which a reply is still attributed to this campaign.
- `schedule.set` — "When may this campaign send?" — windows per day, timezone policy with its ordered fallback, delay basis, out-of-window behaviour, attached blackout calendars.
- `pacing.set` — "Slow this campaign without slowing everything else on the same mailbox" — people admitted per day and messages sent per day, plus minimum gap, jitter and cold-start ramp.
- `sender_binding.set` — "Send this from these mailboxes, numbers or social accounts" — for a campaign or for named participations.
- `campaign.validate` — "Is this safe to turn on?" — missing or unpublished content, steps no sender can serve, a schedule that never opens, no terminal condition, the count already enrolled, projected first-24-hour volume, projected metered cost, whether the test send passed, and any successor campaign.
- `campaign.test` — "Let me receive step 1 myself, and click the unsubscribe link, before two hundred strangers do" — only to addresses the workspace can demonstrate it owns.
- `campaign.activate` — "Turn it on"
- `campaign.pause` † — "Stop this campaign" — incident response: never requires a lookup first and never waits for permission.
- `campaign.resume` — "Start it sending again" — refuses unless it is actually paused.
- `campaign.freeze` — "Suspend it over the holidays, the conference, the incident" — carries a mandatory expiry and resumes itself.
- `campaign.unfreeze` — "Let it go early"
- `campaign.archive` — "Retire it" — refused while active enrolments remain unless they are first stopped with a reason.
- `campaign.unarchive` — "Bring it back into the working set" — never resumes sending.
- `schedule.resolve` — "Show me the actual calendar" — day by day, for a campaign or one person's participation, including what a weekend, a holiday, a closed window or a deferral moved.
- `send_window.resolve` — "When may this person be contacted on this channel?" — legal quiet hours in their local time, operator blackouts, working days, the campaign's windows, and the basis used for each.
- `blackout.define` — "Nothing goes out that week" — named date ranges and region-coded holiday calendars, each with defer-or-skip and whose region applies.
- `blackout.list` — "What is currently suppressing sending, and until when?"
- `blackout.remove` — "Lift the suppression"
- `frequency_policy.define` — "Nobody hears from us more than this often" — max touches per person, account or domain per window type; which message classes count and which are exempt; and whether a breach defers with a date or skips.
- `frequency_policy.get` — "What cap is in force here, and where did it come from?"
- `capacity.estimate` — "Can this plan physically run?" — sending capacity, windows, blackouts, pacing and existing load over a stated horizon, against the volume the plan implies.

## 06 — Steps, content and content policy

*The programme* · [catalog-06-steps.md](catalog-06-steps.md)

- `step.list` — "What does this campaign actually send, in what order, and which steps owe a human?"
- `step.add` — "Add a step" — channel, execution mode, kind, position, delay with unit and basis, optional declared intent, personalisation mode and budget, content binding, threading, and for a branch its condition, evaluation window and early-satisfaction behaviour.
- `step.update` — "Change a step" — delay, content binding, intent, personalisation, threading, branch condition, or whether it is enabled at all.
- `step.remove` — "Delete a step" — destroys its variants and their measurement history and can advance everyone waiting on it sooner than they expected.
- `step.reorder` — "Put the new step at position 2" — moves steps without delete-and-re-add, so variants, template bindings and history survive.
- `variant.add` — "Try a second version of this step's copy", optionally with a split share — an unsettable split is reported, never silently assumed to be even.
- `variant.update` — "Change a variant's content, its split share, or whether it is enabled"
- `variant.remove` — "Delete this version" — destroys its measurement history, so a variant under test is disabled rather than removed.
- `variant.list` — "What versions of this step exist, which are enabled, and at what share?"
- `template.create` — "Save this copy so other campaigns can use it" — creates the template and its first draft version; a draft cannot be sent.
- `template.update` — "Change the copy" — always produces a new draft version, never mutates a published one.
- `template.get` — "Show me this template" — the content of each version, which version is published, when each was published or retired, and which live campaigns bind each version.
- `template.list` — "What content do we have, and what state is it in?"
- `template.publish` — "Make this version sendable" — a published version is immutable.
- `template.retire` — "Stop anyone using this version" — refused while live campaigns bind it unless a replacement is named; history stays readable.
- `content_policy.define` — "State a rule our copy must obey" — never mention pricing, always carry the identification block, never claim a customer by name, never use a competitor's trademark — each with its scope and whether a breach blocks or warns.
- `content_policy.list` — "Which content rules apply here, and which of them block rather than warn?"
- `content_policy.check` — "Does this draft break any of them?" — takes a rendered message and returns the rules that fired, where, and whether each blocks or warns.

## 07 — Messages

*The programme* · [catalog-07-messages.md](catalog-07-messages.md)

- `message.draft` † — "Show me exactly what this person would receive" — the real bytes: merge fields resolved against this contact, fallbacks applied or reported missing, footer, identification block and unsubscribe mechanism in place.
- `message.send` † — "Send this message to this person" — one person, one channel per call, with that channel's own required elements and cost
- `message.schedule` — "Send this on the 14th" — the Q3 they asked for, the day they come back from leave, the morning after the event
- `scheduled_message.list` — "What is queued to go out, to whom, and when?" — by person, campaign, sender, or everything
- `scheduled_message.cancel` — "Do not send that after all"

## 08 — Enrollment

*The programme* · [catalog-08-enrollment.md](catalog-08-enrollment.md)

- `campaign.enroll` † — "Put these fifty people into the Q3 campaign" — with an explicit collision policy, an explicit start position and an explicit first-touch timing: the authored delay, the next open window, or immediately.
- `enrollment.list` † — "Who is in this campaign?" and "where is this person across everything?" — the same read from either end, and with historical participations included on request.
- `enrollment.get` — "Where is this person in this campaign?" — state, position, why it is not progressing, exit reason, business outcome, the sending identity behind it, and when the next touch is due.
- `enrollment.pause` † — "Hold this person here" — with a reason and an optional dated automatic resume.
- `enrollment.resume` — "Let them carry on" — refuses unless the participation is actually held.
- `enrollment.stop` † — "Take them out, and record why" — the reason is mandatory and must be able to name a different person as the cause.
- `disposition.set` — "What came of this participation?" — the business outcome, from the account's declared vocabulary, distinct from any reply's intent and from the execution state.

## 09 — Conversations and activity

*The conversation, the queue and the day* · [catalog-09-conversations.md](catalog-09-conversations.md)

- `conversation.list` † — "What came back overnight, and what needs answering first?" — filterable by meaning, channel, owner, campaign and how long each has been waiting, ordered by what is most overdue
- `conversation.get` † — "Read me the thread before I answer" — every message in order, who is on it, what we have decided it means, and what it is attached to
- `conversation.classify` † — "What does this reply actually mean, in a value I can plan a branch on — and who decided it, from which message, and how sure were they?"
- `conversation.assign` — "Give this thread to someone else's inbox" — internal routing only; nothing is promised and no clock starts
- `conversation.snooze` — "Not now — bring it back on this date"
- `conversation.close` — "Done with this thread" — worked, not ignored
- `conversation.reopen` — "I closed that too early — put it back in the queue"
- `referral.record` — "I'm not the right person — talk to Steve in finance" — record who they named, where the name came from, and take this person out of the campaign as referred out
- `activity.log` — "Record something that happened outside the tool so the record is not a lie" — an email from a personal client, a hallway conversation, a message on a channel we do not send from, a note

## 10 — Task queue and touches

*The conversation, the queue and the day* · [catalog-10-tasks.md](catalog-10-tasks.md)

- `task.list` † — "What do I owe today, in the order I should do it?" — everything due, including work a signal put there rather than a calendar
- `task.get` — "Show me this one properly, including what it is actually asking me to send"
- `task.create` — "Put work in someone's queue" — a call to make, a message to write, a person to research, a meeting to confirm
- `task.complete` † — "I did this one"
- `task.skip` — "I am not doing this one, and that is different from having done it"
- `task.reschedule` — "The day overran — push these to tomorrow"
- `task.cancel` — "This work should not happen at all — drop it"
- `task.reassign` — "Give this work to someone else"
- `call.place` — "Dial this person now"
- `call.log` — "Record the call that happened: what came of it, how long we talked, and whether it was recorded with everyone's agreement"
- `call_recording.get` — "Play me that call back, and show me the agreement that let us record it"
- `call_recording.discard` — "They did not agree to be recorded, or we should never have recorded it — destroy it and say why"

## 11 — Meetings, qualification and handoff

*The conversation, the queue and the day* · [catalog-11-meetings.md](catalog-11-meetings.md)

- `meeting.propose` — "Offer them a time" — concrete slots, or a way to pick their own; also how we counter when their slots do not work
- `meeting.book` — "Book it" — time, attendees, how to join, and whose calendar and whose meeting it is
- `meeting.confirm` — "Confirm tomorrow's meetings and chase the ones nobody has answered" — a daily act, not an edge case
- `meeting.reschedule` — "Move it, keeping it the same meeting with the same history"
- `meeting.cancel` — "Call it off" — a booked meeting, or an outstanding proposal whose offered times we are withdrawing
- `meeting_outcome.record` — "What actually happened at the appointed time" — held, no-show, cancelled beforehand, moved beforehand, and who did not turn up
- `meeting.list` — "What is on the calendar" — mine or someone else's, coming up or already past, with outcomes
- `meeting.get` — "One meeting in full" — attendees and what each answered, how to join, the owner, and what it came from
- `qualification.record` — "Write down the evidence behind 'this is worth someone's time', in the account's own questions"
- `handoff.create` — "Pass this person on, with everything the receiver needs" — the qualification evidence, the meeting, the thread, and what was agreed as the next step
- `handoff.accept` — "I will take this, and I will contact them within this time"
- `handoff.reject` — "I am not taking this, and here is exactly why"
- `handoff.return` — "I accepted this and I cannot work it — giving it back, with the reason"
- `handoff.withdraw` — "Pull it back before anyone takes it" — they opted out, the meeting fell through, or it went to the wrong person
- `handoff.list` — "What has been handed to me and not answered, and what have I handed on that nobody has picked up?" — with the clock on each
- `handoff.get` — "Everything I need to decide whether to accept" — the evidence, the thread, the meeting, the agreed next step, and who is asking
- `opportunity.create` — "Record the conversion: this became a real deal in the pipeline"

## 12 — Signals and triggers

*The conversation, the queue and the day* · [catalog-12-signals.md](catalog-12-signals.md)

- `signal.list` — "What has happened out there that I should know about" — someone changed job, someone was on the pricing page, funding was announced, usage jumped, someone engaged publicly.
- `signal.get` — "One observation in full, including what it was derived from".
- `signal.acknowledge` — "Handled — stop showing me this".
- `signal_policy.define` — "When two things fire on the same person at once, which one wins, and does it interrupt what is running, add to it, or wait its turn?"
- `signal_policy.get` — "What ordering is actually in force right now?"
- `trigger.create` — "A standing rule: when this happens, produce this work".
- `trigger.update` — "Change the rule, including switching it off without losing it".
- `trigger.list` — "What standing rules are running, what do they watch, and what do they produce?" — in full.
- `trigger.delete` — "Remove the rule entirely".
- `trigger_run.list` — "What did my rules actually do last night" — which fired, on whom, what work came out, and what was held back by policy.

## 13 — Inbound

*The conversation, the queue and the day* · [catalog-13-inbound.md](catalog-13-inbound.md)

- `inbound_lead.record` † — "Somebody raised their hand — record it now, and tell me who they are".
- `inbound_lead.list` † — "What has come in and not been worked, oldest first" — with how long each has been waiting and how long is left.
- `inbound_lead.get` — "Everything about this one" — what they told us, who they turned out to be, what we already have running against them, who owns it and what the clock says.
- `inbound_lead.route` — "Send it to the right owner by the rule" — territory, who already owns the account, round-robin, whoever is actually available.
- `inbound_lead.claim` — "Mine — taking it off the shared queue".
- `inbound_lead.release` — "I cannot work this — put it back".
- `inbound_lead.triage` — "What kind of request is this actually?" — a sales enquiry, a support problem, a billing question, a job application, someone selling to us, a student, an existing customer wanting more, or noise.
- `inbound_lead.close` — "Done with this arrival, and here is what became of it" — a meeting, a handoff, answered and nothing more, routed elsewhere, unreachable, or nothing.
- `inbound_lead.reopen` — "That was closed too early".
- `response_policy.define` — "State how fast a human has to respond, by source and by what kind of request it is — and what happens when the target is missed".
- `response_policy.get` — "What are we actually holding ourselves to?"

## 14 — Consent, suppression and privacy

*Permission and capability* · [catalog-14-privacy.md](catalog-14-privacy.md)

- `outreach.precheck` † — "May I contact this person, on this channel, right now — and if not, when, and what would fix it?"
- `suppression.check` — "Is this address, number, profile, domain or account one we must not touch — and why?" — works on a raw identifier that is not a person in our system yet.
- `suppression.list` — "Show me the exclusion lists" — by scope, reason class and who put them there.
- `suppression.add` † — "Never contact this again, and here is why" — a person, an address, a number, a profile, a domain or a whole account.
- `suppression.remove` — "Lift this exclusion, and here is the evidence that lets me".
- `optout.record` † — "They said stop" — records the act itself: what they said, where it arrived, when they did it, when we learned of it.
- `optout.poll` — "What stop signals happened at the channel that we have not ingested yet?" — one-click unsubscribes, list-unsubscribe requests, blocks, stop keywords, platform-side opt-outs.
- `optout.confirm` — "Send the one confirmation of their opt-out that this channel permits" — exactly once, non-promotional, and it may never require anything of the recipient.
- `consent.record` — "Here is the permission we hold, what they were told, when, how it was captured, and what it covers".
- `consent.revalidate` — "Has a time-limited permission quietly expired?" — recompute it and downgrade contactability without waiting for a human.
- `consent.prove` — "Show me the evidence that we were allowed to send this" — status plus the bundle.
- `notice.send` — "Deliver the privacy notice to someone whose data we did not get from them, and record that we did".
- `disclosure.check` — "Must this outreach say it was machine-generated — in what form, in which places, on this channel?"
- `disclosure.record` — "It did say so, here, in this message".
- `dnc_registry.check` — "Screen these numbers or addresses against the external do-not-contact register this jurisdiction requires".
- `privacy_request.create` — "Someone has asked to see, correct, restrict, port or erase their data — start the clock".
- `privacy_request.update` — "Verify who they are, or extend the deadline with the reason recorded, because the reason has to be told to them".
- `privacy_request.refuse` — "Refuse it, with the demonstrable basis".
- `privacy_request.fulfill` — "Export or erase against a recorded request — the only path to either".
- `deletion_feed.poll` — "Pull the erasure demands waiting on the external register and match them against what we hold".
- `deletion_feed.report` — "Tell the register what we did with each of its requests".
- `retention.apply` — "Enforce the storage limits — and stop at the floors that an erasure may not cross".
- `audit_log.search` — "Who changed what, when, on our side — and why was this person contacted at all?"

## 15 — Sending capability and deliverability

*Permission and capability* · [catalog-15-sending.md](catalog-15-sending.md)

- `sender.list` — "What can we send from?" — mailboxes, social accounts, numbers and business messaging identities in one list.
- `sender.get` — "Show me this sending capability in full" — identity, channel, configured pacing, message defaults, pool membership, registration.
- `sender.health` † — "Can this sender send today, and if not why?" — connection, restrictions, warm-up position, capacity budgets, and the channel's automation sanction.
- `sender.connect` — "Attach a mailbox, social account, number or business messaging identity".
- `sender.disconnect` — "Detach it" — refused while live enrolments or open conversations depend on it, unless a rebind target is supplied.
- `sender.reauthorize` — "Restore a lapsed authorisation without losing the history behind it".
- `sender.pause` — "Stop this capability sending, now" — the first move in every incident on every channel.
- `sender.resume` — "Let it send again" — refuses unless the warm-up position is valid or an override is recorded.
- `sender_limit.set` — "How much may this send, how fast, and with what spacing" — one decision, not three.
- `warmup_plan.set` — "Define the ramp this sender follows, and whether it is running".
- `message_element.set` — "The mandatory elements every message from this sender carries" — legal identification block, postal address where required, the channel's required stop artefacts each separately (on email: header, one-click endpoint, visible link), tracking domain.
- `sender.reserve` — "Claim one unit of allowance so parallel work cannot spend it twice".
- `sender.release` — "Give the claim back".
- `sender.summarize` — "This sender's numbers over a window" — failures by class, complaints with their publisher and denominator, deferrals, throttles.
- `restriction.record` — "The platform has restricted this sender" — including the ones we only learned about from a refusal.
- `restriction.appeal` — "Appeal it where the platform offers a route — and say plainly when it does not".
- `sender_pool.create` — "Group these capabilities so work can rotate across them".
- `sender_pool.update` — "Change who is in the pool and how work is routed across it" — including sticky per recipient, which on conversational channels is correctness and not optimisation.
- `sender_pool.list` — "What pools exist, who is in them, and how do they route?"
- `sender_pool.delete` — "Dissolve the pool" — refused while campaigns bind it unless a replacement target is supplied; the capabilities themselves are untouched.
- `sending_domain.register` — "Declare a domain we send from".
- `sending_domain.list` — "Which domains do we send from, and what is each one's compliance state as of when?"
- `sending_domain.retire` — "Stop treating this as a domain we send from" — refused while any sender on it has live enrolments or open conversations, unless a rebind target is supplied.
- `authentication_requirement.list` — "What has to be published for this domain to authenticate" — publishing it is not ours to do.
- `domain_compliance.check` — "Per publisher, per requirement: pass, fail or unknown — and name the failing requirement".
- `domain_reputation.get` — "The named signals about this domain, each with its publisher, scale, window and denominator" — including blocklist listings and the dataset each came from.
- `message_compliance.check` — "Check this rendered message against the requirements it will actually be held to" — required elements, a working unsubscribe checked properly, and the reputation of every domain it links to.
- `placement_test.create` — "Send this to a monitored set of addresses and see where it lands".
- `placement_test.get` — "What did the test say, per mailbox provider" — typed as simulated placement, never as our inbox rate.
- `messaging_registration.submit` — "Register the brand and the use case, because on this channel we may not send at all until it is approved".
- `messaging_registration.get` — "What is the registration's status, and on refusal what must change?"
- `approved_content.submit` — "Submit content for platform pre-approval, where the platform gates the content rather than the sender".
- `approved_content.get` — "Approval status and quality state" — including whether the platform has paused it under us.
- `approved_content.retire` — "Withdraw it from use" — the correct fix when it is the content that got flagged and not the sender.

## 16 — Social and messaging channels

*Permission and capability* · [catalog-16-social.md](catalog-16-social.md)

- `social_invitation.send` — "Connect with them first, then talk" — a connection request, with an optional note.
- `social_invitation.list` — "What is still pending, and how old is it?"
- `social_invitation.withdraw` — "Take one back" — and it does not lift a restriction.
- `social_invitation.accept` — "Accept an invitation someone sent us".
- `social_invitation.decline` — "Decline one we do not want".
- `social_relationship.get` — "What is our standing with this person — connected, pending, none — and at what degree?"
- `social_profile.view` — "Open their profile" — it spends a counted allowance, and on some channels they can see that we did.
- `social_profile.follow` — "Follow them without connecting".
- `social_profile.unfollow` — "Stop following".
- `social_post.react` — "React to something they posted".
- `social_post.comment` — "Comment on something they posted" — published content under a named human's identity.
- `social_credit.get` — "How many metered messaging credits are left, when do they expire, and does a reply give one back?"
- `conversation_window.get` — "May I write to this person freely right now, or only with pre-approved content, or only by spending a credit?"

## 17 — Oversight, review and safety

*The organisation around the work* · [catalog-17-oversight.md](catalog-17-oversight.md)

- `approval.request` — "Put something in front of a human and wait" — a message about to go, a bulk write, a piece of content, a policy change.
- `approval.list` † — "What is waiting for a human to look at, and how long has it been waiting?"
- `approval.get` — "Show me this one item exactly as it will go out, with the reasoning behind it"
- `approval.resolve` † — "Approve this, or reject it with a reason." Approving is the send.
- `autonomy.get` — "What may this agent currently do without asking, in this scope, on this channel — and where does the gate live?"
- `autonomy.set` — "Set what runs unattended and what stops" — the gate, the mode, the queue timeout and what a timeout means, the unattended action cap, the escalation target, and which channels are treated differently.
- `escalation.raise` — "I cannot or must not proceed; a named human owns this now" — explicitly not an approval, because nobody is granting permission.
- `escalation.list` — "What has been handed to a human and not yet picked up, and how old is it?"
- `escalation.resolve` — "Record how it ended, and whether the work comes back to the agent or stays with the human"
- `editorial_review.record` — "A named human read this machine-drafted message, changed what they changed, and let it go" — bound to the message actually sent, with the editor, the time and the difference from the draft.
- `editorial_review.get` — "Show me the human review behind this message that went out — or tell me plainly that there is none"
- `review.request` — "Put a call, a sent message or an unsent draft into someone's review queue" — mine going out for feedback, or a manager's going to a named reviewer.
- `review.list` — "The review queue: what is waiting, what is done, by person and by period"
- `review.get` — "One review in full" — the answers, the scorecard version used, who was scored, who scored, and whether I am entitled to see it.
- `review.score` — "Score an artefact against a published scorecard" — recording who was scored, who scored, and which version was used.
- `feedback.record` — "I have actually spoken to them about it" — scored and told-them are different facts and only one changes behaviour.
- `review_policy.define` — "State which completed work gets reviewed, at what sample rate, how far back, and by whom"
- `review_policy.get` — "What is the review policy in force, and what coverage does it actually imply?"
- `scorecard.define` — "The questions, their types and their weights, with the arithmetic stated out loud"
- `scorecard.list` — "What scorecards exist, which are published, and what is each scoped to?"
- `scorecard.publish` — "Make this version usable for scoring"
- `scorecard.retire` — "Take it out of use for new scoring" — everything already scored keeps its history.
- `coaching.summarize` — "Per coach, per period: how much coaching actually happened" — attended, listened, commented, scored, feedback given.
- `budget.get` — "What allowance is left on this meter, in this scope, for this period?"
- `budget.set` — "The ceiling the pre-flight check reads" — a hard stop, not an alert.
- `stoprule.set` — "The condition that stops the work by itself" — what is measured, over what window, at what threshold, and what it halts.
- `stoprule.list` — "What stop rules are in force right now, over what scope, and what would each halt?"
- `stoprule.clear` — "Remove a stop rule"
- `stoprule_firing.list` — "What has fired, when, on what measured value, and what is still stopped because of it?"
- `stoprule_firing.get` — "One firing in full" — the rule, the value that tripped it, the operations halted, and every item left unprocessed.
- `outreach.hold` † — "Stop everything for this person, this company or this domain, now" — the kill switch.
- `outreach.release` — "Lift a hold"

## 18 — The team: people, ownership, workload and targets

*The organisation around the work* · [catalog-18-team.md](catalog-18-team.md)

- `actor.list` — "Who is on our side of the table, and what state are they in: active, ramping, unavailable, gone?"
- `actor.get` — "One person: their state, their team, and the ceilings that apply to them"
- `team.list` — "What groups do people belong to?"
- `workload.get` — "What is this person actually carrying" — open tasks, what is due today, live enrolments, and everything wired to them that would break if they left tomorrow.
- `workload_policy.define` — "State how much one person may be holding at once" — open tasks, live enrolments, touches assigned per day — and whether a breach defers the work or refuses it.
- `workload_policy.get` — "What ceiling applies to this person, and how much headroom is left?"
- `ownership.get` — "Who owns this person or this company — and would acting on it make me the owner?"
- `ownership.assign` — "Give this contact or account to someone" — a named person, or whoever an assignment policy picks.
- `assignment_policy.define` — "The rule that picks who gets the next piece of work" — a pool, a selection method and an availability test.
- `assignment_policy.get` — "What routing rule is in force here, and who would it pick right now?"
- `book.transfer` — "Move someone's whole book of work to someone else, or split it across a policy" — ownership, the senders behind live threads, open tasks, open conversations, authored content.
- `goal.define` — "Set the target" — the number, the unit this account actually pays on, the period, and the ramp for anyone not yet at full load.
- `goal.get` — "What is this person's or team's target for this period, and in what unit?"
- `goal.list` — "What targets exist across the team this period?"
- `pace.get` — "Am I on track?" — target, achieved, in flight, working days gone and left, the run rate needed from here, and what makes the projection unreliable.

## 19 — Accounts and buying groups

*The organisation around the work* · [catalog-19-accounts.md](catalog-19-accounts.md)

- `account.get` — "Show me this company as something we own" — its state, its tier, who owns it, what is happening on it.
- `account.search` — "Which of the companies we already hold match this?" — never queries an outside database and never costs anything.
- `account.update` — "Change the company's state — in play, enrolled, nurture, blocked, customer, partner — or its tier, with a reason."
- `account.claim` — "Reserve this company for my attention for a stated period" — a lease, not ownership, and it expires by itself.
- `account.release` — "Give the claim back before it expires."
- `account_membership.create` — "Attach this person to this company deliberately, rather than letting a new company appear for every spelling of a name."
- `account_membership.list` — "Who do we hold at this company, how do we know they belong to it, and which of them is being touched right now?"
- `account_membership.delete` — "Detach a person from this company."
- `collision.check` — "Who else is touching this company, and why?" — my other campaign, another person's campaign, or a live customer, support or opportunity motion.
- `buying_group.define` — "Name the people who decide together at this company, and what each of them does in the decision" — create-or-replace over the whole group; defining it with no members removes it.
- `buying_group.get` — "Show me the group: who is in it, in what role, and where each of them stands."
- `buying_group.enroll` — "Engage the group deliberately" — how many at once, staggered by how many working days, in what order, and what one person's reply does to the others.

## 20 — Measurement

*The organisation around the work* · [catalog-20-measurement.md](catalog-20-measurement.md)

- `engagement.summarize` † — "How is this doing?" — one measure set over one scope, grouped the way the question actually needs.
- `engagement_event.list` — "Show me the rows behind the number"
- `funnel.summarize` — "The whole ladder, stage by stage, with each stage's declared denominator and how well each stage can be known at all"
- `opportunity.summarize` — "What came out the far end, and what was it worth — under which attribution model, on whose authority?"
- `attribution.explain` — "Why was this outcome credited here, and what would a different model have said?"
- `response_time.summarize` — "How fast do we actually pick things up" — an inbound lead from arrival to first human touch, a reply from arrival to answer — by owner, route, source and hour of day.
- `metric.describe` — "What does this measure actually mean here, what was it mapped from, what can it be grouped by, and how much can it be trusted?"
- `metric.compare` — "Two arms, side by side" — the counts, the sample size this comparison needs, the interval around each rate, and whether there is enough data to say anything at all.
- `export.request` — "Get me a bounded extract for reporting" — never the route for a subject access request, which is `privacy_request.fulfill`.
- `export.get` — "Collect the extract"

## 21 — Introspection and runtime

*The organisation around the work* · [catalog-21-introspection.md](catalog-21-introspection.md)

- `operation.search` † — "Find me the operation for this" — the door to everything not sitting in front of you.
- `operation.describe` — "Show me one operation in full" — what it does, what it refuses to do, its five properties, worked examples, and the operations it must not be confused with.
- `operation.preview` — "Dry run this exact call" — who would be reached and how many, when the first touch would land, what allowance it would consume, and whether the effect resolves to a send.
- `plan.validate` — "Check a whole plan before any of it runs" — authority for every step, ordering, preconditions, live bindings, budget headroom and capacity.
- `contract.describe` — "Which version of this contract is in force, what was renamed from what, the derivation table and absence-defaults it binds you to, and how much of this account's vocabulary it covers"
- `capability.list` † — "What is this credential actually allowed to do?" — granted, denied, or unknown, one answer per capability.
- `vocabulary.list` † — "What values is this account allowed to use?" — stages, dispositions, reply categories, enrolment statuses, meeting outcomes, step intents, exit reasons, content-policy rule classes, and the properties a branch condition may be written over.
- `schema.describe` — "Which fields exist on this kind of record, and which of them can be written" — custom fields included.
- `channel.describe` — "A channel's own rules" — whether automation is sanctioned there at all, how its message allowance works, how long a reply window stays open, what recording consent it expects.
- `adapter.describe` — "How much, how fast, inline or queued" — request ceilings, batch ceilings, page sizes, and the scope and lifetime of an idempotency key.
- `adapter.verify` — "Prove by test, not by assertion, which operations this installation actually fulfils, how faithfully, and where a promise is composed out of several smaller acts."
- `job.get` — "How far has this queued work got, and what happened to each item?"
- `job.cancel` — "Stop queued work.
- `invocation.get` — "I lost the result of a call — show me what actually happened, by the key I sent, instead of running it again."
- `term.resolve` — "Someone said a word I do not use — a colleague's shorthand, an inherited spreadsheet header, another system's label.
