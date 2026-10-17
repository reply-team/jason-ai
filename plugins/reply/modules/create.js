// `campaign.create`: the provider's campaign made, as a draft that holds nobody and has never been started, so a person
// never has to make the sequence by hand before Jason can work with it.
//
// Reply has no command that creates a sequence, so this is the one operation that makes raw calls of its own beyond the
// contact search: the list of sequences, read to its end, and the create. A sequence holds no key, so the name the
// request carries is what tells a repeat what an earlier attempt made — and that is why every attempt lists first. A
// first attempt that finds the name taken makes nothing and adopts nothing. An attempt after one that may have made it
// adopts exactly one sequence of the name, a draft that holds nobody, made no earlier than the work item; anything else
// is a person's to decide. A second sequence of a name that exists is never made.
//
// The name never reaches a command line: the host writes every argument to its `exec` line, and an operation's argument
// does not belong in a log. So the list is asked for every sequence and the names are compared here, and the name
// travels in the create's body, on stdin.
import { call, run } from "./cli.js";
import { describe, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { cut } from "./reading.js";

const OPERATION = "campaign.create";
const SEQUENCES = "/v3/sequences";
const LIST = "api GET " + SEQUENCES;
const CREATE = "api POST " + SEQUENCES;
const PEOPLE = "sequence contacts";

// How far the list reads: the cap is twenty pages of a thousand — a thousand is the most Reply answers at once — so
// twenty thousand sequences. A name is free only once the end of the list is reached, so an account holding more
// sequences than that is one this package declines to decide for rather than guesses at: `list_not_finished`, whose
// message says the twenty thousand it read. Every page is a process of its own inside the operation's budget.
const PAGE = 1000;
const PAGES = 20;

// The two clocks: a sequence made up to ten minutes before the moment the work item's identifier carries can still be
// this item's own.
const MARGIN_MS = 10 * 60 * 1000;

// What the output schema allows a campaign's name. Reply publishes no limit of its own.
const MAX_NAME = 500;

// The one step a Reply sequence needs to exist: Reply refuses a sequence with none. A task is its owner's to-do and
// reaches nobody, even once the sequence runs; it says what it is, so whoever opens the sequence replaces it.
const PLACEHOLDER = {
  type: "task",
  actionType: "toDo",
  delayInMinutes: 0,
  description: "Added because a sequence needs one step to exist; replace it before this sequence is ever started.",
};

// How many steps a sequence made here was given.
const STEPS_ADDED = 1;

const LARGEST_SEQUENCE_NUMBER = 2147483647;

const CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

// A moment as Reply writes one: a date, a time, any number of fractional digits, and an offset.
const MOMENT = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,9}))?(Z|[+-]\d{2}:\d{2})$/;

export function campaignCreate(input, context) {
  // A Jason campaign linked already is refused before anything is asked: a second create would make a second sequence
  // beside the first link.
  if (input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign !== undefined) {
    fail(OPERATION, { call: null, row: "campaign_already_linked" }, undefined);
  }

  const name = input.args.name;
  const named = sameName(context, name);

  if (named.length === 0) {
    return made(context, name);
  }

  if (!isRepeat(context)) {
    fail(OPERATION, { call: LIST, row: "campaign_name_taken", reason: "Reply holds " + named.length + " sequence(s) of this name." }, undefined);
  }

  return adopted(context, input, named);
}

// An attempt after one that may have made the sequence. Exactly one of the name, in Reply's `new` state, not archived,
// made no earlier than the work item less the margin, and holding nobody, is this item's own; anything else is not.
// Nothing found here is ever pinned unless it is adopted: a sequence that is not this item's own is not this
// campaign's either.
function adopted(context, input, named) {
  if (named.length > 1) {
    ambiguous("Reply holds " + named.length + " sequences of this name.");
  }

  const only = named[0];
  const id = sequenceNumber(only.id);
  if (id === null) {
    ambiguous("The one sequence of this name carries an identifier this package cannot work by: " + describe(only.id) + ".");
  }

  if (only.status !== "new" || only.isArchived !== false) {
    const archived = only.isArchived === false ? "" : only.isArchived === true ? " and archived" : " and not said to be unarchived";
    ambiguous("The one sequence of this name, " + id + ", is " + describe(only.status) + archived + ".");
  }

  const itemAt = workItemMoment(input.idempotency_key);
  const madeAt = moment(only.created);
  if (itemAt === null || madeAt === null || madeAt < itemAt - MARGIN_MS) {
    ambiguous("The one sequence of this name, " + id + ", was created at " + describe(only.created) + ", before this work item was.");
  }

  // Whether it holds anybody: one person is enough to say. The read names a sequence that is not this campaign's yet,
  // so nothing it fails with may link it.
  const page = run(context, {
    operation: OPERATION,
    command: PEOPLE,
    operands: [id],
    flags: ["--limit", "1", "--offset", "0"],
    learned: undefined,
    pins: false,
  });
  if (!Array.isArray(page.items) || typeof page.has_more !== "boolean") {
    fail(OPERATION, { call: PEOPLE, row: lostAnswerRow(PEOPLE) }, undefined);
  }

  if (page.items.length > 0 || page.has_more) {
    ambiguous("The one sequence of this name, " + id + ", holds somebody.");
  }

  return answered(id, typeof only.name === "string" ? only.name : input.args.name);
}

// The sequence made: the name and the one step, nothing else. Reply attaches the account's default schedule and its
// default mailbox on its own; the draft still sends nothing, because it is never started here, it holds nobody, and its
// one step is a task for its owner.
function made(context, name) {
  const answer = call(context, "POST", SEQUENCES, { name: name, steps: [PLACEHOLDER] }, undefined, OPERATION);
  if (answer.code < 200 || answer.code > 299) {
    fail(OPERATION, observe(CREATE, answer, statusRow(answer.code, CREATE)), undefined);
  }

  const sequence = answer.data !== null && typeof answer.data === "object" && !Array.isArray(answer.data) ? answer.data : null;
  const id = sequence === null ? null : sequenceNumber(sequence.id);
  if (id === null) {
    // Reply said it made something and did not say what: the sequence may be there, under an identifier nobody
    // learned. The next attempt finds it by the name.
    fail(OPERATION, observe(CREATE, answer, lostAnswerRow(CREATE)), undefined);
  }

  if (sequence.status !== "new" || sequence.isArchived !== false) {
    // Never expected. The sequence exists, so it is pinned on the failure: a person looks at it before anything else
    // is done with it.
    const seen = observe(CREATE, answer, "campaign_not_draft");
    seen.provider_item = describe({ status: sequence.status, isArchived: sequence.isArchived });
    fail(OPERATION, seen, { campaign: id });
  }

  return answered(id, typeof sequence.name === "string" && sequence.name.length > 0 ? sequence.name : name);
}

function answered(id, name) {
  return {
    result: {
      items: [{
        campaign_external_id: id,
        name: cut(name, MAX_NAME),
        status: "created",
        state: "draft",
        enrolled: 0,
        steps_added: STEPS_ADDED,
      }],
    },
    external_ids: { campaign: id },
  };
}

// Every sequence whose name is this one, exactly and without regard to case, from the whole list. Reply's own name
// filter matches part of a name, and would put the name on the command line besides.
function sameName(context, name) {
  const wanted = name.toLowerCase();
  const found = [];

  for (let page = 0; page < PAGES; page++) {
    const answer = call(context, "GET", SEQUENCES + "?top=" + PAGE + "&skip=" + page * PAGE, undefined, undefined, OPERATION);
    if (answer.code !== 200) {
      fail(OPERATION, observe(LIST, answer, statusRow(answer.code, LIST)), undefined);
    }

    const read = answer.data !== null && typeof answer.data === "object" && !Array.isArray(answer.data) ? answer.data : null;
    if (read === null || !Array.isArray(read.items) || typeof read.hasMore !== "boolean") {
      // Without the items and the word for the end, a page says nothing — and reading silence as "no sequence of the
      // name" is how a second one gets made. This call changes nothing, so it is simply asked again.
      fail(OPERATION, observe(LIST, answer, lostAnswerRow(LIST)), undefined);
    }

    for (const entry of read.items) {
      if (entry !== null && typeof entry === "object" && typeof entry.name === "string" && entry.name.toLowerCase() === wanted) {
        found.push(entry);
      }
    }

    if (read.hasMore === false) {
      return found;
    }
  }

  fail(OPERATION, {
    call: LIST,
    row: "list_not_finished",
    reason: "Read " + PAGE * PAGES + " sequences, " + PAGES + " pages of " + PAGE + ", and Reply said there were more.",
  }, undefined);
}

function ambiguous(reason) {
  fail(OPERATION, { call: LIST, row: "campaign_name_ambiguous", reason: reason }, undefined);
}

function isRepeat(context) {
  return Boolean(context) && Number.isInteger(context.attempt_number) && context.attempt_number > 1;
}

// Reply's identifier for a sequence, as this package works by it — a string — or null when the value is not one.
function sequenceNumber(value) {
  return typeof value === "number" && Number.isInteger(value) && value > 0 && value <= LARGEST_SEQUENCE_NUMBER
    ? String(value)
    : null;
}

// The moment a work item was made, in milliseconds: the first ten characters of its identifier's ULID. Null when the
// key is not one, which the input schema already refuses.
function workItemMoment(key) {
  const ulid = typeof key === "string" && key.indexOf("wi_") === 0 ? key.slice(3) : "";
  if (ulid.length !== 26) {
    return null;
  }

  let milliseconds = 0;
  for (let at = 0; at < 10; at++) {
    const digit = CROCKFORD.indexOf(ulid.charAt(at));
    if (digit < 0) {
      return null;
    }

    milliseconds = milliseconds * 32 + digit;
  }

  return milliseconds;
}

// A moment Reply wrote, in milliseconds, read with its own offset; null when it is not one. Reply writes two
// fractional digits and an offset, and this reads any number of either, rather than one fixed format.
function moment(value) {
  const parts = typeof value === "string" ? MOMENT.exec(value) : null;
  if (parts === null) {
    return null;
  }

  const month = Number(parts[2]);
  const day = Number(parts[3]);
  const hour = Number(parts[4]);
  const minute = Number(parts[5]);
  const second = Number(parts[6]);
  if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59) {
    return null;
  }

  const fraction = parts[7] === undefined ? 0 : Number((parts[7] + "00").slice(0, 3));
  let at = Date.UTC(Number(parts[1]), month - 1, day, hour, minute, second, fraction);
  if (parts[8] !== "Z") {
    const sign = parts[8].charAt(0) === "-" ? -1 : 1;
    at -= sign * (Number(parts[8].slice(1, 3)) * 60 + Number(parts[8].slice(4, 6))) * 60 * 1000;
  }

  return isFinite(at) ? at : null;
}
