// Lookups over the sample story: its people, their written threads and its two sending accounts. The story itself is
// data alone, in story.js; everything that reads it goes through here.
import { story } from "./story.js";

export const THREAD_PREFIX = "demo-thread-";
const CREATED_PREFIX = "demo-contact-x";

export function senders() {
  return story.senders;
}

export function personById(contactId) {
  return story.people.find((person) => person.contact_id === contactId) || null;
}

/** The story's person who holds the value on the channel, compared as the runtime keys values. */
export function personByValue(channel, value) {
  return story.people.find((person) => person.channels.some(
    (held) => held.channel === channel && host.channelKey(channel, held.value) === value)) || null;
}

/** Whether an identifier is one this package gives: a story person's, or one made for somebody else. */
export function isOwnContact(contactId) {
  return personById(contactId) !== null || /^demo-contact-x[0-9a-f]{8}$/.test(contactId);
}

/** The identifier for a person the story does not hold, the same for the same value every time. */
export function createdContact(channel, value) {
  let hash = 0x811c9dc5;
  const text = channel + ":" + value;
  for (let i = 0; i < text.length; i += 1) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return CREATED_PREFIX + ("0000000" + (hash >>> 0).toString(16)).slice(-8);
}

export function fullName(person) {
  return person.first_name + " " + person.last_name;
}

export function valueOf(person, channel) {
  const held = person.channels.find((candidate) => candidate.channel === channel);
  return held ? held.value : null;
}

/**
 * Every written thread: one for each send the story says was approved, holding that message and the replies written
 * to it, each reply at its offset after the story's start.
 */
export function threads() {
  const all = [];
  for (const person of story.people) {
    for (const send of person.sends) {
      if (send.approval !== "given") {
        continue;
      }

      all.push({
        id: THREAD_PREFIX + send.key,
        person,
        send,
        channel: send.channel,
        subject: send.subject,
        replies: person.replies.filter((reply) => reply.send === send.key),
      });
    }
  }

  return all;
}

export function threadById(id) {
  return threads().find((thread) => thread.id === id) || null;
}
