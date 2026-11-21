// A turn the hook rides: Full access, and Ask first and Auto-edit from Claude Code's floor of Full access on. Reads run
// anywhere with no card, and the hook refuses the three kinds whatever tool names them: Jason's settings and run files,
// its data directory outside the chat's own folder, and what Jason sets for the turn. In Ask first and Auto-edit a write
// outside the chat's folders is one card; every jason verb runs with no card but four, which keep theirs whatever their
// spelling; help and jason's reads never card; a compound of no-card parts runs with no card. Below the floor and in Plan
// only nothing changes. Every path here is made up or a temporary one, every command a neutral one or a made-up one.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { askingCardedVerbs, helpNouns, modeFence, readFence } from '../src/chat-deny.ts';
import { claudeArguments, claudeTurnSettings, hookedSettings, planTurnSettings } from '../src/chat-hosts.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import {
    attemptRefusal,
    autoEditJudgement,
    commandRefusal,
    confinedRefusal,
    dataRefusal,
    fullAccessJudgement,
    judge,
    optionRefusal,
    rootHelpRefusal,
    searchRefusal,
    settingsRefusal,
    type ChatScope,
    type Judgement,
    type Platform,
} from '../src/chat-permission-model.ts';
import { entryOf, realPathOf, textOf } from '../src/chat-real-path.ts';

const fence = readFence(join(import.meta.dirname, '..'));
const asking = modeFence(fence, 'ask_first');

/**
 * A scope of Ask first or Auto-edit as a turn the hook rides has it, its data directory the default one under a made-up
 * home folder and its turn in a conversation's own folder; nothing is on disk.
 */
function hookedScope(platform: Platform, home: string, extra: Partial<ChatScope> = {}): ChatScope {
    const paths = platform === 'win32' ? win32 : posix;
    const data = paths.join(home, '.jason');
    const chat = paths.join(data, 'desktop', 'chats', 'c1');
    return {
        platform,
        cwd: chat,
        chat,
        added: [],
        data,
        home,
        protectedFiles: [paths.join(data, 'config', 'settings.json'), paths.join(data, 'run', 'runtime.json')],
        denied: asking.commands,
        deniedOperations: asking.operations,
        help: helpNouns(asking.allowed, asking.reads),
        allowed: asking.allowed,
        hooked: true,
        real: () => null,
        entry: () => null,
        ...extra,
    };
}

const linux = hookedScope('linux', '/home/someone');
const mac = hookedScope('darwin', '/Users/someone');
const windows = hookedScope('win32', 'C:\\Users\\someone');
const scopes: readonly ChatScope[] = [linux, mac, windows];
/** The same scopes as a turn below the floor of Full access has them: no hook rides it. */
const unhooked = (scope: ChatScope): ChatScope => ({ ...scope, hooked: false });

const allow: Judgement = { verdict: 'allow' };
const asked = (judged: Judgement) => (judged.verdict === 'ask' ? judged.title : null);
/** Ask first's and Auto-edit's judgements of one request, in that order. */
const both = (tool: string, input: Record<string, unknown>, scope: ChatScope) => [judge(tool, input, scope), autoEditJudgement(tool, input, scope)];

test('a turn the hook rides carries no block of reads outside its folders, in Full access, Ask first and Auto-edit; below the floor and in Plan only the block stays', () => {
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) {
        const settings = hookedSettings(mode, 'http://127.0.0.1:1/hook', 'secret') as { permissions?: Record<string, unknown> };
        assert.equal(settings.permissions?.['blockReadsOutsideWorkingDirectories'], undefined, mode);
    }
    assert.deepEqual((hookedSettings('ask_first', 'http://127.0.0.1:1/hook', 'secret') as { permissions?: { ask?: string[] } }).permissions?.ask, ['WebFetch(domain:*)']);
    for (const inline of [claudeTurnSettings, planTurnSettings]) {
        assert.equal((JSON.parse(inline) as { permissions: Record<string, unknown> }).permissions['blockReadsOutsideWorkingDirectories'], true);
    }
});

test('a turn the hook rides refuses no jason command for starting with an option at the host: the hook and the model judge it; below the floor and in Plan only the host still refuses it', () => {
    const args = (mode: ChatMode, hooked: boolean) =>
        claudeArguments({ mcpConfig: 'turn.mcp.json', addDirs: [], denied: [], allowed: [], reads: [], mode, ...(hooked ? { settingsFile: 'turn.settings.json' } : {}), dataRules: [], version: '2.1.329' });
    for (const mode of ['full_access', 'ask_first', 'auto_edit'] as const) assert.ok(!args(mode, true).includes('Bash(jason -*)'), mode);
    assert.ok(args('ask_first', false).includes('Bash(jason -*)'), 'below the floor');
    assert.ok(args('plan_only', false).includes('Bash(jason -*)'), 'Plan only');
});

test('Ask first and Auto-edit read a file the person names anywhere with no card, its path with a space and letters of any script, by a ~ and in a folder of its own; below the floor the card stays', () => {
    const reads: [ChatScope, string, Record<string, unknown>][] = [
        [windows, 'Read', { file_path: 'C:\\Users\\someone\\Desktop\\мой файл 1.txt' }],
        [windows, 'Read', { file_path: 'C:/Users/someone/Desktop/1.txt' }],
        [windows, 'Grep', { pattern: 'x', path: 'C:\\Users\\someone\\Downloads' }],
        [windows, 'Glob', { pattern: '*', path: 'C:\\Users\\someone\\Downloads' }],
        [mac, 'Read', { file_path: '/Users/someone/Desktop/1.txt' }],
        [mac, 'Read', { file_path: '~/Desktop/1.txt' }],
        [linux, 'Read', { file_path: '/srv/shared/notes.md' }],
        [linux, 'Glob', { pattern: '~/Downloads/*' }],
        [linux, 'Grep', { pattern: 'x', path: '/home/someone/Documents' }],
    ];
    for (const [scope, tool, input] of reads) {
        assert.deepEqual(both(tool, input, scope), [allow, allow], `${scope.platform} ${tool} ${JSON.stringify(input)}`);
        assert.equal(judge(tool, input, unhooked(scope)).verdict, 'ask', `below the floor: ${scope.platform} ${tool} ${JSON.stringify(input)}`);
    }
});

test('a change outside the chat\'s folders is one card in Ask first and Auto-edit, for Write and Edit alike, an append among them', () => {
    const changes: [ChatScope, string, Record<string, unknown>][] = [
        [windows, 'Write', { file_path: 'C:\\Users\\someone\\Desktop\\notes.txt', content: 'two lines' }],
        [windows, 'Edit', { file_path: 'C:\\Users\\someone\\Desktop\\мой файл 1.txt', old_string: 'end', new_string: 'end\nline one\nline two' }],
        [linux, 'Edit', { file_path: '/home/someone/notes.md', old_string: 'a', new_string: 'a\nb' }],
        [mac, 'Write', { file_path: '/Users/someone/Desktop/notes.txt', content: 'x' }],
    ];
    for (const [scope, tool, input] of changes) {
        assert.deepEqual(both(tool, input, scope).map(asked), ['Change a file outside the chat\'s folders', 'Change a file outside the chat\'s folders'], `${scope.platform} ${tool}`);
    }
});

/** Jason's verbs that run with no card in Ask first and Auto-edit where the hook rides: its reads, its help, and its acts. */
const noCard = [
    'jason status',
    'jason logs',
    'jason runtime status',
    'jason runtime autostart status',
    'jason update check',
    'jason profile list',
    'jason profile get default',
    'jason ui tokens',
    'jason doctor --dry-run',
    'jason doctor',
    'jason journal watch',
    'jason campaign list',
    'jason campaign archive cmp_1 --actor human:someone --reason \'done with it\'',
    'jason campaign set-send-mode cmp_1 --mode auto --actor human:someone --reason \'as asked\'',
    'jason approval set-standing --category intro --actor human:someone --reason \'as asked\'',
    'jason preference set agent.interactive_host codex --actor human:someone --reason \'as asked\'',
    'jason reply record rpl_1 --verdict interested',
    'jason route set campaign.enroll --plugin reply',
    'jason plugin revoke reply --host api.reply.io',
    'jason plugin install reply',
    'jason suppression remove someone@example.com',
    'jason export --out jason.zip',
    'jason runtime stop',
    'jason update apply',
    'jason --help',
    'jason -h',
    'jason --version',
    'jason runtime --help',
    'jason runtime status --help',
    'jason uninstall --help',
    'jason import --help',
    'jason update rollback --help',
    'jason plugin grant --help',
    'jason plugin grant -h --host api.reply.io',
    'jason preference set --help',
    'jason campaign -h',
];

/** The four verbs that keep a card in Ask first and Auto-edit where the hook rides, every form of each. */
const carded = [
    'jason uninstall',
    'jason uninstall --purge-data',
    'jason import jason.zip',
    'jason import jason.zip --replace',
    'jason update rollback',
    'jason plugin grant reply --host api.reply.io',
];

test('in Ask first and Auto-edit every jason verb runs with no card, its help and its reads among them, but four: uninstall, import, update rollback and plugin grant, each a card in every form', () => {
    assert.deepEqual([...askingCardedVerbs], ['uninstall', 'import', 'update rollback', 'plugin grant']);
    for (const scope of scopes) {
        for (const command of noCard) assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        for (const command of carded) assert.deepEqual(both('Bash', { command }, scope).map(asked), ['Run a command', 'Run a command'], `${scope.platform}: ${command}`);
    }
});

test('the four carded verbs keep their card whatever their spelling and in any compound, --help aside: never a run with no card', () => {
    const spellings = [
        'jason  update   rollback',
        'jason update --verbose rollback',
        'jason UPDATE rollback',
        'jason Uninstall',
        'jason upd\\ate rollback',
        "jason plugin 'grant' reply",
        '"jason" uninstall',
        "ja''son import x.zip",
        'jason status && jason uninstall',
        'jason uninstall; echo done',
        'echo yes | jason uninstall',
        'jason campaign list | jason import -',
        'jason update check || jason update rollback',
        'jason plugin grant reply --host x | head -n 1',
        'sh -c "jason uninstall"',
        'command jason uninstall',
        'env jason uninstall',
        'xargs jason uninstall',
        'jason.exe uninstall',
        '/usr/local/bin/jason uninstall',
        'jason plugin grant reply --help',
    ];
    for (const scope of scopes) {
        for (const command of spellings) {
            for (const judged of both('Bash', { command }, scope)) assert.notDeepEqual(judged, allow, `${scope.platform}: ${command}`);
        }
    }
});

test('a jason command that starts with any other option, or gives a word that begins with @, is refused as before; below the floor and in Plan only jason\'s own help is refused as before', () => {
    for (const scope of scopes) {
        assert.deepEqual(judge('Bash', { command: 'jason --actor human:someone approval approve apr_1' }, scope), { verdict: 'deny', message: optionRefusal });
        assert.deepEqual(judge('Bash', { command: 'jason campaign list @words' }, scope), { verdict: 'deny', message: optionRefusal });
        assert.deepEqual(judge('Bash', { command: 'jason --help' }, unhooked(scope)), { verdict: 'deny', message: rootHelpRefusal });
        assert.equal(asked(judge('Bash', { command: 'jason runtime status' }, unhooked(scope))), 'Run a command', 'below the floor runtime status is a card, as before');
    }
});

test('a pipeline or a sequence of no-card jason forms and filters of the closed list, with no redirect into a file, runs with no card; anything else compound is a card', () => {
    const runs = [
        "jason campaign list | jq '.items | length'",
        'jason workitem list | head -n 5 | wc -l',
        'jason status && jason logs',
        'jason runtime status; jason update check',
        'jason journal list | grep reply | sort | cut -c1-80',
        'jason campaign list > /dev/null && echo listed',
        'jason contact list | tr a-z A-Z | tail -n 3',
    ];
    const cards = [
        'jason campaign list > campaigns.json',
        'jason campaign list | tee campaigns.json',
        'jason campaign list | sort -o sorted.json',
        "jason campaign list | sed 's/a/b/'",
        "jason campaign list | awk '{print $1}'",
        'jason status && frobnicate',
        'cd /tmp && jason status',
        'jason campaign list | xargs echo',
    ];
    for (const scope of scopes) {
        for (const command of runs) assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        for (const command of cards) assert.deepEqual(both('Bash', { command }, scope).map((judged) => judged.verdict), ['ask', 'ask'], `${scope.platform}: ${command}`);
        assert.equal(judge('Bash', { command: runs[0] as string }, unhooked(scope)).verdict, 'ask', 'below the floor a compound is a card, as before');
    }
});

test('the host\'s read-only commands read anywhere with no card in Ask first and Auto-edit, a listing of the home folder among them; a walk of a folder that holds the data directory is refused, by the model and by the hook', () => {
    for (const command of ['ls ~/Downloads', 'ls /home/someone', 'cat /home/someone/Desktop/1.txt', 'head -n 20 /srv/shared/notes.md', 'wc -l /home/someone/notes.md']) {
        assert.deepEqual(both('Bash', { command }, linux), [allow, allow], command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'runs' }, command);
    }
    for (const command of ['grep -r token /home/someone', 'grep -rl token ~', 'ls -R ~', 'find ~ -name settings.json', 'du -sh /home/someone', 'tar czf all.tgz /home/someone', 'cp -r ~ /tmp/copy']) {
        for (const judged of both('Bash', { command }, linux)) assert.deepEqual(judged, { verdict: 'deny', message: searchRefusal }, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'refused', message: searchRefusal }, command);
    }
    for (const [tool, input] of [
        ['Grep', { pattern: 'token', path: '/home/someone' }],
        ['Glob', { pattern: '**/*.json', path: '/home/someone' }],
        ['Glob', { pattern: '/home/someone/**/*.json' }],
    ] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, linux), { outcome: 'refused', message: searchRefusal }, `${tool} ${JSON.stringify(input)}`);
        assert.equal(judge(tool, input, linux).verdict, 'deny', `${tool} ${JSON.stringify(input)}`);
    }
});

/** Each spelling of each of the three kinds' places, per platform: as written, by a ~, relative to the chat's folder, and as the platform also spells it. */
function placesOf(scope: ChatScope): { settings: string[]; data: string[] } {
    if (scope.platform === 'win32') {
        return {
            settings: [
                'C:\\Users\\someone\\.jason\\config\\settings.json',
                '~/.jason/config/settings.json',
                '..\\..\\..\\config\\settings.json',
                'C:/USERS/SOMEONE/.JASON/CONFIG/SETTINGS.JSON',
                '\\\\?\\C:\\Users\\someone\\.jason\\config\\settings.json',
                'C:\\Users\\someone\\.jason.\\config\\settings.json',
            ],
            data: ['C:\\Users\\someone\\.jason\\desktop\\conversations', '~\\.jason\\state', '..\\c2', 'c:/users/someone/.jason/desktop/chats/c2'],
        };
    }
    const home = scope.home;
    const volume = scope.platform === 'darwin' ? [`/System/Volumes/Data${home}/.jason/config/settings.json`, `${home.toLowerCase()}/.JASON/config/settings.json`] : [`${home}/./.jason//config/settings.json`];
    return {
        settings: [`${home}/.jason/config/settings.json`, '~/.jason/config/settings.json', '../../../config/settings.json', ...volume],
        data: [`${home}/.jason/desktop/conversations`, '~/.jason/state', '../c2', `${home}/x/../.jason/desktop/chats/c2`],
    };
}

/** What each tool is given for a place. */
const shapes: readonly [string, (path: string) => Record<string, unknown>][] = [
    ['Read', (path) => ({ file_path: path })],
    ['Edit', (path) => ({ file_path: path, old_string: 'a', new_string: 'b' })],
    ['Write', (path) => ({ file_path: path, content: 'x' })],
    ['Glob', (path) => ({ pattern: '*', path })],
    ['Grep', (path) => ({ pattern: 'x', path })],
    ['Bash', (path) => ({ command: `cat '${path}'` })],
];

test('the three kinds are refused in every mode the hook rides, by the hook and by the model, for Read, Edit, Write, Glob, Grep and Bash alike, written plainly, by a ~, relative to the chat\'s folder and as the system also spells the same folder', () => {
    for (const scope of scopes) {
        const { settings, data } = placesOf(scope);
        for (const [tool, input] of shapes) {
            for (const [paths, sentence] of [[settings, settingsRefusal], [data, dataRefusal]] as const) {
                for (const path of paths) {
                    const name = `${scope.platform} ${tool} ${path}`;
                    // The hook's judgement is one in every mode it rides (`fullAccessJudgement`).
                    const hook = fullAccessJudgement(tool, input(path), scope);
                    assert.equal(hook.outcome, 'refused', name);
                    if (tool !== 'Glob' || sentence === dataRefusal) assert.deepEqual(hook, { outcome: 'refused', message: sentence }, name);
                    for (const judged of both(tool, input(path), scope)) assert.equal(judged.verdict, 'deny', name);
                }
            }
        }
    }
});

test('the three kinds are refused through a link or a junction, from outside the data directory and from inside the chat\'s own folder, by the hook and by the model, for every tool', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-hooked-'));
    t.after(() => rmSync(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }));
    const home = realPathOf(root) ?? root;
    const data = join(home, '.jason');
    const chat = join(data, 'desktop', 'chats', 'c1');
    for (const folder of [join(data, 'config'), join(data, 'run'), chat, join(home, 'Desktop')]) mkdirSync(folder, { recursive: true });
    writeFileSync(join(data, 'config', 'settings.json'), '{}');
    const kind = process.platform === 'win32' ? 'junction' : 'dir';
    symlinkSync(data, join(home, 'Desktop', 'shortcut'), kind);
    symlinkSync(join(data, 'config'), join(chat, 'out'), kind);
    const platform = process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
    const scope = hookedScope(platform, home, { real: realPathOf, entry: entryOf, text: textOf });
    for (const path of [join(home, 'Desktop', 'shortcut', 'config', 'settings.json'), join(chat, 'out', 'settings.json'), join('out', 'settings.json')]) {
        for (const [tool, input] of shapes) {
            if (tool === 'Glob') continue;
            const name = `${tool} ${path}`;
            assert.deepEqual(fullAccessJudgement(tool, input(path), scope), { outcome: 'refused', message: settingsRefusal }, name);
            for (const judged of both(tool, input(path), scope)) assert.equal(judged.verdict, 'deny', name);
        }
    }
    for (const [tool, input] of [['Glob', { pattern: '*', path: join(home, 'Desktop', 'shortcut') }], ['Grep', { pattern: 'x', path: join(home, 'Desktop', 'shortcut') }], ['Glob', { pattern: 'out/*' }]] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, scope), { outcome: 'refused', message: dataRefusal }, `${tool} ${JSON.stringify(input)}`);
    }
});

test('a role\'s attempt and the chat\'s own variables stay refused where the hook rides: JASON_CHAT_TURN, JASON_CONFINED and JASON_ATTEMPT_ID, by the hook and by the model', () => {
    for (const scope of scopes) {
        for (const [command, sentence] of [
            ['JASON_CHAT_TURN=0 jason approval approve apr_1', confinedRefusal],
            ['export JASON_CHAT_TURN=0 && jason campaign start cmp_1', confinedRefusal],
            ['env -u JASON_CONFINED jason status', confinedRefusal],
            ['JASON_ATTEMPT_ID=att_1 jason workitem complete wi_1', attemptRefusal],
        ] as const) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.deepEqual(judged, { verdict: 'deny', message: sentence }, `${scope.platform}: ${command}`);
        }
    }
});

test('Plan only is as it was: the deny list refused, a read outside the folders the card Plan only answers, and no jason verb of the deny list run', () => {
    const plan: ChatScope = { ...linux, hooked: false, denied: fence.commands, deniedOperations: fence.operations, allowed: undefined };
    for (const command of ['jason uninstall', 'jason approval approve apr_1', 'jason runtime status']) assert.deepEqual(judge('Bash', { command }, plan), { verdict: 'deny', message: commandRefusal }, command);
    assert.equal(asked(judge('Read', { file_path: '/srv/shared/notes.md' }, plan)), 'Read a file outside the chat\'s folders');
});

test('the settings and the data refusals say what is closed, that it is Jason\'s own and not one of the person\'s files, and which jason verb does the thing instead', () => {
    for (const sentence of [settingsRefusal, dataRefusal]) {
        assert.match(sentence, /Jason's own/, sentence);
        assert.match(sentence, /not one of the person's files/, sentence);
        assert.match(sentence, /the chat does not open it/, sentence);
    }
    assert.match(settingsRefusal, /`jason status`/);
    assert.match(settingsRefusal, /`jason route set` and `jason plugin grant`/);
    assert.match(dataRefusal, /`jason campaign list`/);
    assert.match(searchRefusal, /holds Jason's own data directory/);
});
