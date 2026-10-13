// `campaign.pause` and `campaign.activate`: the provider's own campaign stopped — the manager's incident response —
// or started sending.
//
// Each is one command. A pause is idempotent at Reply, so a sequence already paused answers `paused` again, and a
// repeat after a lost answer simply runs it again. A start sends: the command starts nothing unless the sequence still
// holds the population a person approved, and it answers `already_active` for a sequence a lost start had already
// started. The state a start left — draft or paused — is the CLI's `previous_status`, read before the start, because
// Reply's own answer to a start describes the sequence after it.
import { run } from "./cli.js";
import { describe, evidence, fail, lostAnswerRow } from "./errors.js";
import { lookup, sequenceOf } from "./reading.js";

const PAUSE = "campaign.pause";
const ACTIVATE = "campaign.activate";
const PAUSE_COMMAND = "sequence pause";
const START_COMMAND = "sequence start";

// Reply's words for a sequence that is not live, and the state the document names each one.
const LEFT = {
  "new": "draft",
  "paused": "paused",
};

export function campaignPause(input, context) {
  const sequence = sequenceOf(input, PAUSE);
  const learned = { campaign: sequence };
  const done = run(context, { operation: PAUSE, command: PAUSE_COMMAND, operands: [sequence], learned: learned });

  if (done.status !== "paused") {
    // The write went out and what came back is not the answer the command documents.
    fail(PAUSE, { call: PAUSE_COMMAND, row: lostAnswerRow(PAUSE_COMMAND), provider_item: evidence(done) }, learned);
  }

  return { result: { items: [{ campaign_external_id: sequence, status: "paused" }] }, external_ids: learned };
}

export function campaignActivate(input, context) {
  const sequence = sequenceOf(input, ACTIVATE);
  const learned = { campaign: sequence };
  const done = run(context, {
    operation: ACTIVATE,
    command: START_COMMAND,
    operands: [sequence],
    // The approval guard: the CLI starts nothing unless the sequence holds exactly the population a person approved.
    flags: ["--expect-contacts", String(input.args.expected.enrolled)],
    learned: learned,
  });

  if (done.status === "already_active") {
    if (!isCount(done.contacts)) {
      fail(ACTIVATE, { call: START_COMMAND, row: "count_not_answered" }, learned);
    }

    return started(sequence, "already_live", null, done.contacts, learned);
  }

  if (done.status === "started" && isCount(done.contacts)) {
    const was = lookup(LEFT, done.previous_status);
    if (was === undefined) {
      fail(ACTIVATE, {
        call: START_COMMAND,
        row: "started_from_a_state_with_no_word",
        reason: "Reply started the sequence from its state '" + describe(done.previous_status) + "', which this version has no word for.",
      }, learned);
    }

    return started(sequence, "activated", was, done.contacts, learned);
  }

  fail(ACTIVATE, { call: START_COMMAND, row: lostAnswerRow(START_COMMAND), provider_item: evidence(done) }, learned);
}

function started(sequence, status, was, enrolled, learned) {
  return {
    result: { items: [{ campaign_external_id: sequence, status: status, was: was, enrolled: enrolled }] },
    external_ids: learned,
  };
}

function isCount(value) {
  return typeof value === "number" && isFinite(value) && value >= 0 && value <= 2147483647 && Math.floor(value) === value;
}
