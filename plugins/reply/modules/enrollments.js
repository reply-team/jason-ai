// `enrollment.list`: one page of the people a sequence holds, read from the campaign's end, and Reply's own count of
// them beside it.
//
// Two reads and nothing written: the page, then the count. The page is mapped person by person onto the published
// vocabulary, and three of Reply's fields are never mapped at all — the address, the company and the title. A
// result is stored with its work item, and a page of addresses would become a second copy of the audience nobody
// meant to keep.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { fill } from "./page.js";
import { flag, identifier, lookup, sequenceOf, text, utc, word } from "./reading.js";

const OPERATION = "enrollment.list";

// Reply's own word for where a person stands, and the neutral word each one is. An out-of-office pause is a pause:
// the person is not being sent to, and why is the provider's own word, which travels under `vendor`. `inactive` —
// Reply stopped because it lacks data it needs — has no neutral word here, so it is `other` with its word beside it.
const STATUS = {
  "active": "active",
  "paused": "paused",
  "outOfOffice": "paused",
  "finished": "finished",
};

// The refusal Reply gives an account whose role may not read a sequence's people.
const FORBIDDEN = "sequenceContact.forbidden";

export function enrollmentList(input, context) {
  const sequence = sequenceOf(input, OPERATION);
  const learned = { campaign: sequence };
  const path = "/v3/sequences/" + sequence + "/contacts?top=" + input.args.limit + "&skip=" + input.args.offset;
  const known = callOf("GET", path);

  const answer = call(context, "GET", path, undefined, learned, OPERATION);

  if (answer.code === 404) {
    // The identifier deliberately does not travel out as a pin: the provider has just denied the link.
    fail(OPERATION, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    const seen = observe(known, answer, statusRow(answer.code, known));
    if (answer.code === 403 && seen.provider_code === FORBIDDEN) {
      seen.row = "feature_not_included";
    }

    fail(OPERATION, seen, learned);
  }

  const page = answer.data;
  const people = page !== null && typeof page === "object" && Array.isArray(page.items) ? page.items.map(person) : null;
  if (people === null || people.indexOf(null) >= 0) {
    // A 200 whose body is not a page of people is not an answer, whatever the status said. On a read that costs
    // nothing to ask again, it is reported as the provider not having answered.
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  const result = { total: count(context, sequence, learned), items: [], has_more: false, vendor: { ordering: "added_desc" } };
  const filled = fill(people, result);
  result.items = filled.items;
  result.has_more = page.hasMore === true || filled.cut;

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

// Reply's own count of the people in the sequence, or null when it refused to count them. A refusal of the count
// alone does not cost the page that was already read: the document lets `total` be null for exactly that. Except
// one: for a sequence this account does not hold, Reply answers the page with an empty list and only the count says
// so, with its 404 `sequenceContact.sequenceNotFound` — and that is the campaign not being found, not a total
// nobody would give.
function count(context, sequence, learned) {
  const path = "/v3/sequences/" + sequence + "/contacts/count";
  const answer = call(context, "GET", path, undefined, learned, OPERATION);
  if (answer.code === 404 && answer.data !== null && typeof answer.data === "object"
      && typeof answer.data.code === "string" && answer.data.code.toLowerCase() === "sequencecontact.sequencenotfound") {
    fail(OPERATION, observe(callOf("GET", path), answer, "campaign_not_found"), undefined);
  }

  const counted = answer.code === 200 && answer.data !== null && typeof answer.data === "object" ? answer.data.count : undefined;

  return typeof counted === "number" && isFinite(counted) && counted >= 0 && counted <= 2147483647 && Math.floor(counted) === counted
    ? counted
    : null;
}
