// `campaign.enroll`: put one person into a Reply sequence, which is what a campaign is at this provider.
//
// This is the most careful of the three operations, and the reason is in the document: an enrollment into a live
// campaign **is a send**, so a blind repeat sends the same person twice and is billed twice. Everything below is
// arranged around that.
//
// The order, and why it is what it is. The provider's own contact is ensured first, because a sequence holds
// Reply's contacts and because a participation cannot be read for a person who has no identifier yet. On any
// attempt after the first the two declared recovery reads are made, in the document's own order — the prior run's
// outcome for this person, then the campaign's live state — and when the participation is already there that
// reading is the answer and nothing is written. Only where there is still a write ahead is Reply's opt-out
// register read, because an opt-out that arrived after the enrollment already landed would otherwise turn a
// finished send into a permanent failure. Then, and only then, the enrolment.
//
// **The first recovery read has no true counterpart in v3.** The document asks for "the per-item outcome of the
// prior run under this idempotency key", and Reply has neither an idempotency key nor a per-run ledger. The
// nearest answer it can give is the participation itself — `GET /v3/sequences/{id}/contacts/{contact_id}`, where a
// 404 carrying `sequenceContact.notInSequence` is the definitive "not enrolled" — so that is what is read. It
// cannot tell this work item's own earlier enrollment from one somebody else made an hour ago, and it is a
// substitution rather than the thing the document named. That is a limitation of this version, stated here rather
// than papered over.
//
// **The order differs by whether Jason holds a pin for the person.** With one, the two reads come first and the
// enrolment follows them. Without one, ensure-contact necessarily comes first and is itself a write, because
// there is no identifier to read a participation by. So the claim this package makes is that **both recovery
// reads happen before the enroll write** — never that they happen before anything is written, which in the
// second order would be plainly false.
import { call } from "./cli.js";
import { callOf, describe, fail, lostAnswerRow, observe, refusalRow, statusRow } from "./errors.js";
import { ADDRESS_FIELD, ensureContact, refuseIfSuppressed } from "./contacts.js";

const OPERATION = "campaign.enroll";

// Reply identifies a sequence with a positive 32-bit integer, so this is what an identifier has to look like
// before there is anything worth asking about.
const SEQUENCE_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_SEQUENCE_NUMBER = 2147483647;

// The definitive "this person does not take part": the one 404 that answers the recovery read rather than
// failing it.
const NOT_IN_SEQUENCE = "sequencecontact.notinsequence";

// The two business refusals that mean the sequence will take no enrollment at all, whichever call meets them.
const NOT_ENROLLABLE = ["sequence.archived", "sequencecontact.nostepsinsequence"];

// Reply's per-item word for one person the bulk enrol would not take, and the row each one is. The published
// table is labelled "Common" and the type behind it — `SequenceContactError` — is named in the description and
// defined nowhere, so this is an open set: a word that is not here falls to the default branch and is reported
// as itself rather than mapped to whichever row looks nearest. The keys are lower-cased because the one other
// per-item vocabulary in this API is documented in two different casings, so matching on case would be matching
// on which page was read.
const NOT_PROCESSED = {
  "contactnotfound": "contact_not_found",
  "contactlimitexceeded": "limit_reached",
  "forbidden": "unauthorized",
  "invalidinput": "request_rejected_as_invalid",
};

// The collision itself, which is the one per-item word whose meaning is the caller's to decide.
const ALREADY_IN_SEQUENCE = "contactalreadyinsequence";

// What the first touch can mean at this provider. `authored_delay` leaves the step's own delay in place; the
// other two ask for it to be skipped — and they are the same request here, because Reply always sends inside the
// sequence's own schedule and publishes no way to bypass a sending window. The two therefore mean the same thing
// at Reply, and this table says so rather than the package pretending to a precision the API does not have.
const IGNORE_STEP_DELAY = {
  "authored_delay": false,
  "next_open_window": true,
  "immediately": true,
};

// The SDK reads request objects strictly, so a key whose value is undefined is dropped rather than passed on.
function compact(request) {
  const cleaned = {};
  for (const key of Object.keys(request)) {
    if (request[key] !== undefined) {
      cleaned[key] = request[key];
    }
  }

  return cleaned;
}

export function campaignEnroll(input, context) {
  const contact = input.contacts[0];
  const sequence = identifier(input);

  // The precondition every write in this package shares: the provider's own contact, worked by the pin where
  // there is one and ensured from the channel value only where there is not. The campaign is already known —
  // the argument named it and this operation pins it — so it travels with any failure the ensure meets,
  // including the lost answer to the import, which is this operation's one ambiguous ending from before the
  // person has an identifier at all.
  const ensured = ensureContact(OPERATION, context, input, { campaign: sequence });

  // What this package now knows Reply calls our two entities. It travels out with a failure as well as with an
  // answer, because after a lost answer the pins are the only trace of what the attempt did — and without the
  // contact's one the next attempt has nobody to read the prior outcome for.
  const learned = { contact: ensured, campaign: sequence };

  // The first declared recovery read, made as a call rather than reasoned about, on every attempt after the
  // first. It is what makes one crash cost one enrollment rather than two.
  const already = context && context.attempt_number > 1 && participates(context, sequence, ensured, learned);

  // The second, and on the first attempt the only read there is: the sequence itself. It answers whether this
  // was a send, whether the campaign will take an enrollment at all, and — for a numbered start position — the
  // shape the steps are in.
  const read = liveState(context, sequence, learned);
  const live = isLive(read);

  if (already) {
    // The document's own instruction: answer from that reading when the effect already happened. Nothing else
    // is weighed — not the collision policy, whose subject is a participation that existed before this call,
    // and not the opt-out register, because there is no write ahead for it to forbid.
    return enrollment(contact.id, "already_enrolled", live, learned);
  }

  if (read.isArchived === true) {
    fail(
      OPERATION,
      {
        call: callOf("GET", sequencePath(sequence)),
        row: "campaign_not_enrollable",
        reason: "the sequence is archived at Reply, and an archived sequence takes no contacts.",
      },
      learned);
  }

  const request = enrolment(read, input.args, ensured, learned);

  // Only now, with a write still to make: a person who asked not to be contacted must not be enrolled into a
  // campaign that may send to them this afternoon, and only the provider knows who asked.
  refuseIfSuppressed(OPERATION, context, ensured, learned);

  return enrollment(contact.id, enrol(context, sequence, request, ensured, input.args.collision, learned), live, learned);
}

// Which sequence this call is about. A pin is worked by whenever there is one — that is what a link is for — and
// the identifier a planner supplied is what makes the first one.
function identifier(input) {
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const supplied = input.args && input.args.campaign && input.args.campaign.external_id;
  const named = pinned || supplied;

  if (typeof named !== "string" || !SEQUENCE_NUMBER.test(named) || Number(named) > LARGEST_SEQUENCE_NUMBER) {
    // Reply numbers its sequences, so a value that is not one names nothing this account could hold under any
    // circumstance. Nothing is asked of the provider, and in particular nobody is ensured for a campaign that
    // cannot exist.
    fail(OPERATION, { call: null, row: "campaign_not_found" }, undefined);
  }

  return named;
}

function sequencePath(sequence) {
  return "/v3/sequences/" + sequence;
}

/**
 * The first recovery read: does this person already take part in the sequence? One call answers it, and a 404
 * carrying `sequenceContact.notInSequence` is the definitive no. Any other 404 is not that answer, so it is
 * reported rather than read as a no — treating an unrecognised absence as "not enrolled" is exactly how a second
 * send happens.
 */
function participates(context, sequence, contactId, learned) {
  const path = sequencePath(sequence) + "/contacts/" + contactId;
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned);

  if (answer.code === 404) {
    const said = businessCode(answer);
    if (said === NOT_IN_SEQUENCE) {
      return false;
    }

    if (said === "sequence.notfound") {
      fail(OPERATION, observe(known, answer, "campaign_not_found"), withoutCampaign(learned));
    }

    if (said === "contact.notfound") {
      fail(OPERATION, observe(known, answer, "contact_not_found"), learned);
    }

    fail(OPERATION, observe(known, answer, "refusal_this_version_has_no_word_for"), learned);
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  if (!isObject(answer.data)) {
    // A 200 that does not answer the question asked is not an answer. Nothing has been written on this attempt
    // and this call changes nothing, so it is asked again — reading silence as "not enrolled" would turn the
    // reading meant to prevent a second send into the cause of one.
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return true;
}

/**
 * The second recovery read, and on a first attempt the only one: the sequence itself. `campaign_live` comes from
 * it, and so does the shape of the steps a numbered start position has to be resolved against — one call, because
 * both questions are about the same reading of the same campaign at the same moment.
 */
function liveState(context, sequence, learned) {
  const path = sequencePath(sequence);
  const known = callOf("GET", path);
  const answer = call(context, "GET", path, undefined, learned);

  if (answer.code === 404) {
    // The provider has just denied the link, so the identifier does not travel out as a pin: recording one it
    // denies is worse than recording none.
    fail(OPERATION, observe(known, answer, "campaign_not_found"), withoutCampaign(learned));
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, enrollabilityRow(answer, known)), learned);
  }

  if (!isObject(answer.data)) {
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  return answer.data;
}

// Whether the campaign was live at the moment of the enrollment, which is what decides whether this was a send.
// Reply spells it as two fields — a status that says the sequence is running, and a flag beside it that says it
// was put away — and both have to be right for a message to go.
function isLive(read) {
  return read.status === "active" && read.isArchived !== true;
}

// The one call this operation exists to make. The identifiers travel in the body, so nothing about this person
// reaches an argument.
function enrol(context, sequence, request, contactId, collision, learned) {
  const path = sequencePath(sequence) + "/contact-links/bulk";
  const known = callOf("POST", path);
  const answer = call(context, "POST", path, request, learned);

  if (answer.code === 404) {
    // The path names the sequence and the body names the contact, and the contact was resolved before this, so
    // a 404 here is about the sequence.
    fail(OPERATION, observe(known, answer, "campaign_not_found"), withoutCampaign(learned));
  }

  if (answer.code !== 200) {
    const row = notEnrollable(answer) ? "campaign_not_enrollable" : refusalRow(answer, known, ADDRESS_FIELD);
    fail(OPERATION, observe(known, answer, row), learned);
  }

  const refused = notProcessed(answer, known, contactId, learned);
  if (refused === undefined) {
    // The answer names the people it would not take; this person is not among them, so the enrollment happened.
    return "enrolled";
  }

  const word = wordFor(refused);
  if (word === ALREADY_IN_SEQUENCE) {
    // The one per-item word whose meaning the caller decides, which is why `collision` may not be defaulted:
    // `skip` asked for the existing participation to be left alone, and that is what happened.
    if (collision === "skip") {
      return "already_enrolled";
    }

    refuse(known, answer, "collision_refused", refused, learned);
  }

  refuse(known, answer, NOT_PROCESSED[word] || "refusal_this_version_has_no_word_for", refused, learned);
}

// What the bulk answer said about this one person, or `undefined` when it said nothing — which is what a person
// it took looks like. A 200 whose shape cannot be read at all is the expensive ending: the write went out, so
// the next attempt reads the participation before it writes again.
function notProcessed(answer, known, contactId, learned) {
  const read = isObject(answer.data) ? answer.data : null;
  const refusals = read === null ? null : read.notProcessed;

  if (read === null || (refusals !== undefined && refusals !== null && !isObject(refusals))) {
    const seen = observe(known, answer, lostAnswerRow(known));
    seen.provider_item = describe(answer.data);
    fail(OPERATION, seen, learned);
  }

  return isObject(refusals) && Object.prototype.hasOwnProperty.call(refusals, contactId)
    ? refusals[contactId]
    : undefined;
}

// Reply's own word inside a per-item refusal, lower-cased for the match. It is read where it is a string, and
// under either of the two names the description uses for it; anything else has no word to read, which is the
// default branch's business rather than a reason to throw.
function wordFor(refused) {
  if (typeof refused === "string") {
    return refused.toLowerCase();
  }

  if (isObject(refused)) {
    if (typeof refused.error === "string") {
      return refused.error.toLowerCase();
    }

    if (typeof refused.code === "string") {
      return refused.code.toLowerCase();
    }
  }

  return "";
}

// One person refused inside an answer that reported success. Whatever Reply said about them travels in the
// details, mapped or not: the mapping is this package's reading, and the provider's own word is the evidence.
function refuse(known, answer, row, refused, learned) {
  const seen = observe(known, answer, row);
  seen.provider_item = describe(refused);
  fail(OPERATION, seen, learned);
}

// The whole request, and every decision it carries stated rather than left to Reply's defaults.
function enrolment(read, args, contactId, learned) {
  return compact({
    contactIds: [Number(contactId)],
    // Always false, and written out rather than left off. Reply's own switch would take this person out of
    // every other sequence they are in; nothing in the contract asked for that, and doing it silently would be
    // an external effect nobody requested — an outreach programme quietly cancelled by an enrollment.
    removeFromExisting: false,
    ignoreStepDelay: ignoreStepDelay(args.first_touch, learned),
    // `first_step` is asked for by saying nothing: Reply's own idea of where its sequence begins is better than
    // this package walking the chain to name a step the provider would have chosen anyway.
    startStepId: args.start.position === "step" ? stepAt(read, args.start.step, learned) : undefined,
  });
}

function ignoreStepDelay(firstTouch, learned) {
  if (!Object.prototype.hasOwnProperty.call(IGNORE_STEP_DELAY, firstTouch)) {
    refuseTheCall("this version has no word for a first touch of '" + describe(firstTouch) + "'.", learned);
  }

  return IGNORE_STEP_DELAY[firstTouch];
}

/**
 * The Nth step of a sequence, for the one argument Reply cannot answer directly.
 *
 * A Reply step has no ordering field at all — no `order`, no number — and no endpoint maps a position to a step
 * id. What a sequence has is a graph of `parentId`s with branch labels like "2A", so "the third step" has a
 * sound meaning only while those steps form a single chain. This walks that chain from the step nobody is the
 * parent of and takes the Nth link. Where the chain forks, ends early, or reaches a condition, there is no Nth
 * step to name and the call is refused — because taking the Nth element of the array instead would be a guess,
 * and the cost of that guess is a real person receiving the wrong message.
 */
function stepAt(read, position, learned) {
  const steps = Array.isArray(read.steps) ? read.steps.filter(isObject) : [];
  if (steps.length === 0) {
    refuseTheCall("this sequence publishes no steps, so it has no step " + position + " to start at.", learned);
  }

  const roots = steps.filter(step => step.parentId === null || step.parentId === undefined);
  if (roots.length !== 1) {
    refuseTheCall(
      "this sequence has " + roots.length + " steps with no parent, so its steps are not one chain and no "
      + "position numbers them.",
      learned);
  }

  let at = roots[0];
  for (let index = 1; index <= steps.length; index++) {
    if (typeof at.type === "string" && at.type.toLowerCase() === "condition") {
      refuseTheCall(
        "step " + index + " of this sequence is a condition, so the steps after it are branches rather than a "
        + "chain and nothing is step " + position + ".",
        learned);
    }

    if (index === position) {
      if (!Number.isInteger(at.id)) {
        refuseTheCall("step " + position + " of this sequence carries no identifier to start at.", learned);
      }

      return at.id;
    }

    const next = steps.filter(step => step.parentId === at.id);
    if (next.length === 0) {
      refuseTheCall("this sequence's chain ends at step " + index + ", so it has no step " + position + ".", learned);
    }

    if (next.length > 1) {
      refuseTheCall(
        "step " + index + " of this sequence has " + next.length + " steps after it, so no single step is step "
        + position + ".",
        learned);
    }

    at = next[0];
  }

  // A chain cannot be longer than the steps it is made of, so arriving here means they lead back into one
  // another. Nothing about that shape can be numbered either.
  refuseTheCall("this sequence's steps lead back into one another, so no position numbers them.", learned);
}

// The package's own refusal, for the endings where the call could not be built at all. Nothing was sent and
// nothing can be: the same arguments build the same impossible call on every attempt. What the call learned on
// the way here still travels: by this point a contact may have been created at Reply moments ago, and a refusal
// that dropped that pin would leave the person behind at the provider with nothing in Jason pointing at them.
function refuseTheCall(reason, learned) {
  fail(OPERATION, { call: null, row: "call_could_not_be_built", reason: reason }, learned);
}

// Whether a refusal says the sequence will take no enrollment whatever is asked of it.
function notEnrollable(answer) {
  return NOT_ENROLLABLE.indexOf(businessCode(answer)) >= 0;
}

function enrollabilityRow(answer, known) {
  return notEnrollable(answer) ? "campaign_not_enrollable" : statusRow(answer.code, known);
}

// Reply's own code for a business refusal, lower-cased for the match. A 401 arrives with an empty body, so this
// reads a code only where there is an object to read one from.
function businessCode(answer) {
  return isObject(answer.data) && typeof answer.data.code === "string" ? answer.data.code.toLowerCase() : null;
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

// What is left to record when the provider has denied the campaign itself. Whatever was learned about the
// person still travels; the link the provider says it does not hold does not.
function withoutCampaign(learned) {
  return learned.contact === undefined ? undefined : { contact: learned.contact };
}

// One item per person, with the reason — a partial success reported as a single verdict is a defect — and the
// campaign's live state, which is what says whether this was bookkeeping or a send. The `contact_id` is the
// identifier the call was given, so the caller can match the answer to the person; Reply's own travels beside
// it. This operation's output schema is closed: there is no `vendor` bag here for anything of Reply's to ride
// along in, unlike `campaign.get`.
function enrollment(contactId, status, live, learned) {
  return {
    result: {
      items: [{ contact_id: contactId, status: status, external_ids: { contact: learned.contact } }],
      campaign_live: live,
    },
    external_ids: learned,
  };
}
