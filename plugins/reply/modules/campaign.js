// `campaign.get`: the sequence read, and the mapping of Reply's own state onto the vocabulary the contract
// publishes. Not written yet — this package lands one operation at a time, and until this one is here the call
// refuses plainly rather than answering something a planner would act on.
export function campaignGet(input, context) {
  throw host.fail({
    class: "permanent",
    code: "provider_call_failed",
    message: "This version of the plugin does not perform campaign.get yet.",
  });
}
