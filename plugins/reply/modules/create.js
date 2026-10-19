// `campaign.create`: the provider's campaign made, as a draft that holds nobody and has never been started, so a person
// never has to make the sequence by hand before Jason can work with it.
//
// Two commands: `reply sequence list --name -`, every sequence whose whole name is this one, ignoring case, read to its
// end; and `reply sequence create --name -`, the draft with the one step a sequence needs. A sequence holds no key, so
// the name the request carries is what tells a repeat what an earlier attempt made — and that is why every attempt
// lists first. A first attempt that finds the name taken makes nothing and adopts nothing. An attempt after one that may
// have made it adopts exactly one sequence of the name, a draft that holds nobody, made no earlier than the work item;
// anything else is a person's to decide. A second sequence of a name that exists is never made.
//
// The name never reaches a command line: the host writes every argument to its `exec` line, and an operation's argument
// does not belong in a log. Both commands read it on stdin. The CLI reads the list to twenty thousand sequences whose
// names contain this one and refuses to decide past that (`sequence.list_incomplete`, this package's
// `list_not_finished`), and a create whose answer was lost may have made the sequence, which the next attempt's list
// finds.
import { run } from "./cli.js";
import { describe, evidence, fail, lostAnswerRow } from "./errors.js";
import { cut } from "./reading.js";

const OPERATION = "campaign.create";
const LIST = "sequence list";
const CREATE = "sequence create";
const PEOPLE = "sequence contacts";

// The two clocks: a sequence made up to ten minutes before the moment the work item's identifier carries can still be
// this item's own.
const MARGIN_MS = 10 * 60 * 1000;

// What the output schema allows a campaign's name. Reply publishes no limit of its own.
const MAX_NAME = 500;

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

  if (only.status !== "new" || only.is_archived !== false) {
    const archived = only.is_archived === false ? "" : only.is_archived === true ? " and archived" : " and not said to be unarchived";
    ambiguous("The one sequence of this name, " + id + ", is " + describe(only.status) + archived + ".");
  }

  const itemAt = workItemMoment(input.idempotency_key);
  const madeAt = moment(only.created_at);
  if (itemAt === null || madeAt === null || madeAt < itemAt - MARGIN_MS) {
    ambiguous("The one sequence of this name, " + id + ", was created at " + describe(only.created_at) + ", before this work item was.");
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

  return answered(id, typeof only.name === "string" ? only.name : input.args.name, 1);
}

// The sequence made: the name and the one step the CLI gives it, nothing else. Reply attaches the account's default
// schedule and its default mailbox on its own; the draft still sends nothing, because it is never started here, it
// holds nobody, and its one step is a task for its owner.
function made(context, name) {
  const sequence = run(context, {
    operation: OPERATION,
    command: CREATE,
    operands: [],
    flags: ["--name", "-"],
    stdin: { name: name },
    learned: undefined,
  });

  const id = sequenceNumber(sequence.sequence_id);
  const steps = sequence.steps_added;
  if (id === null || !Number.isInteger(steps) || steps < 0) {
    // The CLI said it made something and did not say what: the sequence may be there, under an identifier nobody
    // learned. The next attempt finds it by the name.
    fail(OPERATION, { call: CREATE, row: lostAnswerRow(CREATE), provider_item: evidence(sequence) }, id === null ? undefined : { campaign: id });
  }

  if (sequence.status !== "new" || sequence.is_archived !== false) {
    // Never expected. The sequence exists, so it is pinned on the failure: a person looks at it before anything else
    // is done with it.
    fail(OPERATION, {
      call: CREATE,
      row: "campaign_not_draft",
      provider_item: describe({ status: sequence.status, is_archived: sequence.is_archived }),
    }, { campaign: id });
  }

  return answered(id, name, steps);
}

function answered(id, name, steps) {
  return {
    result: {
      items: [{
        campaign_external_id: id,
        name: cut(name, MAX_NAME),
        status: "created",
        state: "draft",
        enrolled: 0,
        steps_added: steps,
      }],
    },
    external_ids: { campaign: id },
  };
}

// Every sequence whose name is this one, exactly and without regard to case: the CLI's list, which reads Reply's
// partial name filter to its end and keeps only the whole name. The comparison is made here again, so that a sequence
// not of this name is never taken for one.
function sameName(context, name) {
  const listed = run(context, {
    operation: OPERATION,
    command: LIST,
    operands: [],
    flags: ["--name", "-"],
    stdin: { name: name },
    learned: undefined,
    pins: false,
  });

  if (!Array.isArray(listed.items)) {
    // Without the items a list says nothing — and reading silence as "no sequence of the name" is how a second one
    // gets made. This call changes nothing, so it is simply asked again.
    fail(OPERATION, { call: LIST, row: lostAnswerRow(LIST) }, undefined);
  }

  const wanted = name.toLowerCase();
  return listed.items.filter((entry) => entry !== null && typeof entry === "object" && typeof entry.name === "string" && entry.name.toLowerCase() === wanted);
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
