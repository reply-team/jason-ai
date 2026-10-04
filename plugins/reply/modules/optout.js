// `optout.record`: "they said stop", recorded at the provider — the catalogue's protective floor, which runs without
// anybody approving it because waiting for permission to stop is the harm.
//
// Three things happen, in this order. The person is found: by the pin, or by looking the address up — exactly, and
// whatever its case — and only when Reply holds nobody under it is a contact made, because an opt-out recorded on a
// second contact leaves the person Reply already holds still reachable. Reply's own flag is read, and a person it
// already holds as opted out answers `already_recorded` with nothing written; that read is also the recovery read a
// lost answer asks for, so a repeat never writes twice. Then the flag is set, and only ever set.
//
// What they said, where it arrived and when are Jason's record: the work item carries them, and none of it reaches
// Reply, which holds a yes or a no.
import { call } from "./cli.js";
import { callOf, describe, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { ensureContact, findContact, optedOut } from "./contacts.js";

const OPERATION = "optout.record";

export function optoutRecord(input, context) {
  const contact = input.contacts[0];

  // The person Reply holds, and only when there is none, a contact made from the address.
  const found = findContact(OPERATION, context, input, undefined);
  const ensured = found !== null ? found : ensureContact(OPERATION, context, input, undefined);

  // What this package now knows Reply calls this person. It travels out with a failure as well as with an answer,
  // because after a lost answer the pin is what the next attempt reads the flag by.
  const learned = { contact: ensured };

  if (optedOut(OPERATION, context, ensured, learned).optedOut) {
    return recorded(contact.id, "already_recorded", ensured);
  }

  set(context, ensured, learned);
  return recorded(contact.id, "recorded", ensured);
}

// The flag, set for this one contact. The identifier goes in the body, so nothing about this person reaches an
// argument, and the value is always true: this operation cannot undo an opt-out.
function set(context, contactId, learned) {
  const path = "/v3/contacts/set-opted-out";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, { contactIds: [Number(contactId)], isOptedOut: true }, learned, OPERATION);

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  const failures = answer.data;
  if (failures === null || typeof failures !== "object" || Array.isArray(failures)) {
    // The write went out and what came back does not say whom it took. The next attempt reads the flag first.
    const seen = observe(known, answer, lostAnswerRow(known));
    seen.provider_item = describe(failures);
    fail(OPERATION, seen, learned);
  }

  if (Object.prototype.hasOwnProperty.call(failures, contactId)) {
    // A failures-only dictionary: an identifier that is absent was updated. Reply's word against a present one is
    // read in either of the two shapes its documentation gives, and only `notFound` has a row of its own.
    const failure = failures[contactId];
    const word = typeof failure === "string"
      ? failure
      : failure !== null && typeof failure === "object" && typeof failure.error === "string" ? failure.error : null;
    const seen = observe(known, answer, word === "notFound" ? "contact_not_found" : "refusal_this_version_has_no_word_for");
    seen.provider_item = describe(failure);
    fail(OPERATION, seen, learned);
  }
}

// One item for the one person, naming the contact the call was given; Reply's own identifier travels beside it.
function recorded(contactId, status, providerContact) {
  return {
    result: { items: [{ contact_id: contactId, status: status, external_ids: { contact: providerContact } }] },
    external_ids: { contact: providerContact },
  };
}
