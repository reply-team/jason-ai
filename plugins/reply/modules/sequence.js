// `campaign.pause` and `campaign.activate`: the provider's own campaign stopped — the manager's incident response —
// or started sending.
//
// Both read the sequence first, because its state decides whether anything is written at all: a paused sequence is
// already what a pause asks for, a live one what a start asks for, and an archived one is refused either way. Then
// one write, with no body.
//
// A pause is documented idempotent, so a repeat after a lost answer simply reads the state again and answers
// `already_paused`. A start sends: it reads the count as well, and starts nothing unless the count is the population a
// person approved — a different one is a different decision. A repeat after a lost start reads the state first and
// answers `already_live` when the start landed.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { sequenceOf } from "./reading.js";

const PAUSE = "campaign.pause";
const ACTIVATE = "campaign.activate";

// Reply's words for a sequence that is not live, and the state the document names each one.
const LEFT = {
  "new": "draft",
  "paused": "paused",
};

export function campaignPause(input, context) {
  const sequence = sequenceOf(input, PAUSE);
  const learned = { campaign: sequence };
  const state = stateOf(PAUSE, context, sequence, learned);

  if (state.archived) {
    fail(PAUSE, { call: state.call, row: "campaign_not_pausable", reason: "Reply holds this sequence archived." }, learned);
  }

  if (state.status === "new") {
    fail(PAUSE, { call: state.call, row: "campaign_not_pausable", reason: "This sequence has never started, so there is nothing to pause." }, learned);
  }

  if (state.status === "paused") {
    return paused(sequence, "already_paused", learned);
  }

  const path = "/v3/sequences/" + sequence + "/pause";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, undefined, learned, PAUSE);

  if (answer.code === 404) {
    fail(PAUSE, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code === 409 || answer.code === 400) {
    // Archived, or a business rule, between the read and the write: Reply's own refusal is carried with its code
    // rather than guessed at from a state that is no longer true.
    fail(PAUSE, observe(known, answer, "campaign_not_pausable"), learned);
  }

  if (answer.code !== 200) {
    fail(PAUSE, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  return paused(sequence, "paused", learned);
}

export function campaignActivate(input, context) {
  const sequence = sequenceOf(input, ACTIVATE);
  const learned = { campaign: sequence };
  const state = stateOf(ACTIVATE, context, sequence, learned);

  if (state.archived) {
    fail(ACTIVATE, { call: state.call, row: "campaign_not_activatable", reason: "Reply holds this sequence archived." }, learned);
  }

  if (state.status !== "active" && !Object.prototype.hasOwnProperty.call(LEFT, state.status)) {
    fail(ACTIVATE, { call: state.call, row: "refusal_this_version_has_no_word_for", reason: "Reply answered a sequence state this version has no word for." }, learned);
  }

  const counted = countOf(ACTIVATE, context, sequence, learned);
  if (state.status === "active") {
    return started(sequence, "already_live", null, counted.enrolled, learned);
  }

  const expected = input.args.expected.enrolled;
  if (counted.enrolled !== expected) {
    fail(ACTIVATE, { call: counted.call, row: "population_changed", expected: expected, actual: counted.enrolled }, learned);
  }

  const path = "/v3/sequences/" + sequence + "/start";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, undefined, learned, ACTIVATE);

  if (answer.code === 404) {
    fail(ACTIVATE, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code === 409 || answer.code === 400) {
    // Reply's own code travels as it came, whatever it is: this package maps no particular refusal of a start.
    fail(ACTIVATE, observe(known, answer, "campaign_not_activatable"), learned);
  }

  if (answer.code !== 200) {
    fail(ACTIVATE, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  return started(sequence, "activated", LEFT[state.status], counted.enrolled, learned);
}

function started(sequence, status, was, enrolled, learned) {
  return {
    result: { items: [{ campaign_external_id: sequence, status: status, was: was, enrolled: enrolled }] },
    external_ids: learned,
  };
}

// How many people the sequence holds, by Reply's own count. Unlike `enrollment.list`'s total, which is answered as
// null when the count alone is refused, a start cannot go on without it: the count is what the decision was about.
function countOf(operation, context, sequence, learned) {
  const path = "/v3/sequences/" + sequence + "/contacts/count";
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned, operation);

  if (answer.code === 404) {
    fail(operation, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    fail(operation, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  const counted = answer.data !== null && typeof answer.data === "object" ? answer.data.count : undefined;
  if (typeof counted !== "number" || !isFinite(counted) || counted < 0 || counted > 2147483647 || Math.floor(counted) !== counted) {
    fail(operation, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return { call: known, enrolled: counted };
}

function paused(sequence, status, learned) {
  return { result: { items: [{ campaign_external_id: sequence, status: status }] }, external_ids: learned };
}

// The sequence's own state, as Reply words it: `new`, `active` or `paused`, and whether it is archived. A 404 here is
// about the one sequence the path names, so it is the operation's own `campaign_not_found`, and the identifier does
// not travel out as a pin. A 200 that does not say the state is not an answer; this call changes nothing, so it is
// reported as Reply not having answered.
function stateOf(operation, context, sequence, learned) {
  const path = "/v3/sequences/" + sequence;
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned, operation);

  if (answer.code === 404) {
    fail(operation, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    fail(operation, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  const data = answer.data;
  if (data === null || typeof data !== "object" || typeof data.status !== "string" || typeof data.isArchived !== "boolean") {
    fail(operation, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return { call: known, status: data.status, archived: data.isArchived };
}
