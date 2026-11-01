// A second provider that answers from its input and nothing else: it asks for no capability, starts no program
// and reaches no network, so whatever it returns came from the call itself. It exists to be the other candidate
// when something has to choose between two plugins, and to refuse an operation it never declared.

function pinnedContact(contact) {
  const channel = contact.channels[0];
  return "other-" + channel.channel + "-" + channel.value;
}

export function invoke(operation, input) {
  switch (operation) {
    case "campaign.get": {
      const externalId = (input.args && input.args.campaign && input.args.campaign.external_id)
        || (input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign);

      return {
        result: {
          campaign: {
            external_id: externalId,
            name: input.campaign.name,
            // It knows nothing about the provider's own states, and `other` is the honest word for that.
            status: "other",
            counts: {},
          },
          // The neutral part of a result is strict, so the one thing worth saying about the answer's origin
          // goes where a provider's own words belong.
          vendor: { answered_by: "other-provider" },
        },
        external_ids: { campaign: externalId },
      };
    }

    case "list_membership.add": {
      const contact = input.contacts[0];
      const provider = pinnedContact(contact);

      return {
        result: {
          items: [{ contact_id: contact.id, status: "added", external_ids: { contact: provider } }],
        },
        external_ids: { contact: provider },
      };
    }

    default:
      throw host.fail({
        class: "validation",
        code: "unknown_operation",
        message: "other-provider does not implement " + operation,
      });
  }
}
