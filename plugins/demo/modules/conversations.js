// The written threads: sender.list, conversation.list, conversation.get and conversation.reply. A thread is one of the
// story's approved sends with the replies written to it; it exists once the story has started, its message at the start
// and each reply at its own offset after it, shown only once that moment has come.
import { fail } from "./fail.js";
import { iso, now, replyAt, storyStart } from "./clock.js";
import { fullName, isOwnContact, personById, senders, threadById, threads, valueOf } from "./people.js";

const PREVIEW = 500;

function account(channel) {
  return senders().find((candidate) => candidate.channel === channel);
}

/** A thread's messages as they stand now: the send, then each reply whose moment has come, oldest first. */
function messages(thread, start, at) {
  const own = account(thread.channel);
  const to = valueOf(thread.person, thread.channel);
  const list = [{
    external_id: "demo-msg-" + thread.send.key,
    direction: "outbound",
    sent_at: iso(start),
    from: own.address,
    to: to === null ? null : [to],
    cc: null,
    bcc: null,
    subject: thread.subject,
    body: thread.send.body,
    truncated: false,
    moment: start,
    meeting_intent: null,
  }];
  thread.replies.forEach((reply, index) => {
    const moment = replyAt(start, reply);
    if (moment > at) {
      return;
    }

    list.push({
      external_id: "demo-reply-" + thread.send.key + "-" + (index + 1),
      direction: "inbound",
      sent_at: iso(moment),
      from: to,
      to: [own.address],
      cc: null,
      bcc: null,
      subject: thread.subject === null ? null : "Re: " + thread.subject,
      body: reply.body,
      truncated: false,
      moment,
      meeting_intent: reply.meeting_intent,
    });
  });
  return list;
}

function contactOf(thread) {
  return { external_id: thread.person.contact_id, name: fullName(thread.person) };
}

function page(items, args) {
  const slice = items.slice(args.offset, args.offset + args.limit);
  return { slice, has_more: args.offset + slice.length < items.length };
}

export function senderList(input) {
  const asked = input.args.sender_channel;
  const listed = senders().filter((candidate) => asked === undefined || candidate.channel === asked).map((candidate) => ({
    external_id: candidate.external_id,
    channel: candidate.channel,
    name: candidate.name,
    address: candidate.address,
    status: "active",
    owner: "own",
    vendor: { status: "demo" },
  }));
  const { slice, has_more } = page(listed, input.args);
  return {
    result: {
      items: slice,
      has_more,
      channels: {
        email: asked === undefined || asked === "email" ? "listed" : "not_asked",
        linkedin: asked === undefined || asked === "linkedin" ? "listed" : "not_asked",
      },
      vendor: { ordering: "email_then_linkedin" },
    },
  };
}

/** A person's threads, newest activity first. A read names nobody new, so a person without a pin is refused. */
export function conversationList(input) {
  const args = input.args;
  const start = storyStart();
  const at = now();
  let mine = [];
  if (input.contacts) {
    const pin = input.contacts[0].external_ids.contact;
    if (!pin) {
      throw fail("permanent", "contact_not_linked", "The demo provider holds no pin for this person, and a read creates nobody.");
    }

    mine = start === null || !isOwnContact(pin) ? [] : threads().filter((thread) => thread.person.contact_id === pin);
  }

  const since = args.since === undefined ? null : Date.parse(args.since);
  const items = mine
    .filter(() => args.category === undefined)
    .filter((thread) => args.thread_channel === undefined || thread.channel === args.thread_channel)
    .map((thread) => {
      const list = messages(thread, start, at);
      const last = list[list.length - 1];
      return { thread, last, inbound: list.some((message) => message.direction === "inbound") };
    })
    .filter((entry) => args.source === "all" || entry.inbound)
    .filter((entry) => since === null || entry.last.moment >= since)
    .sort((a, b) => b.last.moment - a.last.moment)
    .map((entry) => ({
      external_id: entry.thread.id,
      channel: entry.thread.channel,
      subject: entry.thread.subject,
      preview: entry.last.body.slice(0, PREVIEW),
      last_activity_at: iso(entry.last.moment),
      unread: entry.last.direction === "inbound",
      contact: contactOf(entry.thread),
      campaign_external_id: null,
      has_meeting_intent: entry.last.direction === "inbound" ? entry.last.meeting_intent : null,
      vendor: { is_last_message_planned: false, state: "ok", category: null },
    }));
  const { slice, has_more } = page(items, args);
  return { result: { items: slice, has_more, vendor: { ordering: "last_activity_desc" } } };
}

function writtenThread(id) {
  const start = storyStart();
  const thread = threadById(id);
  if (start === null || thread === null) {
    throw fail("permanent", "conversation_not_found", "The demo provider holds no thread under this identifier.");
  }

  return { thread, start };
}

export function conversationGet(input) {
  const { thread, start } = writtenThread(input.args.thread.external_id);
  const list = messages(thread, start, now());
  const { slice, has_more } = page(list, input.args);
  const last = list[list.length - 1];
  return {
    result: {
      thread: {
        external_id: thread.id,
        channel: thread.channel,
        subject: thread.subject,
        contact: contactOf(thread),
        campaign_external_id: null,
        unread: last.direction === "inbound",
        vendor: { category: null, has_ai_draft: false },
      },
      messages: slice.map(({ moment, meeting_intent, ...message }) => Object.assign(message, {
        vendor: { state: "ok", status_code: null, ai_sent_type: null },
      })),
      has_more,
      vendor: { ordering: "oldest_first" },
    },
  };
}

/** A reply into a written thread is taken and goes to nobody, like every send here. */
export function conversationReply(input) {
  const args = input.args;
  const thread = threadById(args.thread.external_id);
  if (thread === null) {
    throw fail("permanent", "conversation_not_found", "The demo provider holds no thread under this identifier. Nothing was sent.");
  }

  if (thread.channel !== args.channel) {
    throw fail("validation", "channel_mismatch", "The thread is on " + thread.channel + ", not on " + args.channel + ". Nothing was sent.");
  }

  const contact = input.contacts[0];
  const pin = contact.external_ids.contact;
  const held = valueOf(thread.person, thread.channel);
  const same = pin ? pin === thread.person.contact_id : held !== null && host.channelKey(thread.channel, held) === contact.channels[0].value;
  if (!same) {
    throw fail("validation", "thread_contact_mismatch", "The thread is not this person's. Nothing was sent.");
  }

  const person = personById(thread.person.contact_id);
  return {
    result: {
      items: [{
        contact_id: contact.id,
        status: "sent",
        thread_external_id: thread.id,
        channel: thread.channel,
        message: { external_id: "demo-msg-" + input.idempotency_key.slice(3), sent_at: iso(now()) },
        external_ids: { contact: person.contact_id },
      }],
    },
    external_ids: { contact: person.contact_id },
  };
}
