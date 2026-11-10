import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { defaultLimits, fileLogger, fileName, holesOf, limitsFrom, lineOf, templates, type Logger } from '../src/log.ts';

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

test('a line that cannot be written is lost, never thrown', (t) => {
    const directory = scratch(t);
    const blocked = join(directory, 'a-file');
    writeFileSync(blocked, '');
    assert.doesNotThrow(() => fileLogger(join(blocked, 'logs')).info('link_opened', {}));
});
