// What every ending this package can meet means, as a table rather than as a habit.
//
// Three things are decided here and nowhere else. The first is the read/write mark of every command this package
// runs: a lost answer to a read costs nothing to repeat, while a lost answer to a write may already have put a person
// into a sequence. The second is which row each of the Reply CLI's own error codes is, operation by operation. The
// third is the code and the failure class each row reports, which is what the runtime acts on — `transient` is
// retried, `permanent` and `validation` are final, `ambiguous` is repeated only where the operation's contract
// says a repeat is safe, and `blocked` holds the work on the block the row's `block` names until a trial gets past it.
//
// The constants below are STRICT JSON: double-quoted keys and strings, no comments inside them, no trailing commas,
// and each one ends with a closing brace at the start of a line followed by a semicolon. A test on the runtime side
// parses exactly that span out of this file and holds every row to the published contract of the operation that
// names it, and every mapped code to a row that exists. There is no second copy of it anywhere. Keep editing them as
// JSON.

// Every command this package runs, and whether it is a read or a write. Every one is a work command: the package makes
// no raw call, so nothing here starts `api`, and a test holds the table to that.
export const COMMANDS = {
  "sequence get": {
    "kind": "read",
    "note": "The sequence as Reply holds it: what campaign.get answers from, and the read enrollment.stop makes of a campaign it names for the first time."
  },
  "sequence contacts": {
    "kind": "read",
    "note": "One page of the people a sequence holds, and Reply's count of them: what enrollment.list answers from, and the read campaign.create makes of a sequence an earlier attempt may have made, to see that it holds nobody."
  },
  "sequence stats": {
    "kind": "read",
    "note": "A sequence's counts over one window: what engagement.summarize answers from."
  },
  "inbox list": {
    "kind": "read",
    "note": "One page of one of Reply's thread buckets, by person or by sequence: what conversation.list answers from — the inbox alone for inbound threads, the inbox and the sent threads merged for every thread."
  },
  "inbox get": {
    "kind": "read",
    "note": "One thread, its subject, and one page of its messages: what conversation.get answers from, and the read conversation.reply makes of the thread's channel and person, one message long, before it sends anything."
  },
  "inbox changes": {
    "kind": "read",
    "note": "One page of Reply's change feed: the threads that moved in the account, oldest first, from a cursor or from a moment within the last 30 days, each with its identifiers, its moment and who moved it. What events.read answers from."
  },
  "inbox categories": {
    "kind": "read",
    "note": "The account's inbox categories, Reply's reserved ones included: what conversation.classify matches the category it was given against, by identifier or by name, before it files anything."
  },
  "contact get": {
    "kind": "read",
    "note": "One contact by its id, as Reply holds it: the read contact.delete makes before it deletes anything, of whether Reply still holds the contact and whether it holds them as opted out; and the read a send makes of the same and of where the contact is reached, for a person Reply holds, an answer without that key read as lost. Nothing is kept."
  },
  "contact update": {
    "kind": "write",
    "note": "Changes the fields and custom fields stdin names on the contact, by its id; a null clears one and the rest stay. Given the person by their fields instead, beside the fields on stdin, it imports a person Reply holds nobody for first, as an add would, and then sets the fields on the contact made or matched. Set outright, so a repeat by the same contact lands on the same values. Before a LinkedIn send it also gives a contact that carries no LinkedIn profile the one the send consumes, and nothing else."
  },
  "contact delete": {
    "kind": "write",
    "note": "Deletes the contact by its id, with its history at Reply: it leaves every sequence and list. Run again after a lost answer, it answers that Reply holds the contact no longer."
  },
  "contact find": {
    "kind": "read",
    "note": "Reply's exact lookup of one address, an email or a LinkedIn URL, read to its end and creating nobody: how every operation about a person with no pin finds the contact Reply holds for them before anything is written. The address is on stdin."
  },
  "sequence list": {
    "kind": "read",
    "note": "Every sequence whose whole name is the one on stdin, ignoring case, read to its end: what campaign.create compares with the name it is to give, before it makes anything."
  },
  "sequence add-contact": {
    "kind": "write",
    "note": "Puts the person into the sequence, importing them first when they are given by their fields. Into a live sequence this is a send, so it is the most expensive repeat of all."
  },
  "sequence remove-contact": {
    "kind": "write",
    "note": "Takes the person out of the sequence; their history stays. It never creates anybody."
  },
  "sequence pause": {
    "kind": "write",
    "note": "Pauses the sequence."
  },
  "sequence start": {
    "kind": "write",
    "note": "Starts the sequence, or resumes a paused one, only while it holds the population a person approved. Step 1 goes to everybody enrolled, so it is a send."
  },
  "contact-list add-contact": {
    "kind": "write",
    "note": "Puts the person on the list, importing them first when they are given by their fields."
  },
  "contact opt-out": {
    "kind": "write",
    "note": "Sets Reply's opt-out flag for the person, creating them only when Reply holds nobody under the address."
  },
  "sequence create": {
    "kind": "write",
    "note": "Makes the sequence campaign.create answers with: a draft that holds nobody, with one task step for its owner. The name travels on stdin."
  },
  "inbox reply": {
    "kind": "write",
    "note": "Replies to the thread's person on the thread's own channel, from the account that owns the thread, with the text on stdin. A send that Reply keeps no key on, so a lost answer is never repeated."
  },
  "inbox categorize": {
    "kind": "write",
    "note": "Files the thread under one category, or clears its category: set outright, so a repeat lands on the same state."
  },
  "inbox mark-read": {
    "kind": "write",
    "note": "Marks the thread read, which is what conversation.close is at Reply: set outright, so a repeat changes nothing."
  },
  "inbox mark-unread": {
    "kind": "write",
    "note": "Marks the thread unread, which is what conversation.reopen is at Reply: set outright, so a repeat changes nothing."
  },
  "auth whoami": {
    "kind": "read",
    "note": "Who the credential signs in as: the one Reply user whose accounts sender.list offers under `own`. Only the user's id is read; the login name beside it is the operator's and is not kept."
  },
  "direct-send get": {
    "kind": "read",
    "note": "A direct send Reply keeps, by its reference. Asked for two things. Of `email-1`, for every channel, before a send that names a moment and before a repeat after an attempt that may have sent: whether the account's Reply keeps direct sends — its own answer about the send it names, against no route at all. And first on a repeat, of the send an earlier attempt that may have sent returned (`context.unsettled_reference`): Reply's record of it, whose state answers the work item with nothing sent. And of the send the runtime names under `send.reference`: what message.status answers from."
  },
  "direct-send cancel": {
    "kind": "write",
    "note": "Takes back a direct send Reply still holds for its moment, by its reference, and answers with Reply's record of it. One cancelled before answers as it stands, so a repeat after a lost answer changes nothing; one that went, that Reply is sending or that failed is refused, and nothing is changed. What message.cancel answers from."
  },
  "account list": {
    "kind": "read",
    "note": "The mailboxes or the LinkedIn accounts a send can go from, one channel at a time, teammates' included, each with the Reply user whose it is: what sender.list answers from."
  },
  "schedule list": {
    "kind": "read",
    "note": "Every sending schedule the team can use, in one answer, each with its Windows time zone and its windows by weekday: what schedule.list answers from."
  },
  "contact linkedin-connect": {
    "kind": "write",
    "note": "Invites the contact to connect on LinkedIn from the account named, with the note on stdin or with none. Given a person Reply holds nobody for by their fields, it imports them first, as an add would, and goes only to the contact it made: one Reply already held it names, sending nothing. Made under the work item's key, and at the moment it goes out after where the arguments name one still ahead: a lost answer is repeated under the same key, which Reply answers with the send it took and sends nothing twice."
  },
  "contact send-email": {
    "kind": "write",
    "note": "Sends the contact one email from the mailbox named, outside any sequence, with the subject and the body on stdin — the text a person approved, written as HTML, or a template in Reply's syntax, which Reply fills in before it sends. Given a person Reply holds nobody for by their fields, it imports them first, as an add would, and goes only to the contact it made: one Reply already held it names, sending nothing. Made under the work item's key, and at the moment it goes out after where the arguments name one still ahead: a lost answer is repeated under the same key, which Reply answers with the send it took and sends nothing twice."
  },
  "contact linkedin-message": {
    "kind": "write",
    "note": "Sends the contact one LinkedIn message from the account named, outside any sequence, with the text on stdin; the contact has to be a connection of the account. Given a person Reply holds nobody for by their fields, it imports them first, as an add would, and goes only to the contact it made: one Reply already held it names, sending nothing. Made under the work item's key, and at the moment it goes out after where the arguments name one still ahead: a lost answer is repeated under the same key, which Reply answers with the send it took and sends nothing twice."
  },
  "contact linkedin-inmail": {
    "kind": "write",
    "note": "Sends the contact one InMail from the account named, outside any sequence, with the subject and the text on stdin. Given a person Reply holds nobody for by their fields, it imports them first, as an add would, and goes only to the contact it made: one Reply already held it names, sending nothing. Made under the work item's key, and at the moment it goes out after where the arguments name one still ahead: a lost answer is repeated under the same key, which Reply answers with the send it took and sends nothing twice."
  },
  "contact linkedin-voice": {
    "kind": "write",
    "note": "Sends the contact one voice message from the account named, outside any sequence, which Reply speaks from the script on stdin; the contact has to be a connection of the account. Given a person Reply holds nobody for by their fields, it imports them first, as an add would, and goes only to the contact it made: one Reply already held it names, sending nothing. Made under the work item's key, and at the moment it goes out after where the arguments name one still ahead: a lost answer is repeated under the same key, which Reply answers with the send it took and sends nothing twice."
  }
};

// Which row each of the Reply CLI's error codes is. An operation's own mapping wins over the shared one; a code in
// neither is `auth.*` (who is calling), `usage.*` (refused before anything was sent), or a refusal this version has
// no word for. `reply.invalid_request:email` is the row for an invalid request whose fields name the email — and
// `reply.invalid_request:message` one whose fields name a send's text — and
// `reply.refused:inboxThread.invalidInput:RenderingFail` the row for a refusal Reply explained by its own code and by the
// first word of its sentence, up to the first colon or white space. A key that starts `template:` is that row only for a
// send composed as a template, before the code alone: Reply answers a template it could not fill in under the code it
// gives a mailbox that failed, and a LinkedIn text it could not fill in under the code it gives a final text holding a
// placeholder, so the same refusal of a final text keeps the row it has. A key `cursor:reply.refused:400` is that row only
// for a read handed a cursor, and only for a refusal at that status Reply explained by a code of its own.
//
// `usage` alone is an exit 2 the CLI gave no code for: commander's own refusal of a command or a flag it does not know,
// written as text. An operation without that key reports it as `call_refused_before_it_was_sent`, as it reports every
// `usage.*`.
//
// `reply.refused:404` is a refusal at 404 with no code of Reply's own, which is how Reply's API answers a route it does
// not have. The CLI passes such a 404 through as it is on every command but the nine that read every 404 as the thing
// they asked for not being found and `auth whoami`, which never answers `reply.refused`, as the row says. A 404 Reply
// explains with a code keeps the row that code has.
//
// `outcome.unknown` is never looked up here for a work command: the CLI answers it with exit 3, and `cli.js` reads an
// exit 3 by the command's own read/write mark in `COMMANDS` before any code is mapped. Its row says what that ending is;
// the mark decides it — which is why `inbox reply` being marked a write is what keeps a lost reply from being sent again.
//
// `inbox.category_not_found` under `conversation.classify` is kept as a defence and is not reached against a live
// account: Reply answers a filing under a category it does not hold, on a thread it does, as a missing thread. The
// package resolves the category from Reply's list first, so that answer comes back only for a category that disappeared
// between the list and the filing, and it is reported as `conversation_not_found`.
export const CODES = {
  "shared": {
    "access.denied": "unauthorized",
    "access.feature_unavailable": "unauthorized",
    "rate_limited": "rate_limited",
    "reply.unavailable": "reply_not_reached",
    "outcome.unknown": "answer_lost_on_a_write",
    "sequence.busy": "reply_busy",
    "reply.refused": "refusal_this_version_has_no_word_for",
    "reply.refused:404": "route_not_offered",
    "reply.invalid_request": "refusal_this_version_has_no_word_for"
  },
  "per_operation": {
    "campaign.create": {
      "sequence.list_incomplete": "list_not_finished"
    },
    "optout.record": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.invalid_email": "invalid_channel_value",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value"
    },
    "enrollment.stop": {
      "sequence.not_found": "campaign_not_found",
      "contact.not_unique": "contact_not_unique"
    },
    "campaign.enroll": {
      "sequence.not_found": "campaign_not_found",
      "sequence.archived": "campaign_not_enrollable",
      "sequence.has_no_steps": "campaign_not_enrollable",
      "sequence.step_not_found": "call_could_not_be_built",
      "usage.range": "call_could_not_be_built",
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.opted_out": "suppressed",
      "contact.invalid_email": "invalid_channel_value",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value"
    },
    "list_membership.add": {
      "contact-list.not_found": "list_not_found",
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.opted_out": "suppressed",
      "contact.invalid_email": "invalid_channel_value",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value"
    },
    "campaign.pause": {
      "sequence.not_found": "campaign_not_found",
      "sequence.archived": "campaign_not_pausable",
      "sequence.not_pausable": "campaign_not_pausable"
    },
    "campaign.activate": {
      "sequence.not_found": "campaign_not_found",
      "sequence.archived": "campaign_not_activatable",
      "sequence.not_startable": "campaign_not_activatable",
      "sequence.contact_count_changed": "population_changed"
    },
    "conversation.list": {
      "access.feature_unavailable": "feature_not_included"
    },
    "conversation.get": {
      "inbox.thread_not_found": "conversation_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "enrollment.list": {
      "sequence.not_found": "campaign_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "engagement.summarize": {
      "sequence.not_found": "campaign_not_found",
      "access.feature_unavailable": "feature_not_included",
      "usage.stats": "window_refused",
      "reply.invalid_request": "window_refused"
    },
    "campaign.get": {
      "sequence.not_found": "campaign_not_found"
    },
    "conversation.reply": {
      "inbox.thread_not_found": "conversation_not_found",
      "inbox.channel_mismatch": "channel_mismatch",
      "contact.opted_out": "suppressed",
      "inbox.send_failed": "delivery_refused",
      "reply.refused:inboxThread.invalidInput:RenderingFail": "text_reply_would_render",
      "access.feature_unavailable": "feature_not_included"
    },
    "conversation.classify": {
      "inbox.thread_not_found": "conversation_not_found",
      "inbox.category_not_found": "category_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "conversation.close": {
      "inbox.thread_not_found": "conversation_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "conversation.reopen": {
      "inbox.thread_not_found": "conversation_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "contact.delete": {
      "contact.not_found": "contact_not_found",
      "access.feature_unavailable": "feature_not_included"
    },
    "contact.update": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.invalid_email": "invalid_channel_value",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value",
      "contact.field_not_found": "field_not_found",
      "reply.invalid_request": "value_refused",
      "access.feature_unavailable": "feature_not_included"
    },
    "sender.list": {
      "access.feature_unavailable": "feature_not_included",
      "account.list_incomplete": "list_not_finished",
      "network": "reply_not_reached"
    },
    "schedule.list": {
      "network": "reply_not_reached"
    },
    "events.read": {
      "inbox.cursor_expired": "cursor_not_held",
      "inbox.cursor_refused": "cursor_not_held",
      "cursor:reply.refused:400": "cursor_not_held",
      "usage": "command_not_known"
    },
    "social_invitation.send": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "contact.already_held": "held_unnamed",
      "contact.no_linkedin_profile": "no_profile_at_reply",
      "account.list_incomplete": "list_not_finished",
      "linkedin.account_not_found": "sender_not_found",
      "linkedin.account_inactive": "sender_blocked_channel",
      "linkedin.invalid_cookie": "sender_blocked_channel",
      "linkedin.limit_reached": "sender_blocked_limit",
      "linkedin.message_too_long": "note_too_long",
      "reply.invalid_request:message": "note_too_long",
      "linkedin.missing_variables": "text_would_render",
      "linkedin.feature_disabled": "feature_not_included",
      "access.feature_unavailable": "feature_not_included",
      "idempotency_key.in_use": "key_in_use"
    },
    "message.status": {
      "direct_send.not_found": "send_not_found",
      "direct_send.not_owned": "send_not_found",
      "usage.reference": "send_not_found"
    },
    "message.cancel": {
      "direct_send.not_found": "send_not_found",
      "direct_send.not_owned": "send_not_found",
      "usage.reference": "send_not_found",
      "direct_send.already_sent": "send_already_sent",
      "direct_send.already_failed": "send_already_failed",
      "direct_send.already_sending": "send_already_sent",
      "direct_send.cancel_not_confirmed": "cancel_not_confirmed"
    },
    "message.send": {
      "template:reply.refused:contact.unknown:RenderingFail": "template_not_filled",
      "template:contact.missing_variables": "template_not_filled",
      "template:contact.rendering_failed": "template_not_filled",
      "template:linkedin.missing_variables": "template_not_filled",
      "contact.missing_variables": "text_would_render",
      "contact.rendering_failed": "text_would_render",
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.invalid_email": "invalid_channel_value",
      "contact.invalid_linkedin_url": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value",
      "reply.invalid_request:scheduleId": "schedule_not_taken_on_the_route",
      "schedule.not_found": "schedule_not_held",
      "contact.time_zone_missing": "schedule_needs_the_contact_s_zone",
      "schedule.no_slot": "schedule_has_no_hours",
      "contact.already_held": "held_unnamed",
      "contact.no_linkedin_profile": "no_profile_at_reply",
      "contact.blacklisted": "blacklisted_at_reply",
      "account.list_incomplete": "list_not_finished",
      "email_account.not_found": "sender_not_found",
      "linkedin.account_not_found": "sender_not_found",
      "linkedin.account_inactive": "sender_blocked_channel",
      "linkedin.invalid_cookie": "sender_blocked_channel",
      "linkedin.sales_navigator_required": "sender_unavailable",
      "linkedin.sales_navigator_disconnected": "sender_unavailable",
      "linkedin.voice_profile_missing": "sender_unavailable",
      "linkedin.limit_reached": "sender_blocked_limit",
      "linkedin.not_connected": "not_a_connection",
      "linkedin.no_inmail_credits": "no_inmail_credits",
      "linkedin.message_too_long": "text_too_long",
      "linkedin.voice_too_long": "text_too_long",
      "linkedin.missing_variables": "text_would_render",
      "linkedin.feature_disabled": "feature_not_included",
      "access.feature_unavailable": "feature_not_included",
      "idempotency_key.in_use": "key_in_use"
    }
  }
};

// The table itself. `shared` holds the rows every operation this package performs declares; `per_operation` holds
// the rows one operation declares and another does not — the documents do not publish the same failure codes,
// and a shared row naming a code an operation never declared would be a code the runtime refuses. The one exception
// is `capability_not_offered`, which no document declares and the runtime admits beside every operation. Each row says
// `when` it applies, the `code` and `class` it reports, and the `note` an operator reads, which is also the
// message the failure carries.
export const ROWS = {
  "shared": {
    "program_never_started": {
      "when": "The CLI could not be started at all: exit code -1, and nothing was sent.",
      "code": "provider_call_failed",
      "class": "permanent",
      "note": "The Reply CLI could not be started, so nothing reached the account. Install it, make sure the runtime's grant names it, and reload the plugins."
    },
    "call_refused_before_it_was_sent": {
      "when": "The CLI exited 2 with a usage error of its own, decided before any request was made, and the operation has no row of its own for that error.",
      "code": "provider_call_failed",
      "class": "permanent",
      "note": "The Reply CLI refused the call before making it. Nothing reached the account, and a repeat would build the same call, so this is reported rather than retried."
    },
    "answer_lost_on_a_read": {
      "when": "A read timed out, exited without an error the CLI explained, or printed something that is not a result — or its answer was cut off at `Plugins:Exec:OutputBytes` inside an operation with no too-large row of its own.",
      "code": "provider_unavailable",
      "class": "transient",
      "note": "Reply could not be reached, or did not answer, on a call that changes nothing. Asking again costs nothing, so the attempt is simply repeated."
    },
    "answer_lost_on_a_write": {
      "when": "A write exited 3 (the CLI's `outcome.unknown`: the request may have reached Reply and no definite answer came back), timed out, exited without an error the CLI explained, printed something that is not a result, or had its answer cut off at `Plugins:Exec:OutputBytes`.",
      "code": "provider_answer_lost",
      "class": "ambiguous",
      "note": "A write may already have happened at Reply, and no answer says whether it did. Repeating a write can add a person twice or send to them twice, so the attempt is repeated only where the operation's own contract says a repeat is safe, and otherwise the work item stops for a person."
    },
    "reply_not_reached": {
      "when": "The CLI answered `reply.unavailable`: Reply could not be reached, or a read failed after the CLI's own retries — an exact lookup that ran ten pages without an end among them. The CLI never resends a write, so a write that ends this way was never sent.",
      "code": "provider_unavailable",
      "class": "transient",
      "note": "Reply could not be reached, or did not answer a call that changes nothing, so nothing happened there. The runtime repeats the attempt."
    },
    "reply_busy": {
      "when": "The CLI answered `sequence.busy`: Reply is still activating the sequence.",
      "code": "provider_unavailable",
      "class": "transient",
      "note": "Reply is still activating this sequence, and nothing was changed. The runtime repeats the attempt once Reply has had time to finish."
    },
    "unauthorized": {
      "when": "The CLI refused for want of a working credential (`auth.*`, exit 2), or answered `access.denied`, or `access.feature_unavailable` for an operation that declares no code of its own for a feature the account lacks.",
      "code": "unauthorized",
      "class": "permanent",
      "note": "The profile this route names is not signed in, or its key does not carry the scope this call needs. Sign in with `reply auth login` for that profile — in Windows PowerShell, `reply.cmd auth login`, because its default execution policy refuses npm's `reply.ps1`; PowerShell 7 and Command Prompt take `reply auth login` as written — or issue a key with the scope; no retry can help."
    },
    "rate_limited": {
      "when": "The CLI answered `rate_limited`: Reply asked to wait longer than the CLI waits on its own, and the wait it asked for travels in the trace as `retry_after`.",
      "code": "rate_limited",
      "class": "transient",
      "note": "Reply asked for this call to be made later. The runtime waits and repeats the attempt."
    },
    "refusal_this_version_has_no_word_for": {
      "when": "The CLI answered `reply.refused`, `reply.invalid_request` about a field this operation has no row for, or a code this version does not know.",
      "code": "provider_call_failed",
      "class": "permanent",
      "note": "Reply refused the call in a word this version of the package does not know; the CLI's code and Reply's own are in the attempt's trace. Nothing was done, so the attempt is not repeated — and a refusal that turns up often enough belongs in this table."
    },
    "route_not_offered": {
      "when": "The CLI answered `reply.refused` at status 404 with no code of Reply's own: Reply's API has no route for the call, so the account's Reply does not offer it yet. The CLI passes such a 404 through as it is on every command but `contact get`, `contact delete`, `inbox get`, `inbox reply`, `sequence get`, `sequence contacts`, `sequence stats`, `sequence pause` and `sequence start`, which read every 404 as the thing they asked for not being found, and `auth whoami`, which never answers `reply.refused`.",
      "code": "capability_not_offered",
      "class": "permanent",
      "quotes": "operation",
      "note": "Reply does not offer this operation yet — its API has no route for the call — so nothing was done there. Asking again meets the same answer, so the attempt is not repeated; what to do instead is the agent's to choose."
    },
    "account_or_sender_blocked": {
      "when": "Not reported yet for the account. The CLI publishes no code that says Reply refused a call for the account's money or plan or a revoked key, and did nothing for it; this row is where such a code lands once it does (see the README, \"Endings every operation can meet\"). A sending account Reply refused for its own state is the sender's block, on the two sends' own rows.",
      "code": "provider_blocked",
      "class": "blocked",
      "note": "Reply refused this call for a reason outside the work item and did nothing for it. Clear it at Reply where the block says; the work can be proposed again once it clears."
    }
  },
  "per_operation": {
    "campaign.create": {
      "campaign_already_linked": {
        "when": "The Jason campaign already carries this plugin's campaign pin. Decided before any call.",
        "code": "campaign_already_linked",
        "class": "validation",
        "note": "This Jason campaign is linked to a Reply sequence already, so no other is made: a second would sit beside the first link. Work with the linked sequence, or make the new one for a new Jason campaign."
      },
      "campaign_name_taken": {
        "when": "A first attempt, and Reply's list of sequences, read to its end, holds one whose name equals `args.name`, ignoring case. Nothing is made.",
        "code": "campaign_name_taken",
        "class": "validation",
        "note": "Reply holds a sequence of this name already, so no second one is made, and the one there is not taken over. Choose another name, or link the sequence Reply holds with campaign.get."
      },
      "campaign_name_ambiguous": {
        "when": "An attempt after the first, and Reply's sequences of the name, read to their end, are not exactly one sequence in the `new` state, not archived, created no earlier than the work item less ten minutes, and holding nobody: one is older, has been started or archived, or holds somebody, or there is more than one. Nothing is made.",
        "code": "campaign_name_ambiguous",
        "class": "permanent",
        "note": "An earlier attempt of this work item may have made the sequence, and what Reply holds under the name does not say which sequence that is, so nothing more is made. Look at the sequences of this name at Reply; the attempt's trace says what was found. Link the one that is meant with campaign.get."
      },
      "campaign_not_draft": {
        "when": "Reply answered the create with a sequence whose status is not `new`, or that is archived. Never expected; the sequence's identifier is pinned on the failure.",
        "code": "campaign_not_draft",
        "class": "permanent",
        "note": "Reply made the sequence but answered it as started or archived rather than as a draft. It is linked, so it can be found: look at it at Reply before anything else is done with it."
      },
      "list_not_finished": {
        "when": "The CLI answered `sequence.list_incomplete`: it read twenty thousand sequences whose names contain this one and Reply said there were more. Nothing is made.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more than twenty thousand sequences whose names contain this one, so whether the name is taken could not be decided, and nothing was made. Make the sequence at Reply and link it with campaign.get."
      }
    },
    "campaign.activate": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found`, or an identifier that is not a Reply sequence number, refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "campaign_not_activatable": {
        "when": "The CLI answered `sequence.archived`, or `sequence.not_startable` — no contacts, no connected sending account, no sending hours — and Reply's own code travels in the trace.",
        "code": "campaign_not_activatable",
        "class": "permanent",
        "note": "Reply will not start this sequence, and nothing was started. Either it is archived, or Reply refused the start: no contacts, no connected sending account or no sending hours. The attempt's trace says which, with Reply's own code where Reply gave one; that has to change at Reply before the activation is proposed again."
      },
      "population_changed": {
        "when": "The CLI answered `sequence.contact_count_changed`: the sequence's count at execution is not the `args.expected.enrolled` a person approved, and nothing was started.",
        "code": "population_changed",
        "class": "permanent",
        "note": "The population a person approved is not the one the campaign holds now, so nothing was started. The attempt's trace carries both numbers; read the count again with enrollment.list and propose a new activation for the number it gives."
      },
      "count_not_answered": {
        "when": "The CLI answered `already_active` without a count: the sequence is live, nothing was started, and Reply would not count its people.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "The sequence is already live, but Reply did not say how many people it holds, and the answer needs that number. Nothing was started; the runtime asks again."
      },
      "started_from_a_state_with_no_word": {
        "when": "The CLI answered `started` from a state other than `new` or `paused` — Reply has a rare `skipped` — which `was` has no word for.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply started the sequence, from a state this version has no word for, so the answer cannot say what it left. The sequence is live now; the attempt's trace names Reply's word for the state."
      }
    },
    "campaign.pause": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found`, or an identifier that is not a Reply sequence number, refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "campaign_not_pausable": {
        "when": "The CLI answered `sequence.not_pausable` (Reply refuses to pause a sequence that never started, or refused the pause for another reason) or `sequence.archived`; Reply's own code travels in the trace.",
        "code": "campaign_not_pausable",
        "class": "permanent",
        "note": "Reply will not pause this sequence, and nothing was changed: it is archived, it never started, or Reply refused the pause. The attempt's trace says which, with Reply's own code where Reply gave one; that has to change at Reply before a pause can succeed."
      }
    },
    "campaign.get": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found`, or the identifier is not a Reply sequence number at all, which is refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `sequence get`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same read would be cut at the same place, so it is not repeated; raising `Plugins:Exec:OutputBytes` is what lets it through."
      }
    },
    "enrollment.list": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found` — Reply's count of a sequence's people refuses a sequence this account does not hold — or an identifier that is not a Reply sequence number, refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: Reply says this account's plan or role may not read a sequence's people.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role may not read a sequence's people. Signing in again would not change it; a role or a plan that includes it would."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `sequence contacts`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same page would be cut at the same place, so it is not repeated: ask for less — a smaller `limit` — or raise `Plugins:Exec:OutputBytes`."
      }
    },
    "engagement.summarize": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found`, or an identifier that is not a Reply sequence number, refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: Reply says this account's plan or role may not read a sequence's statistics.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role may not read a sequence's statistics. Signing in again would not change it; a role or a plan that includes it would."
      },
      "window_refused": {
        "when": "The CLI refused the window before sending (`usage.stats`: a span that does not end after it starts), or Reply did not accept it (`reply.invalid_request`). The window is the whole of what this call asked.",
        "code": "window_invalid",
        "class": "validation",
        "note": "The window this work item asked for was refused, and the same window would be refused again; correct `args.window`."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `sequence stats`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same read would be cut at the same place, so it is not repeated; raising `Plugins:Exec:OutputBytes` is what lets it through."
      }
    },
    "conversation.list": {
      "contact_not_linked": {
        "when": "The work item names a person and no Reply contact identifier is pinned for them. Decided before any call: a read creates nobody.",
        "code": "contact_not_linked",
        "class": "permanent",
        "note": "Reply has never been told about this person through this installation, so there is no thread to read by. A write that ensures the contact — list_membership.add or campaign.enroll — is what links them."
      },
      "identifier_names_nothing": {
        "when": "The pinned contact identifier, the campaign's, or the category's the call filters by, is not a Reply number at all. Decided before any call.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The identifier this read would filter by names nothing a Reply account could hold, so nothing was asked. The pin, or the category the work item names, is wrong; the attempt's trace says which one."
      },
      "call_could_not_be_built": {
        "when": "`source` is `all` and `offset` plus `limit` passes 1000. Every thread is Reply's inbox and its sent threads, each read from its first thread a hundred at a time and merged, as far as 1000. Decided before any call.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A page that far into every thread is not merged from the two buckets, so it is refused rather than guessed at. Ask for `source: inbound`, which Reply pages without that bound, or for a page within the first 1000 threads."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `inbox list`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same page would be cut at the same place, so it is not repeated: ask for less — a smaller `limit` — or raise `Plugins:Exec:OutputBytes`."
      }
    },
    "conversation.get": {
      "conversation_not_found": {
        "when": "The CLI answered `inbox.thread_not_found`, or an identifier that is not a Reply thread number, refused without a call.",
        "code": "conversation_not_found",
        "class": "permanent",
        "note": "This account holds no thread under the identifier the work item carries, or none this account may see. No later attempt will find it."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `inbox get`: the page of messages passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same page would be cut at the same place, so it is not repeated: ask for less — a smaller `limit` — or raise `Plugins:Exec:OutputBytes`."
      }
    },
    "list_membership.add": {
      "list_not_found": {
        "when": "The CLI answered `contact-list.not_found`, or `args.list.external_id` is not a Reply list number, refused without a call.",
        "code": "list_not_found",
        "class": "permanent",
        "note": "This account holds no contact list under `args.list.external_id`. Correct the identifier the work item carries; nothing about repeating the call can change it."
      },
      "contact_not_found": {
        "when": "The CLI answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before this person can be worked again."
      },
      "suppressed": {
        "when": "The CLI answered `contact.opted_out`: Reply's own opt-out register holds this person, and this package always asks the CLI to refuse such a person (`--refuse-opted-out`).",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply's own opt-out register holds this person, so they are not added to an outreach list. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "invalid_channel_value": {
        "when": "The CLI answered `contact.invalid_email` or `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "Reply will not accept the address this person carries on the channel the call named. The contact's data has to be corrected; no later attempt would be accepted either."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact. A list's own capacity, unlike the account's, publishes no code of its own — a refusal there arrives as an ordinary refusal and is reported as a refusal this version has no word for, rather than guessed into this row."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nothing was added. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the exact lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nothing was added. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was added. A person resolves the duplicate at Reply, then proposes the work again."
      }
    },
    "campaign.enroll": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found`, or the identifier is not a Reply sequence number at all, which is refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "contact_not_found": {
        "when": "The CLI answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before this person can be worked again."
      },
      "campaign_not_enrollable": {
        "when": "The CLI answered `sequence.archived` or `sequence.has_no_steps`.",
        "code": "campaign_not_enrollable",
        "class": "permanent",
        "note": "The sequence will not take an enrollment: it is archived, or it holds no step to send. Un-archiving it at Reply, or giving it a step, is what makes the work item runnable — so it fails rather than waiting."
      },
      "collision_refused": {
        "when": "`args.collision` is `refuse`, this is a first attempt, and the CLI answered `already_in_sequence`: the person already takes part. Under `skip`, and on any repeated attempt, the same answer is `already_enrolled`.",
        "code": "collision_refused",
        "class": "permanent",
        "note": "This person already takes part in the sequence and the call asked to be refused rather than to decide. Choose another collision policy if a second participation is what was meant."
      },
      "suppressed": {
        "when": "The CLI answered `contact.opted_out`: Reply refused to add a person its opt-out register holds.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply's own opt-out register holds this person, so they are not enrolled. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "invalid_channel_value": {
        "when": "The CLI answered `contact.invalid_email` or `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "Reply will not accept the address this person carries on the channel the call named. The contact's data has to be corrected; no later attempt would be accepted either."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact or enrollment. The sequence's own capacity, unlike the account's, publishes no code of its own — a refusal there arrives as an ordinary refusal and is reported as a refusal this version has no word for, rather than guessed into this row."
      },
      "call_could_not_be_built": {
        "when": "The CLI answered `sequence.step_not_found` — the start position names no single step of this sequence's chain — or `usage.range` for a step number past the 10000 it takes; or `args.first_touch` is a value this version has no word for, refused without a call.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The enrollment could not be built from the arguments it was given, so nothing was sent. A Reply step carries no ordering field at all — the steps are a graph of parents with branch labels — so a numbered start position has a sound meaning only while they form a single chain, and the attempt's trace says which reading failed. Ask for the first step, or name a position the sequence really has."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nobody was enrolled. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the exact lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nobody was enrolled. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nobody was enrolled. A person resolves the duplicate at Reply, then proposes the work again."
      }
    },
    "enrollment.stop": {
      "campaign_not_found": {
        "when": "The CLI answered `sequence.not_found` — on the read this package makes of a campaign it names for the first time, or when a person the CLI found takes no part in a sequence Reply does not hold — or an identifier that is not a Reply sequence number, refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "contact_not_found": {
        "when": "A pin that is not a Reply contact number at all, refused without a call. A well-formed pin Reply no longer holds takes part in nothing, so it reads `already_stopped` instead.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person names nobody a Reply account could hold, so there is nobody to take out by it. The pin is wrong and has to be cleared."
      },
      "contact_not_unique": {
        "when": "The CLI answered `contact.not_unique` to the exact lookup this package makes for a person with no pin: more than one contact is held under the address.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nobody was taken out. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by. Decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no value on the channel the call named, so there is no address to find them by. Name a channel they have."
      }
    },
    "conversation.reply": {
      "conversation_not_found": {
        "when": "The CLI answered `inbox.thread_not_found` to the read of the thread or to the reply, or the identifier is not a Reply thread number, refused without a call.",
        "code": "conversation_not_found",
        "class": "permanent",
        "note": "This account holds no thread under the identifier the work item carries, or none this account may see, so nothing was sent. Reply by an identifier conversation.list returned."
      },
      "channel_mismatch": {
        "when": "The thread, read before anything is sent, is on another channel than the one `args.channel` names, or on one Reply sends no reply on; or the CLI answered `inbox.channel_mismatch`.",
        "code": "channel_mismatch",
        "class": "validation",
        "note": "The thread is not on the channel this work item names, so nothing was sent. Name the channel the thread is on — conversation.list and conversation.get say which — and propose the reply again."
      },
      "thread_contact_mismatch": {
        "when": "The thread, read before anything is sent, is not this person's: its person is not the contact pinned for them, its address on the channel `args.channel` names is not that channel's value, or it names nobody Reply still holds.",
        "code": "thread_contact_mismatch",
        "class": "validation",
        "note": "The thread's person is not the person this work item is about, so nothing was sent and nobody was pinned. Reply in a thread of this person's, or propose the reply for the person the thread is with."
      },
      "placeholder_in_the_text": {
        "when": "The text carries a double opening brace, which Reply may read as a placeholder of its own and fill in. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The text carries a double opening brace, which Reply may read as a placeholder it fills in for each person, so what would go out might not be the text a person approved. Nothing was sent. Write the text without one and propose the reply again."
      },
      "text_reply_would_render": {
        "when": "The CLI answered `reply.refused` with Reply's code `inboxThread.invalidInput` and a sentence whose first word is `RenderingFail`: Reply read part of the text as a placeholder of its own, could not fill it in, and refused the send.",
        "code": "message_invalid",
        "class": "validation",
        "note": "Reply read part of the text as a placeholder it fills in for each person and could not fill it in, so it refused the reply and nothing was sent: what would have gone out was not the text a person approved. Write the text without anything Reply reads as a placeholder and propose the reply again."
      },
      "suppressed": {
        "when": "The CLI answered `contact.opted_out`: Reply's own opt-out register holds the thread's person.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply's own opt-out register holds this person, so nothing was sent. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "delivery_refused": {
        "when": "The CLI answered `inbox.send_failed`: Reply took the reply and could not deliver it, and its own reason for the failed message travels in the trace as `provider_status_code`, null where Reply recorded none.",
        "code": "delivery_refused",
        "class": "permanent",
        "note": "Reply took the reply and could not deliver it, so it did not go out. Reply's own reason is in the attempt's trace: a sending limit waits for its window, a disconnected account has to be connected again at Reply. Then propose the reply again."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      }
    },
    "conversation.classify": {
      "conversation_not_found": {
        "when": "The CLI answered `inbox.thread_not_found` — which Reply also answers for a category it listed and no longer holds when the filing reaches it — or the identifier is not a Reply thread number, refused without a call.",
        "code": "conversation_not_found",
        "class": "permanent",
        "note": "This account holds no thread under the identifier the work item carries, or none this account may see, so nothing was filed."
      },
      "category_not_found": {
        "when": "Reply's list of the account's categories holds none under the identifier or the name the call carried. Nothing is filed. The CLI's `inbox.category_not_found` is mapped here as well, though a live account never answers a filing with it: a category missing at the filing comes back as a missing thread.",
        "code": "category_not_found",
        "class": "permanent",
        "note": "This account holds no inbox category under the identifier or the name the work item carries, so nothing was filed. Name one of the account's categories — conversation.list and conversation.get report the ones threads are under."
      },
      "category_not_unique": {
        "when": "More than one of the account's categories carries the name the call carried, compared exactly and without regard to case. Nothing is filed.",
        "code": "category_not_unique",
        "class": "permanent",
        "note": "More than one of this account's inbox categories carries this name, so picking one would be guessing and nothing was filed. Name the category by its identifier instead."
      },
      "category_that_opts_the_person_out": {
        "when": "The category the lookup found is Reply's reserved Do not contact — reserved unless Reply lists it as not reserved, and named so whatever its case — however the call named it, by identifier or by name. Decided after the lookup and before any filing.",
        "code": "category_opts_out",
        "class": "validation",
        "note": "Filing a thread under Reply's Do not contact opts the person out at Reply in every sequence, ends their part in each and cancels the emails planned for them, and taking the thread out of the category again undoes none of it, so nothing was filed. Stopping a person is optout.record's path: record the stop with it, and with `jason suppression add` for Jason's own register."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      }
    },
    "conversation.close": {
      "conversation_not_found": {
        "when": "The CLI answered `inbox.thread_not_found`, or the identifier is not a Reply thread number, refused without a call.",
        "code": "conversation_not_found",
        "class": "permanent",
        "note": "This account holds no thread under the identifier the work item carries, or none this account may see, so nothing was changed."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      }
    },
    "conversation.reopen": {
      "conversation_not_found": {
        "when": "The CLI answered `inbox.thread_not_found`, or the identifier is not a Reply thread number, refused without a call.",
        "code": "conversation_not_found",
        "class": "permanent",
        "note": "This account holds no thread under the identifier the work item carries, or none this account may see, so nothing was changed."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: the inbox is not available to this user or team.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the inbox. Signing in again would not change it; a plan or a role that includes it would."
      }
    },
    "optout.record": {
      "contact_not_found": {
        "when": "The CLI answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before the opt-out can be recorded there."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so no contact was made and no flag set. Reply holds nobody under this address, so it cannot reach them there either. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach."
      },
      "contact_not_unique": {
        "when": "The CLI answered `contact.not_unique`, to the exact lookup this package makes for a person with no pin or to the opt-out itself: more than one contact is held under the address.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was done. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "invalid_channel_value": {
        "when": "The CLI answered `contact.invalid_email` or `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "Reply will not accept the address this person carries on the channel the call named, so no contact could be made to record the opt-out on. The contact's data has to be corrected; no later attempt would be accepted either."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`: Reply held nobody under the address, and the account will hold no further contact.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact, so the person who said stop could not be made there. Reply never held them under this address, so it cannot reach them there either."
      }
    },
    "contact.delete": {
      "contact_not_linked": {
        "when": "The work item names a person and no Reply contact identifier is pinned for them. Decided before any call: a delete works by the pin alone.",
        "code": "contact_not_linked",
        "class": "permanent",
        "note": "Reply has never been told about this person through this installation, so there is no contact this package could be sure is theirs, and a delete is never aimed by an address. Nothing was deleted. Look the person up at Reply, and delete the contact there if that is what is meant."
      },
      "contact_not_found": {
        "when": "A first attempt, and `reply contact get` answered `contact.not_found` for the pin; or the pin is not a Reply contact number, refused without a call. On an attempt after the first, Reply holding nothing under the pin is the delete already done, and answers `already_deleted`.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person names no contact this account holds, so nothing was deleted. It may have been deleted at Reply already, or the pin may belong to another account; look at Reply before anything else is done with this person."
      },
      "contact_opted_out": {
        "when": "`reply contact get` answered the contact with `isOptedOut: true`. Nothing is deleted.",
        "code": "contact_opted_out",
        "class": "permanent",
        "note": "Reply holds this person as opted out and keeps that stop on the contact itself, so deleting the contact would erase it, and the address imported again later would be reachable. Nothing was deleted. Leave the contact at Reply; Jason's own register keeps its stop either way."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: Reply says this account's plan or role may not read or delete a contact.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role may not read or delete a contact. Signing in again would not change it; a role or a plan that includes it would."
      }
    },
    "contact.update": {
      "contact_not_found": {
        "when": "The CLI answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account — so nothing was changed. The pin has to be cleared before this person can be worked again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was changed. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call; or the CLI answered `contact.invalid_email` or `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email field, to the import of a person Reply holds nobody for.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no value on the channel the call named that Reply could find or create them by, so nothing was changed. Name a channel they have, an email or a LinkedIn profile, or correct the value Jason holds."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`: Reply held nobody under the value, and the account will hold no further contact.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact, and Reply held nobody for this person, so the contact could not be made and nothing was changed. Make room at Reply, then propose the work again."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nothing was changed. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the exact lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nothing was changed. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "field_not_found": {
        "when": "The CLI answered `contact.field_not_found`: a custom field the arguments name is one the account has none of, whatever its case. Nothing is changed.",
        "code": "field_not_found",
        "class": "validation",
        "note": "This account has no contact custom field under a name the work item gives, so nothing was changed. Name each custom field as the account names it, and propose the update again."
      },
      "value_refused": {
        "when": "The CLI answered `reply.invalid_request`: Reply did not accept a value the arguments give a field, and Reply's names for the fields it refused travel in the trace.",
        "code": "invalid_field_value",
        "class": "validation",
        "note": "Reply did not accept a value this work item gives a field, so nothing was changed; the attempt's trace names the fields. Correct the value and propose the update again."
      },
      "custom_field_named_twice": {
        "when": "The arguments name one custom field twice: the same name, once trimmed, whatever its case. Decided before any call.",
        "code": "invalid_field_value",
        "class": "validation",
        "note": "The work item names one custom field twice, so which value is meant cannot be told and nothing was asked. Name each custom field once and propose the update again."
      },
      "contact_gone": {
        "when": "The CLI answered `contact.not_found` for a contact this attempt was not pinned to — one the exact lookup found, or one the update's own import made or matched — so Reply named the contact and then held nothing under it. Nobody is pinned.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply named a contact for this person and then answered that it holds no such contact, so nothing was changed and nothing was pinned. Look the person up at Reply, then propose the update again."
      },
      "person_not_projected": {
        "when": "The input carries no `first_name`, `last_name`, `company`, `title` or `time_zone` for the person at all — absent, not null — which only a runtime older than 0.14.0, whose documents projected the person's identifier and channel alone, composes, so it is reported in a code that runtime's documents already declare. Decided before any call; the cause and the fields it lacks travel in the trace.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "This installation's Jason handed the package less of the person than the operation's document projects, so a person Reply holds nobody for could not be created under the name Jason holds, and nothing was called. Update Jason to 0.22.0 or newer; the attempt's trace names the fields."
      },
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable`: Reply says this account's plan or role may not change a contact.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role may not change a contact. Signing in again would not change it; a role or a plan that includes it would."
      }
    },
    "sender.list": {
      "feature_not_included": {
        "when": "The CLI answered `access.feature_unavailable` for the one channel `sender_channel` names: the account's plan or role does not include its mailboxes or its LinkedIn accounts. Without `sender_channel` the same answer says the channel is `not_included`, and the others are still listed.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include the accounts of the channel named, so there is none a send could go from there. Signing in again would not change it; a plan or a role that includes the channel would."
      },
      "list_not_finished": {
        "when": "The CLI answered `account.list_incomplete`: Reply's list of mailboxes did not end within twenty thousand.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply's list of mailboxes did not end within twenty thousand, so the accounts could not all be listed and none were. Name the sender from Reply's own list of accounts instead."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `account list`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same read would be cut at the same place, so it is not repeated: name one channel with `sender_channel`, or raise `Plugins:Exec:OutputBytes`."
      }
    },
    "schedule.list": {
      "feature_not_included": {
        "when": "Not reported by Reply. Reply refuses this read only for a key without its sequences read scope, which the CLI answers `access.feature_unavailable` and this package reports `unauthorized`; the code stays declared for a provider whose plan or role limits who may read its schedules.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "The provider's plan or the caller's role does not include its schedules, so the read lists nothing."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `schedule list`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. Reply gives every schedule in one answer, so the same read would be cut at the same place and it is not repeated: raise `Plugins:Exec:OutputBytes`."
      }
    },
    "social_invitation.send": {
      "contact_not_found": {
        "when": "`reply contact get` or the invitation answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account — so nothing was sent. The pin has to be cleared before this person can be worked again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the profile answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this profile, so picking one would be guessing and nothing was sent. A person resolves the duplicate at Reply, then proposes the invitation again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call; or the CLI answered `contact.invalid_linkedin_url` to the import of a person Reply holds nobody for.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no LinkedIn profile on the channel the call named that Reply could find or create them by, so there is nobody to invite. Name the channel they have a profile on, or correct the profile Jason holds."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`: Reply held nobody under the value, and the account will hold no further contact.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact, and Reply held nobody for this person, so the contact could not be made and nothing was sent. Make room at Reply, then propose the work again."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nothing was sent. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the exact lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nothing was sent. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "placeholder_in_the_text": {
        "when": "The note carries a double opening brace, which Reply reads as the start of a placeholder of its own, or `@prompt[[`, which Reply reads as the start of a prompt to a model. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The note carries a double opening brace, which Reply reads as a placeholder it fills in for each person, or `@prompt[[`, which Reply reads as a prompt to a model whose answer it sends, so what would go out might not be the note a person approved. Nothing was sent. Write the note without either and propose the invitation again."
      },
      "text_would_render": {
        "when": "The CLI answered `linkedin.missing_variables`: Reply read part of the note as a placeholder of its own and could not fill it in.",
        "code": "message_invalid",
        "class": "validation",
        "note": "Reply read part of the note as a placeholder it fills in for each person and could not fill it in, so nothing was sent: what would have gone out was not the note a person approved. Write the note without anything Reply reads as a placeholder and propose the invitation again."
      },
      "note_too_long": {
        "when": "The CLI answered `linkedin.message_too_long`, or `reply.invalid_request` naming the `message` field: Reply refuses notes over 200 characters. The document holds a note to 200, so this is reached only if Reply's own bound moves.",
        "code": "message_too_long",
        "class": "validation",
        "note": "Reply found the note too long and sent nothing. Reply refuses notes over 200 characters; write a shorter note and propose the invitation again."
      },
      "sender_not_found": {
        "when": "Reply's list of LinkedIn accounts holds none under the sender's identifier, the identifier is not a Reply number, refused without a call, or the CLI answered `linkedin.account_not_found`.",
        "code": "sender_not_found",
        "class": "permanent",
        "note": "Reply has no LinkedIn account under this identifier that this account can send from, so nothing was sent. Name an account sender.list returns."
      },
      "sender_mismatch": {
        "when": "Reply's list holds the sender's account under another address than the arguments state, or under one where they state none, or under none where they state one. Decided before the invitation.",
        "code": "sender_mismatch",
        "class": "validation",
        "note": "The account the arguments name is not held by Reply under the address they state, so the person approving read another account than the one the invitation would go from, and nothing was sent. Name the account by the identifier and the address sender.list returns."
      },
      "sender_blocked_channel": {
        "when": "Reply's list holds the sender's account as `inactive` or `disconnected`, or the CLI answered `linkedin.account_inactive` or `linkedin.invalid_cookie`; Reply's own word travels in the trace.",
        "code": "provider_blocked",
        "class": "blocked",
        "block": { "scope": "sender", "reason": "channel" },
        "unnamed_sender": "sender_unavailable",
        "note": "The LinkedIn account cannot send now — it is turned off, or its session at LinkedIn has to be connected again at Reply — so nothing was sent. Put the account right at Reply; the invitation waits on the sender's block and goes once a trial gets past it."
      },
      "sender_blocked_limit": {
        "when": "The CLI answered `linkedin.limit_reached`, its word for Reply's 429 under `directLinkedIn.dailyLimitReached` or `directLinkedIn.weeklyLimitReached`, naming the send in the state `failed` or not: the LinkedIn account's daily or weekly allowance is spent, which one under `limit` in the trace. Where the CLI said how long to wait (`retry_after`), the block clears that long after the answer.",
        "code": "provider_blocked",
        "class": "blocked",
        "block": { "scope": "sender", "reason": "limit" },
        "unnamed_sender": "sender_allowance_spent",
        "note": "The LinkedIn account has spent its daily or weekly allowance — the attempt's trace says which — so nothing was sent. The invitation waits on the sender's block and goes once the allowance renews and a trial gets past it."
      },
      "sender_unavailable": {
        "when": "Since 0.7.0, only where a command that ran from no sending account, such as a read on the way to the invitation, answered `linkedin.account_inactive` or `linkedin.invalid_cookie`: a block names the account it holds, and there is none to name. Every other such refusal is the sender's block (`sender_blocked_channel`).",
        "code": "sender_unavailable",
        "class": "permanent",
        "note": "The LinkedIn account cannot send now — it is turned off, or its session at LinkedIn has to be connected again at Reply — so nothing was sent. Put the account right at Reply, then propose the invitation again."
      },
      "sender_allowance_spent": {
        "when": "Since 0.7.0, only where a command that ran from no sending account, such as a read on the way to the send, answered `linkedin.limit_reached`: a block names the account it holds, and there is none to name. Every other such refusal is the sender's block (`sender_blocked_limit`).",
        "code": "sender_limit_reached",
        "class": "permanent",
        "note": "The LinkedIn account has spent its daily or weekly allowance — the attempt's trace says which — so nothing was sent. Propose the invitation again once the allowance renews."
      },
      "opted_out_at_reply": {
        "when": "`reply contact get` answered the contact with `isOptedOut: true`. Reply does not refuse an invitation to such a person by itself, so this read is the stop. Nothing is sent.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply holds this person as opted out, so nothing was sent. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "channel_value_conflict": {
        "when": "`reply contact get` answered the contact with another LinkedIn profile than the one the work item consumes, the one Reply holds keyed as the runtime keys a profile and the work item's compared as it stands; or with none — or Reply holds nobody for the person — where the consumed one would be held at Reply under another spelling than the register was asked about. Nothing is changed or imported, and nothing is sent.",
        "code": "contact_channel_conflict",
        "class": "permanent",
        "note": "Reply's contact for this person carries another LinkedIn profile than the one this work item consumes, and this package never changes one, so nothing was sent. Look at the contact at Reply: the profile there, or the one Jason holds, is wrong."
      },
      "no_profile_at_reply": {
        "when": "The CLI answered `contact.no_linkedin_profile`: Reply holds no LinkedIn profile for the contact, though this package gave it the consumed one first.",
        "code": "contact_channel_conflict",
        "class": "permanent",
        "note": "Reply says its contact for this person carries no LinkedIn profile, though the one this work item consumes was written onto it first, so nothing was sent. Look at the contact at Reply."
      },
      "held_unnamed": {
        "when": "The CLI answered `contact.already_held` — Reply already held a contact under the person's fields, so the command sent nothing — without naming that contact in its identifiers. Never expected: the CLI names it, and the package then reads that contact and invites it by its identifier.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply already held a contact for this person and the CLI did not say which, so it could not be read first and nothing was sent. Look the person up at Reply, then propose the invitation again."
      },
      "contact_gone": {
        "when": "The CLI answered `contact.not_found` for a contact this attempt was not pinned to — one the exact lookup found, one the invitation's own import made, or one it named as already held — so Reply named the contact and then held nothing under it. Nobody is pinned.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply named a contact for this person and then answered that it holds no such contact, so nothing was sent and nothing was pinned. Look the person up at Reply, then propose the invitation again."
      },
      "person_not_projected": {
        "when": "The input carries no `first_name`, `last_name`, `company`, `title` or `time_zone` for the person at all — absent, not null — which only a runtime older than 0.14.0, whose documents projected the person's identifier and channel alone, composes, so it is reported in a code that runtime's documents already declare. Decided before any call; the cause and the fields it lacks travel in the trace.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "This installation's Jason handed the package less of the person than the operation's document projects, so a person Reply holds nobody for could not be created under the name Jason holds, and nothing was called. Update Jason to 0.22.0 or newer; the attempt's trace names the fields."
      },
      "patch_answer_lost": {
        "when": "Writing the consumed profile onto a contact that held none timed out, exited 3 or answered something that is not a result. Nothing was sent, and the next attempt reads the contact again first.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "Reply did not say whether the person's LinkedIn profile was written onto its contact, and nothing was sent. The runtime repeats the attempt, which reads the contact again before anything is sent."
      },
      "list_not_finished": {
        "when": "The CLI answered `account.list_incomplete` to the read of the account's LinkedIn accounts. Nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply's list of accounts did not end, so the sender could not be found in it and nothing was sent."
      },
      "key_in_use": {
        "when": "The CLI answered `idempotency_key.in_use`: Reply is still finishing the send an earlier attempt of this work item made under the same key, and this one sent nothing.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "Reply says the first attempt under this key is still being finished, so this one sent nothing. The runtime repeats the attempt, which Reply then answers with the send the first one made, or lets go through where that one failed."
      },
      "linkedin_send_failed": {
        "when": "The CLI answered Reply's own `directLinkedIn.unknown` at status 400 — reply-cli from 0.17.1 as `outcome.unknown`, 0.17.0 as `reply.refused` — in any shape: naming the invitation by its reference or not, in the state `failed` or another. It is a failure Reply answered, and Reply releases the key of a send it answered so, so a repeat under the key would be a new send. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply answered the invitation with a failure on LinkedIn: it lost track of it, or says it did not go. It is not repeated, since a repeat could send it twice. Whether it went is a person's to find out at Reply, and whether to propose it again a person's to decide."
      },
      "send_answered_with_a_failure": {
        "when": "Reply answered the send with a failure at a status of 400 to 499, other than a 409 or a 429, that the CLI passes on as a lost answer (`outcome.unknown`, exit 3): Reply releases the key of a send it answered so, so a repeat under the key would be a new send. Only an ending with no answer of Reply's, none at all or one at 500 or above, is the lost answer. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply answered the invitation with a failure, and keeps no key for a send it answered so, so a repeat could send it twice; it is not repeated. Whether it went is a person's to find out at Reply, and whether to propose it again a person's to decide."
      },
      "send_failed_at_reply": {
        "when": "The send's answer, what Reply holds under the key for another request (`idempotency_key.reused`), or Reply's record of the send an earlier attempt returned (`context.unsettled_reference`, read by `reply direct-send get`) gives the invitation's state as `failed`: Reply took it and says it did not go. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply took the invitation and says it did not go. Whether to propose it again is a person's to decide."
      },
      "send_cancelled_at_reply": {
        "when": "The send's answer, what Reply holds under the key for another request (`idempotency_key.reused`), or Reply's record of the send an earlier attempt returned (`context.unsettled_reference`, read by `reply direct-send get`) gives the invitation's state as `cancelled`: it was cancelled at Reply before it went. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The invitation was cancelled at Reply before it went, so nothing was sent. Whether to propose it again is a person's to decide."
      },
      "send_after_not_offered": {
        "when": "The arguments name a moment still ahead in `send_after`, and `reply direct-send get` answered that Reply's API has no route for it: the account's Reply does not keep a direct send for a later moment. Decided before the send; nothing is sent.",
        "code": "capability_not_offered",
        "class": "permanent",
        "quotes": "operation",
        "note": "The account's Reply does not keep a direct send for a later moment, so the invitation that names one in `send_after` was not sent: it goes at that moment or not at all. Asking again meets the same answer; what to do instead is the agent's to choose."
      },
      "send_after_unreadable": {
        "when": "The arguments name a moment in `send_after` that this package's `Date.parse` cannot read — the claim refuses any form but the one the document names, so this is a moment that form admits and the runtime does not read, or one given past the claim. It is never read as a moment already past. Decided before anything is read; nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The moment in `send_after` is not one this package can read, so the invitation was not sent: it goes at the moment a person approved or not at all. Asking again meets the same answer; the moment is the agent's to write again."
      },
      "send_not_settled": {
        "when": "A repeat after an attempt that may have sent — `context.unsettled_since` names when the earliest one was claimed — where `reply direct-send get` answered that Reply's API has no route for it: the account's Reply keeps no key on a direct send, so the repeat could send the person a second invitation. Decided before the send; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "The account's Reply keeps no key on a direct send, so repeating this invitation could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "send_not_settled_after_the_window": {
        "when": "A repeat 23 hours or more after `context.unsettled_since`, the moment the earliest attempt that may have sent was claimed: Reply keeps a direct send's key for a day, so the key may no longer settle the repeat. Where the runtime names the send such an attempt returned (`context.unsettled_reference`), Reply's record of it was read first and said nothing of it. Decided before the send; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "An earlier attempt of this invitation may have sent it 23 hours or more ago, longer than Reply surely keeps the key it went under, so repeating it could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "send_not_settled_not_owned": {
        "when": "A repeat told the send an earlier attempt returned (`context.unsettled_reference`), and `reply direct-send get` of it answered `direct_send.not_owned`: Reply holds that send for an account this one is not, so what Reply keeps under the key there settles nothing from here. Decided before the send, inside the window and past it; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "An earlier attempt of this invitation may have sent it from an account of Reply's this one is not, so repeating it here could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "feature_not_included": {
        "when": "The CLI answered `linkedin.feature_disabled` — Reply's code travels in the trace — or `access.feature_unavailable`: the account's plan or role does not include LinkedIn sends outside a sequence, or its LinkedIn accounts.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include LinkedIn invitations outside a sequence, so nothing was sent. Signing in again would not change it; a plan or a role that includes them would."
      }
    },
    "message.send": {
      "contact_not_found": {
        "when": "`reply contact get` or the send answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account — so nothing was sent. The pin has to be cleared before this person can be worked again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address or the profile answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address or profile, so picking one would be guessing and nothing was sent. A person resolves the duplicate at Reply, then proposes the message again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by, decided before any call; or the CLI answered `contact.invalid_email` or `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email field, to the import of a person Reply holds nobody for.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no address or LinkedIn profile on the channel the call named that Reply could find or create them by, so there is nobody to send to. Name the channel they have, or correct the value Jason holds."
      },
      "limit_reached": {
        "when": "The CLI answered `account.contact_limit_reached`: Reply held nobody under the value, and the account will hold no further contact.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact, and Reply held nobody for this person, so the contact could not be made and nothing was sent. Make room at Reply, then propose the work again."
      },
      "first_name_not_derivable": {
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the exact lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nothing was sent. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the exact lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nothing was sent. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "placeholder_in_the_text": {
        "when": "The subject or the text of a final message carries a double opening brace, which Reply reads as the start of a placeholder of its own, or `@prompt[[`, which Reply reads as the start of a prompt to a model. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The subject or the text carries a double opening brace, which Reply reads as a placeholder it fills in for each person, or `@prompt[[`, which Reply reads as a prompt to a model whose answer it sends, so what would go out might not be the message a person approved. Nothing was sent. Write it without either and propose the message again."
      },
      "tag_in_the_text": {
        "when": "An email's subject or body, written as final text, holds a `<` an HTML reader takes as the start of a tag: one followed by an ASCII letter, `/`, `!` or `?`. Reply decodes the body before it reads it, and drops a tag it does not know with everything after it, in the body and in the subject alike, so what went out would stop there; the subject goes as written, and a character reference in it is not read. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Write it without a `<` right before a letter, `/`, `!`, `?` or a value, an address without its angle brackets, and propose the message again."
      },
      "template_refused": {
        "when": "The message is composed as a template, and its subject or its body writes a construct the template standard refuses: the package reads a template by the standard, behind the document. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Write the template as the template standard does, and propose the message again."
      },
      "template_names_a_key_reply_keeps": {
        "when": "The message is composed as a template that names a custom field under a key Reply keeps for a value of its own: one of the contact's fields or engagement counts, the root of an account's person or mailbox, a date or a flag Reply computes, one of its helpers, or a word its template engine reads. Reply would write its own value instead of the field. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Keep the field at Reply under another key, name that key in the template, and propose the message again."
      },
      "template_spells_a_prompt": {
        "when": "The message is composed as a template whose text and fallbacks, put together along one way through its conditions, spell `@prompt[[` once Reply fills it in, which Reply reads as a prompt to a model whose answer nobody approved. A value is not the package's to read, so a value between two parts breaks the spelling. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Write the template so that its text and its fallbacks spell no prompt, and propose the message again."
      },
      "template_tag_in_the_text": {
        "when": "The message is composed as a template whose subject or body holds a `<` an HTML reader takes as the start of a tag, in its text, a fallback's or a comparison's: one followed by an ASCII letter, `/`, `!` or `?`, or one right before a value Reply fills in, which may start with a letter. Reply decodes the body before it reads it, and drops a tag it does not know with everything after it, in the body and in the subject alike, so what went out would stop there. A template's text, a fallback's text and the text a comparison compares are held alike, the last because whether Reply sanitizes before or after it fills a template in has not been seen. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Write the template without a `<` right before a letter, `/`, `!`, `?` or a value, an address without its angle brackets, and propose the message again."
      },
      "template_compares_what_reply_cannot_read": {
        "when": "The message is composed as a template whose comparison compares a text holding a character Reply's comparison does not read: Reply reads letters, the marks that combine with them, digits, `_` and the other joining punctuation, white space and the punctuation $@\"`~!#%^&*()+,-./:;<= there, and leaves any other tag to its template engine, which reads it otherwise. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Write the text the comparison compares without that character, or send the message as final text, and propose it again."
      },
      "template_names_a_field_reply_lacks": {
        "when": "The message is composed as a template on LinkedIn that names `sender.last_name`, or names `sender.address` with no fallback where the arguments hold no address: Reply has no variable for either on LinkedIn, and the package writes in its place only the address the arguments hold, so there is nothing to write. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "quotes": "reason",
        "note": "Nothing was sent. Name the sender by `sender.first_name` or `sender.name`, give `sender.address` a fallback, or write the name out, and propose the message again."
      },
      "template_on_a_voice_script": {
        "when": "The message is composed as a template in a voice message: Reply speaks a voice message from a final script alone. The document refuses it first; this is the package's own check behind it. Decided before any call.",
        "code": "template_unsupported",
        "class": "validation",
        "note": "A voice message is spoken from a final script, so nothing was sent. Write the script as final text, and propose it again."
      },
      "template_not_filled": {
        "when": "The message is composed as a template, and Reply refused the send because it could not fill the template in for this person: reply-cli's `contact.missing_variables`, with the names Reply could not fill where it lists them, or `contact.rendering_failed` on an email, `linkedin.missing_variables` on LinkedIn, or `reply.refused` with Reply's code `contact.unknown` and a sentence that starts `RenderingFail`. Reply fills a template in before it sends a message that goes now, so nothing was sent.",
        "code": "template_unrendered",
        "class": "validation",
        "quotes": "reply",
        "note": "Reply could not fill in the template for this person and refused it before anything was sent. Give the person a value for each name the template uses, or a fallback beside it, and propose the message again."
      },
      "format_of_another_channel": {
        "when": "The message's format belongs to another channel than the one the call named — an email on LinkedIn, or a LinkedIn format on the email channel. The document refuses it first; this is the package's own check behind it. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The message's format belongs to another channel than the one the call named, so it could not go as a person approved it. Nothing was sent. Name the format of the channel, or the channel of the format, and propose the message again."
      },
      "text_would_render": {
        "when": "The CLI answered `linkedin.missing_variables`, or on an email `contact.missing_variables` or `contact.rendering_failed`, to a message written as final text: Reply read part of the text as a placeholder of its own and could not fill it in.",
        "code": "message_invalid",
        "class": "validation",
        "note": "Reply read part of the text as a placeholder it fills in for each person and could not fill it in, so nothing was sent: what would have gone out was not the message a person approved. Write it without anything Reply reads as a placeholder and propose the message again."
      },
      "text_too_long": {
        "when": "The CLI answered `linkedin.message_too_long`, or `linkedin.voice_too_long`: a voice message Reply finds too long to speak, which a script within 1000 characters can be. Reply's code travels in the trace.",
        "code": "message_too_long",
        "class": "validation",
        "note": "Reply found the text too long — for a voice message, too long to speak — and sent nothing. Write a shorter text and propose the message again."
      },
      "sender_not_found": {
        "when": "Reply's list of the channel's accounts holds none under the sender's identifier, the identifier is not a Reply number of the channel's size, refused without a call, or the send answered `email_account.not_found` or `linkedin.account_not_found`.",
        "code": "sender_not_found",
        "class": "permanent",
        "note": "Reply has no account of this channel under this identifier that this account can send from, so nothing was sent. Name an account sender.list returns."
      },
      "sender_mismatch": {
        "when": "Reply's list holds the sender's account under another address than the arguments state — compared as the runtime keys an address, so one it would not key matches nothing — or under one where they state none, or under none where they state one. Decided before the send.",
        "code": "sender_mismatch",
        "class": "validation",
        "note": "The account the arguments name is not held by Reply under the address they state, so the person approving read another account than the one the message would go from, and nothing was sent. Name the account by the identifier and the address sender.list returns."
      },
      "sender_blocked_channel": {
        "when": "Reply's list holds the sender's account as `inactive` or `disconnected`, or the send answered `linkedin.account_inactive` or `linkedin.invalid_cookie`; Reply's own word travels in the trace.",
        "code": "provider_blocked",
        "class": "blocked",
        "block": { "scope": "sender", "reason": "channel" },
        "unnamed_sender": "sender_turned_off",
        "note": "The account cannot send now — it is turned off, or its connection at Reply has to be made again — so nothing was sent. Put the account right at Reply; the message waits on the sender's block and goes once a trial gets past it."
      },
      "sender_blocked_limit": {
        "when": "The CLI answered `linkedin.limit_reached`, its word for Reply's 429 under `directLinkedIn.dailyLimitReached` or `directLinkedIn.weeklyLimitReached`, naming the send in the state `failed` or not: the LinkedIn account's daily or weekly allowance is spent, which one under `limit` in the trace. Where the CLI said how long to wait (`retry_after`), the block clears that long after the answer.",
        "code": "provider_blocked",
        "class": "blocked",
        "block": { "scope": "sender", "reason": "limit" },
        "unnamed_sender": "sender_allowance_spent",
        "note": "The LinkedIn account has spent its daily or weekly allowance — the attempt's trace says which — so nothing was sent. The message waits on the sender's block and goes once the allowance renews and a trial gets past it."
      },
      "sender_turned_off": {
        "when": "Only where a command that ran from no sending account, such as a read on the way to the send, answered `linkedin.account_inactive` or `linkedin.invalid_cookie`: a block names the account it holds, and there is none to name. Every other such refusal is the sender's block (`sender_blocked_channel`).",
        "code": "sender_unavailable",
        "class": "permanent",
        "note": "The account cannot send now — it is turned off, or its connection at Reply has to be made again — so nothing was sent. Put the account right at Reply, then propose the message again."
      },
      "sender_unavailable": {
        "when": "The send answered `linkedin.sales_navigator_required`, `linkedin.sales_navigator_disconnected` or `linkedin.voice_profile_missing`: the account lacks what the format needs, and still sends a plain message; Reply's own word travels in the trace.",
        "code": "sender_unavailable",
        "class": "permanent",
        "note": "The account lacks what this format needs — Sales Navigator for an InMail, a voice profile for a voice message — so nothing was sent. The attempt's trace says which; give the account what the format needs at Reply, or send another format, then propose the message again."
      },
      "sender_allowance_spent": {
        "when": "Since 0.7.0, only where a command that ran from no sending account, such as a read on the way to the send, answered `linkedin.limit_reached`: a block names the account it holds, and there is none to name. Every other such refusal is the sender's block (`sender_blocked_limit`).",
        "code": "sender_limit_reached",
        "class": "permanent",
        "note": "The LinkedIn account has spent its daily or weekly allowance — the attempt's trace says which — so nothing was sent. Propose the message again once the allowance renews."
      },
      "not_a_connection": {
        "when": "The CLI answered `linkedin.not_connected`: a LinkedIn message or voice message to a person the account is not connected with.",
        "code": "not_connected",
        "class": "permanent",
        "note": "The LinkedIn account is not connected with this person, and Reply sends a message or a voice message only to a connection, so nothing was sent. Invite them with social_invitation.send and send once they accept, or send an InMail."
      },
      "no_inmail_credits": {
        "when": "The CLI answered `linkedin.no_inmail_credits`: the LinkedIn account has no InMail credits left.",
        "code": "insufficient_credits",
        "class": "permanent",
        "note": "The LinkedIn account has no InMail credits left, so nothing was sent. Propose the InMail again once the account has credits, or send a message to a connection."
      },
      "opted_out_at_reply": {
        "when": "`reply contact get` answered the contact with `isOptedOut: true`. Reply does not refuse a send outside a sequence to such a person by itself, so this read is the stop. Nothing is sent.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply holds this person as opted out, so nothing was sent. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "blacklisted_at_reply": {
        "when": "The CLI answered `contact.blacklisted`: the account's blacklist holds the contact, which Reply consults on a direct email.",
        "code": "suppressed",
        "class": "permanent",
        "note": "The account's blacklist at Reply holds this person, so nothing was sent. This is a refusal on purpose; Reply's blacklist is changed at Reply."
      },
      "channel_value_conflict": {
        "when": "`reply contact get` answered the contact at another address than the one the work item consumes, or at none; or with another LinkedIn profile, or with none — or Reply holds nobody for the person — where the consumed one would be held at Reply under another spelling than the register was asked about. Reply's value is keyed whole as the runtime keys it, and the work item's compared as it stands. Nothing is changed or imported, and nothing is sent.",
        "code": "contact_channel_conflict",
        "class": "permanent",
        "note": "Reply's contact for this person is reached at another address or LinkedIn profile than the one this work item consumes, or at none, and this package never changes an address or a profile, so nothing was sent. Look at the contact at Reply: the value there, or the one Jason holds, is wrong."
      },
      "no_profile_at_reply": {
        "when": "The CLI answered `contact.no_linkedin_profile`: Reply holds no LinkedIn profile for the contact, though this package gave it the consumed one first.",
        "code": "contact_channel_conflict",
        "class": "permanent",
        "note": "Reply says its contact for this person carries no LinkedIn profile, though the one this work item consumes was written onto it first, so nothing was sent. Look at the contact at Reply."
      },
      "held_unnamed": {
        "when": "The CLI answered `contact.already_held` — Reply already held a contact under the person's fields, so the command sent nothing — without naming that contact in its identifiers. Never expected: the CLI names it, and the package then reads that contact and sends to it by its identifier.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply already held a contact for this person and the CLI did not say which, so it could not be read first and nothing was sent. Look the person up at Reply, then propose the message again."
      },
      "contact_gone": {
        "when": "The CLI answered `contact.not_found` for a contact this attempt was not pinned to — one the exact lookup found, one the send's own import made, or one it named as already held — so Reply named the contact and then held nothing under it. Nobody is pinned.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply named a contact for this person and then answered that it holds no such contact, so nothing was sent and nothing was pinned. Look the person up at Reply, then propose the message again."
      },
      "person_not_projected": {
        "when": "The input carries no `first_name`, `last_name`, `company`, `title` or `time_zone` for the person at all — absent, not null — which only a runtime older than 0.14.0, whose documents projected the person's identifier and channel alone, composes, so it is reported in a code that runtime's documents already declare. Decided before any call; the cause and the fields it lacks travel in the trace.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "This installation's Jason handed the package less of the person than the operation's document projects, so a person Reply holds nobody for could not be created under the name Jason holds, and nothing was called. Update Jason to 0.22.0 or newer; the attempt's trace names the fields."
      },
      "patch_answer_lost": {
        "when": "Writing the consumed profile onto a contact that held none timed out, exited 3 or answered something that is not a result. Nothing was sent, and the next attempt reads the contact again first.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "Reply did not say whether the person's LinkedIn profile was written onto its contact, and nothing was sent. The runtime repeats the attempt, which reads the contact again before anything is sent."
      },
      "list_not_finished": {
        "when": "The CLI answered `account.list_incomplete` to the read of the account's mailboxes or LinkedIn accounts. Nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply's list of accounts did not end, so the sender could not be found in it and nothing was sent."
      },
      "key_in_use": {
        "when": "The CLI answered `idempotency_key.in_use`: Reply is still finishing the send an earlier attempt of this work item made under the same key, and this one sent nothing.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "Reply says the first attempt under this key is still being finished, so this one sent nothing. The runtime repeats the attempt, which Reply then answers with the send the first one made, or lets go through where that one failed."
      },
      "linkedin_send_failed": {
        "when": "The CLI answered Reply's own `directLinkedIn.unknown` at status 400 — reply-cli from 0.17.1 as `outcome.unknown`, 0.17.0 as `reply.refused` — in any shape: naming the LinkedIn message by its reference or not, in the state `failed` or another. It is a failure Reply answered, and Reply releases the key of a send it answered so, so a repeat under the key would be a new send. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply answered the message with a failure on LinkedIn: it lost track of it, or says it did not go. It is not repeated, since a repeat could send it twice. Whether it went is a person's to find out at Reply, and whether to propose it again a person's to decide."
      },
      "send_answered_with_a_failure": {
        "when": "Reply answered the send with a failure at a status of 400 to 499, other than a 409 or a 429, that the CLI passes on as a lost answer (`outcome.unknown`, exit 3): Reply releases the key of a send it answered so, so a repeat under the key would be a new send. Only an ending with no answer of Reply's, none at all or one at 500 or above, is the lost answer. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply answered the message with a failure, and keeps no key for a send it answered so, so a repeat could send it twice; it is not repeated. Whether it went is a person's to find out at Reply, and whether to propose it again a person's to decide."
      },
      "send_failed_at_reply": {
        "when": "The send's answer, what Reply holds under the key for another request (`idempotency_key.reused`), or Reply's record of the send an earlier attempt returned (`context.unsettled_reference`, read by `reply direct-send get`) gives the message's state as `failed`: Reply took it and says it did not go. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "quotes": "reply",
        "note": "Reply took the message and says it did not go. Whether to propose it again is a person's to decide."
      },
      "send_cancelled_at_reply": {
        "when": "The send's answer, what Reply holds under the key for another request (`idempotency_key.reused`), or Reply's record of the send an earlier attempt returned (`context.unsettled_reference`, read by `reply direct-send get`) gives the message's state as `cancelled`: it was cancelled at Reply before it went. The reference, where Reply names one, is kept on the attempt.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The message was cancelled at Reply before it went, so nothing was sent. Whether to propose it again is a person's to decide."
      },
      "send_after_not_offered": {
        "when": "The arguments name a moment still ahead in `send_after`, and `reply direct-send get` answered that Reply's API has no route for it: the account's Reply does not keep a direct send for a later moment. Decided before the send; nothing is sent.",
        "code": "capability_not_offered",
        "class": "permanent",
        "quotes": "operation",
        "note": "The account's Reply does not keep a direct send for a later moment, so the message that names one in `send_after` was not sent: it goes at that moment or not at all. Asking again meets the same answer; what to do instead is the agent's to choose."
      },
      "schedule_not_offered": {
        "when": "The provider's own object names a sending schedule in `provider.schedule_id`, and `reply direct-send get` answered that Reply's API has no route for it: the account's Reply does not time a direct send by a schedule. Decided before the send; nothing is sent.",
        "code": "capability_not_offered",
        "class": "permanent",
        "quotes": "operation",
        "note": "The account's Reply does not time a direct send by a schedule, so the message that names one in `provider.schedule_id` was not sent: it goes by the schedule or not at all. Asking again meets the same answer; what to do instead is the agent's to choose."
      },
      "schedule_not_taken_on_the_route": {
        "when": "The provider's own object names a sending schedule in `provider.schedule_id`, and the send answered `reply.invalid_request` naming the field `scheduleId`: a Reply whose direct sends are deployed and whose LinkedIn routes do not take a schedule yet answers one so (scheduleId is not supported for LinkedIn sends in this version.). The package gives Reply only a schedule id it can read, so the field refused is the route not taking one, never a request built wrong. Nothing is sent.",
        "code": "capability_not_offered",
        "class": "permanent",
        "quotes": "operation",
        "note": "The account's Reply does not time a send on this channel by a schedule yet, so the message that names one in `provider.schedule_id` was not sent: it goes by the schedule or not at all. Asking again meets the same answer until Reply times such sends; what to do instead is the agent's to choose."
      },
      "schedule_not_held": {
        "when": "The send answered `schedule.not_found`, Reply's code `contact.scheduleNotFound` on an email or `directLinkedIn.scheduleNotFound` on a LinkedIn message, at 404, the second once Reply times deferred LinkedIn sends itself: the schedule `provider.schedule_id` names is not one the account can read, unknown and another account's alike. reply-cli gives both the one code, so an email and a LinkedIn message end on this row alike. Nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The schedule `provider.schedule_id` names is not one Reply holds for this account, so the message was not sent. Name a schedule `schedule.list` returns and propose the message again."
      },
      "schedule_needs_the_contact_s_zone": {
        "when": "The send answered `contact.time_zone_missing`, Reply's code `contact.contactTimeZoneMissing` on an email or `directLinkedIn.contactTimeZoneMissing` on a LinkedIn message, at 409, the second once Reply times deferred LinkedIn sends itself: the schedule `provider.schedule_id` names keeps the contact's own time zone, and Reply holds none for this person. reply-cli gives both the one code, so an email and a LinkedIn message end on this row alike. Nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The schedule `provider.schedule_id` names keeps the contact's own time zone, and Reply holds none for this person, so the message was not sent. Give the contact a time zone at Reply, or name a schedule that keeps its own, and propose the message again."
      },
      "schedule_has_no_hours": {
        "when": "The send answered `schedule.no_slot`, Reply's code `contact.scheduleHasNoSlot` on an email or `directLinkedIn.scheduleHasNoSlot` on a LinkedIn message, at 409, the second once Reply times deferred LinkedIn sends itself: the schedule `provider.schedule_id` names has no day with hours to send in. reply-cli gives both the one code, so an email and a LinkedIn message end on this row alike. Nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The schedule `provider.schedule_id` names has no day with hours to send in, so the message was not sent. Give the schedule hours at Reply, or name another, and propose the message again."
      },
      "provider_unreadable": {
        "when": "The work item carries the provider's own object beside the arguments (`args.provider`), and it is not an object whose one member is `schedule_id`: it names another member beside it or instead, or none. Decided before anything is read; nothing is sent.",
        "code": "provider_input_invalid",
        "class": "validation",
        "note": "`args.provider` is read on message.send as `{\"schedule_id\": \"<id>\"}` and nothing else, the id of a Reply sending schedule written as a string of its digits; the object given is not that shape."
      },
      "provider_schedule_unreadable": {
        "when": "The provider's own object names a schedule in `provider.schedule_id` that is not a string of a Reply id's digits — a positive whole number up to 2147483647, written without a sign, a space or a leading zero — such as a JSON number, or nothing. Decided before anything is read; nothing is sent.",
        "code": "provider_input_invalid",
        "class": "validation",
        "note": "`args.provider` is read on message.send as `{\"schedule_id\": \"<id>\"}` and nothing else, the id of a Reply sending schedule written as a string of its digits; the object given is not that shape."
      },
      "send_after_unreadable": {
        "when": "The arguments name a moment in `send_after` that this package's `Date.parse` cannot read — the claim refuses any form but the one the document names, so this is a moment that form admits and the runtime does not read, or one given past the claim. It is never read as a moment already past. Decided before anything is read; nothing is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The moment in `send_after` is not one this package can read, so the message was not sent: it goes at the moment a person approved or not at all. Asking again meets the same answer; the moment is the agent's to write again."
      },
      "send_not_settled": {
        "when": "A repeat after an attempt that may have sent — `context.unsettled_since` names when the earliest one was claimed — where `reply direct-send get` answered that Reply's API has no route for it: the account's Reply keeps no key on a direct send, so the repeat could send the person a second message. Decided before the send; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "The account's Reply keeps no key on a direct send, so repeating this message could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "send_not_settled_after_the_window": {
        "when": "A repeat 23 hours or more after `context.unsettled_since`, the moment the earliest attempt that may have sent was claimed: Reply keeps a direct send's key for a day, so the key may no longer settle the repeat. Where the runtime names the send such an attempt returned (`context.unsettled_reference`), Reply's record of it was read first and said nothing of it. Decided before the send; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "An earlier attempt of this message may have sent it 23 hours or more ago, longer than Reply surely keeps the key it went under, so repeating it could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "send_not_settled_not_owned": {
        "when": "A repeat told the send an earlier attempt returned (`context.unsettled_reference`), and `reply direct-send get` of it answered `direct_send.not_owned`: Reply holds that send for an account this one is not, so what Reply keeps under the key there settles nothing from here. Decided before the send, inside the window and past it; nothing is sent.",
        "code": "send_not_settled",
        "class": "permanent",
        "note": "An earlier attempt of this message may have sent it from an account of Reply's this one is not, so repeating it here could send the person a second one, and nothing was sent. Whether the first attempt's went is a person's to find out at Reply."
      },
      "feature_not_included": {
        "when": "The CLI answered `linkedin.feature_disabled` — Reply's code travels in the trace — or `access.feature_unavailable`: the account's plan or role does not include LinkedIn sends outside a sequence, or the accounts of the channel.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include messages outside a sequence on this channel, so nothing was sent. Signing in again would not change it; a plan or a role that includes them would."
      }
    },
    "message.status": {
      "send_not_found": {
        "when": "The CLI answered `direct_send.not_found`, or `direct_send.not_owned` for a send Reply holds for an account this one is not, or refused the reference without a call (`usage.reference`).",
        "code": "send_not_found",
        "class": "permanent",
        "note": "Reply holds no send under the reference the runtime keeps for this send, or none this account may see, so nothing was read. No later attempt will find it."
      },
      "answer_too_large_on_a_read": {
        "when": "The host reports the CLI's stdout truncated on `direct-send get`: the answer passed `Plugins:Exec:OutputBytes` and was cut off before it could be read.",
        "code": "provider_answer_too_large",
        "class": "permanent",
        "note": "Reply's answer was larger than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. The same read would be cut at the same place, so it is not repeated; raising `Plugins:Exec:OutputBytes` is what lets it through."
      }
    },
    "message.cancel": {
      "cancel_not_confirmed": {
        "when": "The CLI answered `direct_send.cancel_not_confirmed`, Reply's code `directSend.cancelNotConfirmed` at 503: it could not confirm the cancel, the send is still queued (or, once Reply times deferred LinkedIn sends itself, waiting for its schedule), and Reply asks for the cancel again, after the wait it names as `retry_after` where it names one. Any other answer at 500 or above to a cancel stays the lost answer.",
        "code": "provider_unavailable",
        "class": "transient",
        "note": "Reply could not confirm the cancel: the send is still queued (or, once Reply times deferred LinkedIn sends itself, waiting for its schedule), and Reply asks for the cancel again. The runtime cancels again later; a send already cancelled by then answers cancelled."
      },
      "send_not_found": {
        "when": "The CLI answered `direct_send.not_found`, or `direct_send.not_owned` for a send Reply holds for an account this one is not, or refused the reference without a call (`usage.reference`).",
        "code": "send_not_found",
        "class": "permanent",
        "note": "Reply holds no send under the reference the runtime keeps for this send, or none this account may see, so nothing was cancelled. No later attempt will find it."
      },
      "send_already_sent": {
        "when": "The CLI answered `direct_send.already_sent`, for a send that went or that Reply is sending now; or, once Reply times deferred LinkedIn sends itself, `direct_send.already_sending`, Reply's code `directSend.alreadySending` at 409, for a send Reply is sending now, even one a read still told as queued; or the cancel answered with a send in the state `sent` or `sending`. Nothing was changed.",
        "code": "send_not_pending",
        "class": "permanent",
        "state": "sent",
        "note": "The send already went, or Reply is sending it now, so there is nothing left to cancel and nothing was changed. What it became is message.status's to read."
      },
      "send_already_failed": {
        "when": "The CLI answered `direct_send.already_failed`, or the cancel answered with a send in the state `failed`. Nothing was changed.",
        "code": "send_not_pending",
        "class": "permanent",
        "state": "failed",
        "note": "The send already failed at Reply, so there is nothing left to cancel and nothing was changed. Reply's reason is message.status's to read."
      }
    },
    "events.read": {
      "program_never_started": {
        "when": "The CLI could not be started at all: exit code -1, and nothing was read.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "The Reply CLI could not be started, so the account's events were not read. Install it, make sure the runtime's grant names it, and reload the plugins; the runtime reads again later."
      },
      "command_not_known": {
        "when": "The CLI exited 2 with no code of its own, decided before any request was made: commander refused `inbox changes` or one of its flags as one it does not know. A reload refuses a reply-cli older than 0.17.0, so this is one that reports 0.17.0 or newer and lacks the command.",
        "code": "events_unsupported",
        "class": "permanent",
        "note": "The Reply CLI does not know `reply inbox changes` as this package calls it, although the version it reports has the command, so nothing was read. Install a published release of reply-cli and reload the plugins; the inbound sync keeps reading the account's threads page by page, and the runtime asks again after a day."
      },
      "call_refused_before_it_was_sent": {
        "when": "The CLI exited 2 with a usage error it gave a code to (`usage.*`), decided before any request was made: it refused what the call was built from, a moment, a count or a pair of flags this package wrote, or the profile the route names.",
        "code": "events_unsupported",
        "class": "permanent",
        "note": "The Reply CLI refused the call before making it. Nothing reached the account, and a repeat would build the same call, so this is reported rather than retried; it is no sign of an old reply-cli. The CLI's code is in the attempt's trace: a profile is the route's binding to correct, anything else is a defect of this package. The inbound sync keeps reading the account's threads page by page, and the runtime asks again after a day."
      },
      "answer_lost_on_a_read": {
        "when": "The read timed out, exited without an error the CLI explained, printed something that is not a page of the feed — one holding more items than it asked for, or saying more follows with no cursor of Reply's to go on from, among them — or had its answer cut off at `Plugins:Exec:OutputBytes`.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "Reply could not be reached, or did not answer with a page of its change feed. Reading again costs nothing, so the runtime reads again later."
      },
      "reply_not_reached": {
        "when": "The CLI answered `reply.unavailable`: Reply could not be reached, or did not answer after the CLI's own retries.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "Reply could not be reached, so the account's events were not read. The runtime reads again later."
      },
      "rate_limited": {
        "when": "The CLI answered `rate_limited`: Reply asked to wait longer than the CLI waits on its own, and the wait it asked for travels in the trace as `retry_after`.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "Reply asked for the account's events to be read later. The runtime reads again on its own cadence."
      },
      "unauthorized": {
        "when": "The CLI refused for want of a working credential (`auth.*`, exit 2), or answered `access.denied` or `access.feature_unavailable`.",
        "code": "events_unauthorized",
        "class": "permanent",
        "note": "The profile this route names is not signed in, or its key may not read the account's inbox. Sign in with `reply auth login` for that profile, or issue a key with the scope; the runtime tries again after a day."
      },
      "refusal_this_version_has_no_word_for": {
        "when": "The CLI answered `reply.refused`, `reply.invalid_request` naming no cursor, or a code this version does not know.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "Reply refused the read in a word this version of the package does not know; the CLI's code and Reply's own are in the attempt's trace. Nothing changes at Reply by reading, so the runtime reads again later, at a cadence that doubles while the refusal lasts."
      },
      "route_not_offered": {
        "when": "The CLI answered `reply.refused` at status 404 with no code of Reply's own: Reply's API has no route for the change feed, so the account's Reply does not offer it yet.",
        "code": "events_unsupported",
        "class": "permanent",
        "note": "This account's Reply has no change feed yet, so nothing was read; the inbound sync keeps reading the account's threads page by page. The runtime asks again after a day."
      },
      "cursor_not_held": {
        "when": "The CLI answered `inbox.cursor_expired`, the cursor or the moment older than the 30 days Reply's feed holds, or `inbox.cursor_refused`, a cursor Reply says it never issued, one ahead of its clock among them; or, to a read handed a cursor, `reply.refused` at status 400 with a code of Reply's own.",
        "code": "events_cursor_unknown",
        "class": "permanent",
        "note": "Reply's change feed cannot be read from the point this read started from: it keeps 30 days, and takes only cursors it issued. The runtime reads again by date at once."
      },
      "cursor_not_written_here": {
        "when": "The cursor is none of the three this package writes: `c|` and Reply's cursor, `m|` and a moment, or `u|`, a moment, `|` and Reply's cursor, each moment as `toISOString` writes one. A spent span's `u|` with no cursor of Reply's is among them. Decided before any call.",
        "code": "events_cursor_unknown",
        "class": "permanent",
        "note": "The cursor handed back is not one this version of the package wrote, so it cannot be read from. The runtime reads again by date."
      },
      "item_unreadable": {
        "when": "A page of the feed held an item with no thread identifier or no moment of last activity this package can read. The whole page is refused rather than the item passed over, because the cursor would move past it for good.",
        "code": "events_unreachable",
        "class": "transient",
        "note": "Reply's change feed answered with a thread this package could not read, so the page was not taken and the cursor stays where it was. The runtime reads again later. Should the same thread come back on every read, the source stops being healthy, and the inbound sync reads the campaigns it covers again, so a stuck feed loses no reply."
      }
    }
  }
};

// The SDK reads request objects strictly, so a key whose value is undefined is dropped rather than passed on.
function compact(request) {
  const cleaned = {};
  for (const key of Object.keys(request)) {
    if (request[key] !== undefined) {
      cleaned[key] = request[key];
    }
  }

  return cleaned;
}

function has(table, key) {
  return table !== null && typeof table === "object" && Object.prototype.hasOwnProperty.call(table, key);
}

// Read or write, for the one decision that turns on it. A command this package does not declare gets no default:
// treating an unmarked write as a read is exactly the mistake that ends in a double send.
export function kindOf(command) {
  return has(COMMANDS, command) ? COMMANDS[command].kind : null;
}

// Which lost-answer row an ending on this command belongs in. Only a command positively marked a read is treated as
// one: the expensive side is the safe side.
export function lostAnswerRow(command) {
  return kindOf(command) === "read" ? "answer_lost_on_a_read" : "answer_lost_on_a_write";
}

// The row an answer cut off at the exec capture belongs in. A read whose operation has a row of its own for it fails
// with that one, permanently: asking again would be cut at the same byte. Anything else keeps the lost-answer ending.
export function cutAnswerRow(operation, command) {
  const own = operation !== null && operation !== undefined ? ROWS.per_operation[operation] : undefined;
  const readsIt = own !== undefined && has(own, "answer_too_large_on_a_read");
  return kindOf(command) === "read" && readsIt ? "answer_too_large_on_a_read" : lostAnswerRow(command);
}

// The row an error the CLI explained belongs in, for this operation: its own mapping, then the shared one, then the
// CLI's namespaces. An invalid request naming a field has a row of its own wherever the operation has one for that
// field: the email, where the operation takes an address, because there the person's data is what Reply refused rather
// than a request this package built wrong; and a send's text, which Reply holds to a length of its own. `variant` is
// `template` for a send composed as one, and undefined otherwise.
export function rowOf(operation, error, variant) {
  const code = typeof error.code === "string" ? error.code : "";
  const own = has(CODES.per_operation, operation) ? CODES.per_operation[operation] : {};
  const details = error.details !== null && typeof error.details === "object" ? error.details : {};
  const fields = Array.isArray(details.fields) ? details.fields : [];

  for (const field of code === "reply.invalid_request" ? fields : []) {
    if (typeof field === "string" && has(own, code + ":" + field)) {
      return own[code + ":" + field];
    }
  }

  // A refusal Reply explained, by its own code and the first word of its sentence, where the operation has a row for
  // exactly that one: Reply's code for invalid input covers more causes than any one row could name.
  if (typeof error.reply_code === "string" && typeof error.detail === "string") {
    const keyed = code + ":" + error.reply_code + ":" + error.detail.split(/[:\s]/)[0];
    if (typeof variant === "string" && has(own, variant + ":" + keyed)) {
      return own[variant + ":" + keyed];
    }

    if (has(own, keyed)) {
      return own[keyed];
    }
  }

  // A refusal the variant has a row of its own for, by the CLI's code alone: a template Reply could not fill in.
  if (typeof variant === "string" && has(own, variant + ":" + code)) {
    return own[variant + ":" + code];
  }

  // A refusal Reply explained by a code of its own, at a status the variant has a row for whatever the code: a read
  // handed a cursor, refused at 400, was refused for that cursor.
  if (typeof variant === "string" && typeof error.reply_code === "string" && has(own, variant + ":" + code + ":" + error.status)) {
    return own[variant + ":" + code + ":" + error.status];
  }

  const unknown = unknownKey(code, error);
  if (unknown !== null && has(CODES.shared, unknown)) {
    return CODES.shared[unknown];
  }

  if (has(own, code)) {
    return own[code];
  }

  if (has(CODES.shared, code)) {
    return CODES.shared[code];
  }

  if (code.indexOf("auth.") === 0) {
    return "unauthorized";
  }

  if (code.indexOf("usage.") === 0) {
    return "call_refused_before_it_was_sent";
  }

  return "refusal_this_version_has_no_word_for";
}

// The row of an exit 2 the CLI explained with no code at all, for this operation.
export function unexplainedRefusalRow(operation) {
  const own = has(CODES.per_operation, operation) ? CODES.per_operation[operation] : {};
  return has(own, "usage") ? own["usage"] : "call_refused_before_it_was_sent";
}

// The key of a refusal that says Reply's API has no route for the call, or null: a refusal at 404 with no code of
// Reply's own.
function unknownKey(code, error) {
  return code === "reply.refused" && typeof error.reply_code !== "string" && error.status === 404 ? code + ":404" : null;
}

// The canonical code a row reports, for the one decision that turns on it: a campaign Reply has just denied is never
// pinned.
export function codeOf(operation, key) {
  return rowFor(operation, key).code;
}

// What came back, short enough to travel in a failure: a provider's answer is reported rather than interpreted
// wherever this package has no word for it, and 200 characters is enough to recognise it.
export function describe(value) {
  let text;
  try {
    text = typeof value === "string" ? value : JSON.stringify(value);
  } catch (error) {
    text = String(value);
  }

  if (typeof text !== "string") {
    return String(value);
  }

  return text.length > 200 ? text.slice(0, 200) : text;
}

// What a result of an unexpected shape may say in a failure: the identifiers and words reply-cli's results are
// documented to carry, and nothing else, because anything else a result held could be part of a person's record.
const EVIDENCE = ["status", "previous_status", "sequence_id", "list_id", "contact_id", "contacts", "contact_created", "sequence_active", "thread_id", "channel", "category_id", "activity_id", "reply_status", "reference", "state", "send_after"];

export function evidence(result) {
  const kept = {};
  if (result !== null && typeof result === "object" && !Array.isArray(result)) {
    for (const key of EVIDENCE) {
      if (has(result, key)) {
        kept[key] = result[key];
      }
    }
  }

  return describe(kept);
}

// The one way this package reports a failure. `operation` is the canonical operation being performed; `observed`
// names the row and what was seen; `learned` is what the package now knows Reply calls our entities, which travels
// out with the failure because after a lost answer a pin is the only trace of what was done.
export function fail(operation, observed, learned) {
  const named = rowFor(operation, observed.row);
  // A sender's block names the sending account the call went from. A command that ran from none, such as a read on the
  // way to the send, has none to name, and a block without it is one the runtime refuses: it ends in the row's own
  // permanent word instead.
  const row = named.block !== undefined && named.block.scope === "sender" && !(typeof observed.sender === "string" && observed.sender.length > 0)
    ? rowFor(operation, named.unnamed_sender)
    : named;
  const details = compact({
    call: observed.call,
    status: observed.status,
    cli_code: observed.cli_code,
    provider_code: observed.provider_code,
    // Reply's own sentence, where the CLI passed one on: "Invalid data" is the only sign that a first name was missing.
    provider_detail: observed.provider_detail === undefined ? undefined : describe(observed.provider_detail),
    // What an answer said about this one person where it could not be read as a result. Reported, never read.
    provider_item: observed.provider_item,
    // Reply's own reason for a message it took and could not deliver, where it recorded one.
    provider_status_code: observed.provider_status_code,
    // The names of a template Reply could not fill in, as Reply names them, where it listed them.
    missing_variables: observed.missing_variables,
    // What this package itself could not do, for the endings that are its own refusal.
    reason: observed.reason,
    retry_after: observed.retry_after,
    // Which of a sender's allowances is spent, where the provider said: daily or weekly.
    limit: observed.limit,
    // Where in the provider's own object this package could not read what it holds (provider.js), and what it reads there.
    pointer: observed.pointer,
    // The two numbers a person's decision was about and the one found at execution, where a population moved; for the
    // provider's object, the shape this package reads at the pointer.
    expected: observed.expected,
    actual: observed.actual,
    // What became of a send that can no longer be cancelled, where the row says: `sent` or `failed`.
    state: row.state,
    exit_code: observed.exit_code,
    timed_out: observed.timed_out,
  });

  throw host.fail(compact({
    class: row.class,
    code: row.code,
    message: messageOf(row, observed, operation),
    details: Object.keys(details).length === 0 ? undefined : details,
    external_ids: learned,
    block: row.block === undefined ? undefined : blockOf(row.block, observed),
  }));
}

// The failure's message: the row's note, and for a row that `quotes`, what would otherwise reach only the trace — this
// package's own reason before the note, or Reply's own sentence after it — or the operation it is about, which a row
// every operation shares cannot name itself.
function messageOf(row, observed, operation) {
  if (row.quotes === "operation" && typeof operation === "string") {
    return operation + ": " + row.note;
  }

  if (row.quotes === "reason" && typeof observed.reason === "string") {
    return observed.reason + " " + row.note;
  }

  const names = Array.isArray(observed.missing_variables) && observed.missing_variables.length > 0
    ? " Reply could not fill " + describe(observed.missing_variables.join(", ")) + "."
    : "";
  if (row.quotes === "reply" && (typeof observed.provider_detail === "string" || names.length > 0)) {
    return row.note + (typeof observed.provider_detail === "string" ? " Reply said: " + describe(observed.provider_detail) : "") + names;
  }

  return row.note;
}

// What a row of class `blocked` says is blocked: the scope and the reason the row names, the sending account the call
// went from where the scope is a sender, and when it clears where the CLI said how long to wait — that long after this
// answer, since Reply's wait counts from it. A wait that is not a number of seconds between none and a year is left out,
// and the runtime tries the block on its own cadence: Reply's allowances renew by the day or the week, and a moment far
// enough out would be written with a year the block contract does not take.
const LONGEST_WAIT_SECONDS = 365 * 24 * 60 * 60;

function blockOf(block, observed) {
  const wait = observed.retry_after;
  const clears = typeof wait === "number" && Number.isFinite(wait) && wait >= 0 && wait <= LONGEST_WAIT_SECONDS
    ? new Date(Date.now() + wait * 1000).toISOString()
    : undefined;
  return compact({
    scope: block.scope,
    sender_external_id: block.scope === "sender" ? observed.sender : undefined,
    reason: block.reason,
    clears_at: clears,
  });
}

function rowFor(operation, key) {
  const own = operation !== null && operation !== undefined ? ROWS.per_operation[operation] : undefined;
  if (own !== undefined && has(own, key)) {
    return own[key];
  }

  if (has(ROWS.shared, key)) {
    return ROWS.shared[key];
  }

  // A row this package named and did not write is a defect in this package, not an answer from Reply. It is reported
  // as itself rather than folded into a neighbouring row.
  throw host.fail({
    class: "permanent",
    code: "provider_call_failed",
    message: "This plugin has no error row named '" + key + "'" + (operation ? " for " + operation : "") + ".",
  });
}
