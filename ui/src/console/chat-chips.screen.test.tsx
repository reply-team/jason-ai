import { act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { DesktopConversationRecord, DesktopKeptEntry } from '../platform/desktop';
import { mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { acts } from './card-acts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId, feedUnseen } from './state';
import { forgetWaiting } from './waiting';

// The chips as a person meets them: an empty chat draws its opening three; no chat draws any under an answer, whatever
// the agent's counts, and neither do the Feed and a set-up chat; a click sends the chip's words as the person's first
// message.

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
    watch(agentHeads(fake));
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

describe('under an agent’s last answer', () => {
    it('no chip stands, whatever the agent’s counts, before or after a turn', async () => {
        const { desktop } = renderConsole([spring(), summer(), winter(), autumn()]);
        for (const title of ['Spring', 'Summer', 'Winter', 'Autumn']) {
            const pane = await openChat(title);
            await waitFor(() => within(pane).getByText('Hello.'));
            expect(pane.querySelector('.sugs'), title).toBeNull();
            expect(pane.querySelector('.thread .sug'), title).toBeNull();
        }
        const pane = await openChat('Summer');
        flush(() => {
            desktop.emit(summer().id, { kind: 'text', text: 'Founders of small agencies.', at });
            desktop.emit(summer().id, { kind: 'done', error: null });
        });
        await waitFor(() => within(pane).getByText('Founders of small agencies.'));
        expect(pane.querySelector('.sugs')).toBeNull();
    });
});

describe('the opening chips', () => {
    const opening = ['What is waiting for me?', 'How are my agents doing?', 'Help me start a new agent.'];
    const drawn = (pane: HTMLElement) =>
        Array.from(pane.querySelectorAll<HTMLButtonElement>('.sugs .sug'), (each) => each.textContent);

    it('are drawn in an empty agent’s chat, still', async () => {
        renderConsole([agent('conv_01JDCHIPS0000000000000000H', 'cmp_1', 'Spring', [])]);
        const pane = await openChat('Spring');
        await waitFor(() => expect(drawn(pane)).toEqual(opening));
    });

    it('are drawn in an empty chat only: an answered Feed and an answered set-up chat draw none', async () => {
        // The Feed's row is in the demo's sidebar.
        mode.value = 'demo';
        renderConsole([
            setUp(),
            { id: 'conv_01JDCHIPS0000000000000000G', campaignId: null, title: 'Fresh', entries: [] },
            { id: 'conv_01JDCHIPS0000000000000000F', campaignId: null, title: 'Feed', main: true, entries: answered },
        ]);
        const fresh = await openChat('Fresh');
        await waitFor(() => expect(drawn(fresh)).toEqual(opening));
        const setup = await openChat('Sending');
        await waitFor(() => within(setup).getByText('Hello.'));
        expect(drawn(setup)).toEqual([]);
        const feed = await openFeed();
        await waitFor(() => within(feed).getByText('Hello.'));
        expect(drawn(feed)).toEqual([]);
    });
});
