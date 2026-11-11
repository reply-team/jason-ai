import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';

// The conversations the desktop keeps: listed in the sidebar, reopened with what the page drew, a set-up chat handed
// over to the campaign its turn made or to the one the person picks, a fresh session said in one line, and a
// conversation started afresh or removed.

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(desktop: StubDesktop = stubDesktop()): { desktop: StubDesktop; fake: FakeApi } {
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
    return { desktop, fake };
}

const side = () => document.querySelector<HTMLElement>('.side')!;
const listed = () => within(side()).getByRole('navigation', { name: 'Conversations' });
/** What the sidebar's list says, in its order. */
const names = () =>
    within(listed())
        .getAllByRole('button')
        .map((each) => each.textContent?.trim());
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const thread = () =>
    document.querySelector<HTMLElement>('.dv-active-group .thread') ?? document.querySelector<HTMLElement>('.thread')!;

/** Types into a chat, the only one shown unless `panel` names it, and sends; resolves with the conversation it went to. */
async function send(desktop: StubDesktop, text: string, panel?: HTMLElement): Promise<string> {
    const before = calls(desktop, 'chat.send').length;
    const scope = panel ? within(panel) : screen;
    await waitFor(() => scope.getByRole('button', { name: 'Send' }));
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
        const desktop = stubDesktop();
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
        expect(document.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
        // Shown again, it is brought forward, not read again.
        fireEvent.click(within(listed()).getByRole('button', { name: 'Spring' }));
        expect(calls(desktop, 'chat.open')).toHaveLength(1);
    });

    it('reads the list again when a turn ends, so what the desktop changed meanwhile is listed', async () => {
        const desktop = stubDesktop();
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

    it('sends a turn with the host alone: the conversation the desktop keeps decides the campaign', async () => {
        const { desktop } = renderConsole();
        await waitFor(() => screen.getByRole('button', { name: 'Campaigns' }));
        fireEvent.click(within(side()).getByRole('button', { name: 'Campaigns' }));
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
        fireEvent.click(within(side()).getByRole('button', { name: 'Campaigns' }));
        const autumn = await waitFor(() => screen.getByText('Autumn', { selector: 'b' }).closest('tr')!);
        fireEvent.click(within(autumn).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(calls(desktop, 'chat.create').at(-1)!.args).toEqual(['cmp_2']));
        expect(Object.keys(chats.value)).toHaveLength(1);
    });

    it('offers the campaigns a set-up turn made to pick from, by name and as text, and attaches the one picked', async () => {
        const desktop = stubDesktop();
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
        const desktop = stubDesktop();
        desktop.refuse.set('chat.attach', 'That campaign has its conversation already. Open it from the campaign.');
        renderConsole(desktop);
        const id = await send(desktop, 'Make two campaigns');
        flush(() => {
            desktop.emit(id, { kind: 'pick', campaignIds: ['cmp_1', 'cmp_2'] });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByRole('button', { name: 'Spring' }));
        fireEvent.click(screen.getByRole('button', { name: 'Spring' }));
        await waitFor(() => screen.getByText('That campaign has its conversation already. Open it from the campaign.'));
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
            screen.getByText("The agent's memory of this conversation was gone; it starts from the last messages."),
        );
    });

    it('starts a conversation afresh, and removes one only once asked again, saying the host keeps its transcript', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        await waitFor(() =>
            expect(screen.getByRole('button', { name: 'Start afresh' })).toHaveProperty('disabled', false),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Start afresh' }));
        await waitFor(() => expect(calls(desktop, 'chat.restart').map((each) => each.args)).toEqual([[id]]));
        await waitFor(() =>
            screen.getByText("Started afresh: the agent's next turn begins without its memory of this conversation."),
        );

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        const asking = screen.getByRole('alertdialog', { name: 'Remove this conversation?' });
        expect(asking.textContent).toContain('the agent host keeps its own transcript of it');
        expect(calls(desktop, 'chat.remove')).toHaveLength(0);
        fireEvent.click(within(asking).getByRole('button', { name: 'Keep it' }));
        expect(screen.queryByRole('alertdialog')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
        fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Remove it' }));
        await waitFor(() => expect(calls(desktop, 'chat.remove').map((each) => each.args)).toEqual([[id]]));
        await waitFor(() => expect(chats.value[id]).toBeUndefined());
        await waitFor(() => expect(within(listed()).queryByRole('button', { name: 'Go' })).toBeNull());
    });

    it('keeps Start afresh and Remove from a turn that runs, and says a refusal in the desktop’s words', async () => {
        const desktop = stubDesktop();
        renderConsole(desktop);
        const id = await send(desktop, 'Go');
        expect(screen.getByRole('button', { name: 'Start afresh' })).toHaveProperty('disabled', true);
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

    it('opens a conversation whose turn still runs as running, with Stop, and the turn’s events reach it', async () => {
        const desktop = stubDesktop();
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
        expect(within(panel).getByRole('button', { name: 'Remove' })).toHaveProperty('disabled', true);
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
        const desktop = stubDesktop();
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
        desktop.refuse.set('chat.create', 'That is not a campaign id.');
        fireEvent.click(within(side()).getByRole('button', { name: 'New chat' }));
        await waitFor(() => within(side()).getByText('That is not a campaign id.'));
        expect(Object.keys(chats.value)).toEqual([id]);
    });

    it('says in the chat, in the desktop’s words, a Start afresh it refused and a turn sent to a conversation it does not keep', async () => {
        const { desktop } = renderConsole();
        const id = await send(desktop, 'Go');
        flush(() => desktop.emit(id, { kind: 'done', error: null }));
        desktop.refuse.set(
            'chat.restart',
            'This conversation is still answering. Wait for it to finish, or stop it, then start it afresh.',
        );
        await waitFor(() =>
            expect(screen.getByRole('button', { name: 'Start afresh' })).toHaveProperty('disabled', false),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Start afresh' }));
        await waitFor(() =>
            screen.getByText(
                'This conversation is still answering. Wait for it to finish, or stop it, then start it afresh.',
            ),
        );
        expect(screen.queryByText(/^Started afresh/)).toBeNull();
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

    it('opens one conversation for two quick clicks on a campaign’s Chat', async () => {
        const { desktop } = renderConsole();
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        fireEvent.click(within(side()).getByRole('button', { name: 'Campaigns' }));
        const spring = await waitFor(() => screen.getByText('Spring', { selector: 'b' }).closest('tr')!);
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        await waitFor(() =>
            expect(calls(desktop, 'chat.create').filter((each) => each.args[0] === 'cmp_1')).toHaveLength(2),
        );
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
        expect(Object.values(chats.value).filter((chat) => chat.campaignId === 'cmp_1')).toHaveLength(1);
        expect(screen.getAllByText('Spring', { selector: '.dv-default-tab-content' })).toHaveLength(1);
    });
});
