// `contact.update`: change a person's fields and custom fields on Reply's contact for them.
//
// The person is found first — by the pin, or by `reply contact find`, Reply's exact lookup of the address (contacts.js)
// — and nobody is made: an update changes a contact Reply holds, and a person it holds nobody for is enrolled or put on
// a list first, by the operations that put a person somewhere. Then one command, `reply contact update <id> --fields -`, with exactly the fields and the custom fields the
// arguments name on stdin: a null clears one, and a field left out stays as Reply holds it. A custom field is named as
// the account names it; the CLI matches the name whatever its case and refuses one the account has none of before
// anything is written. Setting the same values twice leaves the same record, so a repeat after a lost answer works by
// the contact the failure pinned and simply runs the command again.
//
// The values are the planner's, and they travel on stdin and nowhere else: no field value ever sits in an argument.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toFind } from "./contacts.js";

const OPERATION = "contact.update";
const COMMAND = "contact update";

export function contactUpdate(input, context) {
  const contact = input.contacts[0];
  const fields = fieldsOf(input.args);

  const named = toFind(OPERATION, context, input, undefined);
  if (named === null) {
    fail(OPERATION, { call: null, row: "nobody_under_the_address" }, undefined);
  }

  const id = named.learned.contact;
  const done = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [id],
    flags: ["--fields", "-"],
    stdin: fields,
    learned: named.learned,
  });

  const provider = contactNumber(done.contact_id);
  if (done.status !== "updated" || provider === null) {
    // The write went out and what came back does not say whom it changed.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(done) }, named.learned);
  }

  return {
    result: { items: [{ contact_id: contact.id, status: "updated", external_ids: { contact: provider } }] },
    external_ids: { contact: provider },
  };
}

// What the CLI is given on stdin: the arguments' fields, each as written, and their custom fields. reply-cli refuses a
// custom field named twice — the same name whatever its case, once trimmed — as a usage error; it is the arguments' to
// correct, so it is refused here first, in the operation's own word, before anything is asked.
function fieldsOf(args) {
  const fields = Object.assign({}, args.fields || {});
  if (Array.isArray(args.custom_fields)) {
    const seen = {};
    for (const entry of args.custom_fields) {
      const name = entry.name.trim().toLowerCase();
      if (Object.prototype.hasOwnProperty.call(seen, name)) {
        fail(OPERATION, { call: null, row: "custom_field_named_twice" }, undefined);
      }

      seen[name] = true;
    }

    fields.custom_fields = args.custom_fields;
  }

  return fields;
}
