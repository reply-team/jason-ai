// `message.status` and `message.cancel`: one direct send, read by `reply direct-send get` or taken back by `reply
// direct-send cancel`, by the reference Reply gave it when it took it.
//
// The reference is the runtime's: it composes it under `send` from the attempt that made the send, so nothing the caller
// wrote is ever read for one, and a send made from another account was refused before this package ran. Reply lists no
// pending sends, so one send is all either operation names.
//
// Reply's state for a send is told in the document's five words. `sent`, `cancelled` and `failed` are final and are kept
// as they are, but for a failed send whose code says Reply lost track of it while it was going out, which may have gone
// and is `unknown`. Any other state — `queued`, `sending`, `waitingForSchedule`, or one Reply adds later — still holds the send,
// and is `queued`; only a send Reply is not already sending can still be cancelled. A cancel Reply answers for a send that
// went, that it is sending or that failed is refused as `send_not_pending`, with the state that says which, and never
// reported as cancelled; a send already cancelled answers as one cancelled now.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { momentOf } from "./send.js";
import { cut, lookup } from "./reading.js";

const STATUS = "message.status";
const CANCEL = "message.cancel";
const GET = "direct-send get";
const TAKE_BACK = "direct-send cancel";

// Reply's final states, each the document's word for itself; every other state still holds the send.
const FINAL = { "sent": "sent", "cancelled": "cancelled", "failed": "failed" };

// Reply's code for a send it attempted and lost track of: it may have gone out, so it is told as `unknown`, never as
// `failed`. Reply's other failure codes, a missed window among them, say nothing went out.
const LOST_TRACK = "directSend.dispatchInterrupted";

// The one state that still holds the send and that a cancel can no longer stop: Reply is sending it now.
const SENDING = "sending";

// What a cancel answer that is not a cancelled send says became of it, in the word the refusal's details carry.
const NOT_PENDING = { "sent": "sent", "sending": "sent", "failed": "failed" };

// The channel is the reference's own word: Reply spells every reference with its channel first. Reply's own channel field
// is never read for it, since Reply writes LinkedIn in its own enum, `linkedIn`, and reply-cli passes it on as given.
const CHANNELS = { "email-": "email", "linkedin-": "linkedin" };

// The bounds the document gives what it carries: a message identifier and a subject as a mail header holds one, a body as
// the sends take one, a failure reason and a failure code.
const LONGEST_MESSAGE_ID = 998;
const LONGEST_SUBJECT = 998;
const LONGEST_BODY = 100000;
const LONGEST_REASON = 512;
const LONGEST_CODE = 128;

export function messageStatus(input, context) {
  const reference = input.send.reference;
  const record = run(context, { operation: STATUS, command: GET, operands: [reference], pins: false });
  const read = recordOf(record, reference);
  if (read === undefined) {
    // An answer about another send, without a state, or with a field in a shape the document does not take is no answer:
    // a read changes nothing, and it is asked again.
    fail(STATUS, { call: GET, row: lostAnswerRow(GET), provider_item: evidence(record) }, undefined);
  }

  return { result: read };
}

export function messageCancel(input, context) {
  const reference = input.send.reference;
  const answer = run(context, { operation: CANCEL, command: TAKE_BACK, operands: [reference], pins: false });
  const state = answer.reference === reference && typeof answer.state === "string" ? answer.state : null;
  const gone = lookup(NOT_PENDING, state);
  if (gone !== undefined) {
    // An answer whose state says the send went or failed is the refusal, whatever the exit: never a cancelled send.
    fail(CANCEL, { call: TAKE_BACK, row: gone === "sent" ? "send_already_sent" : "send_already_failed" }, undefined);
  }

  if (state !== "cancelled") {
    // An answer about another send, or in a state that still holds it, says nothing of the cancel: it is repeated, which
    // changes nothing for a send already cancelled.
    fail(CANCEL, { call: TAKE_BACK, row: lostAnswerRow(TAKE_BACK), provider_item: evidence(answer) }, undefined);
  }

  return { result: { items: [{ state: "cancelled", cancelled_at: momentOrNull(answer.cancelled_at) }] } };
}

// Reply's record of the send, in the document's words, or undefined where it names another send, gives no state, or gives
// a message identifier in a shape the document does not take.
function recordOf(record, reference) {
  const channel = channelOf(reference);
  if (record.reference !== reference || typeof record.state !== "string" || channel === undefined) {
    return undefined;
  }

  const final = lookup(FINAL, record.state);
  const read = {
    state: final === undefined ? "queued" : final === "failed" && record.failure_code === LOST_TRACK ? "unknown" : final,
    channel: channel,
    cancelable: final === undefined && record.state !== SENDING,
    accepted_for: momentOrNull(record.send_after),
    sent_at: momentOrNull(record.sent_at),
    message_id: messageIdOf(record.message_id),
    rendered_subject: copyOf(record.rendered_subject, LONGEST_SUBJECT),
    rendered_body: copyOf(record.rendered_body, LONGEST_BODY),
    failure_reason: typeof record.failure_reason === "string" && record.failure_reason.length > 0 ? cut(record.failure_reason, LONGEST_REASON) : null,
    // Reply's own code, as given and never interpreted; one the document cannot carry whole is none, since a cut code names
    // nothing.
    failure_code: typeof record.failure_code === "string" && record.failure_code.length > 0 ? copyOf(record.failure_code, LONGEST_CODE) : null,
    cancelled_at: momentOrNull(record.cancelled_at),
  };

  return Object.keys(read).every(name => read[name] !== undefined) ? read : undefined;
}

// The channel a reference names by its prefix, or undefined for one in no channel's spelling.
function channelOf(reference) {
  const prefix = Object.keys(CHANNELS).find(start => reference.startsWith(start));
  return prefix === undefined ? undefined : CHANNELS[prefix];
}

// A moment in UTC, or null where Reply gives none or one that cannot be read: the state answers, whatever the moment.
function momentOrNull(value) {
  return momentOf(value) ?? null;
}

// The message's identifier as Reply gives it: a string, or a number written out; null where it gives none or one past the
// document's bound, which cut would name nothing; undefined for anything else.
function messageIdOf(value) {
  if (value === undefined || value === null) {
    return null;
  }

  if (typeof value === "number" && Number.isSafeInteger(value) && value > 0) {
    return String(value);
  }

  if (typeof value !== "string") {
    return undefined;
  }

  return value.length > 0 && value.length <= LONGEST_MESSAGE_ID ? value : null;
}

// The message as it went, where Reply kept that copy: carried whole or not at all, since a copy cut short is not what went.
function copyOf(value, longest) {
  return typeof value === "string" && value.length <= longest ? value : null;
}
