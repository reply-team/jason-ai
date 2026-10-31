// The provider's own object, `args.provider`: the one key of the arguments every operation reserves and none declares,
// which the runtime never reads and hands to this package exactly as written. This version reads one member of it, on
// `message.send` alone: `schedule_id`, the Reply sending schedule a message is timed by (`reply schedule list` names
// them), on email and on LinkedIn alike, written as a string of its digits, since a Reply id written as a JSON number is
// one a reader may round. An object that names another member, or none, or a schedule in another form, is refused before
// anything is called with `provider_input_invalid`, class `validation`, which the runtime reserves for exactly that: the
// message names the shape this package reads and never quotes what it was given, and the pointer names where it went
// wrong. Every other operation ignores the object.
import { fail } from "./errors.js";

// A Reply id as reply-cli takes one for `--schedule-id`: a positive whole number of 32 bits, written without a sign, a
// space or a leading zero.
const ID = /^[1-9][0-9]{0,9}$/;
const LARGEST_ID = 2147483647;

/**
 * The sending schedule `args.provider` names for a message, as written, or null where the work item carries no provider
 * object. Every channel takes one: the format's own command carries it as `--schedule-id`.
 */
export function scheduleOf(operation, input) {
  const args = input.args;
  if (!Object.prototype.hasOwnProperty.call(args, "provider")) {
    return null;
  }

  // The runtime holds the key to an object; this holds it here too.
  const provider = args.provider;
  const names = provider !== null && typeof provider === "object" && !Array.isArray(provider) ? Object.keys(provider) : [];
  if (names.length !== 1 || names[0] !== "schedule_id") {
    fail(operation, { call: null, row: "provider_unreadable", pointer: "/args/provider", expected: "object whose one member is schedule_id" }, undefined);
  }

  const schedule = provider.schedule_id;
  if (typeof schedule !== "string" || !ID.test(schedule) || Number(schedule) > LARGEST_ID) {
    fail(operation, { call: null, row: "provider_schedule_unreadable", pointer: "/args/provider/schedule_id", expected: "string of a Reply id's digits, 1 to 2147483647" }, undefined);
  }

  return schedule;
}
