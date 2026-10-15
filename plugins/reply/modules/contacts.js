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
// them itself, matched by address, so that is the whole of it for a person with a first name, and for a stop, which
// creates nobody. Reply requires a first name for every contact it holds and Jason does not, and making one fit the
// other is this package's job, never the runtime's. Reply's import refuses a person without one even when it already
// holds them, so for a list add, an enrolment or an opt-out of such a person this package looks the address up
// itself, through Reply's contact search, and names the one contact it finds by its identifier: nothing is written to
// a record Reply holds. When Reply holds nobody, the person is imported under a first name derived from what Jason
// holds — their last name, or else their address's text before the `@` — unless the route's binding forbids it for an
// add, or there is nothing to derive one from.
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
 * How a write that may create somebody — a list add, an enrolment, an opt-out — names the person to the CLI: the pin;
 * or the person's fields on stdin; or, for a person with no pin and no first name, the identifier this package's own
 * lookup by address finds; or, when Reply holds nobody there, the person's fields with a first name derived from what
 * Jason holds. Reply requires a first name and Jason does not, and adapting one to the other is this package's job,
 * never the runtime's. A person Reply holds is named by that identifier, so nothing derived can reach their record.
 *
 * `options.protective` marks a write that protects the person rather than reaching them: an opt-out greets nobody, so
 * the route's word against deriving, which exists so that no sequence greets a person by a made-up name, does not
 * apply to it, and refusing it would leave them unflagged at Reply.
 */
export function toEnsure(operation, context, input, learned, options) {
  const protective = Boolean(options && options.protective);
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
  if (found !== null) {
    return { flags: ["--contact-id", found], learned: withContact(learned, found) };
  }

  if (!protective && !derivationAllowed(context)) {
    fail(operation, { call: null, row: "first_name_derivation_forbidden", reason: "the route forbids deriving a first name" }, learned);
  }

  const named = derived(contact, channel);
  if (named === null) {
    fail(operation, { call: null, row: "first_name_not_derivable", reason: "a first name could not be derived" }, learned);
  }

  // Which field the name came from, and never the name: the runtime keeps this line, and its logs hold no person.
  host.log("info", "Reply holds nobody under the address and the person has no first name, so one was derived for the import.", {
    first_name_from: named.source,
  });
  return { flags: ["--contact", "-"], stdin: named.person, learned: learned };
}

/**
 * How a stop names the person: the pin, or the person's fields on stdin for the CLI to look up itself. A stop creates
 * nobody, so it derives nothing.
 */
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

// The first name Reply is given for a person Jason holds none for: their last name, which is then not sent as a last
// name too — the one name they have is their first at Reply, and a second copy of it would say something untrue —
// or else the address's text before its `@`, exactly as spelled. Null when there is neither. The CLI accepts an
// address with exactly one `@` and text on both sides, so for every address it would take, "before the first `@`" and
// "before the last" are the same text.
function derived(contact, channel) {
  const fields = { email: channel.value, company: text(contact.company), title: text(contact.title) };
  const last = text(contact.last_name);
  if (last !== undefined) {
    return { source: "last_name", person: compact(Object.assign(fields, { first_name: last })) };
  }

  const at = channel.value.indexOf("@");
  const local = at > 0 ? text(channel.value.slice(0, at)) : undefined;
  if (local !== undefined) {
    return { source: "email_local_part", person: compact(Object.assign(fields, { first_name: local })) };
  }

  return null;
}

// The route's own word on deriving, from its binding. Only `false` turns it off: the manifest's schema refuses any
// other type when the route is set or activated, so absent is the one other thing that arrives, and absent is the
// default.
function derivationAllowed(context) {
  const binding = (context && context.binding) || {};
  return binding.derive_first_name !== false;
}

// A field as Jason holds it, or undefined when there is nothing in it. "Nothing" is judged as the CLI judges it — by
// JavaScript's own trim — because a field the CLI trims to nothing never reaches Reply, and a first name of spaces
// sent as one is a person Reply refuses where the lookup would have found them. The value itself goes out as given.
function text(value) {
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}
