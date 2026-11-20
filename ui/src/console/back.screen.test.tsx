import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { fullAccessStanding } from '../platform/full-access';
import { mode, modeRefusal, modeUnopened } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import { setupFound, stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { newChat } from './chat-model';
import { Console } from './console';
import { attention } from './needs';
import { startDraft, startMaking, startMode, startPick, startWaiting } from './start';
import { activePanel, chats, conversations, here, lines, setup, setupError } from './state';

// Back from a page returns to the chat as it was, whole and working, in the desktop window and in what is drawn after a
// reload or a deep link. Nothing throws and no control of the dock is dead.

/** The console in the window at an address; `menuNewChat` has the desktop send its menu's New chat as the window opens. */
async function renderIn(address = '#/chat', menuNewChat = true) {
    const desktop = stubDesktop({ ...setupFound }, { newChat: menuNewChat });
    desktop.keep({ id: 'conv_1', title: 'Spring outreach' });
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('sender.list', 'sender.list/card-message-sender')
        .answer('approval.get', 'approval.get/email-message', 'approval.get/other-pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('route.resolve', 'route.resolve/global-default')
        .answer('ui.tokens', 'ui.tokens/two')
        .answer('system.info', 'system.info/with-ui');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = address;
    route.value = parseRoute(address);
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    await waitFor(() => expect(document.querySelector('[data-pane]')).not.toBeNull());
    return desktop;
}

const pane = () => document.querySelector<HTMLElement>('[data-pane="chat:conv_1"]')!;

async function openChat(): Promise<HTMLTextAreaElement> {
    const list = await waitFor(() =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' }),
    );
    fireEvent.click(await waitFor(() => within(list).getByText('Spring outreach').closest('button')!));
    await waitFor(() => expect(activePanel.value).toBe('chat:conv_1'));
    return await waitFor(() => within(pane()).getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement);
}

/** What a card's Open does: shows a page of the browser shell in the console's area. */
const openPage = () => flush(() => here().dock.page({ screen: 'system' }));
const back = async (to: string) => {
    flush(() => history.back());
    await waitFor(() => expect(location.hash).toBe(to));
};
const forward = async (to: string) => {
    flush(() => history.forward());
    await waitFor(() => expect(location.hash).toBe(to));
};

const chatShown = (draft: string) => {
    expect(pane().hidden).toBe(false);
    const box = within(pane()).getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement;
    expect(box.value).toBe(draft);
};

beforeEach(() => {
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    startDraft.value = '';
    startWaiting.value = null;
    startMaking.value = null;
    startMode.value = null;
    startPick.value = newChat('', null);
    fullAccessStanding.value = null;
});

afterEach(() => {
    cleanup();
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('Back from a page', () => {
    it('returns to the chat as it was, with its draft, and the dock still works', async () => {
        await renderIn();
        const box = await openChat();
        fireEvent.change(box, { target: { value: 'half a thought' } });
        openPage();
        await waitFor(() => expect(document.querySelector('[data-pane="page"]')!.hasAttribute('hidden')).toBe(false));
        await back('#/chat');
        await waitFor(() => chatShown('half a thought'));
        expect(() => here().dock.show('connect')).not.toThrow();
        expect(activePanel.value).toBe('connect');
        flush(() => here().dock.show('start'));
        expect(activePanel.value).toBe('start');
    });

    it('Back twice from two pages returns to the chat, and Forward shows the page again', async () => {
        await renderIn();
        const box = await openChat();
        fireEvent.change(box, { target: { value: 'still here' } });
        openPage();
        flush(() => here().dock.page({ screen: 'accounts' }));
        await back('#/system');
        await back('#/chat');
        await waitFor(() => chatShown('still here'));
        await forward('#/system');
        await waitFor(() => expect(activePanel.value).toBe('page'));
        await back('#/chat');
        await waitFor(() => chatShown('still here'));
        expect(() => here()).not.toThrow();
    });
});

describe('A reload draws what the address names', () => {
    it.each([
        ['#/system', 'page'],
        ['#/campaigns', 'campaigns'],
        ['#/chat', 'start'],
        ['', 'start'],
        ['#/attention', 'start'],
        ['#/attention/approvals/apr_1', 'needs'],
    ])('the address %j is drawn as %s', async (address, pane) => {
        await renderIn(address, false);
        expect(activePanel.value).toBe(pane);
        expect(() => here().dock.show('connect')).not.toThrow();
        flush(() => here().dock.show('start'));
        expect(activePanel.value).toBe('start');
    });

    it('a page reloaded on gives way to Start at the chat address and comes back with Back', async () => {
        await renderIn('#/system', false);
        flush(() => {
            location.hash = '#/chat';
        });
        await waitFor(() => expect(activePanel.value).toBe('start'));
        await back('#/system');
        await waitFor(() => expect(activePanel.value).toBe('page'));
        await forward('#/chat');
        await waitFor(() => expect(activePanel.value).toBe('start'));
        expect(() => here().dock.show('connect')).not.toThrow();
    });

    it('a deep link to a panel, then a page, then Back, shows the panel again', async () => {
        await renderIn('#/campaigns', false);
        openPage();
        await back('#/chat');
        await waitFor(() => expect(activePanel.value).toBe('campaigns'));
        expect(() => here().dock.show('connect')).not.toThrow();
    });
});

describe.each(['#/chat', ''])(
    'The same page opened again after the chat was chosen, from the address %j',
    (address) => {
        it('Back shows the chat with its draft and Forward shows the page', async () => {
            await renderIn(address);
            const box = await openChat();
            fireEvent.change(box, { target: { value: 'kept' } });
            flush(() => here().dock.page({ screen: 'accounts' }));
            // The chat is chosen from the sidebar: the bar must not keep naming the page.
            flush(() => here().dock.showChat('conv_1'));
            expect(activePanel.value).toBe('chat:conv_1');
            flush(() => here().dock.page({ screen: 'accounts' }));
            await back('#/chat');
            await waitFor(() => chatShown('kept'));
            await forward('#/accounts');
            await waitFor(() => expect(activePanel.value).toBe('page'));
            await back('#/chat');
            await waitFor(() => chatShown('kept'));
        });

        it('a line that arrives while the page is shown is in the chat on Back', async () => {
            const desktop = await renderIn(address);
            const box = await openChat();
            fireEvent.change(box, { target: { value: 'while it runs' } });
            openPage();
            flush(() => desktop.emit('conv_1', { kind: 'text', text: 'Arrived while the page was shown' }));
            await back('#/chat');
            await waitFor(() => chatShown('while it runs'));
            expect(within(pane()).getByText('Arrived while the page was shown')).toBeTruthy();
        });
    },
);
