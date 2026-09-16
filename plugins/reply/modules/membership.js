// `list_membership.add`: the provider-contact precondition, the suppression read, the add itself, and the
// recovery read on a repeated attempt. Not written yet — until it is, the call refuses rather than pretending to
// have added anyone.
export function listMembershipAdd(input, context) {
  throw host.fail({
    class: "permanent",
    code: "provider_call_failed",
    message: "This version of the plugin does not perform list_membership.add yet.",
  });
}
