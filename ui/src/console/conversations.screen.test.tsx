import type { DesktopFullAccessWhy } from '../platform/desktop';
import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiProvider } from '../api/context';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { setupFound, chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { inFront, shownPane, bringForward } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { layOutChats } from '../test/scroll';

/** Lets what a change set going run out: the effects, the observers and the timers already due. */
const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 20)));
import * as consoleCampaigns from './campaigns';
import { ChatPanel } from './chat';
import { Console } from './console';
import { chats, conversations } from './state';
import { choicesOf, currentOf, menuButton, waitForMenus } from '../test/menus';

// The conversations the desktop keeps: listed in the sidebar, reopened with what the page drew, a set-up chat handed
// over to the campaign its turn made or to the one the person picks, a fresh session said in one line, and a
// conversation started fresh or removed.

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(desktop: StubDesktop = chatDesktop()): { desktop: StubDesktop; fake: FakeApi } {
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
    return { desktop, fake };
}

const side = () => document.querySelector<HTMLElement>('.side')!;
const listed = () => within(side()).getByRole('navigation', { name: 'Conversations' });
/** What the sidebar's list says, in its order. */
const names = () =>
    within(listed())
        .getAllByRole('button')
        .map((each) => each.querySelector('.rt')?.textContent?.trim());
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
/** The thread of the chat the area shows. */
const thread = () => shownPane()!.querySelector<HTMLElement>('.thread')!;

/** Types into a chat, the only one shown unless `panel` names it, and sends; resolves with the conversation it went to. */
async function send(desktop: StubDesktop, text: string, panel?: HTMLElement): Promise<string> {
    const before = calls(desktop, 'chat.send').length;
    const scope = panel ? within(panel) : screen;
    await waitFor(() => scope.getByRole('textbox', { name: 'Your message' }));
    fireEvent.change(scope.getByRole('textbox', { name: 'Your message' }), { target: { value: text } });
    fireEvent.click(scope.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(before + 1));
    return calls(desktop, 'chat.send')[before]!.args[0] as string;
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});
afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('the desktop’s conversations', () => {
    it('are listed in the sidebar newest first, and one reopens with what the page drew, drawn as a live turn is', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000A1',
            campaignId: 'cmp_1',
            title: 'Spring',
            dropped: 2,
            entries: [
                { kind: 'user', text: `How is ${hostile} doing?` },
                { kind: 'step', text: 'Ran a jason command', detail: 'jason campaign get cmp_1' },
                { kind: 'permission', title: 'Run a command', detail: 'git status', answer: 'deny' },
                { kind: 'permission', title: 'Read a file', detail: '/x', answer: null },
                { kind: 'text', text: `**Well.** ${hostile}` },
                { kind: 'error', text: 'Claude Code stopped.' },
            ],
        });
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000A2', title: hostile });
        renderConsole(desktop);
        // The two kept ones, newest first; the set-up chat the window opens on is not listed until its first turn.
        await waitFor(() => expect(names()).toEqual([hostile, 'Spring']));
        expect(Object.keys(chats.value)).toHaveLength(1);
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => screen.getByText('Claude Code stopped.'));
        expect(calls(desktop, 'chat.open').map((each) => each.args)).toEqual([['conv_01JDKEPT0000000000000000A1']]);
        const shown = thread();
        expect(within(shown).getByText(`How is ${hostile} doing?`)).toBeTruthy();
        expect(within(shown).getByText('1 action')).toBeTruthy();
        expect(within(shown).getByText('Not allowed')).toBeTruthy();
        expect(within(shown).getByText('This request has ended')).toBeTruthy();
        expect(within(shown).queryByRole('button', { name: 'Allow' })).toBeNull();
        expect(shown.querySelector('.chat-md strong')?.textContent).toBe('Well.');
        expect(
            within(shown).getByText('Earlier messages of this conversation were let go to keep it within its size.'),
        ).toBeTruthy();
        // The only images of the page are the brand's own: the sidebar's and the start screen's mark.
        expect(
            Array.from(document.querySelectorAll('img')).filter(
                (img) => !img.closest('.brand') && !img.classList.contains('start-mark'),
            ),
        ).toEqual([]);
        expect((window as { hit?: number }).hit).toBeUndefined();
        // Shown again, it is brought forward, not read again.
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        expect(calls(desktop, 'chat.open')).toHaveLength(1);
    });

    it('reopens a kept Full access card with its why, and one this page has no words for without the line', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000F1',
            campaignId: 'cmp_1',
            title: 'Spring',
            entries: [
                {
                    kind: 'permission',
                    title: 'Full access asks about this one',
                    detail: 'ls *.md',
                    answer: 'deny',
                    why: { reason: 'command.glob' },
                },
                {
                    kind: 'permission',
                    title: 'Full access asks about this one',
                    detail: 'ls ?.md',
                    answer: 'allow',
                    why: { reason: 'command.not_known_here' } as unknown as DesktopFullAccessWhy,
                },
            ],
        });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => expect(thread().querySelectorAll('.ask.perm')).toHaveLength(2));
        const [known, unknown] = [...thread().querySelectorAll<HTMLElement>('.ask.perm')];
        expect(known!.querySelector('.perm-why')?.textContent).toBe(
            'This command has a wildcard (*, ? or [ ]), which the shell turns into file names only as it runs, so Jason cannot read plainly what it would touch.',
        );
        expect(unknown!.querySelector('.perm-why')).toBeNull();
        expect(unknown!.querySelector('.ask-h + .perm-detail')?.textContent).toBe('ls ?.md');
        expect(within(unknown!).getByText('Allowed')).toBeTruthy();
    });

    it('reopens on the host, model and effort its last turn was sent with, and the next turn names them again', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000C1',
            campaignId: 'cmp_1',
            title: 'Spring',
            chosen: { host: 'claude', model: 'sonnet', effort: 'low' },
        });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        const hero = await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const panel = hero.closest<HTMLElement>('.chat-panel')!;
        await waitForMenus(() => expect(currentOf('Model', 'model', panel)).toBe('sonnet'));
        expect(currentOf('Model', 'effort', panel)).toBe('low');
        expect(menuButton('Model', panel).getAttribute('aria-label')).toBe('Model: Sonnet, Low effort');
        const id = await send(desktop, 'And now?', panel);
        expect(calls(desktop, 'chat.send').at(-1)!.args).toEqual([
            id,
            'And now?',
            { host: 'claude', model: 'sonnet', effort: 'low' },
        ]);
        expect(desktop.kept.get(id)?.chosen).toEqual({ host: 'claude', model: 'sonnet', effort: 'low' });
    });

    it('reopened on a Claude Code whose version cannot be read, runs no turn on Codex and says why, with Check again', async () => {
        const unreadable =
            'The version of Claude Code could not be read, so the chat does not run on it. Run `claude --version` in a terminal to see it; the chat needs 2.1.257 or newer, and `claude update` updates it. Then check again.';
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, version: null, belowFloor: unreadable }
                    : each.id === 'codex'
                      ? { ...each, found: true, version: '0.130.0', install: null }
                      : each,
            ),
        });
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000H1',
            campaignId: 'cmp_1',
            title: 'Spring',
            chosen: { host: 'claude', model: 'sonnet', effort: 'low' },
        });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        const hero = await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const panel = hero.closest<HTMLElement>('.chat-panel')!;
        const note = await waitFor(() => {
            const element = panel.querySelector<HTMLElement>('.composer .host-held');
            expect(element).toBeTruthy();
            return element!;
        });
        expect(note.textContent).toContain('The version of Claude Code could not be read');
        expect(within(note).getByRole('button', { name: 'Check again' })).toBeTruthy();
        expect(currentOf('Model', 'host', panel)).toBe('claude');
        expect(within(panel).getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);
        fireEvent.change(within(panel).getByRole('textbox', { name: 'Your message' }), {
            target: { value: 'And now?' },
        });
        fireEvent.click(within(panel).getByRole('button', { name: 'Send' }));
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });

    it('reopens on a model its host no longer lists, says so in the control, and sends the turn with the host’s own and no effort', async () => {
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, found: false }
                    : each.id === 'codex'
                      ? { ...each, found: true, version: '0.80.0', install: null }
                      : each,
            ),
        });
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000D1',
            campaignId: 'cmp_1',
            title: 'Spring',
            chosen: { host: 'codex', model: 'gpt-5-old', effort: 'high' },
        });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        const hero = await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const panel = hero.closest<HTMLElement>('.chat-panel')!;
        await waitFor(() => menuButton('Model', panel));
        expect(choicesOf('Model', 'model', panel).find((each) => each.checked)).toEqual({
            value: 'gpt-5-old',
            text: 'gpt-5-old',
            said: "No longer listed, so the turn uses Codex's own model with no effort.",
            disabled: false,
            checked: true,
        });
        expect(menuButton('Model', panel).getAttribute('aria-label')).toBe('Model: gpt-5-old');
        expect(choicesOf('Model', 'effort', panel)).toEqual([]);
        const id = await send(desktop, 'And now?', panel);
        expect(calls(desktop, 'chat.send').at(-1)!.args).toEqual([id, 'And now?', { host: 'codex' }]);
    });

    it('the stand-in refuses what the desktop refuses, keeping the record as it was', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000E1',
            title: 'Kept',
            chosen: { host: 'claude', model: 'opus', effort: 'low' },
        });
        const said: { chatId: string; event: unknown }[] = [];
        desktop.chat.onEvent((chatId, event) => said.push({ chatId, event }));
        for (const [options, sentence] of [
            [{ host: 'codex', model: 'opus' }, 'That model is not one this host offers.'],
            [{ host: 'claude', model: 'haiku', effort: 'low' }, 'That effort is not one this model takes.'],
            [{ host: 'claude', effort: 'low' }, 'That effort is not one this model takes.'],
            [
                { host: 'claude', model: 'opus', effort: 'ultra' },
                'That effort is not one the desktop passes to a host.',
            ],
        ] as const) {
            expect(await desktop.chat.send('conv_01JDKEPT0000000000000000E1', 'x', options as never)).toBeNull();
            await waitFor(() => expect(said.at(-1)?.event).toEqual({ kind: 'done', error: sentence }));
        }
        expect(desktop.kept.get('conv_01JDKEPT0000000000000000E1')?.chosen).toEqual({
            host: 'claude',
            model: 'opus',
            effort: 'low',
        });
        expect(desktop.kept.get('conv_01JDKEPT0000000000000000E1')?.running).toBe(false);
    });

    it('reads the list again when a turn ends, so what the desktop changed meanwhile is listed', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000B1', campaignId: 'cmp_1', title: 'Spring' });
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000B2', campaignId: 'cmp_2', title: 'Autumn' });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        const hero = await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const id = await send(desktop, 'How is it doing?', hero.closest<HTMLElement>('.chat-panel')!);
        await waitFor(() => expect(names()).toEqual(['Spring', 'Autumn']));
        // While the turn runs, the desktop renames Autumn after its campaign; the list says so once the turn ends.
        desktop.kept.get('conv_01JDKEPT0000000000000000B2')!.title = 'Autumn outreach';
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() => expect(names()).toEqual(['Spring', 'Autumn outreach']));
    });

    it('shows a suggestion in the word the page uses for a campaign, and sends the word the runtime uses', async () => {
        const { desktop } = renderConsole();
        const chip = await waitFor(() => screen.getByRole('button', { name: 'Help me start a new agent.' }));
        expect(screen.getByRole('button', { name: 'How are my agents doing?' })).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Help me start a new campaign.' })).toBeNull();
        fireEvent.click(chip);
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.send')[0]!.args[1]).toBe('Help me start a new campaign.');
    });

    it('sends a turn with the host alone: the conversation the desktop keeps decides the campaign', async () => {
        const { desktop } = renderConsole();
        await waitFor(() => screen.getByRole('button', { name: /^Needs you/ }));
        bringForward('Agents');
        const spring = await waitFor(() => screen.getByText('Spring', { selector: 'b' }).closest('tr')!);
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        const hero = await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const id = await send(desktop, 'How is it doing?', hero.closest<HTMLElement>('.chat-panel')!);
        expect(calls(desktop, 'chat.create').map((each) => each.args)).toEqual([[], ['cmp_1']]);
        expect(desktop.kept.get(id)?.campaignId).toBe('cmp_1');
        expect(calls(desktop, 'chat.send').at(-1)!.args).toEqual([id, 'How is it doing?', { host: 'claude' }]);
    });

    it('hands a set-up chat over to the campaign its turn made: the chat takes its name, and the list follows', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Help me start a new campaign.');
        await waitFor(() =>
            expect(within(listed()).getByRole('button', { name: 'Help me start a new campaign.' })).toBeTruthy(),
        );
        desktop.kept.get(id)!.campaignId = 'cmp_2';
        desktop.kept.get(id)!.title = 'Autumn';
        flush(() => {
            desktop.emit(id, { kind: 'text', text: 'Made it.' });
            desktop.emit(id, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText('This chat is now the conversation of Autumn.'));
        expect(chats.value[id]).toMatchObject({ campaignId: 'cmp_2', title: 'Autumn' });
        await waitFor(() => expect(within(listed()).getByRole('button', { name: 'Autumn' })).toBeTruthy());
        // From now on it is that campaign's conversation: the Campaigns list's Chat opens it, and no other.
        bringForward('Agents');
        const autumn = await waitFor(() => screen.getByText('Autumn', { selector: 'b' }).closest('tr')!);
        fireEvent.click(within(autumn).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(inFront()).toBe('Autumn'));
        expect(calls(desktop, 'chat.create').filter((each) => each.args[0] === 'cmp_2')).toEqual([]);
        expect(Object.keys(chats.value)).toHaveLength(1);
    });

    it('offers the campaigns a set-up turn made to pick from, by name and as text, and attaches the one picked', async () => {
        const desktop = chatDesktop();
        desktop.names.set('cmp_3', 'Summer referrals');
        renderConsole(desktop);
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        const id = await send(desktop, 'Make two campaigns');
        flush(() => {
            desktop.emit(id, { kind: 'pick', campaignIds: ['cmp_3', 'cmp_9'] });
            desktop.emit(id, { kind: 'done', error: null });
        });
        const card = await waitFor(() => document.querySelector<HTMLElement>('.chat-pick')!);
        expect(card.textContent).toContain('Make this the conversation of');
        // A campaign the console has not read yet is named by its id.
        expect(
            within(card)
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Summer referrals', 'cmp_9']);
        fireEvent.click(within(card).getByRole('button', { name: 'Summer referrals' }));
        await waitFor(() => expect(calls(desktop, 'chat.attach').map((each) => each.args)).toEqual([[id, 'cmp_3']]));
        await waitFor(() => expect(chats.value[id]).toMatchObject({ campaignId: 'cmp_3', title: 'Summer referrals' }));
        expect(within(document.querySelector<HTMLElement>('.chat-pick')!).queryByRole('button')).toBeNull();
    });

    it('says a pick the desktop refused in its own words, and keeps the choice open', async () => {
        const desktop = chatDesktop();
        desktop.refuse.set('chat.attach', 'That agent has its conversation already. Open it from the agent.');
        renderConsole(desktop);
        const id = await send(desktop, 'Make two campaigns');
        flush(() => {
            desktop.emit(id, { kind: 'pick', campaignIds: ['cmp_1', 'cmp_2'] });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByRole('button', { name: 'Spring' }));
        fireEvent.click(screen.getByRole('button', { name: 'Spring' }));
        await waitFor(() => screen.getByText('That agent has its conversation already. Open it from the agent.'));
        expect(chats.value[id]?.campaignId).toBeNull();
        expect(within(document.querySelector<HTMLElement>('.chat-pick')!).getAllByRole('button')).toHaveLength(2);
    });

    it('says in one line that a turn starts a fresh session, and why', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'fresh', reason: 'host_changed' }));
        await waitFor(() =>
            screen.getByText('Claude Code has not seen this conversation; it was given the last messages.'),
        );
        flush(() => desktop.emit(id, { kind: 'fresh', reason: 'not_found' }));
        await waitFor(() =>
            screen.getByText("The AI's memory of this conversation was gone; it starts from the last messages."),
        );
    });

    it('says a notice once in the thread, and its Open folder button asks the desktop to open that folder alone', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'notice', text: 'Left out of this chat: the folder “x” has skills.' }));
        await waitFor(() => screen.getByText('Left out of this chat: the folder “x” has skills.'));
        expect(screen.queryByRole('button', { name: 'Open folder' })).toBeNull();
        flush(() =>
            desktop.emit(id, {
                kind: 'notice',
                text: 'Full access has ended. The folder is kept for seven days and then removed: /d/01JD0000000000000000000000-1788000000000',
                folder: '01JD0000000000000000000000-1788000000000',
            }),
        );
        await waitFor(() =>
            screen.getByText(/kept for seven days and then removed: \/d\/01JD0000000000000000000000-1788000000000/),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Open folder' }));
        await waitFor(() =>
            expect(calls(desktop, 'chat.openFolder').map((each) => each.args)).toEqual([
                ['01JD0000000000000000000000-1788000000000'],
            ]),
        );
    });

    it('draws Rename, Start fresh and Remove as icons that keep their names and say what they do', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Rename' })).toHaveProperty('disabled', false));
        const titles: Record<string, string> = {
            Rename: 'Give this conversation a name of your own',
            'Start fresh':
                "The AI's next turn begins without its memory of this conversation; what is shown here stays",
            Remove: 'Remove this conversation',
        };
        for (const [name, title] of Object.entries(titles)) {
            const button = screen.getByRole('button', { name });
            expect(button.textContent).toBe('');
            expect(button.getAttribute('title')).toBe(title);
            const glyph = button.querySelector('svg');
            expect(glyph?.getAttribute('aria-hidden')).toBe('true');
            expect(glyph?.querySelector('path')).toBeTruthy();
        }
    });

    it('starts a conversation fresh, and removes one only once asked again, saying the host keeps its transcript', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole('button', { name: 'Start fresh' })).toHaveProperty('disabled', false),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Start fresh' }));
        await waitFor(() => expect(calls(desktop, 'chat.restart').map((each) => each.args)).toEqual([[id]]));
        await waitFor(() =>
            screen.getByText("Started fresh: the AI's next turn begins without its memory of this conversation."),
        );

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        const asking = screen.getByRole('alertdialog', { name: 'Remove this conversation?' });
        expect(asking.textContent).toContain('the AI host keeps its own transcript of it');
        expect(calls(desktop, 'chat.remove')).toHaveLength(0);
        fireEvent.click(within(asking).getByRole('button', { name: 'Keep it' }));
        expect(screen.queryByRole('alertdialog')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Remove it' }));
        await waitFor(() => expect(calls(desktop, 'chat.remove').map((each) => each.args)).toEqual([[id]]));
        await waitFor(() => expect(chats.value[id]).toBeUndefined());
        await waitFor(() => expect(within(listed()).queryByRole('button', { name: 'Go' })).toBeNull());
    });

    it('asks before removing a conversation with a folder of its own, saying its files go seven days later and where they are until then; one an earlier desktop made names no folder', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000B1', title: 'Own folder', ownFolder: true });
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000B2', title: 'Shared folder' });
        renderConsole(desktop);
        for (const [id, title, own] of [
            ['conv_01JDKEPT0000000000000000B1', 'Own folder', true],
            ['conv_01JDKEPT0000000000000000B2', 'Shared folder', false],
        ] as const) {
            await waitFor(() => within(listed()).getByRole('button', { name: title }));
            fireEvent.click(within(listed()).getByRole('button', { name: title }));
            await waitFor(() => expect(calls(desktop, 'chat.open').map((each) => each.args[0])).toContain(id));
            const pane = shownPane()!;
            await waitFor(() =>
                expect(within(pane).getByRole('button', { name: 'Remove' })).toHaveProperty('disabled', false),
            );
            fireEvent.click(within(pane).getByRole('button', { name: 'Remove' }));
            const asking = within(pane).getByRole('alertdialog', { name: 'Remove this conversation?' });
            expect(asking.textContent).toContain('the AI host keeps its own transcript of it');
            if (own)
                expect(asking.textContent).toContain(
                    "The files in its folder go seven days later; until then they are in Jason's data directory, under desktop/chat-full-ended.",
                );
            else expect(asking.textContent).not.toMatch(/folder|seven days|chat-full-ended/);
            fireEvent.click(within(asking).getByRole('button', { name: 'Keep it' }));
        }
    });

    it('keeps Start fresh and Remove from a turn that runs, and says a refusal in the desktop’s words', async () => {
        const desktop = chatDesktop();
        renderConsole(desktop);
        const id = await send(desktop, 'Go');
        expect(screen.getByRole('button', { name: 'Start fresh' })).toHaveProperty('disabled', true);
        expect(screen.getByRole('button', { name: 'Remove' })).toHaveProperty('disabled', true);
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.refuse.set(
            'chat.remove',
            'The conversation could not be removed. Close anything that has its file open, then try again.',
        );
        await waitFor(() => expect(screen.getByRole('button', { name: 'Remove' })).toHaveProperty('disabled', false));
        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Remove it' }));
        await waitFor(() =>
            screen.getByText(
                'The conversation could not be removed. Close anything that has its file open, then try again.',
            ),
        );
        expect(chats.value[id]).toBeDefined();
    });

    it('leaves the place of a panel alone when it is given another chat with more turns', async () => {
        const height = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollHeight');
        Object.defineProperty(HTMLElement.prototype, 'scrollHeight', {
            configurable: true,
            get(this: HTMLElement) {
                return this.classList.contains('chat-scroll') ? 900 : 0;
            },
        });
        try {
            const desktop = chatDesktop();
            const one = 'conv_01JDKEPT0000000000000000A1';
            const three = 'conv_01JDKEPT0000000000000000A2';
            desktop.keep({
                id: one,
                campaignId: 'cmp_1',
                title: 'Spring',
                entries: [{ kind: 'user', text: 'One turn only' }],
            });
            desktop.keep({
                id: three,
                campaignId: 'cmp_2',
                title: 'Autumn',
                entries: [
                    { kind: 'user', text: 'First' },
                    { kind: 'user', text: 'Second' },
                    { kind: 'user', text: 'Third' },
                ],
            });
            const { fake } = renderConsole(desktop);
            // Both chats are held by opening them from the sidebar, as a person does.
            await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
            fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
            await waitFor(() => screen.getByText('One turn only'));
            fireEvent.click(within(listed()).getByRole('button', { name: 'Autumn' }));
            await waitFor(() => screen.getByText('Third'));
            // One panel, drawn with one chat and then given the other at the same place: React keeps the panel, as the
            // console does where a pane's chat changes under it.
            const view = render(
                <ApiProvider api={fake}>
                    <ChatPanel chatId={one} headed />
                </ApiProvider>,
            );
            const box = view.container.querySelector<HTMLElement>('.chat-scroll')!;
            await waitFor(() => within(box).getByText('One turn only'));
            box.scrollTop = 50;
            view.rerender(
                <ApiProvider api={fake}>
                    <ChatPanel chatId={three} headed />
                </ApiProvider>,
            );
            await waitFor(() => within(box).getByText('Third'));
            expect(view.container.querySelector('.chat-scroll')).toBe(box);
            expect(box.scrollTop).toBe(50);
        } finally {
            if (height) Object.defineProperty(HTMLElement.prototype, 'scrollHeight', height);
            else delete (HTMLElement.prototype as { scrollHeight?: number }).scrollHeight;
        }
    });

    it('opens a long conversation where it is, and the words its running turn adds then leave the person’s place alone', async () => {
        const layout = layOutChats();
        try {
            const desktop = chatDesktop();
            const id = 'conv_01JDKEPT0000000000000000R2';
            desktop.keep({
                id,
                campaignId: 'cmp_1',
                title: 'Spring',
                running: true,
                entries: [
                    { kind: 'user', text: 'How is it doing?' },
                    { kind: 'text', text: `So far: ${'a reply, '.repeat(200)}` },
                    { kind: 'user', text: 'And the bounces?' },
                ],
            });
            renderConsole(desktop);
            await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
            fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
            await waitFor(() => screen.getByText('And the bounces?'));
            const box = screen.getByText('And the bounces?').closest<HTMLElement>('.chat-scroll')!;
            await settle();
            expect(box.scrollTop).toBe(0);
            flush(() => desktop.emit(id, { kind: 'text', text: `Two bounced: ${'an address, '.repeat(40)}` }));
            await waitFor(() => expect(box.textContent).toContain('Two bounced:'));
            await settle();
            expect(box.scrollTop).toBe(0);
        } finally {
            layout.restore();
        }
    });

    it('opens a conversation whose turn still runs as running, with Stop, and the turn’s events reach it', async () => {
        const desktop = chatDesktop();
        // A page loaded again while the desktop runs a turn of Spring's conversation.
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000R1',
            campaignId: 'cmp_1',
            title: 'Spring',
            running: true,
            entries: [{ kind: 'user', text: 'How is it doing?' }],
        });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => screen.getByText('How is it doing?'));
        const panel = screen.getByText('How is it doing?').closest<HTMLElement>('.chat-panel')!;
        expect(within(panel).getByRole('button', { name: 'Stop' })).toBeTruthy();
        expect(within(panel).getByText('Thinking…')).toBeTruthy();
        // Its acts are in the "…" of the agent's header over it, Remove held while the turn runs.
        const header = document.querySelector<HTMLElement>('.agent-head')!;
        fireEvent.click(within(header).getByRole('button', { name: "More for this agent's conversation" }));
        const remove = within(header).getByRole('menuitem', { name: 'Remove conversation' });
        expect(remove).toHaveProperty('disabled', true);
        fireEvent.keyDown(remove, { key: 'Escape' });
        fireEvent.click(within(panel).getByRole('button', { name: 'Stop' }));
        expect(calls(desktop, 'chat.stop').map((each) => each.args)).toEqual([['conv_01JDKEPT0000000000000000R1']]);
        flush(() => {
            desktop.emit('conv_01JDKEPT0000000000000000R1', { kind: 'text', text: 'Twelve replies.' });
            desktop.emit('conv_01JDKEPT0000000000000000R1', { kind: 'done', error: null });
        });
        await waitFor(() => within(panel).getByText('Twelve replies.'));
        expect(within(panel).queryByRole('button', { name: 'Stop' })).toBeNull();
        expect(within(panel).getByRole('button', { name: 'Send' })).toBeTruthy();
    });

    it('says in the sidebar, in the desktop’s words, a list that failed, a conversation it would not open and one it would not make', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000F1', campaignId: 'cmp_1', title: 'Spring' });
        desktop.refuse.set('chat.list', 'The desktop could not read its conversations.');
        renderConsole(desktop);
        await waitFor(() => within(side()).getByText('The desktop could not read its conversations.'));
        desktop.refuse.delete('chat.list');
        // The list read again with the next turn's end: the note goes, the conversation is there.
        const id = await send(desktop, 'Go');
        await waitFor(() =>
            expect(within(side()).queryByText('The desktop could not read its conversations.')).toBeNull(),
        );
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.refuse.set('chat.open', 'That conversation is not one this desktop keeps.');
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => within(side()).getByText('That conversation is not one this desktop keeps.'));
        expect(screen.queryByRole('heading', { name: 'What should Jason do in Spring?' })).toBeNull();
        desktop.refuse.set('chat.create', "That is not an agent's id.");
        fireEvent.click(within(side()).getByRole('button', { name: 'More to create' }));
        fireEvent.click(within(side()).getByRole('menuitem', { name: /^New chat/ }));
        await waitFor(() => within(side()).getByText("That is not an agent's id."));
        expect(Object.keys(chats.value)).toEqual([id]);
    });

    it('says in the chat, in the desktop’s words, a Start fresh it refused and a turn sent to a conversation it does not keep', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.refuse.set(
            'chat.restart',
            'This conversation is still answering. Wait for it to finish, or stop it, then start it fresh.',
        );
        await waitFor(() =>
            expect(screen.getByRole('button', { name: 'Start fresh' })).toHaveProperty('disabled', false),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Start fresh' }));
        await waitFor(() =>
            screen.getByText(
                'This conversation is still answering. Wait for it to finish, or stop it, then start it fresh.',
            ),
        );
        expect(screen.queryByText(/^Started fresh/)).toBeNull();
        // The desktop no longer keeps it, removed elsewhere: its next turn is refused, said as the turn's error.
        desktop.kept.delete(id);
        await send(desktop, 'Again');
        await waitFor(() => screen.getByText('That conversation is not one this desktop keeps.'));
        expect(chats.value[id]).toMatchObject({ running: false });
        expect(chats.value[id]?.entries.slice(-2)).toEqual([
            { kind: 'user', text: 'Again' },
            { kind: 'error', text: 'That conversation is not one this desktop keeps.' },
        ]);
        expect(document.querySelector('.chat-error')?.textContent).not.toMatch(/Error invoking|remote method/);
    });

    it('draws one chat for two quick clicks on a campaign’s Chat, and makes no conversation', async () => {
        const { desktop } = renderConsole();
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        bringForward('Agents');
        const spring = await waitFor(() => screen.getByText('Spring', { selector: 'b' }).closest('tr')!);
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        expect(calls(desktop, 'chat.create').filter((each) => each.args[0] === 'cmp_1')).toEqual([]);
        expect(Object.values(chats.value).filter((chat) => chat.campaignId === 'cmp_1')).toHaveLength(1);
        expect(inFront()).toBe('Spring');
    });

    it('asks Remove as a dialog: focus on keeping it, Escape and Keep it close it back to Remove, nothing else usable meanwhile', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        const remove = await waitFor(() => {
            const button = screen.getByRole('button', { name: 'Remove' });
            expect(button).toHaveProperty('disabled', false);
            return button;
        });
        fireEvent.click(remove);
        const dialog = screen.getByRole('alertdialog', { name: 'Remove this conversation?' });
        expect(
            within(dialog)
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Keep it', 'Remove it']);
        await waitFor(() =>
            expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Keep it' })),
        );
        const described = document.getElementById(dialog.getAttribute('aria-describedby') ?? '');
        expect(described?.textContent).toContain('the AI host keeps its own transcript of it');
        // While it asks, the conversation takes nothing else.
        expect(screen.getByRole('textbox', { name: 'Your message' })).toHaveProperty('disabled', true);
        expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);
        expect(screen.getByRole('button', { name: 'Start fresh' })).toHaveProperty('disabled', true);
        expect(screen.getByRole('button', { name: 'Remove' })).toHaveProperty('disabled', true);
        fireEvent.keyDown(dialog, { key: 'Escape' });
        expect(screen.queryByRole('alertdialog')).toBeNull();
        await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Remove' })));
        expect(screen.getByRole('textbox', { name: 'Your message' })).toHaveProperty('disabled', false);
        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Keep it' }));
        await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Remove' })));
        expect(calls(desktop, 'chat.remove')).toHaveLength(0);
    });

    it('offers only the last pick of a set-up chat, and draws an earlier one as text', async () => {
        const desktop = chatDesktop();
        desktop.names.set('cmp_3', 'Summer referrals');
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000P1',
            title: 'Make campaigns',
            entries: [
                { kind: 'user', text: 'Make two campaigns' },
                { kind: 'pick', campaignIds: ['cmp_2', 'cmp_3'] },
                { kind: 'user', text: 'Make another' },
                { kind: 'pick', campaignIds: ['cmp_1'] },
            ],
        });
        renderConsole(desktop);
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Make campaigns' }));
        await waitFor(() => expect(document.querySelectorAll('.chat-pick')).toHaveLength(2));
        const [earlier, last] = [...document.querySelectorAll<HTMLElement>('.chat-pick')];
        expect(within(earlier!).queryByRole('button')).toBeNull();
        expect(earlier!.textContent).toContain('Autumn, Summer referrals');
        expect(
            within(last!)
                .getAllByRole('button')
                .map((each) => each.textContent?.trim()),
        ).toEqual(['Spring']);
        fireEvent.click(within(last!).getByRole('button', { name: 'Spring' }));
        await waitFor(() => expect(chats.value['conv_01JDKEPT0000000000000000P1']?.campaignId).toBe('cmp_1'));
        expect(desktop.calls.filter((each) => each.name === 'chat.attach').map((each) => each.args)).toEqual([
            ['conv_01JDKEPT0000000000000000P1', 'cmp_1'],
        ]);
    });

    it('reads the list again after Start fresh, so the conversation started fresh is listed first', async () => {
        const desktop = chatDesktop();
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000S1',
            campaignId: 'cmp_1',
            title: 'Spring',
            updatedAt: '2026-11-01T00:00:00.000Z',
        });
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000S2',
            campaignId: 'cmp_2',
            title: 'Autumn',
            updatedAt: '2026-11-02T00:00:00.000Z',
        });
        desktop.keep({
            id: 'conv_01JDKEPT0000000000000000S3',
            campaignId: 'cmp_3',
            title: 'Summer',
            updatedAt: '2026-11-03T00:00:00.000Z',
        });
        renderConsole(desktop);
        await waitFor(() => expect(names()).toEqual(['Summer', 'Autumn', 'Spring']));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        const header = document.querySelector<HTMLElement>('.agent-head')!;
        fireEvent.click(within(header).getByRole('button', { name: "More for this agent's conversation" }));
        fireEvent.click(within(header).getByRole('menuitem', { name: 'Start conversation fresh' }));
        await waitFor(() => expect(names()).toEqual(['Spring', 'Summer', 'Autumn']));
    });

    it('says a failed list once: the note is not drawn again as the chats change', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000N1', campaignId: 'cmp_1', title: 'Spring' });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        // The list read again after a turn starts fails; the list keeps what it had, and says why.
        desktop.refuse.set('chat.list', 'The desktop could not read its conversations.');
        await send(desktop, 'Go');
        const note = await waitFor(() => within(side()).getByRole('alert'));
        expect(note.textContent).toContain('The desktop could not read its conversations.');
        expect(within(listed()).getByRole('button', { name: 'Spring' })).toBeTruthy();
        const box = screen.getByRole('textbox', { name: 'Your message' });
        for (const text of ['F', 'Fi', 'Fin', 'Find']) fireEvent.change(box, { target: { value: text } });
        expect(within(side()).getAllByRole('alert')).toEqual([note]);
        expect(note.isConnected).toBe(true);
    });

    it('marks the conversation open in front in the sidebar', async () => {
        const desktop = chatDesktop();
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000C1', campaignId: 'cmp_1', title: 'Spring' });
        desktop.keep({ id: 'conv_01JDKEPT0000000000000000C2', campaignId: 'cmp_2', title: 'Autumn' });
        renderConsole(desktop);
        await waitFor(() => within(listed()).getByRole('button', { name: 'Spring' }));
        const current = () =>
            within(listed())
                .getAllByRole('button')
                .filter((each) => each.getAttribute('aria-current') === 'true')
                .map((each) => each.querySelector('.rt')?.textContent?.trim());
        expect(current()).toEqual([]);
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        await waitFor(() => expect(current()).toEqual(['Spring']));
        fireEvent.click(within(listed()).getByRole('button', { name: 'Autumn' }));
        await waitFor(() => expect(current()).toEqual(['Autumn']));
    });

    it('renames a conversation from its header: the sidebar follows, and nothing is asked before it is kept', async () => {
        const { desktop } = renderConsole();
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        expect(screen.getByRole('button', { name: 'Rename' })).toHaveProperty('disabled', true);
        const id = await send(desktop, 'Help me start a new campaign.');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Rename' })).toHaveProperty('disabled', false));
        fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
        const box = screen.getByRole('textbox', { name: 'Conversation name' });
        expect((box as HTMLInputElement).value).toBe('Help me start a new campaign.');
        expect(document.activeElement).toBe(box);
        fireEvent.change(box, { target: { value: 'Spring plan' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save name' }));
        await waitFor(() =>
            expect(calls(desktop, 'chat.rename').map((each) => each.args)).toEqual([[id, 'Spring plan']]),
        );
        await waitFor(() => expect(within(listed()).getByRole('button', { name: 'Spring plan' })).toBeTruthy());
        expect(inFront()).toBe('Spring plan');
        expect(screen.queryByRole('textbox', { name: 'Conversation name' })).toBeNull();
        // Escape leaves the name as it was and asks nothing.
        fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Conversation name' }), { target: { value: 'Other' } });
        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Conversation name' }), { key: 'Escape' });
        expect(screen.queryByRole('textbox', { name: 'Conversation name' })).toBeNull();
        expect(calls(desktop, 'chat.rename')).toHaveLength(1);
        expect(chats.value[id]?.title).toBe('Spring plan');
    });

    it('says a rename the desktop refused in its own words and keeps the name as it was', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.refuse.set('chat.rename', 'A name needs at least one character that shows.');
        await waitFor(() => expect(screen.getByRole('button', { name: 'Rename' })).toHaveProperty('disabled', false));
        fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Conversation name' }), { target: { value: hostile } });
        fireEvent.submit(screen.getByRole('textbox', { name: 'Conversation name' }));
        await waitFor(() => screen.getByText('A name needs at least one character that shows.'));
        expect(chats.value[id]?.title).toBe('Go');
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('takes the name the desktop gave a set-up chat after its first turn, said after the turn ended', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Help me reach dentists in Ohio');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.kept.get(id)!.title = hostile;
        flush(() => desktop.emit(id, { kind: 'renamed', title: hostile }));
        await waitFor(() => expect(within(listed()).getByRole('button', { name: hostile })).toBeTruthy());
        expect(chats.value[id]?.title).toBe(hostile);
        expect(document.querySelector('.side img[src="x"]')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('gives focus back to Rename after Save, Cancel or Escape, and Escape closes the box from anywhere in it', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        const rename = () => screen.getByRole('button', { name: 'Rename' });
        await waitFor(() => expect(rename()).toHaveProperty('disabled', false));

        fireEvent.click(rename());
        fireEvent.change(screen.getByRole('textbox', { name: 'Conversation name' }), {
            target: { value: 'Saved name' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Save name' }));
        await waitFor(() => expect(document.activeElement).toBe(rename()));

        fireEvent.click(rename());
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(document.activeElement).toBe(rename());

        fireEvent.click(rename());
        fireEvent.keyDown(screen.getByRole('button', { name: 'Save name' }), { key: 'Escape' });
        expect(screen.queryByRole('textbox', { name: 'Conversation name' })).toBeNull();
        expect(document.activeElement).toBe(rename());
        expect(calls(desktop, 'chat.rename')).toHaveLength(1);
    });

    it('cuts a typed name by characters as the desktop does, and a picked campaign keeps a name a person gave', async () => {
        const desktop = chatDesktop();
        desktop.names.set('cmp_3', 'Summer referrals');
        renderConsole(desktop);
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        const id = await send(desktop, 'Make two campaigns');
        flush(() => {
            desktop.emit(id, { kind: 'pick', campaignIds: ['cmp_3', 'cmp_9'] });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => expect(screen.getByRole('button', { name: 'Rename' })).toHaveProperty('disabled', false));
        fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
        const box = screen.getByRole('textbox', { name: 'Conversation name' }) as HTMLInputElement;
        fireEvent.change(box, { target: { value: '\u{1F600}'.repeat(70) } });
        expect(Array.from(box.value)).toHaveLength(60);
        fireEvent.change(box, { target: { value: 'Summer\u3164 picks' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save name' }));
        await waitFor(() => expect(chats.value[id]?.title).toBe('Summer picks'));
        const card = await waitFor(() => document.querySelector<HTMLElement>('.chat-pick')!);
        fireEvent.click(within(card).getByRole('button', { name: 'Summer referrals' }));
        await waitFor(() => expect(chats.value[id]).toMatchObject({ campaignId: 'cmp_3', title: 'Summer picks' }));
        expect(desktop.kept.get(id)?.title).toBe('Summer picks');
    });

    it('draws a conversation’s title made of a person’s words as text in Recent', async () => {
        const { desktop } = renderConsole();
        await send(desktop, hostile);
        await waitFor(() => expect(names()).toContain(hostile));
        expect(listed().querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });
});
