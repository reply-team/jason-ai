// The sends: message.send and social_invitation.send are taken and delivered to nobody, at once; nothing is held for a
// later moment. Nothing is kept between runs either, so every identifier is made from the work item's own key: a repeat
// under the same key is answered with the same reference.
import { fail } from "./fail.js";
import { iso, now } from "./clock.js";
import { createdContact, isOwnContact, personById, personByValue, senders, valueOf } from "./people.js";

/** The person's contact here: by the pin, else by the consumed value, else made for them. */
function settle(contact, channel) {
  const value = contact.channels[0].value;
  const pin = contact.external_ids && contact.external_ids.contact;
  if (pin) {
    if (!isOwnContact(pin)) {
      throw fail("permanent", "contact_not_found", "The contact pinned for this person is not one the demo provider holds. Nothing was sent.");
    }

    const person = personById(pin);
    const held = person ? valueOf(person, channel) : null;
    if (held !== null && host.channelKey(channel, held) !== value) {
      throw fail("permanent", "contact_channel_conflict", "The demo provider holds this person at another " + channel + " value. Nothing was sent.",
        { external_ids: { contact: pin } });
    }

    return { id: pin, origin: null };
  }

  const person = personByValue(channel, value);
  return person ? { id: person.contact_id, origin: "found" } : { id: createdContact(channel, value), origin: "created" };
}

function same(channel, stated, held) {
  if (stated === null || held === null) {
    return stated === held;
  }

  return channel === "email" ? stated.toLowerCase() === held.toLowerCase() : stated === held;
}

/** The account named, held to the address the arguments state. */
function sender(args, channel, contact) {
  const account = senders().find((candidate) => candidate.external_id === args.sender.external_id && candidate.channel === channel);
  if (!account) {
    throw fail("permanent", "sender_not_found", "The demo provider holds no " + channel + " account under that identifier. Nothing was sent.",
      { external_ids: { contact: contact.id } });
  }

  if (!same(channel, args.sender.address, account.address)) {
    throw fail("validation", "sender_mismatch", "The demo provider holds that account under another address. Nothing was sent.",
      { external_ids: { contact: contact.id } });
  }

  return account;
}

function answer(item, contact, ref) {
  const answered = { result: { items: [item] }, external_ids: { contact: contact.id, send: ref } };
  if (contact.origin !== null) {
    answered.external_id_origins = { contact: contact.origin };
  }

  return answered;
}

export function messageSend(input) {
  const args = input.args;
  const contact = settle(input.contacts[0], args.channel);
  const account = sender(args, args.channel, contact);
  const key = input.idempotency_key.slice(3);
  const ref = "demo-send-" + key;
  return answer({
    contact_id: input.contacts[0].id,
    status: "sent",
    accepted_for: null,
    reference: ref,
    cancelable: false,
    channel: args.channel,
    format: args.message.format,
    sender_external_id: account.external_id,
    message: { external_id: "demo-msg-" + key, sent_at: iso(now()) },
    external_ids: { contact: contact.id, send: ref },
  }, contact, ref);
}

export function invitationSend(input) {
  const args = input.args;
  const contact = settle(input.contacts[0], "linkedin");
  const account = sender(args, "linkedin", contact);
  const key = input.idempotency_key.slice(3);
  const ref = "demo-send-" + key;
  return answer({
    contact_id: input.contacts[0].id,
    status: "sent",
    accepted_for: null,
    reference: ref,
    cancelable: false,
    sender_external_id: account.external_id,
    invitation: { external_id: "demo-inv-" + key, sent_at: iso(now()) },
    external_ids: { contact: contact.id, send: ref },
  }, contact, ref);
}
