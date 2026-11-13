// The bar around the standing choice of Full access: no turn of the chat can set it by any plain act. (a) A turn not in
// Full access is refused it by the rules that already keep the data directory from the chat: the permission model in
// Ask first and Plan only, and the host's own rules, which name the file before it exists. (b) A plain command of a Full
// access turn that would write it is refused the same way, before anything is read literally, since Full access is
// never weaker than Ask first. (c) A program the AI writes and runs is not read; the confirmation says so.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { dataRules } from '../src/chat-hosts.ts';
import { fullAccessDialog } from '../src/chat-modes.ts';
import { fullAccessJudgement, judge, type ChatScope } from '../src/chat-permission-model.ts';
import { denyListFile, parseDenyList } from '../src/chat-deny.ts';

const carried = parseDenyList(readFileSync(join(import.meta.dirname, '..', denyListFile), 'utf8'));
const dataRefusal = "That path is the installation's own data, outside the chat's folder: the chat neither reads nor writes it. Nothing was done.";

function linux(cwd = '/home/someone/.jason/desktop/chat'): ChatScope {
    const existing = new Set([
        '/',
        '/home',
        '/home/someone',
        '/home/someone/.jason',
        '/home/someone/.jason/desktop',
        '/home/someone/.jason/desktop/chat',
        '/home/someone/.jason/desktop/chat-full',
        '/home/someone/.jason/desktop/chat-full/grant-1',
        '/home/someone/.jason/config',
        '/home/someone/.jason/run',
        '/home/someone/work',
    ]);
    return {
        platform: 'linux',
        cwd,
        chat: cwd,
        added: [],
        data: '/home/someone/.jason',
        home: '/home/someone',
        protectedFiles: ['/home/someone/.jason/config/settings.json', '/home/someone/.jason/run/runtime.json'],
        denied: carried.commands,
        deniedOperations: carried.operations,
        real: (path) => (existing.has(path) ? path : null),
    };
}

/** The same installation on Windows, case-insensitive as NTFS is by default. */
function windows(): ChatScope {
    const existing: Record<string, string> = {
        'c:\\': 'C:\\',
        'c:\\users': 'C:\\Users',
        'c:\\users\\someone': 'C:\\Users\\someone',
        'c:\\users\\someone\\.jason': 'C:\\Users\\someone\\.jason',
        'c:\\users\\someone\\.jason\\desktop': 'C:\\Users\\someone\\.jason\\desktop',
        'c:\\users\\someone\\.jason\\desktop\\chat': 'C:\\Users\\someone\\.jason\\desktop\\chat',
        'c:\\users\\someone\\.jason\\config': 'C:\\Users\\someone\\.jason\\config',
        'c:\\users\\someone\\.jason\\run': 'C:\\Users\\someone\\.jason\\run',
    };
    return {
        platform: 'win32',
        cwd: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        chat: 'C:\\Users\\someone\\.jason\\desktop\\chat',
        added: [],
        data: 'C:\\Users\\someone\\.jason',
        home: 'C:\\Users\\someone',
        protectedFiles: ['C:\\Users\\someone\\.jason\\config\\settings.json', 'C:\\Users\\someone\\.jason\\run\\runtime.json'],
        denied: carried.commands,
        deniedOperations: carried.operations,
        real: (path) => existing[path.toLowerCase()] ?? null,
    };
}

const file = '/home/someone/.jason/desktop/full-access.json';
const body = '{"claude":{"consent":1,"given_at":"2026-01-02T03:04:05.000Z","new_chats":true}}';

const commands = [
    `echo '${body}' > ${file}`,
    `printf x | tee ${file}`,
    `cp /home/someone/work/choice.json ${file}`,
    `mv /home/someone/work/choice.json ${file}`,
    `cd /home/someone/.jason/desktop && echo x > full-access.json`,
    `echo x > ~/.jason/desktop/full-access.json`,
    `echo x > $HOME/.jason/desktop/full-access.json`,
    `rm ${file}`,
];

test('(a) a turn not in Full access cannot set the standing choice: every plain act on its file is refused by the data rule', () => {
    const scope = linux();
    for (const [tool, input] of [
        ['Write', { file_path: file, content: body }],
        ['Edit', { file_path: file, old_string: 'false', new_string: 'true' }],
        ['Read', { file_path: file }],
        ...commands.map((command) => ['Bash', { command }] as const),
    ] as const) {
        const judged = judge(tool, input as Record<string, unknown>, scope);
        assert.equal(judged.verdict, 'deny', `${tool}: ${JSON.stringify(input)}`);
        assert.equal(judged.verdict === 'deny' ? judged.message : '', dataRefusal, `${tool}: ${JSON.stringify(input)}`);
    }
});

test('(a) the host\'s own rules refuse the file to every Claude Code turn, before it exists and in a Full access grant\'s turn', () => {
    for (const rules of [dataRules('linux', '/home/someone/.jason', [], []), dataRules('linux', '/home/someone/.jason', [], [], { id: 'grant-1', present: ['grant-1'] })]) {
        for (const rule of ['Read', 'Edit']) assert.ok(rules.includes(`${rule}(//home/someone/.jason/desktop/full-access.json)`), rule);
    }
});

test('(b) a plain command of a Full access turn that would write the standing choice is refused, never held and never run', () => {
    for (const scope of [linux(), linux('/home/someone/.jason/desktop/chat-full/grant-1')]) {
        for (const command of commands) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: dataRefusal }, command);
        }
        assert.deepEqual(fullAccessJudgement('Write', { file_path: file, content: body }, scope), { outcome: 'refused', message: dataRefusal });
        assert.deepEqual(fullAccessJudgement('Edit', { file_path: file, old_string: 'false', new_string: 'true' }, scope), { outcome: 'refused', message: dataRefusal });
    }
});

test('(a) and (b) hold in every spelling: from a grant folder by a relative path, in Git Bash\'s drive form, and in another case on Windows', () => {
    const grant = linux('/home/someone/.jason/desktop/chat-full/grant-1');
    for (const command of ['echo x > ../../full-access.json', 'cat ../../full-access.json', 'cp /home/someone/work/c.json ../../full-access.json']) {
        const judged = judge('Bash', { command }, grant);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, grant), { outcome: 'refused', message: dataRefusal }, command);
    }
    assert.deepEqual(fullAccessJudgement('Write', { file_path: '../../full-access.json', content: body }, grant), { outcome: 'refused', message: dataRefusal });
    const scope = windows();
    const spellings = [
        'echo x > /c/Users/someone/.jason/desktop/full-access.json',
        'echo x > C:\\Users\\SOMEONE\\.JASON\\Desktop\\FULL-ACCESS.JSON',
        'echo x > c:/users/someone/.Jason/DESKTOP/Full-Access.json',
        'echo x > /C/USERS/someone/.jason/desktop/FULL-access.json',
    ];
    for (const command of spellings) {
        const judged = judge('Bash', { command }, scope);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: dataRefusal }, command);
    }
    for (const path of ['C:\\Users\\someone\\.Jason\\DESKTOP\\Full-Access.json', 'C:/USERS/SOMEONE/.JASON/DESKTOP/FULL-ACCESS.JSON']) {
        const judged = judge('Write', { file_path: path, content: body }, scope);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, path);
        assert.deepEqual(fullAccessJudgement('Write', { file_path: path, content: body }, scope), { outcome: 'refused', message: dataRefusal }, path);
    }
});

test('(c) what the bar does not hold is said in the confirmation itself: a program the AI writes and runs could choose Always', () => {
    assert.match(fullAccessDialog().detail, /a program the AI writes and runs could choose it for you too\./);
});
