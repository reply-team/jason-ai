/** Pure: what the desktop reads of the run file the runtime writes once it listens (`docs/runtime-api.md`). */

/**
 * The runtime's origin, `http://127.0.0.1:<port>`, or null when the text is not a run file or names anything but the
 * loopback address. The run file also carries the runtime's token; the desktop never reads it, since the page pairs
 * through the link `jason ui open` prints and every other call goes through the CLI.
 */
export function runtimeOrigin(text: string): string | null {
    let raw: unknown;
    try {
        raw = JSON.parse(text);
    } catch {
        return null;
    }
    if (typeof raw !== 'object' || raw === null) return null;
    const baseUrl = (raw as { base_url?: unknown }).base_url;
    return typeof baseUrl === 'string' ? loopbackOrigin(baseUrl) : null;
}

/** The origin of an address on the loopback address over http, or null for anything else. */
export function loopbackOrigin(address: string): string | null {
    let url: URL;
    try {
        url = new URL(address);
    } catch {
        return null;
    }
    // The runtime listens on the loopback address only; a run file naming anything else is not one to follow.
    if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || url.username !== '' || url.password !== '') return null;
    return url.origin;
}

/**
 * The link to load from what `jason ui open --no-open` printed: its `url`, when it is a page of the runtime's own UI at
 * the runtime's origin, and null otherwise.
 */
export function pairingLink(stdout: string, origin: string): string | null {
    let raw: unknown;
    try {
        raw = JSON.parse(stdout);
    } catch {
        return null;
    }
    if (typeof raw !== 'object' || raw === null) return null;
    const url = (raw as { url?: unknown }).url;
    return typeof url === 'string' && isOwnPage(url, origin) ? url : null;
}

/** Whether an address is a page of the runtime's own UI: at the runtime's origin, under `/ui/`. */
export function isOwnPage(url: string, origin: string): boolean {
    return loopbackOrigin(url) === origin && new URL(url).pathname.startsWith('/ui/');
}
