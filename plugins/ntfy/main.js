// The ntfy reference plugin: Jason's pointer - the counts the runtime composes, and where to look - published
// to an ntfy topic. It is sent as JSON to the server's root, so the topic travels in the body and the host's log line
// about the call names the root and not the topic.
export function invoke(operation, input, context) {
  if (operation !== "notification.send") {
    throw host.fail({ class: "validation", code: "unknown_operation", message: "ntfy delivers notification.send only" });
  }
  const binding = context.binding || {};
  const server = binding.server || "https://ntfy.sh";
  const headers = { "Content-Type": "application/json" };
  const token = host.env("NTFY_TOKEN");
  if (token) {
    headers.Authorization = "Bearer " + token;
  }
  const response = host.http({ method: "POST", url: server + "/", headers,
    body: JSON.stringify({ topic: binding.topic, title: input.title, message: input.message }) });
  if (response.status === 0) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "the server did not answer: " + response.error });
  }
  if (response.status === 401 || response.status === 403) {
    throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "the server refused the credential (" + response.status + ")" });
  }
  if (response.status === 429 || response.status >= 500) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "the server cannot take messages for now (" + response.status + ")" });
  }
  if (response.status < 200 || response.status >= 300) {
    throw host.fail({ class: "permanent", code: "notification_refused", message: "the server refused the message (" + response.status + ")" });
  }
  let id;
  try { id = JSON.parse(response.body).id; } catch (e) { id = undefined; }
  return { result: typeof id === "string" && id.length > 0 ? { receipt: id.slice(0, 256) } : {} };
}
