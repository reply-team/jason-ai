// A turn the hook rides: Full access, and Ask first and Auto-edit from Claude Code's floor of Full access on. Reads run
// anywhere with no card, and the hook refuses the three kinds whatever tool names them: Jason's settings and run files,
// its data directory outside the chat's own folder, and what Jason sets for the turn. In Ask first and Auto-edit a write
// outside the chat's folders is one card; every jason verb runs with no card but the carded forms, which keep theirs
// whatever their spelling; help and jason's reads never card; a compound of no-card parts runs with no card. A folder
// that holds the data directory is walked in no form but those known not to descend. Below the floor and in Plan only
// nothing changes. Every path here is made up or a temporary one, every command a neutral one or a made-up one.

import assert from 'node:assert/strict';
import { linkSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { test } from 'node:test';
import { askingCardedForms, helpNouns, modeFence, readFence } from '../src/chat-deny.ts';
import { claudeArguments, claudeTurnSettings, hookedSettings, planTurnSettings } from '../src/chat-hosts.ts';
import type { ChatMode } from '../src/chat-modes.ts';
import {
    attemptRefusal,
    autoEditJudgement,
    braceRefusal,
    commandRefusal,
    confinedRefusal,
    dataRefusal,
    filesFromRefusal,
    fullAccessJudgement,
    jqRefusal,
    judge,
    lookupRefusal,
    noCardFilters,
    optionRefusal,
    ownToolRefusal,
    personsRefusal,
    rootHelpRefusal,
    inRootRefusal,
    searchRefusal,
    settingsRefusal,
    type ChatScope,
    type Judgement,
    type Platform,
} from '../src/chat-permission-model.ts';
import { entryOf, identityOf, realPathOf, textOf } from '../src/chat-real-path.ts';

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
        [mac, 'Write', { file_path: '~/Desktop/notes.txt', content: 'x' }],
        [windows, 'Edit', { file_path: '~\\Desktop\\1.txt', old_string: 'a', new_string: 'a\nb' }],
    ];
    for (const [scope, tool, input] of changes) {
        assert.deepEqual(both(tool, input, scope).map(asked), ['Change a file outside the chat\'s folders', 'Change a file outside the chat\'s folders'], `${scope.platform} ${tool} ${JSON.stringify(input)}`);
        // Its card shows where the change lands, the home folder's ~ taken as the file tools take it.
        const judged = judge(tool, input, scope);
        assert.ok(judged.verdict === 'ask' && !judged.detail.startsWith('~'), JSON.stringify(judged));
        assert.equal(judge(tool, input, unhooked(scope)).verdict, 'ask', 'below the floor a card, as before');
    }
    // Inside the chat's folders as before, in both modes.
    for (const [tool, input] of [['Write', { file_path: 'notes.txt', content: 'x' }], ['Edit', { file_path: 'notes.txt', old_string: 'a', new_string: 'b' }], ['Write', { file_path: '.github/workflows/x.yml', content: 'x' }]] as const) {
        assert.deepEqual(both(tool, input, linux), both(tool, input, unhooked(linux)), `${tool} ${JSON.stringify(input)}`);
    }
});

test('a memory file of the person\'s is shown first in Ask first and Auto-edit, each time, in a campaign\'s folder the turn works in and outside it alike, and Full access writes it', () => {
    // The client's file at the harness root, which no turn works in, and a campaign's in its folder, which a campaign's
    // conversation adds to the turn.
    const campaign = '/home/someone/Jason/projects/p/campaigns/c';
    const inCampaign = { ...linux, added: [campaign] };
    const files = ['/home/someone/Jason/AGENTS.md', `${campaign}/AGENTS.md`];
    for (const file of files) {
        for (const [tool, input] of [['Write', { file_path: file, content: '- Write in a short way.\n' }], ['Edit', { file_path: file, old_string: 'end', new_string: 'end\n- Write in a short way.' }]] as const) {
            const where = `${tool} ${file}`;
            for (const judged of both(tool, input, inCampaign)) {
                assert.ok(judged.verdict === 'ask' && judged.title === 'Change instructions the AI reads' && judged.remember === null, `${where}: ${JSON.stringify(judged)}`);
            }
            assert.deepEqual(fullAccessJudgement(tool, input, inCampaign), { outcome: 'runs' }, `Full access: ${where}`);
        }
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
    "jason profile create local-claude --host claude --description 'for the night runs' --tool web_search",
    'jason profile update local-claude --arg --model --arg opus --deny Bash --tool web_search --host-version-verified 2.1.322',
    "jason profile update local-claude --description 'for the night runs' --actor human:someone --reason 'as asked'",
    'jason role set-profile researcher --execution-profile local-claude',
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
    'jason runtime stop',
    'jason update apply',
    'jason update apply --drain-seconds 30',
    'jason update apply --finish',
    'jason update check --feed https://example.com/manifest.json',
    'jason ui install',
    'jason install',
    'jason install --roles',
    "jason install --terminal-path ''",
    "jason install --terminal-path=''",
    "jason install --roles --terminal-path ''",
    'jason skills install',
    'jason skills install --host claude --pack business',
    'jason skills update',
    'jason skills update --force',
    'jason --help',
    'jason -h',
    'jason --version',
    'jason runtime --help',
    'jason runtime status --help',
    'jason uninstall --help',
    'jason uninstall --purge-data --help',
    'jason import --help',
    'jason export --help',
    'jason update rollback --help',
    'jason update apply --version 0.40.0 --help',
    'jason plugin grant --help',
    'jason plugin grant -h --host api.reply.io',
    'jason plugin grant reply --help',
    'jason plugin install reply --replace --help',
    'jason skills install --source packs -h',
    'jason preference set --help',
    'jason campaign -h',
];

/** The forms that keep a card in Ask first and Auto-edit where the hook rides. */
const carded = [
    'jason uninstall',
    'jason uninstall --purge-data',
    'jason import jason.zip',
    'jason import jason.zip --replace',
    'jason export',
    'jason export --out jason.zip',
    'jason export --stop',
    'jason update rollback',
    'jason update apply --version 0.40.0',
    'jason update apply --version=0.40.0',
    'jason plugin grant reply --host api.reply.io',
    'jason plugin install reply --feed https://example.com/manifest.json',
    'jason plugin install reply --replace',
    'jason skills install --source packs',
    'jason skills install --feed https://example.com/manifest.json',
    'jason skills install --root harness',
    'jason skills update --feed https://example.com/manifest.json',
    'jason skills update --root harness',
    'jason skills update --source packs',
    'jason update apply --feed https://example.com/manifest.json',
    'jason update apply --feed=https://example.com/manifest.json',
    'jason update apply --finish --feed https://example.com/manifest.json',
    'jason skills install --ref v0.40.0',
    'jason skills install --ref=v0.40.0',
    'jason skills update --ref v0.40.0',
    'jason ui install --feed https://example.com/manifest.json',
    'jason ui install --feed=https://example.com/manifest.json',
    'jason install --retire /opt/jason/jason',
    'jason install --terminal-path /usr/bin --retire /opt/jason/jason',
    'jason profile create local-claude --host claude --program claude',
    'jason profile create local-claude --program=claude',
    'jason profile update local-claude --program claude',
    'jason profile update local-claude --program:claude',
    'jason profile create local-claude --host claude --cli-command jason',
    'jason profile update local-claude --cli-command=jason',
    'jason profile create local-claude --file profile.json',
    'jason profile update local-claude --file=profile.json',
];

test('in Ask first and Auto-edit every jason verb runs with no card, its help and its reads among them, but the carded forms: uninstall, import, export, update rollback and plugin grant in every form, and a verb that changes the installation in a form that names another source, place, ref or version, its plain form running with no card', () => {
    assert.deepEqual(askingCardedForms.map(({ verb, options }) => [verb, [...(options ?? [])]]), [
        ['uninstall', []],
        ['import', []],
        ['export', []],
        ['update rollback', []],
        ['update apply', ['--version', '--feed']],
        ['plugin grant', []],
        ['plugin install', ['--feed', '--replace']],
        ['skills install', ['--source', '--feed', '--root', '--ref']],
        ['skills update', ['--source', '--feed', '--root', '--ref']],
        ['ui install', ['--feed']],
        ['install', ['--retire']],
        ['profile create', ['--program', '--cli-command', '--file']],
        ['profile update', ['--program', '--cli-command', '--file']],
    ]);
    for (const scope of scopes) {
        // The search path a terminal has names no source: `install --terminal-path` runs with no card, in a spelling of the platform's own.
        const terminalPaths = scope.platform === 'win32'
            ? ["jason install --terminal-path 'C:\\Windows\\System32'", "jason install --terminal-path='C:\\Windows\\System32;C:\\Windows'"]
            : ['jason install --terminal-path /usr/bin:/bin', 'jason install --terminal-path=/usr/local/bin:/usr/bin'];
        for (const command of [...noCard, ...terminalPaths]) assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        for (const command of carded) assert.deepEqual(both('Bash', { command }, scope).map(asked), ['Run a command', 'Run a command'], `${scope.platform}: ${command}`);
    }
});

test('the carded forms keep their card whatever their spelling and in any compound, --help aside and a --help after `--` not: never a run with no card', () => {
    const spellings = [
        'jason EXPORT out.zip',
        'jason status && jason export out.zip',
        'jason update apply --VERSION 0.40.0',
        'jason update apply --version:0.40.0',
        'jason update --verbose apply --version 0.40.0',
        'jason plugin install reply --Feed https://example.com/manifest.json',
        'jason plugin install reply --replace=true',
        'jason skills --verbose install --source packs',
        'jason skills install --root=harness',
        'jason update apply --FEED https://example.com/manifest.json',
        'jason update apply --feed:https://example.com/manifest.json',
        'jason update --verbose apply --finish --feed https://example.com/manifest.json',
        'jason skills install --REF v0.40.0',
        'jason skills install --ref:v0.40.0',
        'jason skills update --Ref=v0.40.0',
        'jason ui install --FEED=https://example.com/manifest.json',
        'jason status && jason ui install --feed https://example.com/manifest.json',
        'jason install --Retire=/opt/jason/jason',
        'echo y | jason skills update --feed https://example.com/manifest.json',
        'jason update apply --version 0.40.0 -- --help',
        'jason export -- --help',
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
        'jason profile update local-claude --PROGRAM claude',
        'jason profile update local-claude --Program=claude',
        'jason profile create local-claude --PROGRAM:claude',
        'jason PROFILE Create local-claude --program claude',
        'jason profile --verbose update local-claude --program claude',
        'jason status && jason profile update local-claude --program claude',
        "jason profile create local-claude --description 'x' --program claude --reason 'as asked'",
        'jason profile update local-claude --CLI-Command jason',
        'jason profile create local-claude --File=profile.json',
        "echo '{}' | jason profile create local-claude --file -",
    ];
    for (const scope of scopes) {
        for (const command of spellings) {
            for (const judged of both('Bash', { command }, scope)) assert.notDeepEqual(judged, allow, `${scope.platform}: ${command}`);
        }
    }
});

test('a jason whose words do not begin with a verb of the CLI\'s own tree, or a noun\'s help, is a card in Ask first and Auto-edit, whatever stands before the noun, and so is every mode of the executable that is no verb; Full access runs each', () => {
    const cards = [
        'jason jason uninstall',
        'jason ./jason uninstall',
        'jason /any/folder/jason uninstall',
        "jason '[a]' uninstall",
        "jason '[a b]' uninstall",
        "jason '' uninstall",
        'jason jason status',
        'jason campaign',
        'jason campaign frobnicate',
        'jason frobnicate',
        'jason runtime run',
        'jason plugin-host --protocol 1 --plugin p --operation o --correlation c',
        'jason toast-host',
        'jason host-hook',
        'jason jason://approvals/apr_1',
    ];
    for (const scope of scopes) {
        for (const command of cards) {
            assert.deepEqual(both('Bash', { command }, scope).map((judged) => judged.verdict), ['ask', 'ask'], `${scope.platform}: ${command}`);
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `Full access: ${scope.platform}: ${command}`);
        }
    }
});

test('every form of askingCardedForms is named in rule 11 of the chat\'s instructions, in desktop/README.md and in docs/desktop.md, each with every option that cards it and with no other', () => {
    const desktop = join(import.meta.dirname, '..');
    const rule = readFileSync(join(desktop, 'chat-instructions.md'), 'utf8').split('\n').find((line) => line.startsWith('11. ')) ?? '';
    for (const [name, text] of [['rule 11', rule], ['desktop/README.md', readFileSync(join(desktop, 'README.md'), 'utf8')], ['docs/desktop.md', readFileSync(join(desktop, '..', 'docs', 'desktop.md'), 'utf8')]] as const) {
        const flat = text.replace(/\s+/g, ' ');
        for (const { verb, options } of askingCardedForms) {
            const mentions = [...flat.matchAll(new RegExp(`\`(?:jason )?${verb}\``, 'g'))].map((match) => match.index);
            assert.ok(mentions.length > 0, `${name} names ${verb}`);
            const segments = mentions.map((at) => flat.slice(at).split(/; |\. /)[0] as string);
            const named = (segment: string) => [...segment.matchAll(/`(--[a-z][a-z-]*)`/g)].map((match) => match[1] as string);
            assert.ok(
                options === undefined || segments.some((segment) => options.every((option) => segment.includes(`\`${option}\``)) && named(segment).every((option) => options.includes(option))),
                `${name} names ${verb} with ${(options ?? []).join(', ')} and no other option`,
            );
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

test('the host\'s read-only commands read anywhere with no card in Ask first and Auto-edit, a listing of the home folder among them; a walk of a folder that holds the data directory is refused by the hook, and the model\'s card each time', () => {
    for (const command of ['ls ~/Downloads', 'ls /home/someone', 'cat /home/someone/Desktop/1.txt', 'head -n 20 /srv/shared/notes.md', 'wc -l /home/someone/notes.md']) {
        assert.deepEqual(both('Bash', { command }, linux), [allow, allow], command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'runs' }, command);
    }
    for (const command of ['grep -r token /home/someone', 'grep -rl token ~', 'ls -R ~', 'find ~ -name settings.json', 'du -sh /home/someone', 'tar czf all.tgz /home/someone', 'cp -r ~ /tmp/copy']) {
        for (const judged of both('Bash', { command }, linux)) assert.deepEqual(judged, { verdict: 'ask', title: 'Run a command', detail: command, remember: null }, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, linux), { outcome: 'refused', message: searchRefusal }, command);
        assert.equal(judge('Bash', { command }, unhooked(linux)).verdict, 'ask', `below the floor a card, as before: ${command}`);
    }
    for (const [tool, input, title] of [
        ['Grep', { pattern: 'token', path: '/home/someone' }, 'Search a folder outside the chat\'s folders'],
        ['Glob', { pattern: '**/*.json', path: '/home/someone' }, 'Find files in a folder outside the chat\'s folders'],
        ['Glob', { pattern: '/home/someone/**/*.json' }, 'Find files in a folder outside the chat\'s folders'],
    ] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, linux), { outcome: 'refused', message: searchRefusal }, `${tool} ${JSON.stringify(input)}`);
        for (const judged of both(tool, input, linux)) assert.deepEqual(judged, { verdict: 'ask', title, detail: '/home/someone', remember: null }, `${tool} ${JSON.stringify(input)}`);
        assert.deepEqual(judge(tool, input, unhooked(linux)), { verdict: 'deny', message: searchRefusal }, `below the floor refused, as before: ${tool} ${JSON.stringify(input)}`);
    }
});

test('a double-quoted Windows home path keeps its backslashes, as a POSIX shell keeps one before any letter but a dollar, a backquote, a quote, a backslash and a line end: each walker of it and a removal of it are refused by the hook, and the model\'s card each time', () => {
    for (const command of [
        'grep -r api_key "C:\\Users\\someone"',
        'ls -R "C:\\Users\\someone"',
        'du -a "C:\\Users\\someone"',
        'find "C:\\Users\\someone" -name settings.json',
        'tar -cf x.tar "C:\\Users\\someone"',
        'cp -r "C:\\Users\\someone" x',
        'rm -rf "C:\\Users\\someone"',
        'grep -r api_key "C:\\Users\\someone\\\\"',
    ]) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), { outcome: 'refused', message: searchRefusal }, command);
        for (const judged of both('Bash', { command }, windows)) assert.equal(judged.verdict, 'ask', command);
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'cat "C:\\Users\\someone\\.jason\\config\\settings.json"' }, windows), { outcome: 'refused', message: settingsRefusal });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'ls "C:\\Users\\someone\\Downloads"' }, windows), { outcome: 'runs' });
    assert.deepEqual(fullAccessJudgement('Bash', { command: 'echo "a \\$b \\"c\\" d"' }, windows), { outcome: 'runs' });
});

test('a value joined to a one-dash option is no folder to the walk but a folder change\'s and a copy\'s target; a look at a path runs on such a folder; a copy or a move into one stays refused', () => {
    for (const scope of scopes) {
        for (const command of ["awk -F/ '{print $1}' notes.txt", "awk -F, '{print $NF}' data.csv", "python3 -c 'import os; print(os.environ[\"X\"])'", 'column -s/ -t notes.txt', 'cut -d/ -f2 notes.txt', 'realpath ~', 'stat ~', 'stat /', 'basename ~', 'dirname ~/notes.txt', 'df -h ~']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
        }
        for (const command of ['tar -C/ -cf x.tar .', 'git -C/ archive -o x.zip HEAD', 'cp -t/ notes.txt', 'cp notes.txt ~', 'mv notes.txt ~/', 'cp -r notes ~']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: searchRefusal }, `${scope.platform}: ${command}`);
        }
    }
});

test('a script the agent writes is walked as its lines would run: a walk of a folder that holds the data directory in it, and a program its lines run in one, are refused as the command would be; a script of plain forms is written', () => {
    for (const scope of scopes) {
        for (const [input, sentence] of [
            [{ file_path: 'go.sh', content: '#!/bin/sh\ndu -sh ~\n' }, searchRefusal],
            [{ file_path: 'go.sh', content: '#!/bin/sh\ncd ~\nfrobnicate\n' }, inRootRefusal],
            [{ file_path: 'Makefile', content: 'all:\n\ttar -cf x.tar /\n' }, searchRefusal],
            [{ file_path: 'package.json', content: '{"scripts":{"backup":"cp -r ~ /tmp/copy"}}' }, searchRefusal],
        ] as const) {
            assert.deepEqual(fullAccessJudgement('Write', input, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${input.content}`);
        }
        assert.deepEqual(fullAccessJudgement('Write', { file_path: 'go.sh', content: '#!/bin/sh\nls ~\ncat ~/Desktop/notes.txt\n' }, scope), { outcome: 'runs' }, scope.platform);
    }
});

test('a word that begins with a variable the reading does not know is refused by the hook in every mode it rides in a form known to descend, remove or copy a tree, each positional spelling alike, and runs in any other form; a variable the text gives a literal value is read as that value; the profile\'s drive, its path, the system drive and the folder PowerShell is asked for name a folder that holds the data directory', () => {
    for (const scope of scopes) {
        const trees = [
            'grep -r api_key "$X"',
            'grep -r k "$X"',
            'du -sh "$X"',
            'rsync -a "$X" /tmp/copy',
            'ls -R $X',
            'cp -r ${X} copy',
            'cp -r "$X" y',
            'rm -rf "$X"',
            'zip -r x.zip "${X:-.}"',
            'find "$X" -name settings.json',
            'tar -czf x.tgz "$X"',
            'chmod -R 700 "$X"',
            'for d in ~ /; do du -sh "$d"; done',
            'd=~; tar -cf x.tar "$d"',
            'rm -rf "$1"',
            'rm -rf "${1}"',
            'rm -rf "$@"',
            'rm -rf "${@}"',
            'rm -rf "$*"',
            'rm -rf "${*}"',
        ];
        for (const command of trees) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: searchRefusal }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'allow', `${scope.platform}: ${command}`);
        }
        const runs = [
            'ls "$X"',
            'cat "$X"',
            'grep api_key "$X"',
            'grep -n TODO "$f"',
            'git commit -m "$MSG"',
            'git push origin "$BRANCH"',
            'curl "$URL/x"',
            'curl -d "$BODY" http://127.0.0.1:4011/v1/campaign.list',
            'mkdir -p "$TMPDIR/x"',
            'eval "$CMD"',
            'echo "$X"',
            'head -n 5 "$X"',
            'd=~/Documents; du -sh "$d"',
            'for f in a.txt b.txt; do wc -l "$f"; done',
            'test -d "$X" && echo $?',
            'd=~/Documents; ls "$d"',
            'for f in $(git ls-files); do git log -1 "$f"; done',
            'git log -1 "$1"',
            'git log -1 "${1}"',
            'git add "$@"',
            'git add "${@}"',
            'git add "$*"',
            'git add "${*}"',
        ];
        for (const command of runs) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'deny', `${scope.platform}: ${command}`);
        }
        // The jason loops of acceptance line 10, the ids taken from a capture and from a pipe: run, never refused.
        for (const command of [
            "ID=$(jason campaign create --name 'Spring' | jq -r .id); jason campaign start \"$ID\" --actor human:someone --reason 'as asked'",
            "jason campaign list | jq -r '.items[].id' | while read id; do jason campaign archive \"$id\" --actor human:someone --reason 'as asked'; done",
        ]) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'deny', `${scope.platform}: ${command}`);
        }
        // A script the agent writes that passes a variable to a program that descends no tree is written; one that removes a tree a variable names is not.
        const script = '#!/bin/sh\ngit commit -m "$1"\ngit push origin "${BRANCH}"\ncurl "$URL/x"\n';
        assert.deepEqual(fullAccessJudgement('Write', { file_path: 'go.sh', content: script }, scope), { outcome: 'runs' }, scope.platform);
        assert.deepEqual(fullAccessJudgement('Write', { file_path: 'go.sh', content: '#!/bin/sh\nrm -rf "$1"\n' }, scope), { outcome: 'refused', message: searchRefusal }, scope.platform);
    }
    for (const command of [
        'Get-ChildItem $env:SystemDrive -Recurse',
        'Get-ChildItem $env:SystemDrive\\ -Recurse',
        'Get-ChildItem "$env:HOMEDRIVE\\" -Recurse',
        'Get-ChildItem $env:HOMEPATH -Recurse',
        'Get-ChildItem ([Environment]::GetFolderPath(\'UserProfile\')) -Recurse',
        'Get-ChildItem -Path ([System.Environment]::GetFolderPath("Desktop")) -Recurse',
        'Get-ChildItem $x -Recurse',
        'Get-ChildItem $x -Depth 2',
        'Remove-Item $x -Recurse',
        'Copy-Item $x C:\\Users\\someone\\Documents\\copy -Recurse',
        '$files | ForEach-Object { Remove-Item $_ -Recurse }',
    ]) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'refused', message: searchRefusal }, command);
    }
    for (const command of ['du -sh "$SYSTEMDRIVE/"', 'du -sh $HOMEDRIVE/']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), { outcome: 'refused', message: searchRefusal }, command);
    }
    for (const command of [
        'Get-ChildItem $x',
        'Write-Output $x',
        'Get-ChildItem $env:SystemDrive\\Users\\someone\\Documents',
        '$files | ForEach-Object { Remove-Item $_ }',
        'foreach ($f in $files) { Copy-Item $f C:\\Users\\someone\\Documents\\copy }',
        'Copy-Item x "$env:TEMP\\y"',
        'Invoke-RestMethod "$base/v1/campaign.list"',
        '$x | ConvertTo-Json -Depth 5',
    ]) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'runs' }, command);
        for (const judged of both('PowerShell', { command }, windows)) assert.notEqual(judged.verdict, 'deny', command);
    }
});

test('a folder change the reading cannot follow, by a place on the folder stack, two operands, an option the shell sets, a search path of its own or a sourced file, leaves the shell in any folder it has been in or the text names: a walk, a program run there and a read from there are refused', () => {
    for (const scope of scopes) {
        for (const [command, sentence] of [
            ['pushd ~ && pushd /tmp && pushd +1 && du -sh .', searchRefusal],
            ['pushd ~ && pushd /tmp && pushd -1 && frobnicate', inRootRefusal],
            ['cd ~ && cd /tmp && cd +1 && cat .jason/config/settings.json', settingsRefusal],
            ['cd /tmp ~ && du -sh .', searchRefusal],
            ['shopt -s autocd; ~; du -sh .', inRootRefusal],
            ['setopt auto_cd; ~; du -sh .', inRootRefusal],
            ['CDPATH=~ cd .jason && cat config/settings.json', dataRefusal],
            ['export CDPATH=~; cd .jason; cat run/runtime.json', dataRefusal],
            ['cd ~ && cd /tmp && source ./env.sh && frobnicate', searchRefusal],
            ['cd ~ && cd /tmp && . ./env.sh && frobnicate', searchRefusal],
            ['echo ~ | source /dev/stdin', searchRefusal],
            ['cd ~ && cd /tmp && source ./env.sh && cat .jason/config/settings.json', settingsRefusal],
        ] as const) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'allow', `${scope.platform}: ${command}`);
        }
        for (const command of ['cd /tmp && source ./env.sh && make', 'pushd /tmp && pushd +1 && ls']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
        }
    }
});

test('PowerShell reads ${env:NAME} as one variable and a home spelling, a provider-qualified path as its path, and an en dash, an em dash or a horizontal bar as a dash: each spelling of the home folder and of a settings file is refused as the plain one', () => {
    for (const [command, sentence] of [
        ['Get-ChildItem ${env:USERPROFILE} -Recurse', searchRefusal],
        ['Get-ChildItem ${env:HOME} -Recurse', searchRefusal],
        ['Get-ChildItem $env:HOME -Recurse', searchRefusal],
        ['Get-ChildItem "$env:HOMEDRIVE$env:HOMEPATH" -Recurse', searchRefusal],
        ['Get-ChildItem "${env:HOMEDRIVE}${env:HOMEPATH}" -Recurse', searchRefusal],
        ['Get-Content ${env:USERPROFILE}\\.jason\\config\\settings.json', settingsRefusal],
        ['Get-Content "$env:HOMEDRIVE$env:HOMEPATH\\.jason\\config\\settings.json"', settingsRefusal],
        ['Get-Content FileSystem::C:\\Users\\someone\\.jason\\config\\settings.json', settingsRefusal],
        ['Get-Content Microsoft.PowerShell.Core\\FileSystem::C:\\Users\\someone\\.jason\\config\\settings.json', settingsRefusal],
        ['Get-ChildItem Microsoft.PowerShell.Core\\FileSystem::C:\\Users\\someone -Recurse', searchRefusal],
        ['Get-ChildItem FileSystem::C:\\ -Recurse', searchRefusal],
        ['Get-ChildItem ~ \u2013Recurse', searchRefusal],
        ['Get-ChildItem ~ \u2014Recurse', searchRefusal],
        ['Get-ChildItem ~ \u2015Recurse', searchRefusal],
        ['Get-ChildItem \u2013Path ~ \u2013Recurse', searchRefusal],
    ] as const) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'refused', message: sentence }, command);
        for (const judged of both('PowerShell', { command }, windows)) assert.notEqual(judged.verdict, 'allow', command);
    }
    for (const command of ['cmd /c type %HOMEDRIVE%%HOMEPATH%\\.jason\\config\\settings.json', 'cat $HOMEDRIVE$HOMEPATH/.jason/config/settings.json', 'cat ${HOMEDRIVE}${HOMEPATH}/.jason/config/settings.json']) {
        assert.deepEqual(fullAccessJudgement('Bash', { command }, windows), { outcome: 'refused', message: settingsRefusal }, command);
    }
    for (const command of ['Get-ChildItem ${env:USERPROFILE}\\Downloads', 'Get-Content FileSystem::C:\\Users\\someone\\Desktop\\notes.txt', 'Get-ChildItem ~']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'runs' }, command);
    }
});

test('a brace group outside quotes is read over the words it expands into: a hidden option, a hidden folder, a hidden program and a hidden --files0-from are refused as written out; a group of plain names reads as before', () => {
    for (const scope of scopes) {
        for (const [command, sentence] of [
            ['grep {-r,} api_key ~', searchRefusal],
            ['ls {-R,} ~', searchRefusal],
            ['grep -r api_key {~,}', searchRefusal],
            ['{ls,-R} ~', searchRefusal],
            ['du -sh {/,x}', searchRefusal],
            ['sort {--files0-from=-,}', filesFromRefusal],
            ['wc {--files0-from=list,}', filesFromRefusal],
        ] as const) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'allow', `${scope.platform}: ${command}`);
        }
        for (const command of ['ls ~/{Desktop,Downloads}', 'cat ~/Desktop/{a,b}.txt', 'echo {a,b}']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
        }
    }
});

test('a command that removes or descends from a folder that holds the data directory is refused with the walk\'s sentence, and another program run in such a folder with the sentence that says to run it in a narrower folder or name the folder it should work on; a plain form there runs', () => {
    for (const scope of scopes) {
        for (const command of ['du -sh ~', 'rm -rf ~/', 'rm -r ~', 'cd ~ && rm -rf .', 'cd / && grep -r api_key .']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: searchRefusal }, `${scope.platform}: ${command}`);
        }
        for (const command of ['cd ~ && git status', 'cd ~ && frobnicate', 'cd /; make', 'cd ~ && npm test']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: inRootRefusal }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.equal(judged.verdict, 'ask', `${scope.platform}: ${command}`);
        }
        for (const command of ['cd ~ && ls', 'cd ~ && cat Desktop/notes.txt', 'cd ~ && pwd', 'cd ~ && echo done']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
        }
    }
    assert.deepEqual(fullAccessJudgement('PowerShell', { command: 'cd ~; git status' }, windows), { outcome: 'refused', message: inRootRefusal });
});

test('a folder that holds the data directory is walked in no form but those the chat knows do not descend: any other option, an abbreviated long one among them, any -d value, and a program that descends or copies is refused by the hook and the model\'s card each time', () => {
    for (const scope of scopes) {
        const root = scope.platform === 'win32' ? '/c/Users/someone' : scope.home;
        const walks = [
            `grep -d recurse -a -h api_key ${root}`,
            'grep --directories recurse api_key ~',
            'grep --dir=recurse api_key ~',
            'grep -d skip api_key ~',
            'grep --recur api_key ~',
            'grep --rec api_key ~',
            'grep --deref api_key ~',
            'ls --recur ~',
            'tar --cre -f x.tar ~',
            'cp --recur ~ x',
            '7z a x.7z ~',
            'ls --frobnicate ~',
            'grep --frobnicate api_key ~',
            'cd ~; find .',
            'cd /; grep -r api_key .',
            'cd; du -sh .',
            'cd ~ && tar -cf x.tar .',
            'sh -c "du -sh ~"',
            "bash -c 'find ~ -name x'",
            'echo ~ | xargs du -sh',
            'du -sh $(echo ~)',
        ];
        for (const command of walks) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: searchRefusal }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) {
                assert.deepEqual(judged, { verdict: 'ask', title: 'Run a command', detail: command, remember: null }, `${scope.platform}: ${command}`);
            }
        }
    }
    for (const command of ['Compress-Archive -Path ~ -DestinationPath x.zip', 'Compress-Archive -Path:~ -DestinationPath x.zip', 'Get-ChildItem ~ -Rec', 'gci -Path ~ -Recurse']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'refused', message: searchRefusal }, command);
    }
});

test('a jq program that names the environment with space between the dollar and the name is refused as the joined one is', () => {
    for (const scope of scopes) {
        for (const command of ["jq -n '$ ENV'", "jq -n '$  ENV.HOME'", "jq -n '$\tENV'", "jq -n '$\nENV'", "jason campaign list | jq '$ ENV'"]) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: jqRefusal }, `${scope.platform}: ${JSON.stringify(command)}`);
        }
        assert.deepEqual(fullAccessJudgement('Bash', { command: "jason campaign list | jq '$ENVY'" }, scope).outcome, 'runs', scope.platform);
    }
});

test('a quoted text or a here-document is read again as commands only where a shell, a runner or eval may run it: a commit message, a pull request body and a printed text holding a lone / or ~, or the words of a jq program, are data and are not refused; under sh -c, eval or a pipe into a shell the same words are read', () => {
    for (const scope of scopes) {
        for (const command of [
            "git commit -m 'a / b'",
            "git commit -m 'kept under ~ for now'",
            "gh pr create --title 'Notes' --body 'kept under ~ for now'",
            "git commit -F - <<'EOF'\nmove a / b\nEOF",
            "echo 'a / b' | sort",
            "echo 'jq env'",
        ]) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'deny', `${scope.platform}: ${command}`);
        }
        for (const [command, sentence] of [
            ["sh -c 'du -sh /'", searchRefusal],
            ["eval 'du -sh ~'", searchRefusal],
            ["echo 'du -sh ~' | sh", searchRefusal],
            ["sh -c 'jq env'", jqRefusal],
            ["eval 'jq env'", jqRefusal],
        ] as const) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
        }
    }
});

test('a plain listing of such a folder, a read of a file in it and a body that only mentions a walk run with no card in Ask first and Auto-edit and run in Full access: the Downloads folder, the home folder, a here-document and a quoted text', () => {
    for (const scope of scopes) {
        const commands = [
            'ls ~/Downloads',
            'ls -la ~/Downloads',
            'ls ~',
            'ls -la ~',
            'ls -1 --color=never ~',
            'grep -i -n api_key ~/Documents/notes.md',
            "jason journal append --file - <<'EOF'\n{\"note\":\"check du / weekly\"}\nEOF",
            "jason campaign update-context cmp_1 --file - <<'EOF'\n{\"plan\":\"find ~ -name x, then tree /\"}\nEOF",
            "echo 'find / is slow'",
            'jason campaign list | grep -c "tree /"',
        ];
        for (const command of commands) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        }
    }
    for (const command of ['Get-ChildItem ~', 'gci -Path ~ -Force', 'dir ~\\Downloads']) {
        assert.deepEqual(fullAccessJudgement('PowerShell', { command }, windows), { outcome: 'runs' }, command);
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

test('the three kinds are read as the walk reads a command: after a folder change, through a variable the reading knows and by a pattern that can name them, for a read and a listing alike, by the hook and by the model; a file of the person\'s in their home still reads', () => {
    for (const scope of scopes) {
        const refusals: [string, string, string][] = [
            ['Bash', 'cd "$HOME" && cat .jason/config/settings.json', settingsRefusal],
            ['Bash', 'cd ~ && cat .jason/run/runtime.json', settingsRefusal],
            ['Bash', 'd=.jason; cat ~/$d/config/settings.json', settingsRefusal],
            ['Bash', 'export d=.jason && cat "$HOME/$d/config/settings.json"', settingsRefusal],
            ['Bash', 'cat ~/.jas*/config/settings.json', settingsRefusal],
            ['Bash', 'cat ~/.j?son/config/settings.json', settingsRefusal],
            ['Bash', 'cat ~/.[j]ason/config/settings.json', settingsRefusal],
            ['Bash', 'cat ~someone/.jason/config/settings.json', settingsRefusal],
            ['Bash', 'cd ~ && grep api_key .j*/config/*', settingsRefusal],
            ['Bash', 'grep -s api_key ~/.*/**/*', settingsRefusal],
            ['Bash', 'shopt -s globstar dotglob; grep -s api_key ~/**', settingsRefusal],
            ['Bash', 'ls ~/.j*', dataRefusal],
            ['Bash', 'cd ~ && ls .jason', dataRefusal],
            ['Bash', 'cd ~ && ls .j*', dataRefusal],
            ['Bash', 'cat ${HOME%/}/.jason/config/settings.json', settingsRefusal],
        ];
        if (scope.platform === 'win32') {
            refusals.push(
                ['PowerShell', 'cd $HOME; Get-Content .jason\\config\\settings.json', settingsRefusal],
                ['PowerShell', "$d = '.jason'; Get-Content ~\\$d\\config\\settings.json", settingsRefusal],
                ['PowerShell', 'Get-Content ~\\.jas*\\config\\settings.json', settingsRefusal],
                ['PowerShell', 'Get-ChildItem ~\\.j*', dataRefusal],
            );
        }
        for (const [tool, command, sentence] of refusals) {
            assert.deepEqual(fullAccessJudgement(tool, { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
            for (const judged of both(tool, { command }, scope)) assert.deepEqual(judged, { verdict: 'deny', message: sentence }, `${scope.platform}: ${command}`);
        }
        for (const input of [{ pattern: '~/.jas*/config/*' }, { pattern: '.j*/config/*', path: scope.home }]) {
            assert.equal(fullAccessJudgement('Glob', input, scope).outcome, 'refused', `${scope.platform}: ${JSON.stringify(input)}`);
        }
        for (const command of ['cat ~/Desktop/notes.txt', 'ls ~/Downloads', 'ls -la ~', 'cat ~/.bashrc', 'cat ~/.*rc', 'cd ~ && cat Desktop/notes.txt', 'ls ~/D*', 'f=notes.md; cat ~/Documents/$f']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'deny', `${scope.platform}: ${command}`);
        }
        for (const command of ['cat ~/Desktop/notes.txt', 'ls ~/Downloads', 'ls -la ~']) assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
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

test('a file tool\'s target that is another link to a settings file or the run file is refused as that file, by the hook and by the model; a file with one link, and another link to a file of no kind, are not', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-hooked-'));
    t.after(() => rmSync(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }));
    const home = realPathOf(root) ?? root;
    const data = join(home, '.jason');
    const documents = join(home, 'Documents');
    for (const folder of [join(data, 'config'), join(data, 'run'), join(data, 'desktop', 'chats', 'c1'), documents]) mkdirSync(folder, { recursive: true });
    writeFileSync(join(data, 'config', 'settings.json'), '{}');
    writeFileSync(join(data, 'run', 'runtime.json'), '{}');
    writeFileSync(join(documents, 'plain.md'), 'x');
    writeFileSync(join(documents, 'copy.json'), '{}');
    try {
        linkSync(join(data, 'config', 'settings.json'), join(documents, 'notes.json'));
    } catch {
        t.skip('the file system of the temporary folder cannot make a second link to a file');
        return;
    }
    linkSync(join(data, 'run', 'runtime.json'), join(documents, 'state.json'));
    linkSync(join(documents, 'plain.md'), join(documents, 'plain-again.md'));
    const platform = process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
    const scope = hookedScope(platform, home, { real: realPathOf, entry: entryOf, text: textOf, identity: identityOf });
    for (const path of [join(documents, 'notes.json'), join(documents, 'state.json')]) {
        for (const [tool, input] of shapes) {
            if (tool === 'Glob' || tool === 'Bash') continue;
            const name = `${tool} ${path}`;
            assert.deepEqual(fullAccessJudgement(tool, input(path), scope), { outcome: 'refused', message: settingsRefusal }, name);
            for (const judged of both(tool, input(path), scope)) assert.deepEqual(judged, { verdict: 'deny', message: settingsRefusal }, name);
        }
    }
    for (const file of ['plain.md', 'plain-again.md', 'copy.json']) {
        const path = join(documents, file);
        assert.deepEqual(fullAccessJudgement('Read', { file_path: path }, scope), { outcome: 'runs' }, file);
        assert.deepEqual(judge('Read', { file_path: path }, scope), { verdict: 'allow' }, file);
    }
    assert.equal(identityOf(documents), null, 'a folder has no identity of a file');
    assert.equal(identityOf(join(documents, 'missing.md')), null, 'nothing is there');
});

test('a command that names another link to a settings file or the run file, as a word or as a redirect\'s source, is refused as that file by the hook and by the model; in a demo another link to the person\'s own settings file is refused as the person\'s own', (t) => {
    const root = mkdtempSync(join(tmpdir(), 'jason-hooked-'));
    t.after(() => rmSync(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }));
    const home = realPathOf(root) ?? root;
    const data = join(home, '.jason');
    const own = join(home, 'own', '.jason');
    const documents = join(home, 'Documents');
    for (const folder of [join(data, 'config'), join(data, 'run'), join(data, 'desktop', 'chats', 'c1'), join(own, 'config'), documents]) mkdirSync(folder, { recursive: true });
    writeFileSync(join(data, 'config', 'settings.json'), '{}');
    writeFileSync(join(data, 'run', 'runtime.json'), '{}');
    writeFileSync(join(own, 'config', 'settings.json'), '{}');
    writeFileSync(join(documents, 'plain.md'), 'x');
    try {
        linkSync(join(data, 'config', 'settings.json'), join(documents, 'notes.json'));
    } catch {
        t.skip('the file system of the temporary folder cannot make a second link to a file');
        return;
    }
    linkSync(join(data, 'run', 'runtime.json'), join(documents, 'state.json'));
    linkSync(join(own, 'config', 'settings.json'), join(documents, 'mine.json'));
    const platform = process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
    const scope = hookedScope(platform, home, { real: realPathOf, entry: entryOf, text: textOf, identity: identityOf });
    const quoted = (path: string) => `'${path}'`;
    for (const file of ['notes.json', 'state.json']) {
        const path = join(documents, file);
        for (const command of [`cat ${quoted(path)}`, `cp ${quoted(path)} copy.json`, `wc -l < ${quoted(path)}`, `cd ${quoted(documents)} && cat ${file}`]) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: settingsRefusal }, command);
            for (const judged of both('Bash', { command }, scope)) assert.deepEqual(judged, { verdict: 'deny', message: settingsRefusal }, command);
        }
    }
    assert.deepEqual(fullAccessJudgement('Bash', { command: `cat ${quoted(join(documents, 'plain.md'))}` }, scope), { outcome: 'runs' });
    const demo = { ...scope, personsOwn: [own] };
    const mine = join(documents, 'mine.json');
    for (const [tool, input] of [['Read', { file_path: mine }], ['Bash', { command: `cat ${quoted(mine)}` }]] as const) {
        assert.deepEqual(fullAccessJudgement(tool, input, demo), { outcome: 'refused', message: personsRefusal }, `${tool} ${JSON.stringify(input)}`);
        for (const judged of both(tool, input, demo)) assert.deepEqual(judged, { verdict: 'deny', message: personsRefusal }, `${tool} ${JSON.stringify(input)}`);
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
    assert.match(searchRefusal, /holds Jason's data directory, the installation's own data and not one of the person's files/);
    assert.match(searchRefusal, /Name a narrower folder that does not hold it/);
    assert.match(searchRefusal, /search, walk, copy, archive or remove/);
    assert.match(searchRefusal, /On such a folder the chat runs a listing of its names, a read or a search of a file named in it, a look at a path/);
    assert.match(inRootRefusal, /holds Jason's data directory, the installation's own data and not one of the person's files/);
    assert.match(inRootRefusal, /Run it in a narrower folder/);
    assert.match(inRootRefusal, /name in the command the folder it should work on/);
    assert.notEqual(inRootRefusal, searchRefusal);
    for (const sentence of [confinedRefusal, attemptRefusal, lookupRefusal]) assert.match(sentence, /Jason's own (?:variables? )?(?:for this turn )?and not the person's/, sentence);
    assert.match(ownToolRefusal, /Jason's own, set for this turn/);
    // A walk of such a folder would pass through Jason's own data, and the sentence names the narrower folder to use.
    assert.match(searchRefusal, /would pass through Jason's own data/);
});

test('in Full access the hook refuses the three kinds and what would reach them unseen, and nothing else: the person\'s files anywhere are read, appended to and written, and every jason verb runs, those Ask first and Auto-edit show first among them', () => {
    for (const scope of scopes) {
        const desktop = scope.platform === 'win32' ? 'C:\\Users\\someone\\Desktop\\мой файл 1.txt' : `${scope.home}/Desktop/мой файл 1.txt`;
        for (const [tool, input] of [
            ['Read', { file_path: desktop }],
            ['Edit', { file_path: desktop, old_string: 'end', new_string: 'end\nline one\nline two' }],
            ['Write', { file_path: desktop.replace('мой файл 1.txt', 'notes.txt'), content: 'x' }],
            ['Grep', { pattern: 'x', path: desktop.replace(/[\\/]мой файл 1\.txt$/, '') }],
            ['Glob', { pattern: '*.txt', path: desktop.replace(/[\\/]мой файл 1\.txt$/, '') }],
            ['LS', { path: scope.home }],
            ['Bash', { command: 'ls ~' }],
            ['Bash', { command: 'cat ~/Desktop/notes.txt' }],
        ] as const) {
            assert.deepEqual(fullAccessJudgement(tool, input, scope), { outcome: 'runs' }, `${scope.platform} ${tool} ${JSON.stringify(input)}`);
        }
        for (const command of [...noCard, ...carded, 'jason --actor human:someone approval approve apr_1']) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
        }
        const data = scope.platform === 'win32' ? win32 : posix;
        assert.deepEqual(fullAccessJudgement('Read', { file_path: data.join(scope.data, 'config', 'settings.json') }, scope), { outcome: 'refused', message: settingsRefusal });
        assert.deepEqual(fullAccessJudgement('Read', { file_path: data.join(scope.data, 'jason.db') }, scope), { outcome: 'refused', message: dataRefusal });
        assert.deepEqual(fullAccessJudgement('Bash', { command: 'JASON_CHAT_TURN=0 jason campaign start cmp_1' }, scope), { outcome: 'refused', message: confinedRefusal });
        assert.deepEqual(fullAccessJudgement('mcp__jason_desktop__permission', { tool_name: 'Bash', input: {} }, scope), { outcome: 'refused', message: ownToolRefusal });
    }
});

test('a Glob whose pattern climbs is judged where it can lead, and a brace group that can name a place is not taken, by the hook in every mode it rides', () => {
    const documents = '/home/someone/Documents';
    const inDocuments = { ...linux, added: [documents] };
    for (const [input, outcome] of [
        [{ pattern: 'x*/../*' }, { outcome: 'refused', message: dataRefusal }],
        [{ pattern: '**/../../../*' }, { outcome: 'refused', message: dataRefusal }],
        [{ pattern: '**/../../../../*' }, { outcome: 'refused', message: searchRefusal }],
        [{ pattern: '*/../*', path: documents }, { outcome: 'refused', message: searchRefusal }],
        [{ pattern: '{..,x}/*', path: documents }, { outcome: 'refused', message: searchRefusal }],
        [{ pattern: '*/../*', path: `${documents}/sub` }, { outcome: 'runs' }],
        [{ pattern: '**/*.{md,txt}', path: documents }, { outcome: 'runs' }],
        [{ pattern: '{/home/someone/.jason,x}/*', path: documents }, { outcome: 'refused', message: braceRefusal }],
        [{ pattern: '~/.jason/config/*' }, { outcome: 'refused', message: dataRefusal }],
        [{ pattern: '~/Documents/**/*.md' }, { outcome: 'runs' }],
    ] as const) {
        assert.deepEqual(fullAccessJudgement('Glob', input, inDocuments), outcome, JSON.stringify(input));
    }
});

test('a no-card compound takes each filter of its closed list only with that filter\'s own options and only as a reader of standard input: any other option, a file a filter names in a compound, and one that can write a file keep their card', () => {
    assert.deepEqual([...noCardFilters], ['jq', 'grep', 'head', 'tail', 'wc', 'sort', 'uniq', 'cut', 'tr', 'cat', 'ls', 'echo']);
    for (const scope of scopes) {
        const runs = [
            'jason campaign list | sort -r',
            'jason campaign list | sort -k 2 -t , -n',
            'jason campaign list | uniq -c',
            'echo done',
            'jason contact list | grep -c reply',
            'jason contact list | grep -i -e reply -e sent',
            'jason campaign list | jq -r .items',
            'jason campaign list | jq -rc .items',
            "jason campaign list | jq -c --arg id cmp_1 '.items[] | select(.id == $id)'",
            "jason campaign list | jq --argjson n 2 '.total > $n'",
            'jason campaign list | jq -S -M -a -j -e -s .',
            'jason contact list | cut -d , -f 1',
            'jason contact list | tr -d x',
            'jason campaign list | head -5',
            'jason campaign list | tail -n 2',
            'jason campaign list | wc -l',
            'jason campaign list | cat -n',
        ];
        for (const command of runs) assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        const cards = [
            'jason campaign list | sort -ro x',
            'jason campaign list | sort --out=x',
            'jason campaign list | sort -T /tmp',
            'jason campaign list | sort --compress-program=gzip',
            'jason campaign list | uniq - out.txt',
            'jason campaign list | tee x',
            'jason campaign list >> x',
            'jason campaign list 2> errors.txt',
            'jason campaign list | sort -- -o',
            'jason campaign list | sort notes.txt',
            'jason campaign list | cat notes.txt',
            'jason campaign list | grep reply notes.txt',
            'jason campaign list | jq . notes.json',
            'jason campaign list | jq -n .',
            'jason campaign list | jq --raw-output .',
            'jason campaign list | jq -L lib .',
            'jason campaign list | jq --slurpfile x data.json .',
            'jason campaign list | jq --rawfile x notes.txt .',
            'jason campaign list | tail -f',
            'jason campaign list | head --frobnicate',
            'jason campaign list | wc --frobnicate',
            'jason campaign list | sort --frobnicate',
            'jason campaign list | uniq --frobnicate',
            'jason campaign list | cut --frobnicate',
            'jason campaign list | tr --frobnicate a b',
            'jason campaign list | grep --frobnicate reply',
            'jason campaign list | jq --frobnicate .',
            'jason campaign list | cat --frobnicate',
            'jason campaign list && ls --frobnicate',
        ];
        for (const command of cards) assert.deepEqual(both('Bash', { command }, scope).map((judged) => judged.verdict), ['ask', 'ask'], `${scope.platform}: ${command}`);
    }
});

test('a jq program that reads the environment or a file by its own words, and a command told the files it reads on its input, are refused where the hook rides, by the hook and by the model', () => {
    for (const scope of scopes) {
        const refusals: [string, string][] = [
            ["jason campaign list | jq 'env'", jqRefusal],
            ["jason campaign list | jq '$ENV.HOME'", jqRefusal],
            ['jason campaign list | jq -n \'import "settings" as $s; $s\'', jqRefusal],
            ['jason campaign list | jq \'include "x"; .\'', jqRefusal],
            ['jason campaign list | jq -f prog.jq', jqRefusal],
            ['jason campaign list | jq -rf prog.jq', jqRefusal],
            ['jason campaign list | jq --from-file prog.jq', jqRefusal],
            ['echo notes.txt | tr "\\n" "\\0" | sort --files0-from=-', filesFromRefusal],
            ['echo notes.txt | sort --fil=-', filesFromRefusal],
            ['echo notes.txt | wc --files0-from=-', filesFromRefusal],
            ['echo notes.txt | wc --files0=-', filesFromRefusal],
            ['echo notes.txt | du --files0-from=-', filesFromRefusal],
            ['find -files0-from list.txt', filesFromRefusal],
        ];
        for (const [command, sentence] of refusals) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: sentence }, `${scope.platform}: ${command}`);
            for (const judged of both('Bash', { command }, scope)) assert.deepEqual(judged, { verdict: 'deny', message: sentence }, `${scope.platform}: ${command}`);
        }
        // A name that only holds the words, and a program that names none, run as before.
        for (const command of ["jason campaign list | jq '.environment'", "jason campaign list | jq '.include_paths'"]) {
            assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
        }
    }
});

test('a path in single quotes spelled with a letter whose compatibility form or case is ASCII is no read with no card in Ask first and Auto-edit: a long s, a Kelvin sign and fullwidth letters', () => {
    const commands: [ChatScope, string][] = [
        [linux, "cat '/home/someone/.ja\u017fon/config/settings.json'"],
        [mac, "cat '/Users/someone/.ja\u017fon/config/settings.json'"],
        [windows, "cat 'C:\\Users\\someone\\.\u212aason\\config\\settings.json'"],
        [windows, "jq . 'C:\\Users\\someone\\\uff0e\uff4a\uff41\uff53\uff4f\uff4e\\config\\settings.json'"],
        [linux, "jq . '/home/someone/.jason/config/\uff53\uff45\uff54\uff54\uff49\uff4e\uff47\uff53.json'"],
    ];
    for (const [scope, command] of commands) {
        for (const judged of both('Bash', { command }, scope)) assert.notEqual(judged.verdict, 'allow', `${scope.platform}: ${command}`);
    }
    assert.deepEqual(both('Bash', { command: "cat '/home/someone/Desktop/мой файл.txt'" }, linux), [allow, allow]);
});

test('a reason in Russian and an account named in Cyrillic, each in single quotes, run with no card where the hook rides Ask first and Auto-edit, and below the floor each is a card as before; in double quotes, bare, or with a mark that is no letter, each is a card', () => {
    const runs = [
        "jason campaign archive cmp_1 --actor 'human:Некто' --reason 'человек попросил архивировать'",
        "jason campaign start cmp_1 --actor 'human:Некто' --reason 'человек попросил запустить'",
        "jason campaign list | jq '.items[] | select(.name == \"Кампания\")'",
    ];
    for (const scope of scopes) {
        for (const command of runs) {
            assert.deepEqual(both('Bash', { command }, scope), [allow, allow], `${scope.platform}: ${command}`);
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'runs' }, `${scope.platform}: ${command}`);
            assert.equal(judge('Bash', { command }, unhooked(scope)).verdict, 'ask', `below the floor: ${command}`);
        }
        for (const command of [
            "jason campaign archive cmp_1 --actor human:Некто --reason 'as asked'",
            "jason campaign archive cmp_1 --actor \"human:Некто\" --reason 'as asked'",
            "jason campaign archive cmp_1 --reason 'человек\u202eпопросил'",
            "jason campaign archive cmp_1 --reason 'человек\u0007'",
            "jason campaign archive cmp_1 --reason 'человек\u2026'",
        ]) {
            assert.deepEqual(both('Bash', { command }, scope).map(asked), ['Run a command', 'Run a command'], `${scope.platform}: ${command}`);
        }
    }
});
