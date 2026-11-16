import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { sealedPage } from '../../scripts/package-lib.mjs';

// The page under sealed/ is served unable to act, but a browser may look a host name up before any policy speaks: from
// an address in the markup it is about to parse, or from a hint that asks it to. No request log shows such a lookup, so
// the file itself is read. The addresses it holds are counted in one list beside it, `desktop/demo/addresses.txt`, each
// exact string with how many times it stands in the page and why it is inert there; the desktop's tests read the same
// list, and a page that brings an address, or one more of a listed one, fails both until the list says why. Beyond the
// list the page holds no address with no scheme where markup or a style takes one, and no hint that looks a name up,
// connects or fetches ahead. What keeps a listed address inert is where the page is shown: a frame whose every request is
// refused, which the browser smoke and the sealed probe try.

const listFile = join(dirname(sealedPage.from), 'addresses.txt');

interface AddressList {
    pattern: RegExp;
    entries: { text: string; count: number; why: string }[];
}

/** The list as written: one `pattern<TAB>expression` line, then `<count><TAB><exact string><TAB><why>` lines. */
function addressList(text: string): AddressList {
    const lines = text.split('\n').filter((line) => line.trim() !== '' && !line.startsWith('#'));
    const patterns = lines.filter((line) => line.startsWith('pattern\t'));
    if (patterns.length !== 1) throw new Error(`${listFile} has ${patterns.length} pattern lines, not one`);
    const entries = lines
        .filter((line) => !line.startsWith('pattern\t'))
        .map((line) => {
            const [count = '', found = '', why = '', ...rest] = line.split('\t');
            if (rest.length > 0 || !/^[1-9][0-9]*$/.test(count)) throw new Error(`${listFile}: not an entry: ${line}`);
            return { text: found, count: Number(count), why };
        });
    return { pattern: new RegExp(patterns[0]!.slice('pattern\t'.length), 'g'), entries };
}

/** What differs between the page and its list: a string not listed, a count other than the listed one, one gone. */
function counted(page: string, list: AddressList): string[] {
    const found = new Map<string, number>();
    for (const match of page.matchAll(list.pattern)) found.set(match[0], (found.get(match[0]) ?? 0) + 1);
    const listed = new Map(list.entries.map((entry) => [entry.text, entry.count]));
    const findings: string[] = [];
    for (const [text, count] of found) {
        if (!listed.has(text)) findings.push(`not listed: ${text} (${count})`);
        else if (listed.get(text) !== count)
            findings.push(`${text}: the page holds ${count}, the list says ${listed.get(text)}`);
    }
    for (const [text, count] of listed)
        if (!found.has(text)) findings.push(`${text}: the list says ${count}, the page holds none`);
    return findings;
}

/** Every way the text asks for a place with no scheme of its own, a mail or telephone address, or a lookup ahead. */
function lookups(text: string): string[] {
    const found: string[] = [];
    for (const match of text.matchAll(
        /\b(?:src|href|action|formaction|poster|ping|srcset|data|background|cite|manifest|xlink:href)\s*=\s*["'`]?\s*(\/\/[^\s"'`<>/]*)/gi,
    ))
        found.push(match[1]!);
    for (const match of text.matchAll(/(?:url\(|@import)\s*["'`]?\s*(\/\/[^\s"'`<>)/]*)/gi)) found.push(match[1]!);
    // An address of mail or a telephone, which a press hands to another program on this computer.
    for (const match of text.matchAll(/\b(?:mailto|tel):[^\s"'`<>()[\]{},;]+/gi)) found.push(match[0]);
    for (const match of text.matchAll(
        /\brel\s*=\s*["'`]?\s*(dns-prefetch|preconnect|prefetch|preload|prerender|modulepreload)\b/gi,
    ))
        found.push(`rel=${match[1]!.toLowerCase()}`);
    return found;
}

const page = () => readFileSync(sealedPage.from, 'utf8');
const list = () => addressList(readFileSync(listFile, 'utf8'));

describe('the page under sealed/', () => {
    it('holds exactly the addresses its list counts, each listed once with why it is inert', () => {
        const read = list();
        expect(counted(page(), read)).toEqual([]);
        expect(new Set(read.entries.map((entry) => entry.text)).size).toBe(read.entries.length);
        for (const entry of read.entries) {
            expect(entry.text, entry.text).toMatch(new RegExp(`^${read.pattern.source}$`));
            expect(entry.why.trim().length, entry.text).toBeGreaterThan(10);
        }
    });

    it('takes no address with no scheme, and asks for no lookup ahead', () => {
        expect(lookups(page())).toEqual([]);
    });
});

describe('the reading of it', () => {
    const shape = { pattern: list().pattern, entries: [{ text: 'https://listed.example', count: 1, why: 'a sample' }] };

    it('refuses an address not listed, one more or one fewer of a listed one, of any scheme', () => {
        expect(counted('"https://listed.example"', shape)).toEqual([]);
        expect(counted('"https://listed.example" "wss://socket.example/x"', shape)).toEqual([
            'not listed: wss://socket.example (1)',
        ]);
        expect(counted('https://listed.example https://listed.example', shape)).toEqual([
            'https://listed.example: the page holds 2, the list says 1',
        ]);
        expect(counted('nothing', shape)).toEqual(['https://listed.example: the list says 1, the page holds none']);
        expect(counted('ftp://files.example file://share', shape)).toEqual([
            'not listed: ftp://files.example (1)',
            'not listed: file://share (1)',
            'https://listed.example: the list says 1, the page holds none',
        ]);
    });

    it('finds an address with no scheme where markup or a style takes one', () => {
        expect(lookups('<img src="//cdn.example/x.png">')).toEqual(['//cdn.example']);
        expect(lookups('<link href=//fonts.example/a>')).toEqual(['//fonts.example']);
        expect(lookups('a{background:url(//img.example/b.png)}')).toEqual(['//img.example']);
        expect(lookups('@import "//css.example/c.css";')).toEqual(['//css.example']);
    });

    it('finds an address of mail or a telephone, and not a word that only ends in the scheme', () => {
        expect(lookups('<a href="mailto:someone@example.com">')).toEqual(['mailto:someone@example.com']);
        expect(lookups("location.href = 'tel:+15550100';")).toEqual(['tel:+15550100']);
        expect(lookups('const channels = { sms: 1, tel: [] }; hotel: x')).toEqual([]);
    });

    it('finds a hint that looks a name up, connects or fetches ahead', () => {
        expect(lookups('<link rel="dns-prefetch" href="#"><link rel=preconnect href="#">')).toEqual([
            'rel=dns-prefetch',
            'rel=preconnect',
        ]);
        expect(
            lookups('<link rel="prefetch"><link rel="preload"><link rel="prerender"><link rel="modulepreload">'),
        ).toEqual(['rel=prefetch', 'rel=preload', 'rel=prerender', 'rel=modulepreload']);
    });

    it('lets a host written as text, a relative path, a fragment and data: pass', () => {
        expect(
            lookups(
                'linkedin.com/in/someone-sample · a@example.com <a href="#top"> <img src="data:image/png;base64,AA"> <a href="sealed/x.html">',
            ),
        ).toEqual([]);
    });
});

describe('the page it reads', () => {
    it('is the one the desktop shows and the build carries, beside its list', () => {
        expect(sealedPage.from).toBe(join(import.meta.dirname, '..', '..', '..', 'desktop', 'demo', 'index.html'));
        expect(listFile).toBe(join(import.meta.dirname, '..', '..', '..', 'desktop', 'demo', 'addresses.txt'));
    });
});
