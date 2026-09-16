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
      "when": "401, whose body Reply leaves empty, or 403 on any call — and the per-item word `forbidden`, which is how the bulk enrol says the same thing about one person inside a 200.",
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
        "when": "400 with code `contactLimitExceeded`, on the ensure-contact call or on the add itself.",
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
        "when": "404 with code `contact.notFound` on `GET /v3/contacts/{id}/statuses`, read by the pin Jason holds, or the per-item word `contactNotFound` against this person inside the bulk enrol's `notProcessed`.",
        "code": "contact_not_found",
        "class": "permanent",
        "note": "The identifier pinned for this person no longer resolves at Reply — the contact was deleted, or it belongs to another account. The pin has to be cleared before this person can be worked again."
      },
      "campaign_not_enrollable": {
        "when": "The live-state read answers a sequence that is archived, or a 400 on the sequence carries `sequence.archived` or `sequenceContact.noStepsInSequence`.",
        "code": "campaign_not_enrollable",
        "class": "permanent",
        "note": "The sequence will not take an enrollment: it is archived, or it holds no step to send. Un-archiving it at Reply, or giving it a step, is what makes the work item runnable — so it fails rather than waiting."
      },
      "collision_refused": {
        "when": "`args.collision` is `refuse` and the bulk enrol answers the per-item word `contactAlreadyInSequence` against this person. Under `skip` the same word is the successful answer `already_enrolled` instead.",
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
      },
      "call_could_not_be_built": {
        "when": "The arguments name a call this provider cannot be asked to make: a start position no shape of this sequence's steps can answer, or a first-touch timing this version has no word for. Decided before anything is sent.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "The enrollment could not be built from the arguments it was given, so nothing was sent. A Reply step carries no ordering field at all — the steps are a graph of parents with branch labels — so a numbered start position has a sound meaning only while they form a single chain, and the details say which reading failed. Ask for the first step, or name a position the sequence really has."
      },
      "request_rejected_as_invalid": {
        "when": "The bulk enrol answers the per-item word `invalidInput` against this person: Reply read the request and would not take it.",
        "code": "provider_call_failed",
        "class": "permanent",
        "note": "Reply rejected the enrollment request as invalid. A request built wrong is built the same way on every attempt, so it is reported rather than retried, and what Reply said travels in the details."
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

// What was seen on one call, in the shape a failure is reported from. Reply's own code is read only where it is
// a string on an object, because a 401 arrives with an empty body and parsing one is how a plugin fails on the
// answer it meets most often. A caller may add `provider_item` afterwards, for the answers that refuse one
// person inside a 200.
export function observe(call, answer, row) {
  const data = answer.data;
  return {
    call: call,
    row: row,
    status: answer.code,
    provider_code: data !== null && typeof data === "object" && typeof data.code === "string" ? data.code : undefined,
  };
}

// What came back, short enough to travel in a failure's details. A provider's answer is reported rather than
// interpreted wherever this package has no word for it, and 200 characters is enough for an operator to
// recognise it without the details becoming a copy of the body.
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

// Which row a refusal belongs in, for a call whose 400 has a meaning of its own. Reply's 400 is two shapes and
// which member is present says which: a validation problem carries `errors[]` and no `code`, a business
// rejection carries `code` and no `errors[]`. Everything else is decided by the status alone.
//
// The two rows this can name are per-operation ones that only the writes declare, so only a write may ask: a
// `campaign.get` that reached for `invalid_channel_value` would name a code its document does not publish.
export function refusalRow(answer, call, addressField) {
  if (answer.code !== 400 && answer.code !== 422) {
    return statusRow(answer.code, call);
  }

  const data = answer.data !== null && typeof answer.data === "object" ? answer.data : {};
  if (Array.isArray(data.errors)) {
    // A pointer naming the field the channel value went in is the provider saying the value itself is wrong,
    // which no later attempt would accept either. A pointer naming anything else is a request this package
    // built wrong, which is a defect rather than a person's data.
    const named = data.errors.some(entry =>
      entry !== null && typeof entry === "object" && lastSegment(entry.pointer) === addressField);
    return named ? "invalid_channel_value" : "refusal_this_version_has_no_word_for";
  }

  // The account's own contact cap is the one capacity Reply publishes a code for; a list's or a sequence's is
  // not, and guessing one out of an ordinary business refusal is how a full list comes to look like a full
  // account.
  return data.code === "contactLimitExceeded" ? "limit_reached" : "refusal_this_version_has_no_word_for";
}

function lastSegment(pointer) {
  if (typeof pointer !== "string") {
    return null;
  }

  const segments = pointer.split("/");
  return segments[segments.length - 1];
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
    // What a 200 said about this one person, where it refused one inside an answer that reported success. It is
    // reported and never read: the shape of that value is documented two incompatible ways.
    provider_item: observed.provider_item,
    // What this package itself could not do, for the endings that are its own refusal rather than an answer
    // from Reply. An operator reading one of those has no status and no provider code to go on, so the reason
    // is the whole of what they get.
    reason: observed.reason,
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
