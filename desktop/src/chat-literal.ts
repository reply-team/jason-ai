// Pure: the literal reading of one shell command, for Full access. A word runs only if it is literal for its shell, made
// of the characters a shell passes on as they are, with quotes as the only change it undergoes. Anything else anywhere
// in the command makes the whole command held, with a closed reason; nothing is re-spelled, so what is read is exactly
// what the shell passes on. Ask first and Plan only never read a command this way: their cards show the text as written.

import { posix, win32 } from 'node:path';
import type { Platform } from './chat-permission-model.ts';

/**
 * Why a command is held rather than read. The grammar's reasons say which character or form is not literal; the others
 * say what a literal command does that the desktop will not run without a person (`heldByWhatRuns`, and the permission
 * model's own: a guarded name, a search variable, a jason of the agent's own, a folder that holds the data directory,
 * reply).
 */
export type HeldReason =
    | 'expansion'
    | 'escape'
    | 'glob'
    | 'brace'
    | 'group'
    | 'history'
    | 'comment'
    | 'tilde'
    | 'non_ascii'
    | 'control_character'
    | 'character'
    | 'operator'
    | 'background'
    | 'assignment'
    | 'here_document'
    | 'process_substitution'
    | 'quote'
    | 'array'
    | 'stop_parsing'
    | 'call_operator'
    | 'syntax'
    | 'unknown_shell'
    | 'command_prompt'
    | 'inline_code'
    | 'runner'
    | 'builtin'
    | 'servers'
    | 'directory_change'
    | 'reply'
    | 'guarded_name'
    | 'search'
    | 'local_jason'
    | 'holds_data';

/** What a simple command's standard input is: nothing given, a pipe, a here-document's body, or a file. */
export type LiteralInput = 'none' | 'pipe' | 'here_document' | 'file';

/** One simple command of a literal reading. */
export interface LiteralCommand {
    /** Its resolved words, the program first: exactly what the shell passes on. */
    words: string[];
    /** The resolved targets of its redirections, each a path it reads or writes. */
    targets: string[];
    input: LiteralInput;
    /**
     * The folder it runs in when a directory change earlier in the command set one: a literal absolute path, as the
     * command spells it. Null when it runs in the turn's own folder.
     */
    cwd: string | null;
}

export type LiteralReading = { held: false; commands: LiteralCommand[] } | { held: true; reason: HeldReason };

export interface LiteralContext {
    platform: Platform;
    /** What a leading `~` resolves to on POSIX. */
    home: string;
}

const held = (reason: HeldReason): LiteralReading => ({ held: true, reason });

class Held {
    readonly reason: HeldReason;
    constructor(reason: HeldReason) {
        this.reason = reason;
    }
}

/**
 * Reads one tool input's command: the Bash tool's by the POSIX grammar (Git Bash on Windows), the PowerShell tool's by
 * PowerShell's. Any other tool's command is held, since its shell's grammar is not known.
 */
export function readLiteral(tool: string, command: string, context: LiteralContext): LiteralReading {
    try {
        let commands: Parsed[];
        if (tool === 'Bash') commands = readPosix(command, context.home);
        else if (tool === 'PowerShell') commands = readPowerShell(command);
        else return held('unknown_shell');
        if (commands.length === 0) return held('syntax');
        return { held: false, commands: withDirectories(commands, tool === 'PowerShell', context) };
    } catch (error) {
        if (error instanceof Held) return held(error.reason);
        throw error;
    }
}

interface Parsed {
    words: string[];
    targets: string[];
    input: LiteralInput;
}

type Separator = 'and' | 'or' | 'semicolon' | 'pipe' | 'newline' | 'end';

/** The command being read: its words so far, the word in progress and what the next word is. */
class Builder {
    readonly commands: Parsed[] = [];
    words: string[] = [];
    targets: string[] = [];
    input: LiteralInput = 'none';
    /** The word in progress, or null between words. */
    word: string | null = null;
    /** The word in progress as written, to tell an assignment and a bare `2` apart. */
    raw = '';
    quoted = false;
    /** A redirection waiting for its target. */
    redirect = false;
    /** The operator that ended the last command, if it needs a command after it. */
    pending: Separator | null = null;

    readonly finish: (builder: Builder, word: string) => void;

    constructor(finish: (builder: Builder, word: string) => void) {
        this.finish = finish;
    }

    append(text: string, raw: string, quoted: boolean): void {
        this.word = (this.word ?? '') + text;
        this.raw += raw;
        this.quoted ||= quoted;
    }

    endWord(): void {
        if (this.word === null) return;
        const word = this.word;
        this.finish(this, word);
        if (this.redirect) {
            // A device the shell emulates (`/dev/tcp/...`, `/dev/fd/...`) is not a file; only `/dev/null` is read as one.
            if (word.startsWith('/dev/') && word !== '/dev/null') throw new Held('operator');
            this.targets.push(word);
            this.redirect = false;
        } else {
            this.words.push(word);
        }
        this.word = null;
        this.raw = '';
        this.quoted = false;
    }

    endCommand(separator: Separator): void {
        this.endWord();
        if (this.redirect) throw new Held('syntax');
        const empty = this.words.length === 0;
        if (empty && this.targets.length > 0) throw new Held('syntax');
        if (empty) {
            // A blank line, or the end after a `;`, is no command; an operator with nothing on one side is not literal.
            if (separator === 'newline') return;
            if (separator === 'end' && (this.pending === null || this.pending === 'semicolon' || this.pending === 'newline')) return;
            throw new Held('syntax');
        }
        this.commands.push({ words: this.words, targets: this.targets, input: this.input });
        this.words = [];
        this.targets = [];
        this.input = separator === 'pipe' ? 'pipe' : 'none';
        this.pending = separator === 'end' ? null : separator;
    }
}

const posixBare = /[A-Za-z0-9_./:@+,%=-]/;
const posixDelimiter = /[\s;|&<>]/;
const assignment = /^[A-Za-z_][A-Za-z0-9_]*\+?=/;

/**
 * The words that are not a program in first position: bash's reserved words, and zsh's reserved words and precommand
 * modifiers, since the Bash tool's shell may be zsh. `builtin` and `command` are runners (`runners`).
 */
const reservedWords: ReadonlySet<string> = new Set(
    'if then elif else fi do done case esac while until for select function coproc time in - noglob nocorrect repeat foreach'.split(' '),
);

function asciiOnly(text: string): void {
    for (const character of text) {
        const code = character.codePointAt(0) ?? 0;
        if (code > 0x7e) throw new Held('non_ascii');
        if (code < 0x20 && character !== '\n' && character !== '\t') throw new Held('control_character');
    }
}

/** The POSIX grammar: the Bash tool's shell. */
function readPosix(text: string, home: string): Parsed[] {
    const builder = new Builder((b, word) => {
        // zsh rewrites an unquoted word that starts with `=` to the path of the program it names.
        if (b.raw.startsWith('=')) throw new Held('expansion');
        if (b.redirect || b.words.length > 0) return;
        // An unquoted name and `=` as the first word is an assignment, not a program.
        if (assignment.test(b.raw)) throw new Held('assignment');
        // A reserved word, bash's or zsh's, is not the program: the program that runs is a later word.
        if (reservedWords.has(word)) throw new Held('syntax');
    });
    const documents: string[] = [];
    let at = 0;
    const startOfWord = () => builder.word === null;
    while (at < text.length) {
        const c = text[at] as string;
        const code = c.codePointAt(0) ?? 0;
        // A here-document's body is data and is never read here; everywhere else, quoted bodies included, a character
        // outside ASCII or a control character is held.
        if (code > 0x7e) throw new Held('non_ascii');
        if (code < 0x20 && c !== '\n' && c !== '\t') throw new Held('control_character');
        if (c === ' ' || c === '\t') {
            builder.endWord();
            at++;
            continue;
        }
        if (c === '\n') {
            builder.endWord();
            const continues = builder.words.length === 0 && builder.targets.length === 0 && (builder.pending === 'and' || builder.pending === 'or' || builder.pending === 'pipe');
            if (!continues) builder.endCommand('newline');
            at++;
            at = readDocuments(text, at, documents);
            continue;
        }
        if (c === '\'') {
            const end = text.indexOf('\'', at + 1);
            if (end < 0) throw new Held('quote');
            const body = text.slice(at + 1, end);
            if (body.includes('\n')) throw new Held('quote');
            asciiOnly(body);
            builder.append(body, text.slice(at, end + 1), true);
            at = end + 1;
            continue;
        }
        if (c === '"') {
            const end = text.indexOf('"', at + 1);
            if (end < 0) throw new Held('quote');
            const body = text.slice(at + 1, end);
            if (/[$`]/.test(body)) throw new Held('expansion');
            if (body.includes('\\')) throw new Held('escape');
            if (body.includes('!')) throw new Held('history');
            if (body.includes('\n')) throw new Held('quote');
            asciiOnly(body);
            builder.append(body, text.slice(at, end + 1), true);
            at = end + 1;
            continue;
        }
        if (c === '~') {
            const next = text[at + 1];
            if (startOfWord() && (next === undefined || next === '/' || posixDelimiter.test(next))) {
                builder.append(home, '~', false);
                at++;
                continue;
            }
            throw new Held('tilde');
        }
        if (c === '&') {
            if (text[at + 1] === '&') {
                builder.endCommand('and');
                at += 2;
                continue;
            }
            throw new Held('background');
        }
        if (c === '|') {
            if (text[at + 1] === '|') {
                builder.endCommand('or');
                at += 2;
                continue;
            }
            if (text[at + 1] === '&') throw new Held('operator');
            builder.endCommand('pipe');
            at++;
            continue;
        }
        if (c === ';') {
            if (text[at + 1] === ';' || text[at + 1] === '&') throw new Held('syntax');
            builder.endCommand('semicolon');
            at++;
            continue;
        }
        if (c === '>') {
            if (builder.redirect) throw new Held('syntax');
            const fd = builder.word !== null && !builder.quoted ? builder.raw : null;
            if (fd !== null && /^[0-9]+$/.test(fd)) {
                if (fd !== '2') throw new Held('operator');
                // `2>` and `2>&1`.
                builder.word = null;
                builder.raw = '';
                if (text.startsWith('>&1', at) && (text[at + 3] === undefined || posixDelimiter.test(text[at + 3] as string))) {
                    at += 3;
                    continue;
                }
                if (text[at + 1] === '>' || text[at + 1] === '&' || text[at + 1] === '|' || text[at + 1] === '(') throw new Held('operator');
                builder.redirect = true;
                at++;
                continue;
            }
            builder.endWord();
            const next = text[at + 1];
            if (next === '(') throw new Held('process_substitution');
            if (next === '&' || next === '|') throw new Held('operator');
            if (next === '>') {
                if (text[at + 2] === '>' || text[at + 2] === '&' || text[at + 2] === '|' || text[at + 2] === '(') throw new Held('operator');
                at += 2;
            } else {
                at++;
            }
            builder.redirect = true;
            continue;
        }
        if (c === '<') {
            if (builder.redirect) throw new Held('syntax');
            if (builder.word !== null && !builder.quoted && /^[0-9]+$/.test(builder.raw)) throw new Held('operator');
            builder.endWord();
            const next = text[at + 1];
            if (next === '(') throw new Held('process_substitution');
            if (next === '>' || next === '&') throw new Held('operator');
            if (next === '<') {
                const after = text[at + 2];
                if (after === '<') throw new Held('operator');
                if (after === '-') throw new Held('here_document');
                at += 2;
                while (text[at] === ' ' || text[at] === '\t') at++;
                const quote = text[at];
                if (quote !== '\'' && quote !== '"') throw new Held('here_document');
                const end = text.indexOf(quote, at + 1);
                if (end < 0) throw new Held('here_document');
                const tag = text.slice(at + 1, end);
                if (!/^[A-Za-z0-9_.-]+$/.test(tag)) throw new Held('here_document');
                const after2 = text[end + 1];
                if (after2 !== undefined && !posixDelimiter.test(after2)) throw new Held('here_document');
                documents.push(tag);
                builder.input = 'here_document';
                at = end + 1;
                continue;
            }
            if (builder.input === 'none') builder.input = 'file';
            builder.redirect = true;
            at++;
            continue;
        }
        if (c === '$' || c === '`') throw new Held('expansion');
        if (c === '\\') throw new Held('escape');
        if ('*?[]'.includes(c)) throw new Held('glob');
        if (c === '{' || c === '}') throw new Held('brace');
        if (c === '(' || c === ')') throw new Held('group');
        if (c === '!') throw new Held('history');
        if (c === '#') throw new Held('comment');
        if (posixBare.test(c)) {
            builder.append(c, c, false);
            at++;
            continue;
        }
        throw new Held('character');
    }
    if (documents.length > 0) throw new Held('here_document');
    builder.endCommand('end');
    return builder.commands;
}

/**
 * Reads the bodies of the here-documents a line opened, in order, from the start of the next line: each ends at a line
 * that is its tag exactly. A body is data and is not read as words. A tag that never comes is held.
 */
function readDocuments(text: string, from: number, documents: string[]): number {
    let at = from;
    while (documents.length > 0) {
        const tag = documents.shift() as string;
        for (;;) {
            if (at > text.length) throw new Held('here_document');
            const end = text.indexOf('\n', at);
            const line = end < 0 ? text.slice(at) : text.slice(at, end);
            const next = end < 0 ? text.length + 1 : end + 1;
            if (line === tag) {
                at = Math.min(next, text.length);
                break;
            }
            if (end < 0) throw new Held('here_document');
            at = next;
        }
    }
    return at;
}

const powerShellBare = /[A-Za-z0-9_./\\:+=%-]/;

/** PowerShell's grammar: the PowerShell tool's shell. */
function readPowerShell(text: string): Parsed[] {
    asciiOnly(text);
    const builder = new Builder((b, word) => {
        if (b.raw === '--%') throw new Held('stop_parsing');
        if (word.includes('"')) throw new Held('quote');
        if (!b.redirect && b.words.length === 0) {
            if (b.raw === '.') throw new Held('call_operator');
            // A quoted first word is a value, not a program: PowerShell runs one only after `&`.
            if (b.quoted) throw new Held('call_operator');
        }
    });
    let at = 0;
    while (at < text.length) {
        const c = text[at] as string;
        if (c === ' ' || c === '\t') {
            builder.endWord();
            at++;
            continue;
        }
        if (c === '\n') throw new Held('operator');
        if (c === '\'') {
            let body = '';
            let end = at + 1;
            for (;;) {
                if (end >= text.length) throw new Held('quote');
                const d = text[end] as string;
                if (d === '\n') throw new Held('quote');
                if (d === '\'') {
                    if (text[end + 1] === '\'') {
                        body += '\'';
                        end += 2;
                        continue;
                    }
                    break;
                }
                body += d;
                end++;
            }
            builder.append(body, text.slice(at, end + 1), true);
            at = end + 1;
            continue;
        }
        if (c === '"') {
            let body = '';
            let end = at + 1;
            for (;;) {
                if (end >= text.length) throw new Held('quote');
                const d = text[end] as string;
                if (d === '$' || d === '`') throw new Held('expansion');
                if (d === '\n') throw new Held('quote');
                if (d === '"') {
                    if (text[end + 1] === '"') {
                        body += '"';
                        end += 2;
                        continue;
                    }
                    break;
                }
                body += d;
                end++;
            }
            builder.append(body, text.slice(at, end + 1), true);
            at = end + 1;
            continue;
        }
        if (c === '&') {
            if (text[at + 1] === '&') {
                builder.endCommand('and');
                at += 2;
                continue;
            }
            if (builder.word === null && builder.words.length === 0) throw new Held('call_operator');
            throw new Held('background');
        }
        if (c === '|') {
            if (text[at + 1] === '|') {
                builder.endCommand('or');
                at += 2;
                continue;
            }
            builder.endCommand('pipe');
            at++;
            continue;
        }
        if (c === ';') {
            builder.endCommand('semicolon');
            at++;
            continue;
        }
        if (c === '>') {
            if (builder.redirect) throw new Held('syntax');
            // A stream's number before `>` (`2>`, `3>`) is not in the literal set.
            if (builder.word !== null && !builder.quoted && /^[0-9]+$/.test(builder.raw)) throw new Held('operator');
            builder.endWord();
            const next = text[at + 1];
            if (next === '&') throw new Held('operator');
            if (next === '>') {
                if (text[at + 2] === '>' || text[at + 2] === '&') throw new Held('operator');
                at += 2;
            } else {
                at++;
            }
            builder.redirect = true;
            continue;
        }
        if (c === '<') throw new Held('operator');
        if (c === '$' || c === '`') throw new Held('expansion');
        if (c === '(' || c === ')') throw new Held('group');
        if (c === '{' || c === '}') throw new Held('brace');
        if (c === '@' || c === ',') throw new Held('array');
        if (c === '~') throw new Held('tilde');
        if ('*?[]'.includes(c)) throw new Held('glob');
        if (c === '#') throw new Held('comment');
        if (powerShellBare.test(c)) {
            builder.append(c, c, false);
            at++;
            continue;
        }
        throw new Held('character');
    }
    builder.endCommand('end');
    return builder.commands;
}

/** A program's name as recognised: its last path part, in lower case, without an executable's extension. */
export function programName(word: string): string {
    const name = word.split(/[\\/]/).pop() ?? '';
    return name.toLowerCase().replace(/\.(exe|com|ps1)$/, '');
}

const posixDirectoryChanges = new Set(['cd', 'chdir', 'pushd', 'popd']);
const powerShellDirectoryChanges = new Set(['cd', 'chdir', 'sl', 'set-location', 'pushd', 'push-location', 'popd', 'pop-location']);

/** The folder a directory change goes to, as the command spells it; null when it is not one literal path. */
function directoryTarget(words: readonly string[], powerShell: boolean, home: string): string | null {
    const program = programName(words[0] ?? '');
    if (program === 'popd' || program === 'pop-location') return null;
    const rest = words.slice(1);
    if (rest.length === 0) return powerShell ? null : home;
    if (powerShell && rest.length === 2 && /^-(path|literalpath|lp|pspath)$/i.test(rest[0] as string)) return rest[1] as string;
    if (rest.length === 1 && !(rest[0] as string).startsWith('-') && rest[0] !== '') return rest[0] as string;
    return null;
}

function isAbsolute(path: string, powerShell: boolean, platform: Platform): boolean {
    if (platform === 'win32') return /^[A-Za-z]:[\\/]/.test(path) || (powerShell ? win32.isAbsolute(path) : path.startsWith('/'));
    return posix.isAbsolute(path);
}

/**
 * Settles the folder each command runs in. A directory change to a literal absolute path sets it for the commands after
 * it; one to any other target leaves the folder unknown, and a command after it is held, since its relative words could
 * not be read plainly.
 */
function withDirectories(parsed: readonly Parsed[], powerShell: boolean, context: LiteralContext): LiteralCommand[] {
    const changes = powerShell ? powerShellDirectoryChanges : posixDirectoryChanges;
    let cwd: string | null = null;
    let unknown = false;
    return parsed.map((command) => {
        if (unknown) throw new Held('directory_change');
        const read: LiteralCommand = { ...command, cwd };
        if (changes.has(programName(command.words[0] ?? ''))) {
            const target = directoryTarget(command.words, powerShell, context.home);
            if (target !== null && isAbsolute(target, powerShell, context.platform)) cwd = target;
            else unknown = true;
        }
        return read;
    });
}

// Names to recognise, never to run: written as one string each, as the process guard expects of every module but the
// login-shell lookup.
const shellPrograms = new Set('sh bash zsh dash ksh mksh ash fish csh tcsh'.split(' '));
const [windowsPowerShell, powerShellCore, commandPrompt] = 'powershell pwsh cmd'.split(' ') as [string, string, string];
const powerShellPrograms = new Set([windowsPowerShell, powerShellCore]);
const pythonStem = /^(python|pythonw|py)[0-9.]*$/;
const nodeStem = /^(node|nodejs|bun)[0-9.]*$/;
const awkPrograms = new Set(['awk', 'gawk', 'mawk', 'nawk']);
const eInterpreters = /^(ruby|lua|luajit|julia|rscript|osascript|groovy)[0-9.]*$/;

/** The programs that start a later or another command they are given. A closed list: a program it misses runs. */
const runners = new Set(
    ('. source eval exec xargs env sudo doas su pkexec runuser nohup setsid at batch crontab schtasks ssh wsl start '
        + 'start-process saps invoke-expression iex invoke-command icm start-job sajb start-threadjob invoke-item ii '
        + 'register-scheduledtask timeout nice ionice stdbuf time command builtin watch flock script caffeinate chroot '
        + 'taskset strace ltrace unbuffer parallel systemd-run busybox chronic runas psexec winrs wscript cscript mshta '
        + 'rundll32 regsvr32 forfiles conhost fc r let mapfile readarray compgen complete').split(' '),
);

/** The builtins that change how a later word or program is read. */
const builtins = new Set(
    ('alias unalias set shopt enable export unset trap declare typeset readonly local hash set-alias sal new-alias nal '
        + 'remove-alias set-variable sv new-variable nv remove-variable rv import-module ipmo').split(' '),
);

/**
 * True when a single-dash option word's letters, read up to the first character that is not a letter, hold one of the
 * code letters before any letter that takes the rest of the word as its value: `-c`, `-lc`, `-c'...'` glued, `-Bc"..."`.
 */
function shortOption(word: string, letters: string, valued = ''): boolean {
    const cluster = /^-([A-Za-z]+)/.exec(word)?.[1];
    if (cluster === undefined) return false;
    for (const letter of cluster) {
        if (letters.includes(letter)) return true;
        if (valued.includes(letter)) return false;
    }
    return false;
}

function prefixOf(word: string, names: readonly string[]): boolean {
    if (!/^[-/][A-Za-z]+$/.test(word)) return false;
    const name = word.slice(1).toLowerCase();
    return names.some((each) => each.startsWith(name)) || ['ec', 'enc', 'cwa'].includes(name);
}

const editors = new Set(['vi', 'vim', 'nvim', 'gvim', 'view', 'vimdiff', 'ex']);
const packageManagers = new Set(['npm', 'pnpm', 'yarn']);

/** True when a package runner is given shell code to run (`npx -c`, `npm exec -c`, `pnpm exec -c`). */
function packageCode(program: string, args: readonly string[]): boolean {
    const call = (word: string) => word === '-c' || /^--(call|shell-mode)(=|$)/.test(word);
    if (program === 'npx' || program === 'pnpx') return args.some(call);
    return packageManagers.has(program) && args.some((word) => word === 'exec' || word === 'x' || word === 'dlx') && args.some(call);
}

/** True when the command is a shell or an interpreter handed code as text, or code through its standard input. */
function takesCode(command: LiteralCommand): boolean {
    const program = programName(command.words[0] ?? '');
    const args = command.words.slice(1);
    if (packageCode(program, args)) return true;
    if (editors.has(program)) return args.some((word) => word === '-c' || word === '--cmd' || word.startsWith('+'));
    if (program === 'gdb') return args.some((word) => /^-{1,2}(ex|iex|eval-command|init-eval-command)(=|$)/.test(word));
    const interpreter = shellPrograms.has(program) || powerShellPrograms.has(program) || pythonStem.test(program) || nodeStem.test(program)
        || program === 'deno' || program === 'perl' || program === 'php' || awkPrograms.has(program) || eInterpreters.test(program);
    if (!interpreter) return false;
    if (command.input === 'pipe' || command.input === 'here_document') return true;
    if (shellPrograms.has(program)) return args.some((word) => shortOption(word, 'c') || /^--command(=|$)/.test(word));
    if (powerShellPrograms.has(program)) {
        if (args.some((word) => prefixOf(word, ['command', 'encodedcommand', 'commandwithargs']))) return true;
        // Windows PowerShell reads a word with no option before it as a command, pwsh as a file.
        return program === windowsPowerShell && args.length > 0 && !args.some((word) => /^[-/]f(i(le?)?)?$/i.test(word));
    }
    if (pythonStem.test(program)) return args.some((word) => shortOption(word, 'c', 'mWXQ') || word === '-');
    if (nodeStem.test(program)) return args.some((word) => shortOption(word, 'ep', 'r') || /^--(eval|print)(=|$)/.test(word) || word === '-');
    if (program === 'deno') return args[0] === 'eval' || args[0] === 'repl';
    // perl's -M and -m take a module's name, and text after a `;` there is code.
    if (program === 'perl') return args.some((word) => shortOption(word, 'eE', 'MmIx') || (/^-[Mm]/.test(word) && word.includes(';')));
    if (program === 'php') return args.some((word) => shortOption(word, 'rRBEa', 'cdfzt'));
    // awk's program is its first word unless a file names it; gawk's -e and --source add program text beside a file.
    if (awkPrograms.has(program)) return args.some((word) => shortOption(word, 'e', 'fvF') || /^--source(=|$)/.test(word)) || !args.some((word) => word.startsWith('-f') || word === '--file');
    return args.some((word) => shortOption(word, 'eE'));
}

/** git's options that name a program it runs, wherever the verb takes them. */
const gitExecuting = /^--(upload-pack|receive-pack|exec|open-files-in-pager|config)(=|$)|^--[a-z-]*-cmd(=|$)/;

/** True when git is given configuration before its verb, or a verb or an option that runs a command it is given. */
function gitRuns(args: readonly string[]): boolean {
    let at = 0;
    while (at < args.length && (args[at] as string).startsWith('-')) {
        const word = args[at] as string;
        if (word.startsWith('-c') || word.startsWith('--config-env') || word.startsWith('--exec-path')) return true;
        at += word === '-C' || word === '--git-dir' || word === '--work-tree' || word === '--namespace' ? 2 : 1;
    }
    const verb = args[at];
    const rest = args.slice(at + 1);
    if (rest.some((word) => gitExecuting.test(word))) return true;
    if (verb === 'clone') return rest.some((word) => shortOption(word, 'uc', 'bo'));
    if (verb === 'grep') return rest.some((word) => shortOption(word, 'O', 'efABCm'));
    if (verb === 'config') return !rest.some((word) => /^--(get|get-all|get-regexp|list)$|^-l$/.test(word));
    if (verb === 'rebase') return rest.some((word) => word === '-x' || word.startsWith('--exec'));
    if (verb === 'bisect') return rest[0] === 'run';
    if (verb === 'submodule') return rest.includes('foreach');
    if (verb === 'filter-branch') return true;
    if (verb === 'difftool' || verb === 'mergetool') return rest.some((word) => word === '-x' || word.startsWith('--extcmd') || word.startsWith('--tool'));
    return false;
}

const containerPrograms = new Set(['docker', 'podman', 'nerdctl']);
const clusterPrograms = new Set(['kubectl', 'oc']);
const tarPrograms = new Set(['tar', 'bsdtar', 'gtar']);

/** True when a program is given a command, or a program of the agent's choosing, that it starts. */
function startsGiven(program: string, args: readonly string[]): boolean {
    if (containerPrograms.has(program)) return args.some((word) => word === 'run' || word === 'exec' || word === 'create');
    if (clusterPrograms.has(program)) return args.some((word) => word === 'exec' || word === 'run' || word === 'debug');
    if (program === 'rsync') return args.some((word) => shortOption(word, 'e') || /^--rsh(=|$)/.test(word));
    if (tarPrograms.has(program)) {
        return args.some((word) => /^--(to-command|use-compress-program|checkpoint-action|info-script|new-volume-script|rsh-command)(=|$)/.test(word)
            || shortOption(word, 'IF', 'fCbHKLNTVX'));
    }
    if (program === 'sort') return args.some((word) => /^--compress-program(=|$)/.test(word));
    return false;
}

/** The builtins that evaluate an array subscript they are handed, which runs a command substitution inside it. */
const evaluatesSubscript = new Set(['printf', 'read', 'test', '[', 'getopts', 'wait']);

/**
 * What a literal command does that is held all the same, by what runs (the order of 1.3's held list): cmd and its batch
 * files, code given as text, a runner, a builtin that changes how later words are read; then a command that changes the
 * MCP servers or plugins an agent host loads. Null when none.
 */
export function heldByWhatRuns(commands: readonly LiteralCommand[]): HeldReason | null {
    for (const command of commands) {
        const first = command.words[0] ?? '';
        const program = programName(first);
        const args = command.words.slice(1);
        if (program === commandPrompt || (/\.(bat|cmd)$/i.test(first) && command.words.length > 1)) return 'command_prompt';
        if (takesCode(command)) return 'inline_code';
        if (runners.has(program)) return 'runner';
        if (program === 'git' && gitRuns(args)) return 'runner';
        if (program === 'find' && command.words.some((word) => /^-(exec|execdir|ok|okdir)$/.test(word))) return 'runner';
        if (startsGiven(program, args)) return 'runner';
        if (builtins.has(program)) return 'builtin';
        if (command.words.some((word) => /^(alias|function|variable|env):/i.test(word))) return 'builtin';
        if (evaluatesSubscript.has(program) && args.some((word) => /[[$`]/.test(word))) return 'expansion';
        if (changesServers(command.words)) return 'servers';
    }
    return null;
}

const serverVerbs = new Set(['remove', 'login']);
const pluginVerbs = new Set(['install', 'enable', 'update', 'marketplace']);

/**
 * True when a command's words hold `mcp` with a verb after it that starts with `add`, or is `remove` or `login`, or hold
 * `plugin` or `plugins` with a verb after it that starts with `add` or is `install`, `enable`, `update` or
 * `marketplace`, whatever the program and whatever stands between: the shape of an agent host's command that changes
 * the MCP servers or the plugins it loads, or signs a server in. Any program counts, since a host can be started by a
 * name or a path the desktop does not know.
 */
function changesServers(words: readonly string[]): boolean {
    const folded = words.map((word) => word.toLowerCase());
    const after = (names: readonly string[]) => {
        const at = folded.findIndex((word) => names.includes(word));
        return at < 0 ? [] : folded.slice(at + 1);
    };
    if (after(['mcp']).some((word) => word.startsWith('add') || serverVerbs.has(word))) return true;
    return after(['plugin', 'plugins']).some((word) => word.startsWith('add') || pluginVerbs.has(word));
}
