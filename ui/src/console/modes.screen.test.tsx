import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { cardReads } from './card-frame';
import { proposals } from './card-plan';
import { openConversation } from './chat';
import { Console } from './console';
import { chats, conversations } from './state';
import { choicesOf, chooseIn, closeMenu, currentOf, menuButton, waitForMenus } from '../test/menus';

// A chat's access mode: the menu beside the composer, Full access disabled where the desktop says it cannot be chosen,
// and the line under the composer, drawn from the mode the desktop holds and from nothing the page asked for.

function renderConsole(desktop: StubDesktop = chatDesktop(), more?: (fake: FakeApi) => void): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    more?.(fake);
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
    it('starts in Ask first, and offers Plan only, Auto-edit and Full access beside the composer, narrow to wide', async () => {
        renderConsole();
        await waitFor(() => menu());
        expect(value()).toBe('ask_first');
        expect(options()).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['auto_edit', 'Auto-edit', false],
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
        await waitForMenus(() => expect(value()).toBe('ask_first'));
        expect(line()).toBeNull();

        desktop.confirmFullAccess = true;
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        // In the console's form: the mode's name, a dot and what it does, with no link; the menu beside it changes it.
        expect(line()!.textContent).toBe(
            'Full access · the AI runs commands without asking, except those Jason cannot read',
        );
        expect(within(line()!.parentElement!).queryByRole('button')).toBeNull();
        expect(value()).toBe('full_access');
    });

    it('takes the line away when the desktop puts the chat back to Ask first, whoever asked', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'ask_first' }));
        expect(line()).toBeNull();
        expect(value()).toBe('ask_first');
        // And draws it only from the desktop's word: an event alone puts the chat in Full access.
        flush(() => desktop.emit(chatId(), { kind: 'mode', mode: 'full_access' }));
        expect(line()).not.toBeNull();
    });

    it('shows Full access disabled with the desktop’s reason on a host that has none, and choosing that host leaves Full access', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        chooseHost('codex');
        await waitFor(() => expect(line()).toBeNull());
        expect(calls(desktop, 'chat.setMode').at(-1)!.args[1]).toBe('ask_first');
        const full = choicesOf('Access mode', 'mode').find((each) => each.value === 'full_access')!;
        expect(full.disabled).toBe(true);
        expect([full.text, full.said]).toEqual(['Full access', 'Full access runs on Claude Code only.']);
    });

    it('says under the composer, on Codex alone, that the chat does not read the person’s Codex config.toml', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        const note = () => screen.queryByRole('note', { name: 'What Codex reads' });
        expect(note()).toBeNull();
        chooseHost('codex');
        await waitFor(() => expect(note()).not.toBeNull());
        expect(note()!.textContent).toBe(
            'The chat does not read your Codex config.toml, so a provider or profile set there is not used; connect a key on the Connect card.',
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
            expect(note()).toBeNull();
            for (const id of ['codex', 'codex-api-key']) {
                chooseHost(id);
                await waitFor(() => expect(note()).not.toBeNull());
                expect(note()!.textContent).toBe(line);
                expect(note()!.textContent).not.toMatch(/act for you|acts through nothing|network/);
            }
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
        expect(value()).toBe('ask_first');
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

    it('says in the desktop’s words why Full access cannot be had on the host chosen, asks nothing, and shows no line', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        chooseHost('codex');
        const full = choicesOf('Access mode', 'mode').find((each) => each.value === 'full_access')!;
        expect([full.disabled, full.said]).toEqual([true, 'Full access runs on Claude Code only.']);
        choose('full_access');
        expect(calls(desktop, 'chat.setMode')).toHaveLength(0);
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

const editing = () => screen.queryByRole('status', { name: 'Auto-edit is on' });
const codexAutoEdit =
    'Auto-edit runs on Claude Code only: Codex cannot ask before it acts, so on Codex the chat only reads and proposes.';

describe('Auto-edit', () => {
    it('enters with no box, draws its own line under the composer, and takes it away on leaving', async () => {
        const desktop = chatDesktop();
        // A box would say no: Auto-edit asks nothing.
        desktop.confirmFullAccess = false;
        renderConsole(desktop);
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

    it('is disabled with the desktop’s reason on Codex, and choosing Codex takes the chat back to Ask first', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        chooseHost('codex');
        await waitFor(() => expect(editing()).toBeNull());
        expect(calls(desktop, 'chat.setMode').at(-1)!.args[1]).toBe('ask_first');
        const edit = choicesOf('Access mode', 'mode').find((each) => each.value === 'auto_edit')!;
        expect(edit.disabled).toBe(true);
        expect([edit.text, edit.said]).toEqual(['Auto-edit', codexAutoEdit]);
        // Chosen all the same, it asks nothing of the desktop: the menu's item says why and does nothing.
        const asked = calls(desktop, 'chat.setMode').length;
        choose('auto_edit');
        expect(calls(desktop, 'chat.setMode')).toHaveLength(asked);
        expect(value()).toBe('ask_first');
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
        fireEvent.click(screen.getByRole('button', { name: 'Send when the turn ends' }));
        await waitFor(() => expect(screen.getByRole('status', { name: 'Waiting message' })).toBeTruthy());
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

    it('a change from Auto-edit to Full access is wider: the turn goes on and its waiting message follows it', async () => {
        const desktop = renderConsole();
        await waitFor(() => menu());
        choose('auto_edit');
        await waitFor(() => expect(editing()).not.toBeNull());
        await running(desktop);
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(screen.getByRole('status', { name: 'Waiting message' })).toBeTruthy();
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
        fireEvent.click(screen.getByRole('button', { name: 'Send when the turn ends' }));
        await waitFor(() => expect(screen.getByRole('status', { name: 'Waiting message' })).toBeTruthy());
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

describe('Full access answered once for good', () => {
    const always = { givenAt: '2026-11-11T09:00:00.000Z', newChats: false };

    it('while Always stands the menu enters Full access without the box and offers the take-back after the modes', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        // A box would say no: the menu enters all the same, since the desktop asks nothing while the choice stands.
        desktop.confirmFullAccess = false;
        renderConsole(desktop);
        await waitForMenus(() => expect(options()).toHaveLength(5));
        expect(options()).toEqual([
            ['ask_first', 'Ask first', false],
            ['plan_only', 'Plan only', false],
            ['auto_edit', 'Auto-edit', false],
            ['full_access', 'Full access', false],
            ['take_back', 'Full access: ask each time again', false],
        ]);
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(value()).toBe('full_access');
    });

    it('the take-back in the menu takes the choice back and leaves the chat in the mode it is in, its line still shown', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        renderConsole(desktop);
        await waitForMenus(() => expect(options()).toHaveLength(5));
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        choose('take_back');
        await waitFor(() => expect(calls(desktop, 'fullAccess.takeBack')).toHaveLength(1));
        expect(calls(desktop, 'fullAccess.takeBack')[0]!.args).toEqual([]);
        await waitForMenus(() => expect(options()).toHaveLength(4));
        expect(value()).toBe('full_access');
        expect(line()).not.toBeNull();
        expect(calls(desktop, 'chat.setMode')).toHaveLength(1);
    });

    it('reads the choice again as the menu opens: one the desktop never said is offered its take-back', async () => {
        const desktop = chatDesktop();
        renderConsole(desktop);
        await waitForMenus(() => expect(options()).toHaveLength(4));
        // The file changed behind the desktop's back: no change is said to the page.
        desktop.standing = always;
        // Opening the menu reads it again.
        fireEvent.click(menu());
        closeMenu('Access mode');
        await waitForMenus(() => expect(options()).toHaveLength(5));
        expect(options()[4]![0]).toBe('take_back');
    });

    it('follows a take-back made elsewhere: the option goes', async () => {
        const desktop = chatDesktop();
        desktop.standing = always;
        renderConsole(desktop);
        await waitForMenus(() => expect(options()).toHaveLength(5));
        flush(() => desktop.stand(null));
        expect(options()).toHaveLength(4);
    });

    it('a new chat that starts in Full access by the standing choice shows the line from its first moment, before anything is sent', async () => {
        const desktop = chatDesktop();
        desktop.standing = { ...always, newChats: true };
        renderConsole(desktop);
        await waitFor(() => expect(line()).not.toBeNull());
        expect(value()).toBe('full_access');
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
        expect(value()).toBe('ask_first');
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
        expect(value()).toBe('ask_first');
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
        'On Codex the approved plan still only reads: every Codex turn here is read-only. To have it carried out, choose Claude Code for this chat.';
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

    it('on Codex says the approved plan still only reads, naming Claude Code, and sends the go to Codex', async () => {
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
    const sentence = 'Nothing written here reaches a person without your approval.';
    // The sentence of the hero under the heading, or null when that hero draws none.
    const said = (heading = 'What should Jason work on?') => {
        const text = screen.getByRole('heading', { name: heading }).closest('.hero')!.querySelector('p')?.textContent;
        return text === sentence ? text : null;
    };

    it('is drawn in Ask first, Plan only and Auto-edit, and not in Full access, and follows a switch of mode both ways', async () => {
        renderConsole();
        await waitFor(() => menu());
        expect(said()).not.toBeNull();
        choose('plan_only');
        await waitForMenus(() => expect(value()).toBe('plan_only'));
        expect(said()).not.toBeNull();
        choose('auto_edit');
        await waitForMenus(() => expect(value()).toBe('auto_edit'));
        expect(said()).not.toBeNull();
        choose('full_access');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(said()).toBeNull();
        expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
        choose('ask_first');
        await waitFor(() => expect(line()).toBeNull());
        expect(said()).not.toBeNull();
    });

    it('is not drawn in a new chat that starts in Full access by the standing choice, from its first moment', async () => {
        const desktop = chatDesktop();
        desktop.standing = { givenAt: '2026-11-11T09:00:00.000Z', newChats: true };
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
            if (heading?.closest('.hero')?.querySelector('p')?.textContent === sentence) stale = true;
        });
        watching.observe(document.body, { childList: true, subtree: true, characterData: true });
        open('Spring');
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        watching.disconnect();
        expect(stale).toBe(false);
        expect(said('What should Jason do in Spring?')).toBeNull();
        flush(() => desktop.emit('conv_01JDKEPT0000000000000000A1', { kind: 'mode', mode: 'plan_only' }));
        expect(said('What should Jason do in Spring?')).not.toBeNull();
    });
});
