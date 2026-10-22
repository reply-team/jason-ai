// `contact.update`: change a person's fields and custom fields on Reply's contact for them.
//
// The person is found first — by the pin, or by `reply contact find`, Reply's exact lookup of the address (contacts.js)
// — and then one command: `reply contact update <id> --fields -` for a person Reply holds, with exactly the fields and the
// custom fields the arguments name on stdin; or, for a person it holds nobody for, `reply contact update --contact -
// --fields -`, which imports them from their own fields, as an add would, and then sets the arguments' fields on the
// contact made — or on the one the import matched, where Reply's lookup did not return a contact it held, as an add acts
// on one — the person and the fields one object on stdin. A null clears a field, and a field left out stays as
// Reply holds it, or as the import made it. A custom field is named as the account names it; the CLI matches the name
// whatever its case and refuses one the account has none of before anything is written or imported. Setting the same
// values twice leaves the same record, so a repeat after a lost answer works by the contact the failure pinned, or by
// the one the lookup then finds, and simply runs the command again.
//
// The values are the planner's, and they travel on stdin and nowhere else: no field value ever sits in an argument.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, refuseUnprojected, toEnsure } from "./contacts.js";

const OPERATION = "contact.update";
const COMMAND = "contact update";

export function contactUpdate(input, context) {
  refuseUnprojected(OPERATION, input);
  const contact = input.contacts[0];
  const fields = fieldsOf(input.args);

  const named = toEnsure(OPERATION, context, input, undefined);
  const imports = named.stdin !== undefined;
  const done = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: imports ? [] : [named.learned.contact],
    flags: imports ? named.flags.concat(["--fields", "-"]) : ["--fields", "-"],
    stdin: imports ? { contact: named.stdin, fields: fields } : fields,
    learned: named.learned,
    // A contact found by the lookup, or made or matched by the import, that Reply then holds nothing under is no stale
    // pin, and pins nothing.
    gone: named.pinned ? undefined : "contact_gone",
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
