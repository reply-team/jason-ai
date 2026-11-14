// The Connect cards' writes, byte for byte: one row per card that writes the installation's settings file, each from the
// same file a person wrote and held to the bytes recorded from the desktop before it knew the data directory's layout
// (`fixtures/connect-card-bytes.jsonl`, one line per card, never rewritten). A missing layout stamp is layout 1, and in
// layout 1 every card writes exactly what it wrote before the stamp.

import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { loggedCli } from '../src/installation.ts';
import { ProviderKeys } from '../src/providers.ts';
import { Setup } from '../src/setup.ts';
import { bench, fakeBox, memoryLog, type Bench } from './harness.ts';

function setupOn(b: Bench) {
    const log = memoryLog();
    return new Setup({
        paths: b.paths,
        cli: loggedCli(b.cli, log),
        programs: b.programs,
        keys: new ProviderKeys(b.paths.providerKeys, fakeBox),
        log,
        bundleVersion: '0.34.0-rc.1',
        runtimeVersion: () => '0.34.0-rc.1',
        settings: () => ({ feed: null, skillsDeployedFor: null, theme: null }),
    });
}

const settingsFile = (b: Bench) => b.paths.installationSettings;

/** The file a person wrote: other sections, a key in another letter case, and the three notification plugins configured. */
const written = `{"Dispatcher":{"MaxParallel":2},"routes":{"Campaigns":{}},
"Plugins":{"Grants":{"other":{"Http":["example.test"]},"slack":{"Http":["slack.com"]},"telegram":{"Http":["api.telegram.org"]},"ntfy":{"Http":["ntfy.example.test"]}},
"Config":{"other":{"url":"https://example.test"},"slack":{"token":"xoxb-1"},"telegram":{"token":"123:abc"}}},
"Notifications":{"QuietSeconds":30,"Plugins":{"slack":{"Binding":{"channel":"C0123ABCD"}},"telegram":{"Binding":{"chat":"@team_room"}},"ntfy":{"Binding":{"topic":"jason-alerts","server":"https://ntfy.example.test"}}}}}`;

const rows: readonly { card: string; act: (setup: Setup) => Promise<void> }[] = [
    { card: 'Reply, after a sign-in', act: (setup) => setup.connectReplyWithKey('planted-reply-key') },
    { card: 'Slack, Connect', act: (setup) => setup.configurePlugin('slack', { token: 'xoxb-2', target: 'C0999ZZZZ' }) },
    { card: 'Telegram, Connect', act: (setup) => setup.configurePlugin('telegram', { token: '456:def', target: '-100123' }) },
    { card: 'ntfy, Connect', act: (setup) => setup.configurePlugin('ntfy', { target: 'other-topic', server: 'https://ntfy.other.test' }) },
    { card: 'Journal forwarder, Connect', act: (setup) => setup.configurePlugin('journal-forwarder', { token: 'bearer-1', target: 'https://receiver.example.test/lines' }) },
    { card: 'Slack, Disconnect', act: (setup) => setup.disconnectPlugin('slack') },
    { card: 'Telegram, Disconnect', act: (setup) => setup.disconnectPlugin('telegram') },
    { card: 'ntfy, Disconnect', act: (setup) => setup.disconnectPlugin('ntfy') },
];

/** The bytes each card wrote, recorded once from the desktop before the layout stamp. */
const recorded = new Map(
    readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'fixtures', 'connect-card-bytes.jsonl'), 'utf8')
        .split('\n')
        .filter(Boolean)
        .map((line) => {
            const { card, bytes } = JSON.parse(line) as { card: string; bytes: string };
            return [card, bytes] as const;
        }),
);

test('every card that writes the settings file has its recorded bytes, and nothing else is recorded', () => {
    assert.deepEqual([...recorded.keys()], rows.map((row) => row.card));
});

for (const row of rows) {
    test(`with no layout stamp, ${row.card} writes the settings file byte for byte as it did before the stamp`, async (t) => {
        const b = bench(t, { programs: ['reply'] });
        mkdirSync(dirname(settingsFile(b)), { recursive: true });
        writeFileSync(settingsFile(b), written);
        await row.act(setupOn(b));
        assert.equal(readFileSync(settingsFile(b), 'utf8'), recorded.get(row.card));
    });
}
