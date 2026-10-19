// `list_membership.add`: put one person on one of the account's contact lists.
//
// The person is found first — by the pin, or by `reply contact find`, Reply's exact lookup of the address (contacts.js)
// — and then one command, `reply contact-list add-contact`, which imports them only when Reply holds nobody under the
// address, and adds them. Reply keeps one membership however often a person is added, so a repeat after a lost answer
// simply runs the command again. A person Reply holds as opted out is refused rather than added — Jason's own rule, not Reply's: a
// list is an outreach audience, and a Reply list may otherwise hold opted-out people.
//
// The answer's vocabulary is the output schema's, which is closed: `added`, one item naming the contact the call was
// given, and no `vendor` bag. Reply does not say whether the person was on the list already, so `already_member` is
// never answered: the effect at Reply is the same either way.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toEnsure } from "./contacts.js";

const OPERATION = "list_membership.add";
const COMMAND = "contact-list add-contact";

// Reply numbers its contact lists with a positive 32-bit integer, so this is what the planner's identifier has
// to look like before there is anything worth asking about.
const LIST_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_LIST_NUMBER = 2147483647;

export function listMembershipAdd(input, context) {
  const contact = input.contacts[0];
  const list = listOf(input);
  const named = toEnsure(OPERATION, context, input, undefined);

  const added = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [list],
    flags: named.flags.concat(["--refuse-opted-out"]),
    stdin: named.stdin,
    learned: named.learned,
  });

  const provider = contactNumber(added.contact_id);
  if (added.status !== "added" || provider === null) {
    // The write went out and what came back does not say whom it took.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(added) }, named.learned);
  }

  return membership(contact.id, "added", provider);
}

// Which list this call is about. Reply numbers its lists, so a value that is not a list number names nothing this
// account could hold under any circumstance, and nothing is asked.
function listOf(input) {
  const named = input.args.list.external_id;

  if (typeof named !== "string" || !LIST_NUMBER.test(named) || Number(named) > LARGEST_LIST_NUMBER) {
    fail(OPERATION, { call: null, row: "list_not_found" }, undefined);
  }

  return named;
}

// One item for the one person, naming the contact the call was given; Reply's own identifier travels beside it, in
// the two places the contract names.
function membership(contactId, status, providerContact) {
  return {
    result: { items: [{ contact_id: contactId, status: status, external_ids: { contact: providerContact } }] },
    external_ids: { contact: providerContact },
  };
}
