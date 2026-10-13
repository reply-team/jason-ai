// `campaign.get`: one read of the sequence a campaign is, and the mapping of Reply's own state onto the
// vocabulary the contract publishes.
//
// Reply has no campaign resource. A campaign is a **sequence**, so `reply sequence get` is the whole of this
// operation: one command, nothing written, nothing to recover. The document's repeat rule is `safe` for exactly that
// reason — an attempt that ended without an answer simply asks again.
import { run } from "./cli.js";
import { fail } from "./errors.js";
import { lookup } from "./reading.js";

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

  // What this package now knows Reply calls this campaign. It travels out with a failure as well as with an answer,
  // because a read that resolved the campaign and then lost its answer should still leave the link behind. A
  // sequence the account does not hold fails `campaign_not_found`, and that link is not recorded.
  const learned = { campaign: sequence };
  const sequenceRead = run(context, { operation: OPERATION, command: "sequence get", operands: [sequence], learned: learned });

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
        // A state this vocabulary has no word for is `other`, never the closest-looking word.
        status: archived ? "archived" : (lookup(STATUS, state) || "other"),
        // An empty object says the provider reports no counts, which is a different fact from reporting zero.
        counts: {},
      },
      // Reply's own answer, untranslated and untyped, so nothing is lost while the neutral half stays strict.
      vendor: {
        // Only when it did not fit: a name repeated unchanged in both halves would say the two disagree.
        name: name === named ? undefined : named,
        status: sequenceRead.status === undefined ? null : sequenceRead.status,
        is_archived: sequenceRead.isArchived === undefined ? null : sequenceRead.isArchived,
        health: sequenceRead.health === undefined ? null : sequenceRead.health,
      },
    },
    external_ids: learned,
  };
}

// The first five hundred characters of a name, cut where a character ends rather than in the middle of one.
function shortened(named) {
  const cut = named.slice(0, MAX_NAME);
  const last = cut.charCodeAt(MAX_NAME - 1);
  return last >= 0xD800 && last <= 0xDBFF ? cut.slice(0, MAX_NAME - 1) : cut;
}

// Which sequence this call is about. A pin is worked by whenever there is one, and the identifier a planner supplied
// is what makes the first one. A value that is not a sequence number names nothing an account could hold, so
// nothing is asked.
function identifier(input) {
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const supplied = input.args && input.args.campaign && input.args.campaign.external_id;
  const named = pinned || supplied;

  if (typeof named !== "string" || !SEQUENCE_NUMBER.test(named) || Number(named) > LARGEST_SEQUENCE_NUMBER) {
    fail(OPERATION, { call: null, row: "campaign_not_found" }, undefined);
  }

  return named;
}
