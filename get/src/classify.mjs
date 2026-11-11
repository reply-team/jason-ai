// What a counted request is counted as. Each value is one of a short fixed set, or a source restricted to lowercase
// letters, digits and hyphens; the User-Agent it is read from is never kept.

const SOURCE = /^[a-z0-9-]{1,40}$/;

/** The link's source: its first `ref`, lowercased, when it is 1 to 40 letters, digits and hyphens. */
export function sourceOf(params) {
  const ref = params.get('ref');
  if (ref === null || ref === '') return 'none';
  const lowered = ref.toLowerCase();
  return SOURCE.test(lowered) ? lowered : 'other';
}

/** A link preview or a crawler, by a word of its own. Read first: some of them also say Mozilla. */
const BOT = /bot|crawl|spider|preview|slurp|facebookexternalhit|embedly|whatsapp|skypeuripreview/i;

/**
 * An agent's or a command line's fetcher, by a word of its own. Read before the browser rule, since agents' fetchers
 * often send a browser-like User-Agent with their own word in it.
 */
const FETCHER = /claude-user|chatgpt-user|claude-code|codex|curl|wget|powershell|python|node|undici|axios|go-http-client|okhttp/i;

/** `bot`, `fetcher` or `browser`; a request with no User-Agent is a fetcher. */
export function clientOf(userAgent) {
  if (!userAgent) return 'fetcher';
  if (BOT.test(userAgent)) return 'bot';
  if (FETCHER.test(userAgent)) return 'fetcher';
  if (userAgent.startsWith('Mozilla/')) return 'browser';
  return 'fetcher';
}

/** The UTC day of a moment, as `YYYY-MM-DD`. */
export function dayOf(moment) {
  return moment.toISOString().slice(0, 10);
}
