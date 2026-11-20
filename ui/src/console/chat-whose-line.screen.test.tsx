import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { mode, type Mode } from '../platform/mode';
import { firstCampaignTab, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { nothingWaitsInChats } from '../test/waiting';
import { bringForward, shown, shownPane } from '../test/area';
import { styleOf } from '../test/stylesheet';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations, feedId } from './state';

// Every chat says whose it is on one standing line at the top of its panel: "Chat · <agent's name>" for an agent's chat,
// "Chat · no agent" for a chat about no agent. The line is there before the first message and stays after it. The Feed
// is not a chat of an agent or of none, and carries no such line.

function renderConsole(where: Mode = 'live', desktop: StubDesktop = chatDesktop()): StubDesktop {
    mode.value = where;
    const fake = nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
        .answer('campaign.get', 'campaign.get/details')
        .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
        .answer('journal.list', 'journal.list/by-campaign')
        .answer('campaign.list_contacts', 'campaign.list_contacts/everyone');
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
    fake.answer('system.info', 'system.info/with-ui');
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);

/** The standing line of the chat in sight, as a person reads it; null where it has none. */
const line = (): HTMLElement | null => shownPane()?.querySelector<HTMLElement>('.chat-bar .chat-whose') ?? null;
const lineText = () => line()?.textContent?.replace(/\s+/g, ' ').trim() ?? null;

async function send(desktop: StubDesktop, text: string): Promise<string> {
    const before = calls(desktop, 'chat.send').length;
    const pane = within(shownPane()!);
    fireEvent.change(await waitFor(() => pane.getByRole('textbox', { name: 'Your message' })), {
        target: { value: text },
    });
    fireEvent.click(pane.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(before + 1));
    return calls(desktop, 'chat.send')[before]!.args[0] as string;
}

/** Opens Spring from the Agents panel: its header, on its Chat tab. */
async function openSpring(): Promise<void> {
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    bringForward('Agents');
    const panel = screen.getByRole('heading', { name: 'Agents', level: 1 }).closest<HTMLElement>('.painted')!;
    const row = (await waitFor(() => within(panel).getByText('Spring'))).closest('tr')!;
    fireEvent.click(within(row).getByRole('button', { name: 'Open' }));
    await waitFor(() => expect(document.querySelector('.agent-head')).not.toBeNull());
}

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
    firstCampaignTab.value = 'chat';
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});
afterEach(() => {
    vi.useRealTimers();
    firstCampaignTab.value = 'sequence';
});

describe('the line that says whose chat it is', () => {
    it('says "Chat · no agent" on a chat New chat made, before its first message and after it', async () => {
        const desktop = renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(shown()).toMatch(/^chat:/));
        await waitFor(() => expect(lineText()).toBe('Chat · no agent'));
        const id = await send(desktop, 'Who replied this week?');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() => expect(chats.value[id]?.running).toBe(false));
        expect(lineText()).toBe('Chat · no agent');
    });

    it('names the agent on an agent’s chat, before its first message and after it', async () => {
        const desktop = renderConsole();
        await openSpring();
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        expect(lineText()).toBe('Chat · Spring');
        const id = await send(desktop, 'Who replied this week?');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() => expect(chats.value[id]?.running).toBe(false));
        expect(screen.queryByRole('heading', { name: 'What should Jason do in Spring?' })).toBeNull();
        expect(lineText()).toBe('Chat · Spring');
    });

    it('names the agent as the runtime lists it, also where the person gave its conversation a name of their own', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000S1',
            campaignId: 'cmp_1',
            title: 'Notes on the spring push',
            entries: [{ kind: 'user', text: 'The kept words about Spring' }],
        });
        renderConsole('live', desktop);
        await openSpring();
        expect(await screen.findByText('The kept words about Spring')).toBeTruthy();
        expect(lineText()).toBe('Chat · Spring');
    });

    it('stands first in the chat’s bar, before the bar’s buttons, in the quiet small type of a label', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(line()).not.toBeNull());
        const bar = line()!.parentElement!;
        expect(bar.firstElementChild).toBe(line());
        expect(styleOf(line()!)).toMatchObject({
            color: 'var(--mute)',
            'font-size': '13px',
            'margin-right': 'auto',
        });
    });

    it('is not drawn on the Feed', async () => {
        renderConsole('demo');
        await waitFor(() => expect(feedId.value).not.toBeNull());
        fireEvent.click(await waitFor(() => side().getByRole('button', { name: /^Feed/ })));
        await waitFor(() => expect(shown()).toBe(`chat:${feedId.value}`));
        await waitFor(() => screen.getByRole('heading', { name: 'The Feed' }));
        expect(line()).toBeNull();
    });
});
