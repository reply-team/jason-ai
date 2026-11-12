import { act as flush, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';

// A chat's access mode: the menu beside the composer, Full access disabled where the desktop says it cannot be chosen,
// and the line under the composer, drawn from the mode the desktop holds and from nothing the page asked for.

function renderConsole(desktop: StubDesktop = chatDesktop()): StubDesktop {
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

const menu = () => screen.getByRole<HTMLSelectElement>('combobox', { name: 'Access mode' });
const line = () => screen.queryByRole('status', { name: 'Full access is on' });
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const choose = (mode: string) => fireEvent.change(menu(), { target: { value: mode } });
const chatId = () => Object.keys(chats.value)[0]!;

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('a chat’s access mode', () => {
    it('starts in Ask first, offers Plan only and Full access beside the composer, and no fourth mode', async () => {
        renderConsole();
        await waitFor(() => menu());
        expect(menu().value).toBe('ask_first');
        expect([...menu().options].map((each) => [each.value, each.text, each.disabled])).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['full_access', 'Full access', false],
        ]);
        expect(line()).toBeNull();
    });

    it('draws the line under the composer once the desktop holds the chat in Full access, and never before', async () => {
        const desktop = chatDesktop();
        desktop.confirmFullAccess = false;
        renderConsole(desktop);
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(1));
        expect(calls(desktop, 'chat.setMode')[0]!.args[1]).toBe('full_access');
        // The person said no to the desktop's own box: the menu goes back, and no line is drawn.
        await waitFor(() => expect(menu().value).toBe('ask_first'));
        expect(line()).toBeNull();

        desktop.confirmFullAccess = true;
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(line()!.textContent).toContain(
            'Full access: the agent runs commands without asking, except those Jason cannot read.',
        );
        expect(menu().value).toBe('full_access');
    });

    it('takes the line away when the desktop puts the chat back to Ask first, whoever asked', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'ask_first' }));
        expect(line()).toBeNull();
        expect(menu().value).toBe('ask_first');
        // And draws it only from the desktop's word: an event alone puts the chat in Full access.
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'full_access' }));
        expect(line()).not.toBeNull();
    });

    it('Change on the line takes the person to the menu', async () => {
        renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        fireEvent.click(screen.getByRole('button', { name: 'Change' }));
        expect(document.activeElement).toBe(menu());
    });

    it('shows Full access disabled with the desktop’s reason on a host that has none, and choosing that host leaves Full access', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: 'codex' } });
        await waitFor(() => expect(line()).toBeNull());
        expect(calls(desktop, 'chat.setMode').at(-1)!.args[1]).toBe('ask_first');
        const full = [...menu().options].find((each) => each.value === 'full_access')!;
        expect(full.disabled).toBe(true);
        expect(full.text).toBe('Full access: Full access runs on Claude Code only.');
    });

    it('says under the composer, on Codex alone, that the chat does not read the person’s Codex config.toml', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        const note = () => screen.queryByRole('note', { name: 'What Codex reads' });
        expect(note()).toBeNull();
        fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: 'codex' } });
        await waitFor(() => expect(note()).not.toBeNull());
        expect(note()!.textContent).toBe(
            'The chat does not read your Codex config.toml, so a provider or profile set there is not used; connect a key on the Connect card.',
        );
        fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: 'claude' } });
        await waitFor(() => expect(note()).toBeNull());
    });

    it.each([
        [
            'win32',
            'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, but not the key Jason’s own commands use, its run token. Codex runs it read-only.',
        ],
        [
            'darwin',
            'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, and the key Jason’s own commands use, its run token. Codex runs it read-only.',
        ],
        [
            'linux',
            'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, and the key Jason’s own commands use, its run token. Codex runs it read-only.',
        ],
        [
            'unsaid',
            'A Codex command can read your files, Jason’s data and settings among them, a plugin’s secret kept in settings.json too, and the key Jason’s own commands use, its run token. Codex runs it read-only.',
        ],
    ] as const)(
        'says under the composer, on Codex and on the OpenAI key alike, what a Codex command can reach on %s, as seen there',
        async (platform, line) => {
            const status = structuredClone(setupFound);
            const codex = status.hosts.find((each) => each.id === 'codex')!;
            codex.found = true;
            status.hosts.push({
                ...structuredClone(codex),
                id: 'codex-api-key',
                label: 'OpenAI',
                kind: 'provider',
                version: null,
                install: null,
            });
            const desktop = chatDesktop(status);
            // Only Windows was seen keeping the run token from a command; a desktop that does not say its system is told nothing more.
            const said = desktop as { platform?: string };
            if (platform === 'unsaid') delete said.platform;
            else said.platform = platform;
            renderConsole(desktop);
            await waitFor(() => menu());
            const note = () => screen.queryByRole('note', { name: 'What a Codex command can reach' });
            expect(note()).toBeNull();
            for (const id of ['codex', 'codex-api-key']) {
                fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: id } });
                await waitFor(() => expect(note()).not.toBeNull());
                expect(note()!.textContent).toBe(line);
                expect(note()!.textContent).not.toMatch(/act for you|acts through nothing|network/);
            }
            fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: 'claude' } });
            await waitFor(() => expect(note()).toBeNull());
        },
    );

    it('says a mode the desktop refused in its own words, and keeps the mode it holds', async () => {
        const desktop = chatDesktop();
        desktop.refuse.set('chat.setMode', 'That conversation is not one this desktop keeps.');
        renderConsole(desktop);
        await waitFor(() => menu());
        choose('plan_only');
        await waitFor(() => expect(screen.getByText('That conversation is not one this desktop keeps.')).toBeTruthy());
        expect(menu().value).toBe('ask_first');
    });

    it('reopens a kept conversation in the mode the desktop holds', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000P1', title: 'Planning', mode: 'plan_only' });
        renderConsole(desktop);
        await waitFor(() => screen.getByRole('button', { name: 'Planning' }));
        fireEvent.click(screen.getByRole('button', { name: 'Planning' }));
        await waitFor(() => expect(chats.value['conv_01JDKEPT0000000000000000P1']?.mode).toBe('plan_only'));
    });
});

describe('a chat’s access mode, on the host and around a running turn', () => {
    it('asks the desktop for the mode on the host the next turn runs on', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(1));
        expect(calls(desktop, 'chat.setMode')[0]!.args.slice(1)).toEqual(['full_access', 'claude']);
    });

    it('says in the desktop’s words a Full access it refused on the host chosen, and shows no line', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        fireEvent.change(screen.getByRole('combobox', { name: 'Agent host' }), { target: { value: 'codex' } });
        choose('full_access');
        await waitFor(() => expect(screen.getByText('Full access runs on Claude Code only.')).toBeTruthy());
        expect(line()).toBeNull();
        expect(menu().value).toBe('ask_first');
    });

    it('leaving Full access while a turn runs gives the waiting message back, as Stop does: no turn follows by itself', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'Approve them all' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'And then the next' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send when the turn ends' }));
        await waitFor(() => expect(screen.getByRole('status', { name: 'Waiting message' })).toBeTruthy());
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        // The desktop stops the turn it ran in Full access; its end says no error.
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }).value).toBe(
                'And then the next',
            ),
        );
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
        expect(screen.queryByRole('status', { name: 'Waiting message' })).toBeNull();
    });
});

describe('a change to Plan only around a running turn', () => {
    it('gives the waiting message back, as Stop does: no turn follows by itself', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'Change the steps' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'And then the next' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send when the turn ends' }));
        await waitFor(() => expect(screen.getByRole('status', { name: 'Waiting message' })).toBeTruthy());
        choose('plan_only');
        await waitFor(() => expect(menu().value).toBe('plan_only'));
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }).value).toBe(
                'And then the next',
            ),
        );
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
    });
});

describe('Full access answered once for good', () => {
    const always = { givenAt: '2026-11-11T09:00:00.000Z', newChats: false };

    it('while Always stands the menu enters Full access without the box and offers the take-back after the modes', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        // A box would say no: the menu enters all the same, since the desktop asks nothing while the choice stands.
        desktop.confirmFullAccess = false;
        renderConsole(desktop);
        await waitFor(() => expect(menu().options).toHaveLength(4));
        expect([...menu().options].map((each) => [each.value, each.text, each.disabled])).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['full_access', 'Full access', false],
            ['take_back', 'Full access: ask each time again', false],
        ]);
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(menu().value).toBe('full_access');
    });

    it('the take-back in the menu takes the choice back and leaves the chat in the mode it is in, its line still shown', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        renderConsole(desktop);
        await waitFor(() => expect(menu().options).toHaveLength(4));
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        choose('take_back');
        await waitFor(() => expect(calls(desktop, 'fullAccess.takeBack')).toHaveLength(1));
        expect(calls(desktop, 'fullAccess.takeBack')[0]!.args).toEqual([]);
        await waitFor(() => expect(menu().options).toHaveLength(3));
        expect(menu().value).toBe('full_access');
        expect(line()).not.toBeNull();
        expect(calls(desktop, 'chat.setMode')).toHaveLength(1);
    });

    it('follows a take-back made elsewhere: the option goes', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        renderConsole(desktop);
        await waitFor(() => expect(menu().options).toHaveLength(4));
        flush(() => desktop.stand(null));
        expect(menu().options).toHaveLength(3);
    });

    it('a new chat that starts in Full access by the standing choice shows the line from its first moment, before anything is sent', async () => {
        const desktop = chatDesktop();
        desktop.standing = { ...always, newChats: true };
        renderConsole(desktop);
        await waitFor(() => expect(line()).not.toBeNull());
        expect(menu().value).toBe('full_access');
        expect(calls(desktop, 'chat.create')[0]!.args).toEqual([undefined, 'claude']);
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
        expect(calls(desktop, 'chat.setMode')).toHaveLength(0);
    });

    it('a new chat on a host that cannot take Full access starts in Ask first with no notice; the menu shows the mode it has', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'claude')!.found = false;
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = chatDesktop(status);
        desktop.standing = { ...always, newChats: true };
        renderConsole(desktop);
        await waitFor(() => menu());
        await waitFor(() => expect(calls(desktop, 'chat.create')).toHaveLength(1));
        expect(calls(desktop, 'chat.create')[0]!.args).toEqual([undefined, 'codex']);
        expect(menu().value).toBe('ask_first');
        expect(line()).toBeNull();
        expect(screen.queryByText(/Full access/, { selector: '.note:not(option)' })).toBeNull();
    });

    it('without "start new chats" a new chat is made as before, naming no host, and starts in Ask first', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        renderConsole(desktop);
        await waitFor(() => menu());
        await waitFor(() => expect(calls(desktop, 'chat.create')).toHaveLength(1));
        expect(calls(desktop, 'chat.create')[0]!.args).toEqual([]);
        expect(menu().value).toBe('ask_first');
    });
});
