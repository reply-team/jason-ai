// `campaign.enroll`: put one person into a Reply sequence, which is what a campaign is at this provider.
//
// One command, `reply sequence add-contact`. It imports the person when they are given by their fields, reads whether
// they already take part — a person already in the sequence is never moved or re-added, and nothing is written for
// them — and adds them; Reply itself refuses a person its opt-out register holds. An enrollment into a live campaign
// **is a send**, so what this package decides is what the CLI cannot know: whether a person already taking part is
// the answer the caller asked for (`collision: skip`) or a refusal (`refuse`), and that a repeated attempt, which may
// be meeting its own lost write, answers `already_enrolled` whatever the policy.
import { run } from "./cli.js";
import { describe, evidence, fail, lostAnswerRow } from "./errors.js";
import { contactNumber, toEnsure } from "./contacts.js";

const OPERATION = "campaign.enroll";
const COMMAND = "sequence add-contact";

// Reply identifies a sequence with a positive 32-bit integer, so this is what an identifier has to look like
// before there is anything worth asking about.
const SEQUENCE_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_SEQUENCE_NUMBER = 2147483647;

// What the first touch can mean at this provider. `authored_delay` leaves the step's own delay in place; the other
// two ask for it to be skipped — and they are the same request here, because Reply always sends inside the
// sequence's own schedule and publishes no way to bypass a sending window.
const IGNORE_STEP_DELAY = {
  "authored_delay": false,
  "next_open_window": true,
  "immediately": true,
};

export function campaignEnroll(input, context) {
  const contact = input.contacts[0];
  const sequence = identifier(input);
  const known = { campaign: sequence };

  // Refused before anybody is looked up or named: the same arguments build the same impossible call every time.
  if (!Object.prototype.hasOwnProperty.call(IGNORE_STEP_DELAY, input.args.first_touch)) {
    fail(OPERATION, {
      call: null,
      row: "call_could_not_be_built",
      reason: "this version has no word for a first touch of '" + describe(input.args.first_touch) + "'.",
    }, known);
  }

  const named = toEnsure(OPERATION, context, input, known);
  let flags = named.flags;
  if (input.args.start.position === "step") {
    // `first_step` is asked for by saying nothing. A numbered position is the CLI's to resolve: Reply's steps have no
    // order field, so it walks their chain and refuses a position no single step answers.
    flags = flags.concat(["--start-step", String(input.args.start.step)]);
  }

  if (IGNORE_STEP_DELAY[input.args.first_touch]) {
    flags = flags.concat(["--ignore-step-delay"]);
  }

  const added = run(context, {
    operation: OPERATION,
    command: COMMAND,
    operands: [sequence],
    flags: flags,
    stdin: named.stdin,
    learned: named.learned,
  });

  const provider = contactNumber(added.contact_id);
  if (provider === null || typeof added.sequence_active !== "boolean") {
    // The write went out and what came back does not say whom it took, or whether this was a send.
    fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(added) }, named.learned);
  }

  const learned = { contact: provider, campaign: sequence };
  if (added.status === "added") {
    return enrollment(contact.id, "enrolled", added.sequence_active, learned);
  }

  if (added.status === "already_in_sequence") {
    // On a repeated attempt the participation may be this work item's own lost write, so the document's answer is
    // `already_enrolled` whatever the policy. On a first attempt it existed before the call, and the policy decides.
    const repeated = Boolean(context && context.attempt_number > 1);
    if (repeated || input.args.collision === "skip") {
      return enrollment(contact.id, "already_enrolled", added.sequence_active, learned);
    }

    fail(OPERATION, {
      call: COMMAND,
      row: "collision_refused",
      reason: "this person already takes part in the sequence, and the call asked to be refused rather than decide.",
    }, learned);
  }

  fail(OPERATION, { call: COMMAND, row: lostAnswerRow(COMMAND), provider_item: evidence(added) }, learned);
}

// Which sequence this call is about. A pin is worked by whenever there is one, and the identifier a planner supplied
// is what makes the first one.
function identifier(input) {
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const supplied = input.args && input.args.campaign && input.args.campaign.external_id;
  const named = pinned || supplied;

  if (typeof named !== "string" || !SEQUENCE_NUMBER.test(named) || Number(named) > LARGEST_SEQUENCE_NUMBER) {
    // Reply numbers its sequences, so a value that is not one names nothing this account could hold. Nothing is
    // asked of the provider, and in particular nobody is ensured for a campaign that cannot exist.
    fail(OPERATION, { call: null, row: "campaign_not_found" }, undefined);
  }

  return named;
}

// One item per person, with the reason, and the campaign's live state, which is what says whether this was
// bookkeeping or a send. The `contact_id` is the identifier the call was given; Reply's own travels beside it.
function enrollment(contactId, status, live, learned) {
  return {
    result: {
      items: [{ contact_id: contactId, status: status, external_ids: { contact: learned.contact } }],
      campaign_live: live,
    },
    external_ids: learned,
  };
}
