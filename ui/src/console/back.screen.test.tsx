import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { fullAccessStanding } from '../platform/full-access';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, setupFound, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { newChat } from './chat-model';
import { Console } from './console';
import { attention } from './needs';
import { startDraft, startMaking, startMode, startPick, startWaiting } from './start';
import { activePanel, chats, conversations, here, lines, setup, setupError } from './state';

// Back from a page returns to the chat as it was, whole and working, in every place: the tab the desktop opened, the
// desktop window, and what is drawn after a reload or a deep link. Nothing throws and no control of the dock is dead.

type Place = 'tab' | 'window';

async function renderIn(place: Place, address = '#/chat') {
    const desktop = chatDesktop({ ...setupFound });
    desktop.keep({ id: 'conv_1', title: 'Spring outreach' });
    let held: Bridge | null = null;
    let used: typeof desktop | ReturnType<typeof tabDesktop> = desktop;
    if (place === 'tab') {
        const stand = standInBridge(desktop);
        const bridge = new Bridge({ port: 43210, secret: standInSecret }, stand.fetch);
        used = tabDesktop(bridge);
        held = bridge;
        setPlace('tab', used);
        await loadMode(used);
    } else setPlace('window');
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
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
    // The tab the desktop opened holds its bridge, as the page's start leaves it.
    session.bridge.value = held;
    render(<Console api={fake} desktop={used} stream={stubStream().stream} bootstrapped />);
    await waitFor(() => screen.getByRole('form', { name: 'Start' }));
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
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
    session.bridge.value = null;
});

describe.each<Place>(['tab', 'window'])('Back from a page, in the %s', (place) => {
    it('returns to the chat as it was, with its draft, and the dock still works', async () => {
        await renderIn(place);
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
        await renderIn(place);
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

describe('Back after a reload or a deep link', () => {
    it('after a reload on the page, Back draws nothing that throws and leaves the dock working', async () => {
        await renderIn('tab', '#/system');
        flush(() => {
            location.hash = '#/chat';
        });
        await waitFor(() => expect(location.hash).toBe('#/chat'));
        expect(() => here().dock.show('connect')).not.toThrow();
        expect(document.querySelector('.tab-chat')).toBeNull();
        flush(() => here().dock.show('start'));
        expect(screen.getByRole('form', { name: 'Start' })).toBeTruthy();
        // Back to the address the page was reloaded on draws that page.
        await back('#/system');
        await waitFor(() => expect(activePanel.value).toBe('page'));
        await forward('#/chat');
        expect(() => here().dock.show('connect')).not.toThrow();
    });

    it('a deep link first, then a page, then Back, shows where the console started', async () => {
        await renderIn('tab', '#/campaigns');
        openPage();
        await back('#/campaigns');
        await waitFor(() => expect(activePanel.value).toBe('start'));
        expect(() => here().dock.show('connect')).not.toThrow();
    });
});
