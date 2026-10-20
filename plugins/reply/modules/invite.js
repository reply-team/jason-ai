// `social_invitation.send`: invite one person to connect on LinkedIn, from one of the account's LinkedIn accounts, with a
// note or without one, through `reply contact linkedin-connect`.
//
// Everything before the invitation is the send path every send outside a sequence takes (send.js): the note is held to
// what Reply would send as written, the account to the address the person approving read, and the person to the profile
// the runtime asked its suppression register about. Reply refuses notes over 200 characters; the document holds a note
// to that already, so a longer one never reaches this package, and Reply's own refusal of one is mapped as the backstop.
// The note goes on stdin and nowhere else, and an invitation without one sends no `--message` at all. Reply answers a
// person who is already a connection without sending anything. An invitation that may have gone is never sent again:
// the document says `never`.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber } from "./contacts.js";
import { personFor, refusePlaceholders, senderFor } from "./send.js";

const OPERATION = "social_invitation.send";
const COMMAND = "contact linkedin-connect";

// What Reply answers an invitation with: one went out, or none was needed.
const ANSWERS = ["sent", "already_connected"];

export function socialInvitationSend(input, context) {
  const args = input.args;
  const person = input.contacts[0];

  refusePlaceholders(OPERATION, [args.note]);
  const sender = senderFor(OPERATION, context, "linkedin", args.sender);
  const learned = personFor(OPERATION, context, input);

  const withNote = args.note !== null;
  const sent = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [],
    flags: ["--contact-id", learned.contact, "--linkedin-account-id", sender].concat(withNote ? ["--message", "-"] : []),
    stdin: withNote ? { note: args.note } : undefined,
    learned: learned,
  });

  const activity = sent.activity_id;
  const named = activity === null || activity === undefined ? null : typeof activity === "number" && Number.isSafeInteger(activity) && activity > 0 ? String(activity) : undefined;
  const provider = contactNumber(sent.contact_id);
  if (ANSWERS.indexOf(sent.status) < 0 || provider === null || named === undefined) {
    // An answer that does not say the invitation went, or was not needed, may come from one that went.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(sent) }, learned);
  }

  // The contact Reply answered about is the one returned: the runtime records it beside the pin where they differ.
  // Reply's moment for a LinkedIn send is given as Reply gives it, and nothing has shown it to be UTC, so it is not
  // passed on: `sent_at` is null until it is.
  const answered = { contact: provider };
  return {
    result: {
      items: [{
        contact_id: person.id,
        status: sent.status,
        sender_external_id: sender,
        invitation: { external_id: sent.status === "sent" ? named : null, sent_at: null },
        external_ids: answered,
      }],
    },
    external_ids: answered,
  };
}
