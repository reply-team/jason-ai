// What a counted request is counted as. Each value is one of a short fixed set, or a source restricted to lowercase
// letters, digits and hyphens; the User-Agent it is read from is never kept.

const SOURCE = /^[a-z0-9-]{1,40}$/;

/** The rows for no source and for a source that is not one: in parentheses, which a source can never hold. */
export const NO_SOURCE = '(none)';
export const NOT_A_SOURCE = '(other)';

/** The link's source: its first `ref`, lowercased, when it is 1 to 40 letters, digits and hyphens. */
export function sourceOf(params) {
  const ref = params.get('ref');
  if (ref === null || ref === '') return NO_SOURCE;
  const lowered = ref.toLowerCase();
  return SOURCE.test(lowered) ? lowered : NOT_A_SOURCE;
}

/**
 * An agent's own fetcher, acting for a person, by the name its maker gives it. Read first: a maker's published string
 * may also carry a help address with "bot" in it, as ChatGPT-User's does.
 */
const AGENT = /claude-user|chatgpt-user|perplexity-user|claude-code|codex/i;

/** A link preview or a crawler, by a word of its own. Read before the generic fetchers and browsers, which it may resemble. */
const BOT = /bot|crawl|spider|preview|slurp|facebookexternalhit|embedly|whatsapp|skypeuripreview/i;

/**
 * A command line's or a program's HTTP client, by its own word. Read before the browser rule, since some of them send a
 * browser-like User-Agent with their own word in it.
 */
const FETCHER = /curl|wget|powershell|python|node|undici|axios|go-http-client|okhttp/i;

/** `bot`, `fetcher` or `browser`; a request with no User-Agent is a fetcher. */
export function clientOf(userAgent) {
  if (!userAgent) return 'fetcher';
  if (AGENT.test(userAgent)) return 'fetcher';
  if (BOT.test(userAgent)) return 'bot';
  if (FETCHER.test(userAgent)) return 'fetcher';
  if (userAgent.startsWith('Mozilla/')) return 'browser';
  return 'fetcher';
}

/** The UTC day of a moment, as `YYYY-MM-DD`. */
export function dayOf(moment) {
  return moment.toISOString().slice(0, 10);
}
