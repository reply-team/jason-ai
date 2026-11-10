/**
 * The desktop's log: JSON Lines under `<data>/logs/`, in the form and the file shape the runtime's log sink writes
 * (`desktop-YYYYMMDD.jsonl`, rolled to `_001`, `_002` at the size limit, the same number of files kept), so `jason logs
 * --source desktop` and `jason doctor` read it as they read the runtime's.
 *
 * Under the logs rule no text a person gave reaches it: no key, no token, no prompt, no message, no error text a program
 * printed. Every line is one of the templates below, and every field is a hole of its template holding a number, a
 * boolean or a short single-line value the desktop itself produced: an id from one of its closed tables, a version, an
 * exit code, the loopback origin, a path the installation runs from. A field that is not a hole of its template is
 * dropped.
 */

import { appendFileSync, mkdirSync, readdirSync, statSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';

export const templates = {
    desktop_started: 'Desktop {Version} started on {Platform} with the data directory {DataDirectory}',
    runtime_found: 'A runtime was already up at {Origin}; it is used as it is',
    runtime_starting: 'No runtime was up; starting one with jason runtime start',
    runtime_start_failed: 'jason runtime start exited {Exit}',
    versions_differ: 'The runtime is {RuntimeVersion} and this desktop carries {BundleVersion}; the UI the runtime serves is loaded',
    ui_installed: 'jason ui install exited {Exit}',
    skills_deployed: 'jason skills install for {Target} exited {Exit}',
    window_loaded: 'The window loaded the UI at {Origin}',
    window_load_failed: 'The window could not load the UI: step {Step} exited {Exit}',
    navigation_refused: 'A navigation off {Origin} was refused',
    link_opened: 'An https link was handed to the system browser',
    link_refused: 'A link that is not https was refused',
    ipc_refused: 'A call on {Channel} from outside {Origin} was refused',
    cli_exited: 'jason {Verb} exited {Exit} after {ElapsedMs} ms',
    program_exited: '{Program} {Verb} exited {Exit} after {ElapsedMs} ms',
    settings_refused: 'The settings file at {Path} could not be read as JSON; nothing was written to it',
    plugin_configured: 'Plugin {PluginId} was configured; token {TokenChanged}, target {TargetChanged}, server {ServerChanged}; jason plugin reload exited {Exit}; valid {Valid}',
    provider_key_stored: 'The key of provider {ProviderId} was stored',
    provider_key_removed: 'The key of provider {ProviderId} was removed',
    reply_connected: 'Reply sign-in by {Method} exited {Exit}',
    reply_wired: 'Jason’s Reply plugin was placed and granted reply; global default written {RouteWritten}; jason plugin reload exited {Exit}',
    program_installed: 'npm install of {Program} exited {Exit}',
    // The desk: the tray, its notifications.
    tray_state: 'The tray shows the state {State}',
    attention_notified: 'A notification said that {Approvals} approvals and {Questions} questions wait on the person',
} as const;

export type TemplateName = keyof typeof templates;

type Holes<Text extends string> = Text extends `${string}{${infer Hole}}${infer Rest}` ? Hole | Holes<Rest> : never;

export type Value = string | number | boolean;
export type FieldsOf<Name extends TemplateName> = { [Hole in Holes<(typeof templates)[Name]>]: Value };

export type Level = 'Information' | 'Warning' | 'Error';

export interface Logger {
    info<Name extends TemplateName>(name: Name, fields: FieldsOf<Name>): void;
    warn<Name extends TemplateName>(name: Name, fields: FieldsOf<Name>): void;
    error<Name extends TemplateName>(name: Name, fields: FieldsOf<Name>): void;
}

export interface LogLimits {
    /** How many of the desktop's log files are kept; the runtime's `Logging:RetainedFileCountLimit`. */
    retainedFileCountLimit: number;
    /** The size a file is rolled at; the runtime's `Logging:FileSizeLimitBytes`. */
    fileSizeLimitBytes: number;
}

/** The runtime's own defaults (`LoggingOptions`). */
export const defaultLimits: LogLimits = { retainedFileCountLimit: 14, fileSizeLimitBytes: 200 * 1024 * 1024 };

/** The holes of a template, in order. */
export function holesOf(template: string): string[] {
    return [...template.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g)].map((match) => match[1] ?? '');
}

const longest = 400;

/** A value as it may stand in a line, or undefined when it may not: a string must be one short line. */
export function admitted(value: unknown): Value | undefined {
    if (typeof value === 'number') return Number.isFinite(value) ? value : undefined;
    if (typeof value === 'boolean') return value;
    if (typeof value !== 'string' || value.length > longest || /[\u0000-\u001f\u007f]/.test(value)) return undefined;
    return value;
}

/** One line as the runtime's compact JSON formatter writes it: `@t`, `@mt`, `@l` unless Information, then the fields. */
export function lineOf(level: Level, name: TemplateName, fields: Readonly<Record<string, unknown>>, at: Date): string {
    const template = templates[name];
    const event: Record<string, Value> = { '@t': at.toISOString(), '@mt': template };
    if (level !== 'Information') event['@l'] = level;
    for (const hole of holesOf(template)) {
        const value = admitted(fields[hole]);
        if (value !== undefined) event[hole] = value;
    }
    event.SourceContext = 'Jason.Desktop';
    event.EventName = name;
    return JSON.stringify(event);
}

const filePattern = /^desktop-(\d{8})(?:_(\d{3,9}))?\.jsonl$/;

/** The day in a file name, as the runtime's sink writes it: the machine's local date. */
export function dayOf(at: Date): string {
    const pad = (value: number) => String(value).padStart(2, '0');
    return `${at.getFullYear()}${pad(at.getMonth() + 1)}${pad(at.getDate())}`;
}

/** The name of a day's file at a roll sequence; 0 is the first file of the day. */
export function fileName(day: string, sequence: number): string {
    return sequence === 0 ? `desktop-${day}.jsonl` : `desktop-${day}_${String(sequence).padStart(3, '0')}.jsonl`;
}

interface Listed {
    name: string;
    day: string;
    sequence: number;
}

function listed(directory: string): Listed[] {
    let names: string[];
    try {
        names = readdirSync(directory);
    } catch {
        return [];
    }
    const files: Listed[] = [];
    for (const name of names) {
        const match = filePattern.exec(name);
        if (match) files.push({ name, day: match[1] ?? '', sequence: match[2] ? Number(match[2]) : 0 });
    }
    return files.sort((a, b) => (a.day === b.day ? a.sequence - b.sequence : a.day < b.day ? -1 : 1));
}

function sizeOf(file: string): number {
    try {
        return statSync(file).size;
    } catch {
        return 0;
    }
}

/**
 * A logger writing into `directory`. A line that cannot be written is lost rather than stopping the application, as a
 * log must never be the reason the desktop does not work.
 */
export function fileLogger(directory: string, limits: LogLimits = defaultLimits, clock: () => Date = () => new Date()): Logger {
    let current: { day: string; sequence: number; size: number } | null = null;

    const place = (at: Date, length: number): string => {
        const day = dayOf(at);
        if (current === null || current.day !== day) {
            const today = listed(directory).filter((file) => file.day === day);
            const last = today[today.length - 1];
            current = { day, sequence: last?.sequence ?? 0, size: last ? sizeOf(join(directory, last.name)) : 0 };
            // A new day's first file is a new file too, and older ones beyond the count go as they do at a roll.
            if (last === undefined) prune();
        }
        if (current.size > 0 && current.size + length > limits.fileSizeLimitBytes) {
            current = { day, sequence: current.sequence + 1, size: 0 };
            prune();
        }
        return join(directory, fileName(current.day, current.sequence));
    };

    const prune = () => {
        const files = listed(directory);
        // The file about to be started counts as one of those kept.
        for (const file of files.slice(0, Math.max(0, files.length - (limits.retainedFileCountLimit - 1)))) {
            try {
                unlinkSync(join(directory, file.name));
            } catch {
                // Kept for the next roll.
            }
        }
    };

    const write = (level: Level, name: TemplateName, fields: Readonly<Record<string, unknown>>) => {
        try {
            const at = clock();
            const line = `${lineOf(level, name, fields, at)}\n`;
            const length = Buffer.byteLength(line);
            mkdirSync(directory, { recursive: true });
            const file = place(at, length);
            appendFileSync(file, line, { encoding: 'utf8', mode: 0o600 });
            if (current) current.size += length;
        } catch {
            // Lost; see above.
        }
    };

    return {
        info: (name, fields) => write('Information', name, fields),
        warn: (name, fields) => write('Warning', name, fields),
        error: (name, fields) => write('Error', name, fields),
    };
}

/** The limits the installation's settings name under `Logging`, where they are valid; the runtime's defaults otherwise. */
export function limitsFrom(settingsText: string | null): LogLimits {
    if (settingsText === null) return defaultLimits;
    let raw: unknown;
    try {
        raw = JSON.parse(settingsText);
    } catch {
        return defaultLimits;
    }
    const section = Object.entries((raw ?? {}) as Record<string, unknown>).find(([key]) => key.toLowerCase() === 'logging')?.[1];
    const value = (name: string): unknown =>
        section && typeof section === 'object' ? Object.entries(section as Record<string, unknown>).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1] : undefined;
    const count = value('RetainedFileCountLimit');
    const size = value('FileSizeLimitBytes');
    return {
        retainedFileCountLimit: Number.isInteger(count) && (count as number) >= 1 ? (count as number) : defaultLimits.retainedFileCountLimit,
        fileSizeLimitBytes: Number.isInteger(size) && (size as number) >= 1024 * 1024 ? (size as number) : defaultLimits.fileSizeLimitBytes,
    };
}
