// Pure: the agent's words read as Markdown of a fixed subset - paragraphs, emphasis, inline code, code blocks, lists,
// headings, block quotes and simple pipe tables - into a plain tree that a view draws. A link is kept as its text and
// its address, both text. Raw HTML is not in the subset: it stays the text it is, like everything else the subset does
// not hold, and a table that is not whole (ragged rows, no header separator) is text too, never guessed. Nesting is
// read to a fixed depth and the rest is text, so no input makes the reading deep or slow: every expression below
// matches a line's start alone or takes its rest whole, so none of them scans a line more than once.

export type Inline =
    | { kind: 'text'; text: string }
    | { kind: 'em'; children: Inline[] }
    | { kind: 'strong'; children: Inline[] }
    | { kind: 'code'; text: string }
    | { kind: 'link'; text: string; href: string };

export type Block =
    | { kind: 'paragraph'; children: Inline[] }
    | { kind: 'heading'; level: 1 | 2 | 3 | 4 | 5 | 6; children: Inline[] }
    | { kind: 'code'; text: string }
    | { kind: 'list'; ordered: boolean; start: number; items: Block[][] }
    | { kind: 'quote'; children: Block[] }
    | { kind: 'table'; head: Inline[][]; rows: Inline[][][] };

/** How deep quotes and lists, and emphasis inside emphasis, are read; what lies deeper is text. */
const maxDepth = 8;
/** Text longer than this is drawn as it is, unread. */
const maxLength = 200_000;

const fenceLine = /^ {0,3}(`{3,}|~{3,})/;
/** A heading's marker and the spaces after it; the rest of the line is taken by index (`headingOf`). */
const headingStart = /^ {0,3}(#{1,6})(?:[ \t]+|$)/;
const quoteLine = /^ {0,3}> ?(.*)$/;
/** An item's marker; its rest is taken by a class that matches every character, a line separator included. */
const itemLine = /^( {0,3})([-*+]|\d{1,9}[.)])(?:[ \t]+([^]*))?$/;
const separatorCell = /^:?-+:?$/;

/** The agent's text as blocks of the subset. */
export function markdown(source: string): Block[] {
    const text = source.replace(/\r\n?/g, '\n');
    if (text.length > maxLength) return [asText([text])];
    return blocks(text.split('\n'), 0);
}

const asText = (lines: readonly string[]): Block => ({
    kind: 'paragraph',
    children: [{ kind: 'text', text: lines.join('\n') }],
});

const blank = (line: string) => line.trim() === '';
const indentOf = (line: string) => line.length - line.trimStart().length;
const spaceOrTab = (char: string | undefined) => char === ' ' || char === '\t';

/** A heading's level and its text, without the spaces and the closing run of # that end it; null when not one. */
function headingOf(line: string): { level: 1 | 2 | 3 | 4 | 5 | 6; text: string } | null {
    const start = headingStart.exec(line);
    if (!start) return null;
    const rest = line.slice(start[0].length);
    let end = rest.length;
    while (end > 0 && spaceOrTab(rest[end - 1])) end--;
    let hashes = end;
    while (hashes > 0 && rest[hashes - 1] === '#') hashes--;
    // A closing run is the whole rest, or follows a space or a tab; one glued to a word is the word's.
    if (hashes < end && (hashes === 0 || spaceOrTab(rest[hashes - 1]))) {
        end = hashes;
        while (end > 0 && spaceOrTab(rest[end - 1])) end--;
    }
    return { level: start[1]!.length as 1 | 2 | 3 | 4 | 5 | 6, text: rest.slice(0, end) };
}

function startsBlock(line: string): boolean {
    return fenceLine.test(line) || headingStart.test(line) || quoteLine.test(line) || itemLine.test(line);
}

function blocks(lines: readonly string[], depth: number): Block[] {
    if (depth >= maxDepth) return lines.some((line) => !blank(line)) ? [asText(trimmed(lines))] : [];
    const out: Block[] = [];
    let at = 0;
    while (at < lines.length) {
        const line = lines[at]!;
        if (blank(line)) {
            at++;
            continue;
        }
        const fence = fenceLine.exec(line);
        if (fence) {
            const mark = fence[1]!;
            const indent = indentOf(line);
            const body: string[] = [];
            at++;
            while (at < lines.length && !closesFence(lines[at]!, mark)) {
                body.push(lines[at]!.replace(new RegExp(`^ {0,${indent}}`), ''));
                at++;
            }
            at++;
            out.push({ kind: 'code', text: body.join('\n') });
            continue;
        }
        const heading = headingOf(line);
        if (heading) {
            out.push({ kind: 'heading', level: heading.level, children: inline(heading.text, depth) });
            at++;
            continue;
        }
        if (quoteLine.test(line)) {
            const inner: string[] = [];
            while (at < lines.length && quoteLine.test(lines[at]!)) inner.push(quoteLine.exec(lines[at++]!)![1]!);
            out.push({ kind: 'quote', children: blocks(inner, depth + 1) });
            continue;
        }
        if (itemLine.test(line)) {
            at = list(lines, at, depth, out);
            continue;
        }
        if (tableStarts(lines, at)) {
            at = table(lines, at, depth, out);
            continue;
        }
        const paragraph: string[] = [line.trim()];
        at++;
        while (at < lines.length && !blank(lines[at]!) && !startsBlock(lines[at]!) && !tableStarts(lines, at))
            paragraph.push(lines[at++]!.trim());
        out.push({ kind: 'paragraph', children: inline(paragraph.join('\n'), depth) });
    }
    return out;
}

/** The lines from the first that is not blank to the last, cut once. */
function trimmed(lines: readonly string[]): string[] {
    let first = 0;
    while (first < lines.length && blank(lines[first]!)) first++;
    let last = lines.length;
    while (last > first && blank(lines[last - 1]!)) last--;
    return lines.slice(first, last);
}

function closesFence(line: string, mark: string): boolean {
    const close = /^ {0,3}(`{3,}|~{3,})[ \t]*$/.exec(line);
    return close !== null && close[1]![0] === mark[0] && close[1]!.length >= mark.length;
}

/** A list from the item at `start`, into `out`; returns the line after it. */
function list(lines: readonly string[], start: number, depth: number, out: Block[]): number {
    const first = itemLine.exec(lines[start]!)!;
    const ordered = /\d/.test(first[2]![0]!);
    const delimiter = first[2]!.at(-1);
    const sameKind = (marker: RegExpExecArray) =>
        /\d/.test(marker[2]![0]!) === ordered && marker[2]!.at(-1) === delimiter;
    const items: Block[][] = [];
    let at = start;
    while (at < lines.length) {
        const item = itemLine.exec(lines[at]!);
        if (!item || !sameKind(item)) break;
        const markerIndent = item[1]!.length;
        const contentIndent = markerIndent + item[2]!.length + 1;
        const body: string[] = [item[3] ?? ''];
        at++;
        while (at < lines.length) {
            const next = lines[at]!;
            if (blank(next)) {
                let after = at;
                while (after < lines.length && blank(lines[after]!)) after++;
                if (after < lines.length && indentOf(lines[after]!) > markerIndent) {
                    // One at a time: a spread of a long run passes more arguments than a call takes.
                    for (let gap = at; gap < after; gap++) body.push('');
                    at = after;
                    continue;
                }
                break;
            }
            if (indentOf(next) > markerIndent) {
                body.push(next.slice(Math.min(indentOf(next), contentIndent)));
                at++;
                continue;
            }
            // A line that starts nothing of its own carries the item's last paragraph on.
            if (!startsBlock(next) && !tableStarts(lines, at) && !blank(body.at(-1)!)) {
                body.push(next);
                at++;
                continue;
            }
            break;
        }
        items.push(blocks(body, depth + 1));
        let after = at;
        while (after < lines.length && blank(lines[after]!)) after++;
        const following = after < lines.length ? itemLine.exec(lines[after]!) : null;
        if (following && sameKind(following) && following[1]!.length === first[1]!.length) at = after;
        else break;
    }
    out.push({ kind: 'list', ordered, start: ordered ? Number(first[2]!.slice(0, -1)) : 1, items });
    return at;
}

/** A row's cells: split at each pipe a backslash does not escape, the outer pipes dropped. */
function cells(line: string): string[] {
    let body = line.trim();
    if (body.startsWith('|')) body = body.slice(1);
    if (body.endsWith('|') && !body.endsWith('\\|')) body = body.slice(0, -1);
    const out: string[] = [];
    let cell = '';
    for (let at = 0; at < body.length; at++) {
        const char = body[at]!;
        if (char === '\\' && body[at + 1] === '|') {
            cell += '|';
            at++;
        } else if (char === '|') {
            out.push(cell.trim());
            cell = '';
        } else cell += char;
    }
    out.push(cell.trim());
    return out;
}

/** Whether a table starts here: a row with a pipe, then a separator of the same number of cells. */
function tableStarts(lines: readonly string[], at: number): boolean {
    const head = lines[at];
    const separator = lines[at + 1];
    if (head === undefined || separator === undefined || !head.includes('|')) return false;
    if (!/^[\s|:-]+$/.test(separator) || !separator.includes('-')) return false;
    const marks = cells(separator);
    return marks.every((mark) => separatorCell.test(mark)) && marks.length === cells(head).length;
}

/** The table at `start`, into `out`; a row of another width makes the whole of it text. Returns the line after it. */
function table(lines: readonly string[], start: number, depth: number, out: Block[]): number {
    const width = cells(lines[start]!).length;
    let at = start + 2;
    while (at < lines.length && !blank(lines[at]!) && lines[at]!.includes('|')) at++;
    const rows = lines.slice(start + 2, at).map(cells);
    if (rows.some((row) => row.length !== width)) out.push(asText(lines.slice(start, at)));
    else {
        const read = (row: string[]) => row.map((cell) => inline(cell, depth));
        out.push({ kind: 'table', head: read(cells(lines[start]!)), rows: rows.map(read) });
    }
    return at;
}

const punctuation = /[!-/:-@[-`{-~]/;
const space = /\s/;
const alphanumeric = /[\p{L}\p{N}]/u;
const link = /\[([^[\]\n]*)\]\(([^\s()]*)\)/y;

/** The length of the run of `char` at `at`. */
function runAt(source: string, at: number, char: string): number {
    let end = at;
    while (source[end] === char) end++;
    return end - at;
}

/**
 * Inline text of the subset. A delimiter pairs only with a run of the same character and the same length, so a run it
 * cannot pair stays text; and the search for a closing run that is not there is made once per kind of run, since a
 * later opener would search a part of the same text.
 */
export function inline(source: string, depth = 0): Inline[] {
    const out: Inline[] = [];
    let text = '';
    const flush = () => {
        if (text) out.push({ kind: 'text', text });
        text = '';
    };
    /** By the kind of run, the first place from which no closing run of that kind is found. */
    const none = new Map<string, number>();
    const closer = (char: string, size: number, from: number, test: (at: number) => boolean): number => {
        const key = `${char}${size}`;
        if ((none.get(key) ?? Infinity) <= from) return -1;
        let at = source.indexOf(char, from);
        while (at !== -1) {
            const run = runAt(source, at, char);
            if (run === size && test(at)) return at;
            at = source.indexOf(char, at + run);
        }
        none.set(key, from);
        return -1;
    };
    let at = 0;
    while (at < source.length) {
        const char = source[at]!;
        if (char === '\\' && at + 1 < source.length && punctuation.test(source[at + 1]!)) {
            text += source[at + 1];
            at += 2;
            continue;
        }
        if (char === '`') {
            const run = runAt(source, at, '`');
            const close = closer('`', run, at + run, () => true);
            if (close === -1) {
                text += source.slice(at, at + run);
                at += run;
                continue;
            }
            let code = source.slice(at + run, close).replace(/\n/g, ' ');
            if (code.length > 2 && code.startsWith(' ') && code.endsWith(' ') && code.trim()) code = code.slice(1, -1);
            flush();
            out.push({ kind: 'code', text: code });
            at = close + run;
            continue;
        }
        if (char === '!' && source[at + 1] === '[') {
            text += '![';
            at += 2;
            continue;
        }
        if (char === '[') {
            link.lastIndex = at;
            const found = link.exec(source);
            if (found) {
                flush();
                out.push({ kind: 'link', text: found[1]!, href: found[2]! });
                at = link.lastIndex;
                continue;
            }
        }
        if (char === '*' || char === '_') {
            const run = runAt(source, at, char);
            const after = source[at + run];
            const opens =
                run <= 2 &&
                depth < maxDepth &&
                after !== undefined &&
                !space.test(after) &&
                (char === '*' || at === 0 || !alphanumeric.test(source[at - 1]!));
            const close = opens
                ? closer(char, run, at + run + 1, (end) => {
                      const before = source[end - 1]!;
                      const next = source[end + run];
                      return !space.test(before) && (char === '*' || next === undefined || !alphanumeric.test(next));
                  })
                : -1;
            if (close === -1) {
                text += source.slice(at, at + run);
                at += run;
                continue;
            }
            flush();
            const children = inline(source.slice(at + run, close), depth + 1);
            out.push(run === 2 ? { kind: 'strong', children } : { kind: 'em', children });
            at = close + run;
            continue;
        }
        text += char;
        at++;
    }
    flush();
    return out;
}
