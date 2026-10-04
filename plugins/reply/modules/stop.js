// `enrollment.stop`: take one person out of one sequence, and record why — the why is Jason's, and never reaches
// Reply.
//
// Three things happen, in this order, after a campaign named for the first time has been read to confirm Reply holds
// it. The person is found without creating anybody: by the pin, or by looking the address up — exactly, and whatever
// its case. Somebody Reply does not hold takes part in nothing, which is the
// catalogue's completed stop, and nothing more is asked. Then the participation is read, because a person who takes
// no part, or whose part is finished, is already stopped and nothing is written; that read is also the recovery read
// a lost answer asks for, so a repeat never removes twice. Then the participation is removed — taken out, never
// marked finished, so the person's history stays and no count of people who completed the sequence includes them.
//
// Reply's own opt-out is never read here. A stop is for exactly the people it would refuse.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { findContact } from "./contacts.js";
import { sequenceOf } from "./reading.js";

const OPERATION = "enrollment.stop";

// Reply's words for the 404s a participation read and a removal can meet, lower-cased as they are compared. One
// path names both the sequence and the contact, so the code is what says which was missing.
const NOT_IN_SEQUENCE = "sequencecontact.notinsequence";
const CONTACT_NOT_HELD = "contact.notfound";
const SEQUENCE_NOT_HELD = ["sequence.notfound", "sequencecontact.sequencenotfound"];

export function enrollmentStop(input, context) {
  const contact = input.contacts[0];
  const sequence = sequenceOf(input, OPERATION);
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  if (!pinned) {
    confirmed(context, sequence);
  }

  const learned = { campaign: sequence };

  const found = findContact(OPERATION, context, input, learned);
  if (found === null) {
    // Nobody under this address, read to the end: the person takes part in nothing at Reply.
    return stopped(contact.id, "already_stopped", null, learned);
  }

  learned.contact = found;

  if (!participates(context, sequence, found, learned)) {
    return stopped(contact.id, "already_stopped", found, learned);
  }

  return stopped(contact.id, remove(context, sequence, found, learned), found, learned);
}

// A campaign named for the first time, read before anything is weighed. Reply answers a participation read with one
// 404 for a sequence it does not hold, a contact it does not hold and a person who takes no part, so without this read a
// wrong identifier would read as a completed stop and be pinned for every later operation to aim at. A campaign Jason
// pinned earlier was confirmed when it was pinned, and is not read again.
function confirmed(context, sequence) {
  const path = "/v3/sequences/" + sequence;
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, undefined, OPERATION);

  if (answer.code === 404) {
    fail(OPERATION, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), undefined);
  }

  if (!isObject(answer.data)) {
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), undefined);
  }
}

// Whether this person still takes part. A 404 that says they are not in the sequence is the definitive no — and a
// contact Reply no longer holds answers the same way, which is why a pin that resolves to nobody reads as a
// completed stop. A finished participation is one too. Any other 404 is reported as itself rather than read as a
// no, because reading an unrecognised absence as "already out" would leave a person being written to.
function participates(context, sequence, contactId, learned) {
  const path = "/v3/sequences/" + sequence + "/contacts/" + contactId;
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned, OPERATION);

  if (answer.code === 404) {
    absent(known, answer, learned);
    return false;
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  if (!isObject(answer.data)) {
    // Nothing has been written on this attempt and this call changes nothing, so it is asked again.
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return answer.data.statusInSequence !== "finished";
}

// The removal itself, with no body. A 404 here is the same race the read could have met: the person left between
// the two calls, which is a stop all the same.
function remove(context, sequence, contactId, learned) {
  const path = "/v3/sequences/" + sequence + "/contact-links/" + contactId;
  const known = callOf("DELETE", path);
  const answer = call(context, "DELETE", path, undefined, learned, OPERATION);

  if (answer.code === 404) {
    absent(known, answer, learned);
    return "already_stopped";
  }

  if (answer.code !== 200 && answer.code !== 204) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  return "stopped";
}

// What a 404 on a path naming both the sequence and the person says. It returns only for the two that mean the
// person takes no part; a missing sequence and a word this version does not know end the attempt.
function absent(known, answer, learned) {
  const said = isObject(answer.data) && typeof answer.data.code === "string" ? answer.data.code.toLowerCase() : null;

  if (said === NOT_IN_SEQUENCE || said === CONTACT_NOT_HELD) {
    return;
  }

  if (said !== null && SEQUENCE_NOT_HELD.includes(said)) {
    // The provider has just denied the campaign, so its identifier does not travel out as a pin.
    fail(OPERATION, observe(known, answer, "campaign_not_found"), { contact: learned.contact });
  }

  fail(OPERATION, observe(known, answer, "refusal_this_version_has_no_word_for"), learned);
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

// One item for the one person, naming the contact the call was given. Reply's identifier for them travels beside it
// exactly when Reply holds them; a person it never held has none to report.
function stopped(contactId, status, providerContact, learned) {
  const item = { contact_id: contactId, status: status };
  if (providerContact !== null) {
    item.external_ids = { contact: providerContact };
  }

  return {
    result: { items: [item] },
    external_ids: providerContact === null ? { campaign: learned.campaign } : { contact: providerContact, campaign: learned.campaign },
  };
}
