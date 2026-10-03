// `conversation.list` and `conversation.get`: Reply's inbox, read.
//
// A thread is Reply's own resource, and both reads work by identifiers alone: a person by the pin Jason holds, a
// campaign by its sequence, a thread by the identifier the list returned. What Reply says about the person on a
// thread is mapped to two things — their identifier and the name it shows — and never to the address, the phone,
// the profile URL, the company or the title that come with them.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { fill } from "./page.js";
import { cut, flag, identifier, isReplyNumber, lookup, text, utc, word } from "./reading.js";

const LIST = "conversation.list";
const GET = "conversation.get";

// What a message's body may be, and how many addresses each recipient list carries.
const BODY = 12000;
const RECIPIENTS = 10;

// The refusal Reply gives an account whose plan or role does not include the inbox.
const FORBIDDEN = "inboxThread.forbidden";

const CHANNEL = { "email": "email", "linkedIn": "linkedin" };
const STATE = { "ok": "ok", "needsAttention": "needs_attention" };
const AI_SENT = { "aiGenerated": "ai_generated", "aiGeneratedEdited": "ai_generated_edited", "autoSent": "auto_sent" };

export function conversationList(input, context) {
  const person = Array.isArray(input.contacts) && input.contacts.length > 0 ? input.contacts[0] : null;
  const filter = {};
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

    filter.contactIds = [Number(pin)];
  } else {
    const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
    const supplied = input.args.campaign && input.args.campaign.external_id;
    const sequence = pinned || supplied;
    if (!isReplyNumber(sequence)) {
      fail(LIST, { call: null, row: "identifier_names_nothing", reason: "The campaign's identifier is not a Reply sequence number." }, undefined);
    }

    filter.sequenceIds = [Number(sequence)];

    // Only a read that made the first link returns the campaign's identifier: a pin is never written twice.
    learned = pinned ? undefined : { campaign: sequence };
  }

  if (input.args.source === "inbound") {
    filter.source = "inbox";
  }

  if (typeof input.args.since === "string") {
    filter.from = input.args.since;
  }

  const path = "/v3/inbox/threads/filter?top=" + input.args.limit + "&skip=" + input.args.offset;
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, filter, learned, LIST);

  if (answer.code !== 200) {
    // A campaign Reply does not hold answers an empty page, not a refusal, so there is no `*_not_found` here. A
    // contact identifier Reply does not hold is refused, as a 400 with no code of its own, which is the refusal
    // this version has no word for.
    const seen = observe(known, answer, statusRow(answer.code, known));
    if (answer.code === 403 && seen.provider_code === FORBIDDEN) {
      seen.row = "feature_not_included";
    }

    fail(LIST, seen, learned);
  }

  const page = answer.data;
  const threads = page !== null && typeof page === "object" && Array.isArray(page.items) ? page.items.map(listed) : null;
  if (threads === null || threads.indexOf(null) >= 0) {
    fail(LIST, observe(known, answer, lostAnswerRow(known)), learned);
  }

  const result = { items: [], has_more: false, vendor: { ordering: "last_activity_desc" } };
  const filled = fill(threads, result);
  result.items = filled.items;
  result.has_more = page.hasMore === true || filled.cut;

  return learned === undefined ? { result: result } : { result: result, external_ids: learned };
}

export function conversationGet(input, context) {
  const thread = input.args.thread.external_id;
  if (!isReplyNumber(thread)) {
    // Reply numbers its threads, so anything else names no thread this account could hold.
    fail(GET, { call: null, row: "conversation_not_found" }, undefined);
  }

  // The header first: the thread itself, with its whole history inline — which is why a history too long to read
  // whole fails here, as too large, and never as a page.
  const header = read(context, "/v3/inbox/threads/" + thread);
  const messages = read(context, "/v3/inbox/threads/" + thread + "/messages?top=" + input.args.limit + "&skip=" + input.args.offset);

  if (header.thread === null) {
    // A 200 whose body is not a thread is not an answer; on a read, it is asked again.
    fail(GET, observe(header.known, header.answer, lostAnswerRow(header.known)), undefined);
  }

  const page = messages.data;
  const entries = page !== null && typeof page === "object" && Array.isArray(page.items) ? page.items.map(message) : null;
  if (entries === null || entries.indexOf(null) >= 0) {
    fail(GET, observe(messages.known, messages.answer, lostAnswerRow(messages.known)), undefined);
  }

  const result = { thread: header.thread, messages: [], has_more: false, vendor: { ordering: "oldest_first" } };
  const filled = fill(entries, result);
  result.messages = filled.items;
  result.has_more = page.hasMore === true || filled.cut;

  // Nothing is pinned: the thread named itself, and its person is reported, never linked.
  return { result: result };
}

// One read of the thread, refused in the operation's own words where Reply has them.
function read(context, path) {
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, undefined, GET);

  if (answer.code === 404) {
    fail(GET, observe(known, answer, "conversation_not_found"), undefined);
  }

  if (answer.code !== 200) {
    const seen = observe(known, answer, statusRow(answer.code, known));
    if (answer.code === 403 && seen.provider_code === FORBIDDEN) {
      seen.row = "feature_not_included";
    }

    fail(GET, seen, undefined);
  }

  return { known: known, answer: answer, data: answer.data, thread: header(answer.data) };
}

// The thread as the document publishes it, from Reply's header. Reply's thread carries no subject of its own, so
// the subject is the one the last email message of its history carries; a channel without subjects has none.
function header(data) {
  if (data === null || typeof data !== "object" || Array.isArray(data) || identifier(data.id) === null) {
    return null;
  }

  const history = Array.isArray(data.messages) ? data.messages : [];
  let subject = null;
  for (let at = history.length - 1; at >= 0 && subject === null; at--) {
    const entry = history[at];
    if (entry !== null && typeof entry === "object" && entry.channel === "email") {
      subject = text(entry.subject, 1000);
    }
  }

  return {
    external_id: identifier(data.id),
    channel: lookup(CHANNEL, data.channel) || "other",
    subject: subject,
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
