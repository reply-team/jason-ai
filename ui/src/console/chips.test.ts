import { describe, expect, it } from 'vitest';
import type { CampaignSummaryRow } from '../api/types';
import type { Chat, Entry } from './chat-model';
import { chipWords, chipsUnder, openingChips } from './chips';

// The chips under a chat's last answer: chosen from the campaign's counts as the runtime keeps them, words a person
// would ask and never a claim, none while a turn runs or a choice stands open, never the question just asked again.

const row = (counts: Partial<CampaignSummaryRow> = {}): CampaignSummaryRow => ({
    campaign_id: 'cmp_1',
    people: 0,
    waiting: 0,
    failed: 0,
    working: 0,
    new: 0,
    idle: 0,
    sequence: 0,
    out: 0,
    removed: 0,
    sequence_linked: false,
    ...counts,
});

type Asked = Pick<Chat, 'entries' | 'campaignId' | 'main' | 'running' | 'reopened'>;
const answered = (entries: Entry[] = [], more: Partial<Asked> = {}): Asked => ({
    entries: [{ kind: 'user', text: 'Hi' }, { kind: 'text', text: 'Hello.' }, ...entries],
    campaignId: 'cmp_1',
    main: false,
    running: false,
    reopened: 0,
    ...more,
});
const shown = (chips: readonly { shown: string }[]) => chips.map((chip) => chip.shown);
const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const choice = {
    kind: 'choice',
    question: 'Which first?',
    options: [{ label: 'Founders' }, { label: 'Heads of sales' }],
};

describe('the chips under an agent’s answer', () => {
    it('asks who the agent should reach while nobody is in it', () => {
        expect(shown(chipsUnder(answered(), row()))).toEqual([
            'Who should this agent reach?',
            'Show me the plan',
            'What can you do here?',
        ]);
    });

    it('puts what waits for the person first', () => {
        expect(
            shown(chipsUnder(answered(), row({ people: 12, waiting: 2, working: 2, new: 8, idle: 1, out: 1 }))),
        ).toEqual(['What is waiting for me?', 'How is it going?', 'Show me the people']);
    });

    it('asks about the people and the start while nobody in it was worked on', () => {
        expect(shown(chipsUnder(answered(), row({ people: 2, new: 2 })))).toEqual([
            'Show me the people',
            'What happens when it starts?',
        ]);
    });

    it('asks how it goes once anyone in it was worked on, by any count past new', () => {
        for (const worked of ['working', 'sequence', 'out', 'idle', 'failed'] as const)
            expect(shown(chipsUnder(answered(), row({ people: 3, new: 2, [worked]: 1 }))), worked).toEqual([
                'How is it going?',
                'Show me the people',
            ]);
    });

    it('offers nothing for an agent whose counts were not read', () => {
        expect(chipsUnder(answered(), undefined)).toEqual([]);
    });

    it('shows the page’s word, agent, and sends the runtime’s, campaign', () => {
        const [reach] = chipsUnder(answered(), row());
        expect(reach).toEqual({ shown: 'Who should this agent reach?', sent: 'Who should this campaign reach?' });
    });
});

describe('the chips of a chat of no agent', () => {
    it('are the opening three in the Feed and in a set-up chat, whatever the counts', () => {
        for (const place of [
            { main: true, campaignId: null },
            { main: false, campaignId: null },
        ])
            expect(chipsUnder(answered([], place), row({ people: 5, waiting: 5 }))).toEqual(openingChips);
    });

    it('stand after the Feed’s lines that came after the answer', () => {
        const line = { kind: 'line', line: {} } as unknown as Entry;
        expect(chipsUnder(answered([line, { kind: 'gap' }], { main: true, campaignId: null }), undefined)).toEqual(
            openingChips,
        );
    });
});

describe('when there are no chips', () => {
    it('none while a turn runs', () => {
        expect(chipsUnder(answered([], { running: true }), row())).toEqual([]);
    });

    it('none on an empty chat, whose chips its opening draws', () => {
        expect(chipsUnder({ ...answered(), entries: [] }, row())).toEqual([]);
    });

    it('none while a choice stands open, and back once something follows it', () => {
        const asking: Entry = { kind: 'text', text: `Which first?\n\n${fenced(choice)}` };
        expect(chipsUnder(answered([{ kind: 'user', text: 'Go' }, asking]), row())).toEqual([]);
        expect(
            shown(
                chipsUnder(
                    answered([asking, { kind: 'user', text: 'Founders' }, { kind: 'text', text: 'Noted.' }]),
                    row(),
                ),
            ),
        ).toHaveLength(3);
    });

    it('none under a plan the desktop marked, which has its own buttons', () => {
        expect(chipsUnder(answered([{ kind: 'text', text: 'The plan.', plan: true }]), row())).toEqual([]);
    });

    it('none after anything but the AI’s answer: an error, a permission asked, a pick of campaigns, steps', () => {
        const after: Entry[] = [
            { kind: 'error', text: 'It failed.' },
            { kind: 'permission', request: 'r', title: 't', detail: 'd', remember: null, answer: null },
            { kind: 'pick', campaignIds: ['cmp_1'] },
            { kind: 'step', step: { text: 'Read' } as never },
        ];
        for (const entry of after) expect(chipsUnder(answered([entry]), row()), entry.kind).toEqual([]);
    });

    it('never the person’s last message again, as a person reads it, shown or sent', () => {
        const asked = (text: string) =>
            answered(
                [
                    { kind: 'user', text },
                    { kind: 'text', text: 'Here.' },
                ],
                { main: true, campaignId: null },
            );
        expect(shown(chipsUnder(asked('  what is WAITING for me  '), undefined))).toEqual([
            'How are my agents doing?',
            'Help me start a new agent.',
        ]);
        expect(shown(chipsUnder(asked('How are my campaigns doing?'), undefined))).not.toContain(
            'How are my agents doing?',
        );
        expect(shown(chipsUnder(asked('How are my agents doing'), undefined))).not.toContain(
            'How are my agents doing?',
        );
        const agent = answered([
            { kind: 'user', text: 'Who should this campaign reach?' },
            { kind: 'text', text: 'Founders.' },
        ]);
        expect(shown(chipsUnder(agent, row()))).toEqual(['Show me the plan', 'What can you do here?']);
    });
});

describe('the chips’ words', () => {
    it('are at most three, hold no number, and are questions or asks of a person, each shown once', () => {
        const counts = [
            row(),
            row({ people: 1, waiting: 1, working: 1 }),
            row({ people: 2, new: 2 }),
            row({ people: 2, working: 2 }),
        ];
        for (const each of counts) expect(chipsUnder(answered(), each).length).toBeLessThanOrEqual(3);
        for (const word of chipWords) expect(word, word).not.toMatch(/\p{N}/u);
        expect(new Set(chipWords).size).toBe(chipWords.length);
        expect(chipWords).toEqual(
            expect.arrayContaining([...openingChips.map((chip) => chip.shown), 'Who should this agent reach?']),
        );
    });
});
