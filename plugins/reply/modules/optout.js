// `optout.record`: "they said stop", recorded at the provider — the catalogue's protective floor, which runs without
// anybody approving it because waiting for permission to stop is the harm.
//
// The person is found first — by the pin, or by `reply contact find`, Reply's exact lookup of the address (contacts.js)
// — and then one command, `reply contact opt-out`, makes a contact only when Reply holds nobody under the address,
// because an opt-out recorded on a second contact leaves the person Reply already holds still reachable, and sets
// Reply's flag, which is only ever set. Setting a flag that is set already changes nothing, so the answer is `recorded`
// either way, and a repeat after a lost answer simply runs the command again.
//
// A person Reply holds nobody for and who has no first name is imported under a derived one (contacts.js). The route's
// `derive_first_name: false` does not apply here: an opt-out greets nobody, and refusing it would leave the person
// unflagged at Reply.
//
// What they said, where it arrived and when are Jason's record: the work item carries them, and none of it reaches
// Reply, which holds a yes or a no.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toEnsure, withOrigin } from "./contacts.js";

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

  return withOrigin({
    result: { items: [{ contact_id: contact.id, status: "recorded", external_ids: { contact: provider } }] },
    external_ids: { contact: provider },
  }, done);
}
