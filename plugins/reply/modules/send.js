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
// The send itself is made under the work item's idempotency key, and no other command of the attempt carries one; a
// moment the arguments name that is still ahead goes beside it as `--send-after`, as written, and Reply holds the send
// for it. A moment already past is no moment: the send goes as one that names none. Reply keeps the key and the moment
// only where it keeps direct sends, which `direct-send get` of a reference no send is held under tells — Reply's own
// answer about the send it names, against no route at all — so that is read first wherever it matters: before a send
// that names a moment, which goes at that moment or not at all (`capability_not_offered`), before a message that names
// the sending schedule Reply times it by (provider.js), which goes by that schedule or not at all (the same code), and
// before a repeat after an attempt that may have sent, which without the key could send the person a second message
// (`send_not_settled`). The schedule goes beside the key as `--schedule-id`, with the moment where one is named too.
// Nothing is sent in either case. The runtime says when the earliest such attempt was claimed, `context.unsettled_since`,
// and null where none was; Reply keeps a key for a day, so a repeat 23 hours or more after that moment is not settled by
// it either, and ends before anything is sent. A later attempt after attempts that all settled is a first send again.
//
// Where the runtime also names the send such an attempt returned, `context.unsettled_reference`, that send is read
// first, by `direct-send get` of its reference, inside the window and past it: a read never makes a send twice. Reply's
// record of it then answers the work item as the send under the key would, a state that says it failed or was cancelled
// included, and nothing is sent. A read that finds nothing, gets no answer, or names another send or none in a shape
// this package reads says nothing of it, and the window decides as it does without a reference. A read the CLI refuses
// for want of a working credential is the profile's failure, not the send's, and fails as every such command does.
//
// A lost answer from the one write before a send is asked again as a read is: nothing was sent, and the next attempt
// reads the contact first. A send's own lost answer — no answer of Reply's at all, or one at 500 or above — is repeated
// under the same key: Reply answers a send it already took with what it answered then and sends nothing twice, and
// answers the key held for another request — a repeat that names the person or the moment otherwise — with what it holds
// under it. A send Reply answered with a failure is never repeated, since Reply keeps no key for it and a repeat would
// be a new send. Either way the answer is the send the work item made Reply take. A command that imported the person
// carries the key too, and Reply keeps it only where it took the send.
import { offered, readOrNone, run, runOrHeld, runOrKept } from "./cli.js";
import { describe, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, sameAddress, sameValue, toEnsure, writtenAsItIs } from "./contacts.js";
import { scheduleOf } from "./provider.js";

const SENDERS = "account list";
const READ = "contact get";
const PATCH = "contact update";

// The read that tells whether the account's Reply keeps direct sends, of one reference in reply-cli's own spelling for
// every channel, and the CLI's words for Reply's answer about the send it names, which only a Reply that keeps them gives.
const CHECK = "direct-send get";
const CHECKED = "email-1";
const KEEPS = ["direct_send.not_found", "direct_send.not_owned"];

// The row of a read of the send an earlier attempt returned that Reply answers as another account's (errors.js).
const NOT_OWNED = "send_not_settled_not_owned";

// Reply keeps a direct send's key for a day; a repeat goes under it only within 23 hours of the moment the earliest
// attempt that may have sent was claimed, so the key Reply answers by is still the one it kept.
const KEY_WINDOW_MS = 23 * 60 * 60 * 1000;

// Reply's states for a send it no longer holds back: it went, or is going now, so it can no longer be cancelled. Every
// other state it still holds the send in can be, whatever its name.
const GOING = ["sent", "sending"];

// Reply's states for a send that is not going at all: it failed, or was cancelled. Either is final.
const UNTAKEN = ["failed", "cancelled"];

// A moment as reply-cli takes one for `--send-after` and as Reply answers one: with its offset.
const MOMENT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})$/;

// The longest reference the document takes.
const LONGEST_REFERENCE = 256;

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
 * The send an earlier attempt that may have sent returned, as Reply's record of it says, read before anything else
 * (`context.unsettled_reference`): `{ kept, learned, call }` as `sendTo` answers a key Reply holds for another request —
 * `kept` holding the record as `details` and the contact it names as `ids` — and the read as `call`. Undefined where the
 * context names no send, and where the read finds nothing, gets no answer, or answers for another send or in a shape that
 * names no contact or that `taken` does not read: the repeat is then the window's (`keyedFlags`). A read Reply answers
 * as another account's send (`direct_send.not_owned`) fails the operation as not settled: what Reply keeps under the
 * key there settles nothing from this account, so nothing is sent. A read refused for want of a working credential
 * fails the operation as `unauthorized` (`readOrNone`).
 */
export function recordedSend(operation, context) {
  const reference = context && typeof context.unsettled_reference === "string" ? context.unsettled_reference : null;
  if (reference === null) {
    return undefined;
  }

  const record = readOrNone(context, { operation: operation, command: CHECK, operands: [reference], pins: false, final: { "direct_send.not_owned": NOT_OWNED } });
  if (record === null || record.reference !== reference || contactNumber(record.contact_id) === null || taken(record, true) === undefined) {
    return undefined;
  }

  return { kept: { details: record, ids: { contact_id: record.contact_id } }, learned: undefined, call: CHECK };
}

/**
 * Runs the send's one command for the person this send may reach, from `sender` — the account `senderFor` answered,
 * which a block the command meets is said for — with `flags` after the person's own, then the work item's key and the
 * moment (`keyedFlags`), and `text` — the command's own input, or none — on stdin, and answers `{ sent, learned }`: what
 * the command answered, and what this package knows Reply calls the person, which travels out with every failure from
 * here on — or `{ kept, learned }` where Reply holds the key for another request (cli.js). `variant` is `template` for a
 * text composed as one, which names the rows of the send's own refusals (errors.js), and undefined otherwise.
 *
 * A person found by the pin or by the consumed value is read first and named by Reply's identifier. A person Reply holds
 * nobody for is named by their fields, for the command to import; where the command answers that Reply already held
 * them, it sent nothing, and the contact it names is read exactly as a found one is and the send made to it by its
 * identifier — one more command in that case, never a send to a contact nobody read.
 */
export function sendTo(operation, context, input, command, sender, flags, text, variant) {
  const keyed = flags.concat(keyedFlags(operation, context, input));
  const named = personFor(operation, context, input);
  if (named.stdin === undefined) {
    return answered(runOrKept(context, sendRequest(operation, command, sender, named, keyed, text, variant)), named.learned);
  }

  const first = runOrHeld(context, sendRequest(operation, command, sender, named, keyed, text, variant));
  if (first.held === undefined) {
    return answered(first, named.learned);
  }

  const held = read(operation, context, input, { flags: ["--contact-id", first.held], learned: { contact: first.held } });
  return answered(runOrKept(context, sendRequest(operation, command, sender, held, keyed, text, variant)), held.learned);
}

function answered(ended, learned) {
  return ended.kept !== undefined ? { kept: ended.kept, learned: learned } : { sent: ended.result, learned: learned };
}

/**
 * The moment the arguments name in `send_after`, as `Date.parse` reads it, or null where they name none. A moment it
 * cannot read is never one already past: the attempt ends final with nothing read or sent, since the send would
 * otherwise go now where a later moment was approved. Each send asks it before its first call, as it holds its text,
 * so neither the sender's read nor the read of the send an earlier attempt returned comes first; `keyedFlags` asks it
 * again.
 */
export function readableMoment(operation, input) {
  const moment = input.args.send_after === undefined || input.args.send_after === null ? null : input.args.send_after;
  const at = typeof moment === "string" ? Date.parse(moment) : Number.NaN;
  if (moment !== null && !Number.isFinite(at)) {
    fail(operation, { call: null, row: "send_after_unreadable" }, undefined);
  }

  return moment === null ? null : at;
}

/**
 * The flags a send carries beside its own: the work item's key, the moment it goes out after where the arguments name
 * one still ahead, as written (`readableMoment`), and for a message the sending schedule the provider's object names
 * (`scheduleOf`). A repeat after an attempt that may have sent goes only while Reply still keeps the key
 * (`repeatStillSettleable`). Before a send that names a moment or a schedule, and before such a repeat, the account's
 * Reply is read to keep direct sends; one that does not ends the attempt with nothing sent.
 */
function keyedFlags(operation, context, input) {
  const at = readableMoment(operation, input);
  const ahead = at !== null && at > Date.now();
  const schedule = operation === "message.send" ? scheduleOf(operation, input) : null;
  const repeat = Boolean(context) && typeof context.unsettled_since === "string";
  if (repeat && !repeatStillSettleable(context)) {
    fail(operation, { call: null, row: "send_not_settled_after_the_window" }, undefined);
  }

  if ((ahead || schedule !== null || repeat) && !offered(context, { operation: operation, command: CHECK, operands: [CHECKED], pins: false }, KEEPS)) {
    fail(operation, { call: CHECK, row: repeat ? "send_not_settled" : ahead ? "send_after_not_offered" : "schedule_not_offered" }, undefined);
  }

  return ["--idempotency-key", input.idempotency_key]
    .concat(ahead ? ["--send-after", input.args.send_after] : [])
    .concat(schedule !== null ? ["--schedule-id", schedule] : []);
}

/** Whether Reply still keeps the key a repeat goes under: less than 23 hours since `context.unsettled_since`. */
function repeatStillSettleable(context) {
  const since = Date.parse(context.unsettled_since);
  return Number.isFinite(since) && Date.now() < since + KEY_WINDOW_MS;
}

/**
 * What Reply took, from what `sendTo` answered. Where Reply holds the key for another request, what it holds under it
 * is the send, named by its reference.
 */
export function acceptance(done) {
  return done.kept !== undefined ? taken(done.kept.details, true) : taken(done.sent, false);
}

/**
 * What Reply says of a send it took, from an answer that names it: `{ status, given }`. The status is what happened:
 * `sent` where Reply's state is `sent`, or where it gives no state and names no send, as a Reply that sends inside the
 * call answers; `accepted` for any other state, one Reply adds later included, and for a send it names by a reference
 * or a moment with no state. `given` holds each of `accepted_for` — the moment Reply holds the send for, in UTC —
 * `reference` and `cancelable` — whether the send still waits, so it can be cancelled — only where reply-cli gave its
 * source, a null being none given. A send Reply says failed or was cancelled is `{ ended, reason, given }`: the state,
 * Reply's reason where it gave one, and the reference alone in `given`. Undefined where the answer names any of them in
 * another shape, and, for a send Reply holds under the key (`named`), where it names no reference.
 */
export function taken(said, named) {
  const given = {};
  if (said.send_after !== undefined && said.send_after !== null) {
    given.accepted_for = momentOf(said.send_after);
  }

  if (said.reference !== undefined && said.reference !== null) {
    given.reference = referenceOf(said.reference);
  }

  const state = said.state === undefined || said.state === null ? null : said.state;
  if (state !== null) {
    given.cancelable = typeof state === "string" ? GOING.indexOf(state) < 0 : undefined;
  }

  const shaped = Object.keys(given).every((name) => given[name] !== undefined);
  if (!shaped || (named && given.reference === undefined)) {
    return undefined;
  }

  if (UNTAKEN.indexOf(state) >= 0) {
    const reason = typeof said.failure_reason === "string" && said.failure_reason.length > 0 ? said.failure_reason : undefined;
    return { ended: state, reason: reason, given: given.reference === undefined ? {} : { reference: given.reference } };
  }

  const went = state === null ? given.reference === undefined && given.accepted_for === undefined : state === "sent";
  return { status: went ? "sent" : "accepted", given: given };
}

/**
 * What `learned` pins, with the send an answer names beside it where it names one by a reference of the document's
 * shape: an answer refused as a lost one for another field's shape still names the send, which the next attempt then
 * reads first (`recordedSend`).
 */
export function withNamedSend(learned, said) {
  const reference = said !== null && typeof said === "object" ? referenceOf(said.reference) : undefined;
  return reference === undefined ? learned : Object.assign({}, learned, { send: reference });
}

/**
 * Ends the attempt for a send Reply took and says is not going — it failed, or was cancelled — which is final: Reply's
 * reason is quoted where it gave one, and the send it names is kept as the work item's beside what `learned` pins.
 */
export function endedAtReply(operation, call, took, learned) {
  const pinned = took.given.reference === undefined ? learned : Object.assign({}, learned, { send: took.given.reference });
  fail(operation, { call: call, row: took.ended === "failed" ? "send_failed_at_reply" : "send_cancelled_at_reply", provider_detail: took.reason }, pinned);
}

// A reference as the document takes one: a string of 1 to 256 characters, and undefined for anything else.
function referenceOf(value) {
  return typeof value === "string" && value.length > 0 && value.length <= LONGEST_REFERENCE ? value : undefined;
}

// A moment with its offset, written in UTC to the second, or to the millisecond where it names one; undefined for
// anything else.
export function momentOf(value) {
  const at = typeof value === "string" && MOMENT.test(value) ? Date.parse(value) : Number.NaN;
  return Number.isFinite(at) ? new Date(at).toISOString().replace(".000Z", "Z") : undefined;
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
    kept: true,
    channel: command === "contact send-email" ? "email" : "linkedin",
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
