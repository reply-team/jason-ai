// What every ending this package can meet means, as a table rather than as a habit.
//
// Three things are decided here and nowhere else. The first is the read/write mark of every command this package
// runs: a lost answer to a read costs nothing to repeat, while a lost answer to a write may already have put a person
// into a sequence. The second is which row each of the Reply CLI's own error codes is, operation by operation. The
// third is the code and the failure class each row reports, which is what the runtime acts on — `transient` is
// retried, `permanent` and `validation` are final, and `ambiguous` is repeated only where the operation's contract
// says a repeat is safe.
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
  "inbox categories": {
    "kind": "read",
    "note": "The account's inbox categories, Reply's reserved ones included: what conversation.classify matches the category it was given against, by identifier or by name, before it files anything."
  },
  "contact get": {
    "kind": "read",
    "note": "One contact by its id, as Reply holds it: the read contact.delete makes before it deletes anything, of whether Reply still holds the contact and whether it holds them as opted out; and the read a send makes of the same and of where the contact is reached, an answer without that key read as lost. Nothing is kept."
  },
  "contact update": {
    "kind": "write",
    "note": "Changes the fields and custom fields stdin names on the contact, by its id; a null clears one and the rest stay. Set outright, so a repeat by the same contact lands on the same values. Before a LinkedIn send it also gives a contact that carries no LinkedIn profile the one the send consumes, and nothing else."
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
  "account list": {
    "kind": "read",
    "note": "The mailboxes or the LinkedIn accounts a send can go from, one channel at a time, teammates' included, each with the Reply user whose it is: what sender.list answers from."
  },
  "contact linkedin-connect": {
    "kind": "write",
    "note": "Invites the contact to connect on LinkedIn from the account named, with the note on stdin or with none. A send Reply keeps no key on, so a lost answer is never repeated."
  },
  "contact send-email": {
    "kind": "write",
    "note": "Sends the contact one email from the mailbox named, outside any sequence, with the subject and the body on stdin — the text a person approved, written as HTML. A send Reply keeps no key on, so a lost answer is never repeated."
  },
  "contact linkedin-message": {
    "kind": "write",
    "note": "Sends the contact one LinkedIn message from the account named, outside any sequence, with the text on stdin; the contact has to be a connection of the account. A send Reply keeps no key on, so a lost answer is never repeated."
  },
  "contact linkedin-inmail": {
    "kind": "write",
    "note": "Sends the contact one InMail from the account named, outside any sequence, with the subject and the text on stdin. A send Reply keeps no key on, so a lost answer is never repeated."
  },
  "contact linkedin-voice": {
    "kind": "write",
    "note": "Sends the contact one voice message from the account named, outside any sequence, which Reply speaks from the script on stdin; the contact has to be a connection of the account. A send Reply keeps no key on, so a lost answer is never repeated."
  }
};

// Which row each of the Reply CLI's error codes is. An operation's own mapping wins over the shared one; a code in
// neither is `auth.*` (who is calling), `usage.*` (refused before anything was sent), or a refusal this version has
// no word for. `reply.invalid_request:email` is the row for an invalid request whose fields name the email — and
// `reply.invalid_request:message` one whose fields name a send's text — and
// `reply.refused:inboxThread.invalidInput:RenderingFail` the row for a refusal Reply explained by its own code and by the
// first word of its sentence, up to the first colon or white space.
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
      "contact.field_not_found": "field_not_found",
      "reply.invalid_request": "value_refused",
      "access.feature_unavailable": "feature_not_included"
    },
    "sender.list": {
      "access.feature_unavailable": "feature_not_included",
      "account.list_incomplete": "list_not_finished",
      "network": "reply_not_reached"
    },
    "social_invitation.send": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.no_linkedin_profile": "no_profile_at_reply",
      "account.list_incomplete": "list_not_finished",
      "linkedin.account_not_found": "sender_not_found",
      "linkedin.account_inactive": "sender_unavailable",
      "linkedin.invalid_cookie": "sender_unavailable",
      "linkedin.limit_reached": "sender_allowance_spent",
      "linkedin.message_too_long": "note_too_long",
      "reply.invalid_request:message": "note_too_long",
      "linkedin.missing_variables": "text_would_render",
      "linkedin.feature_disabled": "feature_not_included",
      "access.feature_unavailable": "feature_not_included"
    },
    "message.send": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.no_linkedin_profile": "no_profile_at_reply",
      "contact.blacklisted": "blacklisted_at_reply",
      "account.list_incomplete": "list_not_finished",
      "email_account.not_found": "sender_not_found",
      "linkedin.account_not_found": "sender_not_found",
      "linkedin.account_inactive": "sender_unavailable",
      "linkedin.invalid_cookie": "sender_unavailable",
      "linkedin.sales_navigator_required": "sender_unavailable",
      "linkedin.sales_navigator_disconnected": "sender_unavailable",
      "linkedin.voice_profile_missing": "sender_unavailable",
      "linkedin.limit_reached": "sender_allowance_spent",
      "linkedin.not_connected": "not_a_connection",
      "linkedin.no_inmail_credits": "no_inmail_credits",
      "linkedin.message_too_long": "text_too_long",
      "linkedin.voice_too_long": "text_too_long",
      "linkedin.missing_variables": "text_would_render",
      "linkedin.feature_disabled": "feature_not_included",
      "access.feature_unavailable": "feature_not_included"
    }
  }
};

// The table itself. `shared` holds the rows every operation this package performs declares; `per_operation` holds
// the rows one operation declares and another does not — the documents do not publish the same failure codes,
// and a shared row naming a code an operation never declared would be a code the runtime refuses. Each row says
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
      "nobody_under_the_address": {
        "when": "The person has no pin, and the CLI's exact lookup of the address found nobody Reply holds under it. Nothing is made: an update changes a contact Reply holds.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "Reply holds nobody under this address, so there is no contact to change, and an update makes none. Enroll the person or put them on a list first — either makes the contact — then propose the update again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was changed. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by. Decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no value on the channel the call named that Reply could find them by, so nothing was asked. Name a channel they have: an email or a LinkedIn profile."
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
    "social_invitation.send": {
      "contact_not_found": {
        "when": "`reply contact get` or the invitation answered `contact.not_found` for the pinned contact, or the pin is not a Reply contact number, refused without a call.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account — so nothing was sent. The pin has to be cleared before this person can be worked again."
      },
      "nobody_under_the_value": {
        "when": "The person has no pin, and the CLI's exact lookup of the profile found nobody Reply holds under it. Nothing is made: an invitation goes to a contact Reply holds.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "Reply holds nobody under this profile, so there is no contact to invite, and an invitation makes none. Put the person on a list or enroll them first — either makes the contact — then propose the invitation again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the profile answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this profile, so picking one would be guessing and nothing was sent. A person resolves the duplicate at Reply, then proposes the invitation again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by. Decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no LinkedIn profile on the channel the call named, so there is nobody to invite. Name the channel they have a profile on."
      },
      "placeholder_in_the_text": {
        "when": "The note carries a double opening brace, which Reply reads as the start of a placeholder of its own. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The note carries a double opening brace, which Reply reads as a placeholder it fills in for each person, so what would go out might not be the note a person approved. Nothing was sent. Write the note without one and propose the invitation again."
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
      "sender_unavailable": {
        "when": "Reply's list holds the sender's account as `inactive` or `disconnected`, or the CLI answered `linkedin.account_inactive` or `linkedin.invalid_cookie`; Reply's own word travels in the trace.",
        "code": "sender_unavailable",
        "class": "permanent",
        "note": "The LinkedIn account cannot send now — it is turned off, or its session at LinkedIn has to be connected again at Reply — so nothing was sent. Put the account right at Reply, then propose the invitation again."
      },
      "sender_allowance_spent": {
        "when": "The CLI answered `linkedin.limit_reached`: the LinkedIn account's daily or weekly allowance is spent, which one under `limit` in the trace.",
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
        "when": "`reply contact get` answered the contact with another LinkedIn profile than the one the work item consumes, the one Reply holds keyed as the runtime keys a profile and the work item's compared as it stands; or with none, where the consumed one would be held at Reply under another spelling than the register was asked about. Nothing is changed and nothing is sent.",
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
      "nobody_under_the_value": {
        "when": "The person has no pin, and the CLI's exact lookup of the address or the profile found nobody Reply holds under it. Nothing is made: a message goes to a contact Reply holds.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "Reply holds nobody under this address or profile, so there is no contact to send to, and a message makes none. Put the person on a list or enroll them first — either makes the contact — then propose the message again."
      },
      "contact_not_unique": {
        "when": "The person has no pin, and the CLI's exact lookup of the address or the profile answered `contact.not_unique`: more than one contact is held under it.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address or profile, so picking one would be guessing and nothing was sent. A person resolves the duplicate at Reply, then proposes the message again."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named, or names a channel Reply looks nobody up by. Decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no address or LinkedIn profile on the channel the call named, so there is nobody to send to. Name the channel they have."
      },
      "placeholder_in_the_text": {
        "when": "The subject or the text carries a double opening brace, which Reply reads as the start of a placeholder of its own. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The subject or the text carries a double opening brace, which Reply reads as a placeholder it fills in for each person, so what would go out might not be the message a person approved. Nothing was sent. Write it without one and propose the message again."
      },
      "format_of_another_channel": {
        "when": "The message's format belongs to another channel than the one the call named — an email on LinkedIn, or a LinkedIn format on the email channel. The document refuses it first; this is the package's own check behind it. Decided before any call.",
        "code": "message_invalid",
        "class": "validation",
        "note": "The message's format belongs to another channel than the one the call named, so it could not go as a person approved it. Nothing was sent. Name the format of the channel, or the channel of the format, and propose the message again."
      },
      "text_would_render": {
        "when": "The CLI answered `linkedin.missing_variables`: Reply read part of a LinkedIn text as a placeholder of its own and could not fill it in.",
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
      "sender_unavailable": {
        "when": "Reply's list holds the sender's account as `inactive` or `disconnected`, or the send answered `linkedin.account_inactive`, `linkedin.invalid_cookie`, `linkedin.sales_navigator_required`, `linkedin.sales_navigator_disconnected` or `linkedin.voice_profile_missing`; Reply's own word travels in the trace.",
        "code": "sender_unavailable",
        "class": "permanent",
        "note": "The account cannot send this message now — it is turned off, its connection at Reply has to be made again, or it lacks what the format needs: Sales Navigator for an InMail, a voice profile for a voice message — so nothing was sent. The attempt's trace says which; put the account right at Reply, then propose the message again."
      },
      "sender_allowance_spent": {
        "when": "The CLI answered `linkedin.limit_reached`: the LinkedIn account's daily or weekly allowance is spent, which one under `limit` in the trace.",
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
        "when": "`reply contact get` answered the contact at another address than the one the work item consumes, or at none; or with another LinkedIn profile, or with none where the consumed one would be held at Reply under another spelling than the register was asked about. Reply's value is keyed whole as the runtime keys it, and the work item's compared as it stands. Nothing is changed and nothing is sent.",
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
      "feature_not_included": {
        "when": "The CLI answered `linkedin.feature_disabled` — Reply's code travels in the trace — or `access.feature_unavailable`: the account's plan or role does not include LinkedIn sends outside a sequence, or the accounts of the channel.",
        "code": "feature_unavailable",
        "class": "permanent",
        "note": "Reply says this account's plan or role does not include messages outside a sequence on this channel, so nothing was sent. Signing in again would not change it; a plan or a role that includes them would."
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
// than a request this package built wrong; and a send's text, which Reply holds to a length of its own.
export function rowOf(operation, error) {
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
    if (has(own, keyed)) {
      return own[keyed];
    }
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
const EVIDENCE = ["status", "previous_status", "sequence_id", "list_id", "contact_id", "contacts", "contact_created", "sequence_active", "thread_id", "channel", "category_id", "activity_id", "reply_status"];

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
  const row = rowFor(operation, observed.row);
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
    // What this package itself could not do, for the endings that are its own refusal.
    reason: observed.reason,
    retry_after: observed.retry_after,
    // Which of a sender's allowances is spent, where the provider said: daily or weekly.
    limit: observed.limit,
    // The two numbers a person's decision was about and the one found at execution, where a population moved.
    expected: observed.expected,
    actual: observed.actual,
    exit_code: observed.exit_code,
    timed_out: observed.timed_out,
  });

  throw host.fail(compact({
    class: row.class,
    code: row.code,
    message: row.note,
    details: Object.keys(details).length === 0 ? undefined : details,
    external_ids: learned,
  }));
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
