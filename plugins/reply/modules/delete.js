// `contact.delete`: delete Reply's contact for one person, with the history Reply keeps for them — they leave every
// sequence and every list there.
//
// By the pin alone, never by an address: a person nobody pinned is `contact_not_linked` before anything is asked, so no
// lookup can aim a delete at somebody else's record. The contact is read first, with `reply contact get`. Reply keeps a
// stop on the contact itself, so one it holds as opted out is refused — deleting it would erase that stop. One it no
// longer holds is `contact_not_found` on a first attempt, and on any later attempt the delete already done: that read is
// what makes a lost answer safe to repeat. Then `reply contact delete`, by the same identifier; Reply answering that it
// holds the contact no longer, right after the read found it, is the delete done as well.
//
// A delete pins nothing — the identifier it worked by names nobody afterwards — so nothing the CLI learned travels out
// with a failure either. Of the record the read brings back, two keys are read and nothing is kept: it is the person's.
import { run } from "./cli.js";
import { describe, fail, lostAnswerRow } from "./errors.js";
import { contactNumber } from "./contacts.js";
import { isReplyNumber } from "./reading.js";

const OPERATION = "contact.delete";
const READ = "contact get";
const COMMAND = "contact delete";

// The CLI's word for an id Reply holds nothing under, which this operation answers itself.
const MISSING = "contact.not_found";

export function contactDelete(input, context) {
  const contact = input.contacts[0];
  const pin = pinOf(contact);
  const repeated = Boolean(context) && Number.isInteger(context.attempt_number) && context.attempt_number > 1;

  const held = run(context, { operation: OPERATION, command: READ, operands: [pin], missing: MISSING, pins: false });
  if (held === null) {
    if (!repeated) {
      fail(OPERATION, { call: READ, row: "contact_not_found" }, undefined);
    }

    return deleted(contact.id, "already_deleted");
  }

  // A read that does not say who it is, or whether Reply holds them as opted out, is no answer: nothing is deleted on
  // a guess, and the read is asked again.
  if (contactNumber(held.id) !== pin || typeof held.isOptedOut !== "boolean") {
    fail(OPERATION, {
      call: READ,
      row: lostAnswerRow(READ),
      provider_item: describe({ id: held.id, isOptedOut: held.isOptedOut }),
    }, undefined);
  }

  if (held.isOptedOut) {
    fail(OPERATION, { call: READ, row: "contact_opted_out" }, undefined);
  }

  const done = run(context, { operation: OPERATION, command: COMMAND, operands: [pin], missing: MISSING, pins: false });
  if (done === null) {
    return deleted(contact.id, "already_deleted");
  }

  if (done.status !== "deleted" || contactNumber(done.contact_id) !== pin) {
    // The delete went out and what came back does not say it took this contact.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: describe({ status: done.status, contact_id: done.contact_id }) }, undefined);
  }

  return deleted(contact.id, "deleted");
}

// The pin, and nothing else, names the contact to delete. None is a person this installation never linked; one that is
// not a Reply contact number names nobody a Reply account could hold, so nothing is asked either way.
function pinOf(contact) {
  const pin = contact.external_ids && contact.external_ids.contact;
  if (pin === undefined || pin === null) {
    fail(OPERATION, { call: null, row: "contact_not_linked" }, undefined);
  }

  if (!isReplyNumber(pin)) {
    fail(OPERATION, { call: null, row: "contact_not_found" }, undefined);
  }

  return pin;
}

// One item for the one person, naming the contact the call was given, and no identifier beside it: a delete pins
// nothing.
function deleted(contactId, status) {
  return { result: { items: [{ contact_id: contactId, status: status }] } };
}
