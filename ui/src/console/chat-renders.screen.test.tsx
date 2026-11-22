import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { shown } from '../test/area';
import { stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import * as chatModel from './chat-model';
import { Console } from './console';
import { attention } from './needs';
import { resetRecent } from './recent';
import { chats, conversations } from './state';
import { sideRow } from '../test/side-rows';

// A chat drawn out of sight is not drawn again for a word typed in the chat shown. A chat's panel asks for the model
// and effort its next turn runs on (`onHost`) each time it is drawn, so the calls naming a chat count its draws.
vi.mock('./chat-model', async (importOriginal) => {
    const real = await importOriginal<typeof chatModel>();
    return { ...real, onHost: vi.fn(real.onHost) };
});

const draws = (id: string) => vi.mocked(chatModel.onHost).mock.calls.filter(([chat]) => chat.id === id).length;

async function renderConsole(): Promise<void> {
    const desktop = stubDesktop();
    desktop.keep({ id: 'conv_a', campaignId: 'cmp_1', title: 'Q4 push', updatedAt: new Date().toISOString() });
    desktop.keep({ id: 'conv_b', title: 'North list', updatedAt: new Date(Date.now() - 60_000).toISOString() });
    const fake = nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
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
    fake.answer('system.info', 'system.info/with-ui');
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
}

const recentRow = (name: string) => sideRow(name);
const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });

beforeEach(() => {
    resetRecent();
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    vi.mocked(chatModel.onHost).mockClear();
});

describe('a chat out of sight', () => {
    it('is not drawn again while a person types in the chat shown', async () => {
        await renderConsole();
        fireEvent.click(await waitFor(() => recentRow('Spring')));
        await waitFor(() => expect(shown()).toBe('chat:conv_a'));
        fireEvent.change(box(), { target: { value: 'Half a thought' } });
        fireEvent.click(recentRow('North list'));
        await waitFor(() => expect(shown()).toBe('chat:conv_b'));
        // Both are drawn, one of them out of sight.
        expect(document.querySelector('[data-pane="chat:conv_a"]')?.hasAttribute('hidden')).toBe(true);
        await waitFor(() => expect(draws('conv_b')).toBeGreaterThan(0));
        const hidden = draws('conv_a');
        const before = draws('conv_b');
        expect(hidden).toBeGreaterThan(0);
        for (const text of ['N', 'No', 'Nor', 'Nort', 'North']) fireEvent.change(box(), { target: { value: text } });
        expect(box().value).toBe('North');
        // The chat shown was drawn for every word, the one out of sight not once.
        expect(draws('conv_b')).toBeGreaterThanOrEqual(before + 5);
        expect(draws('conv_a')).toBe(hidden);
        expect(chats.value['conv_a']?.draft).toBe('Half a thought');
    });
});
