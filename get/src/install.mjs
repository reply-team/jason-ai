// The install page as this address serves it: the text read from the one fixed address, its relative links made whole
// against that address. The same rule for every request, so what a link names never changes the text.

import { INSTALL_HEADING, INSTALL_MAX_BYTES, INSTALL_PAGE } from './paths.mjs';

const LINK = /\]\(([^)\s]+)\)/g;
const ABSOLUTE = /^[a-z][a-z0-9+.-]*:/i;

/**
 * Makes each relative Markdown link target whole against the page's own address. Read from this address, a relative
 * link would resolve against it, where nothing answers; read from the fixed address, it resolves to the same file.
 */
export function wholeLinks(text) {
  return text.replace(LINK, (whole, target) =>
    ABSOLUTE.test(target) || target.startsWith('#') ? whole : `](${new URL(target, INSTALL_PAGE).href})`,
  );
}

const NEWLINE = String.fromCharCode(10);
const CRLF = String.fromCharCode(13, 10);

/** Whether a body is the guide: no larger than the bound, and beginning with the guide's own first heading as a line. */
export function isGuide(text) {
  return new TextEncoder().encode(text).length <= INSTALL_MAX_BYTES && (text.startsWith(`${INSTALL_HEADING}${NEWLINE}`) || text.startsWith(`${INSTALL_HEADING}${CRLF}`));
}

/** Reads the guide, or says it could not: an answer that is not 200, or a body that is not the guide, is no guide. */
export async function readInstallPage(fetch) {
  try {
    // Kept at Cloudflare's edge for five minutes, so a burst of installs reads the fixed address once.
    const upstream = await fetch(INSTALL_PAGE, { cf: { cacheTtl: 300, cacheEverything: true } });
    if (upstream.status !== 200) return null;
    const text = await upstream.text();
    return isGuide(text) ? wholeLinks(text) : null;
  } catch {
    return null;
  }
}
