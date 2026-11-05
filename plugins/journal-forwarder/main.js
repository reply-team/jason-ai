// The journal-forwarder reference subscriber: each journal line it is handed is POSTed as JSON to one URL, and the
// envelope it was handed goes on unchanged, so a forwarder anywhere in a chain passes the envelope through. It reads
// nothing of Jason and changes nothing in it. The URL and the optional bearer token come from the environment by name;
// neither is ever written into a failure's message.
const URL_SHAPE = /^https?:\/\/[^\s\/?#]+([\/?#]\S*)?$/i;

export function invoke(operation, input, context) {
  if (typeof operation !== "string" || !input || !input.line || input.line.kind !== operation) {
    throw host.fail({ class: "validation", code: "unknown_operation", message: "the forwarder takes a journal line under the line's own kind" });
  }
  const url = host.env("JOURNAL_FORWARDER_URL");
  if (typeof url !== "string" || url.length === 0) {
    throw host.fail({ class: "permanent", code: "forwarder_url_missing", message: "JOURNAL_FORWARDER_URL is not set in the runtime's environment" });
  }
  if (url.length > 4096 || !URL_SHAPE.test(url)) {
    throw host.fail({ class: "permanent", code: "forwarder_url_invalid", message: "JOURNAL_FORWARDER_URL is not an http or https URL" });
  }
  const headers = { "Content-Type": "application/json", "Jason-Line-Id": String(input.line.id) };
  const token = host.env("JOURNAL_FORWARDER_TOKEN");
  if (token) {
    headers.Authorization = "Bearer " + token;
  }
  const response = host.http({ method: "POST", url, headers, timeout_ms: 20000,
    body: JSON.stringify({ line: input.line, envelope: input.envelope, attempt: input.attempt }) });
  if (response.status === 0 || response.status === 408 || response.status === 429 || response.status >= 500) {
    throw host.fail({ class: "transient", code: "forwarder_unreachable", message: "the receiver did not take the line for now (" + response.status + ")" });
  }
  if (response.status === 401 || response.status === 403) {
    throw host.fail({ class: "permanent", code: "forwarder_unauthorized", message: "the receiver refused the credential (" + response.status + ")" });
  }
  if (response.status < 200 || response.status >= 300) {
    throw host.fail({ class: "permanent", code: "forwarder_refused", message: "the receiver refused the line (" + response.status + ")" });
  }
  return { result: { envelope: input.envelope } };
}
