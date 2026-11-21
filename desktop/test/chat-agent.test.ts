// New GTM agent's click, end to end against a stub jason: the desktop reads every campaign, names the agent "New agent N"
// after the highest number such a name carries, makes it through the CLI under the click's key and the person's actor,
// and keeps the agent's conversation from that moment, marked made at the click. A retry of a click asks the same create;
// clicks are taken one after another; a list or a create that fails makes nothing.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Chat, ChatRefused, type ChatOptions } from '../src/chat.ts';
import { agentActor, agentRefused, nextAgentNumber } from '../src/chat-agent.ts';
import { readFence } from '../src/chat-deny.ts';
import { readConversations } from '../src/chat-conversations.ts';
import { bench, outsideStandIn, placeSandboxFiles, type Bench } from './harness.ts';

const fence = readFence(join(import.meta.dirname, '..'));

const clickA = '0b5c1e4f-1d7a-4c3e-9f6b-2a8d4e6f8a10';
const clickB = '7d1e2f3a-4b5c-4d6e-8f9a-0b1c2d3e4f50';

interface Rig {
    b: Bench;
    chat: Chat;
    campaigns: string;
    again(): Chat;
    jason(): string[][];
    made(): { id: string; name: string; idempotency_key?: string; project_id?: string }[];
}

function rig(t: { after(fn: () => void | Promise<void>): void }, options: Partial<ChatOptions> = {}): Rig {
    const removals: (() => void)[] = [];
    const b = bench({ after: (fn) => void removals.push(fn) }, { programs: ['claude', 'codex'] });
    placeSandboxFiles(b);
    const campaigns = join(b.root, 'campaigns.json');
    writeFileSync(campaigns, '[]');
    b.env.STUB_CAMPAIGNS_FILE = campaigns;
    const chats: Chat[] = [];
    const make = () => {
        const chat = new Chat({
            paths: b.paths,
            env: b.env,
            bundle: join(b.root, 'bundle'),
            dataNamed: true,
            bundleVersion: null,
            cli: b.cli,
            programs: b.programs,
            keyFor: () => undefined,
            fence: () => fence,
            sandboxOutside: outsideStandIn,
            emit: () => undefined,
            account: 'ada',
            ...options,
        });
        chats.push(chat);
        return chat;
    };
    const chat = make();
    t.after(async () => {
        for (const each of chats) each.closeAll();
        for (const each of chats) await each.idle();
        for (const remove of removals) remove();
    });
    return {
        b,
        chat,
        campaigns,
        again: make,
        jason: () => b.record().filter((each) => each.name === 'jason').map((each) => each.args),
        made: () => JSON.parse(readFileSync(campaigns, 'utf8')) as { id: string; name: string; idempotency_key?: string; project_id?: string }[],
    };
}

const plant = (r: Rig, items: { id: string; name: string; status?: string }[]) =>
    writeFileSync(r.campaigns, JSON.stringify(items.map((each) => ({ status: 'draft', created_at: '2026-01-01T00:00:00.000Z', ...each }))));
const creates = (r: Rig) => r.jason().filter((args) => args[0] === 'campaign' && args[1] === 'create');

test('the next number is one more than the highest exact "New agent N", gaps never filled, renamed agents not counted', () => {
    assert.equal(nextAgentNumber([]), 1);
    assert.equal(nextAgentNumber(['Spring', 'New agent', 'New agent 0', 'New agent 01', 'new agent 4', 'New agent 2 copy', ' New agent 3', 'New agent 3x']), 1);
    assert.equal(nextAgentNumber(['New agent 1', 'New agent 4', 'Spring']), 5);
    assert.equal(nextAgentNumber(['New agent 12', 'New agent 9']), 13);
});

test('the actor is the person by the account\'s name where it can stand, and none where it cannot', () => {
    assert.equal(agentActor('ada'), 'human:ada');
    assert.equal(agentActor('Ada Lovelace'), 'human:Ada Lovelace');
    for (const account of [null, undefined, '', ' ada', 'a;b', 'x'.repeat(101)]) assert.equal(agentActor(account), null, String(account));
});

test('a create that made nothing says the runtime\'s message, else the first diagnostic, after the desktop\'s words', () => {
    assert.equal(agentRefused({ exit: 1, stdout: '{"error":{"code":"validation_failed","message":"The name is not valid.","retryable":false}}\n', stderr: '' }), 'Jason could not make the agent: The name is not valid.');
    assert.equal(agentRefused({ exit: 2, stdout: '', stderr: '\nUnrecognized option --idempotency-key\nmore\n' }), 'Jason could not make the agent: Unrecognized option --idempotency-key.');
    assert.equal(agentRefused({ exit: 1, stdout: '', stderr: '' }), 'Jason could not make the agent.');
});

test('a click makes "New agent N" in the default project under its key and the person\'s actor, and keeps its conversation at once', async (t) => {
    const r = rig(t);
    plant(r, [
        { id: 'cmp_A', name: 'New agent 1' },
        { id: 'cmp_B', name: 'New agent 4', status: 'archived' },
        { id: 'cmp_C', name: 'Renamed from New agent 7' },
    ]);
    const made = await r.chat.createAgent(clickA);
    assert.deepEqual(r.jason(), [
        ['campaign', 'list', '--all-projects', '--include-archived', '--limit', '1000'],
        ['project', 'get', '--slug', 'default'],
        ['campaign', 'create', '--name', 'New agent 5', '--project', 'prj_01STUB', '--idempotency-key', `desktop-agent:${clickA}`, '--actor', 'human:ada'],
    ]);
    assert.deepEqual(made.campaign, { id: 'cmp_MADE4', name: 'New agent 5' });
    assert.deepEqual({ ...made.conversation, updatedAt: '' }, { id: made.conversation.id, campaignId: 'cmp_MADE4', title: 'New agent 5', updatedAt: '', running: false, mode: 'full_access', ownFolder: true, madeAtClick: true });
    // Kept before any turn: listed, and read back by a desktop started again.
    assert.deepEqual(r.chat.list().map((each) => each.id), [made.conversation.id]);
    const kept = readConversations(r.b.paths.conversations).kept;
    assert.deepEqual(kept.map((each) => [each.campaign_id, each.title, each.made_at_click]), [['cmp_MADE4', 'New agent 5', true]]);
    assert.equal(r.again().list()[0]?.madeAtClick, true);
    // The agent's conversation is the one its campaign has: a later create of the campaign finds it.
    assert.equal((await r.chat.create('cmp_MADE4')).id, made.conversation.id);
});

test('an account whose name cannot stand makes the agent under the CLI\'s own actor', async (t) => {
    const r = rig(t, { account: null });
    await r.chat.createAgent(clickA);
    assert.deepEqual(creates(r), [['campaign', 'create', '--name', 'New agent 1', '--project', 'prj_01STUB', '--idempotency-key', `desktop-agent:${clickA}`]]);
});

test('a retry of the same click asks the same create by the same name, and a create whose answer was lost gives that campaign back', async (t) => {
    const r = rig(t);
    r.b.env.STUB_CREATE_LOST = 'yes';
    await assert.rejects(r.chat.createAgent(clickA), { message: 'Jason could not make the agent.' });
    assert.deepEqual(r.chat.list(), [], 'nothing is kept for a click that made nothing the desktop heard of');
    // Meanwhile another agent appears under a higher number: the retry keeps the click's own name, so the key holds.
    plant(r, [...r.made(), { id: 'cmp_OTHER', name: 'New agent 7' }]);
    delete r.b.env.STUB_CREATE_LOST;
    const made = await r.chat.createAgent(clickA);
    assert.deepEqual(made.campaign, { id: 'cmp_MADE1', name: 'New agent 1' });
    assert.equal(r.made().filter((each) => each.idempotency_key === `desktop-agent:${clickA}`).length, 1, 'one campaign for the click');
    assert.deepEqual(creates(r).map((args) => args.slice(0, 8)), [
        ['campaign', 'create', '--name', 'New agent 1', '--project', 'prj_01STUB', '--idempotency-key', `desktop-agent:${clickA}`],
        ['campaign', 'create', '--name', 'New agent 1', '--project', 'prj_01STUB', '--idempotency-key', `desktop-agent:${clickA}`],
    ]);
    assert.equal(r.jason().filter((args) => args[1] === 'list' || args[0] === 'project').length, 2, 'the list and the project are read once, for the click\'s name');
    // The same click once more is the same agent and the same conversation.
    assert.equal((await r.chat.createAgent(clickA)).conversation.id, made.conversation.id);
    assert.equal(r.chat.list().length, 1);
});

test('two clicks at once are made one after another, N and N+1', async (t) => {
    const r = rig(t);
    plant(r, [{ id: 'cmp_A', name: 'New agent 2' }]);
    const [first, second] = await Promise.all([r.chat.createAgent(clickA), r.chat.createAgent(clickB)]);
    assert.deepEqual([first.campaign.name, second.campaign.name], ['New agent 3', 'New agent 4']);
    assert.deepEqual(r.jason().map((args) => args[1]), ['list', 'get', 'create', 'list', 'get', 'create'], 'the second list is read after the first create');
    assert.notEqual(first.conversation.id, second.conversation.id);
});

test('a list that cannot be read whole, an unreachable runtime and a refused create make nothing, each said in the desktop\'s words', async (t) => {
    const r = rig(t);
    r.b.env.STUB_LIST_FAIL_PAGE = 'all';
    await assert.rejects(r.chat.createAgent(clickA), { message: 'Jason is not running. Start it, then try again.' });
    assert.deepEqual(creates(r), [], 'no number is guessed');
    delete r.b.env.STUB_LIST_FAIL_PAGE;
    r.b.env.STUB_CREATE_FAILS = '3';
    await assert.rejects(r.chat.createAgent(clickA), { message: 'Jason is not running. Start it, then try again.' });
    r.b.env.STUB_CREATE_FAILS = '1';
    r.b.env.STUB_CREATE_SAYS = 'The name is too long.';
    await assert.rejects(r.chat.createAgent(clickB), (error: unknown) => error instanceof ChatRefused && error.message === 'Jason could not make the agent: The name is too long.');
    assert.deepEqual(r.chat.list(), []);
    assert.deepEqual(readConversations(r.b.paths.conversations).kept, []);
    // A failed click does not hold the next one back.
    delete r.b.env.STUB_CREATE_FAILS;
    assert.equal((await r.chat.createAgent(clickB)).campaign.name, 'New agent 1');
});

test('the agent is made in the default project, by its id, even when the desktop runs inside another project\'s folder', async (t) => {
    const r = rig(t);
    // Outside the bench: on Windows a folder a process was started in can stay held after the run, so its removal is tried
    // and a held one left in the temporary folder.
    const other = mkdtempSync(join(tmpdir(), 'jason-agent-cwd-'));
    t.after(() => {
        try {
            rmSync(other, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
        } catch {
            // Held still: the system's temporary folder keeps it.
        }
    });
    mkdirSync(join(other, '.jason'), { recursive: true });
    writeFileSync(join(other, '.jason', 'marker.json'), JSON.stringify({ marker_version: 1, kind: 'project', id: 'prj_01ACME' }));
    const was = process.cwd();
    process.chdir(other);
    t.after(() => process.chdir(was));
    const made = await r.chat.createAgent(clickA);
    assert.deepEqual(r.made().map((each) => [each.id, each.project_id]), [[made.campaign.id, 'prj_01STUB']]);
});

test('a default project that cannot be read makes nothing, said in the desktop\'s words', async (t) => {
    const r = rig(t);
    r.b.env.STUB_PROJECT_GET_FAILS = 'yes';
    await assert.rejects(r.chat.createAgent(clickA), { message: 'Jason is not running. Start it, then try again.' });
    assert.deepEqual(creates(r), []);
});

test('a click the page did not make as a click is refused before anything runs', async (t) => {
    const r = rig(t);
    for (const clickId of [undefined, null, '', 'cmp_1', clickA.toUpperCase(), `${clickA} `, 42]) {
        await assert.rejects(r.chat.createAgent(clickId), { message: 'That click is not one the desktop can make an agent for.' });
    }
    assert.deepEqual(r.jason(), []);
});
