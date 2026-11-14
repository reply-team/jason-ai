/**
 * The names of the person's own MCP servers, for the Connect page's line under Claude Code and Codex: where each program
 * keeps the servers set up in its user settings, and a reader of each file that keeps the names and nothing else. A file
 * holds secrets beside the names (a server's environment, its headers, its arguments, its address), so it is read here,
 * in the main process, whole, and only the names leave: no value, command, argument, header, URL or environment entry.
 *
 * A name is the person's own text: the page draws it as text only, and it is made one line and cut at forty characters
 * here. It is never written to a log (a read that fails is logged by the program and a kind from a closed table), never
 * given to the chat and never part of a diagnostic bundle.
 *
 * No file can make the listing fail or hang: only a regular file is opened, one larger than `fileLimit` is not read, a
 * file nested deeper than `depthLimit` or naming more than `namesLimit` servers lists nothing, and whatever the reading or
 * the parse throws is caught here and lists nothing.
 *
 * What is read is what those programs load in a session of the person's own, as Open in starts one; what is not read is
 * not listed, and the docs say so: Claude Code's servers of a project folder (local and project scope), of a plugin, of
 * managed settings and claude.ai connectors; Codex's project layers, profiles, plugins, cloud-managed defaults and ChatGPT
 * apps. A file that is not there, cannot be read or cannot be parsed lists nothing, and a wrong guess of a place shows
 * nothing and harms nothing.
 */

import { constants } from 'node:fs';
import { open, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { unseen } from './chat-header.ts';

/** The programs whose own servers Connect names. */
export type OwnServersProgram = 'claude' | 'codex';

/** How long a name is drawn, in characters: a longer one keeps the first thirty-nine and ends with an ellipsis. */
export const nameLimit = 40;

/**
 * The largest file read, in bytes: 64 MiB. A `.claude.json` with a long history of project folders runs to tens of MB,
 * and one of 45 MB parses in a fifth of a second; a larger file is not read and lists nothing.
 */
export const fileLimit = 64 * 1024 * 1024;

/**
 * How deep a file may nest its arrays and tables (objects in JSON): 64. A real file nests a handful of levels; the Codex
 * reader steps over a value by recursion, so a file nested past this is taken as one this reader cannot follow and lists
 * nothing, and the JSON file is held to the same depth.
 */
export const depthLimit = 64;

/**
 * The most servers a file may name: 1,000. The page draws the first five and counts the rest, so the names are kept
 * whole up to this many and the count it draws is the file's own; a file naming more lists nothing, and no count is
 * ever short.
 */
export const namesLimit = 1000;

/**
 * Why a file listed nothing although it is there; never its text. `unreadable`: it could not be read, or it is no
 * regular file; `too_large`: larger than `fileLimit`, or naming more than `namesLimit` servers; `malformed`: it could not
 * be parsed, or nests deeper than `depthLimit`.
 */
export type UnreadKind = 'unreadable' | 'too_large' | 'malformed';

/** What `readOwnServers` reads a file with: its text, or null for one larger than `fileLimit`; a failure throws. */
export type ReadFile = (file: string) => Promise<string | null>;

export type OwnServersRead = { names: string[] } | { unread: UnreadKind };

/**
 * The file each program keeps its user servers in.
 *
 * Claude Code: `~/.claude.json`, its top-level `mcpServers` ("User-scoped servers are stored in `~/.claude.json`",
 * https://code.claude.com/docs/en/mcp, "MCP installation scopes"). Where `CLAUDE_CONFIG_DIR` moves its configuration
 * folder, the file moves with it: "If you set `CLAUDE_CONFIG_DIR`, every `~/.claude` path on this page lives under that
 * directory instead. The same applies to `.claude.json`, which otherwise sits at `~/.claude.json`."
 * (https://code.claude.com/docs/en/claude-directory, read 2026-11-14), so `$CLAUDE_CONFIG_DIR/.claude.json`.
 *
 * Codex: `config.toml` in `CODEX_HOME`, by default `~/.codex`, its `[mcp_servers.<server-name>]` tables
 * (https://learn.chatgpt.com/docs/extend/mcp?surface=cli, read 2026-11-14).
 */
export function ownServersFile(program: OwnServersProgram, env: Readonly<Record<string, string | undefined>>, home: string): string {
    if (program === 'claude') {
        const moved = env['CLAUDE_CONFIG_DIR']?.trim();
        return moved ? join(moved, '.claude.json') : join(home, '.claude.json');
    }
    const moved = env['CODEX_HOME']?.trim();
    return moved ? join(moved, 'config.toml') : join(home, '.codex', 'config.toml');
}

/**
 * Reads a program's file and keeps its servers' names; a file that is not there lists none and is no failure. It never
 * rejects: whatever the reading or the parse throws lists nothing.
 */
export async function readOwnServers(
    program: OwnServersProgram,
    env: Readonly<Record<string, string | undefined>>,
    home: string,
    read: ReadFile = readRegularFile,
): Promise<OwnServersRead> {
    let text: string | null;
    try {
        text = await read(ownServersFile(program, env, home));
    } catch (error) {
        const code = (error as NodeJS.ErrnoException | undefined)?.code;
        return code === 'ENOENT' || code === 'ENOTDIR' ? { names: [] } : { unread: 'unreadable' };
    }
    if (text === null) return { unread: 'too_large' };
    try {
        const names = program === 'claude' ? claudeServerNames(text) : codexServerNames(text);
        if (names === null) return { unread: 'malformed' };
        const kept = shown(names);
        return kept === null ? { unread: 'too_large' } : { names: kept };
    } catch {
        return { unread: 'malformed' };
    }
}

/**
 * A file's text, when it is a regular file of at most `limit` bytes; null for a larger one. What is no regular file (a
 * folder, a FIFO, a device, a socket, or a link to one) is refused by its kind before it is opened, so no open waits for
 * a writer and no read runs without end; the file is opened without blocking where the system has the flag (Windows has
 * none), checked again once opened, and read no further than the size it had then.
 */
export async function readRegularFile(file: string, limit = fileLimit): Promise<string | null> {
    const notRegular = () => new Error('not a regular file');
    const before = await stat(file);
    if (!before.isFile()) throw notRegular();
    if (before.size > limit) return null;
    const handle = await open(file, constants.O_RDONLY | (constants.O_NONBLOCK ?? 0));
    try {
        const opened = await handle.stat();
        if (!opened.isFile()) throw notRegular();
        if (opened.size > limit) return null;
        const buffer = Buffer.alloc(opened.size);
        let filled = 0;
        while (filled < buffer.length) {
            const { bytesRead } = await handle.read(buffer, filled, buffer.length - filled, filled);
            if (bytesRead === 0) break;
            filled += bytesRead;
        }
        return buffer.toString('utf8', 0, filled);
    } finally {
        await handle.close();
    }
}

/**
 * Each name as the page draws it, in order: white space made single spaces and every unseen character taken out (the
 * house class, `unseen`: controls, format characters such as the bidirectional and zero-width ones and the tag
 * characters, the line and paragraph separators, every default-ignorable code point), an empty one dropped, each once,
 * then cut to `nameLimit` with an ellipsis. Names are told apart before the cut, so two that differ only past it both
 * stay and the count stays the file's. Null for more than `namesLimit` names.
 */
function shown(names: readonly string[]): string[] | null {
    if (names.length > namesLimit) return null;
    const seen = new Set<string>();
    const out: string[] = [];
    for (const name of names) {
        const clean = name.replace(/\s+/g, ' ').replace(unseen, '').replace(/\s+/g, ' ').trim();
        if (clean === '' || seen.has(clean)) continue;
        seen.add(clean);
        const characters = Array.from(clean);
        out.push(characters.length > nameLimit ? `${characters.slice(0, nameLimit - 1).join('')}…` : clean);
    }
    return out;
}

/** Whether a JSON text nests its arrays and objects deeper than `limit`; brackets inside strings are not counted. */
function nestedDeeperThan(text: string, limit: number): boolean {
    let depth = 0;
    let inString = false;
    for (let at = 0; at < text.length; at++) {
        const char = text.charCodeAt(at);
        if (inString) {
            if (char === 0x5c) at++;
            else if (char === 0x22) inString = false;
        } else if (char === 0x22) inString = true;
        else if (char === 0x5b || char === 0x7b) {
            if (++depth > limit) return true;
        } else if (char === 0x5d || char === 0x7d) depth--;
    }
    return false;
}

/**
 * The keys of the top-level `mcpServers` object of Claude Code's `.claude.json`, in the file's order; null for text that
 * is not a JSON object. The rest of the file (its projects, its account, every server's own entry) is parsed and dropped.
 * JavaScript puts a key that reads as an array index (`"7"`) before the others, so such a name may move forward.
 */
export function claudeServerNames(text: string): string[] | null {
    if (nestedDeeperThan(text, depthLimit)) return null;
    let parsed: unknown;
    try {
        parsed = JSON.parse(text.replace(/^\uFEFF/, ''));
    } catch {
        return null;
    }
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return null;
    const servers = (parsed as Record<string, unknown>)['mcpServers'];
    if (typeof servers !== 'object' || servers === null || Array.isArray(servers)) return [];
    return Object.keys(servers);
}

/**
 * The servers of a Codex `config.toml`, in the file's order, without one whose own table says `enabled = false`; null
 * for text this reader cannot follow. A narrow reader, so the desktop carries no TOML package: it walks the file's
 * statements (table headers and key/value pairs), steps over every value, strings of all four kinds, arrays and inline
 * tables over several lines included, and keeps the second key of each `mcp_servers.<name>` it meets, bare or quoted:
 * - `[mcp_servers.<name>]` and its subtables (`[mcp_servers.<name>.env]`),
 * - `<name> = { ... }` and `<name>.<key> = ...` under `[mcp_servers]`,
 * - `mcp_servers.<name>.<key> = ...` at the top, and the keys of `mcp_servers = { ... }`.
 *
 * What it misses, or takes as written: it checks no value's own syntax beyond its shape (a number, a date, a boolean are
 * runs of characters), so a file Codex refuses for such a value may still list its names here; it does not refuse a key
 * or a table defined twice; `enabled = false` is honoured in a server's table, in `<name>.enabled = false` and inside the
 * server's inline table, and missed under `mcp_servers = { ... }`; a bare key is ASCII letters, digits, `_` and `-`, as
 * TOML 1.0 has it, and a file with any other is followed no further and lists nothing; a value nested deeper than
 * `depthLimit` arrays and inline tables is followed no further either.
 */
export function codexServerNames(text: string): string[] | null {
    const reader = new TomlStatements(text.replace(/^\uFEFF/, ''));
    return reader.servers();
}

/** The walk `codexServerNames` makes; every step that meets what it cannot follow ends it with null. */
class TomlStatements {
    #text: string;
    #at = 0;
    #names = new Set<string>();
    #disabled = new Set<string>();

    constructor(text: string) {
        this.#text = text;
    }

    servers(): string[] | null {
        let table: string[] = [];
        for (;;) {
            this.#blanks(true);
            if (this.#at >= this.#text.length) break;
            const char = this.#text[this.#at];
            if (char === '[') {
                const many = this.#text[this.#at + 1] === '[';
                this.#at += many ? 2 : 1;
                this.#spaces();
                const key = this.#key();
                if (key === null) return null;
                this.#spaces();
                if (!this.#take(many ? ']]' : ']')) return null;
                table = key;
                if (!many && key[0] === 'mcp_servers' && key.length >= 2) this.#name(key[1]!);
            } else {
                const key = this.#key();
                if (key === null) return null;
                this.#spaces();
                if (!this.#take('=')) return null;
                this.#spaces();
                const value = this.#value();
                if (value === null) return null;
                const full = [...table, ...key];
                if (full[0] === 'mcp_servers') {
                    if (full.length === 1) for (const inner of value.keys) this.#name(inner[0]!);
                    else {
                        this.#name(full[1]!);
                        if (full.length === 3 && full[2] === 'enabled' && value.isFalse) this.#disabled.add(full[1]!);
                        if (full.length === 2 && value.falseKeys.has('enabled')) this.#disabled.add(full[1]!);
                    }
                }
            }
            if (!this.#lineEnd()) return null;
        }
        return [...this.#names].filter((name) => !this.#disabled.has(name));
    }

    #name(name: string): void {
        this.#names.add(name);
    }

    #take(word: string): boolean {
        if (!this.#text.startsWith(word, this.#at)) return false;
        this.#at += word.length;
        return true;
    }

    /** Spaces and tabs. */
    #spaces(): void {
        while (this.#text[this.#at] === ' ' || this.#text[this.#at] === '\t') this.#at++;
    }

    /** Spaces, comments and, where `lines`, line ends. */
    #blanks(lines: boolean): void {
        for (;;) {
            this.#spaces();
            const char = this.#text[this.#at];
            if (char === '#') this.#comment();
            else if (lines && (char === '\n' || (char === '\r' && this.#text[this.#at + 1] === '\n'))) this.#at += char === '\n' ? 1 : 2;
            else return;
        }
    }

    #comment(): void {
        while (this.#at < this.#text.length && this.#text[this.#at] !== '\n' && this.#text[this.#at] !== '\r') this.#at++;
    }

    /** The end of a statement: spaces, a comment, then a line end or the end of the text. */
    #lineEnd(): boolean {
        this.#spaces();
        if (this.#text[this.#at] === '#') this.#comment();
        if (this.#at >= this.#text.length) return true;
        if (this.#take('\n') || this.#take('\r\n')) return true;
        return false;
    }

    /** A dotted key: bare or quoted parts, with spaces around each dot. */
    #key(): string[] | null {
        const parts: string[] = [];
        for (;;) {
            const part = this.#keyPart();
            if (part === null) return null;
            parts.push(part);
            const back = this.#at;
            this.#spaces();
            if (this.#text[this.#at] !== '.') {
                this.#at = back;
                return parts;
            }
            this.#at++;
            this.#spaces();
        }
    }

    #keyPart(): string | null {
        const char = this.#text[this.#at];
        if (char === '"') return this.#text.startsWith('"""', this.#at) ? null : this.#basic();
        if (char === "'") return this.#text.startsWith("'''", this.#at) ? null : this.#literal();
        const start = this.#at;
        while (this.#at < this.#text.length && /[A-Za-z0-9_-]/.test(this.#text[this.#at]!)) this.#at++;
        return this.#at > start ? this.#text.slice(start, this.#at) : null;
    }

    /** A basic string on one line, its escapes read. */
    #basic(): string | null {
        this.#at++;
        let out = '';
        for (;;) {
            const char = this.#text[this.#at];
            if (char === undefined || char === '\n' || char === '\r') return null;
            this.#at++;
            if (char === '"') return out;
            if (char !== '\\') {
                out += char;
                continue;
            }
            const escape = this.#text[this.#at++];
            const simple: Record<string, string> = { b: '\b', t: '\t', n: '\n', f: '\f', r: '\r', e: '\u001b', '"': '"', '\\': '\\' };
            if (escape !== undefined && escape in simple) out += simple[escape];
            else if (escape === 'u' || escape === 'U' || escape === 'x') {
                const length = escape === 'u' ? 4 : escape === 'U' ? 8 : 2;
                const hex = this.#text.slice(this.#at, this.#at + length);
                if (!new RegExp(`^[0-9A-Fa-f]{${length}}$`).test(hex)) return null;
                const point = Number.parseInt(hex, 16);
                if (point > 0x10ffff || (point >= 0xd800 && point <= 0xdfff)) return null;
                out += String.fromCodePoint(point);
                this.#at += length;
            } else return null;
        }
    }

    /** A literal string on one line. */
    #literal(): string | null {
        const end = this.#text.indexOf("'", this.#at + 1);
        if (end < 0) return null;
        const body = this.#text.slice(this.#at + 1, end);
        if (/[\r\n]/.test(body)) return null;
        this.#at = end + 1;
        return body;
    }

    /** A multi-line string: its body is stepped over, a backslash in a basic one taking the character after it. */
    #multiline(quote: '"' | "'"): boolean {
        const fence = quote.repeat(3);
        this.#at += 3;
        for (;;) {
            if (this.#at >= this.#text.length) return false;
            if (quote === '"' && this.#text[this.#at] === '\\') {
                this.#at += 2;
                continue;
            }
            if (this.#text.startsWith(fence, this.#at)) {
                this.#at += 3;
                // Up to two more quotes belong to the body: `""""` ends a body that ends in one.
                for (let more = 0; more < 2 && this.#text[this.#at] === quote; more++) this.#at++;
                return true;
            }
            this.#at++;
        }
    }

    /**
     * Steps over one value. What it says of it: whether it is the boolean `false`, and for an inline table its keys and
     * which of its one-part keys are `false`. Null past `depthLimit` levels of arrays and inline tables.
     */
    #value(depth = 0): { isFalse: boolean; keys: string[][]; falseKeys: Set<string> } | null {
        const plain = { isFalse: false, keys: [] as string[][], falseKeys: new Set<string>() };
        const char = this.#text[this.#at];
        if (char === undefined) return null;
        // One more array or inline table here would nest deeper than `depthLimit`.
        if ((char === '[' || char === '{') && depth >= depthLimit) return null;
        if (char === '"' || char === "'") {
            if (this.#text.startsWith(char.repeat(3), this.#at)) return this.#multiline(char) ? plain : null;
            return (char === '"' ? this.#basic() : this.#literal()) === null ? null : plain;
        }
        if (char === '[') {
            this.#at++;
            for (;;) {
                this.#blanks(true);
                if (this.#take(']')) return plain;
                if (this.#value(depth + 1) === null) return null;
                this.#blanks(true);
                if (this.#take(']')) return plain;
                if (!this.#take(',')) return null;
            }
        }
        if (char === '{') {
            this.#at++;
            const keys: string[][] = [];
            const falseKeys = new Set<string>();
            for (;;) {
                this.#blanks(true);
                if (this.#take('}')) return { isFalse: false, keys, falseKeys };
                const key = this.#key();
                if (key === null) return null;
                this.#spaces();
                if (!this.#take('=')) return null;
                this.#spaces();
                const inner = this.#value(depth + 1);
                if (inner === null) return null;
                keys.push(key);
                if (key.length === 1 && inner.isFalse) falseKeys.add(key[0]!);
                this.#blanks(true);
                if (this.#take('}')) return { isFalse: false, keys, falseKeys };
                if (!this.#take(',')) return null;
            }
        }
        const run = this.#run();
        if (run === '') return null;
        // A date and a time may be parted by one space: `1979-05-27 07:32:00Z`.
        if (/^\d{4}-\d{2}-\d{2}$/.test(run) && this.#text[this.#at] === ' ' && /\d/.test(this.#text[this.#at + 1] ?? '')) {
            this.#at++;
            this.#run();
        }
        return { ...plain, isFalse: run === 'false' };
    }

    /** A number, a boolean, a date: the characters up to a space, a separator, a comment or a line end. */
    #run(): string {
        const start = this.#at;
        while (this.#at < this.#text.length && !/[\s,\]}#]/.test(this.#text[this.#at]!)) this.#at++;
        return this.#text.slice(start, this.#at);
    }
}
