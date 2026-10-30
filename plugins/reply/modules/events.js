// `events.read`: what moved in the Reply account, read from Reply's change feed a page at a time by `reply inbox changes`.
//
// Reply's feed lists the threads that moved, oldest first, from a cursor or from a moment, and holds the last 30 days.
// Each item is one `thread_moved`: the thread, its contact and sequence, the channel, and who moved it, from the thread's
// last message and Reply's flag that the thread holds a reply — the other side's, the account's, and nobody this package
// can name where the account's message tops a thread that holds a reply or where Reply gave no last message. A thread
// moves in this feed only when a message lands on it, so no move is Reply's own. A move by the account is read and not
// kept: it is left out of the page's events, after it has been held to the end of a span, and the cursor still moves
// past it. Reply's flags for a bounce, an automatic reply and an absence are not carried, and nothing anybody wrote is in
// an item at all.
//
// The cursor this package hands back has three forms, each with a prefix of its own, so that one of any other shape is
// none it wrote. While a read goes on, `c|<Reply's cursor>` asks for the next page from where the last one ended. Once a
// read has read everything, `m|<moment>` is when it ended by this package's clock, and the read after it asks Reply from
// an hour before that moment: a thread's last activity is when its last message was sent, so a reply Reply takes in
// late lands behind the point the feed had already passed, and the hour reads it still. The repeats it brings are the
// runtime's to drop, by the events' own keys. A read that ends at a moment, a span asked for again, keeps that moment in
// `u|<until>|<Reply's cursor>` and never overlaps, since it says where it starts; a span that has read to its end hands
// back `u|<until>|` with no cursor of Reply's, which is none a read goes on from. Reply's cursors are base64url and never
// hold a `|`, and Reply's cursor is needed only where a read goes on from it. A moment in a cursor is written, and read,
// only as `toISOString` writes one. A read by date starts no further back than the feed holds, and an answer that says
// the feed no longer holds the point a read starts from is the runtime's sign to read by date again.
import { run } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { flag, identifier, utc } from "./reading.js";

const OPERATION = "events.read";
const FEED = "inbox changes";

// How far back the feed holds, and the margin a read by date keeps inside it. The window is counted by this machine's
// clock and checked by Reply's, and a machine whose clock trails Reply's would ask for a moment Reply has already let go:
// every read by date that reaches the edge would be refused, a day apart, for as long as the clocks disagree. An hour
// out of 30 days is room for any clock that keeps time at all.
const WINDOW_MS = 30 * 24 * 60 * 60 * 1000;
const MARGIN_MS = 60 * 60 * 1000;

// How far before the end of the last read that read everything the next one starts.
const OVERLAP_MS = 60 * 60 * 1000;

const GOING_ON = "c|";
const ENDED = "m|";
const SPAN = "u|";

// What the contract admits as a cursor.
const CURSOR = /^[!-~]{1,2048}$/;
const REPLY_CURSOR = /^[!-{}~]+$/;

// A moment as `toISOString` writes one, the only form a cursor of this package carries.
const MOMENT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/;

const CHANNEL = { email: "email", linkedIn: "linkedin" };

export function eventsRead(input, context) {
  const from = startOf(input);
  const floor = Date.now() - WINDOW_MS + MARGIN_MS;
  const asked = typeof from.since === "string" ? Date.parse(from.since) : Number.NaN;
  const flags = from.cursor !== null
    ? ["--cursor", from.cursor]
    : ["--since", new Date(isNaN(asked) || asked < floor ? floor : asked).toISOString()];
  flags.push("--top", String(input.limit));

  // A refusal Reply explains by a code of its own is a refusal of the cursor where the read was handed one.
  const page = run(context, {
    operation: OPERATION, command: FEED, operands: [], flags: flags, pins: false,
    variant: from.cursor !== null ? "cursor" : undefined,
  });
  // A page holding more than was asked for is no page of the feed as it was asked: cutting it short would hand back a
  // cursor past events never read.
  if (!Array.isArray(page.items) || page.items.length > input.limit || typeof page.has_more !== "boolean" || typeof page.cursor !== "string") {
    fail(OPERATION, { call: FEED, row: lostAnswerRow(FEED) }, undefined);
  }

  const until = from.until === null ? null : Date.parse(from.until);
  const events = [];
  let ended = false;
  for (const item of page.items) {
    const event = eventOf(item);
    if (until !== null && Date.parse(event.occurred_at) >= until) {
      ended = true;
      break;
    }

    if (event.moved_by !== "account") {
      events.push(event);
    }
  }

  // Reply's cursor is read only where the read goes on from it; a read that goes on without one has lost its place.
  const more = page.has_more && !ended;
  if (more && !REPLY_CURSOR.test(page.cursor)) {
    fail(OPERATION, { call: FEED, row: lostAnswerRow(FEED) }, undefined);
  }

  const cursor = until !== null
    ? SPAN + new Date(until).toISOString() + "|" + (more ? page.cursor : "")
    : more ? GOING_ON + page.cursor : ENDED + new Date().toISOString();
  if (!CURSOR.test(cursor)) {
    fail(OPERATION, { call: FEED, row: lostAnswerRow(FEED) }, undefined);
  }

  return { result: { events: events, cursor: cursor, has_more: more } };
}

// Where a read starts: Reply's cursor, or a moment, and the moment it ends at where it ends at one.
function startOf(input) {
  const cursor = input.cursor;
  if (typeof cursor !== "string") {
    return { cursor: null, since: input.since, until: input.until };
  }

  const rest = cursor.slice(2);
  const prefix = cursor.slice(0, 2);
  if (prefix === GOING_ON && REPLY_CURSOR.test(rest)) {
    return { cursor: rest, since: null, until: null };
  }

  if (prefix === ENDED && isMoment(rest)) {
    return { cursor: null, since: new Date(Date.parse(rest) - OVERLAP_MS).toISOString(), until: null };
  }

  const bar = rest.indexOf("|");
  if (prefix === SPAN && bar > 0 && isMoment(rest.slice(0, bar)) && REPLY_CURSOR.test(rest.slice(bar + 1))) {
    return { cursor: rest.slice(bar + 1), since: null, until: rest.slice(0, bar) };
  }

  fail(OPERATION, { call: null, row: "cursor_not_written_here" }, undefined);
}

// Whether a cursor's moment is one this package wrote: `toISOString`'s form, and a day the calendar has.
function isMoment(text) {
  const at = MOMENT.test(text) ? Date.parse(text) : Number.NaN;
  return !isNaN(at) && new Date(at).toISOString() === text;
}

// One item as a moved thread. An item with no thread or no moment of its own cannot be passed over: the cursor would
// move past it, and the move would never be read again.
function eventOf(item) {
  const thread = item !== null && typeof item === "object" && !Array.isArray(item) ? identifier(item.threadId) : null;
  const moment = thread !== null ? utc(item.lastActivityDate) : null;
  if (moment === null) {
    fail(OPERATION, { call: FEED, row: "item_unreadable" }, undefined);
  }

  return {
    kind: "thread_moved",
    id: null,
    occurred_at: moment,
    thread: thread,
    contact: identifier(item.contactId),
    campaign: identifier(item.sequenceId),
    moved_by: moverOf(item),
    channel: typeof item.channel === "string" && Object.prototype.hasOwnProperty.call(CHANNEL, item.channel) ? CHANNEL[item.channel] : "other",
    unread: null,
    has_meeting_intent: flag(item.hasMeetingIntent),
  };
}

// Who moved the thread. The other side's last message is the other side's move, whatever else Reply says. The account's
// last message is the account's move only on a thread Reply does not flag as holding a reply: on one that holds a reply,
// the feed shows the account's message on top and cannot say whether that reply is newer than the last read, so the move
// names nobody, which says the line a reply says. Each later message the account sends on that thread says it again, as
// the read of the account's threads does. Nobody this package can name, too, where Reply gave no last message, or one
// that does not say which way it went. A thread moves in this feed only when a message lands on it, so no move is Reply's
// own, and the moment of the last activity is never compared with the message's.
function moverOf(item) {
  const last = item.lastMessage;
  if (last === null || typeof last !== "object" || Array.isArray(last)) {
    return null;
  }

  if (last.isOutbound === false) {
    return "other_side";
  }

  if (last.isOutbound !== true) {
    return null;
  }

  const flags = item.flags;
  const replied = flags !== null && typeof flags === "object" && !Array.isArray(flags) && flags.hasReply === true;
  return replied ? null : "account";
}
