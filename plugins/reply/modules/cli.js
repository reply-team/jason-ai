// The only place in this package where a command line is built.
//
// One place, so that "a person's data never travels as an argument" and "these flags never appear" are properties of
// the package rather than of whoever wrote the newest module. The `exec` diagnostic records every argument, and the
// runtime's logs may never hold contact data, so a person goes on stdin and nowhere else. A test on the runtime side
// reads the argument vector out of the stand-in's call log and holds it to exactly the shapes built here.
//
// This module knows command lines and how the Reply CLI ends, and nothing about operations; the operation modules know
// which command serves them and nothing about command lines. That split is what makes the rules above assertable
// instead of aspirational.
import { codeOf, cutAnswerRow, fail, lostAnswerRow, rowOf } from "./errors.js";

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
 * The whole argument vector of the one raw call: `--json -q [--profile P] [--team-id T] api <path> [--method M]
 * [--body -]`. The body itself goes on stdin: it carries a person's address, and arguments are written to the log.
 */
export function apiArgs(context, method, path, hasBody) {
  const line = prefix(context).concat(["api", path]);
  if (method !== "GET") {
    line.push("--method", method);
  }

  if (hasBody) {
    line.push("--body", "-");
  }

  return line;
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
    // reply-cli 0.11.0 looks for a newer release only under `--version`, which this package never runs; the variable
    // keeps a later release from doing it on a work command, where it would cost time inside the operation's budget,
    // a write to the configuration directory and a notice on stderr. An ordinary name, so it needs no capability —
    // and it is the only variable this package sets on anything.
    env: { REPLY_NO_UPDATE_CHECK: "1" },
  }));
}

/**
 * One reply-cli work command: its result object when it exits 0, and `host.fail` with the row the table decides for
 * every other ending. `request` is `{ operation, command, operands, flags, stdin, learned, pins }`: the canonical
 * operation the command serves, the command's two words, its ids, its flags, the person for stdin, and what this
 * package already knows Reply calls our entities — which travels out with a failure, joined by whatever the CLI says it
 * had learned before it failed. `pins: false` keeps what the CLI learned out of the failure, for a command that names
 * something which is not ours yet.
 */
export function run(context, request) {
  const stdin = request.stdin === undefined || request.stdin === null ? undefined : JSON.stringify(request.stdin);
  const answer = exec(context, commandArgs(context, request.command, request.operands || [], request.flags || []), stdin);
  return ended(answer, request);
}

/**
 * The raw call this package still makes, `reply api`, where reply-cli has no command for the request: the contact
 * search a list add, an enrolment or an opt-out makes for a person with no pin and no first name, and the list and the
 * create of sequences `campaign.create` makes. The answer is `{ code, data }` — Reply's own status and its body,
 * whatever that status was — and every ending without one throws `host.fail`. The table knows the call as
 * `api <METHOD> <path without its query>`, which is how its read/write mark is found.
 */
export function call(context, method, path, body, learned, operation) {
  const hasBody = body !== undefined && body !== null;
  const named = "api " + method + " " + String(path).split("?")[0];
  const answer = exec(context, apiArgs(context, method, path, hasBody), hasBody ? JSON.stringify(body) : undefined);
  return answered(answer, named, learned, operation);
}

// The endings that come before anything the program printed can be read. A timeout is read first, because a process
// that was killed can report -1 exactly as a program that never started does, and reading the exit code first would
// let a write that may have landed be called "never sent". An answer cut off at the exec capture is not parsed at
// all: what survived the cut can even parse, and acting on half an answer is worse than having none. On a read it
// fails with the operation's own row — the same read would be cut at the same byte — and anywhere else it keeps the
// lost-answer ending.
function failIfUnread(answer, command, operation, learned) {
  const observed = { call: command, exit_code: answer.exit_code, timed_out: answer.timed_out === true };

  if (answer.timed_out === true) {
    observed.row = lostAnswerRow(command);
    fail(operation, observed, learned);
  }

  if (answer.exit_code === NEVER_STARTED) {
    observed.row = "program_never_started";
    fail(operation, observed, learned);
  }

  if (answer.truncated !== null && typeof answer.truncated === "object" && answer.truncated.stdout === true) {
    observed.row = cutAnswerRow(operation, command);
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
  const observed = failIfUnread(answer, command, operation, request.learned);

  if (answer.exit_code === DONE) {
    const result = resultOf(answer.stdout);
    if (result === null) {
      observed.row = lostAnswerRow(command);
      fail(operation, observed, request.learned);
    }

    return result;
  }

  const said = verdict(answer.stderr);
  if (said !== null) {
    observed.cli_code = said.code;
    observed.status = said.status;
    observed.provider_code = said.reply_code;
    observed.provider_detail = said.detail;
    observed.retry_after = said.retry_after;
    if (said.details !== null && typeof said.details === "object") {
      observed.expected = said.details.expected;
      observed.actual = said.details.actual;
    }
  }

  if (answer.exit_code === OUTCOME_UNKNOWN) {
    observed.row = lostAnswerRow(command);
  } else if (answer.exit_code === REFUSED_THE_CALL) {
    // Nothing was sent. The CLI's own word says whether it was about who is calling or about what was asked.
    observed.row = said === null ? "call_refused_before_it_was_sent" : rowOf(operation, said);
  } else if (answer.exit_code === FAILED && said !== null) {
    observed.row = rowOf(operation, said);
  } else {
    // An exit the CLI did not explain: a crash, a stack trace. On a write it may have happened.
    observed.row = lostAnswerRow(command);
  }

  fail(operation, observed, request.pins === false ? request.learned : learnedWith(request.learned, said, operation, observed.row));
}

// Every ending of the raw call. `reply api` prints `{code, data}` for any status Reply answered, with exit 0 or 1, and
// that is the answer whatever the status. An ending without it is the CLI's own: a refusal before sending (exit 2),
// Reply not reached (exit 1), or a write whose outcome is unknown (exit 3) — which the CLI says of any POST, this
// search included, and which the table's mark for this call reads as the read it is.
function answered(answer, named, learned, operation) {
  const observed = failIfUnread(answer, named, operation, learned);

  const envelope = envelopeOf(answer.stdout);
  if (envelope !== null) {
    return envelope;
  }

  const said = verdict(answer.stderr);
  if (said !== null) {
    observed.cli_code = said.code;
  }

  if (answer.exit_code === REFUSED_THE_CALL) {
    observed.row = said === null ? "call_refused_before_it_was_sent" : rowOf(operation, said);
  } else {
    observed.row = lostAnswerRow(named);
  }

  fail(operation, observed, learned);
}

// What a failure carries out: what this package knew, joined by the ids the CLI had learned — the contact it created
// before Reply refused the add, say. A campaign the provider has just denied is never pinned.
function learnedWith(learned, said, operation, row) {
  const merged = Object.assign({}, learned || {});
  const ids = said !== null && said.ids !== null && typeof said.ids === "object" ? said.ids : {};

  if (isReplyNumber(ids.sequence_id)) {
    merged.campaign = String(ids.sequence_id);
  }

  if (isReplyNumber(ids.contact_id)) {
    merged.contact = String(ids.contact_id);
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

// The raw call's envelope: a status and the body Reply answered with, for any status. Anything else is not an answer.
function envelopeOf(stdout) {
  const parsed = jsonOf(stdout);
  if (parsed === null || typeof parsed !== "object" || typeof parsed.code !== "number") {
    return null;
  }

  return { code: parsed.code, data: parsed.data === undefined ? null : parsed.data };
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

function isReplyNumber(value) {
  return typeof value === "number" && Number.isInteger(value) && value > 0 && value <= LARGEST_REPLY_NUMBER;
}
