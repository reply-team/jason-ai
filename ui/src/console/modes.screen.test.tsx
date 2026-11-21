import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import * as consoleCampaigns from './campaigns';
import { cardReads } from './card-frame';
import { proposals } from './card-plan';
import { openConversation } from './chat';
import { Console } from './console';
import { chats, conversations } from './state';
import { choicesOf, chooseIn, currentOf, menuButton, waitForMenus } from '../test/menus';

// A chat's access mode: the menu beside the composer, Full access disabled where the desktop says it cannot be chosen,
// and the line under the composer, drawn from the mode the next turn runs in as the desktop hands it, and from nothing
// the page asked for.

function renderConsole(desktop: StubDesktop = chatDesktop(), more?: (fake: FakeApi) => void): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    more?.(fake);
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
    return desktop;
}

const menu = () => menuButton('Access mode');
const value = () => currentOf('Access mode', 'mode');
const options = () => choicesOf('Access mode', 'mode').map((each) => [each.value, each.text, each.disabled]);
const chooseHost = (id: string) => chooseIn('Model', 'host', id);
const line = () => screen.queryByRole('status', { name: 'Full access is on' });
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const choose = (mode: string) => chooseIn('Access mode', 'mode', mode);
const chatId = () => Object.keys(chats.value)[0]!;

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    proposals.value = {};
    cardReads.value = {};
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('a chat’s access mode', () => {
    it('starts in Full access, the mode the desktop makes a new chat in, with its line, and offers the four modes narrow to wide', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        expect(value()).toBe('full_access');
        expect(options()).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['auto_edit', 'Auto-edit', false],
            ['full_access', 'Full access', false],
        ]);
        await waitFor(() => expect(line()).not.toBeNull());
        // The desktop made it so: the page asked for no mode and named no host.
        expect(calls(desktop, 'chat.create')[0]!.args).toEqual([]);
        expect(calls(desktop, 'chat.setMode')).toHaveLength(0);
    });

    it('draws the line under the composer once the desktop holds the chat in Full access, and never before', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        desktop.refuse.set('chat.setMode', 'Full access is not in this version yet.');
        choose('full_access');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(2));
        expect(calls(desktop, 'chat.setMode')[1]!.args[1]).toBe('full_access');
        // The desktop refused: the menu goes back, and no line is drawn.
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        expect(line()).toBeNull();

        desktop.refuse.delete('chat.setMode');
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        // In the console's form: the mode's name, a dot and what it does, with no link; the menu beside it changes it.
        expect(line()!.textContent).toBe(
            "Full access · the AI runs commands without asking, except those that name Jason's own settings, data or variables",
        );
        expect(within(line()!.parentElement!).queryByRole('button')).toBeNull();
        expect(value()).toBe('full_access');
    });

    it('takes the line away when the desktop puts the chat back to Ask first, whoever asked', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        await waitFor(() => expect(line()).not.toBeNull());
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'ask_first' }));
        expect(line()).toBeNull();
        expect(value()).toBe('ask_first');
        // And draws it only from the desktop's word: an event alone puts the chat in Full access.
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'full_access' }));
        expect(line()).not.toBeNull();
    });

    it('on Codex, runs Full access as itself: offered, drawn, said in Codex’s own words in the menu and under the composer, with no sentence why, and choosing Codex asks the desktop for no mode', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        const asked = calls(desktop, 'chat.setMode').length;
        chooseHost('codex');
        const full = () => choicesOf('Access mode', 'mode').find((each) => each.value === 'full_access')!;
        await waitFor(() => expect(line()!.textContent).toBe(codexFullLine));
        expect(value()).toBe('full_access');
        expect([full().text, full().disabled, full().said]).toEqual(['Full access', false, codexFullSays]);
        expect(screen.queryByRole('note', { name: /^Why the next turn runs in/ })).toBeNull();
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
        // Back on Claude Code, Claude Code's own words.
        chooseHost('claude');
        await waitFor(() => expect(line()!.textContent).toBe(claudeFullLine));
        expect(full().said).toBe(
            "Runs commands on this computer without asking, except those that name Jason's own settings, data or variables.",
        );
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
    });

    it('on Codex, says Ask first reads and proposes, in the menu and under the composer, and Claude Code’s Ask first in its own words', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        chooseHost('codex');
        choose('ask_first');
        await waitFor(() =>
            expect(calls(desktop, 'chat.setMode').at(-1)?.args.slice(1)).toEqual(['ask_first', 'codex']),
        );
        const ask = () => choicesOf('Access mode', 'mode').find((each) => each.value === 'ask_first')!;
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        expect(ask().said).toBe(codexAskSays);
        expect(screen.getByText(codexAskLine)).toBeTruthy();
        expect(line()).toBeNull();
        chooseHost('claude');
        await waitFor(() => expect(ask().said).toBe('Asks on a card before anything Jason does not settle itself.'));
        expect(screen.queryByText(codexAskLine)).toBeNull();
    });

    it('from a desktop of an earlier version, whose hosts hand no mode a turn runs in, draws the chat’s own mode on every host, as before', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        for (const host of status.hosts) delete (host as { runs?: unknown }).runs;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        await waitFor(() => expect(line()).not.toBeNull());
        chooseHost('codex');
        await flush(() => new Promise((resolve) => setTimeout(resolve, 100)));
        expect(value()).toBe('full_access');
        expect(line()).not.toBeNull();
        expect(screen.queryByRole('note', { name: /^Why the next turn runs in/ })).toBeNull();
    });

    it('says under the composer, on Codex alone and outside Full access, that the chat does not read the person’s Codex config.toml', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        const note = () => screen.queryByRole('note', { name: 'What Codex reads' });
        expect(note()).toBeNull();
        // In Full access a Codex turn reads it, as Codex in a terminal does: nothing is said.
        chooseHost('codex');
        await waitFor(() => expect(line()!.textContent).toBe(codexFullLine));
        expect(note()).toBeNull();
        choose('ask_first');
        await waitFor(() => expect(note()).not.toBeNull());
        expect(note()!.textContent).toBe(
            'In this mode the chat does not read your Codex config.toml; Full access does. A provider or profile set there is not used here; connect a key on the Connect card.',
        );
        chooseHost('claude');
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
            choose('ask_first');
            await waitForMenus(() => expect(value()).toBe('ask_first'));
            expect(note()).toBeNull();
            for (const id of ['codex', 'codex-api-key']) {
                chooseHost(id);
                await waitFor(() => expect(note()).not.toBeNull());
                expect(note()!.textContent).toBe(line);
                expect(note()!.textContent).not.toMatch(/act for you|acts through nothing|network/);
            }
            // In Full access a Codex command reaches what the person can, which the Full access line says.
            choose('full_access');
            await waitFor(() => expect(note()).toBeNull());
            chooseHost('claude');
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
        expect(value()).toBe('full_access');
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
        choose('ask_first');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(1));
        choose('full_access');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(2));
        expect(calls(desktop, 'chat.setMode').map((each) => each.args.slice(1))).toEqual([
            ['ask_first', 'claude'],
            ['full_access', 'claude'],
        ]);
    });

    it('says in the desktop’s words why Full access cannot be had on the host chosen, asks nothing, and shows no line', async () => {
        const status = structuredClone(setupFound);
        const codex = status.hosts.find((each) => each.id === 'codex')!;
        codex.found = true;
        // A desktop that does not offer Full access yet says so on every host.
        codex.fullAccess = 'Full access is not in this version yet.';
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        chooseHost('codex');
        const full = choicesOf('Access mode', 'mode').find((each) => each.value === 'full_access')!;
        expect([full.disabled, full.said]).toEqual([true, 'Full access is not in this version yet.']);
        choose('full_access');
        expect(calls(desktop, 'chat.setMode').map((each) => each.args[1])).toEqual(['ask_first']);
        expect(line()).toBeNull();
        expect(value()).toBe('ask_first');
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
        fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
        await waitFor(() => expect(screen.getByRole('group', { name: 'Queued message' })).toBeTruthy());
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
        expect(screen.queryByRole('group', { name: 'Queued message' })).toBeNull();
    });
});

const editing = () => screen.queryByRole('status', { name: 'Auto-edit is on' });
const codexFullLine = 'Full access · Codex runs commands on this computer without asking, with no sandbox';
const claudeFullLine =
    "Full access · the AI runs commands without asking, except those that name Jason's own settings, data or variables";
const codexFullSays = 'Runs commands on this computer without asking, with no sandbox.';
const codexAskSays = 'Reads and proposes. Codex cannot ask before it acts.';
const codexAskLine = 'Ask first · reads and proposes. Codex cannot ask before it acts';
const codexAutoEdit =
    'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';

describe('Auto-edit', () => {
    it('enters with no box, draws its own line under the composer, and takes it away on leaving', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        expect(editing()).toBeNull();
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        expect(calls(desktop, 'chat.setMode')[0]!.args.slice(1)).toEqual(['auto_edit', 'claude']);
        expect(editing()!.textContent).toBe(
            "Auto-edit · the AI creates, moves, copies and removes files in the chat's own folder without asking; everything else is as in Ask first",
        );
        expect(line()).toBeNull();
        expect(value()).toBe('auto_edit');
        expect(screen.queryByRole('button', { name: 'Change' })).toBeNull();
        choose('ask_first');
        await waitFor(() => expect(editing()).toBeNull());
        // Drawn from the desktop's word alone.
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'auto_edit' }));
        expect(editing()).not.toBeNull();
    });

    it('is disabled with the desktop’s reason on Codex, where the chat’s next turn runs in Ask first, saying why, and choosing Codex asks the desktop for no mode', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        const asked = calls(desktop, 'chat.setMode').length;
        chooseHost('codex');
        const edit = () => choicesOf('Access mode', 'mode').find((each) => each.value === 'auto_edit')!;
        await waitFor(() => expect(edit().disabled).toBe(true));
        await flush(() => new Promise((resolve) => setTimeout(resolve, 100)));
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
        // The chat keeps Auto-edit; its next turn, on Codex, runs in Ask first, which is drawn, with the desktop's sentence.
        expect(value()).toBe('ask_first');
        expect(editing()).toBeNull();
        expect(screen.getByRole('note', { name: 'Why the next turn runs in Ask first' }).textContent).toBe(
            codexAutoEdit,
        );
        expect([edit().text, edit().said]).toEqual(['Auto-edit', codexAutoEdit]);
        // Chosen all the same, it asks nothing of the desktop: the menu's item says why and does nothing.
        choose('auto_edit');
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
        expect(value()).toBe('ask_first');
        // Back on Claude Code the next turn runs in the Auto-edit the chat kept.
        chooseHost('claude');
        await waitFor(() => expect(editing()).not.toBeNull());
        expect(value()).toBe('auto_edit');
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
    });

    it('is offered by a desktop that lists it, and disabled by one that does not, saying so', async () => {
        const status = structuredClone(setupFound);
        for (const host of status.hosts) delete (host as Partial<typeof host>).autoEdit;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        const edit = choicesOf('Access mode', 'mode').find((each) => each.value === 'auto_edit')!;
        expect(edit.disabled).toBe(true);
        expect([edit.text, edit.said]).toEqual([
            'Auto-edit',
            'This version of Jason Desktop does not offer Auto-edit.',
        ]);
        expect(choicesOf('Access mode', 'mode').find((each) => each.value === 'full_access')!.disabled).toBe(false);
    });
});

describe('a change of mode around a running turn, narrow to wide', () => {
    async function running(desktop: StubDesktop): Promise<void> {
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'Tidy the notes' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'And then the next' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
        await waitFor(() => expect(screen.getByRole('group', { name: 'Queued message' })).toBeTruthy());
    }

    const givenBack = async (desktop: StubDesktop): Promise<void> => {
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }).value).toBe(
                'And then the next',
            ),
        );
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
    };

    it('leaving Auto-edit for Ask first gives the waiting message back, as Stop does', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        await running(desktop);
        choose('ask_first');
        await waitFor(() => expect(editing()).toBeNull());
        await givenBack(desktop);
    });

    it('a change from Full access to Auto-edit is narrower, and gives the waiting message back', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        await running(desktop);
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        await givenBack(desktop);
    });

    it('on Codex, where Auto-edit runs in Ask first, choosing the checked Ask first asks the desktop for it and stops nothing: the waiting message follows the turn', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        chooseHost('codex');
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        await running(desktop);
        const asked = calls(desktop, 'chat.setMode').length;
        choose('ask_first');
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(asked + 1));
        expect(calls(desktop, 'chat.setMode').at(-1)!.args.slice(1)).toEqual(['ask_first', 'codex']);
        expect(screen.getByRole('group', { name: 'Queued message' })).toBeTruthy();
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
    });

    it('a change from Auto-edit to Full access is wider: the turn goes on and its waiting message follows it', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        await running(desktop);
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(screen.getByRole('group', { name: 'Queued message' })).toBeTruthy();
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
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
        fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
        await waitFor(() => expect(screen.getByRole('group', { name: 'Queued message' })).toBeTruthy());
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        flush(() => desktop.emit(chatId(), { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }).value).toBe(
                'And then the next',
            ),
        );
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
    });
});

describe('Full access, with no standing choice to offer or take back', () => {
    it('offers the four modes and nothing after them, even from a desktop that still says Always stands', async () => {
        const desktop = chatDesktop();
        // A desktop of an earlier version kept Always on this computer and says so when asked: nothing of it is offered.
        const always = { givenAt: '2026-11-11T09:00:00.000Z', newChats: false };
        const asked: string[] = [];
        Object.assign(desktop, {
            fullAccess: {
                standing: () => (asked.push('standing'), Promise.resolve(always)),
                takeBack: () => (asked.push('takeBack'), Promise.resolve(null)),
                onChanged: () => (asked.push('onChanged'), () => undefined),
            },
        });
        renderConsole(desktop);
        await waitFor(() => menu());
        await flush(() => new Promise((resolve) => setTimeout(resolve, 100)));
        expect(options()).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['auto_edit', 'Auto-edit', false],
            ['full_access', 'Full access', false],
        ]);
        expect(screen.queryByText('Full access: ask each time again')).toBeNull();
        expect(asked).toEqual([]);
    });
});

describe('Plan only’s plan card', () => {
    const hostile =
        '**Bold** <script>window.hit=1</script> <a href="https://example.test/x">here</a> [link](https://example.test/y)';
    const card = () => document.querySelector<HTMLElement>('.chat-plan');
    const approve = () => screen.queryByRole('button', { name: 'Approve and run' });
    const go = 'Go ahead with the plan in your last answer.';

    /** Puts the chat in Plan only, sends a turn, and ends it as the desktop ends a Plan only turn with a plan. */
    const codexWords =
        'On Codex the approved plan still only reads: Codex runs Ask first read-only. To have it carried out, choose Full access or Claude Code for this chat.';
    const otherHost =
        'This plan came from another AI; the one this chat is on now never saw it. Ask for the plan again to approve it.';

    async function planned(desktop: StubDesktop, words = hostile, mark = true, host?: string): Promise<string> {
        await waitFor(() => menu());
        if (host !== undefined) chooseHost(host);
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Plan it' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        const id = calls(desktop, 'chat.send')[0]!.args[0] as string;
        flush(() => {
            desktop.emit(id, { kind: 'text', text: words });
            desktop.emit(id, mark ? { kind: 'done', error: null, plan: true } : { kind: 'done', error: null });
        });
        return id;
    }

    it('shows the plan as text, never as markup, with the line for Claude Code and its two buttons', async () => {
        const desktop = renderConsole();
        await planned(desktop);
        await waitFor(() => expect(card()).not.toBeNull());
        const text = card()!.querySelector('.plan-text')!;
        expect(text.textContent).toBe(hostile);
        expect(card()!.querySelector('strong, script, a, em, code')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
        expect(card()!.textContent).toContain(
            'Approve and run leaves Plan only for Ask first and tells the AI to go ahead; whatever Ask first asks about still comes to you as a card.',
        );
        expect(approve()).not.toBeNull();
        expect(screen.getByRole('button', { name: 'Keep planning' })).toBeTruthy();
        // The plan is the card, not a second bubble beside it.
        expect(document.querySelectorAll('.m.ai')).toHaveLength(0);
    });

    // A plan the AI wrote as a card is drawn inside the plan's own card, both of its parts, so the person approves only
    // what is on screen; a block the window does not draw as a card stays in the plan's words, as written.
    const block =
        '```card\n{"kind":"plan","title":"Spring","channels":["email"],"steps":[{"channel":"email","subject":"Hello","body":"Hi there,\\nA short note."}],"question":"Shall I keep it short?","options":[{"label":"Yes, short","detail":"Two lines"},{"label":"Longer"}]}\n```';
    const proposal = () => within(card()!).queryByRole('group', { name: 'Jason’s proposal' });
    const routes = (fake: FakeApi) => fake.answer('route.list', 'route.list/card-accounts');

    it('draws a plan card inside the plan’s card, both parts and its question as words, with Approve and run under all of it', async () => {
        const desktop = renderConsole(chatDesktop(), routes);
        await planned(desktop, `Here is the plan.\n${block}`);
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(card()!.querySelector('.plan-text')!.textContent).toBe('Here is the plan.');
        const drawn = proposal()!;
        expect(within(drawn).getByText('This card creates and sends nothing.')).toBeTruthy();
        expect(within(drawn).getByText('Spring')).toBeTruthy();
        expect(within(drawn).getByText('Hello')).toBeTruthy();
        await waitFor(() => within(drawn).getByText('No agent is created yet.'));
        // Its question is the AI's words, shown whole, and nothing on it is a button to press: the plan's card decides.
        const asked = within(drawn).getByRole('group', { name: 'Jason asks' });
        expect(within(asked).getByText('Shall I keep it short?')).toBeTruthy();
        expect(within(asked).getByText('Yes, short')).toBeTruthy();
        expect(within(asked).getByText('Two lines')).toBeTruthy();
        expect(within(asked).getByText('Longer')).toBeTruthy();
        expect(within(asked).queryAllByRole('button')).toEqual([]);
        expect(document.querySelector('.ui-opt')).toBeNull();
        // Approve and run comes after everything of the answer, the draft one press away.
        expect(drawn.compareDocumentPosition(approve()!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(within(drawn).getByRole('button', { name: 'Show the draft' })).toBeTruthy();
        expect(card()!.textContent).not.toContain('"kind"');
        expect(screen.queryByText('Jason asked for a card this app does not draw.')).toBeNull();
    });

    it('draws a plan that is only a plan card as that card, with Approve and run under it', async () => {
        const desktop = renderConsole(chatDesktop(), routes);
        await planned(desktop, block);
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(card()!.querySelector('.plan-text')).toBeNull();
        expect(approve()).not.toBeNull();
    });

    it('opening a draft in the plan’s card approves nothing and sends nothing', async () => {
        const desktop = renderConsole(chatDesktop(), routes);
        await planned(desktop, block);
        await waitFor(() => expect(proposal()).not.toBeNull());
        const before = calls(desktop, 'chat.setMode').length;
        fireEvent.click(within(proposal()!).getByRole('button', { name: 'Show the draft' }));
        await waitFor(() => within(proposal()!).getByText('Jason’s draft'));
        expect(proposal()!.querySelector('.proposal-body')!.textContent).toBe('Hi there,\nA short note.');
        expect(calls(desktop, 'chat.setMode')).toHaveLength(before);
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
        expect(approve()).not.toBeNull();
    });

    it.each([
        ['a plan card that does not read', '```card\n{"kind":"plan","channels":["email"]}\n```'],
        ['a choice', '```card\n{"kind":"choice","question":"Which?","options":[{"label":"One"},{"label":"Two"}]}\n```'],
        ['a data card', '```card\n{"kind":"needs"}\n```'],
    ])(
        'keeps %s in the plan’s words as written, never an empty frame, and offers Approve and run over it',
        async (_, other) => {
            const desktop = renderConsole();
            await planned(desktop, `Here is the plan.\n${other}`);
            await waitFor(() => expect(card()).not.toBeNull());
            expect(card()!.querySelector('.plan-text')!.textContent).toBe(`Here is the plan.\n${other}`);
            expect(within(card()!).queryByText(/its drafts included/)).toBeNull();
            expect(proposal()).toBeNull();
            expect(within(card()!).queryByRole('group', { name: 'Jason asks' })).toBeNull();
            expect(approve()).not.toBeNull();
            expect(screen.queryByText(/^Jason(’s card| asked for a card)/)).toBeNull();
        },
    );

    it('keeps an earlier block of the plan in its words when its last block is the plan card drawn', async () => {
        const desktop = renderConsole(chatDesktop(), routes);
        const earlier = '```card\n{"kind":"needs"}\n```';
        await planned(desktop, `First this.\n${earlier}\nThen the plan.\n${block}`);
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(card()!.querySelector('.plan-text')!.textContent).toBe(`First this.\n${earlier}\nThen the plan.`);
    });

    it('reads what is on record once across the turn’s end that marks the words its plan, and again only on Refresh', async () => {
        let fake!: FakeApi;
        const desktop = renderConsole(chatDesktop(), (given) => routes((fake = given)));
        await waitFor(() => menu());
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Plan it' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        const id = calls(desktop, 'chat.send')[0]!.args[0] as string;
        const routeReads = () => fake.calls.filter((each) => each.operation === 'route.list');
        // The words arrive while the turn runs, and their plan card reads; the turn's end marks them the plan.
        flush(() => desktop.emit(id, { kind: 'text', text: block }));
        await waitFor(() => screen.getByText('No agent is created yet.'));
        flush(() => desktop.emit(id, { kind: 'done', error: null, plan: true }));
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(within(proposal()!).getByText('No agent is created yet.')).toBeTruthy();
        expect(routeReads()).toHaveLength(1);
        fireEvent.click(within(proposal()!).getByRole('button', { name: 'Refresh' }));
        await waitFor(() => expect(routeReads()).toHaveLength(2));
    });

    /**
     * A Plan only turn whose words arrive while it runs and are read there, then end marked the plan: what a person sees
     * on a real turn. Returns the chat's id and the reads of the routes the card made.
     */
    async function readWhileRunning(): Promise<{ desktop: StubDesktop; id: string; reads: () => number }> {
        let fake!: FakeApi;
        const desktop = renderConsole(chatDesktop(), (given) => routes((fake = given)));
        await waitFor(() => menu());
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Plan it' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        const id = calls(desktop, 'chat.send')[0]!.args[0] as string;
        flush(() => desktop.emit(id, { kind: 'text', text: block }));
        await waitFor(() => screen.getByText('No agent is created yet.'));
        return { desktop, id, reads: () => fake.calls.filter((each) => each.operation === 'route.list').length };
    }
    const drawn = () => screen.queryByRole('group', { name: 'Jason’s proposal' });
    /** Lets what the page queued once it drew again run: a read a card asked for starts there. */
    const settled = () => flush(() => new Promise((done) => setTimeout(done, 20)));
    const asOf = () => drawn()!.querySelector('.card-asof')!.textContent;

    it('keeps its one read when the chat leaves Plan only after the turn’s end marked the plan', async () => {
        const { desktop, id, reads } = await readWhileRunning();
        flush(() => desktop.emit(id, { kind: 'done', error: null, plan: true }));
        await waitFor(() => expect(proposal()).not.toBeNull());
        const read = asOf();
        choose('ask_first');
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        await waitFor(() => expect(card()).toBeNull());
        expect(drawn()).not.toBeNull();
        expect(within(drawn()!).getByText('No agent is created yet.')).toBeTruthy();
        await settled();
        expect(asOf()).toBe(read);
        expect(reads()).toBe(1);
    });

    it('keeps its one read when the plan is approved and the next turn ends', async () => {
        const { desktop, id, reads } = await readWhileRunning();
        flush(() => desktop.emit(id, { kind: 'done', error: null, plan: true }));
        await waitFor(() => expect(approve()).not.toBeNull());
        fireEvent.click(approve()!);
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
        flush(() => {
            desktop.emit(id, { kind: 'text', text: 'Done as planned.' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText('Done as planned.'));
        await settled();
        expect(drawn()).not.toBeNull();
        expect(within(drawn()!).getByText('No agent is created yet.')).toBeTruthy();
        expect(reads()).toBe(1);
    });

    it('keeps a draft opened while the turn ran open across its end and after the chat leaves Plan only', async () => {
        const { desktop, id } = await readWhileRunning();
        fireEvent.click(within(drawn()!).getByRole('button', { name: 'Show the draft' }));
        await waitFor(() => within(drawn()!).getByText('Jason’s draft'));
        flush(() => desktop.emit(id, { kind: 'done', error: null, plan: true }));
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(within(proposal()!).getByText('Jason’s draft')).toBeTruthy();
        choose('ask_first');
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        await waitFor(() => expect(card()).toBeNull());
        await settled();
        expect(within(drawn()!).getByText('Jason’s draft')).toBeTruthy();
    });

    it('reads afresh, its drafts closed, when the conversation is opened again from the desktop’s record', async () => {
        const { desktop, id, reads } = await readWhileRunning();
        fireEvent.click(within(drawn()!).getByRole('button', { name: 'Show the draft' }));
        await waitFor(() => within(drawn()!).getByText('Jason’s draft'));
        flush(() => desktop.emit(id, { kind: 'done', error: null, plan: true }));
        await waitFor(() => expect(proposal()).not.toBeNull());
        expect(reads()).toBe(1);
        // The desktop's record of the conversation, its plan marked as kept, read again by the page.
        desktop.kept.get(id)!.entries = structuredClone(chats.value[id]!.entries) as never;
        chats.value = {};
        await flush(() => openConversation(id));
        await waitFor(() => expect(reads()).toBe(2));
        await waitFor(() => expect(drawn()!.querySelector('.card-asof')).not.toBeNull());
        expect(within(drawn()!).queryByText('Jason’s draft')).toBeNull();
    });

    it('says over Approve and run that the plan is approved as written, its drafts included, open or not', async () => {
        const desktop = renderConsole(chatDesktop(), routes);
        await planned(desktop, block);
        await waitFor(() => expect(approve()).not.toBeNull());
        const said = within(card()!).getByText(
            'Approve and run approves this plan as written, its drafts included, open or not.',
        );
        expect(said.compareDocumentPosition(approve()!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(proposal()!.compareDocumentPosition(said) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('offers no Approve and run over a plan of no words', async () => {
        const desktop = renderConsole();
        await planned(desktop, '  \n ');
        await waitFor(() => expect(card()).not.toBeNull());
        expect(approve()).toBeNull();
        expect(screen.queryByRole('button', { name: 'Keep planning' })).toBeNull();
    });

    it('shows no card for a turn’s end the desktop did not mark', async () => {
        const desktop = renderConsole();
        await planned(desktop, 'Just an answer', false);
        await waitFor(() => screen.getByText('Just an answer'));
        expect(card()).toBeNull();
    });

    it('shows no card once the chat left Plan only, nor when it comes back to it', async () => {
        const again = renderConsole();
        const id = await planned(again);
        await waitFor(() => expect(card()).not.toBeNull());
        flush(() => again.emit(id, { kind: 'mode', mode: 'ask_first' }));
        expect(card()).toBeNull();
        flush(() => again.emit(id, { kind: 'mode', mode: 'plan_only' }));
        expect(card()).toBeNull();
    });

    it('Approve and run asks the desktop for Ask first through the menu’s call, then sends the go naming Ask first, once however often it is pressed', async () => {
        const desktop = renderConsole();
        const id = await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        const before = calls(desktop, 'chat.setMode').length;
        // Two presses before the page has drawn again, then one on the card it drew.
        const button = approve()!;
        fireEvent.click(button);
        fireEvent.click(button);
        const pressed = screen.queryByRole('button', { name: 'Approving…' });
        if (pressed) fireEvent.click(pressed);
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
        const modes = calls(desktop, 'chat.setMode').slice(before);
        expect(modes.map((each) => each.args)).toEqual([[id, 'ask_first', 'claude']]);
        const sent = calls(desktop, 'chat.send')[1]!;
        expect(sent.args).toEqual([id, go, { host: 'claude', mode: 'ask_first' }]);
        // The order: the mode first, the go after its answer.
        const order = desktop.calls
            .filter((each) => each.name === 'chat.setMode' || each.name === 'chat.send')
            .slice(-2);
        expect(order.map((each) => each.name)).toEqual(['chat.setMode', 'chat.send']);
        expect(value()).toBe('ask_first');
        expect(card()!.textContent).toContain('Approved: the chat is in Ask first.');
        expect(approve()).toBeNull();
        expect(screen.getByText(go)).toBeTruthy();
        expect(calls(desktop, 'chat.setMode').slice(before)).toHaveLength(1);
        // Once the go's turn ended, the approved plan is an answer of the past.
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        expect(card()).toBeNull();
    });

    it('sends nothing when the desktop answers another mode, and says so', async () => {
        const desktop = renderConsole();
        await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        const setMode = desktop.chat.setMode;
        // Another choice from the menu came first: the desktop answers the chat in Full access.
        desktop.chat.setMode = async (chat, _mode, host) => setMode(chat, 'full_access', host);
        fireEvent.click(approve()!);
        await waitFor(() =>
            screen.getByText("The chat's mode changed before the plan was approved, so nothing was sent."),
        );
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
        expect(value()).toBe('full_access');
        expect(approve()).toBeNull();
        desktop.chat.setMode = setMode;
    });

    it('sends nothing when the desktop refuses the change, and says its words', async () => {
        const refused = renderConsole();
        const other = await planned(refused);
        await waitFor(() => expect(approve()).not.toBeNull());
        refused.refuse.set('chat.setMode', 'That conversation is not one this desktop keeps.');
        fireEvent.click(approve()!);
        await waitFor(() => screen.getByText('That conversation is not one this desktop keeps.'));
        expect(calls(refused, 'chat.send')).toHaveLength(1);
        expect(chats.value[other]!.mode).toBe('plan_only');
        expect(approve()).toBeNull();
    });

    it('a go the desktop refuses because the mode moved is said, and no turn runs', async () => {
        const desktop = renderConsole();
        const id = await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        // The mode moves after the desktop answered Ask first and before the go reaches it.
        const setMode = desktop.chat.setMode;
        desktop.chat.setMode = async (...args) => {
            const answer = await setMode(...args);
            await setMode(id, 'full_access', 'claude');
            return { ...answer, mode: 'ask_first' } as typeof answer;
        };
        fireEvent.click(approve()!);
        await waitFor(() =>
            screen.getByText("The chat's mode changed before the plan was approved, so nothing was sent."),
        );
        expect(calls(desktop, 'chat.send')[1]!.args[2]).toEqual({ host: 'claude', mode: 'ask_first' });
        expect(chats.value[id]!.running).toBe(false);
    });

    it('on Codex says the approved plan still only reads, naming Full access and Claude Code, and sends the go to Codex', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        const id = await planned(desktop, hostile, true, 'codex');
        await waitFor(() => expect(card()).not.toBeNull());
        expect(card()!.textContent).toContain(codexWords);
        fireEvent.click(approve()!);
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
        expect(calls(desktop, 'chat.setMode').at(-1)!.args).toEqual([id, 'ask_first', 'codex']);
        expect(calls(desktop, 'chat.send')[1]!.args).toEqual([id, go, { host: 'codex', mode: 'ask_first' }]);
    });

    it('a plan another host made is no card to approve: it says to ask for the plan again, until the chat is back on that host', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        const before = calls(desktop, 'chat.setMode').length;
        chooseHost('codex');
        await waitFor(() => expect(card()!.textContent).toContain(otherHost));
        expect(approve()).toBeNull();
        expect(screen.queryByRole('button', { name: 'Keep planning' })).toBeNull();
        expect(card()!.textContent).not.toContain(codexWords);
        expect(card()!.querySelector('.plan-text')!.textContent).toBe(hostile);
        chooseHost('claude');
        await waitFor(() => expect(approve()).not.toBeNull());
        expect(card()!.textContent).not.toContain(otherHost);
        expect(calls(desktop, 'chat.setMode').length).toBe(before);
    });

    it('an /export after the plan is no turn: the card stays live, its answer is said beside it, and a refusal gives the words back', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        const id = await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
        // Sent from another host, the export leaves the plan the plan of the host that made it.
        chooseHost('codex');
        fireEvent.change(box(), { target: { value: '/export plan.md' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(2));
        expect(calls(desktop, 'chat.send')[1]!.args[1]).toBe('/export plan.md');
        flush(() => {
            desktop.emit(id, { kind: 'notice', text: 'Written: /h/plan.md' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => expect(screen.getByText('Written: /h/plan.md')).toBeTruthy());
        expect(screen.queryByText('/export plan.md')).toBeNull();
        chooseHost('claude');
        await waitFor(() => expect(approve()).not.toBeNull());
        // A refusal is said beside the plan, and the words go back to the box.
        fireEvent.change(box(), { target: { value: '/export plan.md' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(3));
        flush(() => desktop.emit(id, { kind: 'done', error: 'A file or folder of that name is already there.' }));
        await waitFor(() => expect(screen.getByText('A file or folder of that name is already there.')).toBeTruthy());
        expect(box().value).toBe('/export plan.md');
        expect(approve()).not.toBeNull();
    });

    it('Keep planning puts the card aside, keeps Plan only, asks the desktop nothing and gives the box the focus', async () => {
        const desktop = renderConsole();
        await planned(desktop);
        await waitFor(() => expect(approve()).not.toBeNull());
        const before = desktop.calls.length;
        fireEvent.click(screen.getByRole('button', { name: 'Keep planning' }));
        expect(approve()).toBeNull();
        expect(card()!.querySelector('.plan-text')!.textContent).toBe(hostile);
        expect(value()).toBe('plan_only');
        const asked = desktop.calls
            .slice(before)
            .filter((each) => each.name === 'chat.setMode' || each.name === 'chat.send');
        expect(asked).toEqual([]);
        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Your message' }));
    });
});

describe('the empty chat’s sentence about approval', () => {
    // What is true of each mode: in Plan only nothing written reaches a person unapproved; in Ask first and Auto-edit a
    // message follows its campaign's send mode, and the AI approves on the person's word, as theirs.
    const planOnly = 'Nothing written here reaches a person without your approval.';
    const onYourWord = "A message written here follows its campaign's send mode; the AI approves when you tell it to.";
    // The sentence of the hero under the heading, or null when that hero draws none.
    const said = (heading = 'What should Jason work on?') => {
        const text = screen.getByRole('heading', { name: heading }).closest('.hero')!.querySelector('p')?.textContent;
        return text === planOnly || text === onYourWord ? text : null;
    };

    it('says in each of Ask first, Plan only and Auto-edit what is true of it, draws none in Full access, and follows a switch of mode both ways', async () => {
        renderConsole();
        await waitFor(() => menu());
        await waitFor(() => expect(line()).not.toBeNull());
        expect(said()).toBeNull();
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        expect(said()).toBe(onYourWord);
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        expect(said()).toBe(planOnly);
        choose('auto_edit');
        await waitForMenus(() => expect(value()).toBe('auto_edit'));
        expect(said()).toBe(onYourWord);
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(said()).toBeNull();
        expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        expect(said()).not.toBeNull();
    });

    it('is drawn on a host where a Full access chat’s next turn runs in Ask first, and not once back where it runs in Full access', async () => {
        const status = structuredClone(setupFound);
        const codex = status.hosts.find((each) => each.id === 'codex')!;
        codex.found = true;
        // A host on which Full access is not offered runs a Full access chat's turn in Ask first.
        const notYet = 'Full access is not in this version yet.';
        codex.fullAccess = notYet;
        codex.runs.window.full_access = { mode: 'ask_first', why: notYet };
        renderConsole(chatDesktop(status));
        await waitFor(() => expect(line()).not.toBeNull());
        expect(said()).toBeNull();
        chooseHost('codex');
        await waitFor(() => expect(line()).toBeNull());
        expect(said()).not.toBeNull();
        chooseHost('claude');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(said()).toBeNull();
    });

    it('is not drawn in a new chat, which starts in Full access, from its first moment', async () => {
        const desktop = chatDesktop();
        renderConsole(desktop);
        await waitFor(() => expect(line()).not.toBeNull());
        expect(value()).toBe('full_access');
        expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
        expect(said()).toBeNull();
    });

    it('is not drawn for a mode word this desktop does not know, which a newer desktop may write', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000C1',
            campaignId: 'cmp_3',
            title: 'Autumn',
            mode: 'some_newer_mode' as never,
        });
        renderConsole(desktop, (fake) =>
            fake
                .answerAnyRequest('approval.list', () => 'approval.list/empty')
                .answerAnyRequest('decision.list', () => 'decision.list/empty'),
        );
        const nav = () => document.querySelector<HTMLElement>('.side nav[aria-label="Conversations"]')!;
        await waitFor(() => within(nav()).getByRole('button', { name: 'Autumn' }));
        fireEvent.click(within(nav()).getByRole('button', { name: 'Autumn' }));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Autumn?' }));
        expect(said('What should Jason do in Autumn?')).toBeNull();
    });

    it('is drawn from the mode a reopened chat holds, never from an earlier drawing of it', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000A1',
            campaignId: 'cmp_1',
            title: 'Spring',
            mode: 'ask_first',
        });
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000B1',
            campaignId: 'cmp_2',
            title: 'Summer',
            mode: 'full_access',
        });
        // A chat of an agent reads what waits on a person in that agent's campaign; nothing does here.
        renderConsole(desktop, (fake) =>
            fake
                .answerAnyRequest('approval.list', () => 'approval.list/empty')
                .answerAnyRequest('decision.list', () => 'decision.list/empty'),
        );
        const nav = () => document.querySelector<HTMLElement>('.side nav[aria-label="Conversations"]')!;
        const open = (name: string) => fireEvent.click(within(nav()).getByRole('button', { name }));
        await waitFor(() => within(nav()).getByRole('button', { name: 'Spring' }));
        open('Spring');
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        expect(said('What should Jason do in Spring?')).not.toBeNull();
        open('Summer');
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Summer?' }));
        expect(said('What should Jason do in Summer?')).toBeNull();
        // Spring's mode moves while it is out of sight; it is drawn from the new mode the moment it is back.
        flush(() => desktop.emit('conv_01JDKEPT0000000000000000A1', { kind: 'mode', mode: 'full_access' }));
        // Every frame drawn from here on is watched: none may carry the sentence under Spring's heading.
        let stale = false;
        const watching = new MutationObserver(() => {
            const heading = screen.queryByRole('heading', { name: 'What should Jason do in Spring?' });
            const text = heading?.closest('.hero')?.querySelector('p')?.textContent;
            if (text === planOnly || text === onYourWord) stale = true;
        });
        watching.observe(document.body, { childList: true, subtree: true, characterData: true });
        open('Spring');
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        watching.disconnect();
        expect(stale).toBe(false);
        expect(said('What should Jason do in Spring?')).toBeNull();
        flush(() => desktop.emit('conv_01JDKEPT0000000000000000A1', { kind: 'mode', mode: 'plan_only' }));
        expect(said('What should Jason do in Spring?')).toBe(planOnly);
    });
});
