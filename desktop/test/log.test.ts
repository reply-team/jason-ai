import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { defaultLimits, fileLines, fileLogger, fileName, heldLimit, holesOf, limitsFrom, lineOf, LogHold, releaseAtQuit, releasedAfter, templates, type Logger } from '../src/log.ts';
import { changeJasonLoggedCodes } from '../src/open-in.ts';

function scratch(t: { after(fn: () => void): void }): string {
    const directory = mkdtempSync(join(tmpdir(), 'jason-desktop-log-'));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    return directory;
}

function lines(directory: string): Record<string, unknown>[] {
    return readdirSync(directory)
        .sort()
        .flatMap((name) => readFileSync(join(directory, name), 'utf8').split('\n').filter(Boolean))
        .map((line) => JSON.parse(line) as Record<string, unknown>);
}

test('the templates are a closed table whose fields are named holes and nothing else', () => {
    for (const [name, template] of Object.entries(templates)) {
        assert.match(name, /^[a-z]+(?:_[a-z]+)*$/, name);
        for (const hole of holesOf(template)) assert.match(hole, /^[A-Z][A-Za-z]*$/, `${name}: ${hole}`);
        // No hole for free text: what a person or a program said has no place to go.
        for (const hole of holesOf(template)) assert.doesNotMatch(hole, /(?:Message|Text|Error|Output|Stderr|Stdout|Key|Token|Prompt|Reason|Url|Value|Args)$/, `${name}: ${hole}`);
    }
});

test('Change Jason itself is logged by where its host came from, and a refusal by the command line\'s code from a closed table', () => {
    assert.deepEqual(holesOf(templates.open_in_change_jason), ['Host', 'Exit']);
    assert.deepEqual(holesOf(templates.open_in_change_jason_refused), ['Host', 'Exit', 'Code']);
    // The host's source reads as a source: the preference, or the host the desktop found.
    assert.equal(templates.open_in_change_jason, 'Change Jason itself was opened, its host from {Host}; jason session open exited {Exit}');
    assert.equal(templates.open_in_change_jason_refused, 'Change Jason itself could not be opened, its host from {Host}; jason session open exited {Exit} with the code {Code}');
    assert.doesNotMatch(templates.open_in_change_jason_refused, /chosen by/);
    assert.deepEqual(changeJasonLoggedCodes, ['host_unknown', 'preference_unreadable', 'host_not_found', 'folder_missing', 'terminal_not_found', 'terminal_start_failed']);
});

test('a line is the runtime’s compact JSON form: @t, @mt, @l unless Information, the holes, the source', () => {
    const at = new Date('2026-11-09T12:00:00.000Z');
    assert.deepEqual(JSON.parse(lineOf('Information', 'cli_exited', { Verb: 'status', Exit: 0, ElapsedMs: 12 }, at)), {
        '@t': '2026-11-09T12:00:00.000Z',
        '@mt': templates.cli_exited,
        Verb: 'status',
        Exit: 0,
        ElapsedMs: 12,
        SourceContext: 'Jason.Desktop',
        EventName: 'cli_exited',
    });
    assert.equal(JSON.parse(lineOf('Warning', 'link_refused', {}, at))['@l'], 'Warning');
});

test('the desktop’s log, read back, carries none of the secrets the desktop was handed', (t) => {
    const directory = scratch(t);
    const log: Logger = fileLogger(directory);
    const planted = ['planted-reply-key-1f9a', 'xoxb-planted-slack-token', 'sk-ant-planted-provider-key', 'a prompt a person typed', 'planted-run-token-77'];
    // Every way a caller could try to put one in: a field that is no hole, a hole given text too long or on two lines.
    const loose = log as unknown as Record<'info' | 'warn' | 'error', (name: string, fields: Record<string, unknown>) => void>;
    loose.info('reply_connected', { Method: 'key', Exit: 0, Key: planted[0], stdin: planted[0] });
    loose.info('plugin_configured', { PluginId: 'slack', TokenChanged: true, TargetChanged: true, ServerChanged: false, Exit: 0, Token: planted[1] });
    loose.info('provider_key_stored', { ProviderId: 'deepseek', key: planted[2] });
    loose.warn('navigation_refused', { Origin: `${planted[3]}\nsecond line` });
    loose.error('window_load_failed', { Step: 'x'.repeat(401) + planted[4], Exit: 3, Message: planted[4] });
    loose.info('cli_exited', { Verb: 'status', Exit: 0, ElapsedMs: 3, Args: ['ui', 'open', planted[4]] });

    const text = readdirSync(directory).map((name) => readFileSync(join(directory, name), 'utf8')).join('');
    for (const secret of planted) assert.equal(text.includes(secret), false, `${secret} reached the log`);
    const read = lines(directory);
    assert.equal(read.length, 6);
    assert.deepEqual(read.map((line) => line.EventName), ['reply_connected', 'plugin_configured', 'provider_key_stored', 'navigation_refused', 'window_load_failed', 'cli_exited']);
});

test('the log is a file a day named as the runtime’s, rolled at the size limit, with the same count kept', (t) => {
    const directory = scratch(t);
    let now = new Date(2026, 10, 9, 10, 0, 0);
    const log = fileLogger(directory, { retainedFileCountLimit: 3, fileSizeLimitBytes: 600 }, () => now);
    for (let index = 0; index < 14; index++) log.info('cli_exited', { Verb: 'status', Exit: 0, ElapsedMs: index });
    const names = readdirSync(directory).sort();
    assert.ok(names.every((name) => /^desktop-20261109(?:_\d{3})?\.jsonl$/.test(name)), names.join(','));
    assert.equal(names.length, 3);
    assert.equal(names.includes(fileName('20261109', 0)), false, 'the oldest file was kept past the count');

    now = new Date(2026, 10, 10, 9, 0, 0);
    log.info('desktop_started', { Version: '0.34.0-rc.1', Platform: 'win32', DataDirectory: directory });
    const after = readdirSync(directory).sort();
    assert.equal(after.length, 3);
    assert.ok(after.includes('desktop-20261110.jsonl'));
});

test('a new logger continues the day’s last file rather than starting over', (t) => {
    const directory = scratch(t);
    const now = new Date(2026, 10, 9, 10, 0, 0);
    writeFileSync(join(directory, 'desktop-20261109_002.jsonl'), '{}\n');
    writeFileSync(join(directory, 'runtime-20261109.jsonl'), '{}\n');
    fileLogger(directory, defaultLimits, () => now).info('link_opened', {});
    assert.equal(readFileSync(join(directory, 'desktop-20261109_002.jsonl'), 'utf8').split('\n').filter(Boolean).length, 2);
    assert.equal(readFileSync(join(directory, 'runtime-20261109.jsonl'), 'utf8'), '{}\n');
});

test('the limits are the runtime’s Logging settings where valid, and its defaults otherwise', () => {
    assert.deepEqual(limitsFrom(null), defaultLimits);
    assert.deepEqual(limitsFrom('{'), defaultLimits);
    assert.deepEqual(limitsFrom(JSON.stringify({ logging: { retainedFileCountLimit: 5, FileSizeLimitBytes: 2 * 1024 * 1024 } })), {
        retainedFileCountLimit: 5,
        fileSizeLimitBytes: 2 * 1024 * 1024,
    });
    assert.deepEqual(limitsFrom(JSON.stringify({ Logging: { RetainedFileCountLimit: 0, FileSizeLimitBytes: 10 } })), defaultLimits);
});

test('the desktop’s default limits are the runtime’s own LoggingOptions defaults, read from its source', () => {
    const source = readFileSync(join(import.meta.dirname, '..', '..', 'dotnet', 'src', 'Jason.Runtime', 'Configuration', 'LoggingOptions.cs'), 'utf8');
    const initial = (property: string) => {
        const text = new RegExp(`\\b${property}\\s*\\{\\s*get;\\s*set;\\s*\\}\\s*=\\s*([^;]+);`).exec(source)?.[1];
        assert.ok(text, `LoggingOptions.${property} has an initial value`);
        return text.split('*').map((factor) => Number(factor.trim().replace(/L$/, ''))).reduce((product, factor) => product * factor, 1);
    };
    assert.equal(defaultLimits.retainedFileCountLimit, initial('RetainedFileCountLimit'));
    assert.equal(defaultLimits.fileSizeLimitBytes, initial('FileSizeLimitBytes'));
});

test('a line that cannot be written is lost, never thrown', (t) => {
    const directory = scratch(t);
    const blocked = join(directory, 'a-file');
    writeFileSync(blocked, '');
    assert.doesNotThrow(() => fileLogger(join(blocked, 'logs')).info('link_opened', {}));
});

test('a held log writes nothing while it is held, then every line in the order it was said, each with its own moment', (t) => {
    const directory = join(scratch(t), 'logs');
    const hold = new LogHold();
    let now = new Date('2026-11-13T10:00:00.000Z');
    const clock = () => now;
    hold.hold();
    // Two loggers on the one hold, as a start has: the early one and the run's, whose file is opened only when it is let go.
    const early = hold.logger(() => fileLines(directory), clock);
    let opened = 0;
    const run = hold.logger(() => (opened++, fileLines(directory)), clock);
    early.info('desktop_started', { Version: '0.41.0', Platform: 'win32', DataDirectory: 'D' });
    now = new Date('2026-11-13T10:00:01.500Z');
    run.warn('demo_unopened', {});
    now = new Date('2026-11-13T10:00:02.250Z');
    early.info('deep_link_opened', { Page: 'feed' });
    assert.throws(() => readdirSync(directory), 'no folder of the log is made while the hold stands');
    assert.equal(opened, 0, 'no file is opened while the hold stands');

    now = new Date('2026-11-13T10:00:09.000Z');
    hold.release();
    run.info('demo_archive_placed', { ElapsedMs: 4200 });
    const read = lines(directory);
    assert.deepEqual(read.map((line) => line.EventName), ['desktop_started', 'demo_unopened', 'deep_link_opened', 'demo_archive_placed']);
    assert.deepEqual(read.map((line) => line['@t']), ['2026-11-13T10:00:00.000Z', '2026-11-13T10:00:01.500Z', '2026-11-13T10:00:02.250Z', '2026-11-13T10:00:09.000Z']);
    assert.equal(opened, 1, 'the run’s file is opened once, when it is let go');
    // A second release writes nothing twice.
    hold.release();
    assert.equal(lines(directory).length, 4);
});

test('a log that is never held writes at once', (t) => {
    const directory = join(scratch(t), 'logs');
    const hold = new LogHold();
    hold.logger(() => fileLines(directory)).info('link_opened', {});
    assert.equal(lines(directory).length, 1);
});

test('a held log keeps at most its limit of lines, and says at the release how many more were said', (t) => {
    const directory = join(scratch(t), 'logs');
    const hold = new LogHold();
    hold.hold();
    const log = hold.logger(() => fileLines(directory));
    for (let index = 0; index < heldLimit + 5; index++) log.info('cli_exited', { Verb: 'status', Exit: 0, ElapsedMs: index });
    hold.release();
    const read = lines(directory);
    assert.equal(read.length, heldLimit + 1);
    assert.equal(read[heldLimit - 1]?.ElapsedMs, heldLimit - 1, 'the first lines are the ones kept, in order');
    assert.equal(read[heldLimit]?.EventName, 'log_held_dropped');
    assert.equal(read[heldLimit]?.Count, 5);
    assert.equal(read[heldLimit]?.Limit, heldLimit);
});

test('a start that throws while the log is held still writes every held line, the one that explains it included', async (t) => {
    const directory = join(scratch(t), 'logs');
    const hold = new LogHold();
    hold.hold();
    const log = hold.logger(() => fileLines(directory));
    await assert.rejects(
        releasedAfter(hold, async () => {
            log.info('desktop_started', { Version: '0.41.0', Platform: 'win32', DataDirectory: 'D' });
            log.error('window_load_failed', { Step: 'load', Exit: -1 });
            throw new Error('the start failed before the placing ended');
        }),
    );
    assert.deepEqual(lines(directory).map((line) => line.EventName), ['desktop_started', 'window_load_failed']);
});

test('a quit while the log is held writes every held line as the application quits', (t) => {
    const directory = join(scratch(t), 'logs');
    const hold = new LogHold();
    hold.hold();
    const listeners: (() => void)[] = [];
    releaseAtQuit({ on: (event, listener) => (assert.equal(event, 'will-quit'), listeners.push(listener)) }, hold);
    hold.logger(() => fileLines(directory)).warn('demo_runtime_stop_timed_out', {});
    assert.throws(() => readdirSync(directory));
    for (const listener of listeners) listener();
    assert.deepEqual(lines(directory).map((line) => line.EventName), ['demo_runtime_stop_timed_out']);
});
