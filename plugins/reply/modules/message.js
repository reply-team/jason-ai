// `message.send`: send one person one message outside any sequence and any thread, from one of the account's accounts,
// through the reply-cli command made for the format: `reply contact send-email`, or on LinkedIn `contact
// linkedin-message`, `contact linkedin-inmail` or `contact linkedin-voice`, which Reply speaks from the text.
//
// Everything before the send is the path every send outside a sequence takes (send.js): the subject and the text are held
// to what Reply would send as written, the account to the address the person approving read, and the person to the value
// the runtime asked its suppression register about — a person Reply holds nobody for is imported by the same command,
// from their fields beside the text. The document holds each format's elements and lengths — the same lengths the CLI
// holds a LinkedIn text to — so the text that reaches this package is one its format takes. The text goes on stdin and
// nowhere else.
//
// Reply takes an email's body as HTML, and the text a person approved is plain text, so it is written as HTML that shows
// exactly that text: every character HTML reads as markup is escaped, and every line break is one `<br>`. Spaces and tabs
// stay as written, and a mail program shows a run of them as one, as HTML does. The subject is a header, and goes as
// written. A message that may have gone is never sent again: the document says `never`.
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, refuseUnprojected, withOrigin } from "./contacts.js";
import { refusePlaceholders, senderFor, sendTo } from "./send.js";
import { lookup, text } from "./reading.js";

const OPERATION = "message.send";

// Each format: the command made for it, the option that names the account it goes from, and its text on stdin under the
// CLI's own names.
const FORMATS = {
  "email": { command: "contact send-email", account: "--email-account-id", text: message => ({ subject: message.subject, body: html(message.body) }) },
  "linkedin_message": { command: "contact linkedin-message", account: "--linkedin-account-id", text: message => ({ body: message.body }) },
  "linkedin_inmail": { command: "contact linkedin-inmail", account: "--linkedin-account-id", text: message => ({ subject: message.subject, body: message.body }) },
  "linkedin_voice": { command: "contact linkedin-voice", account: "--linkedin-account-id", text: message => ({ script: message.body }) },
};

// What HTML reads as markup, and what it shows each as.
const ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" };

export function messageSend(input, context) {
  const args = input.args;
  const person = input.contacts[0];
  const message = args.message;
  const format = lookup(FORMATS, message.format);

  refuseUnprojected(OPERATION, input);

  // The document and the claim tie a format to its channel; this holds it here too, before anything is called: an email's
  // format is the email channel's, and every other format is LinkedIn's.
  if ((args.channel === "email") !== (message.format === "email")) {
    fail(OPERATION, { call: null, row: "format_of_another_channel" }, undefined);
  }

  refusePlaceholders(OPERATION, [message.subject, message.body]);
  const sender = senderFor(OPERATION, context, args.channel, args.sender);
  const done = sendTo(OPERATION, context, input, format.command, sender, [format.account, sender, "--message", "-"], format.text(message));
  const sent = done.sent;
  const learned = done.learned;

  const provider = contactNumber(sent.contact_id);
  const named = args.channel === "email" ? mailId(sent.message_id) : activityId(sent.activity_id);
  if (sent.status !== "sent" || provider === null || named === undefined) {
    // An answer that does not say the message went, or names it by an identifier of another shape, may come from one
    // that went.
    fail(OPERATION, { call: format.command, row: lostAnswerRow(format.command), provider_item: evidence(sent) }, learned);
  }

  // The contact Reply answered about is the one returned: the runtime records it beside the pin where they differ. Reply
  // answers a direct email with no moment, and a LinkedIn send with one it gives as Reply gives it, which nothing has
  // shown to be UTC, so neither is passed on: `sent_at` is null until one is.
  const answered = { contact: provider };
  return withOrigin({
    result: {
      items: [{
        contact_id: person.id,
        status: "sent",
        channel: args.channel,
        format: message.format,
        sender_external_id: sender,
        message: { external_id: named, sent_at: null },
        external_ids: answered,
      }],
    },
    external_ids: answered,
  }, sent);
}

// The text as HTML that shows exactly it: the characters HTML reads as markup escaped, and each line break — a carriage
// return and a line feed together, either alone, or a Unicode line or paragraph separator — one `<br>`.
function html(written) {
  return written.replace(/[&<>"']/g, character => ESCAPES[character]).replace(/\r\n|[\r\n\u2028\u2029]/g, "<br>");
}

// An email's Message-ID as Reply answers it: a string, null where it gives none or gives one past the output's 998
// characters — cut, it would name nothing — and undefined for anything else.
function mailId(value) {
  if (value === null || value === undefined) {
    return null;
  }

  if (typeof value !== "string") {
    return undefined;
  }

  const kept = text(value, 998);
  return kept === value ? kept : null;
}

// A LinkedIn send's activity as Reply numbers it, written out: null where it gives none, and undefined for anything else.
function activityId(value) {
  if (value === null || value === undefined) {
    return null;
  }

  return typeof value === "number" && Number.isSafeInteger(value) && value > 0 ? String(value) : undefined;
}
