// How a command names the one person an operation is about, and the one lookup this package still makes itself.
//
// A list and a sequence hold Reply's contacts rather than Jason's, so every write about a person names a contact. Two
// things are decided here and nowhere else.
//
// The first is that a pin is worked by and nothing is looked up. After the first link nobody matches this person by
// address again, which is what stops two spellings of one address becoming two people — and it is also why no address
// has to travel anywhere on the ordinary path.
//
// The second is how a person with no pin is named. The CLI takes the person's fields on stdin and finds or imports
// them itself, matched by address, so that is the whole of it for a list add, an enrolment, an opt-out and a stop. The
// one person the CLI cannot import is one without a first name: Reply's import refuses them even when it already
// holds them, and inventing a name would write data nobody supplied into a customer's own records. For a list add or
// an enrolment of such a person this package looks the address up itself, through Reply's contact search, and names
// the one contact it finds by its identifier. Reply holding nobody under the address is a refusal with that reason,
// because nobody can be made for them.
import { call } from "./cli.js";
import { describe, fail, lostAnswerRow, observe, statusRow } from "./errors.js";

// Reply numbers its contacts with a positive 32-bit integer, so this is what a pin has to look like before
// there is anything worth asking about.
const CONTACT_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_CONTACT_NUMBER = 2147483647;

// The contact search, and its name in the table that marks it a read.
const LOOKUP_PATH = "/v3/contacts/filter";
const LOOKUP = "api POST " + LOOKUP_PATH;

// How far a lookup by address reads: five pages of a hundred. Reply's search matches by containment — an address
// that merely holds this one is answered too — so the end has to be reached before a single exact match proves
// there is no second, and a person whose address is held inside five hundred others is a lookup this package
// declines to finish rather than guesses at. Every page is a process of its own inside the operation's budget.
const LOOKUP_PAGE = 100;
const LOOKUP_PAGES = 5;

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
 * How a list add or an enrolment names the person to the CLI, which imports anybody given by their fields: the pin;
 * or the person's fields on stdin; or — for a person with no pin and no first name, whom Reply will not import even
 * when it holds them — the identifier this package's own lookup by address finds. Finding that person is this
 * package's job rather than the CLI's: it is a limit of Reply's business rules, and the answer is Jason's to give.
 */
export function toAdd(operation, context, input, learned) {
  const contact = input.contacts[0];
  const pinned = pinOf(operation, contact, learned);
  if (pinned !== null) {
    return { flags: ["--contact-id", pinned], learned: withContact(learned, pinned) };
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);
  if (text(contact.first_name) !== undefined) {
    return { flags: ["--contact", "-"], stdin: person(contact, channel), learned: learned };
  }

  const found = lookedUp(operation, context, channel.value, learned);
  if (found === null) {
    fail(operation, {
      call: null,
      row: "person_has_no_first_name",
      reason: "The person has no first name, and Reply holds nobody under the address to add instead.",
    }, learned);
  }

  return { flags: ["--contact-id", found], learned: withContact(learned, found) };
}

/** How an opt-out or a stop names the person: the pin, or the person's fields on stdin for the CLI to look up itself. */
export function toFind(operation, input, learned) {
  const contact = input.contacts[0];
  const pinned = pinOf(operation, contact, learned);
  if (pinned !== null) {
    return { flags: ["--contact-id", pinned], learned: withContact(learned, pinned) };
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);
  return { flags: ["--contact", "-"], stdin: person(contact, channel), learned: learned };
}

/** Reply's identifier for a contact, as this package works by it — a string — or null when the value is not one. */
export function contactNumber(value) {
  return typeof value === "number" && Number.isInteger(value) && value > 0 && value <= LARGEST_CONTACT_NUMBER
    ? String(value)
    : null;
}

function withContact(learned, id) {
  return Object.assign({}, learned || {}, { contact: id });
}

// The person as the CLI reads them on stdin: only the fields the projection gives and the CLI takes. `time_zone` is
// never sent: Reply accepts a vocabulary for it that is published nowhere, and an unknown value fails the import.
function person(contact, channel) {
  return compact({
    email: channel.value,
    first_name: text(contact.first_name),
    last_name: text(contact.last_name),
    company: text(contact.company),
    title: text(contact.title),
  });
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

// The one contact Reply holds under this address, read page by page to the end, or null when it holds nobody. The
// search is free text and matches by containment, so it is asked for the address and the comparison here decides:
// exact, and without regard to case, because a live account found the contact whatever the case it was asked in. A
// rule set is never sent — Reply ignored one and answered a page of everybody. The address goes in the body, which is
// stdin.
function lookedUp(operation, context, address, learned) {
  const wanted = address.toLowerCase();
  let found = null;

  for (let page = 0; page < LOOKUP_PAGES; page++) {
    const path = LOOKUP_PATH + "?top=" + LOOKUP_PAGE + "&skip=" + page * LOOKUP_PAGE;
    const answer = call(context, "POST", path, { searchTerm: address }, learned, operation);

    if (answer.code !== 200) {
      fail(operation, observe(LOOKUP, answer, statusRow(answer.code, LOOKUP)), learned);
    }

    const read = answer.data !== null && typeof answer.data === "object" && !Array.isArray(answer.data)
      ? answer.data
      : null;
    if (read === null || !Array.isArray(read.items) || typeof read.hasMore !== "boolean") {
      // Without the items and the word for the end, a page says nothing — and reading silence as "nobody" is how
      // a person gets refused who is there. This call changes nothing, so it is simply asked again.
      fail(operation, observe(LOOKUP, answer, lostAnswerRow(LOOKUP)), learned);
    }

    for (const entry of read.items) {
      if (entry === null || typeof entry !== "object" || typeof entry.email !== "string" || entry.email.toLowerCase() !== wanted) {
        continue;
      }

      const id = contactNumber(entry.id);
      if (id === null) {
        // The person, by the address, under an identifier this package cannot work by. Reported by that identifier
        // alone: the rest of a search answer is a contact's record, and none of it belongs in a failure.
        const seen = observe(LOOKUP, answer, "refusal_this_version_has_no_word_for");
        seen.provider_item = describe(entry.id);
        fail(operation, seen, learned);
      }

      if (found !== null && found !== id) {
        // Two of them. Picking one would be guessing which of them the work is about, so nothing is done; the
        // duplicate is Reply's to resolve.
        fail(operation, { call: LOOKUP, row: "contact_not_unique", status: answer.code }, learned);
      }

      found = id;
    }

    if (read.hasMore === false) {
      return found;
    }
  }

  fail(operation, {
    call: LOOKUP,
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

function text(value) {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}
