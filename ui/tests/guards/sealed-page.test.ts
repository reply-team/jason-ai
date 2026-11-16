import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { sealedPage } from '../../scripts/package-lib.mjs';

// The page under sealed/ is served unable to act, but a browser may look a host name up before any policy speaks: from
// an address in the markup it is about to parse, or from a hint that asks it to. No request log shows such a lookup, so
// the file itself is read: it names no address with a host in it, and asks for no lookup or connection ahead. The three
// XML namespaces a page's SVG and markup are written in are names, not places, and pass by their exact text alone. A
// host written as plain text, `example.com/path` say, is not an address a browser acts on, and passes.

/** The namespaces of SVG, XLink and XHTML, matched exactly: a browser reads them as names and never fetches them. */
const namespaces = ['http://www.w3.org/2000/svg', 'http://www.w3.org/1999/xlink', 'http://www.w3.org/1999/xhtml'];

/** Every way the text names a place a browser may look up, as the address it names, one line each. */
function addresses(text: string): string[] {
    const found: string[] = [];
    // An address with an authority, of any scheme: `https://host`, `ws://host`, `ftp://host`, `file://host`.
    for (const match of text.matchAll(/[a-z][a-z0-9+.-]*:\/\/[^\s"'`<>()\\]*/gi)) {
        const address = match[0];
        if (namespaces.includes(address)) continue;
        found.push(address.replace(/^([^/]*\/\/[^/?#]*).*$/, '$1'));
    }
    // An address with no scheme of its own, `//host`, where markup or a style takes an address.
    for (const match of text.matchAll(
        /\b(?:src|href|action|formaction|poster|ping|srcset|data|background|cite|manifest|xlink:href)\s*=\s*["'`]?\s*(\/\/[^\s"'`<>/]*)/gi,
    ))
        found.push(match[1]!);
    for (const match of text.matchAll(/(?:url\(|@import)\s*["'`]?\s*(\/\/[^\s"'`<>)/]*)/gi)) found.push(match[1]!);
    // A hint that asks the browser to look a name up, or connect, or fetch, ahead of the page's need.
    for (const match of text.matchAll(
        /\brel\s*=\s*["'`]?\s*(dns-prefetch|preconnect|prefetch|preload|prerender|modulepreload)\b/gi,
    ))
        found.push(`rel=${match[1]!.toLowerCase()}`);
    return found;
}

describe('the page under sealed/', () => {
    it('names no address with a host and asks for no lookup ahead', () => {
        expect(addresses(readFileSync(sealedPage.from, 'utf8'))).toEqual([]);
    });
});

describe('the reading of it', () => {
    it('finds an address of any scheme, by its host alone', () => {
        expect(addresses('<a href="https://reply.example/path?x=1">x</a>')).toEqual(['https://reply.example']);
        expect(addresses("const base = 'wss://socket.example:443/feed';")).toEqual(['wss://socket.example:443']);
        expect(addresses('ftp://files.example/a file://share/b HTTP://LOUD.EXAMPLE')).toEqual([
            'ftp://files.example',
            'file://share',
            'HTTP://LOUD.EXAMPLE',
        ]);
        expect(addresses('"https://"')).toEqual(['https://']);
    });

    it('finds an address with no scheme where markup or a style takes one', () => {
        expect(addresses('<img src="//cdn.example/x.png">')).toEqual(['//cdn.example']);
        expect(addresses('<link href=//fonts.example/a>')).toEqual(['//fonts.example']);
        expect(addresses('a{background:url(//img.example/b.png)}')).toEqual(['//img.example']);
        expect(addresses('@import "//css.example/c.css";')).toEqual(['//css.example']);
    });

    it('finds a hint that looks a name up, connects or fetches ahead', () => {
        expect(addresses('<link rel="dns-prefetch" href="#"><link rel=preconnect href="#">')).toEqual([
            'rel=dns-prefetch',
            'rel=preconnect',
        ]);
        expect(
            addresses('<link rel="prefetch"><link rel="preload"><link rel="prerender"><link rel="modulepreload">'),
        ).toEqual(['rel=prefetch', 'rel=preload', 'rel=prerender', 'rel=modulepreload']);
    });

    it('lets the three namespaces pass by their exact text, and nothing that only starts like one', () => {
        expect(addresses(namespaces.map((each) => `<svg xmlns="${each}">`).join(''))).toEqual([]);
        expect(addresses('const ns = "http://www.w3.org/2000/svg";')).toEqual([]);
        expect(addresses('<a href="http://www.w3.org/2000/svgx">')).toEqual(['http://www.w3.org']);
        expect(addresses('<a href="http://www.w3.org/TR/CSP3/">')).toEqual(['http://www.w3.org']);
    });

    it('lets a host written as text, a relative path, a fragment and data: pass', () => {
        expect(
            addresses(
                'linkedin.com/in/someone-sample · a@example.com <a href="#top"> <img src="data:image/png;base64,AA"> <a href="sealed/x.html">',
            ),
        ).toEqual([]);
    });
});

describe('the page it reads', () => {
    it('is the one the desktop shows and the build carries', () => {
        expect(sealedPage.from).toBe(join(import.meta.dirname, '..', '..', '..', 'desktop', 'demo', 'index.html'));
    });
});
