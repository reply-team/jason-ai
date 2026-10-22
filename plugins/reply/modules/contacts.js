// How a command names the one person an operation is about: the contact proxy rule (docs/plugins.md §7) at Reply.
//
// A list and a sequence hold Reply's contacts rather than Jason's, so every write about a person names a contact, and
// this package finds it before the command runs. A pin is worked by, and nothing is looked up: after the first link
// nobody matches this person by address again, which is what stops two spellings of one address becoming two people,
// and it is also why no address has to travel anywhere on the ordinary path.
//
// Without a pin the person is looked up by the value of the channel the operation consumes, with `reply contact find`:
// Reply's exact lookup by email, or by LinkedIn URL, read to its end, two contacts under one address refused rather
// than guessed between. A person Reply holds is named by that identifier, so nothing is written to their record. Only
// a write that may make the person — `find_or_create`: an add, an opt-out, a send, an update — names one Reply does not
// hold by their fields, for the CLI to import with the command that acts on them; a stop creates nobody.
//
// Reply requires a first name for every contact it holds and Jason does not, and making one fit the other is this
// package's job, never the runtime's. Only an import needs one, so a person Reply holds nobody for and who has no first
// name is imported under one derived from what Jason holds — their last name, or else their address's text before the
// `@` — unless the route's binding forbids it for an add, or there is nothing to derive one from.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";

// Reply numbers its contacts with a positive 32-bit integer, so this is what a pin has to look like before
// there is anything worth asking about.
const CONTACT_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_CONTACT_NUMBER = 2147483647;

// The exact lookup, a read: it creates nobody.
const FIND = "contact find";

// The person's own fields, which every document of a write that may create them projects — as null where Jason holds
// nothing in one. A runtime whose documents projected the identifier and the channel alone leaves them out altogether.
const PROJECTED = ["first_name", "last_name", "company", "title", "time_zone"];

// What the CLI takes a person by, for each channel a document may name that Reply can look a person up by: an email,
// or a LinkedIn URL. Any other channel names a value no attempt could look anybody up by.
const ADDRESS = { "email": "email", "linkedin": "linkedin_url" };

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
 * How a write that may create somebody — a list add, an enrolment, an opt-out, a send, an update — names the person to
 * the CLI: the pin, marked `pinned`; or the identifier the exact lookup finds; or, when Reply holds nobody under the
 * value, the person's fields for stdin (`--contact -`), with a first name derived from what Jason holds where they have
 * none. A person the lookup finds is named by that identifier, so nothing derived, and nothing at all, reaches their
 * record. Only a contact Reply's import matches though its lookup did not return it can be touched by the import: Reply
 * fills that contact's empty fields from the ones given.
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
    return { flags: ["--contact-id", pinned], learned: withContact(learned, pinned), pinned: true };
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);
  const found = lookedUp(operation, context, channel, learned);
  if (found !== null) {
    return { flags: ["--contact-id", found], learned: withContact(learned, found) };
  }

  if (text(contact.first_name) !== undefined) {
    return { flags: ["--contact", "-"], stdin: person(contact, channel), learned: learned };
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
 * Refuses an input composed without the person's own fields — absent, which is not the null a runtime that projects
 * them writes for a field Jason holds nothing in — before anything is called. Only a runtime older than 0.14.0, whose
 * documents projected the identifier and the channel alone, composes one, and a person imported from it would be named
 * from their address while Jason holds their name. It is refused in a code those documents already declare,
 * `invalid_channel_value`: a runtime that knew a code of its own for it would never compose such an input. The reason
 * names the cause and the fields lacking, never anything they hold.
 */
export function refuseUnprojected(operation, input) {
  const contact = input.contacts[0];
  const absent = PROJECTED.filter(field => !Object.prototype.hasOwnProperty.call(contact, field));
  if (absent.length > 0) {
    fail(operation, {
      call: null,
      row: "person_not_projected",
      reason: "the runtime that composed this input is older than 0.14.0 and carries no " + absent.join(", ")
        + " for the person, so the person as given cannot be created; update the runtime",
    }, undefined);
  }
}

/**
 * How a write that creates nobody — a stop — names the person: the pin, or the identifier the exact lookup finds; null
 * when Reply holds nobody under the value, which is a person who takes part in nothing there.
 */
export function toFind(operation, context, input, learned) {
  const contact = input.contacts[0];
  const pinned = pinOf(operation, contact, learned);
  if (pinned !== null) {
    return { flags: ["--contact-id", pinned], learned: withContact(learned, pinned) };
  }

  const channel = channelOf(operation, contact, input.args.channel, learned);
  const found = lookedUp(operation, context, channel, learned);
  return found === null ? null : { flags: ["--contact-id", found], learned: withContact(learned, found) };
}

/**
 * Whether a value Reply holds for a person is the one this work item consumes, compared the way the runtime keys the
 * value for its suppression register, so that the register's check at the claim is about exactly the person reached: an
 * address and a profile link alike by `keyOf`, the runtime's own rule. Two values the register keys apart are two people
 * here — two addresses JavaScript would lowercase to one text among them — and a value the runtime would not key, one
 * longer than it stores included, is nobody's.
 *
 * Only the value Reply holds is keyed. The one the work item consumes is the runtime's key already — it was stored so,
 * and the register is asked about it exactly as it stands — so it is compared as it stands: a value stored before the
 * key, which the runtime left as it was, is never the key of anything Reply holds.
 */
export function sameValue(channel, held, consumed) {
  const key = keyOf(channel, held);
  return key !== null && key === consumed;
}

/**
 * Whether two values Reply wrote are one address: an email and a profile link alike by `keyOf` on both sides, since
 * neither is the runtime's stored key. A value the runtime would not key is nobody's, so it matches nothing.
 */
export function sameAddress(channel, one, other) {
  const key = keyOf(channel, one);
  return key !== null && key === keyOf(channel, other);
}

/**
 * Whether a profile can be written onto Reply's contact and stay the one the work item consumes: the value is the
 * runtime's key, which every value the runtime stores is. A value stored before the key, which the runtime left as it
 * was, is not, and is never written: Reply would hold it as a profile the register was not asked about.
 */
export function writtenAsItIs(consumed) {
  return keyOf("linkedin", consumed) === consumed;
}

/**
 * A value on a channel as the runtime keys it — the value its contacts, its register and its claim compare — or null
 * where the runtime would store none. The rule is the runtime's own, asked through `host.channelKey`, so this package
 * never holds a copy of it that could come to differ.
 */
export function keyOf(channel, value) {
  return typeof value === "string" ? host.channelKey(channel, value) : null;
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

// The one contact Reply holds under this value, or null when it holds nobody. The CLI asks Reply's exact lookup, reads
// it to its end and refuses two as `contact.not_unique`; the value goes on stdin, never on the command line. An answer
// that does not say who, or nobody, is not an answer: on a read it is asked again.
function lookedUp(operation, context, channel, learned) {
  const answer = run(context, {
    operation: operation,
    command: FIND,
    operands: [],
    flags: ["--contact", "-"],
    stdin: addressOf(channel),
    learned: learned,
  });

  if (answer.contact_id === null) {
    return null;
  }

  const id = contactNumber(answer.contact_id);
  if (id === null) {
    fail(operation, { call: FIND, row: lostAnswerRow(FIND), provider_item: evidence(answer) }, learned);
  }

  return id;
}

// The person as the CLI reads them on stdin: only the fields the projection gives and the CLI takes, and the value of
// the one channel consumed under the CLI's name for it. `time_zone` is never sent: Reply accepts a vocabulary for it
// that is published nowhere, and an unknown value fails the import.
function person(contact, channel) {
  return compact(Object.assign(addressOf(channel), {
    first_name: text(contact.first_name),
    last_name: text(contact.last_name),
    company: text(contact.company),
    title: text(contact.title),
  }));
}

// The consumed value under the CLI's name for it, the one field every person and every lookup carries first.
function addressOf(channel) {
  const address = {};
  address[ADDRESS[channel.channel]] = channel.value;
  return address;
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

// The one channel this operation consumes. The projection carries exactly that channel and no other, so a person who
// arrives without a value on it — or on a channel Reply looks nobody up by — is a call no later attempt could accept.
function channelOf(operation, contact, name, learned) {
  for (const channel of contact.channels) {
    if (channel.channel === name && typeof channel.value === "string" && channel.value.length > 0
      && Object.prototype.hasOwnProperty.call(ADDRESS, name)) {
      return channel;
    }
  }

  fail(operation, { call: null, row: "invalid_channel_value" }, learned);
}

// The first name Reply is given for a person Jason holds none for: their last name, which is then not sent as a last
// name too — the one name they have is their first at Reply, and a second copy of it would say something untrue —
// or else, for an email, the address's text before its `@`, exactly as spelled. Null when there is neither: a LinkedIn
// URL is no name. The CLI accepts an address with exactly one `@` and text on both sides, so for every address it would
// take, "before the first `@`" and "before the last" are the same text.
function derived(contact, channel) {
  const fields = Object.assign(addressOf(channel), { company: text(contact.company), title: text(contact.title) });
  const last = text(contact.last_name);
  if (last !== undefined) {
    return { source: "last_name", person: compact(Object.assign(fields, { first_name: last })) };
  }

  const at = channel.channel === "email" ? channel.value.indexOf("@") : -1;
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
// sent as one is a person Reply refuses. The value itself goes out as given.
function text(value) {
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}
