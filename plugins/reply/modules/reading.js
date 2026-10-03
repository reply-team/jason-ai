// What the four reads share: naming the sequence a campaign is, and turning Reply's values into the bounded,
// neutral ones the documents publish. Nothing here calls Reply.
import { fail } from "./errors.js";

// Reply identifies a sequence, a contact and a thread with a positive 32-bit integer, so this is what an
// identifier has to look like before there is anything worth asking about.
const NUMBER = /^[1-9][0-9]{0,9}$/;
const LARGEST_NUMBER = 2147483647;

// Whether a value names something Reply could hold at all.
export function isReplyNumber(value) {
  return typeof value === "string" && NUMBER.test(value) && Number(value) <= LARGEST_NUMBER;
}

// Which sequence a read is about. A pin is worked by whenever there is one — that is what a link is for — and the
// identifier a planner supplied is what makes the first one. A value that is not a sequence number names nothing
// this account could hold, so nothing is asked: the operation's own `campaign_not_found` is the answer.
export function sequenceOf(input, operation) {
  const pinned = input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign;
  const supplied = input.args && input.args.campaign && input.args.campaign.external_id;
  const named = pinned || supplied;

  if (!isReplyNumber(named)) {
    fail(operation, { call: null, row: "campaign_not_found" }, undefined);
  }

  return named;
}

// A string cut to `max` characters, counted as the documents count them: by code point, so an emoji is one
// character though JavaScript holds it as two UTF-16 units. A cut by units would cut seven thousand emoji at six
// thousand, inside a bound of twelve thousand, and a cut in the middle of a pair would leave half a character —
// which is not text at all.
export function cut(text, max) {
  if (text.length <= max) {
    return text;
  }

  let units = 0;
  for (let characters = 0; characters < max && units < text.length; characters++) {
    const unit = text.charCodeAt(units);
    const next = units + 1 < text.length ? text.charCodeAt(units + 1) : 0;
    units += unit >= 0xD800 && unit <= 0xDBFF && next >= 0xDC00 && next <= 0xDFFF ? 2 : 1;
  }

  return text.slice(0, units);
}

// The neutral word a table gives a provider word, or undefined when it gives none. Looked up as the table's own
// property, because a provider word can be anything — `constructor` or `__proto__` would otherwise find what every
// JavaScript object inherits.
export function lookup(table, key) {
  return typeof key === "string" && Object.prototype.hasOwnProperty.call(table, key) ? table[key] : undefined;
}

// A string the provider may or may not have sent, cut to its bound, and null when it sent none — an empty string
// included, because "" is a claim the provider did not make.
export function text(value, max) {
  return typeof value === "string" && value.length > 0 ? cut(value, max) : null;
}

// The provider's own word for something, kept as a word: a string cut to its bound, or a number written out.
export function word(value, max) {
  if (typeof value === "string") {
    return value.length > 0 ? cut(value, max) : null;
  }

  return typeof value === "number" && isFinite(value) ? cut(String(value), max) : null;
}

// A moment as UTC, whatever offset the provider wrote it with; null when it wrote none or one that is not a date.
export function utc(value) {
  if (typeof value !== "string" || value.length === 0) {
    return null;
  }

  const moment = new Date(value);
  return isNaN(moment.getTime()) ? null : moment.toISOString();
}

// A boolean the provider sent, or null when it sent something else.
export function flag(value) {
  return value === true || value === false ? value : null;
}

// An identifier as the documents carry one: the provider's number written out, or null where it sent none.
export function identifier(value) {
  if (typeof value === "number" && isFinite(value) && value > 0) {
    return String(value);
  }

  return typeof value === "string" && value.length > 0 ? cut(value, 256) : null;
}
