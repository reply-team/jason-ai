// `sender.list`: the mailboxes and LinkedIn accounts of the Reply account a send can go from.
//
// `reply account list` gives them, and in a team that shares its accounts it gives teammates' accounts too, each with
// the Reply user whose it is. So `reply auth whoami` is asked first, once, for the one thing this package reads from it:
// the user the credential signs in as. Under `own` an account is listed only when Reply names that user as its owner —
// a teammate's account, and one Reply names no owner for, are left out, since either may be somebody else's — and under
// `team` every account is listed, each marked. A message sent from a colleague's mailbox goes out under their name, so
// offering one is never a default.
//
// The list is asked for one channel at a time: asked for both at once, Reply refuses the whole list when the account's
// plan or role leaves one out, and a plan without LinkedIn still has mailboxes to send from. A channel left out is said
// beside the others; one named on its own and left out is the operation's refusal.
import { run } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { fill } from "./page.js";
import { cut, text } from "./reading.js";

const OPERATION = "sender.list";
const WHOAMI = "auth whoami";
const LIST = "account list";

// The order Reply's list answers in: the mailboxes first.
const CHANNELS = ["email", "linkedin"];

// The CLI's words for an account's state, which are the document's.
const STATUSES = ["active", "inactive", "disconnected", "other"];

// The CLI's word for a list the account's plan or role does not include, which this operation answers itself when the
// channel was not named on its own.
const NOT_INCLUDED = "access.feature_unavailable";

export function senderList(input, context) {
  const args = input.args;
  const caller = callerOf(context);
  const channels = { email: "not_asked", linkedin: "not_asked" };
  const accounts = [];

  for (const channel of CHANNELS) {
    if (args.sender_channel !== undefined && args.sender_channel !== channel) {
      continue;
    }

    const listed = run(context, {
      operation: OPERATION,
      command: LIST,
      operands: [],
      flags: ["--channel", channel],
      missing: args.sender_channel === undefined ? NOT_INCLUDED : undefined,
    });
    if (listed === null) {
      channels[channel] = "not_included";
      continue;
    }

    if (!Array.isArray(listed.items)) {
      fail(OPERATION, { call: LIST, row: lostAnswerRow(LIST) }, undefined);
    }

    channels[channel] = "listed";
    for (const entry of listed.items) {
      const account = accountOf(entry, channel, caller);
      if (account !== null && (args.owners === "team" || account.owner === "own")) {
        accounts.push(account);
      }
    }
  }

  const rest = { has_more: false, channels: channels, vendor: { ordering: "email_then_linkedin" } };
  const page = fill(accounts.slice(args.offset, args.offset + args.limit), rest);
  return {
    result: {
      items: page.items,
      has_more: page.cut || accounts.length > args.offset + page.items.length,
      channels: channels,
      vendor: rest.vendor,
    },
  };
}

// The Reply user the credential signs in as. Nothing else of the answer is read: the login name beside it is the
// operator's own. An answer that names nobody is no answer, and a read is asked again.
function callerOf(context) {
  const said = run(context, { operation: OPERATION, command: WHOAMI, operands: [], flags: [] });
  const user = said.userId;
  if (typeof user !== "number" || !Number.isSafeInteger(user) || user <= 0) {
    fail(OPERATION, { call: WHOAMI, row: lostAnswerRow(WHOAMI) }, undefined);
  }

  return user;
}

// One account as the document lists it, or null for an entry no send could go from: one without a usable identifier, or
// on another channel than the one asked for. Reply numbers its mailboxes in 64 bits, so an identifier is any whole
// number a JSON number carries exactly.
function accountOf(entry, channel, caller) {
  if (entry === null || typeof entry !== "object" || Array.isArray(entry) || entry.channel !== channel) {
    return null;
  }

  if (typeof entry.id !== "number" || !Number.isSafeInteger(entry.id) || entry.id <= 0) {
    return null;
  }

  const owner = entry.owner_user_id;
  return {
    external_id: String(entry.id),
    channel: channel,
    name: blankless(entry.name, 200),
    address: blankless(entry.address, 500),
    status: STATUSES.indexOf(entry.status) >= 0 ? entry.status : "other",
    owner: owner === caller ? "own" : typeof owner === "number" && Number.isSafeInteger(owner) && owner > 0 ? "teammate" : "unknown",
    vendor: { status: text(entry.reply_status, 32) },
  };
}

// A name or an address, cut to its bound, and null when Reply holds none — one of nothing but white space included.
function blankless(value, max) {
  return typeof value === "string" && value.trim().length > 0 ? cut(value, max) : null;
}
