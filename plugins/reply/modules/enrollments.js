// `enrollment.list`: one page of the people a sequence holds, read from the campaign's end, and Reply's own count of
// them beside it.
//
// One command answers both: `reply sequence contacts` returns the page and the total. The page is mapped person by
// person onto the published vocabulary, and three of Reply's fields are never mapped at all — the address, the company
// and the title. A result is stored with its work item, and a page of addresses would become a second copy of the
// audience nobody meant to keep.
import { run } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { fill } from "./page.js";
import { flag, identifier, lookup, sequenceOf, text, utc, word } from "./reading.js";

const OPERATION = "enrollment.list";
const COMMAND = "sequence contacts";

// Reply's own word for where a person stands, and the neutral word each one is. An out-of-office pause is a pause:
// the person is not being sent to, and why is the provider's own word, which travels under `vendor`. `inactive` —
// Reply stopped because it lacks data it needs — has no neutral word here, so it is `other` with its word beside it.
const STATUS = {
  "active": "active",
  "paused": "paused",
  "outOfOffice": "paused",
  "finished": "finished",
};

export function enrollmentList(input, context) {
  const sequence = sequenceOf(input, OPERATION);
  const learned = { campaign: sequence };
  const page = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [sequence],
    flags: ["--limit", String(input.args.limit), "--offset", String(input.args.offset)],
    learned: learned,
  });

  const people = Array.isArray(page.items) ? page.items.map(person) : null;
  if (people === null || people.indexOf(null) >= 0 || typeof page.has_more !== "boolean") {
    // A result that is not a page of people is not an answer. On a read that costs nothing to ask again.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND) }, learned);
  }

  const result = { total: count(page.total), items: [], has_more: false, vendor: { ordering: "added_desc" } };
  const filled = fill(people, result);
  result.items = filled.items;
  result.has_more = page.has_more || filled.cut;

  return { result: result, external_ids: learned };
}

// One person as the document publishes them, or null when the row names nobody — which makes the whole page
// unreadable rather than a page with a hole in it.
function person(row) {
  if (row === null || typeof row !== "object" || identifier(row.contactId) === null) {
    return null;
  }

  const state = typeof row.statusInSequence === "string" ? row.statusInSequence : "";
  const step = row.currentStep !== null && typeof row.currentStep === "object" ? row.currentStep : {};
  const disposition = row.emailDisposition !== null && typeof row.emailDisposition === "object" ? row.emailDisposition : null;

  return {
    external_id: identifier(row.contactId),
    first_name: text(row.firstName, 200),
    last_name: text(row.lastName, 200),
    status: lookup(STATUS, state) || "other",
    current_step: text(step.displayName, 64),
    added_at: utc(row.addingDate),
    opted_out: row.isOptedOut === true,
    vendor: {
      status_in_sequence: word(state, 32) || "",
      current_step_id: word(step.stepId, 64),
      call_status: word(row.callStatus, 32),
      meeting_status: word(row.meetingStatus, 32),
      email_disposition: disposition === null ? null : {
        is_replied: flag(disposition.isReplied) === true,
        is_bounced: flag(disposition.isBounced) === true,
      },
    },
  };
}

// Reply's own count, or null where it would not count them: the document lets `total` be null for exactly that.
function count(value) {
  return typeof value === "number" && isFinite(value) && value >= 0 && value <= 2147483647 && Math.floor(value) === value
    ? value
    : null;
}
