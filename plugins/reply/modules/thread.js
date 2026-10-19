// `conversation.classify`, `conversation.close` and `conversation.reopen`: a thread's state at Reply, set outright.
//
// Reply files a thread under one of the account's inbox categories, or under none. It holds no closed state, and shows
// a thread as worked by marking it read, so closing marks the thread read and reopening marks it unread again. Each
// command sets the state whatever it was, so a second one leaves the thread as the first did, and none of them reads the
// thread first. A category named by its name is matched here against Reply's own list of the account's categories, so
// the name never reaches a command line: only Reply's identifier for it does. Reply's reserved Do not contact is found
// the same way and refused before anything is filed, because filing there is a stop.
import { run } from "./cli.js";
import { evidence, fail, lostAnswerRow } from "./errors.js";
import { identifier, isReplyNumber, text } from "./reading.js";

const CLASSIFY = "conversation.classify";
const CATEGORIES = "inbox categories";
const CATEGORIZE = "inbox categorize";

// Reply's reserved category that is a stop rather than a meaning: filing a thread there opts the person out in every
// sequence, ends their part in each and cancels the emails planned for them, and clearing the category undoes none of
// it. Stopping a person is `optout.record`'s path, so a filing there is refused.
const STOP = "do not contact";

export function conversationClassify(input, context) {
  const thread = threadOf(CLASSIFY, input);
  const wanted = input.args.category;

  if (wanted === null) {
    // Taken out of its category: there is nothing to look up first.
    const cleared = run(context, { operation: CLASSIFY, command: CATEGORIZE, operands: [thread], flags: ["--clear"] });
    if (cleared.status !== "cleared") {
      fail(CLASSIFY, { call: CATEGORIZE, row: lostAnswerRow(CATEGORIZE), provider_item: evidence(cleared) }, undefined);
    }

    return filed(thread, "cleared", null);
  }

  // An entry no thread can be filed under — one with no name, or an identifier that is not a Reply number — is passed
  // over: it cannot be the category asked for, and taking it for an answer that could not be read would stop every
  // filing for the sake of one entry.
  const listed = run(context, { operation: CLASSIFY, command: CATEGORIES, operands: [], flags: [] });
  if (!Array.isArray(listed.items)) {
    fail(CLASSIFY, { call: CATEGORIES, row: lostAnswerRow(CATEGORIES) }, undefined);
  }

  const categories = listed.items.map(categoryOf).filter(category => category !== null);

  // By Reply's identifier for it, or by the one name that equals it, compared exactly and without regard to case.
  const matching = typeof wanted.external_id === "string"
    ? categories.filter(category => category.external_id === wanted.external_id)
    : categories.filter(category => category.compared === wanted.name.toLowerCase());
  if (matching.length === 0) {
    fail(CLASSIFY, { call: CATEGORIES, row: "category_not_found" }, undefined);
  }

  if (matching.length > 1) {
    fail(CLASSIFY, { call: CATEGORIES, row: "category_not_unique" }, undefined);
  }

  // The category found, not the words the call used: one named by its identifier is refused as one named by its name.
  const chosen = matching[0];
  if (chosen.reserved && chosen.compared === STOP) {
    fail(CLASSIFY, { call: CATEGORIES, row: "category_that_opts_the_person_out" }, undefined);
  }

  const done = run(context, {
    operation: CLASSIFY,
    command: CATEGORIZE,
    operands: [thread],
    flags: ["--category-id", chosen.external_id],
  });
  if (done.status !== "categorized") {
    fail(CLASSIFY, { call: CATEGORIZE, row: lostAnswerRow(CATEGORIZE), provider_item: evidence(done) }, undefined);
  }

  return filed(thread, "classified", { external_id: chosen.external_id, name: chosen.name });
}

export function conversationClose(input, context) {
  return mark("conversation.close", input, context, "inbox mark-read", "read", "closed");
}

export function conversationReopen(input, context) {
  return mark("conversation.reopen", input, context, "inbox mark-unread", "unread", "reopened");
}

// The read state set outright: the command answers the same word whatever the thread was, and so does this.
function mark(operation, input, context, command, answered, status) {
  const thread = threadOf(operation, input);
  const done = run(context, { operation: operation, command: command, operands: [thread], flags: [] });
  if (done.status !== answered) {
    fail(operation, { call: command, row: lostAnswerRow(command), provider_item: evidence(done) }, undefined);
  }

  // Nothing is pinned: the thread named itself.
  return { result: { items: [{ thread_external_id: thread, status: status }] } };
}

// Reply numbers its threads, so anything else names no thread this account could hold, and nothing is asked.
function threadOf(operation, input) {
  const thread = input.args.thread.external_id;
  if (!isReplyNumber(thread)) {
    fail(operation, { call: null, row: "conversation_not_found" }, undefined);
  }

  return thread;
}

function filed(thread, status, category) {
  return { result: { items: [{ thread_external_id: thread, status: status, category: category }] } };
}

// One of Reply's categories, or null when the entry is not one this package could file a thread under by its number.
// An entry is reserved unless Reply lists it as not reserved: the safe reading of an entry that does not say.
function categoryOf(entry) {
  if (entry === null || typeof entry !== "object" || typeof entry.name !== "string" || entry.name.length === 0) {
    return null;
  }

  const id = identifier(entry.id);
  return id === null || !isReplyNumber(id)
    ? null
    : { external_id: id, name: text(entry.name, 100), compared: entry.name.toLowerCase(), reserved: entry.isReserved !== false };
}
