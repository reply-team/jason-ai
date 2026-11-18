// The install page as this address serves it: the guide of the latest published release, read at that release's tag,
// its relative links made whole against the address it was read from; the guide of main where the tag cannot be learnt.
// The same rule for every request, so what a request or a link names never changes the text.

import { INSTALL_HEADING, INSTALL_MAX_BYTES, INSTALL_PAGE, LATEST_RELEASE, LATEST_RELEASE_LOCATION, installPageAt } from './paths.mjs';

const LINK = /\]\(([^)\s]+)\)/g;
const ABSOLUTE = /^[a-z][a-z0-9+.-]*:/i;

// Kept at Cloudflare's edge for five minutes, so a burst of installs reads each fixed address once.
const EDGE = { cacheTtl: 300, cacheEverything: true };

/**
 * Makes each relative Markdown link target whole against the address the page was read from. Read from this address, a
 * relative link would resolve against it, where nothing answers; made whole, it names the same file of the same tree.
 */
export function wholeLinks(text, from = INSTALL_PAGE) {
  return text.replace(LINK, (whole, target) =>
    ABSOLUTE.test(target) || target.startsWith('#') ? whole : `](${new URL(target, from).href})`,
  );
}

const NEWLINE = String.fromCharCode(10);
const CRLF = String.fromCharCode(13, 10);

/** Whether a body is the guide: no larger than the bound, and beginning with the guide's own first heading as a line. */
export function isGuide(text) {
  return new TextEncoder().encode(text).length <= INSTALL_MAX_BYTES && (text.startsWith(`${INSTALL_HEADING}${NEWLINE}`) || text.startsWith(`${INSTALL_HEADING}${CRLF}`));
}

/**
 * The latest published release's tag, from the redirect of its fixed address: read, never followed, and believed only in
 * the one shape of this repository's release page of a release tag. Null where it cannot be learnt or is anything else.
 */
export async function latestTag(fetch) {
  try {
    const answer = await fetch(LATEST_RELEASE, { redirect: 'manual', cf: EDGE });
    if (answer.status < 300 || answer.status > 399) return null;
    const match = LATEST_RELEASE_LOCATION.exec(answer.headers.get('location') ?? '');
    return match ? match[1] : null;
  } catch {
    return null;
  }
}

/** The guide read from one address, its links made whole there; null for an answer that is not 200 or a body that is not the guide. */
async function readGuide(fetch, address) {
  try {
    const upstream = await fetch(address, { cf: EDGE });
    if (upstream.status !== 200) return null;
    const text = await upstream.text();
    return isGuide(text) ? wholeLinks(text, address) : null;
  } catch {
    return null;
  }
}

/**
 * Reads the guide: the latest published release's, at its tag; else main's, where the tag cannot be learnt or the page at
 * it is not the guide. Null where neither read gives the guide.
 */
export async function readInstallPage(fetch) {
  const tag = await latestTag(fetch);
  return (tag === null ? null : await readGuide(fetch, installPageAt(tag))) ?? (await readGuide(fetch, INSTALL_PAGE));
}
