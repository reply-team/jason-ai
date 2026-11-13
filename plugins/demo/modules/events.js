// events.read: the story's written replies, each a thread the other side moved, in the order they land. Every reply's
// moment is fixed by the story's start, so the replies already landed are always the first ones of that order, and a
// cursor is a place in it: "demo1.<start>.<next>", with ".<until>" for a span that ends. A read made before the story
// started gives "demo1.0.<since>" instead, the depth that read asked for in milliseconds, so the first read after the
// start returns every reply at or after it. A reply names the person by the contact the sends pin, so it reaches a campaign only
// where a send to that person was made; it names no provider campaign, since the demo provider keeps none.
import { fail } from "./fail.js";
import { iso, now, replyAt, storyStart } from "./clock.js";
import { threads } from "./people.js";

const CURSOR = /^demo1\.([0-9]{1,15})\.([0-9]{1,15})(?:\.([0-9]{1,15}))?$/;

function written(start) {
  const all = [];
  if (start === null) {
    return all;
  }

  for (const thread of threads()) {
    thread.replies.forEach((reply, index) => all.push({ thread, reply, index, moment: replyAt(start, reply) }));
  }

  return all.sort((a, b) => a.moment - b.moment || (a.thread.id < b.thread.id ? -1 : a.thread.id > b.thread.id ? 1 : 0));
}

function cursor(start, next, until, since) {
  return "demo1." + (start === null ? "0." + since : start + "." + next) + (until === null ? "" : "." + until);
}

/** The first reply at or after a moment, or the end. */
function firstAt(all, since) {
  const index = all.findIndex((entry) => entry.moment >= since);
  return index < 0 ? all.length : index;
}

function event(entry) {
  return {
    kind: "thread_moved",
    id: "demo-reply-" + entry.thread.send.key + "-" + (entry.index + 1),
    occurred_at: iso(entry.moment),
    thread: entry.thread.id,
    contact: entry.thread.person.contact_id,
    campaign: null,
    moved_by: "other_side",
    channel: entry.thread.channel,
    unread: true,
    has_meeting_intent: entry.reply.meeting_intent,
  };
}

export function eventsRead(input) {
  const start = storyStart();
  const all = written(start);
  let next;
  let until;
  let since;
  if (input.cursor !== null) {
    const parsed = CURSOR.exec(input.cursor);
    const from = parsed === null ? null : Number(parsed[1]);
    if (parsed === null || (from !== 0 && from !== start)) {
      throw fail("permanent", "events_cursor_unknown", "The demo provider cannot read on from this cursor.");
    }

    since = from === 0 ? Number(parsed[2]) : 0;
    next = from === 0 ? firstAt(all, since) : Number(parsed[2]);
    until = parsed[3] === undefined ? null : Number(parsed[3]);
  } else {
    since = Math.max(0, Date.parse(input.since));
    until = input.until === null ? null : Date.parse(input.until);
    next = firstAt(all, since);
  }

  const at = now();
  const landed = (entry) => entry.moment <= at && (until === null || entry.moment < until);
  const page = [];
  while (next < all.length && page.length < input.limit && landed(all[next])) {
    page.push(event(all[next]));
    next += 1;
  }

  return {
    result: {
      events: page,
      cursor: cursor(start, next, until, since),
      has_more: next < all.length && landed(all[next]),
    },
  };
}
