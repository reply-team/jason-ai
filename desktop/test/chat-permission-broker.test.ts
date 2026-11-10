import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';
import { PermissionBroker, type Decided } from '../src/chat-permission-broker.ts';
import type { ChatScope } from '../src/chat-permission-model.ts';

/** A scope on macOS' semantics where every path is what it says it is. */
const scope: ChatScope = {
    platform: 'darwin',
    cwd: '/Users/someone/.jason/desktop/chat',
    chat: '/Users/someone/.jason/desktop/chat',
    added: [],
    data: '/Users/someone/.jason',
    protectedFiles: ['/Users/someone/.jason/config/settings.json'],
    denied: ['approval approve', 'runtime'],
    deniedOperations: ['approval.approve'],
    home: '/home/someone',
    real: (path) => path,
};

interface Sent {
    chat: string;
    event: { kind: string; request?: string; title?: string; detail?: string; remember?: string | null };
}

/** Every broker a row made: what it still waits on ends with the row. */
const made: PermissionBroker[] = [];
afterEach(() => {
    for (const broker of made.splice(0)) broker.endAll();
});

function bench(timeoutMs?: number) {
    const sent: Sent[] = [];
    const decided: Decided[] = [];
    let next = 0;
    const broker = new PermissionBroker({
        emit: (chat, event) => sent.push({ chat, event }),
        decided: (each) => decided.push(each),
        newId: () => `req_${++next}`,
        ...(timeoutMs === undefined ? {} : { timeoutMs }),
    });
    made.push(broker);
    const turn = (chat: string, id: string) => broker.openTurn(chat, id, scope);
    return { broker, sent, decided, turn };
}

const outside = { file_path: '/Users/someone/notes/plan.txt' };
const command = { command: 'git status' };
/** A new card's fields that say it is a new card. */
const card = (request: string) => ({ kind: 'permission', request, ...cardText });
const cardText = { title: 'Run a command', detail: 'git status', remember: 'git status \u2026' };
/** A promise that must settle soon: a row fails rather than waits forever. */
function within<T>(promise: Promise<T>, ms = 1000): Promise<T> {
    let timer: NodeJS.Timeout | undefined;
    const late = new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('still waiting')), ms);
    });
    return Promise.race([promise, late]).finally(() => clearTimeout(timer));
}
const settle = () => new Promise((resolve) => setImmediate(resolve));

test('what the model allows is allowed with its input as received, and no card is shown', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const input = { file_path: '/Users/someone/.jason/desktop/chat/a.md' };
    assert.deepEqual(await broker.ask('turn_1', 'Read', input), { behavior: 'allow', updatedInput: input });
    assert.deepEqual(sent, []);
    assert.deepEqual(decided, [{ outcome: 'allowed', by: 'model' }]);
});

test('what the model refuses is refused with its message, and no card is shown', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const answer = await broker.ask('turn_1', 'Read', { file_path: '/Users/someone/.jason/config/settings.json' });
    assert.equal(answer.behavior, 'deny');
    assert.match(answer.behavior === 'deny' ? answer.message : '', /installation's own data/);
    assert.deepEqual(sent, []);
    assert.deepEqual(decided, [{ outcome: 'refused', by: 'model' }]);
});

test('a card is shown to the asking chat, and the person allowing it allows the call with its input', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const asked = broker.ask('turn_1', 'Read', outside);
    await settle();
    assert.deepEqual(sent, [{ chat: 'chat_a', event: { kind: 'permission', request: 'req_1', title: 'Read a file outside the chat\'s folders', detail: '/Users/someone/notes/plan.txt', remember: 'files in /Users/someone/notes' } }]);
    broker.answer('chat_a', 'req_1', { allow: true, always: false });
    assert.deepEqual(await asked, { behavior: 'allow', updatedInput: outside });
    assert.deepEqual(decided, [{ outcome: 'allowed', by: 'person' }]);
});

test('the person refusing a card refuses the call with a message the agent reads', async () => {
    const { broker, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const asked = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: false, always: false });
    const answer = await asked;
    assert.equal(answer.behavior, 'deny');
    assert.match(answer.behavior === 'deny' ? answer.message : '', /said no/);
    assert.deepEqual(decided, [{ outcome: 'refused', by: 'person' }]);
});

test('a request is answered once: a second answer changes nothing and says the card is gone', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const asked = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: false, always: false });
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    assert.equal((await asked).behavior, 'deny');
    assert.deepEqual(sent.slice(1), [{ chat: 'chat_a', event: { kind: 'permission_gone', request: 'req_1' } }]);
    assert.equal(decided.length, 1);
    // The second answer's "always" was not kept: the same command asks again.
    void broker.ask('turn_1', 'Bash', command);
    await settle();
    assert.deepEqual(sent.at(-1)?.event, card('req_2'));
});

test('an unknown request changes nothing and says the card is gone', () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    broker.answer('chat_a', 'req_404', { allow: true, always: true });
    assert.deepEqual(sent, [{ chat: 'chat_a', event: { kind: 'permission_gone', request: 'req_404' } }]);
});

test('an answer from another chat changes nothing there and says the card is gone; the asking chat can still answer', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    turn('chat_b', 'turn_2');
    const asked = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_b', 'req_1', { allow: true, always: true });
    assert.deepEqual(sent.at(-1), { chat: 'chat_b', event: { kind: 'permission_gone', request: 'req_1' } });
    broker.answer('chat_a', 'req_1', { allow: false, always: false });
    assert.equal((await asked).behavior, 'deny');
    // chat_b's "always" was not kept for chat_a's command.
    void broker.ask('turn_1', 'Bash', command);
    await settle();
    assert.deepEqual(sent.at(-1)?.event, card('req_2'));
});

test('"always" holds for the chat: the same kind of request is settled without a card, by the person', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const first = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    await first;
    const cards = sent.length;
    turn('chat_a', 'turn_3');
    assert.deepEqual(await within(broker.ask('turn_3', 'Bash', { command: 'git status --short' })), { behavior: 'allow', updatedInput: { command: 'git status --short' } });
    assert.equal(sent.length, cards);
    assert.deepEqual(decided.at(-1), { outcome: 'allowed', by: 'person' });
});

test('"always" on a web page holds for its host: a second page of it opens without a card, another host asks again', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const first = broker.ask('turn_1', 'WebFetch', { url: 'https://example.com/a', prompt: 'p' });
    await settle();
    assert.deepEqual(sent.at(-1)?.event, { kind: 'permission', request: 'req_1', title: 'Open a web page', detail: 'https://example.com/a', remember: 'pages on example.com' });
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    await first;
    const cards = sent.length;
    assert.deepEqual(await within(broker.ask('turn_1', 'WebFetch', { url: 'https://example.com/b', prompt: 'p' })), { behavior: 'allow', updatedInput: { url: 'https://example.com/b', prompt: 'p' } });
    assert.equal(sent.length, cards);
    assert.deepEqual(decided.at(-1), { outcome: 'allowed', by: 'person' });
    void broker.ask('turn_1', 'WebFetch', { url: 'https://example.org/a', prompt: 'p' });
    await settle();
    assert.deepEqual(sent.at(-1)?.event, { kind: 'permission', request: 'req_2', title: 'Open a web page', detail: 'https://example.org/a', remember: 'pages on example.org' });
});

test('a remembered grant never allows what the model refuses: the refusal is the model\'s, with no card', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    // Two grants whose keys a refused request would carry were it a card: files in the home folder, and git status.
    const home = broker.ask('turn_1', 'Read', { file_path: '/Users/someone/notes.md' });
    await settle();
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    await home;
    const git = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_2', { allow: true, always: true });
    await git;
    const cards = sent.length;
    for (const [tool, input] of [['Read', { file_path: '/Users/someone/.jason' }], ['Bash', { command: 'git status /Users/someone/.jason/run' }],
        ['Bash', { command: 'git status $JASON_DATA_DIR/state' }]] as const) {
        const answer = await within(broker.ask('turn_1', tool, input));
        assert.equal(answer.behavior, 'deny', `${tool} ${JSON.stringify(input)}`);
        assert.match(answer.behavior === 'deny' ? answer.message : '', /installation's own data/);
        assert.deepEqual(decided.at(-1), { outcome: 'refused', by: 'model' });
    }
    assert.equal(sent.length, cards);
    // The grants still hold for what the model leaves to the person.
    assert.equal((await within(broker.ask('turn_1', 'Read', { file_path: '/Users/someone/other.md' }))).behavior, 'allow');
});

test('"always" holds for that chat only', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    const first = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    await first;
    turn('chat_b', 'turn_2');
    void broker.ask('turn_2', 'Bash', command);
    await settle();
    assert.deepEqual(sent.at(-1)?.chat, 'chat_b');
    assert.deepEqual(sent.at(-1)?.event, card('req_2'));
});

test('"always" on a card that cannot be remembered covers that request alone', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    const write = { file_path: '/Users/someone/notes/plan.txt', content: 'x' };
    const first = broker.ask('turn_1', 'Write', write);
    await settle();
    assert.equal(sent[0]?.event.remember, null);
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    assert.equal((await first).behavior, 'allow');
    void broker.ask('turn_1', 'Write', write);
    await settle();
    assert.deepEqual([sent.at(-1)?.event.kind, sent.at(-1)?.event.request], ['permission', 'req_2']);
});

test('a request the host withdraws is refused, its card goes, and an answer after it changes nothing', async () => {
    const { broker, sent, decided, turn } = bench();
    turn('chat_a', 'turn_1');
    const withdrawn = new AbortController();
    const asked = broker.ask('turn_1', 'Bash', command, withdrawn.signal);
    await settle();
    assert.deepEqual(sent.map((each) => each.event.kind), ['permission']);
    withdrawn.abort();
    const answer = await within(asked);
    assert.equal(answer.behavior, 'deny');
    assert.deepEqual(sent.at(-1)?.event, { kind: 'permission_gone', request: 'req_1' });
    assert.deepEqual(decided, [{ outcome: 'withdrawn', by: 'host' }]);
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    assert.deepEqual(decided, [{ outcome: 'withdrawn', by: 'host' }]);
    // A request withdrawn before it is asked makes no card.
    const early = new AbortController();
    early.abort();
    assert.equal((await broker.ask('turn_1', 'Bash', command, early.signal)).behavior, 'deny');
    assert.equal(sent.filter((each) => each.event.kind === 'permission').length, 1);
});

test('"always" never holds for a refusal', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    const first = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: false, always: true });
    await first;
    void broker.ask('turn_1', 'Bash', command);
    await settle();
    assert.deepEqual(sent.at(-1)?.event, card('req_2'));
});

test('a request nobody answers is refused when the time is up, and its card goes', async () => {
    const { broker, sent, decided, turn } = bench(20);
    turn('chat_a', 'turn_1');
    const answer = await broker.ask('turn_1', 'Bash', command);
    assert.equal(answer.behavior, 'deny');
    assert.match(answer.behavior === 'deny' ? answer.message : '', /Nobody answered/);
    assert.deepEqual(sent.at(-1), { chat: 'chat_a', event: { kind: 'permission_gone', request: 'req_1' } });
    assert.deepEqual(decided, [{ outcome: 'timed_out', by: 'person' }]);
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    assert.equal(decided.length, 1);
});

test('the wait is thirty minutes unless a test says otherwise', () => {
    assert.equal(PermissionBroker.unanswered, 30 * 60_000);
});

test('a turn ending refuses everything it waits on and takes the cards away; later asks for it are refused', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    const one = broker.ask('turn_1', 'Bash', command);
    const two = broker.ask('turn_1', 'Read', outside);
    await settle();
    broker.endTurn('turn_1');
    assert.equal((await within(one)).behavior, 'deny');
    assert.equal((await within(two)).behavior, 'deny');
    assert.deepEqual(sent.slice(2).map((each) => each.event), [{ kind: 'permission_gone', request: 'req_1' }, { kind: 'permission_gone', request: 'req_2' }]);
    const late = await broker.ask('turn_1', 'Bash', command);
    assert.equal(late.behavior, 'deny');
    assert.equal(sent.length, 4);
});

test('a turn ending leaves another turn\'s requests waiting', async () => {
    const { broker, turn } = bench();
    turn('chat_a', 'turn_1');
    turn('chat_b', 'turn_2');
    void broker.ask('turn_1', 'Bash', command);
    const other = broker.ask('turn_2', 'Bash', command);
    await settle();
    broker.endTurn('turn_1');
    broker.answer('chat_b', 'req_2', { allow: true, always: false });
    assert.equal((await other).behavior, 'allow');
});

test('closing the window ends every pending request of every chat and forgets every "always"', async () => {
    const { broker, sent, turn } = bench();
    turn('chat_a', 'turn_1');
    const first = broker.ask('turn_1', 'Bash', command);
    await settle();
    broker.answer('chat_a', 'req_1', { allow: true, always: true });
    await first;
    turn('chat_b', 'turn_2');
    const waiting = broker.ask('turn_2', 'Read', outside);
    await settle();
    broker.endAll();
    assert.equal((await waiting).behavior, 'deny');
    assert.deepEqual(sent.at(-1), { chat: 'chat_b', event: { kind: 'permission_gone', request: 'req_2' } });
    turn('chat_a', 'turn_3');
    void broker.ask('turn_3', 'Bash', command);
    await settle();
    assert.deepEqual(sent.at(-1)?.event, card('req_3'));
});

test('an input too large to send back is allowed without being copied', async () => {
    const { broker, turn } = bench();
    turn('chat_a', 'turn_1');
    const input = { file_path: '/Users/someone/.jason/desktop/chat/big.md', content: 'x'.repeat(40_000) };
    assert.deepEqual(await broker.ask('turn_1', 'Write', input), { behavior: 'allow' });
});
