// `campaign.get`: one read of the sequence a campaign is, and the mapping of Reply's own state onto the
// vocabulary the contract publishes.
//
// Reply has no campaign resource. A campaign is a **sequence**, so `GET /v3/sequences/{id}` is the whole of this
// operation: one call, nothing written, nothing to recover. The document's repeat rule is `safe` for exactly that
// reason — an attempt that ended without an answer simply asks again, and there is no recovery read here.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";

const OPERATION = "campaign.get";

// Reply's own word for a sequence's state, and the neutral word each one is. `active` is `live` here: the answer
// speaks the output schema's vocabulary — `draft | live | paused | archived | other` — and Reply's own word
// travels untranslated under `vendor` rather than into a field a planner reasons over.
const STATUS = {
  "new": "draft",
  "active": "live",
  "paused": "paused",
};

// Reply identifies a sequence with a positive 32-bit integer, so this is what an identifier has to look like
// before there is anything worth asking about.
const SEQUENCE_NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_SEQUENCE_NUMBER = 2147483647;

// What the output schema allows a campaign's name. Reply publishes no limit of its own, so the two can disagree.
const MAX_NAME = 500;

export function campaignGet(input, context) {
  const sequence = identifier(input);
  const path = "/v3/sequences/" + sequence;
  const known = callOf("GET", path);

  // What this package now knows Reply calls this campaign. It travels out with a failure as well as with an
  // answer, because a read that resolved the campaign and then lost its answer should still leave the link
  // behind — and after a lost answer a pin is the only trace that anything happened at all.
  const learned = { campaign: sequence };

  const answer = call(context, "GET", path, undefined, learned);

  if (answer.code === 404) {
    // Reply holds no sequence under this identifier. The identifier deliberately does not travel out as a pin:
    // the provider has just denied the link, and recording one it denies is worse than recording none.
    fail(OPERATION, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    fail(OPERATION, observe(known, answer, statusRow(answer.code, known)), learned);
  }

  const sequenceRead = answer.data;
  if (sequenceRead === null || typeof sequenceRead !== "object" || Array.isArray(sequenceRead)) {
    // A 200 whose body is not a sequence is not an answer, whatever the status said. On a read that costs
    // nothing to ask again, so it is reported as the provider not having answered rather than as a shape this
    // package invented a meaning for.
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  // Being archived is a boolean beside the status at Reply rather than one of its values, so it decides first:
  // an archived sequence is `archived` here whatever its status says.
  const archived = sequenceRead.isArchived === true;
  const state = typeof sequenceRead.status === "string" ? sequenceRead.status : "";
  const named = typeof sequenceRead.name === "string" ? sequenceRead.name : "";

  // The contract allows a campaign's name five hundred characters and Reply agrees to no limit at all. Passing a
  // longer one straight through would make the whole answer `result_invalid` — final, never repeated — and cost
  // an operator the read of that campaign for good, over a display string. So the neutral half carries what it
  // can hold and the provider's own name survives beside it, whole.
  const name = named.length > MAX_NAME ? shortened(named) : named;

  return {
    result: {
      campaign: {
        external_id: sequence,
        name: name,
        // A state this vocabulary has no word for is `other`, never the closest-looking word: `other` is an
        // honest answer a caller can act on, and a guess is one they cannot tell from a fact.
        status: archived ? "archived" : (STATUS[state] || "other"),
        // An empty object is a real answer, and here it is the true one: it says the provider reports no counts,
        // which is a different fact from reporting zero. A Reply sequence carries no people counts at all — the
        // one count endpoint in the published description still says "coming soon" — and synthesising a number
        // would mean paging every contact in the sequence inside this operation's sixty-second budget.
        counts: {},
      },
      // Reply's own answer, untranslated and untyped, so nothing is lost while the neutral half stays strict.
      // Two of Reply's fields collapse into one word above and `health` has no word here at all, so this is
      // where they survive. Of the three operations this package implements only this one publishes a `vendor`
      // bag: the other two output schemas are closed.
      vendor: {
        // Only when it did not fit: a name repeated unchanged in both halves would say the two disagree when
        // they do not.
        name: name === named ? undefined : named,
        status: sequenceRead.status === undefined ? null : sequenceRead.status,
        is_archived: sequenceRead.isArchived === undefined ? null : sequenceRead.isArchived,
        health: sequenceRead.health === undefined ? null : sequenceRead.health,
      },
    },
    // The pin belongs here and never inside `result.campaign`, whose schema is closed: one member the contract
    // does not declare would make the whole answer `result_invalid`.
    external_ids: learned,
  };
}

// The first five hundred characters of a name, cut where a character ends rather than in the middle of one.
// JavaScript counts a name in UTF-16 units and an emoji is two of them, so a cut at the limit can leave the
// first half of a pair behind — and half a character is not text: it cannot be written as JSON, so the whole
// answer would be a protocol failure instead of a campaign with a shortened name.
function shortened(named) {
  const cut = named.slice(0, MAX_NAME);
  const last = cut.charCodeAt(MAX_NAME - 1);
  return last >= 0xD800 && last <= 0xDBFF ? cut.slice(0, MAX_NAME - 1) : cut;
}

// Which sequence this call is about. A pin is worked by whenever there is one — that is what a link is for — and
// the identifier a planner supplied is what makes the first one.
function identifier(input) {
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const supplied = input.args && input.args.campaign && input.args.campaign.external_id;
  const named = pinned || supplied;

  if (typeof named !== "string"
    || !SEQUENCE_NUMBER.test(named)
    || Number(named) > LARGEST_SEQUENCE_NUMBER) {
    // Reply numbers its sequences, so a value that is not a sequence number names nothing this account could
    // hold under any circumstance. That is the permanent absence the document has a word for, and there is
    // nothing to ask the provider about — so nothing is asked.
    fail(OPERATION, { call: null, row: "campaign_not_found" }, undefined);
  }

  return named;
}
