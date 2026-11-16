import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
    blockLimit,
    cardKinds,
    cardLayout,
    controlWords,
    depthOf,
    foldLabel,
    lastCardOf,
    nameWords,
    passiveWords,
    readCard,
    reservedCustomKeys,
    splitCards,
    type CardBlock,
    type CardPlace,
    type Reading,
} from './cards';
import { chipWords } from './chips';

// The chat's card protocol, read the way the page reads it: which fenced blocks are cards, which block of a turn is
// drawn, and what every block reads as, each row of the table of failures by name.

/** Every C# source file under a folder. */
const sources = (folder: string): string[] =>
    readdirSync(folder, { withFileTypes: true }).flatMap((each) =>
        each.isDirectory()
            ? sources(join(folder, each.name))
            : each.name.endsWith('.cs')
              ? [join(folder, each.name)]
              : [],
    );

/** A source file of the chat, beside this one. */
const source = (file: string) => readFileSync(join(import.meta.dirname, file), 'utf8');
/** The English words of the page's keys (`t('key')`). */
const english = new Map(
    Array.from(
        readFileSync(join(import.meta.dirname, '..', 'locale', 'en.ts'), 'utf8').matchAll(
            /'([\w.]+)':\s*(['"])((?:\\.|(?!\2).)*)\2/g,
        ),
        (match) => [match[1]!, match[3]!.replace(/\\'/g, "'")] as const,
    ),
);

/** The index just past the braces that open at `from`, skipping quoted text and the expressions of a template. */
function closing(text: string, from: number): number {
    let depth = 0;
    for (let at = from; at < text.length; at++) {
        const char = text[at]!;
        if (char === "'" || char === '"' || char === '`') {
            for (at++; at < text.length && text[at] !== char; at++) {
                if (text[at] === '\\') at++;
                else if (char === '`' && text[at] === '$' && text[at + 1] === '{') at = closing(text, at + 1) - 1;
            }
        } else if (char === '{') depth++;
        else if (char === '}' && --depth === 0) return at + 1;
    }
    return text.length;
}

/** The index just past the tag that opens at `from`, skipping its expressions and its quoted values. */
function tagEnd(text: string, from: number): number {
    for (let at = from; at < text.length; at++) {
        const char = text[at]!;
        if (char === '{') at = closing(text, at) - 1;
        else if (char === '"' || char === "'") at = text.indexOf(char, at + 1);
        else if (char === '>') return at + 1;
    }
    return text.length;
}

/** The words markup draws: its own text, and what its expressions draw. */
function drawnWords(markup: string): string[] {
    const found: string[] = [];
    let text = '';
    for (let at = 0; at < markup.length; at++) {
        const char = markup[at]!;
        if (char === '<' && /[a-zA-Z/]/.test(markup[at + 1] ?? '')) {
            at = tagEnd(markup, at) - 1;
            text += '\n';
        } else if (char === '{' || (char === '$' && markup[at + 1] === '{')) {
            const start = char === '$' ? at + 1 : at;
            const end = closing(markup, start);
            found.push(...expressionWords(markup.slice(start + 1, end - 1)));
            at = end - 1;
            text += '\n';
        } else text += char;
    }
    return [
        ...found,
        ...text
            .split('\n')
            .map((each) => each.trim())
            .filter((each) => each !== ''),
    ];
}

/** What an expression draws: the words of a key, a string it yields, the markup of its templates; never an argument. */
function expressionWords(expression: string): string[] {
    const found: string[] = [];
    for (let at = 0; at < expression.length; at++) {
        const char = expression[at]!;
        if (expression.startsWith("t('", at) && !/[\w.]/.test(expression[at - 1] ?? '')) {
            const end = expression.indexOf("'", at + 3);
            found.push(english.get(expression.slice(at + 3, end)) ?? `no word for ${expression.slice(at + 3, end)}`);
            at = expression.indexOf(')', end);
        } else if (char === "'" || char === '"') {
            let end = at + 1;
            while (end < expression.length && expression[end] !== char) end += expression[end] === '\\' ? 2 : 1;
            const before = expression.slice(0, at).trimEnd().at(-1);
            if (before !== '(' && before !== ',' && before !== '=' && before !== '[')
                found.push(expression.slice(at + 1, end).replace(/\\'/g, "'"));
            at = end;
        } else if (char === '`') {
            let end = at + 1;
            while (end < expression.length && expression[end] !== '`') {
                if (expression[end] === '\\') end++;
                else if (expression[end] === '$' && expression[end + 1] === '{') end = closing(expression, end + 1) - 1;
                end++;
            }
            if (/\bhtml\s*$/.test(expression.slice(0, at))) found.push(...drawnWords(expression.slice(at + 1, end)));
            at = end;
        }
    }
    return found;
}

/** Every word drawn inside a button of a source file's markup. */
function buttonWords(text: string): string[] {
    const found: string[] = [];
    for (let at = text.indexOf('<button'); at !== -1; at = text.indexOf('<button', at)) {
        const open = tagEnd(text, at);
        // A name the button gives itself, written as it is: the composer's Send and Stop have only that.
        const named = /aria-label=(?:"([^"$]*)"|\{'([^']*)'\})/.exec(text.slice(at, open));
        if (named) found.push(named[1] ?? named[2]!);
        // A button closed in its own tag draws no words of its own.
        if (text[open - 2] === '/') {
            at = open;
            continue;
        }
        const end = text.indexOf('</button>', open);
        found.push(...drawnWords(text.slice(open, end)));
        at = end;
    }
    return found;
}

/** The moves' verbs, as the chat bar's buttons and their dialog draw them. */
const moveVerbs = (text: string): string[] =>
    Array.from(
        /export const verbs[^{]*\{([^}]*)\}/.exec(text)?.[1]?.matchAll(/:\s*'([^']*)'/g) ?? [],
        (match) => match[1]!,
    );

/** The access modes' names, as the menu beside the composer draws them. */
const modeNames = (text: string): string[] =>
    Array.from(
        /const modeWords[^{]*\{([^}]*)\}/.exec(text)?.[1]?.matchAll(/:\s*'([^']*)'/g) ?? [],
        (match) => match[1]!,
    );

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

    it('is no card after a tab, nor with a tab after its word', () => {
        for (const text of [`\t\`\`\`card\n${choice()}\n\`\`\``, `\`\`\`card\t\n${choice()}\n\`\`\``]) {
            const split = splitCards(text);
            expect(split.blocks, JSON.stringify(text)).toEqual([]);
            expect(split.words).toBe(text);
        }
    });

    it('takes out a block no fence closes, to the end of the text', () => {
        const split = splitCards(`Thinking.\n\`\`\`card\n{"kind":"cho`);
        expect(split.blocks).toEqual([{ body: '{"kind":"cho', closed: false }]);
        expect(split.words.trim()).toBe('Thinking.');
    });

    it('takes out a text’s last block alone, every earlier block staying in its words as written', () => {
        const needs = fence('{"kind":"needs"}');
        const last = lastCardOf(`First.\n${needs}\nBetween.\r\n${fence(choice())}\r\nAfter.`);
        expect(last).toEqual({
            words: `First.\n${needs}\nBetween.\n\nAfter.`,
            block: { body: choice(), closed: true },
        });
        expect(lastCardOf(`Open.\n\`\`\`card\n{"kind"`)).toEqual({
            words: 'Open.\n',
            block: { body: '{"kind"', closed: false },
        });
        // A fence that is no card is the words', and a text with no card has none to take out.
        expect(lastCardOf(`Code.\n${fence(choice(), 'json')}`)).toBeNull();
        expect(lastCardOf('Just words.')).toBeNull();
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

    it('takes out no block of a turn the desktop marked a plan, so nothing of a plan is hidden', () => {
        const plan = fence(
            '{"kind":"plan","title":"Spring","channels":["email"],"steps":[{"channel":"email","body":"Hi there"}]}',
        );
        const layout = cardLayout([
            { kind: 'user', text: 'Plan it' },
            { kind: 'text', text: `First.\n${fence(choice())}` },
            { kind: 'text', text: `Here is the plan.\n${plan}`, plan: true },
            { kind: 'user', text: 'And now?' },
            { kind: 'text', text: plan, plan: true },
        ]);
        expect(layout.cards.size).toBe(0);
        expect(layout.words.size).toBe(0);
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
        ['the card that opens Connect with a member', { kind: 'ai_setup', host: 'claude_code' }],
        ['the card that opens Accounts with a member', { kind: 'reply_setup', key: 'x' }],
        ['the card that opens Accounts naming a campaign', { kind: 'reply_setup', campaign: 'cmp_1' }],
    ])('says schema for %s', (_, value) => expect(failure(object(value))).toMatch(/ schema$/));

    it('refuses __proto__, which JSON.parse makes an own key', () => {
        expect(failure(read('{"kind":"accounts","__proto__":{"x":1}}'))).toBe(schema('accounts'));
        expect(failure(read('{"kind":"ai_setup","__proto__":{"x":1}}'))).toBe(schema('ai_setup'));
        expect(failure(read('{"kind":"reply_setup","__proto__":{"x":1}}'))).toBe(schema('reply_setup'));
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

    it('reads the two cards that only lead to a place, of their kind alone, wherever a card may stand', () => {
        for (const kind of ['ai_setup', 'reply_setup'])
            for (const where of [setup, agent, feed])
                expect(object({ kind }, where)).toEqual({ state: 'card', card: { kind } });
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

    it('takes each label once, folded as a person reads it: compatible forms, white space, apostrophes and case', () => {
        for (const [one, other] of [
            ['Yes', ' yes '],
            ['From me', 'From\u00a0me'],
            ['From  me', 'From me'],
            ['Ｆｒｏｍ ｍｅ', 'From me'],
            ['It’s mine', "It's mine"],
        ])
            expect(failure(read(choice({ options: [{ label: one }, { label: other }] }))), one).toBe(schema('choice'));
    });

    it('refuses a label that draws as nothing or as no character: a blank pattern, half a pair, a private character', () => {
        for (const label of ['\u2800', 'A\u2800', 'A\ud800', 'A\udc00B', 'A\ue000', 'A\u{F0000}'])
            expect(failure(read(choice({ options: [{ label }, { label: 'Other' }] }))), JSON.stringify(label)).toBe(
                schema('choice'),
            );
    });

    it('reads labels that are the names of an object’s own members as words, nothing more', () => {
        const reading = read(choice({ options: [{ label: '__proto__' }, { label: 'constructor' }] }));
        expect(reading.state).toBe('card');
        expect(reading.state === 'card' && reading.card.kind === 'choice' ? reading.card.options : []).toEqual([
            { label: '__proto__' },
            { label: 'constructor' },
        ]);
    });

    it('refuses a label that is a control’s word, folded as a person reads it, and takes one that only contains it', () => {
        for (const word of controlWords) {
            const shouted = ` ${word.toUpperCase()} `;
            expect(failure(read(choice({ options: [{ label: shouted }, { label: 'Something else' }] }))), word).toBe(
                schema('choice'),
            );
        }
        expect(read(choice({ options: [{ label: 'Yes, start it' }, { label: 'Not yet' }] })).state).toBe('card');
        for (const label of [
            'ＳＴＡＲＴ',
            'approve\u00a0and run',
            'Approve  and  run',
            'Don’t allow',
            'Don\u02bct allow',
            'Don\u00b4t allow',
            'Answer',
            'Connect',
            'Approve.',
        ])
            expect(failure(read(choice({ options: [{ label }, { label: 'Not yet' }] }))), label).toBe(schema('choice'));
    });

    it('takes the words of controls that only close, open, show or move, and a bare name', () => {
        for (const word of [...passiveWords, ...nameWords])
            expect(read(choice({ options: [{ label: word }, { label: 'Yes' }] })).state, word).toBe('card');
        expect(read(choice({ options: [{ label: 'Yes' }, { label: 'Not now' }] })).state).toBe('card');
    });

    it('names in the chat’s instructions every word it refuses, and no other', () => {
        const rule =
            readFileSync(join(import.meta.dirname, '..', '..', '..', 'desktop', 'chat-instructions.md'), 'utf8')
                .split('\n')
                .find((line) => /^\d+\. A card is not an act\./.test(line)) ?? '';
        const named = /A label is never the word of a button of the screen that acts: ([^.]+)\./.exec(rule)?.[1] ?? '';
        expect(named.split(', ').map(foldLabel)).toEqual(controlWords.map(foldLabel));
    });

    // What the reader reaches: the literal words, the string-yielding expressions, the `t()` keys and the literal
    // `aria-label` inside every `<button>` of the files it reads, which paint the chat: its thread and header, the
    // waiting cards and their message editor ("Edit message", which only opens it), the composer's menus, the Reply card,
    // and the chat bar's moves and their dialog; the access modes' names and the moves' verbs, read from their tables; and
    // the chips' words, which chips.ts publishes.
    // What it cannot reach is listed by hand below.
    const byHand = [
        // The Reply card's row buttons, whose words are arguments of one helper (connect.ts, `rowActs`).
        'Reconnect',
        'Limits',
        'Unblock',
        'Resume',
        'Pause',
        'Delete',
        // The Reply card's mailbox buttons, drawn from a table of providers.
        'Gmail',
        'Outlook',
        // The Reply card's offers after a refusal, drawn from a table.
        'Open the shared mailbox page…',
        'Delete anyway…',
        // The status card's page buttons, whose words are arguments of one helper (card-status.ts, `page`). A people
        // list's rows and footer draw a person's name and a count from the runtime's answer, which no list holds.
        'People',
        'Inbox',
        'Results',
        'Settings',
        // The words of a permission answered are a sentence beside the card, not a button; the effort dots' words are
        // only their names for a screen reader, never drawn; neither is held.
    ];

    it('classes every word the buttons of the files it reads draw as one that acts, one that does not, or a name', () => {
        const drawn = [
            ...buttonWords(source('chat.tsx')),
            ...buttonWords(source('card-plan.ts')),
            ...buttonWords(source('waiting-cards.tsx')),
            ...buttonWords(source('start-menus.tsx')),
            ...buttonWords(source('connect.ts')),
            ...buttonWords(source('../screens/campaigns/moves.tsx')),
            ...buttonWords(source('../screens/campaigns/pause-dialog.tsx')),
            ...buttonWords(source('../screens/attention/message-editor.tsx')),
            ...buttonWords(source('card-frame.ts')),
            ...buttonWords(source('card-status.ts')),
            ...buttonWords(source('card-people.ts')),
            ...buttonWords(source('card-needs.ts')),
            ...buttonWords(source('card-accounts.ts')),
            ...buttonWords(source('card-places.ts')),
            ...buttonWords(source('card-own-list.ts')),
            ...modeNames(source('chat-model.ts')),
            ...moveVerbs(source('../screens/campaigns/pause-dialog.tsx')),
            // The chips' words, drawn from the tables of chips.ts.
            ...chipWords,
            ...byHand,
        ];
        // Every file is read: a reader that finds nothing would hold nothing.
        expect(drawn).toEqual(
            expect.arrayContaining([
                'Approve and run',
                'Answer',
                'Connect',
                'Plan only',
                'Default effort',
                'Send',
                'Disconnect',
                'Edit message',
                'Start',
            ]),
        );
        const lists = [controlWords, passiveWords, nameWords].map((list) => list.map(foldLabel));
        const words = [...new Set(drawn.map((word) => foldLabel(word.replace(/:$/, ''))))];
        expect(words.filter((word) => lists.filter((list) => list.includes(word)).length !== 1)).toEqual([]);
        expect(lists.flat().filter((word, at, all) => all.indexOf(word) !== at)).toEqual([]);
        // And no list holds a word no button draws.
        expect(lists.flat().filter((word) => !words.includes(word))).toEqual([]);
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

describe('a list card’s members', () => {
    const columns = [
        { from: 'Name', field: 'first_name' },
        { from: 'E-mail', field: 'email' },
        { field: 'custom:region' },
    ];
    const list = (fields: Record<string, unknown> = {}) => ({
        kind: 'own_list',
        match_by: 'email',
        columns,
        left_out: ['Notes'],
        rows: [
            ['Ann', 'ann@example.com', 'North'],
            ['Bo', '', ''],
        ],
        ...fields,
    });
    const inAgent = (value: unknown) => object(value, agent);

    it('reads a list in an agent’s chat, its rows as given and its empty cells kept', () => {
        expect(inAgent(list())).toEqual({
            state: 'card',
            card: {
                kind: 'own_list',
                match_by: 'email',
                columns,
                left_out: ['Notes'],
                rows: [
                    ['Ann', 'ann@example.com', 'North'],
                    ['Bo', '', ''],
                ],
            },
        });
        // Without left_out it leaves nothing out; with its own campaign named it is that chat's.
        const bare = inAgent(list({ left_out: undefined, campaign: 'cmp_1' }));
        expect(bare.state === 'card' && bare.card.kind === 'own_list' && bare.card.left_out).toEqual([]);
        expect(
            inAgent(list({ match_by: 'linkedin', columns: [{ field: 'linkedin' }], rows: [['in/ann']] })).state,
        ).toBe('card');
    });

    it('stands in an agent’s chat alone, and says so for another agent’s id', () => {
        expect(failure(object(list(), setup))).toBe(schema('own_list'));
        expect(failure(object(list(), feed))).toBe(schema('own_list'));
        expect(failure(object(list({ campaign: 'cmp_2' }), feed))).toBe(schema('own_list'));
        expect(inAgent(list({ campaign: 'cmp_2' }))).toEqual({ state: 'elsewhere', kind: 'own_list' });
    });

    it.each([
        ['another key', { note: 'x' }],
        ['match_by missing', { match_by: undefined }],
        ['a match_by of phone', { match_by: 'phone' }],
        ['a match_by of a custom field', { match_by: 'custom:region' }],
        ['a match_by no column holds', { match_by: 'linkedin' }],
        ['a field outside the vocabulary', { columns: [...columns.slice(0, 2), { field: 'nickname' }] }],
        ['a field named twice', { columns: [columns[0], columns[1], { field: 'first_name' }] }],
        ['a custom name of other characters', { columns: [...columns.slice(0, 2), { field: 'custom:deal size' }] }],
        ['a custom name over 64', { columns: [...columns.slice(0, 2), { field: `custom:${'x'.repeat(65)}` }] }],
        ['a bare custom', { columns: [...columns.slice(0, 2), { field: 'custom:' }] }],
        ['a column with another member', { columns: [...columns.slice(0, 2), { field: 'title', width: 3 }] }],
        ['a header over 80', { columns: [{ from: 'x'.repeat(81), field: 'first_name' }, ...columns.slice(1)] }],
        ['no columns', { columns: [], rows: [[]] }],
        ['left_out over 20', { left_out: Array.from({ length: 21 }, (_, n) => `C${n}`) }],
        ['a left-out header over 80', { left_out: ['x'.repeat(81)] }],
        ['no rows', { rows: [] }],
        ['a row too short', { rows: [['Ann', 'ann@example.com']] }],
        ['a row too long', { rows: [['Ann', 'ann@example.com', 'North', 'x']] }],
        ['a row that is no list', { rows: ['Ann'] }],
        ['a null cell', { rows: [['Ann', null, '']] }],
        ['a number in a cell', { rows: [['Ann', 'ann@example.com', 5]] }],
        ['a cell over 500', { rows: [['x'.repeat(501), 'ann@example.com', '']] }],
        ['a cell that breaks a line', { rows: [['Ann\nLee', 'ann@example.com', '']] }],
        ['a cell with a tab', { rows: [['Ann\tLee', 'ann@example.com', '']] }],
        ['a cell with an unseen character', { rows: [['Ann​', 'ann@example.com', '']] }],
        ['a cell with a blank pattern', { rows: [['⠀', 'ann@example.com', '']] }],
    ])('refuses %s', (_, fields) => expect(failure(inAgent(list(fields)))).toBe(schema('own_list')));

    it('takes twelve columns and 200 rows, and no more', () => {
        const twelve = ['first_name', 'last_name', 'company', 'title', 'time_zone', 'email', 'phone', 'linkedin'].map(
            (field) => ({ field }),
        );
        const fields = [...twelve, ...['a', 'b', 'c', 'd'].map((name) => ({ field: `custom:${name}` }))];
        const row = (width: number) => Array.from({ length: width }, () => '');
        expect(inAgent(list({ columns: fields, rows: [row(12)] })).state).toBe('card');
        expect(failure(inAgent(list({ columns: [...fields, { field: 'custom:e' }], rows: [row(13)] })))).toBe(
            schema('own_list'),
        );
        const rows = (count: number) => Array.from({ length: count }, (_, n) => [`P${n}`, `p${n}@example.com`, '']);
        expect(inAgent(list({ rows: rows(200) })).state).toBe('card');
        expect(failure(inAgent(list({ rows: rows(201) })))).toBe(schema('own_list'));
        expect(inAgent(list({ rows: [['x'.repeat(500), 'ann@example.com', '']] })).state).toBe('card');
    });

    it('reads a list of exactly 16,000 characters, and no list of 16,001', () => {
        // A cell holds at most 500, so the edge is reached with full rows and one last row filled to it.
        const many = (count: number) => {
            const row = (cell: string) => [cell, 'ann@example.com', ''];
            const text = (rows: string[][]) => JSON.stringify(list({ rows }));
            const full: string[][] = [];
            while (Array.from(text([...full, row('')])).length + 500 < count) full.push(row('ж'.repeat(500)));
            return text([...full, row('ж'.repeat(count - Array.from(text([...full, row('')])).length))]);
        };
        const edge = many(blockLimit);
        expect(Array.from(edge)).toHaveLength(16_000);
        expect(read(edge, agent).state).toBe('card');
        const over = many(blockLimit + 1);
        expect(Array.from(over)).toHaveLength(16_001);
        expect(failure(read(over, agent))).toBe('unknown too_long');
    });

    it('refuses a cell that turns the text around it, and __proto__ on the card or on a column', () => {
        expect(failure(inAgent(list({ rows: [['Ann‮Lee', 'ann@example.com', '']] })))).toBe(schema('own_list'));
        expect(failure(inAgent(list({ rows: [['‭', 'ann@example.com', '']] })))).toBe(schema('own_list'));
        const text = JSON.stringify(list());
        expect(
            failure(read(text.replace('{"kind":"own_list",', '{"kind":"own_list","__proto__":{"x":1},'), agent)),
        ).toBe(schema('own_list'));
        expect(
            failure(
                read(
                    text.replace(
                        '{"from":"Name","field":"first_name"}',
                        '{"from":"Name","field":"first_name","__proto__":{}}',
                    ),
                    agent,
                ),
            ),
        ).toBe(schema('own_list'));
        // The same text without them reads, so it is __proto__ that is refused.
        expect(read(text, agent).state).toBe('card');
    });

    it('nests three levels deep, and refuses a cell that is a list', () => {
        expect(depthOf(list())).toBe(3);
        expect(failure(inAgent(list({ rows: [[['Ann'], 'ann@example.com', '']] })))).toBe(schema('own_list'));
    });

    it('refuses every key the runtime itself writes into a contact’s custom fields, as its own source writes them', () => {
        const runtime = join(import.meta.dirname, '..', '..', '..', 'dotnet', 'src', 'Jason.Runtime');
        const landing = readFileSync(join(runtime, 'Audiences', 'SearchLanding.cs'), 'utf8');
        const contact = readFileSync(join(runtime, 'Persistence', 'Contact.cs'), 'utf8');
        const custom = /private static JsonObject Custom\([\s\S]*?\n {8}\}\n/.exec(landing)?.[0] ?? '';
        const added = Array.from(custom.matchAll(/\bAdd\("([^"]+)"/g), (match) => match[1]!);
        const record = /const string ProviderRecordKey = "([^"]+)"/.exec(contact)?.[1];
        expect(custom).toContain('custom[Contact.ProviderRecordKey] =');
        expect([...added, record].sort()).toEqual([...reservedCustomKeys].sort());
        // No other file of the runtime writes a key into a contact's custom fields.
        const writers = sources(runtime).filter((file) =>
            /\b[cC]ustom\s*\[[^\]]+\]\s*=(?!=)|\bCustom\s*=\s*new JsonObject\s*\{/.test(readFileSync(file, 'utf8')),
        );
        expect(writers.map((file) => file.slice(runtime.length + 1).replace(/\\/g, '/'))).toEqual([
            'Audiences/SearchLanding.cs',
        ]);
        for (const key of reservedCustomKeys)
            expect(failure(inAgent(list({ columns: [...columns.slice(0, 2), { field: `custom:${key}` }] })))).toBe(
                schema('own_list'),
            );
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

    it('teaches only the kinds this window draws, a choice, four cards of data, two that lead to a place, a plan and a list, and each example reads as one', () => {
        const shown = examples(instructions);
        expect(shown.map((each) => (JSON.parse(each) as { kind: string }).kind)).toEqual([
            'choice',
            'people_list',
            'agent_status',
            'needs',
            'accounts',
            'ai_setup',
            'reply_setup',
            'plan',
            'own_list',
        ]);
        expect(shown.map((each) => (JSON.parse(each) as { kind: string }).kind)).toEqual([...cardKinds]);
        // A list stands in an agent's own chat alone, so its example is read there.
        for (const example of shown)
            expect(read(example, example.includes('"own_list"') ? agent : setup).state, example).toBe('card');
    });

    it('names, for a list, the most rows a card carries, below the schema’s own ceiling, and every key Jason keeps', () => {
        const rule = instructions.split('\n').find((line) => line.includes('"own_list"')) ?? '';
        expect(rule).toContain('at most 100 rows');
        expect(rule).toContain('Add to this agent');
        for (const key of reservedCustomKeys) expect(rule).toContain(`custom:${key}`);
    });

    it('tells the AI never to ask in the chat for a key, a password or a sign-in', () => {
        const rule = instructions.split('\n').find((line) => line.startsWith('15. ')) ?? '';
        expect(rule).toContain('never ask in this chat for a key, a password or a sign-in');
        expect(rule).toContain(
            'If the person pastes a key or a password into the chat, do not use it and do not repeat it',
        );
    });
});

describe('the desktop’s log of a card not drawn', () => {
    it('takes every kind this window draws, and unknown, and no other', () => {
        const chat = readFileSync(join(import.meta.dirname, '..', '..', '..', 'desktop', 'src', 'chat.ts'), 'utf8');
        const listed = /export const refusedCardKinds[^=]*= new Set\(\[([^\]]*)\]\)/.exec(chat)?.[1] ?? '';
        expect(Array.from(listed.matchAll(/'([^']*)'/g), (match) => match[1])).toEqual([...cardKinds, 'unknown']);
    });
});
