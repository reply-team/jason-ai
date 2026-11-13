import { act as flush, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import { chats } from '../../console/state';
import { Bridge } from '../../platform/bridge';
import { route } from '../../router';
import { standInBridge, standInSecret, type StandIn } from '../../test/bridge';
import { setupFound, stubDesktop, type StubDesktop } from '../../test/desktop';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

// The classic shell's Chat screen with a bridge to the desktop's chat: the console's list, a conversation and its box,
// with a stand-in bridge over a stub desktop and no network. A tab never says yes: no card has a button, the mode is a
// label, and every act only the desktop's own window may do is not drawn.

const campaigns = () =>
    new FakeApi().answer('campaign.list', 'campaign.list/screen').answer('campaign.summary', 'campaign.summary/all');

let desktop: StubDesktop;
let stand: StandIn;

beforeEach(() => {
    desktop = stubDesktop(setupFound);
    stand = standInBridge(desktop);
    sessionStorage.clear();
    chats.value = {};
});
afterEach(() => {
    session.bridge.value?.close();
    session.bridge.value = null;
    sessionStorage.clear();
    delete window.jasonDesktop;
});

/** The screen as a tab the desktop opened shows it. */
async function openTab(): Promise<void> {
    const bridge: Bridge = new Bridge({ port: 43210, secret: standInSecret }, stand.fetch, () => {
        if (session.bridge.value === bridge) session.dropBridge();
    });
    session.bridge.value = bridge;
    renderApp(campaigns());
    route.value = { screen: 'chat' };
    await waitFor(() => screen.getByRole('navigation', { name: 'Chats' }));
}

const list = () => screen.getByRole('navigation', { name: 'Chats' });
const called = (member: string) => stand.asked.filter((each) => each.member === member);

describe('the Chat screen in a tab the desktop opened', () => {
    it("shows the list of the desktop's conversations in place of its sentence", async () => {
        desktop.keep({ id: 'conv_1', title: 'Spring outreach' });
        desktop.keep({ id: 'conv_2', title: 'Set-up' });
        await openTab();
        expect(screen.queryByText(/part of the desktop application/)).toBeNull();
        await waitFor(() => within(list()).getByRole('button', { name: 'Spring outreach' }));
        expect(called('chat.list').length).toBeGreaterThan(0);
        expect(screen.getByText('Choose a chat, or start a new one.')).toBeTruthy();
    });

    it("opens a conversation with what the desktop kept and sends the person's message through the bridge", async () => {
        desktop.keep({
            id: 'conv_1',
            title: 'Spring outreach',
            entries: [{ kind: 'user', text: 'Earlier question' }] as never,
        });
        await openTab();
        fireEvent.click(await waitFor(() => within(list()).getByRole('button', { name: 'Spring outreach' })));
        await waitFor(() => screen.getByText('Earlier question'));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go on' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(called('chat.send')).toHaveLength(1));
        expect(called('chat.send')[0]!.args![0]).toBe('conv_1');
        expect(called('chat.send')[0]!.args![1]).toBe('Go on');
    });

    it("starts a new chat with the desktop's own call", async () => {
        await openTab();
        fireEvent.click(screen.getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(called('chat.create')).toHaveLength(1));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
    });

    it('lets events reach the conversation that is shown, from the stream alone, and never feeds', async () => {
        desktop.keep({ id: 'conv_1', title: 'Spring outreach' });
        await openTab();
        fireEvent.click(await waitFor(() => within(list()).getByRole('button', { name: 'Spring outreach' })));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        await waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
        flush(() => desktop.emit('conv_1', { kind: 'text', text: 'Hello from the desktop' }));
        await waitFor(() => screen.getByText('Hello from the desktop'));
        expect(stand.asked.filter((each) => each.member === 'chat.feed')).toEqual([]);
        expect(stand.asked.filter((each) => each.path === '/events')).toHaveLength(1);
    });
});

describe('what a tab never says yes to', () => {
    async function shown(extra: Partial<Parameters<StubDesktop['keep']>[0]> = {}): Promise<void> {
        desktop.keep({ id: 'conv_1', title: 'Spring outreach', ...extra });
        await openTab();
        fireEvent.click(await waitFor(() => within(list()).getByRole('button', { name: 'Spring outreach' })));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        await waitFor(() => expect(stand.asked.some((each) => each.path === '/events')).toBe(true));
    }

    it('draws a permission card with no buttons and one sentence that it is answered in Jason Desktop', async () => {
        await shown();
        flush(() =>
            desktop.emit('conv_1', {
                kind: 'permission',
                request: 'req_1',
                title: 'Run a command',
                detail: 'git status',
                remember: 'git status …',
            }),
        );
        await waitFor(() => screen.getByText('Run a command'));
        expect(screen.getByText('git status')).toBeTruthy();
        expect(screen.getByText('This is answered in Jason Desktop.')).toBeTruthy();
        for (const name of [/^Allow/, /Don't allow/]) expect(screen.queryByRole('button', { name })).toBeNull();
        expect(stand.asked.some((each) => each.member === 'chat.answer')).toBe(false);
    });

    it('shows the mode as a label, not a control', async () => {
        await shown();
        expect(screen.queryByRole('combobox', { name: 'Access mode' })).toBeNull();
        const label = screen.getByLabelText('Access mode');
        expect(label.tagName).toBe('SPAN');
        expect(label.textContent).toBe('Ask first');
    });

    it('draws no Start afresh, no Remove, no Open folder, no Reply card and no Connect button', async () => {
        await shown({ entries: [{ kind: 'notice', text: 'Made a folder', folder: 'spring' }] as never });
        expect(screen.getByText('Made a folder')).toBeTruthy();
        for (const name of ['Start afresh', 'Remove', 'Open folder', 'Connect', 'Not now'])
            expect(screen.queryByRole('button', { name })).toBeNull();
        // The one act in the table that the bar keeps.
        expect(screen.getByRole('button', { name: 'Rename' })).toBeTruthy();
    });

    it('offers no button that attaches a set-up chat to a campaign, and no Approve and run', async () => {
        await shown({
            entries: [
                { kind: 'pick', campaignIds: ['cmp_1'] },
                { kind: 'text', text: 'The plan is: do it' },
            ] as never,
        });
        expect(screen.queryByRole('button', { name: 'Spring' })).toBeNull();
        expect(screen.queryByRole('button', { name: /Approve and run/ })).toBeNull();
        expect(screen.queryByRole('button', { name: /Keep planning/ })).toBeNull();
    });

    it('takes no message for a chat in Full access, and says where to send it from', async () => {
        await shown({ mode: 'full_access' });
        expect(screen.getByText(/Send messages to it from Jason Desktop/)).toBeTruthy();
        expect((screen.getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement).disabled).toBe(true);
        expect((screen.getByRole('button', { name: 'Send' }) as HTMLButtonElement).disabled).toBe(true);
        expect(called('chat.send')).toEqual([]);
    });

    it('calls nothing outside the table on any of it', async () => {
        await shown();
        const outside = stand.asked.filter(
            (each) =>
                each.member !== undefined &&
                each.member !== '(not known)' &&
                ![
                    'mode.get',
                    'theme.get',
                    'fullAccess.standing',
                    'setup.status',
                    'chat.list',
                    'chat.open',
                    'chat.create',
                    'chat.send',
                    'chat.stop',
                    'chat.rename',
                ].includes(each.member),
        );
        expect(outside).toEqual([]);
    });
});

describe('the Chat screen in a tab without a bridge', () => {
    it("says the chat is the desktop's and that it can be opened in this tab from Jason Desktop", async () => {
        renderApp(new FakeApi());
        route.value = { screen: 'chat' };
        await waitFor(() => screen.getByRole('heading', { name: 'Chat' }));
        expect(screen.getByText(/part of the desktop application/)).toBeTruthy();
        expect(screen.getByText(/opened in this tab from Jason Desktop: Open in, then the browser/)).toBeTruthy();
        expect(screen.queryByRole('navigation', { name: 'Chats' })).toBeNull();
        expect(screen.getByRole('link', { name: /Get Jason Desktop/ })).toBeTruthy();
    });

    it('goes back to the sentence when the bridge is gone', async () => {
        desktop.keep({ id: 'conv_1', title: 'Spring outreach' });
        await openTab();
        stand.forget();
        fireEvent.click(await waitFor(() => within(list()).getByRole('button', { name: 'Spring outreach' })));
        await waitFor(() => screen.getByText(/part of the desktop application/));
        expect(session.bridge.value).toBeNull();
    });
});
