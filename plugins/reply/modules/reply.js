// `conversation.reply`: answer the person on a thread, in that thread, through `reply inbox reply`.
//
// The thread is read first, for its channel and its person, and nothing is sent unless both agree with the work item:
// the thread is on the channel the call consumes, and its person is the work item's — the contact pinned for them,
// where there is a pin, and in every case the one whose address on that channel is the channel's value. The runtime
// checked its suppression register for that value at the claim, so this is what makes that check one for exactly the
// person reached. Nobody is created: the person is found through the thread, and pinned once found. Reply answers from
// the identity that owns the thread, so no sender is named, and no other recipient can be.
//
// A reply costs three requests to Reply before its send: `reply inbox get` makes two — the thread, and one page of its
// messages one message long — and `reply inbox reply` reads the thread once more itself, for its channel, before it
// posts. The text reaches the CLI on stdin and nowhere else, and a reply that may have gone is never sent again: the
// document says `never`.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { identifier, isReplyNumber, lookup, text } from "./reading.js";

const OPERATION = "conversation.reply";
const READ = "inbox get";
const SEND = "inbox reply";

// Reply's channel words and the document's.
const CHANNEL = { "email": "email", "linkedIn": "linkedin" };

// Where the thread's person keeps the value each channel reaches them at.
const ADDRESS = { "email": "email", "linkedin": "linkedInProfileUrl" };

// How Reply writes a placeholder it fills in for each person. It renders one in a reply in a thread too, and refuses the
// send when it cannot fill it in, so a text with one is refused before anything is called: what would go out would not
// be what a person approved.
const PLACEHOLDER = "{{";

export function conversationReply(input, context) {
  const args = input.args;
  const person = input.contacts[0];
  const thread = args.thread.external_id;

  if (!isReplyNumber(thread)) {
    // Reply numbers its threads, so anything else names no thread this account could hold.
    fail(OPERATION, { call: null, row: "conversation_not_found" }, undefined);
  }

  if (args.body.indexOf(PLACEHOLDER) >= 0) {
    fail(OPERATION, { call: null, row: "placeholder_in_the_text" }, undefined);
  }

  const read = run(context, {
    operation: OPERATION,
    command: READ,
    operands: [thread],
    flags: ["--limit", "1", "--offset", "0"],
  });

  const head = read.thread !== null && typeof read.thread === "object" && !Array.isArray(read.thread) ? read.thread : null;
  if (head === null) {
    // A result that is not a thread is not an answer; on a read, it is asked again.
    fail(OPERATION, { call: READ, row: lostAnswerRow(READ) }, undefined);
  }

  const channel = lookup(CHANNEL, head.channel) || null;
  if (channel !== args.channel) {
    fail(OPERATION, {
      call: READ,
      row: "channel_mismatch",
      reason: "The thread is " + (channel === null ? "on no channel Reply sends a reply on" : "on " + channel) + ", and the call names " + args.channel + ".",
    }, undefined);
  }

  const found = personOn(head.contact, channel);
  const pin = person.external_ids ? person.external_ids.contact : undefined;
  const why = found === null
    ? "The thread names nobody Reply still holds."
    : pin !== undefined && pin !== null && pin !== found.id
      ? "The thread's person is not the contact pinned for this person."
      : found.address === null
        ? "The thread's person has no " + (channel === "email" ? "address" : "LinkedIn profile") + " at Reply, so the value this work item consumes is not theirs."
        : !sameValue(channel, found.address, person.channels[0].value)
          ? "The thread's person is reached at another " + (channel === "email" ? "address" : "profile") + " than the one this work item consumes."
          : null;
  if (why !== null) {
    // Nobody is pinned: the thread's person is not this one.
    fail(OPERATION, { call: READ, row: "thread_contact_mismatch", reason: why }, undefined);
  }

  const learned = { contact: found.id };
  const message = args.reply_to_message
    ? { body: args.body, reply_to_message_id: args.reply_to_message.external_id }
    : { body: args.body };
  const sent = run(context, {
    operation: OPERATION,
    command: SEND,
    operands: [thread],
    flags: ["--message", "-"],
    stdin: message,
    learned: learned,
  });

  // The CLI answers in the document's own channel words.
  if (sent.status !== "sent" || sent.channel !== channel || !nullOrText(sent.message_id) || !nullOrText(sent.sent_at)) {
    // An answer that does not say the reply was sent may come from a send that happened, so it is a lost answer.
    fail(OPERATION, { call: SEND, row: lostAnswerRow(SEND), provider_item: evidence(sent) }, learned);
  }

  // The provider's send answer stamps a local time as UTC — the account's own zone, written with a `Z` — so it is not
  // passed on: `sent_at` is null, and the message's own `date`, which a read of the thread gives in true UTC, is the
  // moment. No offset is applied, because the zone keeps daylight saving.
  return {
    result: {
      items: [{
        contact_id: person.id,
        status: "sent",
        thread_external_id: thread,
        channel: channel,
        message: { external_id: text(sent.message_id, 998), sent_at: null },
        external_ids: learned,
      }],
    },
    external_ids: learned,
  };
}

// The thread's person, by the identifier Reply holds them under and the value they are reached at on this channel, or
// null when the thread names nobody Reply still holds. The value is null where Reply gives the person none.
function personOn(contact, channel) {
  if (contact === null || typeof contact !== "object" || contact.isDeleted === true) {
    return null;
  }

  const id = identifier(contact.id);
  if (id === null) {
    return null;
  }

  // Reply gives an empty string, not null, for a value the person does not have — a LinkedIn profile, say.
  const address = contact[ADDRESS[channel]];
  return { id: id, address: typeof address === "string" && address.length > 0 ? address : null };
}

// Whether the thread's person is reached at the value this work item consumes, compared the way the runtime keys the
// value for its suppression register, so that the register's check at the claim is about exactly the person reached: an
// address whatever its case, a profile link by the key below. Two values the register keys apart are two people here.
function sameValue(channel, held, consumed) {
  return channel === "linkedin" ? profileOf(held) === profileOf(consumed) : held.toLowerCase() === consumed.toLowerCase();
}

// A profile link as the runtime keys one: its scheme and its host whatever their case, a default port, a user name, a
// fragment and one trailing slash dropped, its query exactly as written — and its path whatever its case on linkedin.com
// and its subdomains, where a profile link names a person by its slug, and exactly as written on any other host.
// `/in/Jane` and `/in/jane` are one profile on linkedin.com, and two anywhere else. A value that is not a link is
// compared as it stands, and so is a letter written outside ASCII, which the runtime keys by its escape: such a profile
// is refused rather than reached.
const LINK = /^([A-Za-z][A-Za-z0-9+.\-]*):\/\/([^\/?#]*)([^?#]*)(\?[^#]*)?(#.*)?$/;

// The port a scheme names by saying nothing.
const DEFAULT_PORT = { "http": "80", "https": "443" };

function profileOf(link) {
  const parts = LINK.exec(link);
  if (parts === null) {
    return link;
  }

  const scheme = parts[1].toLowerCase();
  const authority = parts[2].slice(parts[2].lastIndexOf("@") + 1).toLowerCase();
  const colon = authority.lastIndexOf(":");
  const host = colon >= 0 && authority.indexOf("]") < colon ? authority.slice(0, colon) : authority;
  const port = host.length < authority.length ? authority.slice(host.length + 1) : "";
  const kept = port.length > 0 && port !== DEFAULT_PORT[scheme] ? ":" + port : "";

  const trimmed = parts[3].length > 0 && parts[3].charAt(parts[3].length - 1) === "/" ? parts[3].slice(0, -1) : parts[3];
  const path = host === "linkedin.com" || host.endsWith(".linkedin.com") ? trimmed.toLowerCase() : trimmed;
  return scheme + "://" + host + kept + path + (parts[4] || "");
}

function nullOrText(value) {
  return value === null || value === undefined || typeof value === "string";
}
