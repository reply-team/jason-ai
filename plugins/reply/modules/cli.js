// The only place in this package where a command line is built.
//
// One place, so that "a person's address never travels as an argument" and "these flags never appear" are
// properties of the package rather than of whoever wrote the newest module. The `exec` diagnostic records every
// argument, and the runtime's logs may never hold contact data, so a request body goes on stdin and nowhere else.
// A test on the runtime side reads the argument vector out of the stand-in's call log and holds it to exactly the
// shape `args` builds here.
//
// This module knows command lines and nothing about operations; the operation modules know Reply and nothing
// about command lines. That split is what makes the rule above assertable instead of aspirational.
import { callOf, fail, lostAnswerRow } from "./errors.js";

// The vendor program, by the name the manifest declares and the operator grants. The runtime decided at reload
// what that name means on this machine; nothing here looks a program up for itself.
const PROGRAM = "reply";

// The host answers -1 when it could not start the program at all, and the CLI answers 2 when it refuses the call
// it was handed. Neither can have reached the account.
const NEVER_STARTED = -1;
const REFUSED_THE_CALL = 2;

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

/**
 * The whole argument vector for one call, exported so a test can read it rather than infer it from a log.
 * `--json -q [--profile P] [--team-id T] api <path> [--method M] [--body -]`, and nothing else ever.
 */
export function args(context, method, path, hasBody) {
  const binding = (context && context.binding) || {};
  const line = ["--json", "-q"];

  // Which account this call acts in, from the route's binding. Never a key, never a user to act as: the CLI owns
  // the credential, and the binding is written by the people who operate the installation.
  if (binding.profile) {
    line.push("--profile", binding.profile);
  }

  if (binding.team_id) {
    line.push("--team-id", binding.team_id);
  }

  line.push("api", path);

  if (method !== "GET") {
    line.push("--method", method);
  }

  // The body itself goes on stdin. It carries a person's address, and arguments are written to the runtime's log.
  if (hasBody) {
    line.push("--body", "-");
  }

  return line;
}

/**
 * One call to Reply through the CLI: the answer is `{ code, data }` — Reply's own status and its body, whatever
 * that status was — and every other ending throws `host.fail` with the class the table decided. `learned` is what
 * this package already knows Reply calls our entities at the moment of the call; it travels out with a failure,
 * because after a lost answer a pin is the only trace of what was done.
 */
export function call(context, method, path, body, learned) {
  const hasBody = body !== undefined && body !== null;
  const known = callOf(method, path);

  const answer = host.exec(compact({
    executable: PROGRAM,
    args: args(context, method, path, hasBody),
    stdin: hasBody ? JSON.stringify(body) : undefined,
    // The CLI otherwise asks whether a newer release exists: latency inside the operation's budget, a write to
    // the configuration directory, and a notice on stderr, all for nothing. An ordinary name, so it needs no
    // capability — and it is the only variable this package sets on anything.
    env: { REPLY_NO_UPDATE_CHECK: "1" },
  }));

  return read(answer, known, learned);
}

// The four endings that are not an answer, each turned into the row that says what it means. Nothing else is ever
// thrown from here: a plugin that threw something of its own would have the runtime call it `plugin_exception`,
// which says nothing about whether the provider acted.
function read(answer, known, learned) {
  const observed = {
    call: known,
    exit_code: answer.exit_code,
    timed_out: answer.timed_out === true,
  };

  // A timeout is read first, because a process that was killed can report -1 exactly as a program that never
  // started does. Reading the exit code first would let a write that may have landed be called "never sent".
  if (answer.timed_out === true) {
    observed.row = lostAnswerRow(known);
    fail(null, observed, learned);
  }

  if (answer.exit_code === NEVER_STARTED) {
    observed.row = "program_never_started";
    fail(null, observed, learned);
  }

  if (answer.exit_code === REFUSED_THE_CALL) {
    observed.row = "call_refused_before_it_was_sent";
    fail(null, observed, learned);
  }

  const parsed = parse(answer.stdout);
  if (parsed === null) {
    // Exit 1 with nothing to parse: the CLI holds no credential, or it was cut off after the request went out.
    // Its exit vocabulary cannot separate those, so the read/write mark decides, and a write is the expensive
    // ending on purpose.
    observed.row = lostAnswerRow(known);
    fail(null, observed, learned);
  }

  return parsed;
}

// The CLI's own envelope: a status and the body Reply answered with, for any status. Anything else — an empty
// stdout, half a line, a JSON value that is not that envelope — is not an answer and is treated as none.
function parse(stdout) {
  if (typeof stdout !== "string" || stdout.trim().length === 0) {
    return null;
  }

  let parsed;
  try {
    parsed = JSON.parse(stdout);
  } catch (error) {
    return null;
  }

  if (parsed === null || typeof parsed !== "object" || typeof parsed.code !== "number") {
    return null;
  }

  return { code: parsed.code, data: parsed.data === undefined ? null : parsed.data };
}
