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
// a write that may make the person — `find_or_create` — names one Reply does not hold by their fields, for the CLI to
// import; a stop creates nobody.
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
 * How a write that may create somebody — a list add, an enrolment, an opt-out — names the person to the CLI: the pin;
 * or the identifier the exact lookup finds; or, when Reply holds nobody under the value, the person's fields on stdin,
 * with a first name derived from what Jason holds where they have none. A person Reply holds is named by that
 * identifier, so nothing derived, and nothing at all, reaches their record.
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
 * address whatever its case, a profile link by `keyOf`. Two values the register keys apart are two people here, and a
 * value this package cannot key as the runtime does is nobody's.
 *
 * Only the value Reply holds is keyed. The one the work item consumes is already the runtime's key — it was stored so,
 * and the register is asked about it exactly as it stands — and keying it again can make it another: one trailing slash
 * more is dropped each time, so a profile kept as `…/marta-example/` would be taken for the `…/marta-example` a stop may
 * be recorded under.
 */
export function sameValue(channel, held, consumed) {
  if (channel !== "linkedin") {
    return held.toLowerCase() === consumed.toLowerCase();
  }

  const key = keyOf(held);
  return key !== null && key === consumed;
}

/**
 * Whether two values Reply wrote are one address: an email whatever its case, and a profile link by `keyOf` on both sides,
 * since neither is the runtime's stored key. A value this package cannot key is nobody's, so it matches nothing.
 */
export function sameAddress(channel, one, other) {
  if (channel !== "linkedin") {
    return one.toLowerCase() === other.toLowerCase();
  }

  const key = keyOf(one);
  return key !== null && key === keyOf(other);
}

/**
 * Whether a profile can be written onto Reply's contact and stay the one the work item consumes. Reply keys a profile it
 * is given as the runtime does, so what it would hold is the key of the value written; where that is not the value
 * itself, the contact would carry a profile the register keys apart from the one it was asked about.
 */
export function writtenAsItIs(consumed) {
  return keyOf(consumed) === consumed;
}

// A link's parts: its scheme, its authority, its path, its query and its fragment. A backslash anywhere before the
// fragment is no link this package keys: the runtime reads it as a slash.
const LINK = /^([A-Za-z][A-Za-z0-9+.\-]*):\/\/([^\/?#\\]*)([^?#\\]*)(\?[^#\\]*)?(#.*)?$/;

// The port a scheme names by saying nothing.
const DEFAULT_PORT = { "http": 80, "https": 443 };

// The ASCII characters the runtime writes as an escape in a path or a query, beside every control character and every
// one outside ASCII.
const ESCAPED = " \"<>^`{|}";

// The ASCII characters an escape of which the runtime writes as the character itself.
const UNRESERVED = /^[A-Za-z0-9\-._~]$/;

// The characters the runtime drops from a link before it keys it, which this package does not try to follow: a value
// carrying one is nobody's here.
const DROPPED = /[‎‏‪-‮⁦-⁩]/;

/**
 * A profile link as the runtime keys one: its scheme and its host whatever their case, a default port, a user name, a
 * fragment and one trailing slash dropped, its path and its query written with the escapes the runtime writes — every
 * character outside ASCII as the bytes of its UTF-8 in upper-case hexadecimal, as the runtime writes the escape of one
 * too, an escape of a letter, a digit or `-._~` as the character itself, a space and `"<>^`{|}` and each control
 * character escaped, and every other escape as it was written — and its path
 * whatever its case on linkedin.com and its subdomains, where a profile link names a person by its slug, and exactly as
 * written on any other host. `/in/Jane` and `/in/jane` are one profile on linkedin.com, and two anywhere else. Null for
 * a value the runtime would key differently from anything written here — not an http or https link, a host outside
 * ASCII, a backslash, a step to `.` or `..`, a `%` that starts no escape, after which the runtime treats the escapes
 * that follow in ways no one rule describes — so that such a value is never taken for the person.
 */
export function keyOf(link) {
  const parts = typeof link === "string" ? LINK.exec(link.trim()) : null;
  if (parts === null || DROPPED.test(link)) {
    return null;
  }

  const scheme = parts[1].toLowerCase();
  if (!Object.prototype.hasOwnProperty.call(DEFAULT_PORT, scheme)) {
    return null;
  }

  const authority = parts[2].slice(parts[2].lastIndexOf("@") + 1).toLowerCase();
  const colon = authority.lastIndexOf(":");
  const host = colon >= 0 && authority.indexOf("]") < colon ? authority.slice(0, colon) : authority;
  const port = host.length < authority.length ? authority.slice(host.length + 1) : "";
  if (host.length === 0 || /[^\x21-\x7E]/.test(host) || (port.length > 0 && !/^[0-9]{1,5}$/.test(port))) {
    return null;
  }

  const number = port.length > 0 ? Number(port) : DEFAULT_PORT[scheme];
  const kept = number !== DEFAULT_PORT[scheme] ? ":" + String(number) : "";

  const written = escapedAsTheRuntimeWrites(parts[3].length > 0 ? parts[3] : "/");
  const query = parts[4] === undefined ? "" : escapedAsTheRuntimeWrites(parts[4]);
  if (written === null || query === null || query === "?" || written.split("/").some(step => step === "." || step === "..")) {
    return null;
  }

  const trimmed = written.charAt(written.length - 1) === "/" ? written.slice(0, -1) : written;
  const path = host === "linkedin.com" || host.endsWith(".linkedin.com") ? trimmed.toLowerCase() : trimmed;
  return scheme + "://" + host + kept + path + query;
}

// A path or a query with the escapes the runtime writes, or null for a character this package does not key.
function escapedAsTheRuntimeWrites(text) {
  let out = "";
  for (let at = 0; at < text.length;) {
    if (text.charAt(at) === "%") {
      const run = escapeAt(text, at);
      if (run === null) {
        return null;
      }

      out += run.text;
      at = run.next;
      continue;
    }

    const point = text.codePointAt(at);
    if (point >= 0xD800 && point <= 0xDFFF) {
      // Half of a pair, which is no character at all.
      return null;
    }

    const character = String.fromCodePoint(point);
    at += character.length;
    out += point > 0x7E || point < 0x21 || ESCAPED.indexOf(character) >= 0 ? utf8Escapes(character) : character;
  }

  return out;
}

// What one escape starting at `at` becomes: a letter, a digit or `-._~` decoded; a whole UTF-8 sequence of escapes
// written again in upper case; and any other escape as it was written. Null for a `%` that starts no escape.
function escapeAt(text, at) {
  const first = byteAt(text, at);
  if (first === null) {
    return null;
  }

  if (first < 0x80) {
    const character = String.fromCharCode(first);
    return { text: UNRESERVED.test(character) ? character : text.slice(at, at + 3), next: at + 3 };
  }

  const length = first >= 0xC2 && first <= 0xDF ? 2 : first >= 0xE0 && first <= 0xEF ? 3 : first >= 0xF0 && first <= 0xF4 ? 4 : 0;
  const bytes = [first];
  for (let more = 1; more < length; more++) {
    const next = byteAt(text, at + 3 * more);
    if (next === null || next < 0x80 || next > 0xBF) {
      return { text: text.slice(at, at + 3), next: at + 3 };
    }

    bytes.push(next);
  }

  // The second byte's range keeps out an overlong form, a surrogate and a code point past the last one.
  const second = bytes[1];
  const valid = length > 0
    && !(first === 0xE0 && second < 0xA0) && !(first === 0xED && second > 0x9F)
    && !(first === 0xF0 && second < 0x90) && !(first === 0xF4 && second > 0x8F);
  if (!valid) {
    return { text: text.slice(at, at + 3), next: at + 3 };
  }

  return { text: bytes.map(hex).join(""), next: at + 3 * length };
}

// The byte one escape at `at` names, or null where `at` starts no escape.
function byteAt(text, at) {
  const digits = text.slice(at + 1, at + 3);
  return text.charAt(at) === "%" && /^[0-9A-Fa-f]{2}$/.test(digits) ? parseInt(digits, 16) : null;
}

// One character as the escapes of its UTF-8 bytes.
function utf8Escapes(character) {
  const point = character.codePointAt(0);
  const bytes = point < 0x80
    ? [point]
    : point < 0x800
      ? [0xC0 | (point >> 6), 0x80 | (point & 0x3F)]
      : point < 0x10000
        ? [0xE0 | (point >> 12), 0x80 | ((point >> 6) & 0x3F), 0x80 | (point & 0x3F)]
        : [0xF0 | (point >> 18), 0x80 | ((point >> 12) & 0x3F), 0x80 | ((point >> 6) & 0x3F), 0x80 | (point & 0x3F)];
  return bytes.map(hex).join("");
}

function hex(byte) {
  return "%" + (byte < 0x10 ? "0" : "") + byte.toString(16).toUpperCase();
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
