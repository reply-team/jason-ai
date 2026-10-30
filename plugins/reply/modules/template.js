// A message composed as a template, written in Reply's own syntax. The template is read by the template standard the
// contracts README publishes ("The template standard"), the same reading the document holds it to, in one pass from its
// start: a construct the standard refuses is refused by the standard's own name for it and the character it starts on.
// What the standard accepts is written as Reply writes it, name by name for the channel it goes on, and Reply fills it in
// for the person when it sends. Nothing here calls Reply.
import { fail } from "./errors.js";

// Each name the standard lists, as Reply names it in an email. A custom field goes by its key alone.
const NAMES = {
  "contact.first_name": "FirstName",
  "contact.last_name": "LastName",
  "contact.company": "Company",
  "contact.title": "Title",
  "contact.email": "Email",
  "sender.first_name": "SendingEmail.SenderFirstName",
  "sender.last_name": "SendingEmail.SenderLastName",
  "sender.name": "SendingEmail.SenderName",
  "sender.address": "SendingEmail.Email",
};

// Each name as Reply names it in a LinkedIn text, null for one Reply holds no variable for there: Reply's LinkedIn
// sending account carries a first name and a name, and neither a last name nor an address.
const LINKEDIN = Object.assign({}, NAMES, {
  "sender.first_name": "SendingLinkedIn.SenderFirstName",
  "sender.last_name": null,
  "sender.name": "SendingLinkedIn.SenderName",
  "sender.address": null,
});

const CHANNELS = { "email": NAMES, "linkedin": LINKEDIN };

// The names Reply holds no variable for on a channel whose value the arguments hold, written in its place as the text
// the arguments hold: on LinkedIn the sending account's address, its profile. The address is the one Reply's list of
// accounts holds, checked before the send (send.js).
const WRITTEN = { "email": [], "linkedin": ["sender.address"] };

const CUSTOM = "contact.custom.";
const KEY = /^[A-Za-z_][A-Za-z0-9_]{0,63}$/;

// The keys Reply looks a value up under before a custom field's, or reads as something other than a field, compared
// as Reply compares them, exactly: the contact's own fields and its engagement counts, the roots of the account's
// people and mailboxes, the dates and flags Reply computes, its own helpers and the words its template engine reads, `null`
// among them, which it reads as no value rather than a field. A custom field under one of them would be written as
// Reply's value, or not as a value at all.
const KEPT = new Set([
  "FirstName", "LastName", "FullName", "Phone", "Title", "Company", "LinkedIn", "SalesNavigator", "CompanySize",
  "Industry", "City", "State", "Country", "Email", "Domain", "Id", "TimeZoneId", "LastTouch",
  "Opens", "Replies", "Views", "last_touch",
  "Owner", "Sender", "ContactOwner", "ReplyAccount", "SendingEmail", "SendingLinkedIn",
  "today", "tomorrow", "day_of_week", "time_of_the_day", "now_date", "now_month", "now_quarter", "now_year",
  "is_monday", "is_tuesday", "is_wednesday", "is_thursday", "is_friday", "is_saturday", "is_sunday", "is_weekday",
  "is_weekend", "days_from_now", "days_before_now", "weekdays_from_now", "weekdays_before_now", "month_from_now",
  "quarter_from_now", "datetime_now", "Random", "toLower",
  "_sif", "_if", "_f", "_Random",
  "else", "this", "if", "unless", "each", "with", "lookup", "log", "null",
]);

// What the standard calls each construct it refuses, by the key its table gives the row.
const REFUSED = {
  "raw": "a raw value",
  "comment": "a comment",
  "partial": "a partial",
  "block": "a block other than `#if`",
  "elseif": "an `else if`",
  "helper": "a helper or a macro",
  "prompt": "a prompt to a model",
  "comparison": "a comparison other than `==` and `!=`",
  "logic": "a logical operator",
  "variable": "a name where a quoted text belongs",
  "name": "a name this standard does not list",
  "quoted": "a quoted text not closed",
  "spacing": "a tag spaced otherwise than the standard writes it",
  "unknown": "a tag this standard does not know",
  "unbalanced": "an unbalanced condition",
  "depth": "a condition nested deeper than eight",
  "unclosed": "a tag never closed",
  "stray": "a stray closing brace",
  "escaped": "an escaped brace",
};

const DEPTH = 8;
const PROMPT = "@prompt[[";

// White space as the runtime reads it in a template.
const WHITE = /[\t-\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]/;

// The characters Reply's comparison reads in the text it compares: a letter or a mark that combines with one, a digit,
// `_` or other joining punctuation, white space, and this punctuation. Reply leaves a comparison holding any other one
// to its template engine, which reads it otherwise.
const COMPARED = /[\p{L}\p{Mn}\p{Nd}\p{Pc}$@"`~!#%^&*()+,.\/:;<=\-]/u;

// What follows a `<` that an HTML reader takes as the start of a tag: an ASCII letter, `/`, `!` or `?`. Reply decodes the
// whole message before it reads it and drops a tag it does not know with everything after it, so a `<` the text holds,
// though it reaches Reply as `&lt;`, is read there as that start all the same.
const TAG_START = /^[A-Za-z\/!?]$/;

const WORD = "word";
const OPERATOR = "operator";
const QUOTED = "quoted";

/**
 * The subject or the body, `part`, of a message on `channel`, `email` or `linkedin`, as Reply reads a template: each run
 * of text and each fallback passed through `literal`, which writes text as the part carries it, and every tag in Reply's
 * syntax, each name as Reply names it on that channel. A name Reply holds no variable for whose value the arguments hold,
 * `held` by name, is written as that value, or its fallback where the value is none, and a condition on it as the branch
 * it takes, as the example a person approves renders them. Refuses, before anything is called, a template the standard
 * refuses, such a name with no value and no fallback, a name Reply holds no field for on the channel, a custom field
 * under a key Reply keeps, a comparison with a
 * character Reply's comparison does not read, a template whose text and fallbacks spell a prompt to a model once Reply
 * fills it in, and, in an email, a `<` in its text, a fallback or a comparison that starts a tag, or that stands right
 * before a value; a comparison's text is held because whether Reply sanitizes a template before or after it fills it in
 * is not known. A LinkedIn text is written as written, as a final one is, and Reply converts it from HTML to text after
 * filling it in.
 */
export function inReplySyntax(operation, channel, part, template, literal, held) {
  const names = CHANNELS[channel];
  const read = parse(template);
  if (read.refusal !== null) {
    refuse(operation, "template_refused", "The " + part + " writes " + REFUSED[read.refusal.key] + " at character " + read.refusal.position + ", which the template standard refuses.");
  }

  const nodes = withHeld(operation, part, read.nodes, WRITTEN[channel], held || {});
  const lacked = lackedName(nodes, names);
  if (lacked !== null) {
    refuse(operation, "template_names_a_field_reply_lacks", "The " + part + " names `" + lacked.name + "` at character " + lacked.at + ", which Reply holds no field for on " + (channel === "email" ? "email" : "LinkedIn") + ".");
  }

  const kept = keptKey(nodes);
  if (kept !== null) {
    refuse(operation, "template_names_a_key_reply_keeps", "The " + part + " names a custom field at character " + kept + " under a key Reply keeps for a value of its own, which Reply would write instead of the field.");
  }

  const unread = unreadIn(nodes);
  if (unread !== null) {
    refuse(operation, "template_compares_what_reply_cannot_read", "The " + part + " compares a value with a text holding, at character " + unread + ", a character Reply's comparison does not read.");
  }

  const spelled = promptIn(nodes, [0]).at;
  if (spelled !== null) {
    refuse(operation, "template_spells_a_prompt", "The " + part + " spells `@prompt[[` once Reply fills it in, complete at character " + spelled + ", which Reply would read as a prompt to a model.");
  }

  if (channel === "email") {
    refuseTag(operation, "template_tag_in_the_text", part, tagIn(nodes, []).at);
  }

  return written(nodes, literal, names);
}

/**
 * Refuses an email's subject or body, `part`, written as final text, that holds a `<` Reply reads as markup.
 * Nothing is called.
 */
export function refuseTagStart(operation, part, text) {
  refuseTag(operation, "tag_in_the_text", part, typeof text === "string" ? tagIn([{ kind: "text", text: text, at: 1 }], []).at : null);
}

// A final text's refusal is `message_invalid` and a template's `template_unsupported`, so the row is the caller's.
function refuseTag(operation, row, part, at) {
  if (at !== null) {
    refuse(operation, row, "The " + part + " holds, at character " + at + ", a `<` Reply reads as markup, and Reply drops a tag it does not know with everything after it.");
  }
}

function refuse(operation, row, reason) {
  fail(operation, { call: null, row: row, reason: reason }, undefined);
}

// The nodes in Reply's syntax, each name as `names` writes it.
function written(nodes, literal, names) {
  let out = "";
  for (const node of nodes) {
    if (node.kind === "text") {
      out += literal(node.text);
    } else if (node.kind === "value") {
      out += "{{" + replyName(node.name, names) + (node.fallback === null ? "" : " | '" + literal(node.fallback) + "'") + "}}";
    } else {
      // Reply decodes the whole message before it reads it, so the text a comparison compares is written as the rest is.
      out += "{{#if " + replyName(node.name, names) + (node.operator === null ? "" : " " + node.operator + " '" + literal(node.text) + "'") + "}}";
      out += written(node.then, literal, names);
      out += node.otherwise === null ? "" : "{{else}}" + written(node.otherwise, literal, names);
      out += "{{/if}}";
    }
  }

  return out;
}

function replyName(name, names) {
  return name.indexOf(CUSTOM) === 0 ? name.slice(CUSTOM.length) : names[name];
}

// The nodes with each name of `from` written as text: its value in `held`, else its fallback, and a condition on it as
// the branch it takes, compared as the example compares, a value of nothing but white space being none. The text is
// then read as the rest of the template is, for a prompt among the rest.
function withHeld(operation, part, nodes, from, held) {
  const out = [];
  for (const node of nodes) {
    if (node.name === undefined || from.indexOf(node.name) < 0) {
      out.push(node.kind !== "if" ? node : Object.assign({}, node, {
        then: withHeld(operation, part, node.then, from, held),
        otherwise: node.otherwise === null ? null : withHeld(operation, part, node.otherwise, from, held),
      }));
    } else if (node.kind === "value") {
      const value = valued(held[node.name]) ? held[node.name] : node.fallback;
      if (value === null) {
        refuse(operation, "template_names_a_field_reply_lacks", "The " + part + " names `" + node.name + "` at character " + node.at + " with no fallback: Reply has no variable for the sending account's address on LinkedIn, and the arguments hold none.");
      }

      out.push({ kind: "text", text: value, at: node.at });
    } else {
      const value = valued(held[node.name]) ? held[node.name] : "";
      const holds = node.operator === null ? valued(held[node.name]) : (value === node.text) === (node.operator === "==");
      out.push.apply(out, withHeld(operation, part, holds ? node.then : node.otherwise || [], from, held));
    }
  }

  return out;
}

// Whether a value the arguments hold is one: a string with a character that is not white space.
function valued(value) {
  return typeof value === "string" && Array.from(value).some(character => !WHITE.test(character));
}

// The first name `names` holds no field for, with the character it starts on, or null.
function lackedName(nodes, names) {
  for (const node of nodes) {
    if (node.name !== undefined && node.name.indexOf(CUSTOM) !== 0 && names[node.name] === null) {
      return { name: node.name, at: node.at };
    }

    const inside = node.kind === "if" ? lackedName(node.then.concat(node.otherwise || []), names) : null;
    if (inside !== null) {
      return inside;
    }
  }

  return null;
}

// Where the first custom field under a key Reply keeps starts, or null.
function keptKey(nodes) {
  for (const node of nodes) {
    if (node.name !== undefined && node.name.indexOf(CUSTOM) === 0 && KEPT.has(node.name.slice(CUSTOM.length))) {
      return node.at;
    }

    const inside = node.kind === "if" ? keptKey(node.then.concat(node.otherwise || [])) : null;
    if (inside !== null) {
      return inside;
    }
  }

  return null;
}

// Where the first character a comparison holds that Reply's comparison does not read is, or null.
function unreadIn(nodes) {
  for (const node of nodes) {
    if (node.kind !== "if") {
      continue;
    }

    if (node.operator !== null) {
      for (let index = 0; index < node.text.length; index++) {
        if (!COMPARED.test(node.text[index]) && !WHITE.test(node.text[index])) {
          return node.textAt + index;
        }
      }
    }

    const inside = unreadIn(node.then.concat(node.otherwise || []));
    if (inside !== null) {
      return inside;
    }
  }

  return null;
}

// How far into `@prompt[[ the text so far may have spelled, along each way through the conditions: `states` holds every
// length of the prompt's start the text written so far may end with. Text and fallbacks are in hand; a value is not, so a
// value starts the spelling again, and a value with a fallback may be the fallback. Answers the states after `nodes`,
// and `at`, the character the first complete spelling ends on, or null.
function promptIn(nodes, states) {
  let now = states;
  for (const node of nodes) {
    let next;
    if (node.kind === "text") {
      next = stepped(now, node.text, node.at);
    } else if (node.kind === "value") {
      next = node.fallback === null ? { states: [0], at: null } : merged({ states: [0], at: null }, stepped(now, node.fallback, node.fallbackAt));
    } else {
      const then = promptIn(node.then, now);
      next = merged(then, node.otherwise === null ? { states: now, at: null } : promptIn(node.otherwise, now));
    }

    if (next.at !== null) {
      return next;
    }

    now = next.states;
  }

  return { states: now, at: null };
}

// Each state carried through `text`, which starts at character `from`.
function stepped(states, text, from) {
  const after = [];
  let at = null;
  for (const start of states) {
    let state = start;
    for (let index = 0; index < text.length; index++) {
      state = text[index] === PROMPT[state] ? state + 1 : text[index] === PROMPT[0] ? 1 : 0;
      if (state === PROMPT.length) {
        at = at === null ? from + index : Math.min(at, from + index);
        break;
      }
    }

    if (state < PROMPT.length && after.indexOf(state) < 0) {
      after.push(state);
    }
  }

  return { states: after, at: at };
}

function merged(one, other) {
  const at = one.at === null ? other.at : other.at === null ? one.at : Math.min(one.at, other.at);
  return { states: one.states.concat(other.states.filter(state => one.states.indexOf(state) < 0)), at: at };
}

// Where the first `<` an HTML reader takes as the start of a tag stands, along each way through the conditions, or null:
// `open` holds the characters of every `<` the text written so far may end with, and so may start a tag with what comes
// next. Text and fallbacks are in hand, and so is the text a comparison compares, which is read on its own, since
// whether Reply sanitizes before or after it fills a template in is not known; a value is not in hand, and may start
// with a letter, so a `<` right before one is a tag's start. Answers the `<`s the nodes may end with, and `at`.
function tagIn(nodes, open) {
  let now = open;
  for (const node of nodes) {
    let next;
    if (node.kind === "text") {
      next = tagged(now, node.text, node.at);
    } else if (node.kind === "value") {
      next = now.length > 0 ? { open: [], at: Math.min.apply(null, now) } : node.fallback === null ? { open: [], at: null } : tagged([], node.fallback, node.fallbackAt);
    } else {
      // A comparison is read before its branches: with an open `<` before it as well, the comparison's `<` is named.
      const compared = node.operator === null ? null : tagged([], node.text, node.textAt).at;
      next = compared !== null ? { open: [], at: compared } : joined(tagIn(node.then, now), node.otherwise === null ? { open: now, at: null } : tagIn(node.otherwise, now));
    }

    if (next.at !== null) {
      return next;
    }

    now = next.open;
  }

  return { open: now, at: null };
}

// `text`, which starts at character `from`, after the `<`s in `open`.
function tagged(open, text, from) {
  if (open.length > 0 && text.length > 0 && TAG_START.test(text[0])) {
    return { open: [], at: Math.min.apply(null, open) };
  }

  // A `</` that ends the text is refused too, though an HTML reader makes it text: a refusal too many, accepted.
  for (let index = 0; index < text.length - 1; index++) {
    if (text[index] === "<" && TAG_START.test(text[index + 1])) {
      return { open: [], at: from + index };
    }
  }

  return { open: text.length === 0 ? open : text[text.length - 1] === "<" ? [from + text.length - 1] : [], at: null };
}

function joined(one, other) {
  const at = one.at === null ? other.at : other.at === null ? one.at : Math.min(one.at, other.at);
  return { open: one.open.concat(other.open.filter(character => one.open.indexOf(character) < 0)), at: at };
}

/**
 * The template as what it is made of — text, values and conditions, each with the character it starts on — and the
 * first construct the standard refuses in it reading from the start, `{ key, position }`, or null.
 */
export function parse(template) {
  const root = [];
  const open = [];
  let text = "";
  let textAt = 0;
  let index = 0;
  let refusal = null;
  const flush = () => {
    if (text.length > 0) {
      current(root, open).push({ kind: "text", text: text, at: textAt });
      text = "";
    }
  };

  while (index < template.length && refusal === null) {
    if (template.startsWith("\\{{", index)) {
      refusal = refused("escaped", index);
    } else if (template.startsWith("}}", index)) {
      refusal = refused("stray", index);
    } else if (template.startsWith(PROMPT, index)) {
      refusal = refused("prompt", index);
    } else if (template.startsWith("{{", index)) {
      flush();
      const tag = readTag(template, index, root, open);
      refusal = tag.refusal;
      index = tag.index;
    } else {
      if (text.length === 0) {
        textAt = index + 1;
      }

      text += template[index];
      index++;
    }
  }

  if (refusal === null) {
    flush();
    if (open.length > 0) {
      refusal = refused("unbalanced", open[0].at - 1);
    }
  }

  return refusal === null ? { nodes: root, refusal: null } : { nodes: [], refusal: refusal };
}

// One tag, from its `{{` to its `}}`: what it refuses, if anything, and where reading goes on.
function readTag(template, start, root, open) {
  const end = template.indexOf("}}", start + 2);
  if (end < 0) {
    return { refusal: refused("unclosed", start), index: start };
  }

  const content = template.slice(start + 2, end);
  const first = content[0];
  const early = first === "{" ? "raw" : content.indexOf("{{") >= 0 ? "unclosed" : first === "!" ? "comment" : first === ">" ? "partial" : null;
  if (early !== null) {
    return { refusal: refused(early, start), index: start };
  }

  const read = tokens(content, start + 2);
  if (read.problem !== null) {
    return { refusal: read.problem, index: start };
  }

  const placed = place(read.tokens, read.canonical, start, root, open);
  const index = end + 2;
  return { refusal: placed === null && template[index] === "}" ? refused("stray", index) : placed, index: index };
}

// What a tag says, once its parts are read: a condition opened, a branch, a close, or a value.
function place(parts, canonical, start, root, open) {
  if (parts.length === 0 || parts[0].kind !== WORD) {
    return refused("unknown", start);
  }

  const word = parts[0].text;
  if (word === "#if") {
    if (parts.length === 1) {
      return refused("unknown", start);
    }

    const condition = expression(parts, 1, true, start);
    const problem = condition.problem || settled(canonical, condition.name, start);
    if (problem !== null) {
      return problem;
    }

    if (open.length === DEPTH) {
      return refused("depth", start);
    }

    const node = { kind: "if", name: condition.name, operator: condition.operator, text: condition.text, textAt: condition.textAt, then: [], otherwise: null, at: start + 1 };
    current(root, open).push(node);
    open.push(node);
    return null;
  }

  if (word[0] === "#" || word[0] === "/") {
    if (word === "#" || word === "/" || (word === "/if" && parts.length > 1)) {
      return refused("unknown", start);
    }

    if (word !== "/if") {
      return refused("block", start);
    }

    if (!canonical) {
      return refused("spacing", start);
    }

    if (open.length === 0) {
      return refused("unbalanced", start);
    }

    open.pop();
    return null;
  }

  if (word === "else") {
    if (parts.length > 1) {
      return refused(parts[1].kind === WORD && parts[1].text === "if" ? "elseif" : "unknown", start);
    }

    if (!canonical) {
      return refused("spacing", start);
    }

    if (open.length === 0 || open[open.length - 1].otherwise !== null) {
      return refused("unbalanced", start);
    }

    open[open.length - 1].otherwise = [];
    return null;
  }

  const value = expression(parts, 0, false, start);
  const problem = value.problem || settled(canonical, value.name, start);
  if (problem !== null) {
    return problem;
  }

  current(root, open).push({ kind: "value", name: value.name, fallback: value.text, fallbackAt: value.textAt, at: start + 1 });
  return null;
}

// A name, alone or with what follows it: a fallback outside a condition, a comparison inside one.
function expression(parts, at, inCondition, start) {
  const none = { problem: null, name: null, operator: null, text: null, textAt: null };
  if (parts[at].kind !== WORD) {
    return Object.assign(none, { problem: refused("unknown", start) });
  }

  const name = parts[at].text;
  if (parts.length === at + 1) {
    return Object.assign(none, { name: name });
  }

  const second = parts[at + 1];
  let key = second.kind === WORD ? "helper"
    : second.kind === QUOTED ? "unknown"
    : second.text === "&&" || second.text === "||" ? "logic"
    : ["=", "<>", "<", ">", "<=", ">="].indexOf(second.text) >= 0 ? "comparison"
    : second.text === "|" && !inCondition ? null
    : (second.text === "==" || second.text === "!=") && inCondition ? null
    : "unknown";
  if (key === null) {
    const third = parts[at + 2];
    const fourth = parts[at + 3];
    key = third === undefined ? "unknown"
      : third.kind === WORD ? "variable"
      : third.kind === OPERATOR ? "unknown"
      : fourth === undefined ? null
      : fourth.kind === OPERATOR && (fourth.text === "&&" || fourth.text === "||") ? "logic"
      : fourth.kind === OPERATOR && fourth.text === "|" && !inCondition ? "helper"
      : "unknown";
  }

  if (key !== null) {
    return Object.assign(none, { problem: refused(key, start) });
  }

  return { problem: null, name: name, operator: inCondition ? second.text : null, text: parts[at + 2].text, textAt: parts[at + 2].at };
}

// A tag whose shape stands: refused still when it is spaced otherwise, or names what the standard does not list.
function settled(canonical, name, start) {
  return !canonical ? refused("spacing", start) : !isName(name) ? refused("name", start) : null;
}

function isName(name) {
  return name.indexOf(CUSTOM) === 0 ? KEY.test(name.slice(CUSTOM.length)) : Object.prototype.hasOwnProperty.call(NAMES, name);
}

// A tag's parts: words, operators and quoted texts, each quoted text with the character it starts on; whether the tag is
// spaced as the standard writes it; and a quoted text not closed, or one holding a prompt, as the problem.
function tokens(content, offset) {
  const parts = [];
  let canonical = content.length > 0 && !WHITE.test(content[0]) && !WHITE.test(content[content.length - 1]);
  let separated = true;
  let index = 0;
  while (index < content.length) {
    const character = content[index];
    if (WHITE.test(character)) {
      const run = index;
      while (index < content.length && WHITE.test(content[index])) {
        index++;
      }

      canonical = canonical && index - run === 1 && character === " ";
      separated = true;
      continue;
    }

    canonical = canonical && (parts.length === 0 || separated);
    separated = false;
    const from = index;
    if (character === "'") {
      const close = content.indexOf("'", index + 1);
      if (close < 0 || (close + 1 < content.length && !WHITE.test(content[close + 1]))) {
        return { tokens: parts, canonical: canonical, problem: refused("quoted", offset + index) };
      }

      const quoted = content.slice(index + 1, close);
      const prompt = quoted.indexOf(PROMPT);
      if (prompt >= 0) {
        return { tokens: parts, canonical: canonical, problem: refused("prompt", offset + index + 1 + prompt) };
      }

      parts.push({ kind: QUOTED, text: quoted, at: offset + index + 2 });
      index = close + 1;
    } else if (isOperator(character)) {
      while (index < content.length && isOperator(content[index])) {
        index++;
      }

      parts.push({ kind: OPERATOR, text: content.slice(from, index) });
    } else {
      while (index < content.length && !WHITE.test(content[index]) && content[index] !== "'" && !isOperator(content[index])) {
        index++;
      }

      parts.push({ kind: WORD, text: content.slice(from, index) });
    }
  }

  return { tokens: parts, canonical: canonical, problem: null };
}

function isOperator(character) {
  return "|=!<>&".indexOf(character) >= 0;
}

function current(root, open) {
  if (open.length === 0) {
    return root;
  }

  const innermost = open[open.length - 1];
  return innermost.otherwise === null ? innermost.then : innermost.otherwise;
}

// A refusal: the row's key, and the character the construct starts on, from 1.
function refused(key, index) {
  return { key: key, position: index + 1 };
}
