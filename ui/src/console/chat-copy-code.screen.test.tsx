import { act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { shown } from '../test/area';
import { chatDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';

// A code block in an answer carries a Copy code button that puts the block's text on the clipboard exactly. Inline code
// carries none.

const chat = 'conv_01JDCOPYCODE00000000000000A';
const block = 'cd "C:\x5cUsers\x5cme\x5cMy Folder"\njason status --human\n\n  echo "a < b && c > d" # it\'s 100%\n';
const answer = `Go to the folder and run it.\n\n\`\`\`powershell\n${block}\`\`\`\n\nThen use \`jason status\` inline.\n\n\`\`\`\nsecond\n\`\`\``;

let written: string[] = [];

function renderChat(): void {
    const fake = nothingWaitsInChats(new FakeApi())
        .answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending');
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
    const desktop = chatDesktop();
    desktop.keep({
        id: chat,
        campaignId: 'cmp_1',
        title: 'Spring',
        entries: [
            { kind: 'user', text: 'How do I run it?' },
            { kind: 'text', text: answer },
        ],
    });
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
}

async function openChat(): Promise<HTMLElement> {
    const side = () =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });
    await waitFor(() => {
        fireEvent.click(within(side()).getByRole('button', { name: 'Spring' }));
        expect(shown()).toBe(`chat:${chat}`);
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    written = [];
    Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
            writeText: vi.fn((text: string) => {
                written.push(text);
                return Promise.resolve();
            }),
        },
    });
});

afterEach(() => {
    vi.useRealTimers();
});

describe('a code block in an answer', () => {
    it('has a Copy code button for each block, and inline code has none', async () => {
        renderChat();
        const pane = await openChat();
        const buttons = await waitFor(() => {
            const found = within(pane).getAllByRole('button', { name: 'Copy code' });
            expect(found).toHaveLength(2);
            return found;
        });
        for (const button of buttons) {
            expect(button.tagName).toBe('BUTTON');
            expect(button.getAttribute('type')).toBe('button');
            expect(button.tabIndex).toBeGreaterThanOrEqual(0);
            expect(button.closest('.chat-md-code')?.querySelector('pre')).not.toBeNull();
        }
        // The inline code is a bare `code` in a paragraph, with no control around it.
        const inline = [...pane.querySelectorAll('p code')];
        expect(inline.map((each) => each.textContent)).toEqual(['jason status']);
        expect(inline[0]!.closest('.chat-md-code')).toBeNull();
    });

    it('puts the block exactly as it is on the clipboard, without the language tag', async () => {
        renderChat();
        const pane = await openChat();
        const buttons = () => within(pane).getAllByRole('button', { name: /^Cop(y code|ied)$/ });
        await waitFor(() => expect(buttons()).toHaveLength(2));
        fireEvent.click(buttons()[0]!);
        await waitFor(() => expect(written).toEqual([block.replace(/\n$/, '')]));
        fireEvent.click(buttons()[1]!);
        await waitFor(() => expect(written.at(-1)).toBe('second'));
    });

    it('says Copied for about two seconds and then says Copy code again', async () => {
        renderChat();
        const pane = await openChat();
        await waitFor(() => within(pane).getAllByRole('button', { name: 'Copy code' }));
        vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
        const button = () => pane.querySelector<HTMLElement>('.chat-md-copy')!;
        await flush(async () => {
            fireEvent.click(button());
            await Promise.resolve();
            await Promise.resolve();
        });
        expect(button().textContent).toBe('Copied');
        await flush(async () => {
            vi.advanceTimersByTime(1900);
        });
        expect(button().textContent).toBe('Copied');
        await flush(async () => {
            vi.advanceTimersByTime(200);
        });
        expect(button().textContent).toBe('Copy code');
    });
});
