// What every ending this package can meet means, as a table rather than as a habit.
//
// Two things are decided here and nowhere else. The first is the read/write mark of every call the package makes:
// a lost answer to a read costs nothing to repeat, while a lost answer to a write may already have created a
// contact or sent a person an email, and with consumption pricing a repeat is a real charge. The second is the
// code and the failure class each observable condition gets, which is what the runtime acts on — `transient` is
// retried, `permanent` and `validation` are final, and `ambiguous` stops the work item for a person.
//
// The two constants below are STRICT JSON: double-quoted keys and strings, no comments inside them, no trailing
// commas, and each one ends with a closing brace at the start of a line followed by a semicolon. A test on the
// runtime side parses exactly that span out of this file and holds every code it names to the published contract
// of the operation that names it — same code, same class — so this table and the runtime cannot drift apart.
// There is no second copy of it anywhere. Keep editing them as JSON.

// Every Reply API v3 call this package makes, and whether it is a read or a write. The mark is per call rather
// than per verb: `POST /v3/contacts/import` is a write that creates people and can even enrol them, and a filter
// search would be a read for all that it is a POST. A call that is not in this table is refused rather than
// guessed at, because guessing here is how a write comes to be retried.
export const CALLS = {
  "GET /v3/sequences/{id}": {
    "kind": "read",
    "note": "The sequence itself: what campaign.get answers from, and campaign.enroll's live-state recovery read."
  },
  "GET /v3/sequences/{id}/contacts/{contact_id}": {
    "kind": "read",
    "note": "Whether this person already takes part in the sequence: campaign.enroll's first recovery read."
  },
  "GET /v3/contacts/{id}/statuses": {
    "kind": "read",
    "note": "The opt-out register and, by the same call, whether a pinned contact still resolves. No address travels."
  },
  "GET /v3/contacts/{id}/lists": {
    "kind": "read",
    "note": "Which lists hold this person: list_membership.add's recovery read on a repeated attempt."
  },
  "POST /v3/contacts/import": {
    "kind": "write",
    "note": "Ensures a contact by email in one call, matching or creating. A write: it can create a person."
  },
  "POST /v3/contacts": {
    "kind": "write",
    "note": "Ensures a contact that has no first name to import with. A write, and one with no documented duplicate code."
  },
  "POST /v3/contact-lists/{id}/add-contacts": {
    "kind": "write",
    "note": "Adds the person to the list. Re-adding is documented nowhere, which is why the recovery read exists."
  },
  "POST /v3/sequences/{id}/contact-links/bulk": {
    "kind": "write",
    "note": "Enrols the person. Into a live sequence this is a send, so it is the most expensive repeat of all."
  }
};

// The table itself. `shared` holds the rows every one of the three operations declares; `per_operation` holds the
// rows one operation declares and another does not — the three documents do not publish the same failure codes,
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
      "when": "The CLI exited 2, which is its usage error, decided before any request was made.",
      "code": "provider_call_failed",
      "class": "permanent",
      "note": "The Reply CLI refused the call before making it. Nothing reached the account, and a repeat would build the same call, so this is reported rather than retried."
    },
    "answer_lost_on_a_read": {
      "when": "A call marked `read` timed out, answered 5xx, or exited 1 with nothing on stdout that could be parsed.",
      "code": "provider_unavailable",
      "class": "transient",
      "note": "Reply could not be reached, or did not answer, on a call that changes nothing. Asking again costs nothing, so the attempt is simply repeated."
    },
    "answer_lost_on_a_write": {
      "when": "A call marked `write` timed out, answered 5xx, or exited 1 with nothing on stdout that could be parsed.",
      "code": "provider_answer_lost",
      "class": "ambiguous",
      "note": "A write was sent to Reply and no answer came back, so it may already have happened. The CLI's exit codes cannot tell a request that was never sent from one whose answer was lost, and repeating a write can create a person twice or send to them twice — so the work item stops here for a person rather than guessing."
    },
    "unauthorized": {
      "when": "401, whose body Reply leaves empty, or 403 on any call.",
      "code": "unauthorized",
      "class": "permanent",
      "note": "The profile this route names is not signed in, or its key does not carry the scope this call needs. Sign in with `reply auth login` for that profile, or issue a key with the scope; no retry can help."
    },
    "rate_limited": {
      "when": "429 on any call.",
      "code": "rate_limited",
      "class": "transient",
      "note": "Reply asked for this call to be made later. The runtime waits and repeats the attempt."
    },
    "refusal_this_version_has_no_word_for": {
      "when": "Any other status, or a business code no row below names — the bulk-enrol error set is published as open, so there will be some.",
      "code": "provider_call_failed",
      "class": "permanent",
      "note": "Reply refused the call in a word this version of the package does not know; the status and Reply's own code travel in the failure's details. Nothing was done, so the attempt is not repeated — and a refusal that turns up often enough belongs in this table."
    }
  },
  "per_operation": {
    "campaign.get": {
      "campaign_not_found": {
        "when": "404 on `GET /v3/sequences/{id}`, whose path names exactly one sequence — Reply's own code there is `sequence.notFound` — or an identifier that is not a Reply sequence number at all, which is refused without a call.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      }
    },
    "list_membership.add": {
      "list_not_found": {
        "when": "404 on `POST /v3/contact-lists/{id}/add-contacts`.",
        "code": "list_not_found",
        "class": "permanent",
        "note": "This account holds no contact list under `args.list.external_id`. Correct the identifier the work item carries; nothing about repeating the call can change it."
      },
      "contact_not_found": {
        "when": "404 with code `contact.notFound` on `GET /v3/contacts/{id}/statuses`, read by the pin Jason holds.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before this person can be worked again."
      },
      "suppressed": {
        "when": "`isOptedOut` is true in `GET /v3/contacts/{id}/statuses`.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply's own opt-out register holds this person, so they are not added to an outreach list. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "invalid_channel_value": {
        "when": "400 carrying `errors[]` and no `code` on the ensure-contact call, where a pointer names the address.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "Reply will not accept the address this person carries on the channel the call named. The contact's data has to be corrected; no later attempt would be accepted either."
      },
      "limit_reached": {
        "when": "400 with code `contactLimitExceeded` on the ensure-contact call.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact. A list's own capacity, unlike the account's, publishes no code of its own — a refusal there arrives as an ordinary business 400 and is reported as a refusal this version has no word for, rather than guessed into this row."
      }
    },
    "campaign.enroll": {
      "campaign_not_found": {
        "when": "404 with code `sequence.notFound` on `GET /v3/sequences/{id}`.",
        "code": "campaign_not_found",
        "class": "permanent",
        "note": "This account holds no sequence under the identifier the call carried. The pin is stale or the sequence was deleted; no later attempt will find it."
      },
      "contact_not_found": {
        "when": "404 with code `contact.notFound` on `GET /v3/contacts/{id}/statuses`, read by the pin Jason holds.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before this person can be worked again."
      },
      "campaign_not_enrollable": {
        "when": "400 with code `sequence.archived`, or the live-state read answers a sequence that is archived.",
        "code": "campaign_not_enrollable",
        "class": "permanent",
        "note": "The sequence will not take an enrollment: it is archived. Un-archiving it at Reply is what makes the work item runnable, so it fails rather than waiting."
      },
      "collision_refused": {
        "when": "`args.collision` is `refuse` and the participation read answers a participation that already exists.",
        "code": "collision_refused",
        "class": "permanent",
        "note": "This person already takes part in the sequence and the call asked to be refused rather than to decide. Choose another collision policy if a second participation is what was meant."
      },
      "suppressed": {
        "when": "`isOptedOut` is true in `GET /v3/contacts/{id}/statuses`.",
        "code": "suppressed",
        "class": "permanent",
        "note": "Reply's own opt-out register holds this person, so they are not enrolled. This is a refusal on purpose, and the only way past it is the person asking to be reinstated."
      },
      "invalid_channel_value": {
        "when": "400 carrying `errors[]` and no `code` on the ensure-contact call, where a pointer names the address.",
        "code": "invalid_channel_value",
        "class": "validation",
        "note": "Reply will not accept the address this person carries on the channel the call named. The contact's data has to be corrected; no later attempt would be accepted either."
      },
      "limit_reached": {
        "when": "400 with code `contactLimitExceeded`, or that same name against this person inside the bulk enrol's `notProcessed`.",
        "code": "limit_reached",
        "class": "permanent",
        "note": "The account will hold no further contact or enrollment. The sequence's own capacity, unlike the account's, publishes no code of its own — a refusal there arrives as an ordinary business 400 and is reported as a refusal this version has no word for, rather than guessed into this row."
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

// Which entry of CALLS a concrete call is, by matching the path against the templates segment by segment. The
// path is the one the package built, so this is a lookup rather than a parse; a call the table does not hold is
// an omission in this file and says so.
export function callOf(method, path) {
  const segments = String(path).split("?")[0].split("/");

  for (const call of Object.keys(CALLS)) {
    const space = call.indexOf(" ");
    if (call.slice(0, space) !== method) {
      continue;
    }

    const template = call.slice(space + 1).split("/");
    if (template.length === segments.length && template.every((part, at) => part.startsWith("{") || part === segments[at])) {
      return call;
    }
  }

  return null;
}

// Read or write, for the one decision that turns on it. A call this package does not declare gets no default:
// treating an unmarked write as a read is exactly the mistake that ends in a double send.
export function kindOf(call) {
  return call !== null && Object.prototype.hasOwnProperty.call(CALLS, call) ? CALLS[call].kind : null;
}

// Which lost-answer row an ending on this call belongs in — the whole point of the read/write mark. Only a call
// positively marked a read is treated as one: a call missing from the table above is a gap in this file, and the
// expensive side is the safe side, so it stops for a person rather than being retried into a second send.
export function lostAnswerRow(call) {
  return kindOf(call) === "read" ? "answer_lost_on_a_read" : "answer_lost_on_a_write";
}

// Which shared row a status Reply answered with belongs in. Only the statuses that mean the same thing on every
// call are decided here: a business refusal carries a code whose meaning depends on what was asked, so the
// operation that made the call reads that itself against its own rows and falls back to this for the rest.
export function statusRow(status, call) {
  if (status === 401 || status === 403) {
    return "unauthorized";
  }

  if (status === 429) {
    return "rate_limited";
  }

  if (status >= 500) {
    // A failure on Reply's own side is the same ending as an answer that never arrived, and it is the read/write
    // mark that decides what that costs: nothing on a read, possibly a person's email on a write.
    return lostAnswerRow(call);
  }

  return "refusal_this_version_has_no_word_for";
}

// The one way this package reports a failure. `operation` is the canonical operation being performed, or null for
// an ending the CLI itself produced, which every operation shares; `observed` names the row and what was seen;
// `learned` is what the package now knows Reply calls our entities, which travels out with the failure because
// after a lost answer a pin is the only trace of what was done.
export function fail(operation, observed, learned) {
  const row = rowFor(operation, observed.row);
  const details = compact({
    call: observed.call,
    status: observed.status,
    provider_code: observed.provider_code,
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
  if (own !== undefined && Object.prototype.hasOwnProperty.call(own, key)) {
    return own[key];
  }

  if (Object.prototype.hasOwnProperty.call(ROWS.shared, key)) {
    return ROWS.shared[key];
  }

  // A row this package named and did not write is a defect in this package, not an answer from Reply. It is
  // reported as itself rather than folded into a neighbouring row, so it cannot be mistaken for a provider's
  // refusal by whoever reads the attempt.
  throw host.fail({
    class: "permanent",
    code: "provider_call_failed",
    message: "This plugin has no error row named '" + key + "'" + (operation ? " for " + operation : "") + ".",
  });
}
