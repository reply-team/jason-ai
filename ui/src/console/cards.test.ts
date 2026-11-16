import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
    blockLimit,
    cardLayout,
    controlWords,
    depthOf,
    readCard,
    splitCards,
    type CardBlock,
    type CardPlace,
    type Reading,
} from './cards';

// The chat's card protocol, read the way the page reads it: which fenced blocks are cards, which block of a turn is
// drawn, and what every block reads as, each row of the table of failures by name.

const fence = (body: string, info = 'card') => `\`\`\`${info}\n${body}\n\`\`\``;
const choice = (fields: Record<string, unknown> = {}) =>
    JSON.stringify({ kind: 'choice', question: 'Who should the emails come from?', options: two, ...fields });
const two = [{ label: 'From me' }, { label: 'From someone else on my team' }];

const closed = (body: string): CardBlock => ({ body, closed: true });
type Where = { place: CardPlace; campaignId: string | null; ended: boolean };
const setup: Where = { place: 'setup', campaignId: null, ended: true };
const agent: Where = { place: 'agent', campaignId: 'cmp_1', ended: true };
const feed: Where = { place: 'feed', campaignId: null, ended: true };
const read = (body: string, where = setup): Reading => readCard(closed(body), where);
const object = (value: unknown, where = setup): Reading => read(JSON.stringify(value), where);
const failure = (reading: Reading) =>
    reading.state === 'failed' ? `${reading.kind} ${reading.failure}` : reading.state;
const schema = (kind: string) => `${kind} schema`;

describe('which fenced block is a card', () => {
    it('is a block whose fence opens in the first column with the info string card, taken out of the words', () => {
        const split = splitCards(`Here are two ways.\n\n${fence(choice())}`);
        expect(split.blocks).toEqual([{ body: choice(), closed: true }]);
        expect(split.words).not.toContain('kind');
        expect(split.words.trim()).toBe('Here are two ways.');
    });

    it('takes trailing spaces after the word, a tilde fence and a longer fence', () => {
        expect(splitCards(fence(choice(), 'card   ')).blocks).toHaveLength(1);
        expect(splitCards(`~~~card\n${choice()}\n~~~`).blocks).toHaveLength(1);
        expect(splitCards(`\`\`\`\`card\n${choice()}\n\`\`\`\`\``).blocks).toHaveLength(1);
    });

    it.each([
        ['an indented fence', `  ${fence(choice()).replace(/\n/g, '\n  ')}`],
        ['a fence with the info string json', fence(choice(), 'json')],
        ['a fence with the info string Card', fence(choice(), 'Card')],
        ['a fence with card and another word', fence(choice(), 'card json')],
        ['a space between the fence and card', fence(choice(), ' card')],
        ['JSON written on the fence line', `\`\`\`card ${choice()}\n\`\`\``],
        ['a fence inside a list item', `- Two ways:\n\n  ${fence(choice()).replace(/\n/g, '\n  ')}`],
        ['a fence inside a quote', `> ${fence(choice()).replace(/\n/g, '\n> ')}`],
        ['a card fence inside another fence', `\`\`\`\`\n${fence(choice())}\n\`\`\`\``],
    ])('is never %s, which stays the words', (_, text) => {
        const split = splitCards(text);
        expect(split.blocks).toEqual([]);
        expect(split.words).toBe(text);
    });

    it('keeps the words after a block, and takes out every block of a text in order', () => {
        const split = splitCards(
            `First.\n${fence('{"kind":"needs"}')}\nBetween.\n${fence('{"kind":"accounts"}')}\nAfter.`,
        );
        expect(split.blocks.map((each) => each.body)).toEqual(['{"kind":"needs"}', '{"kind":"accounts"}']);
        expect(split.words).toBe('First.\n\nBetween.\n\nAfter.');
    });

    it('reads a text whose lines end in CRLF', () => {
        expect(splitCards(`Two ways.\r\n\`\`\`card\r\n${choice()}\r\n\`\`\``).blocks).toEqual([
            { body: choice(), closed: true },
        ]);
    });

    it('takes out a block no fence closes, to the end of the text', () => {
        const split = splitCards(`Thinking.\n\`\`\`card\n{"kind":"cho`);
        expect(split.blocks).toEqual([{ body: '{"kind":"cho', closed: false }]);
        expect(split.words.trim()).toBe('Thinking.');
    });
});

describe('which block of a turn is drawn', () => {
    it('is the last block of the turn, across two of its texts, placed after the text that held it', () => {
        const layout = cardLayout([
            { kind: 'user', text: 'Hello' },
            { kind: 'text', text: `One.\n${fence('{"kind":"needs"}')}` },
            { kind: 'step' },
            { kind: 'text', text: `Two.\n${fence(choice())}` },
        ]);
        expect([...layout.cards.keys()]).toEqual([3]);
        expect(layout.cards.get(3)).toEqual({ body: choice(), closed: true, lastTurn: true });
        // Both blocks are out of the words.
        expect(layout.words.get(1)?.trim()).toBe('One.');
        expect(layout.words.get(3)?.trim()).toBe('Two.');
    });

    it('draws one card for each turn, only the last turn being the chat’s last', () => {
        const layout = cardLayout([
            { kind: 'text', text: fence('{"kind":"accounts"}') },
            { kind: 'user', text: 'And?' },
            { kind: 'text', text: fence(choice()) },
            { kind: 'user', text: 'From me' },
            { kind: 'text', text: 'Noted.' },
        ]);
        expect([...layout.cards.entries()].map(([index, card]) => [index, card.lastTurn])).toEqual([
            [0, false],
            [2, false],
        ]);
        expect(layout.words.has(4)).toBe(false);
    });

    it('never reads a person’s own message, which is drawn as they wrote it', () => {
        const layout = cardLayout([{ kind: 'user', text: fence(choice()) }]);
        expect(layout.cards.size).toBe(0);
        expect(layout.words.size).toBe(0);
    });
});

describe('what a block reads as', () => {
    it('reads a choice whose schema is met', () => {
        expect(read(choice())).toEqual({
            state: 'card',
            card: { kind: 'choice', question: 'Who should the emails come from?', options: two },
        });
    });

    it.each([
        ['kind missing', { question: 'q', options: two }],
        ['kind not in the allowlist', { kind: 'chart' }],
        ['kind not a string', { kind: 5 }],
    ])('says kind_unknown for %s', (_, value) => expect(failure(object(value))).toBe('unknown kind_unknown'));

    it.each([
        ['JSON that does not parse', '{"kind":"choice",'],
        ['JSON with a trailing comma, which is never mended', '{"kind":"accounts",}'],
        ['JSON in single quotes', "{'kind':'accounts'}"],
        ['an array', '[{"kind":"accounts"}]'],
        ['a string', '"accounts"'],
        ['null', 'null'],
    ])('says malformed for %s', (_, body) => expect(failure(read(body))).toBe('unknown malformed'));

    it('says malformed for a block no fence closed once the turn has ended, and draws nothing while it runs', () => {
        expect(failure(readCard({ body: choice(), closed: false }, setup))).toBe('unknown malformed');
        expect(readCard({ body: '{"kind":"cho', closed: false }, { ...setup, ended: false })).toEqual({
            state: 'pending',
        });
    });

    it('measures the 16,000 characters before the parse, in code points, so no kind is named past them', () => {
        const filled = (count: number, char: string) => {
            const head = '{"kind":"choice","question":"';
            const tail = '","options":[]}';
            return head + char.repeat(count - head.length - tail.length) + tail;
        };
        for (const char of ['ж', '𝔵']) {
            const edge = filled(blockLimit, char);
            expect(Array.from(edge)).toHaveLength(16_000);
            // At the edge it is parsed: its question is too long for a choice.
            expect(failure(read(edge))).toBe(schema('choice'));
            expect(failure(read(filled(blockLimit + 1, char)))).toBe('unknown too_long');
        }
        // Over the cap nothing is parsed: not even a block that is no JSON at all says more.
        expect(failure(read('x'.repeat(blockLimit + 1)))).toBe('unknown too_long');
    });

    it('counts nesting from the card’s own object, and refuses past four levels', () => {
        expect(depthOf({ kind: 'plan', steps: [{ body: 'x' }] })).toBe(3);
        expect(depthOf({ a: [[{}]] })).toBe(4);
        expect(depthOf({ a: [[[{}]]] })).toBe(5);
        // A nesting far past the limit is read no deeper than one past it.
        let deep: unknown = 1;
        for (let level = 0; level < 5000; level++) deep = [deep];
        expect(depthOf(deep)).toBe(6);
        expect(failure(object({ kind: 'needs', x: [[{}]] }))).toBe(schema('needs'));
        expect(
            failure(
                object({ kind: 'choice', question: 'q', options: [{ label: 'a', detail: [[1]] }, { label: 'b' }] }),
            ),
        ).toBe(schema('choice'));
    });

    it.each([
        ['a required member absent', { kind: 'choice', options: two }],
        ['a member of the wrong type', { kind: 'choice', question: 5, options: two }],
        ['an own key not in the schema', { kind: 'choice', question: 'q', options: two, recommended: 0 }],
        ['a value the choice does not take', { kind: 'choice', question: 'q', options: two, multi: true }],
        [
            'an option with a member not in the schema',
            { kind: 'choice', question: 'q', options: [{ label: 'a', value: 'x' }, { label: 'b' }] },
        ],
        [
            'a count over its limit',
            { kind: 'choice', question: 'q', options: [1, 2, 3, 4, 5, 6].map((n) => ({ label: `L${n}` })) },
        ],
        ['a campaign id not of the desktop’s shape', { kind: 'needs', campaign: 'cmp_1;drop' }],
        ['a campaign id that is no string', { kind: 'needs', campaign: 1 }],
        ['an accounts card with a member', { kind: 'accounts', campaign: 'cmp_1' }],
    ])('says schema for %s', (_, value) => expect(failure(object(value))).toMatch(/ schema$/));

    it('refuses __proto__, which JSON.parse makes an own key', () => {
        expect(failure(read('{"kind":"accounts","__proto__":{"x":1}}'))).toBe(schema('accounts'));
        expect(
            failure(read(`{"kind":"choice","question":"q","options":[{"label":"a","__proto__":{}},{"label":"b"}]}`)),
        ).toBe(schema('choice'));
    });

    it('reads duplicate keys as JSON.parse does, the last winning, and validates what won', () => {
        const won = read(
            '{"kind":"choice","question":"First","question":"Second","options":[{"label":"a"},{"label":"b"}]}',
        );
        expect(won.state === 'card' && won.card.kind === 'choice' && won.card.question).toBe('Second');
        expect(
            failure(read('{"kind":"choice","question":"Fine","question":5,"options":[{"label":"a"},{"label":"b"}]}')),
        ).toBe(schema('choice'));
        expect(failure(read('{"kind":"needs","kind":"chart"}'))).toBe('unknown kind_unknown');
    });

    it('holds each kind to where it may stand', () => {
        const plan = { kind: 'plan', title: 'Spring' };
        expect(object(plan, agent).state).toBe('card');
        expect(object(plan, setup).state).toBe('card');
        expect(failure(object(plan, feed))).toBe(schema('plan'));
        // An agent's status names a campaign outside the agent's own chat.
        expect(object({ kind: 'agent_status' }, agent).state).toBe('card');
        expect(failure(object({ kind: 'agent_status' }, setup))).toBe(schema('agent_status'));
        expect(failure(object({ kind: 'agent_status' }, feed))).toBe(schema('agent_status'));
        expect(object({ kind: 'agent_status', campaign: 'cmp_2' }, feed).state).toBe('card');
        // A people list keeps to a standing only of one campaign.
        expect(object({ kind: 'people_list', standing: 'waiting' }, agent).state).toBe('card');
        expect(failure(object({ kind: 'people_list', standing: 'waiting' }, setup))).toBe(schema('people_list'));
        expect(object({ kind: 'people_list', campaign: 'cmp_2', standing: 'waiting' }, setup).state).toBe('card');
        expect(failure(object({ kind: 'people_list', campaign: 'cmp_2', standing: 'interested' }, setup))).toBe(
            schema('people_list'),
        );
    });

    it('draws the frame that says so for another campaign’s id in an agent’s chat, which is no schema failure', () => {
        for (const kind of ['people_list', 'agent_status', 'needs'])
            expect(object({ kind, campaign: 'cmp_2' }, agent)).toEqual({ state: 'elsewhere', kind });
        expect(object({ kind: 'needs', campaign: 'cmp_1' }, agent)).toEqual({
            state: 'card',
            card: { kind: 'needs', campaign: 'cmp_1' },
        });
    });
});

describe('a choice’s members', () => {
    const at = (count: number, char = 'x') => char.repeat(count);
    it.each([
        ['question', (n: number) => ({ question: at(n) }), 200],
        ['label', (n: number) => ({ options: [{ label: at(n) }, { label: 'b' }] }), 80],
        ['detail', (n: number) => ({ options: [{ label: 'a', detail: at(n) }, { label: 'b' }] }), 100],
    ])('holds %s to its limit at the edge, and refuses one over it, never cutting it', (_, fields, limit) => {
        const edge = read(choice(fields(limit)));
        expect(edge.state).toBe('card');
        expect(failure(read(choice(fields(limit + 1))))).toBe(schema('choice'));
    });

    it('counts a member’s characters in code points, so a character of four bytes is one', () => {
        expect(read(choice({ question: '𝔵'.repeat(200) })).state).toBe('card');
        expect(failure(read(choice({ question: '𝔵'.repeat(201) })))).toBe(schema('choice'));
    });

    it('takes two to five options', () => {
        const labels = (n: number) => Array.from({ length: n }, (_, i) => ({ label: `Option ${i + 1}` }));
        expect(failure(read(choice({ options: labels(1) })))).toBe(schema('choice'));
        expect(read(choice({ options: labels(2) })).state).toBe('card');
        expect(read(choice({ options: labels(5) })).state).toBe('card');
        expect(failure(read(choice({ options: labels(6) })))).toBe(schema('choice'));
        expect(failure(read(choice({ options: [] })))).toBe(schema('choice'));
    });

    it('trims its strings, refuses one empty once trimmed, one that breaks a line, and one with a character a title drops', () => {
        const trimmed = read(choice({ options: [{ label: '  From me  ' }, { label: 'Not me' }] }));
        expect(trimmed.state === 'card' && trimmed.card.kind === 'choice' && trimmed.card.options[0]!.label).toBe(
            'From me',
        );
        expect(failure(read(choice({ question: '   ' })))).toBe(schema('choice'));
        expect(failure(read(choice({ question: 'Who\nsends?' })))).toBe(schema('choice'));
        expect(failure(read(choice({ question: 'Who​sends?' })))).toBe(schema('choice'));
        expect(failure(read(choice({ options: [{ label: 'a\u0007' }, { label: 'b' }] })))).toBe(schema('choice'));
    });

    it('takes each label once, case-folded', () => {
        expect(failure(read(choice({ options: [{ label: 'Yes' }, { label: ' yes ' }] })))).toBe(schema('choice'));
    });

    it('refuses a label that is a control’s word, trimmed and case-folded, and takes one that only contains it', () => {
        expect(controlWords).toEqual([
            'approve',
            'reject',
            'edit',
            'approve and run',
            'keep planning',
            'allow',
            'allow for this chat',
            "don't allow",
            'start',
            'resume',
            'pause',
            'send',
        ]);
        for (const word of controlWords) {
            const shouted = ` ${word.toUpperCase()} `;
            expect(failure(read(choice({ options: [{ label: shouted }, { label: 'Something else' }] }))), word).toBe(
                schema('choice'),
            );
        }
        expect(read(choice({ options: [{ label: 'Yes, start it' }, { label: 'Not yet' }] })).state).toBe('card');
    });
});

describe('a plan’s members', () => {
    const at = (count: number, char = 'x') => char.repeat(count);
    const option = (i: number) => ({ label: at(80, String.fromCharCode(97 + i)), detail: at(100) });
    const fullest = () => ({
        kind: 'plan',
        title: at(80),
        who: at(200),
        offer: at(200),
        sender: at(120),
        goal: at(120),
        channels: ['email', 'linkedin'],
        steps: Array.from({ length: 8 }, () => ({
            when: at(30),
            channel: 'linkedin',
            subject: at(120),
            body: at(1200),
        })),
        rules: Array.from({ length: 4 }, () => at(150)),
        manual: Array.from({ length: 4 }, () => at(150)),
        question: at(200),
        options: [0, 1, 2, 3, 4].map(option),
    });

    it('reads a plan at every maximum, which fits under the cap with room to spare', () => {
        const text = JSON.stringify(fullest());
        expect(text.length).toBe(14_574);
        expect(blockLimit - text.length).toBe(1_426);
        expect(read(text).state).toBe('card');
        // Eight bodies with a line break every twenty characters still fit.
        const broken = fullest();
        for (const step of broken.steps) step.body = at(1200).replace(/(.{19})./g, '$1\n');
        const brokenText = JSON.stringify(broken, null, 2);
        expect(Array.from(brokenText).length).toBeLessThan(blockLimit);
        expect(read(brokenText).state).toBe('card');
        // A choice at every maximum.
        expect(JSON.stringify({ kind: 'choice', question: at(200), options: [0, 1, 2, 3, 4].map(option) }).length).toBe(
            1_268,
        );
    });

    it.each([
        ['title', 80, (n: number) => ({ title: at(n) })],
        ['who', 200, (n: number) => ({ who: at(n) })],
        ['offer', 200, (n: number) => ({ offer: at(n) })],
        ['sender', 120, (n: number) => ({ sender: at(n) })],
        ['goal', 120, (n: number) => ({ goal: at(n) })],
        ['a step’s when', 30, (n: number) => ({ steps: [{ when: at(n), channel: 'email' }] })],
        ['a step’s subject', 120, (n: number) => ({ steps: [{ subject: at(n), channel: 'email' }] })],
        ['a step’s body', 1200, (n: number) => ({ steps: [{ body: at(n), channel: 'email' }] })],
        ['a rule', 150, (n: number) => ({ rules: [at(n)] })],
        ['a line done by hand', 150, (n: number) => ({ manual: [at(n)] })],
        ['its question', 200, (n: number) => ({ question: at(n), options: two })],
    ])('holds %s to %d characters', (_, limit, fields) => {
        const plan = (n: number) => ({ kind: 'plan', title: 'Spring', channels: ['email'], ...fields(n) });
        expect(object(plan(limit)).state).toBe('card');
        expect(failure(object(plan(limit + 1)))).toBe(schema('plan'));
    });

    it('holds its lists to their counts, and its steps to its channels', () => {
        const plan = (fields: object) => object({ kind: 'plan', title: 'Spring', ...fields });
        expect(
            plan({ channels: ['email'], steps: Array.from({ length: 8 }, () => ({ channel: 'email' })) }).state,
        ).toBe('card');
        expect(
            failure(plan({ channels: ['email'], steps: Array.from({ length: 9 }, () => ({ channel: 'email' })) })),
        ).toBe(schema('plan'));
        expect(plan({ steps: [] }).state).toBe('card');
        expect(failure(plan({ channels: [] }))).toBe(schema('plan'));
        expect(failure(plan({ channels: ['email', 'linkedin', 'email'] }))).toBe(schema('plan'));
        expect(failure(plan({ channels: ['email', 'email'] }))).toBe(schema('plan'));
        expect(failure(plan({ channels: ['sms'] }))).toBe(schema('plan'));
        expect(failure(plan({ channels: ['email'], steps: [{ channel: 'linkedin' }] }))).toBe(schema('plan'));
        expect(failure(plan({ steps: [{ channel: 'email' }] }))).toBe(schema('plan'));
        expect(failure(plan({ channels: ['email'], steps: [{ subject: 'Hi' }] }))).toBe(schema('plan'));
        expect(failure(plan({ rules: ['a', 'b', 'c', 'd', 'e'] }))).toBe(schema('plan'));
        expect(failure(plan({ manual: ['a', 'b', 'c', 'd', 'e'] }))).toBe(schema('plan'));
        expect(plan({ rules: [], manual: [] }).state).toBe('card');
    });

    it('breaks lines only in a draft’s body', () => {
        const plan = (fields: object) => object({ kind: 'plan', title: 'Spring', channels: ['email'], ...fields });
        const body = plan({ steps: [{ channel: 'email', body: 'Hi Ada,\n\nA short note.' }] });
        expect(body.state === 'card' && body.card.kind === 'plan' && body.card.steps?.[0]?.body).toBe(
            'Hi Ada,\n\nA short note.',
        );
        expect(failure(plan({ steps: [{ channel: 'email', subject: 'Hi\nthere' }] }))).toBe(schema('plan'));
        expect(failure(plan({ who: 'Heads\nof sales' }))).toBe(schema('plan'));
        expect(failure(plan({ steps: [{ channel: 'email', body: 'Tab\there' }] }))).toBe(schema('plan'));
    });

    it('has no numeric member, and asks its question with its answers or not at all', () => {
        const plan = (fields: object) => object({ kind: 'plan', title: 'Spring', ...fields });
        expect(failure(plan({ pilot: 20 }))).toBe(schema('plan'));
        expect(failure(plan({ approvals: true }))).toBe(schema('plan'));
        expect(failure(plan({ mode: 'adaptive' }))).toBe(schema('plan'));
        expect(failure(plan({ question: 'Shall I?' }))).toBe(schema('plan'));
        expect(failure(plan({ options: two }))).toBe(schema('plan'));
        expect(plan({ question: 'Does this look right?', options: two }).state).toBe('card');
        expect(failure(plan({ question: 'Shall I?', options: [{ label: 'Send' }, { label: 'Wait' }] }))).toBe(
            schema('plan'),
        );
    });
});

describe('the people list’s members', () => {
    it('holds its search to 100 characters', () => {
        expect(object({ kind: 'people_list', search: 'x'.repeat(100) }).state).toBe('card');
        expect(failure(object({ kind: 'people_list', search: 'x'.repeat(101) }))).toBe(schema('people_list'));
    });
});

describe('the chat’s instructions', () => {
    // Every example card the instructions show reads as a card of its kind: an example the parser would refuse is a
    // lesson that fails.
    const instructions = readFileSync(
        join(import.meta.dirname, '..', '..', '..', 'desktop', 'chat-instructions.md'),
        'utf8',
    );

    /** Each JSON object in the text that begins with a kind, by its balanced braces. */
    function examples(text: string): string[] {
        const found: string[] = [];
        for (let start = text.indexOf('{"kind":'); start !== -1; start = text.indexOf('{"kind":', start + 1)) {
            let depth = 0;
            let quoted = false;
            for (let at = start; at < text.length; at++) {
                const char = text[at];
                if (quoted) {
                    if (char === '\\') at++;
                    else if (char === '"') quoted = false;
                } else if (char === '"') quoted = true;
                else if (char === '{') depth++;
                else if (char === '}' && --depth === 0) {
                    found.push(text.slice(start, at + 1));
                    break;
                }
            }
        }
        return found;
    }

    it('shows each kind this build draws and no other, and every example reads as a card of its kind', () => {
        const shown = examples(instructions);
        expect(shown.map((each) => (JSON.parse(each) as { kind: string }).kind)).toEqual([
            'choice',
            'people_list',
            'agent_status',
            'needs',
        ]);
        for (const example of shown) expect(read(example).state, example).toBe('card');
    });
});
