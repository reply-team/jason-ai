// The official Reply plugin: Jason's canonical operations, performed against a Reply account through the Reply
// CLI, which owns the credential. Nothing here is privileged — the same loader, the same grants, the same routes
// and the same published contracts as any community package.
//
// This file knows operations and nothing about Reply; the operation modules know Reply and nothing about command
// lines; `modules/cli.js` knows command lines and nothing about operations.
import { campaignGet } from "./modules/campaign.js";
import { listMembershipAdd } from "./modules/membership.js";
import { campaignEnroll } from "./modules/enroll.js";

export function invoke(operation, input, context) {
  switch (operation) {
    case "campaign.get":
      return campaignGet(input, context);

    case "list_membership.add":
      return listMembershipAdd(input, context);

    case "campaign.enroll":
      return campaignEnroll(input, context);

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
