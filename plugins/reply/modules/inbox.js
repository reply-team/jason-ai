// `conversation.list` and `conversation.get`: Reply's inbox, read.
//
// A thread is Reply's own resource, and both reads work by identifiers alone: a person by the pin Jason holds, a
// campaign by its sequence, a thread by the identifier the list returned. What Reply says about the person on a
// thread is mapped to two things — their identifier and the name it shows — and never to the address, the phone,
// the profile URL, the company or the title that come with them.
import { run } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { fill } from "./page.js";
import { cut, flag, identifier, isReplyNumber, lookup, text, utc, word } from "./reading.js";

const LIST = "conversation.list";
const GET = "conversation.get";
const LIST_COMMAND = "inbox list";
const GET_COMMAND = "inbox get";

// What a message's body may be, and how many addresses each recipient list carries.
const BODY = 12000;
const RECIPIENTS = 10;

// How far every thread is read, and how many threads one `inbox list` returns at most.
const MERGE_REACH = 1000;
const STEP = 100;

const CHANNEL = { "email": "email", "linkedIn": "linkedin" };
const STATE = { "ok": "ok", "needsAttention": "needs_attention" };
const AI_SENT = { "aiGenerated": "ai_generated", "aiGeneratedEdited": "ai_generated_edited", "autoSent": "auto_sent" };

export function conversationList(input, context) {
  const person = Array.isArray(input.contacts) && input.contacts.length > 0 ? input.contacts[0] : null;
  let target;
  let learned;

  if (person !== null) {
    // With a person, the page is that person's threads across the account: the campaign is context only. A read
    // works by the pin, so a person with none is somebody this account was never told about, and a read creates
    // nobody — so nothing is asked.
    const pin = person.external_ids && person.external_ids.contact;
    if (pin === undefined || pin === null) {
      fail(LIST, { call: null, row: "contact_not_linked" }, undefined);
    }

    if (!isReplyNumber(pin)) {
      fail(LIST, { call: null, row: "identifier_names_nothing", reason: "The pinned contact identifier is not a Reply contact number." }, undefined);
    }

    target = ["--contact-id", pin];
  } else {
    const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
    const supplied = input.args.campaign && input.args.campaign.external_id;
    const sequence = pinned || supplied;
    if (!isReplyNumber(sequence)) {
      fail(LIST, { call: null, row: "identifier_names_nothing", reason: "The campaign's identifier is not a Reply sequence number." }, undefined);
    }

    target = ["--sequence-id", sequence];

    // Only a read that made the first link returns the campaign's identifier: a pin is never written twice.
    learned = pinned ? undefined : { campaign: sequence };
  }

  // The contract keeps the threads that moved at or after `since`, and so does the CLI's `--since`. The channel and the
  // category are filters Reply applies before it pages, so they travel on every page read and `offset` counts the
  // threads that pass them; a filter applied here, after reading, could not say where the next page starts.
  const filters = typeof input.args.since === "string" ? ["--since", input.args.since] : [];
  if (typeof input.args.thread_channel === "string") {
    filters.push("--channel", input.args.thread_channel);
  }

  if (input.args.category) {
    if (!isReplyNumber(input.args.category.external_id)) {
      fail(LIST, { call: null, row: "identifier_names_nothing", reason: "The category's identifier is not a Reply category number." }, undefined);
    }

    filters.push("--category-id", input.args.category.external_id);
  }

  const result = { items: [], has_more: false, vendor: { ordering: "last_activity_desc" } };
  let threads;
  let more;

  if (input.args.source === "inbound") {
    // One of Reply's own buckets, paged at Reply.
    const page = readPage(context, target, filters, "inbox", input.args.limit, input.args.offset, learned);
    threads = page.threads;
    more = page.more;
  } else {
    // Every thread. Reply's filter has no bucket for that — with no source it answers the inbox, the threads with an
    // inbound message — so the inbox and the threads whose latest message is outbound are read, each from its first
    // thread, and merged: by last activity, newest first, then by identifier, a thread found in both listed once. A
    // page further in than this package reads every thread is refused rather than guessed at.
    const reach = input.args.offset + input.args.limit;
    if (reach > MERGE_REACH) {
      fail(LIST, {
        call: null,
        row: "call_could_not_be_built",
        reason: "source all reads Reply's inbox and its sent threads from the first thread each and merges them, as far as "
          + MERGE_REACH + " threads, so offset plus limit may not pass " + MERGE_REACH + ".",
      }, learned);
    }

    const inbox = readBucket(context, target, filters, "inbox", reach, learned);
    const sent = readBucket(context, target, filters, "sent", reach, learned);
    const merged = merge(inbox.threads, sent.threads);
    threads = merged.slice(input.args.offset, reach);
    more = inbox.more || sent.more || merged.length > reach;
  }

  const filled = fill(threads, result);
  result.items = filled.items;
  result.has_more = more || filled.cut;

  return learned === undefined ? { result: result } : { result: result, external_ids: learned };
}

// One bucket, read from its first thread as far as `reach`: a hundred at a time, the CLI's largest page, stopping
// where the bucket ends.
function readBucket(context, target, filters, source, reach, learned) {
  let threads = [];
  for (let skip = 0; skip < reach; skip += STEP) {
    const page = readPage(context, target, filters, source, Math.min(STEP, reach - skip), skip, learned);
    threads = threads.concat(page.threads);
    if (!page.more) {
      return { threads: threads, more: false };
    }
  }

  return { threads: threads, more: true };
}

// One page of a bucket, read and mapped; any refusal ends the operation in its own words.
function readPage(context, target, filters, source, limit, offset, learned) {
  const page = run(context, {
    operation: LIST,
    command: LIST_COMMAND,
    operands: [],
    flags: target.concat(["--source", source], filters, ["--limit", String(limit), "--offset", String(offset)]),
    learned: learned,
  });

  const threads = Array.isArray(page.items) ? page.items.map(listed) : null;
  if (threads === null || threads.indexOf(null) >= 0 || typeof page.has_more !== "boolean") {
    fail(LIST, { call: LIST_COMMAND, row: lostAnswerRow(LIST_COMMAND) }, learned);
  }

  return { threads: threads, more: page.has_more };
}

// Two buckets as one list: newest activity first, then the larger identifier, and a thread in both once.
function merge(first, second) {
  const seen = {};
  const all = [];
  for (const thread of first.concat(second)) {
    if (!Object.prototype.hasOwnProperty.call(seen, thread.external_id)) {
      seen[thread.external_id] = true;
      all.push(thread);
    }
  }

  return all.sort((a, b) => {
    const moved = Date.parse(b.last_activity_at) - Date.parse(a.last_activity_at);
    return moved !== 0 ? moved : Number(b.external_id) - Number(a.external_id);
  });
}

export function conversationGet(input, context) {
  const thread = input.args.thread.external_id;
  if (!isReplyNumber(thread)) {
    // Reply numbers its threads, so anything else names no thread this account could hold.
    fail(GET, { call: null, row: "conversation_not_found" }, undefined);
  }

  const read = run(context, {
    operation: GET,
    command: GET_COMMAND,
    operands: [thread],
    flags: ["--limit", String(input.args.limit), "--offset", String(input.args.offset)],
  });

  const head = header(read.thread);
  const page = read.messages !== null && typeof read.messages === "object" ? read.messages : null;
  const entries = page !== null && Array.isArray(page.items) ? page.items.map(message) : null;
  if (head === null || entries === null || entries.indexOf(null) >= 0) {
    // A result that is not a thread and a page of its messages is not an answer; on a read, it is asked again.
    fail(GET, { call: GET_COMMAND, row: lostAnswerRow(GET_COMMAND) }, undefined);
  }

  const result = { thread: head, messages: [], has_more: false, vendor: { ordering: "oldest_first" } };
  const filled = fill(entries, result);
  result.messages = filled.items;
  result.has_more = page.has_more === true || filled.cut;

  // Nothing is pinned: the thread named itself, and its person is reported, never linked.
  return { result: result };
}

// The thread as the document publishes it. Reply's thread has no subject of its own; the CLI gives the subject of its
// latest email message, and none for a channel without subjects.
function header(data) {
  if (data === null || typeof data !== "object" || Array.isArray(data) || identifier(data.id) === null) {
    return null;
  }

  return {
    external_id: identifier(data.id),
    channel: lookup(CHANNEL, data.channel) || "other",
    subject: text(data.subject, 1000),
    contact: party(data.contact),
    campaign_external_id: data.sequence !== null && typeof data.sequence === "object" ? identifier(data.sequence.id) : null,
    unread: data.isRead === false,
    vendor: {
      category: category(data.category),
      // Whether Reply holds a drafted answer, and never the draft: what it would say is not this read's to repeat.
      has_ai_draft: data.aiDraft !== null && typeof data.aiDraft === "object",
    },
  };
}

// One message as the document publishes it, or null when the entry is not a message at all.
function message(entry) {
  if (entry === null || typeof entry !== "object" || Array.isArray(entry)) {
    return null;
  }

  const status = entry.status !== null && typeof entry.status === "object" ? entry.status : {};
  const body = typeof entry.body === "string" ? entry.body : null;
  const kept = body === null ? null : cut(body, BODY);

  return {
    external_id: text(entry.messageId, 998),
    direction: entry.isOutbound === true ? "outbound" : entry.isOutbound === false ? "inbound" : "unknown",
    sent_at: utc(entry.date),
    // An address where the channel has one, and the name Reply shows where it has none.
    from: text(entry.fromAddress, 320) || text(entry.fromName, 320),
    to: recipients(entry.to),
    cc: recipients(entry.cc),
    bcc: recipients(entry.bcc),
    subject: text(entry.subject, 1000),
    // As Reply holds it, markup included, cut where a character ends — and a cut body says so.
    body: kept,
    truncated: kept !== null && kept.length < body.length,
    vendor: {
      state: lookup(STATE, status.state) || null,
      status_code: word(status.code, 64),
      ai_sent_type: lookup(AI_SENT, entry.aiSentType) || null,
    },
  };
}

function recipients(list) {
  if (!Array.isArray(list)) {
    return null;
  }

  return list.filter(address => typeof address === "string").slice(0, RECIPIENTS).map(address => cut(address, 320));
}

// One thread as the list publishes it, or null when the row is not a thread at all.
function listed(row) {
  if (row === null || typeof row !== "object" || identifier(row.id) === null || typeof row.isRead !== "boolean") {
    return null;
  }

  const lastActivity = utc(row.lastActivityDate);
  if (lastActivity === null) {
    return null;
  }

  return {
    external_id: identifier(row.id),
    channel: lookup(CHANNEL, row.channel) || "other",
    subject: text(row.subject, 1000),
    preview: text(row.bodyPreview, 500),
    last_activity_at: lastActivity,
    unread: row.isRead === false,
    contact: party(row.contact),
    campaign_external_id: row.sequence !== null && typeof row.sequence === "object" ? identifier(row.sequence.id) : null,
    has_meeting_intent: flag(row.hasMeetingIntent),
    vendor: {
      is_last_message_planned: flag(row.isLastMessagePlanned),
      state: row.status !== null && typeof row.status === "object" ? lookup(STATE, row.status.state) || null : null,
      category: category(row.category),
    },
  };
}

// The person on a thread: the identifier Reply holds them under — null once it holds them no longer — and the name
// it shows. Nothing else of them travels.
export function party(contact) {
  if (contact === null || typeof contact !== "object") {
    return null;
  }

  return {
    external_id: contact.isDeleted === true ? null : identifier(contact.id),
    name: text(contact.fullName, 200),
  };
}

export function category(value) {
  if (value === null || typeof value !== "object") {
    return null;
  }

  const id = word(value.id, 64);
  const name = text(value.name, 100);
  return id === null || name === null ? null : { id: id, name: name };
}
