import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { DesktopSetup } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations, lines, setup } from './state';
import { startDraft, startWaiting } from './start';

// The window opens on the question who the person wants to reach. What is sent there starts a set-up chat; with no AI
// connected the AI card comes first and the message waits; Reply is asked for when the work needs a way to send.

const noHost: DesktopSetup = { ...setupFound, hosts: setupFound.hosts.map((each) => ({ ...each, found: false })) };
const noReply: DesktopSetup = {
    ...setupFound,
    reply: { ...setupFound.reply, signedIn: false, wired: false, user: null },
};

function renderConsole(desktop: StubDesktop): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
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

const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const startForm = () => screen.getByRole('form', { name: 'Start' });
const ready = () => waitFor(() => expect(setup.value).not.toBeNull());
const ask = (text: string) => {
    fireEvent.change(within(startForm()).getByRole('textbox', { name: 'Who do you want to reach?' }), {
        target: { value: text },
    });
    fireEvent.click(within(startForm()).getByRole('button', { name: 'Start' }));
};

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    startDraft.value = '';
    startWaiting.value = null;
});

describe('the start screen', () => {
    it('opens on the question who the person wants to reach, with no chat made yet', async () => {
        const desktop = renderConsole(stubDesktop(undefined, { startScreen: true }));
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?', level: 1 }));
        expect(startForm()).toBeTruthy();
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
    });

    it('starts a set-up chat with what is sent, on the AI that is connected, and shows the chat', async () => {
        const desktop = renderConsole(stubDesktop(undefined, { startScreen: true }));
        await waitFor(() => startForm());
        await ready();
        ask('Heads of sales at logistics firms in Poland');
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.create')).toEqual([{ name: 'chat.create', args: [] }]);
        expect(calls(desktop, 'chat.send')[0]!.args[1]).toBe('Heads of sales at logistics firms in Poland');
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        expect(screen.queryByRole('form', { name: 'Start' })).toBeNull();
    });

    it('sends nothing for an empty message', async () => {
        const desktop = renderConsole(stubDesktop(undefined, { startScreen: true }));
        await waitFor(() => startForm());
        ask('   ');
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        expect(startWaiting.value).toBeNull();
    });

    it('puts the AI card first when no AI is connected, and keeps the message waiting', async () => {
        const desktop = renderConsole(stubDesktop(noHost, { startScreen: true }));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        const card = await waitFor(() => screen.getByRole('region', { name: 'Connect an AI' }));
        expect(within(card).getByText('Claude Code')).toBeTruthy();
        expect(within(startForm().parentElement!).getByText(/Founders of seed-stage fintechs/)).toBeTruthy();
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
    });

    it('sends the waiting message once an AI is connected, and only once', async () => {
        const desktop = renderConsole(stubDesktop(noHost, { startScreen: true }));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        await waitFor(() => screen.getByRole('region', { name: 'Connect an AI' }));
        desktop.status = setupFound;
        flush(() => desktop.changed());
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.send')[0]!.args[1]).toBe('Founders of seed-stage fintechs');
        flush(() => desktop.changed());
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        expect(calls(desktop, 'chat.create')).toHaveLength(1);
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
    });

    it('does not ask for Reply at the start, even with no Reply sign-in', async () => {
        const desktop = renderConsole(stubDesktop(noReply, { startScreen: true }));
        await waitFor(() => startForm());
        await ready();
        ask('Recruiters in Lisbon');
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
    });

    it('asks for Reply in a chat that is about a campaign, once there is no way to send', async () => {
        const desktop = renderConsole(stubDesktop(noReply, { startScreen: true }));
        await waitFor(() => startForm());
        await ready();
        flush(() => desktop.command('navigate.campaigns'));
        await waitFor(() => screen.getByText('Spring'));
        const row = screen.getByText('Spring').closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        const card = await waitFor(() => screen.getByRole('region', { name: 'Connect Reply' }));
        expect(within(card).getByRole('button', { name: 'Sign in' })).toBeTruthy();
    });
});
