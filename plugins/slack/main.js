// The Slack plugin: Jason's pointer - the counts the runtime composes, and where to look - posted to one Slack channel
// with chat.postMessage. Only the pointer's text is sent: no title, no counts, no formatting of its own.
const API = "https://slack.com/api/chat.postMessage";

// Slack's own error words, by what repeating the call could change.
const UNAUTHORIZED = ["invalid_auth", "not_authed", "token_revoked", "token_expired", "account_inactive", "missing_scope",
  "not_allowed_token_type", "no_permission", "team_access_not_granted"];
const TRANSIENT = ["ratelimited", "rate_limited", "fatal_error", "internal_error", "service_unavailable", "request_timeout",
  "team_added_to_org", "org_login_required"];

// What the settings say for a key, and where they say nothing what the environment says.
function setting(key, variable) {
  const configured = host.config(key);
  return typeof configured === "string" && configured.length > 0 ? configured : host.env(variable);
}

export function invoke(operation, input, context) {
  if (operation !== "notification.send") {
    throw host.fail({ class: "validation", code: "unknown_operation", message: "slack delivers notification.send only" });
  }
  const binding = context.binding || {};
  const token = setting("token", "SLACK_BOT_TOKEN");
  if (typeof token !== "string" || token.length === 0) {
    throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "neither Plugins:Config:slack:token nor SLACK_BOT_TOKEN holds a bot token" });
  }
  // mrkdwn is on by default at Slack, and an underscore in a campaign id must not italicise.
  const response = host.http({ method: "POST", url: API,
    headers: { "Content-Type": "application/json; charset=utf-8", Authorization: "Bearer " + token },
    body: JSON.stringify({ channel: binding.channel, text: input.message, mrkdwn: false }) });
  if (response.status === 0) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "Slack did not answer: " + response.error });
  }
  if (response.status === 401 || response.status === 403) {
    throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "Slack refused the credential (" + response.status + ")" });
  }
  if (response.status === 429 || response.status >= 500) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "Slack cannot take messages for now (" + response.status + ")" });
  }
  if (response.status < 200 || response.status >= 300) {
    throw host.fail({ class: "permanent", code: "notification_refused", message: "Slack refused the message (" + response.status + ")" });
  }
  let answer;
  try { answer = JSON.parse(response.body); } catch (e) { answer = undefined; }
  if (!answer || typeof answer !== "object" || typeof answer.ok !== "boolean") {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "Slack's answer was not the JSON it documents" });
  }
  if (!answer.ok) {
    const word = typeof answer.error === "string" && /^[a-z0-9_]{1,80}$/.test(answer.error) ? answer.error : "unspecified";
    if (UNAUTHORIZED.includes(word)) {
      throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "Slack answered " + word });
    }
    if (TRANSIENT.includes(word)) {
      throw host.fail({ class: "transient", code: "notification_unreachable", message: "Slack answered " + word });
    }
    throw host.fail({ class: "permanent", code: "notification_refused", message: "Slack answered " + word });
  }
  const ts = answer.ts;
  return { result: typeof ts === "string" && ts.length > 0 ? { receipt: ts.slice(0, 256) } : {} };
}
