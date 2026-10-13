// `enrollment.stop`: take one person out of one sequence, and record why — the why is Jason's, and never reaches
// Reply.
//
// One command, `reply sequence remove-contact`. It finds the person without creating anybody — by the pin, or by the
// address, exactly and whatever its case — reads their part in the sequence, and takes them out only while they take
// part and have not finished: taken out, never marked finished, so their history stays and no count of people who
// completed the sequence includes them. Somebody Reply does not hold, or who takes no part, is already stopped.
//
// A campaign the work item names for the first time is read first when the person is named by address. A person
// Reply holds under no such address takes part in nothing, and the CLI answers that without checking the sequence,
// so without this read a mistyped identifier would read as a completed stop and be pinned for every later operation
// to aim at. A pinned person needs no such read: the CLI reads their part in the sequence and tells a sequence Reply
// does not hold apart itself.
//
// Reply's own opt-out is never read here. A stop is for exactly the people it would refuse.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toFind } from "./contacts.js";
import { sequenceOf } from "./reading.js";

const OPERATION = "enrollment.stop";
const COMMAND = "sequence remove-contact";

export function enrollmentStop(input, context) {
  const contact = input.contacts[0];
  const sequence = sequenceOf(input, OPERATION);
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const learned = { campaign: sequence };
  const named = toFind(OPERATION, input, learned);
  if (!pinned && named.stdin !== undefined) {
    run(context, { operation: OPERATION, command: "sequence get", operands: [sequence] });
  }
  const done = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [sequence],
    flags: named.flags,
    stdin: named.stdin,
    learned: named.learned,
  });

  const provider = done.contact_id === null ? null : contactNumber(done.contact_id);
  if ((done.status !== "removed" && done.status !== "not_in_sequence") || (done.contact_id !== null && provider === null)) {
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(done) }, named.learned);
  }

  return stopped(contact.id, done.status === "removed" ? "stopped" : "already_stopped", provider, learned);
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
