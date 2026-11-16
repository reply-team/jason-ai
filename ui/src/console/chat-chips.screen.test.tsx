import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import type { DesktopChatMode, DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { loadMode, mode } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The chips under the last answer, as a person meets them: in an agent's chat chosen from the agent's counts as the
// runtime recorded them (`campaign.summary/card-chips`: Spring launch with two people waiting on the person and work
// under way, Summer reach with nobody, Winter plan with two people nobody worked on yet, Autumn push with one person
// worked on and nothing waiting), in the Feed and a set-up chat the opening three; a click sends the chip's words as the
// person's next message, none stand while a turn runs or a choice is open, and the question just asked is not offered
// again.

const at = '2026-10-16T09:05:00.000Z';
const answered: DesktopKeptEntry[] = [
    { kind: 'user', text: 'Hi' },
    { kind: 'text', text: 'Hello.', at },
];
const agent = (id: string, campaignId: string, title: string, entries: DesktopKeptEntry[] = answered) => ({
    id,
    campaignId,
    title,
    entries,
});
const spring = (entries?: DesktopKeptEntry[]) => agent('conv_01JDCHIPS0000000000000000A', 'cmp_1', 'Spring', entries);
const summer = () => agent('conv_01JDCHIPS0000000000000000B', 'cmp_3', 'Summer');
const winter = () => agent('conv_01JDCHIPS0000000000000000C', 'cmp_5', 'Winter');
const autumn = () => agent('conv_01JDCHIPS0000000000000000D', 'cmp_6', 'Autumn');
const setUp = (entries: DesktopKeptEntry[] = answered) => ({
    id: 'conv_01JDCHIPS0000000000000000S',
    campaignId: null,
    title: 'Sending',
    entries,
});

function renderConsole(
    records: (Partial<DesktopConversationRecord> & { id: string })[],
    desktop: StubDesktop = stubDesktop(),
): { fake: FakeApi; desktop: StubDesktop } {
    const fake = given(new FakeApi());
    for (const record of records) desktop.keep(record);
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return { fake, desktop };
}

/** What the console reads at its start, the agents and their counts from the chips' own recordings. */
function given(fake: FakeApi): FakeApi {
    nothingWaitsInChats(fake)
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/card-chips')
        .answer('campaign.summary', 'campaign.summary/card-chips')
        .answer('system.info', 'system.info/with-ui');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    return fake;
}

async function openChat(title: string): Promise<HTMLElement> {
    await waitFor(() => {
        const list = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', {
            name: 'Conversations',
        });
        fireEvent.click(within(list).getByRole('button', { name: title }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

async function openFeed(): Promise<HTMLElement> {
    await waitFor(() => {
        const menu = within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Menu' });
        fireEvent.click(within(menu).getByRole('button', { name: /^Feed/ }));
        expect(document.querySelector('.area > .pane:not([hidden]) .chat-panel')).not.toBeNull();
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

/** The words of the chips under the last answer of a pane, in their order; none when no row of them stands. */
const chips = (pane: HTMLElement) =>
    Array.from(pane.querySelectorAll<HTMLButtonElement>('.thread .sugs.next .sug'), (chip) => chip.textContent);
const chip = (pane: HTMLElement, words: string) =>
    Array.from(pane.querySelectorAll<HTMLButtonElement>('.thread .sugs.next .sug')).find(
        (each) => each.textContent === words,
    )!;
const sends = (desktop: StubDesktop) =>
    desktop.calls.filter((each) => each.name === 'chat.send').map((each) => each.args[1]);
const fenced = (value: unknown) => `\`\`\`card\n${JSON.stringify(value)}\n\`\`\``;
const choice = {
    kind: 'choice',
    question: 'Which first?',
    options: [{ label: 'Founders' }, { label: 'Heads of sales' }],
};

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    acts.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.summary.value = {};
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.loaded.value = false;
    attention.detail.value = null;
    forgetWaiting();
    feedId.value = null;
    feedUnseen.value = false;
});

afterEach(() => {
    acts.value = {};
    mode.value = 'live';
});

describe('the chips under an agent’s last answer', () => {
    it('are chosen from its counts, a reopened chat’s as a chat answered here, what waits for the person first', async () => {
        renderConsole([spring(), summer(), winter(), autumn()]);
        const want: [string, string[]][] = [
            ['Spring', ['What is waiting for me?', 'How is it going?', 'Show me the people']],
            ['Summer', ['Who should this agent reach?', 'Show me the plan', 'What can you do here?']],
            ['Winter', ['Show me the people', 'What happens when it starts?']],
            ['Autumn', ['How is it going?', 'Show me the people']],
        ];
        for (const [title, words] of want) {
            const pane = await openChat(title);
            await waitFor(() => expect(chips(pane), title).toEqual(words));
            // They stand after the answer, as the thread's last row, and hold no number.
            expect(pane.querySelector('.thread')!.lastElementChild!.matches('.sugs.next')).toBe(true);
            for (const word of chips(pane)) expect(word).not.toMatch(/\d/);
        }
        // Back to the first chat: the same chips, read from the same counts, with nothing read again for them.
        const again = await openChat('Spring');
        expect(chips(again)).toEqual(want[0]![1]);
    });

    it('send their words as the person’s next message, the runtime’s word for an agent, and stand again after the answer, not asking it twice', async () => {
        const { desktop } = renderConsole([summer()]);
        const pane = await openChat('Summer');
        await waitFor(() => expect(chips(pane)).toHaveLength(3));
        fireEvent.click(chip(pane, 'Who should this agent reach?'));
        await waitFor(() => expect(sends(desktop)).toEqual(['Who should this campaign reach?']));
        // While the turn runs, none stand.
        await waitFor(() => expect(pane.querySelector('.typing')).not.toBeNull());
        expect(chips(pane)).toEqual([]);
        flush(() => {
            desktop.emit(summer().id, { kind: 'text', text: 'Founders of small agencies.', at });
            desktop.emit(summer().id, { kind: 'done', error: null });
        });
        await waitFor(() => expect(chips(pane)).toEqual(['Show me the plan', 'What can you do here?']));
    });

    it('are buttons a person reaches with the keyboard, each named by its words', async () => {
        renderConsole([autumn()]);
        const pane = await openChat('Autumn');
        await waitFor(() => expect(chips(pane)).toHaveLength(2));
        const row = pane.querySelector<HTMLElement>('.thread .sugs.next')!;
        const buttons = within(row).getAllByRole('button');
        expect(buttons.map((each) => each.getAttribute('type'))).toEqual(['button', 'button']);
        expect(buttons.some((each) => each.hasAttribute('tabindex') || each.hasAttribute('disabled'))).toBe(false);
        expect(within(row).getByRole('button', { name: 'How is it going?' })).toBe(buttons[0]);
    });

    it('wait while a choice the AI asked stands open, and come back once the person answers it and the AI replies', async () => {
        const asking: DesktopKeptEntry[] = [...answered];
        const { desktop } = renderConsole([setUp(asking)]);
        const pane = await openChat('Sending');
        await waitFor(() => expect(chips(pane)).toHaveLength(3));
        flush(() => {
            desktop.emit(setUp().id, { kind: 'text', text: `Two kinds.\n\n${fenced(choice)}`, at });
            desktop.emit(setUp().id, { kind: 'done', error: null });
        });
        const asked = await waitFor(() => within(pane).getByRole('group', { name: 'Jason asks' }));
        expect(chips(pane)).toEqual([]);
        fireEvent.click(within(asked).getByRole('button', { name: /^Founders/ }));
        await waitFor(() => expect(sends(desktop)).toEqual(['Founders']));
        flush(() => {
            desktop.emit(setUp().id, { kind: 'text', text: 'Founders it is.', at });
            desktop.emit(setUp().id, { kind: 'done', error: null });
        });
        await waitFor(() => expect(chips(pane)).toHaveLength(3));
    });

    it('do not stand after an error', async () => {
        renderConsole([
            spring([...answered, { kind: 'user', text: 'Go on' }, { kind: 'error', text: 'The AI host stopped.' }]),
            autumn(),
        ]);
        const pane = await openChat('Spring');
        await waitFor(() => within(pane).getByText('The AI host stopped.'));
        expect(chips(pane)).toEqual([]);
        expect(pane.querySelector('.sugs')).toBeNull();
    });
});

describe('the chips under an answer in a chat of no agent', () => {
    it('are the opening three in a set-up chat and in the Feed, less the question just asked', async () => {
        // The Feed's row is in the demo's sidebar.
        mode.value = 'demo';
        const feedChat = 'conv_01JDCHIPS0000000000000000F';
        const { desktop } = renderConsole([
            setUp([
                { kind: 'user', text: 'What is waiting for me?' },
                { kind: 'text', text: 'Nothing.', at },
            ]),
            { id: feedChat, campaignId: null, title: 'Feed', main: true, entries: answered },
        ]);
        const setup = await openChat('Sending');
        await waitFor(() => expect(chips(setup)).toEqual(['How are my agents doing?', 'Help me start a new agent.']));
        fireEvent.click(chip(setup, 'How are my agents doing?'));
        await waitFor(() => expect(sends(desktop)).toEqual(['How are my campaigns doing?']));
        const feed = await openFeed();
        await waitFor(() =>
            expect(chips(feed)).toEqual([
                'What is waiting for me?',
                'How are my agents doing?',
                'Help me start a new agent.',
            ]),
        );
    });
});

describe('the chips in a browser tab', () => {
    async function inTab(chatMode: DesktopChatMode): Promise<{ pane: HTMLElement; sent: () => number }> {
        const desktop = chatDesktop();
        const stand = standInBridge(desktop);
        const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
        setPlace('tab', tab);
        await loadMode(tab);
        desktop.keep({ ...setUp(), mode: chatMode });
        const fake = given(
            new FakeApi()
                .answer('plugin.list', 'plugin.list/none')
                .answer('route.list', 'route.list/empty')
                .answer('ui.tokens', 'ui.tokens/two'),
        );
        await session.bootstrap();
        render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
        const pane = await openChat('Sending');
        await waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
        return { pane, sent: () => stand.asked.filter((each) => each.member === 'chat.send').length };
    }

    it('stand where the tab may send', async () => {
        try {
            const { pane } = await inTab('ask_first');
            await waitFor(() => expect(chips(pane)).toHaveLength(3));
        } finally {
            cleanup();
            setPlace('window');
        }
    });

    it('stand nowhere the tab may not send', async () => {
        try {
            const { pane, sent } = await inTab('full_access');
            await waitFor(() => within(pane).getByText('Hello.'));
            expect(chips(pane)).toEqual([]);
            expect(sent()).toBe(0);
        } finally {
            cleanup();
            setPlace('window');
        }
    });
});
