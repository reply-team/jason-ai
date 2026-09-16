// `campaign.enroll`: the two recovery reads in the document's order, the step resolution, the collision policy
// and the campaign's live state. Not written yet — until it is, the call refuses rather than enrolling anyone on
// a guess, which is the one mistake this operation cannot take back.
export function campaignEnroll(input, context) {
  throw host.fail({
    class: "permanent",
    code: "provider_call_failed",
    message: "This version of the plugin does not perform campaign.enroll yet.",
  });
}
