// The journal-forwarder reference subscriber: each journal line it is handed is added as JSON to a file, POSTed as JSON to
// one URL, or both, and the envelope it was handed goes on unchanged, so a forwarder anywhere in a chain passes the
// envelope through. It reads nothing of Jason and changes nothing in it. The URL, the file and the optional bearer token
// come from the plugin's configuration first and the environment after it; none is ever written into a failure's message.
const URL_SHAPE = /^https?:\/\/[^\s\/?#]+([\/?#]\S*)?$/i;

// What the settings say for a key, and where they say nothing what the environment says.
function setting(key, variable) {
  const configured = host.config(key);
  return typeof configured === "string" && configured.length > 0 ? configured : host.env(variable);
}

function named(value) {
  return typeof value === "string" && value.length > 0;
}

function post(url, body, id) {
  const headers = { "Content-Type": "application/json", "Jason-Line-Id": String(id) };
  const token = setting("token", "JOURNAL_FORWARDER_TOKEN");
  if (token) {
    headers.Authorization = "Bearer " + token;
  }
  const response = host.http({ method: "POST", url, headers, timeout_ms: 20000, body });
  if (response.status === 0 || response.status === 408 || response.status === 429 || response.status >= 500) {
    throw host.fail({ class: "transient", code: "forwarder_unreachable", message: "the receiver did not take the line for now (" + response.status + ")" });
  }
  if (response.status === 401 || response.status === 403) {
    throw host.fail({ class: "permanent", code: "forwarder_unauthorized", message: "the receiver refused the credential (" + response.status + ")" });
  }
  if (response.status < 200 || response.status >= 300) {
    throw host.fail({ class: "permanent", code: "forwarder_refused", message: "the receiver refused the line (" + response.status + ")" });
  }
}

export function invoke(operation, input, context) {
  if (typeof operation !== "string" || !input || !input.line || input.line.kind !== operation) {
    throw host.fail({ class: "validation", code: "unknown_operation", message: "the forwarder takes a journal line under the line's own kind" });
  }
  const url = setting("url", "JOURNAL_FORWARDER_URL");
  const file = setting("file", "JOURNAL_FORWARDER_FILE");
  if (!named(url) && !named(file)) {
    throw host.fail({ class: "permanent", code: "forwarder_receiver_missing", message: "neither the keys url and file under Plugins:Config:journal-forwarder nor JOURNAL_FORWARDER_URL and JOURNAL_FORWARDER_FILE name a receiver" });
  }
  if (named(url) && (url.length > 4096 || !URL_SHAPE.test(url))) {
    throw host.fail({ class: "permanent", code: "forwarder_url_invalid", message: "the configured receiver is not an http or https URL" });
  }
  const body = JSON.stringify({ line: input.line, envelope: input.envelope, attempt: input.attempt });
  // The file goes first: a POST can take the invocation's whole time, and a kill after it would write nothing. Only what
  // the disk did can be caught here, so the receiver still has the line, and the disk's failure is what the handler ends
  // with, in this plugin's own word: the receiver drops a repeat, so a POST made since is no reason not to try again. A
  // rule the file broke is a misconfiguration, which no plugin code sees: it ends the invocation where it stands.
  let failure = null;
  if (named(file)) {
    try {
      host.appendLine(file, body);
    } catch (refused) {
      if (!named(url) || refused.code !== "file_write_failed") {
        throw refused;
      }
      failure = host.fail({ class: "transient", code: refused.code, message: refused.message });
    }
  }
  if (named(url)) {
    try {
      post(url, body, input.line.id);
    } catch (other) {
      throw failure || other;
    }
  }
  if (failure) {
    throw failure;
  }
  return { result: { envelope: input.envelope } };
}
