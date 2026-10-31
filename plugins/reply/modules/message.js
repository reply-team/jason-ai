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
// written.
//
// The message is made under the work item's key, and at the moment the arguments name where one is still ahead (send.js).
// A message on any channel may also name, in the provider's own object, the Reply sending schedule it is timed by
// (provider.js): it goes by that schedule, at the first opening at or after the moment where one is named too. Reply answers it `queued` with
// the moment where the schedule is open now or opens within ten minutes, and `waitingForSchedule` with only an estimate
// where it opens later; either way it goes when the schedule lets it, so the result names no moment for it. The result says Reply took it — `accepted`, the moment it holds it for, its reference and whether it can still be
// cancelled — and never that it went: whether and when it goes is learned later. A message that may have gone is
// repeated under the same key, which Reply answers with the send it took: the document says `after_recovery_read`. Where
// the runtime names the send such an attempt returned, Reply's record of it is read first, and what it says answers the
// work item with nothing sent (send.js).
//
// A message composed as a template goes by the same command in Reply's own syntax (template.js), which Reply fills in for
// the person when it sends: its text and its fallbacks written as a final text's are, its tags as Reply reads them on the
// channel — an email, a LinkedIn message and an InMail; a voice message is spoken from a final script alone. Reply writes
// a value as the person's field holds it, escaping nothing, so markup in a field is markup in the mail; this package never
// sees the value. A template Reply could not fill in within the call is refused before anything is sent, and one Reply
// sends later — held for a later moment, or queued behind the account's other sends — is filled in when it goes, which
// `message.status` reads.
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, refuseUnprojected, withOrigin } from "./contacts.js";
import { acceptance, endedAtReply, readableMoment, recordedSend, refusePlaceholders, senderFor, sendTo, withNamedSend } from "./send.js";
import { scheduleOf } from "./provider.js";
import { inReplySyntax, refuseTagStart } from "./template.js";
import { lookup, text } from "./reading.js";

const OPERATION = "message.send";

// Each format: the command made for it, the option that names the account it goes from, and its text on stdin under the
// CLI's own names.
const FORMATS = {
  "email": { command: "contact send-email", account: "--email-account-id", text: message => message.compose === "template" ? template(message) : { subject: message.subject, body: html(message.body) } },
  "linkedin_message": { command: "contact linkedin-message", account: "--linkedin-account-id", text: (message, sender) => message.compose === "template" ? { body: onLinkedIn("body", message.body, sender) } : { body: message.body } },
  "linkedin_inmail": { command: "contact linkedin-inmail", account: "--linkedin-account-id", text: (message, sender) => message.compose === "template" ? { subject: onLinkedIn("subject", message.subject, sender), body: onLinkedIn("body", message.body, sender) } : { subject: message.subject, body: message.body } },
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

  // The document holds a voice message to a final script; this holds it here too, before anything is called.
  const composed = message.compose === "template" ? "template" : undefined;
  if (composed !== undefined && message.format === "linkedin_voice") {
    fail(OPERATION, { call: null, row: "template_on_a_voice_script" }, undefined);
  }

  if (composed === undefined) {
    refusePlaceholders(OPERATION, [message.subject, message.body]);
  }

  // Reply decodes an email's body before it reads it, and drops a tag it does not know with everything after it, in the
  // body and in the subject alike, so a `<` that starts one is refused before anything is called, in a final text as in a
  // template. The subject goes as written, and a character reference in it is not read.
  if (composed === undefined && message.format === "email") {
    refuseTagStart(OPERATION, "subject", message.subject);
    refuseTagStart(OPERATION, "body", message.body);
  }

  // A moment this package cannot read ends the attempt before anything is called, as the text does (send.js), and so
  // does a provider object it cannot read (provider.js).
  readableMoment(OPERATION, input);
  const schedule = scheduleOf(OPERATION, input);

  const written = format.text(message, args.sender);

  // The send an earlier attempt returned, where Reply's record of it says what became of it, is the work item's: nothing
  // more is called, and the account is the one the arguments name.
  const recorded = recordedSend(OPERATION, context);
  const sender = recorded !== undefined ? args.sender.external_id : senderFor(OPERATION, context, args.channel, args.sender);
  const done = recorded !== undefined
    ? recorded
    : sendTo(OPERATION, context, input, format.command, sender, [format.account, sender, "--message", "-"], written, composed);
  const call = done.call === undefined ? format.command : done.call;
  const learned = done.learned;
  const took = acceptance(done);

  // A message timed by a schedule goes at the schedule's opening: `queued` with a moment where the schedule is open
  // now or opens within ten minutes, `waitingForSchedule` with only an estimate where it opens later. Neither is a
  // moment Reply holds the message for apart from the schedule, so none is passed on.
  if (schedule !== null && took !== undefined) {
    delete took.given.accepted_for;
  }

  // Where Reply holds the key for another request, the message is the one the first request made: its contact is the
  // one the CLI names, else the one this attempt sent to, and Reply's answer then — its message — is not in hand.
  const sent = done.kept !== undefined ? null : done.sent;
  const kept = done.kept !== undefined ? contactNumber(done.kept.ids.contact_id) : null;
  const provider = sent !== null ? contactNumber(sent.contact_id) : kept !== null ? kept : learned !== undefined && typeof learned.contact === "string" ? learned.contact : null;
  const named = sent === null ? null : args.channel === "email" ? mailId(sent.message_id) : activityId(sent.activity_id);
  if ((sent !== null && sent.status !== "sent") || provider === null || named === undefined || took === undefined) {
    // An answer that does not say Reply took the message, or names it by an identifier of another shape, may come from
    // one that went.
    // A reference it names in the document's shape is kept all the same, for the next attempt to read first.
    const said = sent !== null ? sent : done.kept.details;
    fail(OPERATION, { call: call, row: lostAnswerRow(format.command), provider_item: evidence(said) }, withNamedSend(learned, said));
  }

  if (took.ended !== undefined) {
    // Reply took the message and says what became of it: it failed, or was cancelled. That is final.
    endedAtReply(OPERATION, call, took, { contact: provider });
  }

  // The contact Reply answered about is the one returned: the runtime records it beside the pin where they differ. Reply
  // answers a direct email with no moment, and a LinkedIn send with one it gives as Reply gives it, which nothing has
  // shown to be UTC, so neither is passed on: `sent_at` is null until one is. A message held for its moment has gone
  // nowhere yet, and Reply names none.
  const answered = took.given.reference === undefined ? { contact: provider } : { contact: provider, send: took.given.reference };
  return withOrigin({
    result: {
      items: [Object.assign({ contact_id: person.id, status: took.status }, took.given, {
        channel: args.channel,
        format: message.format,
        sender_external_id: sender,
        message: { external_id: took.given.cancelable === true ? null : named, sent_at: null },
        external_ids: answered,
      })],
    },
    external_ids: answered,
  }, sent);
}

// The text as HTML that shows exactly it: the characters HTML reads as markup escaped, and each line break — a carriage
// return and a line feed together, either alone, or a Unicode line or paragraph separator — one `<br>`.
function html(written) {
  return written.replace(/[&<>"']/g, character => ESCAPES[character]).replace(/\r\n|[\r\n\u2028\u2029]/g, "<br>");
}

// An email composed as a template, in Reply's syntax: the subject as written and the body as HTML, around the tags and
// inside them.
function template(message) {
  return {
    subject: typeof message.subject === "string" ? inReplySyntax(OPERATION, "email", "subject", message.subject, same => same) : message.subject,
    body: inReplySyntax(OPERATION, "email", "body", message.body, html),
  };
}

// A LinkedIn message's or an InMail's subject or body, `part`, composed as a template, in Reply's syntax: its text as
// written, as a final LinkedIn text goes, with the sending account's address the arguments hold written in where the
// template names it, since Reply has no variable for it there.
function onLinkedIn(part, written, sender) {
  return typeof written === "string" ? inReplySyntax(OPERATION, "linkedin", part, written, same => same, { "sender.address": sender.address }) : written;
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
