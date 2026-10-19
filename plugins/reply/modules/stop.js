// `enrollment.stop`: take one person out of one sequence, and record why — the why is Jason's, and never reaches
// Reply.
//
// The person is found without creating anybody — by the pin, or by `reply contact find`, Reply's exact lookup of the
// address (contacts.js) — and then one command, `reply sequence remove-contact`, by Reply's identifier for them: it
// reads their part in the sequence, and takes them out only while they take part and have not finished: taken out,
// never marked finished, so their history stays and no count of people who completed the sequence includes them.
// Somebody who takes no part is already stopped.
//
// Somebody Reply holds nobody for under the address takes part in nothing, and that is answered without a command. A
// campaign the work item names for the first time is read first then, so that a mistyped identifier is not read as a
// completed stop and pinned for every later operation to aim at. A person found needs no such read: the CLI reads
// their part in the sequence and tells a sequence Reply does not hold apart itself.
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
  // A lookup that fails before the sequence is known to be Reply's pins no campaign: the identifier is the planner's.
  const named = toFind(OPERATION, context, input, pinned ? learned : undefined);
  if (named === null) {
    if (!pinned) {
      run(context, { operation: OPERATION, command: "sequence get", operands: [sequence] });
    }

    return stopped(contact.id, "already_stopped", null, learned);
  }

  const done = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [sequence],
    flags: named.flags,
    learned: Object.assign({}, named.learned, learned),
  });

  const provider = done.contact_id === null ? null : contactNumber(done.contact_id);
  if ((done.status !== "removed" && done.status !== "not_in_sequence") || (done.contact_id !== null && provider === null)) {
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(done) }, Object.assign({}, named.learned, learned));
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
