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

// Every command this package runs, and whether it is a read or a write. The raw calls are the contact search a list
// add, an enrolment or an opt-out makes for a person who has no pin and no first name — a POST, and a read all the
// same — and the list and the create of sequences, for which reply-cli has no command.
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
    "note": "One thread, its subject, and one page of its messages: what conversation.get answers from."
  },
  "api POST /v3/contacts/filter": {
    "kind": "read",
    "note": "Reply's contact search by address: the lookup list_membership.add, campaign.enroll and optout.record make for a person with no pin and no first name. A POST, and a read all the same."
  },
  "api GET /v3/sequences": {
    "kind": "read",
    "note": "Reply's list of sequences, a thousand at a time and read to its end: the names campaign.create compares with the one it is to give, before it makes anything. The name is compared here and never put on a command line."
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
  "api POST /v3/sequences": {
    "kind": "write",
    "note": "Makes the sequence campaign.create answers with: a draft that holds nobody, with one task step for its owner. The name travels in the body, on stdin."
  }
};

// Which row each of the Reply CLI's error codes is. An operation's own mapping wins over the shared one; a code in
// neither is `auth.*` (who is calling), `usage.*` (refused before anything was sent), or a refusal this version has
// no word for. `reply.invalid_request:email` is the row for an invalid request whose fields name the email.
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
    "optout.record": {
      "contact.not_found": "contact_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.lookup_incomplete": "lookup_not_finished",
      "contact.invalid_email": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value"
    },
    "enrollment.stop": {
      "sequence.not_found": "campaign_not_found",
      "contact.not_unique": "contact_not_unique",
      "contact.lookup_incomplete": "lookup_not_finished"
    },
    "campaign.enroll": {
      "sequence.not_found": "campaign_not_found",
      "sequence.archived": "campaign_not_enrollable",
      "sequence.has_no_steps": "campaign_not_enrollable",
      "sequence.step_not_found": "call_could_not_be_built",
      "usage.range": "call_could_not_be_built",
      "contact.not_found": "contact_not_found",
      "contact.opted_out": "suppressed",
      "contact.invalid_email": "invalid_channel_value",
      "account.contact_limit_reached": "limit_reached",
      "reply.invalid_request:email": "invalid_channel_value"
    },
    "list_membership.add": {
      "contact-list.not_found": "list_not_found",
      "contact.not_found": "contact_not_found",
      "contact.opted_out": "suppressed",
      "contact.invalid_email": "invalid_channel_value",
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
      "when": "The CLI answered `reply.unavailable`: Reply could not be reached, or a read failed after the CLI's own retries. The CLI never resends a write, so a write that ends this way was never sent.",
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
        "when": "Reply's list of sequences was read to its cap, twenty pages of a thousand, and said there were more. Nothing is made.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply's list of sequences was read to twenty thousand, twenty pages of a thousand, and Reply said there were more, so whether the name is taken could not be decided, and nothing was made. Make the sequence at Reply and link it with campaign.get."
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
        "when": "The pinned contact identifier, or the campaign's, is not a Reply number at all. Decided before any call.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The identifier this read would filter by names nothing a Reply account could hold, so nothing was asked. The pin is wrong; the attempt's trace says which one."
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
        "when": "The CLI answered `contact.invalid_email`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, decided before any call.",
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
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nothing was added. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nothing was added. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "contact_not_unique": {
        "when": "The person has no pin and no first name, and the lookup by address, read to its end, found more than one contact whose email equals it, ignoring case.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was added. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "lookup_not_finished": {
        "when": "The person has no pin and no first name, and the lookup by address read five pages of a hundred and Reply said there were more.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more contacts whose address holds this one than one lookup reads, so whether exactly one of them is this person could not be decided, and nothing was added. Give the person a first name in Jason, or a pin, and propose the work again."
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
        "when": "The CLI answered `contact.invalid_email`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, decided before any call.",
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
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so nobody was enrolled. Give the person a first name in Jason and propose the work again."
      },
      "first_name_derivation_forbidden": {
        "when": "The person has no pin and no first name, the lookup by address found nobody Reply holds under it, and the route's binding says `derive_first_name: false`.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The route forbids deriving a first name, the person has none, and Reply holds nobody under the address and creates nobody without a first name, so nobody was enrolled. Give the person a first name in Jason, or set `derive_first_name` to true in the route's binding (absent means true), and propose the work again."
      },
      "contact_not_unique": {
        "when": "The person has no pin and no first name, and the lookup by address, read to its end, found more than one contact whose email equals it, ignoring case.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nobody was enrolled. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "lookup_not_finished": {
        "when": "The person has no pin and no first name, and the lookup by address read five pages of a hundred and Reply said there were more.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more contacts whose address holds this one than one lookup reads, so whether exactly one of them is this person could not be decided, and nobody was enrolled. Give the person a first name in Jason, or a pin, and propose the work again."
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
        "when": "The CLI answered `contact.not_unique`: more than one contact's email equals the address, ignoring case.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nobody was taken out. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "lookup_not_finished": {
        "when": "The CLI answered `contact.lookup_incomplete`: more than five hundred contacts hold the address, so its lookup could not be read to its end.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more contacts whose address holds this one than one lookup reads, so whether exactly one of them is this person could not be decided, and nobody was taken out — it is never read as a person Reply does not hold. A repeat would read the same pages, so it is not repeated: take the person out at Reply by hand, then check with enrollment.list."
      },
      "invalid_channel_value": {
        "when": "The person carries no value on the channel the call named. Decided before any call.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "The person has no value on the channel the call named, so there is no address to find them by. Name a channel they have."
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
        "when": "The person has no pin, no first name and no last name, the value on the channel the call named has no text before an `@`, and the lookup by address found nobody Reply holds under it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "A first name could not be derived: the person has no first name, no last name and no address to take one from, Reply holds nobody under the value, and Reply creates nobody without a first name, so no contact was made and no flag set. Reply holds nobody under this address, so it cannot reach them there either. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach."
      },
      "contact_not_unique": {
        "when": "The CLI answered `contact.not_unique`, or this package's own lookup, made for a person with no pin and no first name, read to its end and found more than one contact: more than one contact's email equals the address, ignoring case.",
        "code": "contact_not_unique",
        "class": "permanent",
        "note": "Reply holds more than one contact under this address, so picking one would be guessing and nothing was done. A person resolves the duplicate at Reply, then proposes the work again."
      },
      "lookup_not_finished": {
        "when": "The CLI answered `contact.lookup_incomplete`, or this package's own lookup, made for a person with no pin and no first name, read five pages of a hundred and Reply said there were more: more than five hundred contacts hold the address, so the lookup could not be read to its end.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply holds more contacts whose address holds this one than one lookup reads, so whether exactly one of them is this person could not be decided, and nothing was done: no contact made, no flag set. A repeat would read the same pages, so it is not repeated: record the opt-out at Reply by hand. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach."
      },
      "invalid_channel_value": {
        "when": "The CLI answered `contact.invalid_email`, or `reply.invalid_request` naming the email field; or the person carries no value on the channel the call named, decided before any call.",
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
// CLI's namespaces. An invalid request naming the email field has a row of its own wherever the operation takes an
// address, because there the person's data is what Reply refused rather than a request this package built wrong.
export function rowOf(operation, error) {
  const code = typeof error.code === "string" ? error.code : "";
  const own = has(CODES.per_operation, operation) ? CODES.per_operation[operation] : {};
  const details = error.details !== null && typeof error.details === "object" ? error.details : {};
  const fields = Array.isArray(details.fields) ? details.fields : [];

  if (code === "reply.invalid_request" && fields.indexOf("email") >= 0 && has(own, code + ":email")) {
    return own[code + ":email"];
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

// What was seen on the one raw call, in the shape a failure is reported from. Reply's own code is read only where it
// is a string on an object, because a 401 arrives with an empty body.
export function observe(call, answer, row) {
  const data = answer.data;
  return {
    call: call,
    row: row,
    status: answer.code,
    provider_code: data !== null && typeof data === "object" && typeof data.code === "string" ? data.code : undefined,
  };
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
const EVIDENCE = ["status", "previous_status", "sequence_id", "list_id", "contact_id", "contacts", "contact_created", "sequence_active"];

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

// Which shared row the raw call's status belongs in.
export function statusRow(status, call) {
  if (status === 401 || status === 403) {
    return "unauthorized";
  }

  if (status === 429) {
    return "rate_limited";
  }

  if (status >= 500) {
    return lostAnswerRow(call);
  }

  return "refusal_this_version_has_no_word_for";
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
    // What this package itself could not do, for the endings that are its own refusal.
    reason: observed.reason,
    retry_after: observed.retry_after,
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
