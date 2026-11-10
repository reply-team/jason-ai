/** Pure: the one permission the window grants the page it shows. */

/** What the page may be granted: writing to the clipboard, so the Attention screen's Copy works, and nothing else. */
const granted = new Set(['clipboard-sanitized-write']);

function originOf(address: string): string | null {
    try {
        const url = new URL(address);
        return url.protocol === 'http:' ? url.origin : null;
    } catch {
        return null;
    }
}

/**
 * Whether a permission asked for by a page at `requesting` (a URL or an origin) is granted: only one named above, and
 * only when the page is at the runtime's origin. With no runtime origin known, nothing is.
 */
export function permissionAllowed(permission: string, requesting: string, runtime: string | null): boolean {
    return runtime !== null && granted.has(permission) && originOf(requesting) === runtime;
}
