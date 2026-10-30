// `social_invitation.send`: invite one person to connect on LinkedIn, from one of the account's LinkedIn accounts, with a
// note or without one, through `reply contact linkedin-connect`.
//
// Everything before the invitation is the send path every send outside a sequence takes (send.js): the note is held to
// what Reply would send as written, the account to the address the person approving read, and the person to the profile
// the runtime asked its suppression register about — a person Reply holds nobody for is imported by the same command,
// from their fields. Reply refuses notes over 200 characters; the document holds a note to that already, so a longer one
// never reaches this package, and Reply's own refusal of one is mapped as the backstop. The note goes on stdin and
// nowhere else, and an invitation without one sends no `--message` at all. Reply answers a person who is already a
// connection without sending anything.
//
// The invitation is made under the work item's key, and at the moment the arguments name where one is still ahead
// (send.js). The result says Reply took it — `accepted`, the moment it holds it for, its reference and whether it can
// still be cancelled — and never that it went. An invitation that may have gone is repeated under the same key, which
// Reply answers with the send it took: the document says `after_recovery_read`. Where the runtime names the send such an
// attempt returned, Reply's record of it is read first, and what it says answers the work item with nothing sent
// (send.js).
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, refuseUnprojected, withOrigin } from "./contacts.js";
import { acceptance, endedAtReply, readableMoment, recordedSend, refusePlaceholders, senderFor, sendTo, withNamedSend } from "./send.js";

const OPERATION = "social_invitation.send";
const COMMAND = "contact linkedin-connect";

// What Reply answers an invitation with: one went out, or none was needed.
const ANSWERS = ["sent", "already_connected"];

export function socialInvitationSend(input, context) {
  const args = input.args;
  const person = input.contacts[0];

  refuseUnprojected(OPERATION, input);
  refusePlaceholders(OPERATION, [args.note]);

  // A moment this package cannot read ends the attempt before anything is called, as the note does (send.js).
  readableMoment(OPERATION, input);

  // The invitation an earlier attempt returned, where Reply's record of it says what became of it, is the work item's:
  // nothing more is called, and the account is the one the arguments name.
  const recorded = recordedSend(OPERATION, context);
  const sender = recorded !== undefined ? args.sender.external_id : senderFor(OPERATION, context, "linkedin", args.sender);

  const withNote = args.note !== null;
  const done = recorded !== undefined
    ? recorded
    : sendTo(
      OPERATION, context, input, COMMAND, sender,
      ["--linkedin-account-id", sender].concat(withNote ? ["--message", "-"] : []),
      withNote ? { note: args.note } : undefined);
  const call = done.call === undefined ? COMMAND : done.call;
  const learned = done.learned;
  const took = acceptance(done);

  // Where Reply holds the key for another request, the invitation is the one the first request made: its contact is the
  // one the CLI names, else the one this attempt invited, and Reply's answer then — its activity — is not in hand.
  const sent = done.kept !== undefined ? null : done.sent;
  const kept = done.kept !== undefined ? contactNumber(done.kept.ids.contact_id) : null;
  const provider = sent !== null ? contactNumber(sent.contact_id) : kept !== null ? kept : learned !== undefined && typeof learned.contact === "string" ? learned.contact : null;
  const activity = sent === null ? null : sent.activity_id;
  const named = activity === null || activity === undefined ? null : typeof activity === "number" && Number.isSafeInteger(activity) && activity > 0 ? String(activity) : undefined;
  if ((sent !== null && ANSWERS.indexOf(sent.status) < 0) || provider === null || named === undefined || took === undefined) {
    // An answer that does not say Reply took the invitation, or that none was needed, may come from one that went.
    // A reference it names in the document's shape is kept all the same, for the next attempt to read first.
    const said = sent !== null ? sent : done.kept.details;
    fail(OPERATION, { call: call, row: lostAnswerRow(COMMAND), provider_item: evidence(said) }, withNamedSend(learned, said));
  }

  if (took.ended !== undefined) {
    // Reply took the invitation and says what became of it: it failed, or was cancelled. That is final.
    endedAtReply(OPERATION, call, took, { contact: provider });
  }

  // The contact Reply answered about is the one returned: the runtime records it beside the pin where they differ.
  // Reply's moment for a LinkedIn send is given as Reply gives it, and nothing has shown it to be UTC, so it is not
  // passed on: `sent_at` is null until it is. A person who is a connection already was sent nothing, so nothing was
  // taken: no moment, no reference, nothing to cancel.
  const connected = sent !== null && sent.status === "already_connected";
  const given = connected ? {} : took.given;
  const answered = given.reference === undefined ? { contact: provider } : { contact: provider, send: given.reference };
  return withOrigin({
    result: {
      items: [Object.assign({ contact_id: person.id, status: connected ? "already_connected" : took.status }, given, {
        sender_external_id: sender,
        invitation: { external_id: connected || given.cancelable === true ? null : named, sent_at: null },
        external_ids: answered,
      })],
    },
    external_ids: answered,
  }, sent);
}
