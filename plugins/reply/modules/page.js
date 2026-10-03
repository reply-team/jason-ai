// A page of a read, cut short before it outgrows what the runtime accepts as one result.
//
// A page is bounded twice. `limit` is the caller's bound, and the provider honours it. The other is bytes: the
// runtime refuses a result above 1 MiB, measured as System.Text.Json's default encoder writes it — six bytes for
// DEL, for every character outside ASCII and for each of < > & ' " + and the backtick, twelve for a character
// outside the Basic Multilingual Plane. A page of the largest items a schema allows can pass that, so no `limit`
// alone can promise a page that fits. This module stops adding items at a budget a quarter below the cap and lets
// the page say `has_more`; the caller then asks for the next page at `offset` plus the items it got.

// Where a page stops: 768 KiB, a quarter below the runtime's cap on a result. The quarter is room for this
// module's own count being coarser than the runtime's — it counts every structural quote as six bytes, so it only
// ever errs towards a shorter page.
export const PAGE_BUDGET = 786432;

// What one character costs in the runtime's measure, counted over the JSON this value would be written as. A
// surrogate pair is one character outside the Basic Multilingual Plane, written as two \u escapes. Everything the
// runtime escapes costs six — DEL included, which JavaScript's JSON leaves as itself and the runtime does not — and
// so does every quote, structural ones included, which over-counts on purpose.
const SIX = "<>&'\"+`";

export function weigh(value) {
  const text = JSON.stringify(value);
  let bytes = 0;
  for (let at = 0; at < text.length; at++) {
    const unit = text.charCodeAt(at);
    if (unit >= 0xD800 && unit <= 0xDBFF && at + 1 < text.length) {
      const next = text.charCodeAt(at + 1);
      if (next >= 0xDC00 && next <= 0xDFFF) {
        bytes += 12;
        at++;
        continue;
      }
    }

    bytes += unit >= 0x7F || SIX.indexOf(text[at]) >= 0 ? 6 : 1;
  }

  return bytes;
}

// The items of a page that fit beside what the rest of the result already weighs, in order, and whether any were
// left out. One item is always taken: the schema keeps any single item far below the budget, and a page of none
// with more to follow would ask for the same page again forever.
export function fill(items, rest) {
  const taken = [];
  let used = weigh(rest);

  for (const item of items) {
    const weight = weigh(item) + 1;
    if (taken.length > 0 && used + weight > PAGE_BUDGET) {
      break;
    }

    taken.push(item);
    used += weight;
  }

  return { items: taken, cut: taken.length < items.length };
}
