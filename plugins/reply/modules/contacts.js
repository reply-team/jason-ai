// The provider's own contact: the precondition every write in this package shares, and the one read that says
// whether Reply will take this person at all.
//
// A list and a sequence hold Reply's contacts rather than Jason's, so nothing can be written for a person until
// Reply holds one. Two things are decided here and nowhere else.
//
// The first is that a pin is worked by and nothing is looked up. After the first link nobody matches this person
// by address again, which is what stops two spellings of one address becoming two people — and it is also why no
// address has to travel anywhere on the ordinary path.
//
// The second is how a person with no pin is found. A write that may create somebody ensures them through Reply's
// import, which deduplicates by email and answers the identifier whether it matched or created, so one call is the
// whole of it and the address rides on stdin. But the import refuses an item that carries no first name, and
// Jason's own projection allows none; inventing one would write data nobody supplied into a customer's own
// records, so a person without one is created instead through the path that needs only an address. That path
// publishes no code for a duplicate address, which is a documented limitation: such a refusal is reported as the
// refusal it is rather than guessed at. An opt-out looks the person up first and creates them only when Reply
// holds nobody under the address — it has to land on the person Reply holds, never on a second contact made for
// them — and a stop only ever looks up, because taking somebody out of a campaign creates nobody.
import { call } from "./cli.js";
import { callOf, describe, fail, lostAnswerRow, observe, refusalRow, statusRow } from "./errors.js";

// Reply numbers its contacts with a positive 32-bit integer, so this is what a pin has to look like before
// there is anything worth asking about.
const CONTACT_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_CONTACT_NUMBER = 2147483647;

// How far a lookup by address reads: five pages of a hundred. Reply's search matches by containment — an address
// that merely holds this one is answered too — so the end has to be reached before a single exact match proves
// there is no second, and a person whose address is held inside five hundred others is a lookup this package
// declines to finish rather than guesses at. Every page is a process of its own inside the operation's budget.
const LOOKUP_PAGE = 100;
const LOOKUP_PAGES = 5;

/**
 * Reply's own name for the field an email channel's value goes in. A validation pointer whose last segment is
 * this one is the provider refusing the address rather than something else the request carried.
 */
export const ADDRESS_FIELD = "email";

// The SDK reads request objects strictly, so a key whose value is undefined is dropped rather than passed on.
function compact(request) {
  const cleaned = {};
  for (const key of Object.keys(request)) {
    if (request[key] !== undefined) {
      cleaned[key] = request[key];
    }
  }

  return cleaned;
}

/**
 * The provider's identifier for the one person this call is about: the pin when Jason holds one, and otherwise
 * the identifier Reply answers with for a contact matched or created from the channel value. Nothing is looked up
 * by address here; an operation that has to find the person first asks `findContact`.
 *
 * `learned` is what the calling operation already knows Reply calls our entities — the campaign, where there is
 * one. It travels out with every failure from here, because this is where the first write of an operation
 * happens and a pin is the only trace a lost answer leaves behind.
 */
export function ensureContact(operation, context, input, learned) {
  const contact = input.contacts[0];
  const pinned = pinOf(operation, contact, learned);

  if (pinned !== null) {
    return pinned;
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);

  // The one rule that decides which path: Reply's import will not take an item without a first name.
  return typeof contact.first_name === "string" && contact.first_name.length > 0
    ? imported(operation, context, contact, channel, learned)
    : created(operation, context, contact, channel, learned);
}

/**
 * The provider's identifier for the one person this call is about, without creating anybody: the pin when Jason
 * holds one, and otherwise the one contact Reply holds under the channel value, compared exactly and without
 * regard to case. `null` when Reply holds nobody under it — which only a lookup read to its end can say.
 *
 * More than one exact match is refused rather than picked from, and a lookup that cannot be read to its end is
 * refused rather than taken for nobody, which would create a second contact, or for one, which would act on
 * whichever person the first pages happened to hold.
 */
export function findContact(operation, context, input, learned) {
  const contact = input.contacts[0];
  const pinned = pinOf(operation, contact, learned);

  if (pinned !== null) {
    return pinned;
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);
  return lookedUp(operation, context, channel.value, learned);
}

/**
 * Whether Reply's own opt-out register holds this person, and by the same call the check that the identifier
 * still resolves. Both by contact id, so no address travels to ask either question.
 */
export function optedOut(operation, context, contactId, learned) {
  const path = "/v3/contacts/" + contactId + "/statuses";
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned, operation);

  if (answer.code === 404) {
    // The path names exactly one contact, so a 404 here is about that contact and nothing else: the pin no
    // longer resolves. It is reported rather than re-matched by address, which is what working by a pin means.
    fail(operation, observe(known, answer, "contact_not_found"), learned);
  }

  if (answer.code !== 200) {
    fail(operation, observe(known, answer, refusalRow(answer, known, ADDRESS_FIELD)), learned);
  }

  const statuses = answer.data !== null && typeof answer.data === "object" ? answer.data : null;
  if (statuses === null || Array.isArray(statuses) || typeof statuses.isOptedOut !== "boolean") {
    // A 200 that does not answer the question asked is not an answer. This call changes nothing, so asking
    // again costs nothing, and inventing "not suppressed" out of silence is the one reading that could put an
    // outreach list in front of somebody who asked not to be on one.
    fail(operation, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return { optedOut: statuses.isOptedOut, call: known, answer: answer };
}

/**
 * Reply's own refusal of this person, carried back in the runtime's vocabulary, and by the same call the check
 * that the pin still resolves.
 */
export function refuseIfSuppressed(operation, context, contactId, learned) {
  const read = optedOut(operation, context, contactId, learned);

  if (read.optedOut) {
    // The provider knows something the runtime cannot: a person suppressed between the claim and the act is the
    // ordinary case. Nothing is re-checked here — the check happened at Reply, and this maps its answer.
    fail(operation, observe(read.call, read.answer, "suppressed"), learned);
  }
}

// The pin Jason holds for this person, or null. A pin is worked by, never interpreted: one that is not a Reply
// contact identifier resolves to nobody, and saying so is better than falling back to the address the pin was
// recorded to replace.
function pinOf(operation, contact, learned) {
  const pinned = contact.external_ids && contact.external_ids.contact;

  if (pinned === undefined || pinned === null) {
    return null;
  }

  if (typeof pinned !== "string" || !CONTACT_NUMBER.test(pinned) || Number(pinned) > LARGEST_CONTACT_NUMBER) {
    fail(operation, { call: null, row: "contact_not_found" }, learned);
  }

  return pinned;
}

// The one contact Reply holds under this address, read page by page to the end. The search is free text and
// matches by containment, so it is asked for the address and the comparison here decides: exact, and without
// regard to case, because a live account found the contact whatever the case it was asked in. A rule set is never
// sent — Reply ignored one and answered a page of everybody. The address goes in the body, which is stdin.
function lookedUp(operation, context, address, learned) {
  const wanted = address.toLowerCase();
  let found = null;

  for (let page = 0; page < LOOKUP_PAGES; page++) {
    const path = "/v3/contacts/filter?top=" + LOOKUP_PAGE + "&skip=" + page * LOOKUP_PAGE;
    const known = callOf("POST", path);
    const answer = call(context, "POST", path, { searchTerm: address }, learned, operation);

    if (answer.code !== 200) {
      fail(operation, observe(known, answer, statusRow(answer.code, known)), learned);
    }

    const read = answer.data !== null && typeof answer.data === "object" && !Array.isArray(answer.data)
      ? answer.data
      : null;
    if (read === null || !Array.isArray(read.items) || typeof read.hasMore !== "boolean") {
      // Without the items and the word for the end, a page says nothing — and reading silence as "nobody" is how
      // a second contact gets made. This call changes nothing, so it is simply asked again.
      fail(operation, observe(known, answer, lostAnswerRow(known)), learned);
    }

    for (const entry of read.items) {
      if (entry === null || typeof entry !== "object" || typeof entry.email !== "string" || entry.email.toLowerCase() !== wanted) {
        continue;
      }

      if (!isContactNumber(entry.id)) {
        // The person, by the address, under an identifier this package cannot work by. Reported by that identifier
        // alone: the rest of a search answer is a contact's record, and none of it belongs in a failure.
        const seen = observe(known, answer, "refusal_this_version_has_no_word_for");
        seen.provider_item = describe(entry.id);
        fail(operation, seen, learned);
      }

      if (found !== null && found !== String(entry.id)) {
        // Two of them. Picking one would be guessing which of them the work is about, so nothing is done; the
        // duplicate is Reply's to resolve.
        fail(operation, { call: known, row: "contact_not_unique", status: answer.code }, learned);
      }

      found = String(entry.id);
    }

    if (read.hasMore === false) {
      return found;
    }
  }

  fail(operation, {
    call: callOf("POST", "/v3/contacts/filter"),
    row: "lookup_not_finished",
    reason: "Reply holds more than " + LOOKUP_PAGE * LOOKUP_PAGES + " contacts whose record holds this address, so the lookup could not be read to its end.",
  }, learned);
}

// The one channel this operation consumes. The projection carries exactly that channel and no other, so a person
// who arrives without a value on it is a call no later attempt could accept either.
function channelOf(operation, contact, name, learned) {
  for (const channel of contact.channels) {
    if (channel.channel === name && typeof channel.value === "string" && channel.value.length > 0) {
      return channel;
    }
  }

  fail(operation, { call: null, row: "invalid_channel_value" }, learned);
}

// One person as Reply's own vocabulary spells them, and only the fields the projection gives us that Reply
// publishes a word for. `time_zone` is deliberately not among them: Reply's `timeZoneId` accepts a vocabulary
// published nowhere and an unrecognised value fails the whole import item, so sending a person's own time zone
// would cost the import rather than improve it.
function item(contact, channel) {
  return compact({
    email: channel.value,
    firstName: text(contact.first_name),
    lastName: text(contact.last_name),
    company: text(contact.company),
    title: text(contact.title),
  });
}

function text(value) {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

// The ordinary path: one call that matches an existing contact by email or creates one, answering the identifier
// either way. A write, because it can create a person.
function imported(operation, context, contact, channel, learned) {
  const path = "/v3/contacts/import";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, { items: [item(contact, channel)] }, learned, operation);

  if (answer.code !== 200) {
    fail(operation, observe(known, answer, refusalRow(answer, known, ADDRESS_FIELD)), learned);
  }

  const items = answer.data !== null && typeof answer.data === "object" ? answer.data.items : undefined;
  const first = Array.isArray(items) && items.length > 0 && items[0] !== null && typeof items[0] === "object"
    ? items[0]
    : null;

  if (first === null || !isContactNumber(first.id)) {
    // The import answers per item, so a 200 can still refuse this person — and it says so in a sentence with no
    // code of its own. The one documented cause is a missing first name, which this path never sends, so there
    // is nothing here to map, and the refusal is reported as itself. The item travels by its identifier alone:
    // the rest of it can say what the person's record holds, and none of that belongs in a failure.
    const seen = observe(known, answer, "refusal_this_version_has_no_word_for");
    seen.provider_item = describe(first === null ? null : first.id);
    fail(operation, seen, learned);
  }

  return String(first.id);
}

// The path for a person Reply's import will not take. It needs only an address — and it answers no code at all
// for one this account already holds, so a duplicate there is an ordinary business refusal and is reported as
// one. Branching on the code would be branching on something nobody published.
function created(operation, context, contact, channel, learned) {
  const path = "/v3/contacts";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, item(contact, channel), learned, operation);

  if (answer.code !== 200 && answer.code !== 201) {
    fail(operation, observe(known, answer, refusalRow(answer, known, ADDRESS_FIELD)), learned);
  }

  const created = answer.data !== null && typeof answer.data === "object" ? answer.data : null;
  if (created === null || Array.isArray(created) || !isContactNumber(created.id)) {
    // A created contact with no identifier is a write that may well have happened and cannot be worked with.
    // The read/write mark decides what that costs, and this call is a write. The answer travels by its identifier
    // alone: the rest of it is the person's record.
    const seen = observe(known, answer, lostAnswerRow(known));
    seen.provider_item = describe(created === null || Array.isArray(created) ? null : created.id);
    fail(operation, seen, learned);
  }

  return String(created.id);
}

function isContactNumber(id) {
  return typeof id === "number" && Number.isInteger(id) && id > 0 && id <= LARGEST_CONTACT_NUMBER;
}
