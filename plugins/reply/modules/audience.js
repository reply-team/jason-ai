// The audience family over Reply's Live Data: a preview, a search, its status and its cancel, a filter's values, and
// whether the account may search.
//
// Reply keeps no link from a search to the people it found: it puts each new person on the lists the search was started
// with. So a search makes a contact list of its own first, private and named after the work item and the attempt, starts
// Reply's search on it, and answers `accepted` with a reference holding both ids. A status read reads the search's status
// first and then the list's people from the cursor on — Reply moves a batch onto the list before it sets a final status,
// so a status read as ended means every person is already on the list — and the cursor is the number of the list's people
// already returned: Reply adds each batch at the end of an order that holds from page to page, and only that search fills
// the list. The read that returns the last person of an ended search deletes the list, and so does a cancel that stops a
// search; a cancel that comes after the search ended leaves the list for that read. A person Reply already holds is
// skipped by the search, never put on the list.
//
// Reply's statuses are the document's states: inProgress, finished, noMoreContacts, manuallyPaused,
// pausedDueToInsufficientCredits and pausedDueToContactLimit. Reply's 409 is a refusal with its own code, never a block,
// and credits that run out mid-search are a state, `paused_for_credits`.
import { run, runOrUndo, tidy } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { fill } from "./page.js";
import { cut, lookup, text, word } from "./reading.js";

const PREVIEW = "audience.preview";
const SEARCH = "audience.search";
const SEARCH_STATUS = "audience.search_status";
const SEARCH_CANCEL = "audience.search_cancel";
const FILTER_VALUES = "audience.filter_values";

const LIST_CREATE = "contact-list create";
const LIST_DELETE = "contact-list delete";
const LIST_PEOPLE = "contact-list contacts";
const LIVE_PREVIEW = "live-data preview";
const LIVE_START = "live-data start";
const LIVE_GET = "live-data get";
const LIVE_LIST = "live-data list";
const LIVE_DELETE = "live-data delete";
const LIVE_VALUES = "live-data filter-values";

// What the list a search fills is called: this, the work item, " #" and the attempt's number. An attempt never makes a
// list another attempt made, so a repeat never meets the name taken.
const LIST_NAME = "Jason search ";

const LARGEST_REPLY_ID = 2147483647;
const REFERENCE = /^search-([1-9][0-9]{0,9})-list-([1-9][0-9]{0,9})$/;

// The cursor: how many of the list's people were already returned, as reply-cli's `--offset` takes it.
const CURSOR = /^(0|[1-9][0-9]{0,6})$/;
const LARGEST_OFFSET = 1000000;

const STATES = {
  inProgress: "in_progress",
  finished: "finished",
  noMoreContacts: "exhausted",
  manuallyPaused: "paused",
  pausedDueToInsufficientCredits: "paused_for_credits",
  pausedDueToContactLimit: "paused_for_limit",
};

// Reply's five lists of suggested values, by the names the manifest's `keys` give them as typeaheads.
const TYPEAHEADS = ["industries", "departments", "seniorities", "locations", "job-titles"];

// A repeat looks for the search an earlier attempt may have started among the team's searches, newest first, a page of
// a hundred at a time, back to an hour before the earliest attempt that may have acted — Reply's clock and the runtime's
// need not agree — and never more than ten pages. A search it misses is no second search spent on the same people:
// Reply refuses a second unfinished search of the same filters, `search_in_progress`.
const SEARCHES_PAGE = 100;
const SEARCHES_PAGES = 10;
const CLOCKS_APART_MS = 60 * 60 * 1000;

// What a preview leaves of the invocation's budget for the CLI's own work around Reply's job — starting it, and reading it
// back every two seconds of the wait — beside what `cli.js` keeps back for itself: the wait is half of what is left after
// both, so the reads around it have the other half.
const PREVIEW_MARGIN_MS = 15000;
const PREVIEW_RESERVE_MS = 5000;
const LONGEST_WAIT_SECONDS = 600;

const RAW = 16384;
const SAMPLE = 25;

export function audienceSearch(input, context) {
  const args = input.args;
  if (args.include_without_email === true) {
    fail(SEARCH, {
      call: null,
      row: "people_without_an_email_not_offered",
      reason: "reply-cli's live-data start takes no flag for people without an email (include_without_email), so the search cannot ask Reply for them.",
    }, undefined);
  }

  const workItem = input.idempotency_key;
  const attempt = Number.isInteger(context.attempt_number) && context.attempt_number > 0 ? context.attempt_number : 1;
  const since = typeof context.unsettled_since === "string" ? Date.parse(context.unsettled_since) : Number.NaN;
  if (attempt > 1 && Number.isFinite(since)) {
    const earlier = startedEarlier(context, workItem, since);
    if (earlier !== null) {
      return accepted(earlier.search, earlier.list);
    }
  }

  const made = run(context, {
    operation: SEARCH, command: LIST_CREATE, operands: [], flags: ["--name", "-"], stdin: { name: LIST_NAME + workItem + " #" + attempt }, pins: false,
  });
  if (!isReplyId(made.list_id)) {
    fail(SEARCH, { call: LIST_CREATE, row: lostAnswerRow(LIST_CREATE) }, undefined);
  }

  const list = made.list_id;
  const flags = ["--filters", "-", "--count", String(args.count), "--list-id", String(list)];
  if (Number.isInteger(args.people_per_company)) {
    flags.push("--max-per-company", String(args.people_per_company));
  }

  if (args.find_phones === true) {
    flags.push("--with-phones");
  }

  const started = runOrUndo(
    context,
    { operation: SEARCH, command: LIVE_START, operands: [], flags: flags, stdin: input.definition, pins: false },
    () => deleteList(context, list));
  if (!isReplyId(started.search_id)) {
    fail(SEARCH, { call: LIVE_START, row: lostAnswerRow(LIVE_START) }, undefined);
  }

  return accepted(started.search_id, list);
}

export function audienceSearchStatus(input, context) {
  const named = referenceOf(SEARCH_STATUS, input.search.reference);
  const offset = cursorOf(input.search.cursor);
  const held = searchOf(context, SEARCH_STATUS, named.search);
  if (held === null) {
    fail(SEARCH_STATUS, { call: LIVE_GET, row: "search_not_found" }, undefined);
  }

  const status = held.status;
  const page = run(context, {
    operation: SEARCH_STATUS,
    command: LIST_PEOPLE,
    operands: [String(named.list)],
    flags: ["--live-data-only", "--limit", String(input.args.limit), "--offset", String(offset)],
    pins: false,
  });
  if (!Array.isArray(page.items) || typeof page.has_more !== "boolean") {
    fail(SEARCH_STATUS, { call: LIST_PEOPLE, row: lostAnswerRow(LIST_PEOPLE) }, undefined);
  }

  const state = stateOf(status);
  const vendor = { status: cut(status, 64) };
  const found = Number.isInteger(held.contactsFound) && held.contactsFound >= 0 ? held.contactsFound : null;

  // A contact without an identifier Reply would answer is passed over, and still counted as read, so the cursor moves past it.
  const people = [];
  const at = [];
  page.items.forEach((contact, position) => {
    const person = personOf(contact);
    if (person !== null) {
      people.push(person);
      at.push(position);
    }
  });

  const rest = { state: state, found: found, people: [], cursor: String(LARGEST_OFFSET), has_more: true, vendor: vendor };
  const filled = fill(people, rest);
  const read = filled.items.length < people.length ? at[filled.items.length] : page.items.length;
  const hasMore = filled.items.length < people.length || page.has_more;

  if ((state === "finished" || state === "exhausted") && !hasMore) {
    deleteList(context, named.list);
  }

  return {
    result: { state: state, found: found, people: filled.items, cursor: String(offset + read), has_more: hasMore, vendor: vendor },
  };
}

export function audienceSearchCancel(input, context) {
  const named = referenceOf(SEARCH_CANCEL, input.search.reference);
  const attempt = Number.isInteger(context.attempt_number) && context.attempt_number > 0 ? context.attempt_number : 1;
  const held = searchOf(context, SEARCH_CANCEL, named.search);
  if (held === null) {
    // Whoever deleted it, its list has nothing more to give. An earlier attempt may be the one that deleted it, and nothing
    // says which attempts reached Reply.
    deleteList(context, named.list);
    if (attempt === 1) {
      fail(SEARCH_CANCEL, { call: LIVE_GET, row: "search_not_found" }, undefined);
    }

    return ended("already_ended", null);
  }

  // A cancel that comes after the search ended changes nothing: the status read goes on reading the list, and deletes it
  // once the last person is read.
  const state = stateOf(held.status);
  if (state === "finished" || state === "exhausted") {
    return ended("already_ended", state);
  }

  const deleted = run(context, {
    operation: SEARCH_CANCEL, command: LIVE_DELETE, operands: [String(named.search)], flags: [], pins: false, missing: "live_data.not_found",
  });
  deleteList(context, named.list);
  return deleted === null ? ended("already_ended", null) : ended("cancelled", null);
}

export function audiencePreview(input, context) {
  const flags = ["--filters", "-"];
  if (Number.isInteger(input.args.people_per_company)) {
    flags.push("--max-per-company", String(input.args.people_per_company));
  }

  flags.push("--wait-seconds", String(waitOf(context)));
  const answer = run(context, { operation: PREVIEW, command: LIVE_PREVIEW, operands: [], flags: flags, stdin: input.definition, pins: false });
  if (answer.status === "pending") {
    fail(PREVIEW, { call: LIVE_PREVIEW, row: "preview_not_answered" }, undefined);
  }

  if (answer.status !== "done") {
    fail(PREVIEW, { call: LIVE_PREVIEW, row: lostAnswerRow(LIVE_PREVIEW) }, undefined);
  }

  const sample = Array.isArray(answer.sample_contacts) ? answer.sample_contacts : [];
  return {
    result: {
      count: count(answer.contacts_count),
      companies: count(answer.companies_count),
      sample: sample.filter(isObject).slice(0, SAMPLE).map(sampled),
    },
  };
}

export function audienceFilterValues(input, context) {
  const args = input.args;
  if (TYPEAHEADS.indexOf(args.filter) < 0) {
    fail(FILTER_VALUES, { call: null, row: "filter_unknown" }, undefined);
  }

  // The text is a filter's words, which Reply matches inside its own suggestions, and never a person's: reply-cli takes it
  // on the command line and nowhere else.
  const flags = args.query.length > 0 ? ["--search", args.query] : [];
  flags.push("--limit", String(args.limit));
  const answer = run(context, { operation: FILTER_VALUES, command: LIVE_VALUES, operands: [args.filter], flags: flags, pins: false });
  if (!Array.isArray(answer.items)) {
    fail(FILTER_VALUES, { call: LIVE_VALUES, row: lostAnswerRow(LIVE_VALUES) }, undefined);
  }

  // A value no definition could hold — empty, or longer than a filter's value may be — is no value to offer.
  const values = [];
  for (const value of answer.items) {
    if (typeof value === "string" && value.length > 0 && cut(value, 200) === value && values.indexOf(value) < 0 && values.length < args.limit) {
      values.push(value);
    }
  }

  return { result: { values: values } };
}

// Reply has no read of what an account may spend or how many contacts its plan still holds, so nothing is asked: a
// search's own answer is the gate, and this says only that one may be tried.
export function audienceAvailability() {
  return { result: { available: true, reason: null, detail: null, credits: null, headroom: null, vendor: { status: null } } };
}

function accepted(search, list) {
  const reference = "search-" + search + "-list-" + list;
  return {
    result: { items: [{ status: "accepted", reference: reference, found: null, people: [] }] },
    external_ids: { search: reference },
  };
}

function ended(status, endedAs) {
  return { result: { items: [{ status: status, ended_as: endedAs }] } };
}

// The search an earlier attempt of this work item started, found by the list it fills, whatever attempt made the list;
// or null.
function startedEarlier(context, workItem, since) {
  const prefix = LIST_NAME + workItem + " #";
  const floor = since - CLOCKS_APART_MS;
  for (let page = 0; page < SEARCHES_PAGES; page++) {
    const listed = run(context, {
      operation: SEARCH, command: LIVE_LIST, operands: [], flags: ["--limit", String(SEARCHES_PAGE), "--offset", String(page * SEARCHES_PAGE)], pins: false,
    });
    if (!Array.isArray(listed.items)) {
      fail(SEARCH, { call: LIVE_LIST, row: lostAnswerRow(LIVE_LIST) }, undefined);
    }

    for (const search of listed.items.filter(isObject)) {
      const lists = Array.isArray(search.lists) ? search.lists.filter(isObject) : [];
      const ours = lists.find((list) => typeof list.name === "string" && list.name.indexOf(prefix) === 0 && isReplyId(list.id));
      if (ours !== undefined && isReplyId(search.id)) {
        return { search: search.id, list: ours.id };
      }
    }

    const last = listed.items.length > 0 && isObject(listed.items[listed.items.length - 1]) ? Date.parse(listed.items[listed.items.length - 1].startedAt) : Number.NaN;
    if (listed.has_more !== true || (Number.isFinite(last) && last < floor)) {
      break;
    }
  }

  return null;
}

// The search Reply holds under the id, or null where it holds none. An answer with no status is no answer.
function searchOf(context, operation, search) {
  const held = run(context, { operation: operation, command: LIVE_GET, operands: [String(search)], flags: [], pins: false, missing: "live_data.not_found" });
  if (held !== null && (typeof held.status !== "string" || held.status.length === 0)) {
    fail(operation, { call: LIVE_GET, row: lostAnswerRow(LIVE_GET) }, undefined);
  }

  return held;
}

function deleteList(context, list) {
  tidy(context, { command: LIST_DELETE, operands: [String(list)], flags: [] });
}

function referenceOf(operation, reference) {
  const parts = typeof reference === "string" ? REFERENCE.exec(reference) : null;
  if (parts === null || Number(parts[1]) > LARGEST_REPLY_ID || Number(parts[2]) > LARGEST_REPLY_ID) {
    fail(operation, { call: null, row: "search_not_found" }, undefined);
  }

  return { search: Number(parts[1]), list: Number(parts[2]) };
}

function cursorOf(cursor) {
  if (cursor === null || cursor === undefined) {
    return 0;
  }

  if (typeof cursor !== "string" || !CURSOR.test(cursor) || Number(cursor) > LARGEST_OFFSET) {
    fail(SEARCH_STATUS, { call: null, row: "cursor_unreadable" }, undefined);
  }

  return Number(cursor);
}

// Reply's status in the document's word. One this version does not know is the nearest of the six: a pause where Reply's
// own word says it is paused, and otherwise a search still running, which is read again either way.
function stateOf(status) {
  const known = lookup(STATES, status);
  if (known !== undefined) {
    return known;
  }

  return status.toLowerCase().indexOf("paused") === 0 ? "paused" : "in_progress";
}

// How long reply-cli waits for Reply's preview job: half of what is left of the invocation's budget once `cli.js` has kept
// its share back and the CLI its margin, in whole seconds, at most what reply-cli takes.
function waitOf(context) {
  const deadline = typeof context.deadline === "string" ? Date.parse(context.deadline) : Number.NaN;
  if (!Number.isFinite(deadline)) {
    return 0;
  }

  const left = deadline - Date.now();
  const seconds = Math.floor((left - Math.min(PREVIEW_RESERVE_MS, left / 2) - PREVIEW_MARGIN_MS) / 2000);
  return Math.max(0, Math.min(LONGEST_WAIT_SECONDS, seconds));
}

// One person on a search's list, from Reply's contact, in the one shape every provider answers in; null for a contact
// without an identifier.
function personOf(contact) {
  if (!isObject(contact) || !isReplyId(contact.id)) {
    return null;
  }

  const channels = [];
  for (const [channel, value] of [["email", contact.email], ["phone", contact.phone], ["linkedin", contact.linkedInUrl]]) {
    if (typeof value === "string" && value.trim().length > 0 && cut(value, 500) === value) {
      channels.push({ channel: channel, value: value });
    }
  }

  return {
    external_id: String(contact.id),
    first_name: text(contact.firstName, 200),
    last_name: text(contact.lastName, 200),
    title: text(contact.title, 200),
    company: {
      name: text(contact.company, 200),
      domain: text(contact.domain, 253),
      size: word(contact.companySize, 64),
      industry: text(contact.industry, 200),
    },
    location: placeOf([contact.city, contact.state, contact.country]),
    channels: channels,
    raw: rawOf(contact),
  };
}

// One person of a preview's sample: no identifier, since Reply's sample gives none, and no way to reach them — no
// channels, and Reply's record beside them without the LinkedIn link it shows.
function sampled(person) {
  const shown = {};
  for (const key of Object.keys(person)) {
    if (key !== "linkedin_url") {
      shown[key] = person[key];
    }
  }

  return {
    external_id: null,
    first_name: text(person.first_name, 200),
    last_name: text(person.last_name, 200),
    title: text(person.job_title, 200),
    company: { name: text(person.company_name, 200), domain: text(person.company_domain, 253), size: null, industry: null },
    location: text(person.location, 500),
    raw: rawOf(shown),
  };
}

// Where a person is, as Reply holds it: the city, the region and the country it holds, on one line.
function placeOf(parts) {
  const named = parts.filter((part) => typeof part === "string" && part.trim().length > 0);
  return named.length === 0 ? null : cut(named.join(", "), 500);
}

// Reply's record as JSON text, as the document bounds it: whole where it fits, without its custom fields where those are
// what does not, and null where even that does not.
function rawOf(record) {
  const whole = JSON.stringify(record);
  if (cut(whole, RAW) === whole) {
    return whole;
  }

  const trimmed = {};
  for (const key of Object.keys(record)) {
    if (key !== "customFields") {
      trimmed[key] = record[key];
    }
  }

  const shorter = JSON.stringify(trimmed);
  return cut(shorter, RAW) === shorter ? shorter : null;
}

function count(value) {
  return Number.isSafeInteger(value) && value >= 0 ? value : null;
}

function isReplyId(value) {
  return Number.isInteger(value) && value > 0 && value <= LARGEST_REPLY_ID;
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
