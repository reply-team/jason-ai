// The demo provider: every operation the sample story needs, performed against nobody. The package requests no program,
// no host, no variable and no file, so the plugin host gives it none and nothing here can leave the process.
import { eventsRead } from "./modules/events.js";
import { conversationGet, conversationList, conversationReply, senderList } from "./modules/conversations.js";
import { invitationSend, messageSend } from "./modules/sends.js";

const OPERATIONS = {
  "message.send": messageSend,
  "social_invitation.send": invitationSend,
  "conversation.reply": conversationReply,
  "sender.list": senderList,
  "conversation.list": conversationList,
  "conversation.get": conversationGet,
  "events.read": eventsRead,
};

export function invoke(operation, input) {
  const perform = OPERATIONS[operation];
  if (!perform) {
    // Unreachable: the runtime invokes only the operations the manifest lists, and each has its entry above.
    throw host.fail({ class: "permanent", code: "provider_call_failed", message: "The demo provider does not perform " + operation + "." });
  }

  return perform(input);
}
