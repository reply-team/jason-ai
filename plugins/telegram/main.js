// The Telegram plugin: Jason's pointer - the counts the runtime composes, and where to look - sent to one chat by a bot.
// The Bot API takes the bot's token in the path of the request; the plugin host masks it in every line it writes.
const BASE = "https://api.telegram.org";

function describe(response) {
  // Telegram's own words for a refusal, when the body is JSON and carries them; never more than a line.
  try {
    const description = JSON.parse(response.body).description;
    return typeof description === "string" && description.length > 0 ? ": " + description.slice(0, 200) : "";
  } catch (e) {
    return "";
  }
}

// A chat that is digits only travels as the number Telegram documents, when a number can hold it exactly; an @name, and
// a number no runtime could hold, travel as text.
function chatId(chat) {
  if (/^-?[0-9]+$/.test(chat)) {
    const number = Number(chat);
    if (Number.isSafeInteger(number) && String(number) === chat) {
      return number;
    }
  }
  return chat;
}

export function invoke(operation, input, context) {
  if (operation !== "notification.send") {
    throw host.fail({ class: "validation", code: "unknown_operation", message: "telegram delivers notification.send only" });
  }
  const binding = context.binding || {};
  const token = host.config("token") || host.env("TELEGRAM_BOT_TOKEN");
  if (!token) {
    throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "no bot token: set it under Plugins:Config:telegram or in TELEGRAM_BOT_TOKEN" });
  }
  const response = host.http({ method: "POST", url: BASE + "/bot" + token + "/sendMessage",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ chat_id: chatId(binding.chat), text: input.message }) });
  if (response.status === 0) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "the server did not answer: " + response.error });
  }
  if (response.status === 401 || response.status === 404) {
    throw host.fail({ class: "permanent", code: "notification_unauthorized", message: "the server did not accept the bot token (" + response.status + ")" + describe(response) });
  }
  if (response.status === 429 || response.status >= 500) {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "the server cannot take messages for now (" + response.status + ")" + describe(response) });
  }
  if (response.status < 200 || response.status >= 300) {
    throw host.fail({ class: "permanent", code: "notification_refused", message: "the server refused the message (" + response.status + ")" + describe(response) });
  }
  let answer;
  try { answer = JSON.parse(response.body); } catch (e) { answer = undefined; }
  if (answer === null || typeof answer !== "object" || typeof answer.ok !== "boolean") {
    throw host.fail({ class: "transient", code: "notification_unreachable", message: "the server answered with something that is not a Bot API answer (" + response.status + ")" });
  }
  if (!answer.ok) {
    throw host.fail({ class: "permanent", code: "notification_refused", message: "the server did not send the message" + describe(response) });
  }
  const id = answer.result && answer.result.message_id;
  return { result: id !== undefined && id !== null ? { receipt: String(id).slice(0, 256) } : {} };
}
