// The official Reply plugin: Jason's canonical operations, performed against a Reply account through the Reply
// CLI, which owns the credential. Nothing here is privileged — the same loader, the same grants, the same routes
// and the same published contracts as any community package.
//
// This file knows operations and nothing about Reply; the operation modules know Reply and nothing about command
// lines; `modules/cli.js` knows command lines and nothing about operations.
import { campaignGet } from "./modules/campaign.js";
import { campaignCreate } from "./modules/create.js";
import { listMembershipAdd } from "./modules/membership.js";
import { campaignEnroll } from "./modules/enroll.js";
import { enrollmentList } from "./modules/enrollments.js";
import { engagementSummarize } from "./modules/stats.js";
import { conversationGet, conversationList } from "./modules/inbox.js";
import { campaignActivate, campaignPause } from "./modules/sequence.js";
import { optoutRecord } from "./modules/optout.js";
import { conversationReply } from "./modules/reply.js";
import { enrollmentStop } from "./modules/stop.js";
import { conversationClassify, conversationClose, conversationReopen } from "./modules/thread.js";
import { contactDelete } from "./modules/delete.js";
import { contactUpdate } from "./modules/update.js";

export function invoke(operation, input, context) {
  switch (operation) {
    case "campaign.get":
      return campaignGet(input, context);

    case "campaign.create":
      return campaignCreate(input, context);

    case "list_membership.add":
      return listMembershipAdd(input, context);

    case "campaign.enroll":
      return campaignEnroll(input, context);

    case "enrollment.list":
      return enrollmentList(input, context);

    case "engagement.summarize":
      return engagementSummarize(input, context);

    case "conversation.list":
      return conversationList(input, context);

    case "conversation.get":
      return conversationGet(input, context);

    case "campaign.pause":
      return campaignPause(input, context);

    case "campaign.activate":
      return campaignActivate(input, context);

    case "optout.record":
      return optoutRecord(input, context);

    case "enrollment.stop":
      return enrollmentStop(input, context);

    case "conversation.reply":
      return conversationReply(input, context);

    case "conversation.classify":
      return conversationClassify(input, context);

    case "conversation.close":
      return conversationClose(input, context);

    case "conversation.reopen":
      return conversationReopen(input, context);

    case "contact.update":
      return contactUpdate(input, context);

    case "contact.delete":
      return contactDelete(input, context);

    default:
      // A work item can only name an operation the manifest lists, so this is a call that should not exist. It
      // is a validation failure rather than a provider one, because nothing was ever asked of Reply — and the
      // code is this package's own word rather than one a contract publishes, since no contract covers a call
      // that named no operation this plugin has.
      throw host.fail({
        class: "validation",
        code: "unknown_operation",
        message: "This plugin does not implement " + operation + ".",
      });
  }
}
