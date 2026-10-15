// `optout.record`: "they said stop", recorded at the provider — the catalogue's protective floor, which runs without
// anybody approving it because waiting for permission to stop is the harm.
//
// One command, `reply contact opt-out`. It finds the person — by the pin, or by the address, exactly and whatever its
// case — and makes a contact only when Reply holds nobody under it, because an opt-out recorded on a second contact
// leaves the person Reply already holds still reachable. Then it sets Reply's flag, which is only ever set. Setting a
// flag that is set already changes nothing, so the answer is `recorded` either way, and a repeat after a lost answer
// simply runs the command again.
//
// A person with no pin and no first name is looked up by this package first, exactly as a list add does, and imported
// under a derived first name only when Reply holds nobody under the address (contacts.js). The route's
// `derive_first_name: false` does not apply here: an opt-out greets nobody, and refusing it would leave the person
// unflagged at Reply.
//
// What they said, where it arrived and when are Jason's record: the work item carries them, and none of it reaches
// Reply, which holds a yes or a no.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toEnsure } from "./contacts.js";

const OPERATION = "optout.record";
const COMMAND = "contact opt-out";

export function optoutRecord(input, context) {
  const contact = input.contacts[0];
  const named = toEnsure(OPERATION, context, input, undefined, { protective: true });
  const done = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [],
    flags: named.flags,
    stdin: named.stdin,
    learned: named.learned,
  });

  const provider = contactNumber(done.contact_id);
  if (done.status !== "opted_out" || provider === null) {
    // The write went out and what came back does not say whom it took.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(done) }, named.learned);
  }

  return {
    result: { items: [{ contact_id: contact.id, status: "recorded", external_ids: { contact: provider } }] },
    external_ids: { contact: provider },
  };
}
