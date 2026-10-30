// The only place in this package where a command line is built.
//
// One place, so that "a person's data never travels as an argument", "these flags never appear" and "no raw call is
// ever made" are properties of the package rather than of whoever wrote the newest module: every line built here is
// `reply <noun> <verb>` — a work command, or `auth whoami`, the CLI's own answer to whom it signs in as — and every
// request has a command made for it. The `exec` diagnostic records every argument, and the
// runtime's logs may never hold contact data, so a person goes on stdin and nowhere else. A test on the runtime side
// reads the argument vector out of the stand-in's call log and holds it to exactly the shapes built here.
//
// This module knows command lines and how the Reply CLI ends, and nothing about operations; the operation modules know
// which command serves them and nothing about command lines. That split is what makes the rules above assertable
// instead of aspirational.
import { codeOf, cutAnswerRow, fail, lostAnswerRow, rowOf, unexplainedRefusalRow } from "./errors.js";

// The vendor program, by the name the manifest declares and the operator grants. The runtime decided at reload
// what that name means on this machine; nothing here looks a program up for itself.
const PROGRAM = "reply";

// The host answers -1 when it could not start the program at all.
const NEVER_STARTED = -1;

// What reply-cli's exit codes mean: 0 done, 1 failed with the main effect not done, 2 refused before anything was
// sent, 3 a write whose outcome is unknown.
const DONE = 0;
const FAILED = 1;
const REFUSED_THE_CALL = 2;
const OUTCOME_UNKNOWN = 3;

const LARGEST_REPLY_NUMBER = 2147483647;

// The longest reference to a send the documents take.
const LONGEST_REFERENCE = 256;

// Reply's own code for a LinkedIn send it lost track of, at status 400, and the row it belongs in, whatever it names.
const LINKEDIN_UNKNOWN = "directLinkedIn.unknown";
const LINKEDIN_SEND_FAILED = "linkedin_send_failed";

// The row of a direct send made under a key that Reply answered with a failure at a status of 400 to 499, which the CLI
// passes on as a lost answer. Reply releases the key of a send it answered so, and keeps it only for one it completed,
// so a repeat under the key would be a new send.
const SEND_ANSWERED_A_FAILURE = "send_answered_with_a_failure";

// The CLI's word for a contact Reply holds nothing under, and for a contact a command given a person's fields did not act
// on because Reply already held it.
const NOT_FOUND = "contact.not_found";
const ALREADY_HELD = "contact.already_held";

// The CLI's word for a direct send made under a key Reply already holds for another request: Reply sent nothing, and
// says what it holds under the key — the send the first request made.
const KEY_REUSED = "idempotency_key.reused";

// How much of a program's stderr is read to find the CLI's verdict. A refusal is one short line; the rest is a
// bound, not a budget.
const STDERR_TAIL = 8192;

// What this package keeps back from an invocation's budget when it times a call: five seconds, which is how long
// the host waits for a program it has ended to be gone, and never more than half of what is left. A call given
// everything that is left runs out at the moment the host's own engine ends the invocation, and before this
// package hears that the call was cut off the host has ended the program's process tree and waited for its
// pipes — work that takes longer on a busy machine. When that outlasts the engine's last moments the host answers
// `plugin_timeout` itself, and the identifiers this package had learned never leave with it. A call that is cut
// off sooner is one this package answers, from the row that says what the call was.
const RESERVE_MS = 5000;

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

// Which account a call acts in, from the route's binding. Never a key, never a user to act as: the CLI owns the
// credential, and the binding is written by the people who operate the installation.
function prefix(context) {
  const binding = (context && context.binding) || {};
  const line = ["--json", "-q"];
  if (binding.profile) {
    line.push("--profile", binding.profile);
  }

  if (binding.team_id) {
    line.push("--team-id", binding.team_id);
  }

  return line;
}

/**
 * The whole argument vector of one work command, exported so a test can read it rather than infer it from a log:
 * `--json -q [--profile P] [--team-id T] <noun> <verb> [operands…] [flags…]`, and nothing else ever.
 */
export function commandArgs(context, command, operands, flags) {
  return prefix(context).concat(command.split(" "), operands, flags);
}

/**
 * How long one call may run: what is left of the invocation's budget, less what this package keeps back to answer
 * for itself if the call is cut off. The invocation's own deadline is read from the context, so a call made late in
 * an invocation is given what that invocation has left rather than what it started with. Without a deadline to
 * read — a context that does not carry one — the host's own default applies, which is everything that is left.
 */
function timeoutOf(context) {
  const total = context ? context.timeout_ms : undefined;
  const deadline = context && typeof context.deadline === "string" ? Date.parse(context.deadline) : Number.NaN;
  if (!Number.isInteger(total) || total < 1 || !Number.isFinite(deadline)) {
    return undefined;
  }

  const left = deadline - Date.now();
  const kept = Math.min(RESERVE_MS, left / 2);

  // The host refuses a timeout outside 1 .. the invocation's own, so a clock that has drifted from the host's
  // cannot turn this into a call that fails before it is made.
  return Math.min(total, Math.max(1, Math.floor(left - kept)));
}

function exec(context, args, stdin) {
  return host.exec(compact({
    executable: PROGRAM,
    args: args,
    stdin: stdin,
    timeout_ms: timeoutOf(context),
    // The two variables this package sets on anything, both ordinary names, so neither needs a capability.
    // REPLY_NO_UPDATE_CHECK: reply-cli 0.11.0 looks for a newer release only under `--version`, which this package
    // never runs; the variable keeps a later release from doing it on a work command, where it would cost time the
    // operation is allowed, a write to the configuration directory and a notice on stderr.
    // REPLY_CLIENT: from reply-cli 0.17.2 the CLI names the client a call came through to Reply, so Reply can tell the
    // calls the harness makes, and the runtime version they came through, from the rest. It names a build, nothing of
    // the account or the work. The version goes without its build metadata, the "+" and the source revision a build
    // carries: the CLI takes at most thirty-two characters after the slash and leaves out, without a word, a value it
    // does not take.
    env: { REPLY_NO_UPDATE_CHECK: "1", REPLY_CLIENT: "jason-harness/" + context.runtime_version.split("+")[0] },
  }));
}

/**
 * One reply-cli work command: its result object when it exits 0, and `host.fail` with the row the table decides for
 * every other ending. `request` is `{ operation, command, operands, flags, stdin, learned, pins, missing, lost, gone, kept, channel }`: the
 * canonical operation the command serves, the command's two words, its ids, its flags, the person for stdin, and what
 * this package already knows Reply calls our entities — which travels out with a failure, joined by whatever the CLI
 * says it had learned before it failed. `pins: false` keeps what the CLI learned out of the failure, for a command that
 * names something which is not ours yet, or for an operation that pins nothing. `missing` is the CLI's code for an id
 * Reply holds nothing under, where the caller answers that itself: the command then answers null instead of failing.
 * `lost` is the row a lost answer belongs in where the caller knows more than the command's read or write mark says:
 * a write made before a send, which sent nothing, is asked again as a read is. `gone` is the row the CLI's
 * `contact.not_found` belongs in where the contact the command named is not one this attempt was pinned to — found by
 * the lookup, or made or matched by the command's own import: Reply named it and then holds nothing under it, which is
 * no stale pin, so that ending pins no contact at all. `kept: true` marks a direct send made under a key, which `runOrKept`
 * and `runOrHeld` answer for, and whose reference travels out with any failure that names it; `channel` is its channel,
 * `email` or `linkedin`.
 */
export function run(context, request) {
  return ended(call(context, request), request);
}

/**
 * `run`, for a direct send made under a key: `{ result }` when it exits 0, and `{ kept }` when Reply answers that it holds
 * the key for another request — `{ details, ids }`, what it holds under the key and the identifiers the CLI named. Reply
 * sent nothing for this request; the send under the key is the first one's. Every other ending is `run`'s.
 */
export function runOrKept(context, request) {
  const answer = call(context, request);
  const kept = keptIn(answer, request);
  return kept !== null ? { kept: kept } : { result: ended(answer, request) };
}

/**
 * `run`, for a command given a person by their fields that acts only on a contact it creates: `{ result }` when it exits
 * 0, and `{ held }` when it answers that Reply already held a contact under those fields — the contact it names, as this
 * package works by one, and did not act on. A direct send made under a key may answer `{ kept }` as `runOrKept` does.
 * Every other ending is `run`'s, that answer without a contact it names too.
 */
export function runOrHeld(context, request) {
  const answer = call(context, request);
  const held = heldIn(answer);
  if (held !== null) {
    return { held: held };
  }

  const kept = keptIn(answer, request);
  return kept !== null ? { kept: kept } : { result: ended(answer, request) };
}

/**
 * Whether the account's Reply offers what a read names, by reading it once: true when the command exits 0 or refuses in
 * one of `codes` — the CLI's own words for an answer Reply gave about what was asked, which only a route it has can
 * give — and false when Reply's API has no route for the call (a 404 with no code of Reply's own). Every other ending
 * is `run`'s.
 */
export function offered(context, request, codes) {
  const answer = call(context, request);
  if (answer.timed_out !== true && answer.exit_code === DONE) {
    return true;
  }

  const said = answer.timed_out === true || answer.exit_code !== FAILED ? null : verdict(answer.stderr);
  if (said !== null && codes.indexOf(said.code) >= 0) {
    return true;
  }

  if (said !== null && rowOf(request.operation, said) === UNROUTED) {
    return false;
  }

  return ended(answer, request);
}

/**
 * One read whose every ending other than an answer is taken as no answer at all: its result object when it exits 0
 * with an answer read whole, and null for every other ending — a refusal, a timeout, a program that never started, an
 * answer cut off or one that does not parse — but one. A refusal for want of a working credential (`auth.*`, exit 2)
 * says nothing about what was read and everything about the profile, which no later command could make either, so it
 * is `run`'s, and fails the operation with its own row. A refusal whose code `request.final` maps to a row says
 * something of what was read, and fails the operation with that row. The caller decides what no answer means.
 */
export function readOrNone(context, request) {
  const answer = call(context, request);
  const cut = answer.truncated !== null && typeof answer.truncated === "object" && answer.truncated.stdout === true;
  if (answer.timed_out !== true && answer.exit_code === DONE && !cut) {
    return resultOf(answer.stdout);
  }

  const refused = answer.timed_out === true || answer.exit_code !== FAILED ? null : verdict(answer.stderr);
  const final = refused !== null && request.final !== undefined && request.final !== null ? request.final[refused.code] : undefined;
  if (typeof final === "string") {
    fail(request.operation, { call: request.command, row: final }, request.learned);
  }

  const said = answer.timed_out === true || answer.exit_code !== REFUSED_THE_CALL ? null : verdict(answer.stderr);
  return said !== null && said.code.indexOf("auth.") === 0 ? ended(answer, request) : null;
}

function call(context, request) {
  const stdin = request.stdin === undefined || request.stdin === null ? undefined : JSON.stringify(request.stdin);
  return exec(context, commandArgs(context, request.command, request.operands || [], request.flags || []), stdin);
}

// The row of a refusal that says Reply's API has no route for the call (errors.js).
const UNROUTED = "route_not_offered";

// What Reply holds under a key it holds for another request, or null for every other ending, and for a request made
// under no key: an exit 1 whose verdict is that refusal. Its details are read by the operation, which holds them to the
// shapes it answers.
function keptIn(answer, request) {
  if (request.kept !== true || answer.timed_out === true || answer.exit_code !== FAILED) {
    return null;
  }

  const said = verdict(answer.stderr);
  if (said === null || said.code !== KEY_REUSED) {
    return null;
  }

  return {
    details: said.details !== null && typeof said.details === "object" && !Array.isArray(said.details) ? said.details : {},
    ids: said.ids !== null && typeof said.ids === "object" && !Array.isArray(said.ids) ? said.ids : {},
  };
}

// The contact an answer says Reply already held, or null for every other ending: an exit 1 whose verdict is that refusal
// and names a contact in its identifiers. Nothing was done by such a command, so nothing else in the answer is read.
function heldIn(answer) {
  if (answer.timed_out === true || answer.exit_code !== FAILED) {
    return null;
  }

  const said = verdict(answer.stderr);
  const ids = said !== null && said.ids !== null && typeof said.ids === "object" ? said.ids : {};
  return said !== null && said.code === ALREADY_HELD && isReplyNumber(ids.contact_id) ? String(ids.contact_id) : null;
}

// The endings that come before anything the program printed can be read. A timeout is read first, because a process
// that was killed can report -1 exactly as a program that never started does, and reading the exit code first would
// let a write that may have landed be called "never sent". An answer cut off at the exec capture is not parsed at
// all: what survived the cut can even parse, and acting on half an answer is worse than having none. On a read it
// fails with the operation's own row — the same read would be cut at the same byte — and anywhere else it keeps the
// lost-answer ending.
function failIfUnread(answer, command, operation, learned, lost) {
  const observed = { call: command, exit_code: answer.exit_code, timed_out: answer.timed_out === true };

  if (answer.timed_out === true) {
    observed.row = lost;
    fail(operation, observed, learned);
  }

  if (answer.exit_code === NEVER_STARTED) {
    observed.row = "program_never_started";
    fail(operation, observed, learned);
  }

  if (answer.truncated !== null && typeof answer.truncated === "object" && answer.truncated.stdout === true) {
    observed.row = lost === lostAnswerRow(command) ? cutAnswerRow(operation, command) : lost;
    observed.reason = "The answer passed Plugins:Exec:OutputBytes and was cut off.";
    fail(operation, observed, learned);
  }

  return observed;
}

// Every ending of a work command. Nothing else is ever thrown from here: a plugin that threw something of its own
// would have the runtime call it `plugin_exception`, which says nothing about whether the provider acted.
function ended(answer, request) {
  const command = request.command;
  const operation = request.operation;
  const lost = typeof request.lost === "string" ? request.lost : lostAnswerRow(command);
  const observed = failIfUnread(answer, command, operation, request.learned, lost);
  // The sending account a send went from, which a block the command meets is said for.
  observed.sender = request.sender;

  if (answer.exit_code === DONE) {
    const result = resultOf(answer.stdout);
    if (result === null) {
      observed.row = lost;
      fail(operation, observed, request.learned);
    }

    return result;
  }

  const said = verdict(answer.stderr);
  if (answer.exit_code === FAILED && said !== null && typeof request.missing === "string" && said.code === request.missing) {
    return null;
  }

  if (said !== null) {
    observed.cli_code = said.code;
    observed.status = said.status;
    observed.provider_code = said.reply_code;
    observed.provider_detail = said.detail;
    observed.retry_after = said.retry_after;
    if (said.details !== null && typeof said.details === "object") {
      observed.expected = said.details.expected;
      observed.actual = said.details.actual;
      observed.provider_status_code = typeof said.details.status_code === "string" ? said.details.status_code : undefined;
      observed.limit = typeof said.details.limit === "string" ? said.details.limit : undefined;
      observed.missing_variables = namesOf(said.details.missing_variables);
    }
  }

  if (answer.exit_code === OUTCOME_UNKNOWN) {
    observed.row = lost;
  } else if (answer.exit_code === REFUSED_THE_CALL) {
    // Nothing was sent. The CLI's own word says whether it was about who is calling or about what was asked.
    observed.row = said === null ? unexplainedRefusalRow(operation) : rowOf(operation, said, request.variant);
  } else if (answer.exit_code === FAILED && said !== null) {
    observed.row = rowOf(operation, said, request.variant);
  } else {
    // An exit the CLI did not explain: a crash, a stack trace. On a write it may have happened.
    observed.row = lost;
  }

  if (request.kept === true && request.channel === "linkedin" && lostTrackOf(said)) {
    // Reply's own word for a LinkedIn send it lost track of is a failure it answered, in every shape: the key it went
    // under is released, so it settles nothing, and the send is final.
    observed.row = LINKEDIN_SEND_FAILED;
  } else if (request.kept === true && observed.row === lost && answeredAFailure(said)) {
    // Only an ending with no answer of Reply's about the send is the lost answer a repeat under the key settles: none at
    // all, or one at 500 or above. A send that failed with Reply's own 500 left no record, since Reply records the effect
    // as the send's last step, so the repeat under the key it released is the send that did not happen; and a 500 or
    // above from in front of Reply leaves the key as the request left it, held, completed or never claimed.
    observed.row = SEND_ANSWERED_A_FAILURE;
  }

  if (answer.exit_code === FAILED && said !== null && said.code === NOT_FOUND && typeof request.gone === "string") {
    // Reply named this contact and then held nothing under it: no pin of this attempt's went stale, and none is made.
    observed.row = request.gone;
    fail(operation, observed, withoutContact(request.learned));
  }

  fail(operation, observed, request.pins === false ? request.learned : learnedWith(request.learned, said, operation, observed.row, request.kept === true));
}

// Reply's answer about a LinkedIn send it lost track of, as reply-cli from 0.17.1 passes it on (`outcome.unknown`) and
// as 0.17.0 did (`reply.refused`).
function lostTrackOf(said) {
  return said !== null && (said.code === "outcome.unknown" || said.code === "reply.refused") && said.status === 400
    && said.reply_code === LINKEDIN_UNKNOWN;
}

// Whether the CLI's verdict carries an answer of Reply's at a status of 400 to 499 about the send: Reply answered the call
// with a failure. A 409 is the key still held by the first request, or one Reply released with nothing sent, and a 429 is
// refused before the key is touched, so both are repeated under it.
function answeredAFailure(said) {
  return said !== null && Number.isInteger(said.status) && said.status >= 400 && said.status <= 499
    && said.status !== 409 && said.status !== 429;
}

// What this package knew, without a contact: the one a refusal says Reply holds nothing under is nobody's to pin.
function withoutContact(learned) {
  const kept = Object.assign({}, learned || {});
  delete kept.contact;
  return Object.keys(kept).length === 0 ? undefined : kept;
}

// What a failure carries out: what this package knew, joined by the ids the CLI had learned — the contact it created
// before Reply refused the add, say — and, for a direct send made under a key, the reference Reply named it by, whatever
// the ending: it is the work item's send, and the way to read it back. A campaign the provider has just denied is never
// pinned.
function learnedWith(learned, said, operation, row, kept) {
  const merged = Object.assign({}, learned || {});
  const ids = said !== null && said.ids !== null && typeof said.ids === "object" ? said.ids : {};

  if (isReplyNumber(ids.sequence_id)) {
    merged.campaign = String(ids.sequence_id);
  }

  if (isReplyNumber(ids.contact_id)) {
    merged.contact = String(ids.contact_id);
  }

  const details = said !== null && said.details !== null && typeof said.details === "object" ? said.details : {};
  if (kept && isReference(details.reference)) {
    merged.send = details.reference;
  }

  if (codeOf(operation, row) === "campaign_not_found") {
    delete merged.campaign;
  }

  return Object.keys(merged).length === 0 ? undefined : merged;
}

// The CLI's verdict under `--json`: its one `{"error":{"code":…}}` line, the last such line on stderr. Node may print a
// warning before it or after it — a deprecation, an unhandled rejection at exit — so other lines are passed over rather
// than read as no verdict, which would turn a refusal into a lost answer. Bounded on purpose: a program that floods
// stderr may not make reading its tail expensive.
function verdict(stderr) {
  if (typeof stderr !== "string" || stderr.length === 0) {
    return null;
  }

  const tail = stderr.length > STDERR_TAIL ? stderr.slice(stderr.length - STDERR_TAIL) : stderr;
  const lines = tail.split("\n");
  for (let at = lines.length - 1; at >= 0; at--) {
    const line = lines[at].trim();
    if (line.length === 0) {
      continue;
    }

    let parsed;
    try {
      parsed = JSON.parse(line);
    } catch (error) {
      continue;
    }

    const error = parsed !== null && typeof parsed === "object" ? parsed.error : null;
    if (error !== null && typeof error === "object" && typeof error.code === "string") {
      return error;
    }
  }

  return null;
}

// A command's result: one JSON object on stdout. Anything else — nothing, half a line, an array — is not an answer.
function resultOf(stdout) {
  const parsed = jsonOf(stdout);
  return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : null;
}

function jsonOf(stdout) {
  if (typeof stdout !== "string" || stdout.trim().length === 0) {
    return null;
  }

  try {
    return JSON.parse(stdout);
  } catch (error) {
    return null;
  }
}

function isReference(value) {
  return typeof value === "string" && value.length > 0 && value.length <= LONGEST_REFERENCE;
}

function isReplyNumber(value) {
  return typeof value === "number" && Number.isInteger(value) && value > 0 && value <= LARGEST_REPLY_NUMBER;
}

// The names of a template Reply could not fill in, as the CLI lists them: a list of texts, the first twenty of them each up
// to 64 characters, or undefined for anything else. They are the template's names as Reply writes them, never a value.
function namesOf(value) {
  if (!Array.isArray(value) || value.length === 0 || !value.every(name => typeof name === "string")) {
    return undefined;
  }

  return value.slice(0, 20).map(name => name.slice(0, 64));
}
