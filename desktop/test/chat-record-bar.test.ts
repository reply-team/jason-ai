// The bar around a conversation's record, which keeps the conversation's mode: no turn of the chat can change it by any
// plain act. (a) A turn not in Full access is refused it by the rules that already keep the data directory from the chat:
// the permission model in Ask first and Plan only, and the host's own rules, which name the records' folder before it
// exists. (b) A plain command of a Full access turn that would write it is refused the same way by the turn's hook, the
// data directory being one of the three kinds Full access refuses. A program the AI writes and runs is not read.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { dataRules } from '../src/chat-hosts.ts';
import { dataRefusal, fullAccessJudgement, judge, type ChatScope } from '../src/chat-permission-model.ts';
import { denyListFile, parseDenyList } from '../src/chat-deny.ts';

const carried = parseDenyList(readFileSync(join(import.meta.dirname, '..', denyListFile), 'utf8'));

const id = 'conv_01JD0000000000000000000000';
/** The conversation's own folder, where its turns run. */
const own = `/home/someone/.jason/desktop/chats/${id}`;

function linux(cwd = own): ChatScope {
    const existing = new Set([
        '/',
        '/home',
        '/home/someone',
        '/home/someone/.jason',
        '/home/someone/.jason/desktop',
        '/home/someone/.jason/desktop/chat',
        '/home/someone/.jason/desktop/chats',
        own,
        '/home/someone/.jason/desktop/conversations',
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
    const folder = `C:\\Users\\someone\\.jason\\desktop\\chats\\${id}`;
    const existing: Record<string, string> = {
        'c:\\': 'C:\\',
        'c:\\users': 'C:\\Users',
        'c:\\users\\someone': 'C:\\Users\\someone',
        'c:\\users\\someone\\.jason': 'C:\\Users\\someone\\.jason',
        'c:\\users\\someone\\.jason\\desktop': 'C:\\Users\\someone\\.jason\\desktop',
        'c:\\users\\someone\\.jason\\desktop\\chats': 'C:\\Users\\someone\\.jason\\desktop\\chats',
        [folder.toLowerCase()]: folder,
        'c:\\users\\someone\\.jason\\desktop\\conversations': 'C:\\Users\\someone\\.jason\\desktop\\conversations',
        'c:\\users\\someone\\.jason\\config': 'C:\\Users\\someone\\.jason\\config',
        'c:\\users\\someone\\.jason\\run': 'C:\\Users\\someone\\.jason\\run',
    };
    return {
        platform: 'win32',
        cwd: folder,
        chat: folder,
        added: [],
        data: 'C:\\Users\\someone\\.jason',
        home: 'C:\\Users\\someone',
        protectedFiles: ['C:\\Users\\someone\\.jason\\config\\settings.json', 'C:\\Users\\someone\\.jason\\run\\runtime.json'],
        denied: carried.commands,
        deniedOperations: carried.operations,
        real: (path) => existing[path.toLowerCase()] ?? null,
    };
}

const file = `/home/someone/.jason/desktop/conversations/${id}.json`;
/** The record with no mode named, which is Full access. */
const body = `{"id":"${id}","campaign_id":null,"title":"New chat","sessions":{},"entries":[],"dropped":0}`;

const commands = [
    `echo '${body}' > ${file}`,
    `printf x | tee ${file}`,
    `cp /home/someone/work/record.json ${file}`,
    `mv /home/someone/work/record.json ${file}`,
    `cd /home/someone/.jason/desktop/conversations && echo x > ${id}.json`,
    `echo x > ~/.jason/desktop/conversations/${id}.json`,
    `echo x > $HOME/.jason/desktop/conversations/${id}.json`,
    `rm ${file}`,
];

test('(a) a turn not in Full access cannot change a conversation\'s record: every plain act on its file is refused by the data rule', () => {
    for (const scope of [linux(), linux('/home/someone/.jason/desktop/chat')]) {
        for (const [tool, input] of [
            ['Write', { file_path: file, content: body }],
            ['Edit', { file_path: file, old_string: '"mode":"ask_first",', new_string: '' }],
            ['Read', { file_path: file }],
            ...commands.map((command) => ['Bash', { command }] as const),
        ] as const) {
            const judged = judge(tool, input as Record<string, unknown>, scope);
            assert.equal(judged.verdict, 'deny', `${scope.chat}: ${tool}: ${JSON.stringify(input)}`);
            assert.equal(judged.verdict === 'deny' ? judged.message : '', dataRefusal, `${scope.chat}: ${tool}: ${JSON.stringify(input)}`);
        }
    }
});

test('(a) the host\'s own rules refuse the records to every Claude Code turn, before they exist, in the shared chat folder and in a conversation\'s own', () => {
    for (const rules of [dataRules('linux', '/home/someone/.jason', [], []), dataRules('linux', '/home/someone/.jason', [], [], { id, present: [id] })]) {
        for (const rule of ['Read', 'Edit']) {
            assert.ok(rules.includes(`${rule}(//home/someone/.jason/desktop/conversations)`), rule);
            assert.ok(rules.includes(`${rule}(//home/someone/.jason/desktop/conversations/**)`), rule);
        }
    }
});

test('(b) a plain command of a Full access turn that would write a conversation\'s record is refused and never run', () => {
    for (const scope of [linux(), linux('/home/someone/.jason/desktop/chat')]) {
        for (const command of commands) {
            assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: dataRefusal }, command);
        }
        assert.deepEqual(fullAccessJudgement('Write', { file_path: file, content: body }, scope), { outcome: 'refused', message: dataRefusal });
        assert.deepEqual(fullAccessJudgement('Edit', { file_path: file, old_string: '"mode":"ask_first",', new_string: '' }, scope), { outcome: 'refused', message: dataRefusal });
    }
});

test('(a) and (b) hold in every spelling: from a conversation\'s own folder by a relative path, in Git Bash\'s drive form, and in another case on Windows', () => {
    const scope = linux();
    for (const command of [`echo x > ../../conversations/${id}.json`, `cat ../../conversations/${id}.json`, `cp /home/someone/work/c.json ../../conversations/${id}.json`]) {
        const judged = judge('Bash', { command }, scope);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, scope), { outcome: 'refused', message: dataRefusal }, command);
    }
    assert.deepEqual(fullAccessJudgement('Write', { file_path: `../../conversations/${id}.json`, content: body }, scope), { outcome: 'refused', message: dataRefusal });
    const onWindows = windows();
    const spellings = [
        `echo x > /c/Users/someone/.jason/desktop/conversations/${id}.json`,
        `echo x > C:\\Users\\SOMEONE\\.JASON\\Desktop\\CONVERSATIONS\\${id}.JSON`,
        `echo x > c:/users/someone/.Jason/DESKTOP/Conversations/${id}.json`,
        `echo x > /C/USERS/someone/.jason/desktop/CONVERSATIONS/${id}.json`,
    ];
    for (const command of spellings) {
        const judged = judge('Bash', { command }, onWindows);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, command);
        assert.deepEqual(fullAccessJudgement('Bash', { command }, onWindows), { outcome: 'refused', message: dataRefusal }, command);
    }
    for (const path of [`C:\\Users\\someone\\.Jason\\DESKTOP\\Conversations\\${id}.json`, `C:/USERS/SOMEONE/.JASON/DESKTOP/CONVERSATIONS/${id}.JSON`]) {
        const judged = judge('Write', { file_path: path, content: body }, onWindows);
        assert.equal(judged.verdict === 'deny' ? judged.message : judged.verdict, dataRefusal, path);
        assert.deepEqual(fullAccessJudgement('Write', { file_path: path, content: body }, onWindows), { outcome: 'refused', message: dataRefusal }, path);
    }
});
