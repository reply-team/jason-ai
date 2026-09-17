// `list_membership.add`: put one person on one of the account's contact lists.
//
// Four things happen, in this order and for a reason. The provider's own contact is ensured, because a Reply
// list holds Reply's contacts. On any attempt after the first the declared recovery read is made — and it comes
// before everything else, because the document says to answer from that reading when the effect already
// happened: a person can opt out between two attempts, and reporting a refusal for an add that already
// succeeded would be false. Only where there is still something to write is Reply's opt-out register read, by
// that contact's identifier, because a person who asked not to be contacted must not be put on an outreach list
// and only the provider knows. Then, and only then, the add.
//
// The answer's vocabulary is the output schema's, which is closed: `added` or `already_member`, one item naming
// the contact the call was given, and no `vendor` bag for anything of Reply's to ride along in.
import { call } from "./cli.js";
import { callOf, describe, fail, lostAnswerRow, observe, refusalRow } from "./errors.js";
import { ADDRESS_FIELD, ensureContact, refuseIfSuppressed } from "./contacts.js";

const OPERATION = "list_membership.add";

// Reply numbers its contact lists with a positive 32-bit integer, so this is what the planner's identifier has
// to look like before there is anything worth asking about.
const LIST_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_LIST_NUMBER = 2147483647;

// How much of the list the recovery read asks for: a thousand people, which is the largest page Reply's own
// search publishes. One page and no more, because every call this package makes is a process of its own and the
// budget an operation has is spent a call at a time — the alternative, walking a list of any length, is a read
// whose cost is the account's and not this operation's.
const PAGE = 1000;

export function listMembershipAdd(input, context) {
  const contact = input.contacts[0];
  const list = listOf(input);

  // The precondition: the provider's own contact, worked by the pin where there is one and ensured from the
  // channel value only where there is not.
  // Nothing is known of Reply's own names for our entities yet: this operation pins only the contact, and the
  // list is the planner's own identifier rather than something this call learned.
  const ensured = ensureContact(OPERATION, context, input, undefined);

  // What this package now knows Reply calls this person. It travels out with a failure as well as with an
  // answer, because after a lost answer the pin is the only trace that the contact was ensured at all — and
  // without it the next attempt has nothing to read the membership by.
  const learned = { contact: ensured };

  // The recovery read the document declares, made as a call rather than reasoned about, and made before
  // anything is weighed against writing: when the account already holds this membership the document's answer
  // is that reading. There is no ledger under the idempotency key at Reply, so it cannot tell this work item's
  // own earlier write from somebody else's addition of the same person; it answers what the account holds now,
  // which is the most the provider can be asked. That is a limitation of this version and not papered over.
  if (context && context.attempt_number > 1 && holds(context, list, ensured, learned)) {
    return membership(contact.id, "already_member", ensured);
  }

  // Only now, with a write still to make: an opt-out that arrived after the add already landed would otherwise
  // turn a finished piece of work into a permanent failure.
  refuseIfSuppressed(OPERATION, context, ensured, learned);

  add(context, list, ensured, learned);
  return membership(contact.id, "added", ensured);
}

// Which list this call is about. Reply numbers its lists, so a value that is not a list number names nothing
// this account could hold under any circumstance — the permanent absence the document has a word for, with
// nothing to ask the provider about and therefore nothing asked.
function listOf(input) {
  const named = input.args.list.external_id;

  if (typeof named !== "string" || !LIST_NUMBER.test(named) || Number(named) > LARGEST_LIST_NUMBER) {
    fail(OPERATION, { call: null, row: "list_not_found" }, undefined);
  }

  return named;
}

/**
 * Whether this person is already on the list, read from the list rather than from the person.
 *
 * Reply publishes a read from the person's side — `GET /v3/contacts/{id}/lists` — and against a real account it
 * answers `[]` for a list that is not shared, whatever the account holds. So the read the document's obligation
 * is performed with is the search over the list itself: it answered at once for the same private list, and the
 * person is on it when their identifier is among the people it returns. Nothing about them travels to ask —
 * neither the address nor anything else they carry, only the two numbers — and the answer is exact, because a
 * match is on Reply's own identifier for this contact and never on a resemblance.
 *
 * The cost of reading from that side is that the list is paged, and a list can be longer than one attempt may
 * read. That ending has a row of its own rather than being rounded to "not a member".
 */
function holds(context, list, contactId, learned) {
  const path = "/v3/contacts/filter?top=" + PAGE;
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, { listId: Number(list) }, learned);

  if (answer.code !== 200) {
    // A list this account does not hold is not recognised here — the path names no list, so a refusal is about
    // the search — and it does not have to be: the add that follows names the list in its own path and reports
    // that absence itself.
    fail(OPERATION, observe(known, answer, refusalRow(answer, known, ADDRESS_FIELD)), learned);
  }

  const read = answer.data !== null && typeof answer.data === "object" && !Array.isArray(answer.data)
    ? answer.data
    : null;
  const people = read === null ? null : read.items;
  if (!Array.isArray(people)) {
    // Nothing was written yet, and this call changes nothing, so an answer that cannot be read is simply asked
    // again. Reading silence as "not a member" would turn the reading into a second write.
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  // Reply's own identifier for this person, against the identifiers the list answered with. An exact match on a
  // number the provider issued, never a resemblance: there is no way for this to find the wrong person.
  const wanted = Number(contactId);
  if (people.some(entry => entry !== null && typeof entry === "object" && entry.id === wanted)) {
    return true;
  }

  if (read.hasMore === true) {
    // The list is longer than the largest page Reply will answer with, and this person was not in it. There is
    // no call that asks about one person, so the reading cannot be finished inside this attempt — and what
    // follows is the add, which at this provider leaves one membership whether or not one was there already.
    // It is said out loud rather than passed over: the answer will be `added`, and that is exactly what this
    // attempt did, but it is not evidence that the person was not already on the list.
    host.log("warn", "The recovery read could not be finished: this list holds more people than one page of it.", {
      call: known,
      list: list,
      read: PAGE,
    });
  }

  return false;
}

// The add itself. The identifiers go in the body, so nothing about this person reaches an argument.
function add(context, list, contactId, learned) {
  const path = "/v3/contact-lists/" + list + "/add-contacts";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, { contactIds: [Number(contactId)] }, learned);

  if (answer.code === 404) {
    // The path names the list and the body names the contact, and the contact was resolved two calls ago, so a
    // 404 here is about the list.
    fail(OPERATION, observe(known, answer, "list_not_found"), learned);
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, refusalRow(answer, known, ADDRESS_FIELD)), learned);
  }

  const failures = answer.data;
  if (failures === null || typeof failures !== "object" || Array.isArray(failures)) {
    // The write went out and what came back does not say who it took. This call is a write, so the read/write
    // mark makes that the expensive ending it is: the next attempt reads the membership before it writes again.
    const seen = observe(known, answer, lostAnswerRow(known));
    seen.provider_item = describe(failures);
    fail(OPERATION, seen, learned);
  }

  if (Object.prototype.hasOwnProperty.call(failures, contactId)) {
    // The answer is a failures-only dictionary: an identifier that is absent succeeded. What the value against
    // a present one is, Reply's own documentation gives two incompatible answers to — a prose table says
    // `8 | ContactNotProcessed`, the schema says a camelCase string — so the key's presence is the failure and
    // the value is only ever reported. Branching on it would be branching on whichever of the two was read.
    const seen = observe(known, answer, "refusal_this_version_has_no_word_for");
    seen.provider_item = describe(failures[contactId]);
    fail(OPERATION, seen, learned);
  }
}

// One item per person, with the reason — a partial success reported as a single verdict is a defect. The
// `contact_id` is the identifier the call was given, so the caller can match the answer to the person; Reply's
// own travels beside it, in the two places the contract names and which are not copies of each other.
function membership(contactId, status, providerContact) {
  return {
    result: { items: [{ contact_id: contactId, status: status, external_ids: { contact: providerContact } }] },
    external_ids: { contact: providerContact },
  };
}
