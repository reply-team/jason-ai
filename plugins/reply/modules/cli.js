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

// The namespace reply-cli 0.5.1 gives the refusals that are about who is calling: `auth.required` when no
// credential is stored, `auth.expired` and `auth.refresh_failed` when one is there and no longer works. All
// three are decided before a request is built, and the set is open — a fourth would mean the same thing — so the
// namespace is matched rather than the three names.
const AUTHENTICATION = "auth.";

// How much of a program's stderr is read to find its last line. A refusal is one short line; the rest is a
// bound, not a budget.
const STDERR_TAIL = 8192;

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

// The endings that are not an answer, each turned into the row that says what it means. Nothing else is ever
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

  const parsed = parse(answer.stdout);

  // Where the CLI left no answer at all, its own refusal decides — and it says why in a structured envelope,
  // because this package always passes `--json`. That vocabulary is finer than the exit code's: "not signed in"
  // and "unknown flag" are both exit 2, and only one of them is something an operator fixes by signing in. A
  // refusal that was printed beside a readable answer is not read: an answer on stdout is the answer.
  const said = parsed === null ? refusal(answer.stderr) : null;
  if (said !== null && said.slice(0, AUTHENTICATION.length) === AUTHENTICATION) {
    // Nothing was sent: the CLI never had a credential to send it with. Saying a write may have happened here
    // would stop a work item for a person over an account that was never touched.
    observed.row = "unauthorized";
    // The CLI's own word for its refusal, in the field that carries the vendor side's own code. It is the whole
    // of what an operator has to go on here, because there is no HTTP status to report.
    observed.provider_code = said;
    fail(null, observed, learned);
  }

  if (answer.exit_code === REFUSED_THE_CALL) {
    observed.row = "call_refused_before_it_was_sent";
    observed.provider_code = said === null ? undefined : said;
    fail(null, observed, learned);
  }

  if (parsed === null) {
    // Exit 1 with nothing to parse and no refusal of the CLI's own: it was cut off, or the machine it runs on
    // could not reach Reply — endings that can be met after the request went out. The read/write mark decides,
    // and a write is the expensive ending on purpose.
    observed.row = lostAnswerRow(known);
    fail(null, observed, learned);
  }

  return parsed;
}

// The CLI's own refusal as it prints it under `--json`: one line of `{"error":{"code":…}}` on stderr, and the
// exit code beside it. Only the code is read — the title and the hint are prose for a person — and only from the
// last line, because anything the program said before it is not its verdict. Nothing here throws: stderr is
// whatever the machine produced, and an unreadable one simply says nothing.
function refusal(stderr) {
  if (typeof stderr !== "string" || stderr.length === 0) {
    return null;
  }

  // Bounded on purpose: a program that floods stderr may not make reading its last line expensive.
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
      return null;
    }

    const error = parsed !== null && typeof parsed === "object" ? parsed.error : null;
    return error !== null && typeof error === "object" && typeof error.code === "string" ? error.code : null;
  }

  return null;
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
