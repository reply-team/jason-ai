import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import { newChat } from '../../console/chat-model';
import { attention } from '../../console/needs';
import { startDraft, startMaking, startMode, startPick, startWaiting } from '../../console/start';
import { activePanel, chats, conversations, here, lines, setup, setupError } from '../../console/state';
import { fullAccessStanding } from '../../platform/full-access';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { parseRoute, route } from '../../router';
import { setupFound, stubDesktop, stubStream } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';

// The way out of Settings: Back at the head returns to the place the person was in when they opened Settings, else to
// the start place; Escape does the same where nothing else takes the key. The place is held by the page alone.

async function renderConsole(address = '#/chat') {
    // The window as a reload or a link leaves it, with no New chat from its menu: the console draws what an address names.
    const desktop = stubDesktop({ ...setupFound });
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
}

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
});

const settingsPane = () => document.querySelector<HTMLElement>('[data-pane="page"]')!;
const backButton = () => within(settingsPane()).getByRole('button', { name: 'Back' });
const open = async () => {
    flush(() => here().dock.page({ screen: 'settings' }));
    await waitFor(() => expect(within(settingsPane()).getByRole('heading', { name: 'Settings' })).toBeTruthy());
};
const press = (target: Element | Window, init: KeyboardEventInit = {}) =>
    flush(() => {
        fireEvent.keyDown(target, { key: 'Escape', ...init });
    });

describe('The way out of Settings', () => {
    it('Back returns to the place the person was in, a panel', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        fireEvent.click(backButton());
        await waitFor(() => expect(activePanel.value).toBe('connect'));
    });

    it('Back returns to a page the person was in', async () => {
        await renderConsole();
        flush(() => here().dock.page({ screen: 'system' }));
        await open();
        fireEvent.click(backButton());
        await waitFor(() => expect(location.hash).toBe('#/system'));
        expect(activePanel.value).toBe('page');
        expect(route.value.screen).toBe('system');
    });

    it('opened with no place before, as a deep link is, Back goes to the start place', async () => {
        await renderConsole('#/settings');
        await waitFor(() => expect(within(settingsPane()).getByRole('heading', { name: 'Settings' })).toBeTruthy());
        fireEvent.click(backButton());
        await waitFor(() => expect(activePanel.value).toBe('start'));
    });

    it('a reload inside Settings forgets the place: Back goes to the start place', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        cleanup();
        await renderConsole('#/settings');
        fireEvent.click(backButton());
        await waitFor(() => expect(activePanel.value).toBe('start'));
    });

    it('Escape does the same where nothing else takes the key', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        press(document.body);
        await waitFor(() => expect(activePanel.value).toBe('connect'));
    });

    it('Escape in a focused text field only leaves the field, never Settings', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        const field = within(settingsPane()).getAllByRole('textbox')[0] as HTMLElement;
        field.focus();
        expect(document.activeElement).toBe(field);
        press(field);
        expect(document.activeElement).not.toBe(field);
        expect(activePanel.value).toBe('page');
        press(document.body);
        await waitFor(() => expect(activePanel.value).toBe('connect'));
    });

    it('Escape with a dialog, a menu or an open list on the page does not leave Settings', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        for (const role of ['dialog', 'menu', 'listbox']) {
            const taker = document.createElement('div');
            taker.setAttribute('role', role);
            settingsPane().append(taker);
            press(document.body);
            expect(activePanel.value).toBe('page');
            taker.remove();
        }
    });

    it('Escape that something already took is left alone', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        await open();
        flush(() => {
            const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
            event.preventDefault();
            document.body.dispatchEvent(event);
        });
        expect(activePanel.value).toBe('page');
    });

    it('Escape does nothing once Settings is not shown', async () => {
        await renderConsole();
        flush(() => here().dock.show('connect'));
        press(document.body);
        expect(activePanel.value).toBe('connect');
    });

    it('the palette entry for Settings records the place as the other doors do', async () => {
        await renderConsole();
        flush(() => here().dock.show('needs'));
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        const input = await screen.findByPlaceholderText(/search/i);
        fireEvent.change(input, { target: { value: 'settings' } });
        fireEvent.keyDown(input, { key: 'Enter' });
        await waitFor(() => expect(within(settingsPane()).getByRole('heading', { name: 'Settings' })).toBeTruthy());
        fireEvent.click(backButton());
        await waitFor(() => expect(activePanel.value).toBe('needs'));
    });
});
