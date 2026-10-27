// What every send outside a sequence does before anything goes out, in this order: the text, the sender, the person and
// Reply's record of them. Each step refuses with nothing sent.
//
// - The text. Reply reads a double opening brace as the start of a placeholder of its own and fills it in for each
//   person, or refuses the send when it cannot, so a final text with one is refused before anything is called: what
//   would go out would not be the text a person approved. A template is Reply's to fill in (template.js).
// - The sender. The arguments name the account by its identifier and by its address, and the address is what the
//   person approving read, so the account is read from `reply account list` and held to it: an account Reply holds
//   under another address — or under one where the arguments say none — is not the one that was approved.
// - The person. By the pin, or else by `reply contact find`, Reply's exact lookup of the consumed value, as every write
//   that may create the person names them (contacts.js): a person Reply holds is named by that identifier, and one it
//   holds nobody for by their fields, which the send's own command imports before it sends.
// - Reply's record of a person it holds, read with `reply contact get`. Reply does not refuse a direct send to a person
//   it holds as opted out, so that read is what keeps the stop Reply holds. The value the send reaches has to be the
//   one the runtime asked its suppression register about: an email the contact's own address, and a LinkedIn profile
//   the contact's own profile — and a contact that carries no profile is given the consumed one, which is the only
//   thing this package ever writes to a contact before a send. A contact that carries another is never changed. A
//   person Reply holds nobody for has no record to read: the command imports them from the consumed value itself, so
//   the contact it makes is reached there and holds no stop. The command sends only to a contact it made: one Reply
//   already held — found by the command's own lookup, which runs again, or matched by its import where Reply's lookup
//   did not return it — it names as `contact.already_held` and sends nothing to, and that contact is then read like
//   any other before the send is made to it by its identifier. No send reaches a contact Reply held without that read.
//
// A lost answer from the one write before a send is asked again as a read is: nothing was sent, and the next attempt
// reads the contact first. A send's own lost answer is never repeated: Reply keeps no key on a send, and a command that
// imported the person may have sent too.
import { run, runOrHeld } from "./cli.js";
import { describe, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, sameAddress, sameValue, toEnsure, writtenAsItIs } from "./contacts.js";

const SENDERS = "account list";
const READ = "contact get";
const PATCH = "contact update";

// The row for a contact this attempt was not pinned to that Reply named and then held nothing under (cli.js).
const GONE = "contact_gone";

// What Reply reads in a text as its own: the start of a placeholder it fills in for each person, and of a prompt to a
// model whose answer it sends.
const PLACEHOLDERS = ["{{", "@prompt[["];

// Reply numbers its mailboxes in 64 bits and its LinkedIn accounts in 32, so this is what a sender's identifier has to
// look like before there is anything worth asking about.
const LARGEST_SENDER = { "email": Number.MAX_SAFE_INTEGER, "linkedin": 2147483647 };

// An account Reply lists in a state no send can go from: the sender's block, for its channel, until a trial gets past it.
const UNAVAILABLE = ["inactive", "disconnected"];

// Whether an answer carries a field at all, as a string or as null; an answer without it says nothing about it.
function states(answer, field) {
  return Object.prototype.hasOwnProperty.call(answer, field) && (answer[field] === null || typeof answer[field] === "string");
}

/** Refuses a text a person approved that Reply would read a placeholder or a prompt of its own in. Nothing is called. */
export function refusePlaceholders(operation, texts) {
  for (const written of texts) {
    if (typeof written === "string" && PLACEHOLDERS.some(marker => written.indexOf(marker) >= 0)) {
      fail(operation, { call: null, row: "placeholder_in_the_text" }, undefined);
    }
  }
}

/**
 * The account the send goes from, as the CLI names it, once Reply's list holds it under the address the arguments
 * state, in a state a send can go from.
 */
export function senderFor(operation, context, channel, sender) {
  const id = sender.external_id;
  if (!/^[1-9][0-9]*$/.test(id) || Number(id) > LARGEST_SENDER[channel]) {
    // Reply numbers its accounts, so anything else names no account a send could go from.
    fail(operation, { call: null, row: "sender_not_found" }, undefined);
  }

  const listed = run(context, { operation: operation, command: SENDERS, operands: [], flags: ["--channel", channel], pins: false });
  if (!Array.isArray(listed.items)) {
    fail(operation, { call: SENDERS, row: lostAnswerRow(SENDERS) }, undefined);
  }

  const account = listed.items.find(entry => entry !== null && typeof entry === "object" && entry.channel === channel
    && typeof entry.id === "number" && Number.isSafeInteger(entry.id) && String(entry.id) === id);
  if (account === undefined) {
    fail(operation, { call: SENDERS, row: "sender_not_found" }, undefined);
  }

  const held = typeof account.address === "string" && account.address.trim().length > 0 ? account.address : null;
  const given = sender.address;
  // Both sides are Reply's own spelling — the arguments copy the address `sender.list` returned — so both are keyed.
  if (held === null ? given !== null : given === null || !sameAddress(channel, held, given)) {
    fail(operation, {
      call: SENDERS,
      row: "sender_mismatch",
      reason: held === null
        ? "Reply holds no address for this account, and the arguments name one."
        : given === null ? "Reply holds an address for this account, and the arguments name none." : "Reply holds this account under another address.",
    }, undefined);
  }

  if (UNAVAILABLE.indexOf(account.status) >= 0) {
    fail(operation, { call: SENDERS, row: "sender_blocked_channel", provider_code: describe(account.reply_status), sender: id }, undefined);
  }

  return id;
}

/**
 * Runs the send's one command for the person this send may reach, from `sender` — the account `senderFor` answered,
 * which a block the command meets is said for — with `flags` after the person's own and `text` — the command's own
 * input, or none — on stdin, and answers `{ sent, learned }`: what the command answered, and what this package knows
 * Reply calls the person, which travels out with every failure from here on. `variant` is `template` for a text
 * composed as one, which names the rows of the send's own refusals (errors.js), and undefined otherwise.
 *
 * A person found by the pin or by the consumed value is read first and named by Reply's identifier. A person Reply holds
 * nobody for is named by their fields, for the command to import; where the command answers that Reply already held
 * them, it sent nothing, and the contact it names is read exactly as a found one is and the send made to it by its
 * identifier — one more command in that case, never a send to a contact nobody read.
 */
export function sendTo(operation, context, input, command, sender, flags, text, variant) {
  const named = personFor(operation, context, input);
  if (named.stdin === undefined) {
    return { sent: run(context, sendRequest(operation, command, sender, named, flags, text, variant)), learned: named.learned };
  }

  const first = runOrHeld(context, sendRequest(operation, command, sender, named, flags, text, variant));
  if (first.held === undefined) {
    return { sent: first.result, learned: named.learned };
  }

  const held = read(operation, context, input, { flags: ["--contact-id", first.held], learned: { contact: first.held } });
  return { sent: run(context, sendRequest(operation, command, sender, held, flags, text, variant)), learned: held.learned };
}

// One run of the send's command: the person as `named` gives them, then `flags`, and on stdin the command's own input
// alone for a person named by Reply's identifier, or one object, the person under `contact` and the input under
// `message`, for a person named by their fields — or the person alone where the command reads no input of its own.
function sendRequest(operation, command, sender, named, flags, text, variant) {
  const stdin = named.stdin === undefined ? text : text === undefined ? named.stdin : { contact: named.stdin, message: text };
  return {
    operation: operation,
    command: command,
    sender: sender,
    operands: [],
    flags: named.flags.concat(flags),
    stdin: stdin,
    learned: named.learned,
    gone: named.pinned ? undefined : GONE,
    variant: variant,
  };
}

/**
 * How the send names the person, once that is a person this send may reach: `{ flags, stdin, learned, pinned }` as
 * `toEnsure` gives it. A person found by the pin or by the consumed value is named by Reply's identifier once Reply's
 * record of them is read — not held as opted out, and reached at the consumed value, a LinkedIn profile written onto the
 * contact first where it carries none. A person Reply holds nobody for is named by their fields, for the send's own
 * command to import.
 */
function personFor(operation, context, input) {
  const consumed = input.contacts[0].channels[0];
  const named = toEnsure(operation, context, input, undefined);
  if (named.stdin !== undefined) {
    if (consumed.channel === "linkedin" && !writtenAsItIs(consumed.value)) {
      // Imported, the profile would be held at Reply under another spelling than the one the register was asked about,
      // exactly as it would be written onto a contact that carries none.
      fail(operation, {
        call: null,
        row: "channel_value_conflict",
        reason: "Reply holds nobody for this person, and the profile this work item consumes would be held there as another.",
      }, undefined);
    }

    return named;
  }

  return read(operation, context, input, named);
}

/**
 * Reply's record of a contact it holds — pinned, found by the lookup, or named by a command that did not act on it — read
 * before anything is sent to it: `named` once the contact is one this send may reach, and a failure otherwise.
 */
function read(operation, context, input, named) {
  const consumed = input.contacts[0].channels[0];
  const gone = named.pinned ? undefined : GONE;
  const learned = named.learned;
  const id = learned.contact;
  const held = run(context, { operation: operation, command: READ, operands: [id], learned: learned, gone: gone });
  const field = consumed.channel === "linkedin" ? "linkedInUrl" : "email";
  if (contactNumber(held.id) !== id || typeof held.isOptedOut !== "boolean" || !states(held, field)) {
    // A read that does not say whom it read, whether Reply holds them as opted out, or where it reaches them — a null or
    // an empty value says nowhere, a missing one says nothing — is no answer; nothing is written on it, and it is asked
    // again.
    fail(operation, { call: READ, row: lostAnswerRow(READ), provider_item: describe({ id: held.id, isOptedOut: held.isOptedOut }) }, learned);
  }

  if (held.isOptedOut) {
    fail(operation, { call: READ, row: "opted_out_at_reply" }, learned);
  }

  // The value Reply holds is keyed whole, never cut first: cut to a bound, a longer one could key to the consumed value
  // while Reply reaches another. One longer than the runtime stores keys to nothing, and is nobody's.
  const value = consumed.channel === "linkedin" ? held.linkedInUrl : held.email;
  const reached = typeof value === "string" && value.length > 0 ? value : null;
  if (reached !== null && sameValue(consumed.channel, reached, consumed.value)) {
    return named;
  }

  if (reached !== null || consumed.channel !== "linkedin") {
    // Another value, or no address at all: an address is a channel, and nothing here changes one.
    fail(operation, {
      call: READ,
      row: "channel_value_conflict",
      reason: reached === null ? "Reply's contact for this person carries no address." : "Reply's contact for this person is reached at another " + (consumed.channel === "linkedin" ? "profile" : "address") + ".",
    }, learned);
  }

  if (!writtenAsItIs(consumed.value)) {
    // Reply would hold the profile under another spelling than the one the register was asked about.
    fail(operation, {
      call: READ,
      row: "channel_value_conflict",
      reason: "Reply's contact for this person carries no profile, and the one this work item consumes would be held there as another.",
    }, learned);
  }

  // The profile travels on stdin and nowhere else.
  const patched = run(context, {
    operation: operation,
    command: PATCH,
    operands: [id],
    flags: ["--fields", "-"],
    stdin: { linkedin_url: consumed.value },
    learned: learned,
    lost: "patch_answer_lost",
    gone: gone,
  });
  if (patched.status !== "updated" || contactNumber(patched.contact_id) !== id) {
    fail(operation, { call: PATCH, row: "patch_answer_lost" }, learned);
  }

  // Which field was written, and never its value: the runtime keeps this line, and its logs hold no person.
  host.log("info", "Reply's contact held no LinkedIn profile, so the one the send consumes was written onto it first.", { field: "linkedin_url" });
  return named;
}
