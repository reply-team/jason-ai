import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { DesktopCommand, DesktopFullAccessWhy } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { registry } from '../platform/slots';
import { browserPlugins } from '../plugins';
import { navigate, parseRoute, route } from '../router';
import * as classicAttention from '../screens/attention/store';
import * as classicCampaigns from '../screens/campaigns/store';
import { journalLine, setupFound, chatDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { readdirSync, readFileSync } from 'node:fs';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { nothingWaitsInChats } from '../test/waiting';
import { choicesOf, chooseIn, currentOf, menuButton, openMenu, waitForMenus } from '../test/menus';
import { fixtures } from '../test/fixtures';
import { fromFoot, layOutChats, scrollAs } from '../test/scroll';
import * as consoleCampaigns from './campaigns';
import { commandTarget, Console, reloadDelay } from './console';
import { attention, forgetNeeds } from './needs';
import { chats, clock, conversations, currentChat, here, lines, setup, setupBusy, streamState } from './state';

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(fake: FakeApi, desktop: StubDesktop = chatDesktop(), journal: StubStream = stubStream()) {
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
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { desktop, journal };
}

function pending(fake: FakeApi): FakeApi {
    return (
        nothingWaitsInChats(fake)
            .answer('approval.list', 'approval.list/pending')
            // Needs you reads each approval it lists whole, for its row's Approve.
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/set')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
    );
}

/**
 * The panels Live's sidebar does not draw, by the name the sidebar gives each in the demo. A panel is brought forward
 * the way a link or the palette does, since no row of the sidebar reaches it in Live.
 */
const hidden = { Agents: 'campaigns', Activity: 'activity', Connect: 'connect' } as const;

/** Shows a panel in the area the way a person does, from the sidebar; Live's sidebar reaches Needs you and Settings. */
function show(name: string): void {
    if (name in hidden) {
        flush(() => here().dock.show(hidden[name as keyof typeof hidden]));
        return;
    }
    const side = within(document.querySelector<HTMLElement>('.side')!);
    fireEvent.click(side.getByRole('button', { name }));
}

const panel = (name: string) => screen.getByRole('heading', { name, level: 1 }).closest<HTMLElement>('.painted')!;

/**
 * Chooses Needs you from the sidebar once what waits was read and the chat the desktop's New chat made is shown, so
 * neither comes in front of it afterwards; resolves with the panel.
 */
async function openNeeds(): Promise<HTMLElement> {
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    await waitFor(() => expect(currentChat.value).not.toBeNull());
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
    return waitFor(() => panel('Needs you'));
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.approvalsCursor.value = null;
    attention.questionsCursor.value = null;
    forgetNeeds();
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

/** The stylesheet's rules, selector and declarations, with its comments taken out. */
// Read from the file: the test run hands a stylesheet import back empty. The run starts in ui/.
const stylesheet = readFileSync('src/console/console.css', 'utf8');
const rules = (): { selector: string; body: string }[] =>
    Array.from(stylesheet.replace(/[/][*][^]*?[*][/]/g, '').matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
        selector: rule[1]!.trim(),
        body: rule[2]!,
    }));

/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ');

const asked = (fake: FakeApi, operation: string) => fake.calls.filter((each) => each.operation === operation).length;

/** The shown chat's scrolling box, where its thread is drawn. */
const thread = () => document.querySelector<HTMLElement>('.chat-scroll')!;

/** Lets what a change set going run out: the effects, the observers and the timers already due. */
const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 20)));

/** Sends `text` from the console's first chat, at its foot, as a person does; resolves with the chat's id. */
async function sendFromFoot(desktop: StubDesktop, text: string): Promise<string> {
    const box = await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
    scrollAs(thread(), thread().scrollHeight);
    fireEvent.change(box, { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
    await waitFor(() => expect(fromFoot(thread())).toBe(0));
    return desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
}

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('the console', () => {
    it('opens a chat when New chat is chosen, and reads what needs the person and the campaigns from the runtime', async () => {
        const fake = pending(new FakeApi());
        renderConsole(fake);
        // Each read answers when it answers: the approvals, the questions and the campaigns arrive in no fixed order, so the
        // screen is read once every one of them has been asked and has landed.
        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
            expect(fake.calls.map((each) => each.operation).sort()).toEqual(
                ['approval.list', 'campaign.list', 'campaign.summary', 'decision.list', 'system.info'].sort(),
            );
        });
        // What the empty chat says in Ask first is true of it: a message follows its campaign's send mode, and the AI
        // approves on the person's word. A new chat starts in Full access, which says nothing of it.
        chooseIn('Access mode', 'mode', 'ask_first');
        const hero = () => screen.getByRole('heading', { name: 'What should Jason work on?' }).closest('.hero')!;
        await waitFor(() =>
            expect(hero().querySelector('p')?.textContent).toBe(
                "A message written here follows its campaign's send mode; the AI approves when you tell it to.",
            ),
        );
        // What waits is a page of its own, chosen from the sidebar.
        const needs = await openNeeds();
        expect(within(needs).getAllByText('Ada Lovelace').length).toBeGreaterThan(0);
        expect(within(needs).getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
    });

    it('approves and rejects with a reason through the runtime, and reads the list again', async () => {
        const fake = pending(new FakeApi())
            .answer('approval.approve', 'approval.approve/approved')
            .answer('approval.reject', 'approval.reject/rejected');
        renderConsole(fake);
        const needs = await openNeeds();
        await waitFor(() => within(needs).getAllByText('Grace Hopper'));
        // The list is on the left; the item chosen is whole in the pane, with the acts on it alone.
        const pane = () => needs.querySelector<HTMLElement>('.needs-pane')!;
        const live = (name: string) => !within(pane()).getByRole('button', { name }).hasAttribute('disabled');
        // Approve is live once the approval has been read whole.
        await waitFor(() => expect(pane().dataset['id']).toBe('apr_1'));
        await waitFor(() => expect(live('Approve')).toBe(true));
        fireEvent.click(within(pane()).getByRole('button', { name: 'Approve' }));
        await waitFor(() => expect(fake.sent('approval.approve')).toEqual({ approval_id: 'apr_1' }));
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'approval.list')).toHaveLength(2));
        fireEvent.click(needs.querySelector<HTMLElement>('[data-row="apr_2"]')!);
        await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
        await waitFor(() => expect(live('Reject')).toBe(true));
        // Reject asks first, with the reason in its question.
        fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
        const asked = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
        fireEvent.input(within(asked).getByRole('textbox'), { target: { value: 'not this quarter' } });
        fireEvent.click(within(asked).getByRole('button', { name: 'Reject' }));
        await waitFor(() =>
            expect(fake.sent('approval.reject')).toMatchObject({ approval_id: 'apr_2', reason: 'not this quarter' }),
        );
    });

    it('answers a question with one of its options and a reason', async () => {
        const fake = pending(new FakeApi())
            .answer('decision.get', 'decision.get/pending-with-options')
            .answer('decision.answer', 'decision.answer/answered');
        renderConsole(fake);
        await openNeeds();
        await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        // The question chosen from the list is whole in the pane, its options read with it.
        fireEvent.click(panel('Needs you').querySelector<HTMLElement>('[data-row="dec_1"]')!);
        const row = () => panel('Needs you').querySelector<HTMLElement>('.needs-pane[data-id="dec_1"]')!;
        await waitFor(() => within(row()).getByRole('button', { name: 'Pause until the news' }));
        fireEvent.input(within(row()).getByRole('textbox', { name: 'Reason (optional)' }), {
            target: { value: 'their CFO is leaving' },
        });
        fireEvent.click(within(row()).getByRole('button', { name: 'Pause until the news' }));
        await waitFor(() =>
            expect(fake.sent('decision.answer')).toMatchObject({
                decision_id: 'dec_1',
                option: 'Pause until the news',
                reason: 'their CFO is leaving',
            }),
        );
    });

    it('lists the campaigns with the runtime counts, and opens a chat about one', async () => {
        const fake = pending(new FakeApi());
        renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Needs you' }));
        show('Agents');
        await waitFor(() => within(panel('Agents')).getByText('Summer referrals'));
        const spring = within(panel('Agents')).getByText('Spring').closest('tr')!;
        expect(within(spring).getByText('Active')).toBeTruthy();
        expect(within(spring).getByText('22')).toBeTruthy();
        // Counted as the browser's Agents page counts them: archived people left out.
        expect(fake.sent('campaign.summary')).toEqual({
            campaign_ids: ['cmp_1', 'cmp_2', 'cmp_3'],
            exclude_archived_contacts: true,
        });
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
    });

    it('says a Campaigns panel with nothing in it and how a campaign starts, and opens a new chat from it', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('campaign.list', 'campaign.list/screen-empty');
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Needs you' }));
        show('Agents');
        await waitFor(() =>
            expect(words(panel('Agents'))).toContain(
                'No agents yet. An agent is set up in a chat with Jason: say who you want to reach, and the AI sets it up with you.',
            ),
        );
        expect(asked(fake, 'campaign.summary')).toBe(0);
        const made = () => desktop.calls.filter((each) => each.name === 'chat.create');
        await waitFor(() => expect(made()).toHaveLength(1), { timeout: 2000 });
        fireEvent.click(within(panel('Agents')).getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(made()).toHaveLength(2));
        expect(made()[1]!.args).toEqual([]);
    });

    it('draws a hostile campaign name as text, in the list and in a chat about it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Needs you' }));
        show('Agents');
        await waitFor(() => within(panel('Agents')).getByText('Spring'));
        // A person may name a campaign anything; the next read brings that name back, to the desktop as well.
        desktop.names.set('cmp_1', hostile);
        flush(() => {
            consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.map((each) =>
                each.id === 'cmp_1' ? { ...each, name: hostile } : each,
            );
        });
        const row = within(panel('Agents')).getByText(hostile).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: `What should Jason do in ${hostile}?` }));
        // The only images of the page are the brand's own: the sidebar's and the start screen's mark.
        expect(
            Array.from(document.querySelectorAll('img')).filter(
                (img) => !img.closest('.brand') && !img.classList.contains('start-mark'),
            ),
        ).toEqual([]);
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('opens the journal stream at the origin and with the token the stream was given, and draws a hostile line as text', async () => {
        const fake = pending(new FakeApi());
        const { journal } = renderConsole(fake);
        await waitFor(() => expect(journal.opened).toHaveLength(1));
        expect(journal.opened[0]).toEqual({
            url: 'ws://127.0.0.1:7781/v1/journal.stream',
            protocols: ['jason.journal.v1', 'jason.bearer.tok'],
        });
        flush(() => {
            journal.open();
            journal.send(journalLine({ campaign_name: hostile, contact_name: hostile }));
        });
        show('Activity');
        const activity = await waitFor(() => panel('Activity'));
        await waitFor(() => expect(within(activity).getAllByText(hostile)).toHaveLength(2));
        expect(activity.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('describes the Activity panel by the roles, not by agents', async () => {
        renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('button', { name: 'Needs you' }));
        show('Activity');
        const activity = await waitFor(() => panel('Activity'));
        expect(words(activity)).toContain('What Jason and its roles do, as the journal records it.');
    });

    it('picks a model and, where the model takes one, an effort beside the host, and sends both with the turn', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => menuButton('Model'));
        expect(currentOf('Model', 'model')).toBe('');
        expect(choicesOf('Model', 'model').map((each) => each.text)).toEqual([
            'Default (recommended)',
            'Opus',
            'Sonnet',
            'Haiku',
            'Another model…',
        ]);
        // The host's own model takes no effort the desktop knows of: none is offered.
        expect(choicesOf('Model', 'effort')).toEqual([]);
        chooseIn('Model', 'model', 'opus');
        expect(choicesOf('Model', 'effort').map((each) => each.text)).toEqual([
            'Low',
            'Medium',
            'High',
            'Extra high',
            'Max',
            'Default effort',
        ]);
        chooseIn('Model', 'effort', 'xhigh');
        expect(menuButton('Model').getAttribute('aria-label')).toBe('Model: Opus, Extra high effort');
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Think hard.' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
            'Think hard.',
            { host: 'claude', model: 'opus', effort: 'xhigh' },
        ]);
        // A model that takes no effort hides the steps, and the next turn names none.
        chooseIn('Model', 'model', 'haiku');
        expect(choicesOf('Model', 'effort')).toEqual([]);
    });

    it('takes a model’s full name typed on Claude Code, with no effort', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => menuButton('Model'));
        chooseIn('Model', 'model', 'opus');
        chooseIn('Model', 'effort', 'high');
        // Another model keeps the menu open on the box for the name, and takes the focus there.
        fireEvent.click(within(openMenu('Model')).getByRole('menuitemradio', { name: 'Another model…' }));
        const name = await waitFor(() => screen.getByRole('textbox', { name: 'Model name' }));
        expect(document.activeElement).toBe(name);
        expect(choicesOf('Model', 'effort')).toEqual([]);
        // With the name empty the turn is not sent, and Enter sends nothing either.
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);
        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Your message' }), { key: 'Enter' });
        expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(false);
        fireEvent.change(name, { target: { value: ' claude-opus-5-5 ' } });
        expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', false);
        expect(currentOf('Model', 'model')).toBe('-another');
        expect(menuButton('Model').getAttribute('aria-label')).toBe('Model: claude-opus-5-5');
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
            'Go',
            { host: 'claude', model: 'claude-opus-5-5' },
        ]);
    });

    it('shows no model menu for a host that offers none here, and sends the turn with the host alone', async () => {
        const fake = pending(new FakeApi());
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, found: false }
                    : each.id === 'codex'
                      ? { ...each, found: true, version: '0.80.0', install: null, models: [] }
                      : each,
            ),
        });
        renderConsole(fake, desktop);
        await waitForMenus(() => expect(currentOf('Model', 'host')).toBe('codex'));
        expect(choicesOf('Model', 'model')).toEqual([]);
        expect(choicesOf('Model', 'effort')).toEqual([]);
        expect(menuButton('Model').getAttribute('aria-label')).toBe('Model: Codex default');
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
            'Go',
            { host: 'codex' },
        ]);
    });

    it('keeps Codex’s model control where its models could not be read, says so in one line, and sends the turn with the host alone', async () => {
        const fake = pending(new FakeApi());
        const sentence = "Codex's models could not be read, so the turn uses its own default model.";
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, found: false }
                    : each.id === 'codex'
                      ? { ...each, found: true, version: '0.80.0', install: null, models: [], modelsUnread: sentence }
                      : each,
            ),
        });
        renderConsole(fake, desktop);
        await waitFor(() => menuButton('Model'));
        expect(choicesOf('Model', 'model')).toEqual([
            { value: '', text: 'Default (recommended)', said: sentence, disabled: true, checked: true },
        ]);
        expect(document.querySelector('.composer .model-unread')?.textContent).toBe(sentence);
        expect(choicesOf('Model', 'effort')).toEqual([]);
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
            'Go',
            { host: 'codex' },
        ]);
    });

    it('sends a turn to the host that drives the chat, and draws what the agent says as text', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitForMenus(() => expect(currentOf('Model', 'host')).toBe('claude'));
        const hosts = choicesOf('Model', 'host');
        expect(hosts.find((each) => each.text === 'Gemini CLI')).toMatchObject({
            disabled: true,
            said: 'runs campaign work, not the chat yet',
        });
        expect(hosts.find((each) => each.text === 'Codex')).toBeUndefined();
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'What waits?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.find((each) => each.name === 'chat.send')).toBeTruthy());
        const sent = desktop.calls.find((each) => each.name === 'chat.send')!;
        expect(sent.args.slice(1)).toEqual(['What waits?', { host: 'claude' }]);
        const id = sent.args[0] as string;
        flush(() => {
            desktop.emit(id, { kind: 'step', text: 'Ran a jason command', detail: `jason approval list ${hostile}` });
            desktop.emit(id, {
                kind: 'step',
                text: 'Used a tool',
                detail: '<b onclick="window.hit=1">constructor</b>',
            });
            desktop.emit(id, { kind: 'text', text: hostile });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText(hostile));
        expect(document.querySelector('.thread img')).toBeNull();
        expect(screen.getByText('2 actions')).toBeTruthy();
        // Each step is its plain words, with what it acted on beneath them as text.
        const steps = [...document.querySelectorAll('.vstep')];
        expect(steps.map((step) => step.querySelector('.chat-step-text')?.textContent)).toEqual([
            'Ran a jason command',
            'Used a tool',
        ]);
        expect(steps.map((step) => step.querySelector('.chat-step-detail')?.textContent)).toEqual([
            `jason approval list ${hostile}`,
            '<b onclick="window.hit=1">constructor</b>',
        ]);
        expect(document.querySelector('.vsteps b, .vsteps img, .vsteps [onclick]')).toBeNull();
    });

    it('names the speaker over the agent’s first words of a turn, and marks the trail of steps live while the turn runs', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        const message = await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(message, { target: { value: 'What waits?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        flush(() => desktop.emit(id, { kind: 'step', text: 'Ran a jason command' }));
        // While the turn runs its trail is open and live, so it draws no arrow to turn.
        await waitFor(() => expect(document.querySelector('details.vsteps')?.classList.contains('live')).toBe(true));
        flush(() => {
            desktop.emit(id, { kind: 'text', text: 'Two approvals wait.' });
            desktop.emit(id, { kind: 'step', text: 'Read the approvals' });
            desktop.emit(id, { kind: 'text', text: 'Both are emails.' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText('Both are emails.'));
        // The person's words in their bubble; the agent's under one label for the turn.
        expect(document.querySelector('.thread .m.user .bub')?.textContent).toBe('What waits?');
        const label = (each: Element) => each.querySelector(':scope > .who')?.textContent ?? null;
        expect([...document.querySelectorAll('.thread .m.ai')].map(label)).toEqual(['Jason', null]);
        expect([...document.querySelectorAll('details.vsteps')].map((each) => each.className)).toEqual([
            'vsteps',
            'vsteps',
        ]);
        // The next turn is labelled again.
        fireEvent.change(message, { target: { value: 'Approve the first' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'chat.send')).toHaveLength(2));
        flush(() => {
            desktop.emit(id, { kind: 'text', text: 'It waits on your yes.' });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText('It waits on your yes.'));
        expect([...document.querySelectorAll('.thread .m.ai')].map(label)).toEqual(['Jason', null, 'Jason']);
    });

    it('shows the Windows check of Codex’s sandbox, whole and open, while the turn waits on it', async () => {
        const fake = pending(new FakeApi());
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'codex' ? { ...each, found: true, version: '0.167.0', install: null } : each,
            ),
        });
        renderConsole(fake, desktop);
        await waitFor(() => menuButton('Model'));
        chooseIn('Model', 'host', 'codex');
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'What waits?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.find((each) => each.name === 'chat.send')).toBeTruthy());
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        // The desktop's words, as `checkingSandbox` in desktop/src/codex-sandbox.ts says them.
        const checking =
            "Checking Codex's sandbox… Windows may ask once for an administrator; answering No means chatting on Claude Code instead.";
        flush(() => desktop.emit(id, { kind: 'step', text: checking }));
        await waitFor(() => expect(document.querySelector('.vstep .chat-step-text')?.textContent).toBe(checking));
        expect(document.querySelector('details.vsteps')?.hasAttribute('open')).toBe(true);
        // What a Codex command can reach stays said under the composer while the check runs.
        expect(screen.getByRole('note', { name: 'What a Codex command can reach' })).toBeTruthy();
    });

    it("draws the agent's Markdown as its subset, and everything else in it as text", async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Draft it' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        const reply = [
            '## The draft',
            '- first **point**',
            '> Hi Ana, a *quoted* line',
            '| Name | Stage |',
            '|---|---|',
            '| Ana | `sent` |',
            '```',
            hostile,
            '```',
            'See [the guide](javascript:window.hit=1) <script>window.hit=1</script>',
            '',
            '<iframe srcdoc="<script>parent.hit=1</script>"></iframe>',
            '',
            '> ```',
            '> <svg onload="window.hit=1"></svg>',
            '> ```',
            '',
            '| Cell |',
            '|---|',
            '| <b onclick="window.hit=1">bold</b> |',
        ].join('\n');
        flush(() => {
            desktop.emit(id, { kind: 'text', text: reply });
            desktop.emit(id, { kind: 'done', error: null });
        });
        const drawn = await waitFor(() => {
            const element = document.querySelector<HTMLElement>('.thread .chat-md');
            expect(element).toBeTruthy();
            return element!;
        });
        expect(within(drawn).getByRole('heading', { name: 'The draft', level: 2 })).toBeTruthy();
        expect(within(drawn).getByRole('listitem').textContent).toBe('first point');
        expect(drawn.querySelector('blockquote em')?.textContent).toBe('quoted');
        expect(within(drawn).getByRole('cell', { name: 'sent' }).querySelector('code')).toBeTruthy();
        expect(drawn.querySelector('pre code')?.textContent).toBe(hostile);
        expect(words(drawn)).toContain('See the guide (javascript:window.hit=1) <script>window.hit=1</script>');
        expect(words(drawn)).toContain('<iframe srcdoc="<script>parent.hit=1</script>"></iframe>');
        expect(drawn.querySelector('blockquote pre code')?.textContent).toBe('<svg onload="window.hit=1"></svg>');
        expect(within(drawn).getByRole('cell', { name: '<b onclick="window.hit=1">bold</b>' })).toBeTruthy();
        // The one svg a code block's Copy code button draws is its own icon, never the agent's: every other is refused.
        expect(drawn.querySelectorAll('a, b, img, script, iframe, [href], [src]')).toHaveLength(0);
        const icons = Array.from(drawn.querySelectorAll('svg'));
        expect(icons.every((icon) => icon.parentElement?.classList.contains('chat-md-copy') === true)).toBe(true);
        expect(drawn.querySelectorAll('.chat-md-copy')).toHaveLength(2);
        expect(icons).toHaveLength(2);
        for (const button of drawn.querySelectorAll('.chat-md-copy'))
            expect(button.querySelectorAll('svg')).toHaveLength(1);
        const handlers = Array.from(drawn.querySelectorAll('*')).flatMap((element) =>
            element.getAttributeNames().filter((name) => name.startsWith('on')),
        );
        expect(handlers).toEqual([]);
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    describe('a message typed while a turn runs', () => {
        const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
        const type = (text: string) => fireEvent.change(box(), { target: { value: text } });
        const enter = () => fireEvent.keyDown(box(), { key: 'Enter' });
        const escape = (init: { isComposing?: boolean } = {}) => fireEvent.keyDown(box(), { key: 'Escape', ...init });
        const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
        const stops = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.stop');
        const queued = () => screen.queryByRole('group', { name: 'Queued message' });
        const blank = String.fromCharCode(10, 10);
        /** The composer's round button, by its name: the one button of the row that sends or stops. */
        const round = () => Array.from(document.querySelectorAll<HTMLButtonElement>('.chat-panel .crow > .send'));

        /** A chat whose first turn, "Go", runs. */
        async function running(): Promise<{ desktop: StubDesktop; id: string }> {
            const { desktop } = renderConsole(pending(new FakeApi()));
            await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
            type('Go');
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(sends(desktop)).toHaveLength(1));
            return { desktop, id: sends(desktop)[0]!.args[0] as string };
        }

        it('has one round button: Stop with the box empty, the arrow that queues with words typed', async () => {
            const { desktop } = await running();
            // The box is empty: the turn is stopped by the button.
            expect(round().map((each) => each.getAttribute('aria-label'))).toEqual(['Stop']);
            expect(round()[0]!.querySelector('svg')).toBeTruthy();
            expect(box().placeholder).toBe('Sent when this turn ends · Esc stops it');
            // Words typed: the same place holds the arrow, which sends them to wait for the turn's end.
            type('And then?');
            const arrow = round();
            expect(arrow.map((each) => each.getAttribute('aria-label'))).toEqual(['Send when this turn ends']);
            expect(arrow[0]!.title).toBe('Send when this turn ends (Esc stops the turn)');
            expect(arrow[0]!.type).toBe('submit');
            expect(arrow[0]!.disabled).toBe(false);
            expect(screen.queryByRole('button', { name: 'Stop' })).toBeNull();
            // The text button and Replace are gone.
            expect(screen.queryByRole('button', { name: 'Send when the turn ends' })).toBeNull();
            expect(screen.queryByRole('button', { name: 'Replace' })).toBeNull();
            fireEvent.click(arrow[0]!);
            await waitFor(() => expect(queued()).toBeTruthy());
            expect(round().map((each) => each.getAttribute('aria-label'))).toEqual(['Stop']);
            expect(stops(desktop)).toHaveLength(0);
            // While the stop is asked for, words typed cannot be queued: the button stays Stop, and Enter keeps them.
            fireEvent.click(screen.getByRole('button', { name: 'Stop' }));
            type('More');
            expect(round().map((each) => [each.getAttribute('aria-label'), each.disabled])).toEqual([['Stop', false]]);
            enter();
            expect(box().value).toBe('More');
            expect(queued()).toBeNull();
        });

        it('is Stop, never an arrow that cannot send, while the model name is still to be typed', async () => {
            const { desktop } = await running();
            fireEvent.click(within(openMenu('Model')).getByRole('menuitemradio', { name: 'Another model…' }));
            await waitFor(() => screen.getByRole('textbox', { name: 'Model name' }));
            type('More');
            expect(round().map((each) => [each.getAttribute('aria-label'), each.disabled])).toEqual([['Stop', false]]);
            fireEvent.click(round()[0]!);
            expect(stops(desktop)).toHaveLength(1);
            expect(box().value).toBe('More');
        });

        it('is Stop, never an arrow that cannot send, when the host is gone while the turn runs', async () => {
            const { desktop } = await running();
            setup.value = { ...setup.value!, hosts: [] };
            type('More');
            await waitFor(() =>
                expect(round().map((each) => [each.getAttribute('aria-label'), each.disabled])).toEqual([
                    ['Stop', false],
                ]),
            );
            fireEvent.click(round()[0]!);
            expect(stops(desktop)).toHaveLength(1);
            expect(box().value).toBe('More');
        });

        it('says that a message is queued to a screen reader, in the queued line’s words', async () => {
            await running();
            const region = () => document.querySelector<HTMLElement>('.chat-panel .chat-announce')!;
            expect(region().getAttribute('role')).toBe('status');
            expect(region().textContent).toBe('');
            type('And then?');
            enter();
            await waitFor(() => expect(region().textContent).toBe('Queued · Jason takes it when this turn ends'));
            fireEvent.click(within(queued()!).getByRole('button', { name: 'Take back' }));
            await waitFor(() => expect(region().textContent).toBe(''));
        });

        it('is drawn in the conversation as a message sent, with a line that it is queued, and sent once when the turn ends', async () => {
            const { desktop, id } = await running();
            flush(() => desktop.emit(id, { kind: 'step', text: 'Ran jason status' }));
            const steps = await waitFor(() => {
                const element = document.querySelector('.thread details');
                expect(element).toBeTruthy();
                return element!;
            });
            type('And then?');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            const drawn = queued()!;
            // In the conversation, below the thread and above the box, drawn as the person's message is.
            expect(drawn.closest('.chat-scroll')).toBeTruthy();
            expect(drawn.querySelector('.m.user .bub')?.textContent).toBe('And then?');
            expect(drawn.querySelector('.queued-line')?.textContent).toContain(
                'Queued · Jason takes it when this turn ends',
            );
            expect(document.querySelector('.composer .chat-waiting')).toBeNull();
            expect(box().value).toBe('');
            // Typing does not draw the thread again.
            type('Third');
            expect(document.querySelector('.thread details')).toBe(steps);
            expect(sends(desktop)).toHaveLength(1);
            flush(() => {
                desktop.emit(id, { kind: 'done', error: null });
                desktop.emit(id, { kind: 'done', error: null });
            });
            await waitFor(() => expect(sends(desktop)).toHaveLength(2));
            expect(sends(desktop)[1]!.args).toEqual([id, 'And then?', { host: 'claude' }]);
            expect(queued()).toBeNull();
            expect(box().value).toBe('Third');
            expect(Array.from(document.querySelectorAll('.thread .m.user'), (each) => each.textContent)).toEqual([
                'Go',
                'And then?',
            ]);
        });

        it('adds a second message under the first, a blank line between, and both go as one turn', async () => {
            const { desktop, id } = await running();
            type('First');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            type('Second');
            fireEvent.click(screen.getByRole('button', { name: 'Send when this turn ends' }));
            await waitFor(() =>
                expect(queued()!.querySelector('.bub')!.textContent).toBe(['First', 'Second'].join(blank)),
            );
            expect(screen.getAllByRole('group', { name: 'Queued message' })).toHaveLength(1);
            expect(box().value).toBe('');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(sends(desktop)).toHaveLength(2));
            expect(sends(desktop)[1]!.args).toEqual([id, ['First', 'Second'].join(blank), { host: 'claude' }]);
        });

        it('is taken back only into an empty box', async () => {
            const { desktop, id } = await running();
            type('First');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            type('Typed');
            const takeBack = within(queued()!).getByRole('button', { name: 'Take back' });
            expect(takeBack).toHaveProperty('disabled', true);
            expect(takeBack.title).toBe('Empty the box to take it back');
            type('');
            fireEvent.click(within(queued()!).getByRole('button', { name: 'Take back' }));
            await waitFor(() => expect(queued()).toBeNull());
            expect(box().value).toBe('First');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(chats.value[id]!.running).toBe(false));
            expect(sends(desktop)).toHaveLength(1);
        });

        it('goes back to the box the moment the person stops the turn, and the end sends nothing', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            fireEvent.click(screen.getByRole('button', { name: 'Stop' }));
            expect(stops(desktop)).toHaveLength(1);
            await waitFor(() => expect(box().value).toBe('Later'));
            expect(queued()).toBeNull();
            // The person types again before the stopped turn's end arrives.
            type('Something else');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(chats.value[id]!.running).toBe(false));
            expect(box().value).toBe('Something else');
            expect(queued()).toBeNull();
            expect(sends(desktop)).toHaveLength(1);
        });

        it('is stopped by Escape in the box with words typed, which stay, and what waited comes back above them', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            type('More');
            // A key the input method still composes is its own.
            escape({ isComposing: true });
            expect(stops(desktop)).toHaveLength(0);
            escape();
            expect(stops(desktop)).toHaveLength(1);
            await waitFor(() => expect(box().value).toBe(['Later', 'More'].join(blank)));
            // The box is the one place the unsent words are: nothing in the conversation says they are queued.
            expect(queued()).toBeNull();
            expect(document.querySelector('.chat-scroll')!.textContent).not.toMatch(/Queued|Not sent/);
            expect(screen.queryByText(/Not sent/)).toBeNull();
            // A stop already asked for is asked once.
            escape();
            expect(stops(desktop)).toHaveLength(1);
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(chats.value[id]!.running).toBe(false));
            expect(box().value).toBe(['Later', 'More'].join(blank));
            expect(sends(desktop)).toHaveLength(1);
            // With no turn running Escape does nothing.
            escape();
            expect(stops(desktop)).toHaveLength(1);
        });

        it('stops by Escape while a dialog stands open only in a pane that is hidden, as panes not shown are', async () => {
            const { desktop } = await running();
            type('More');
            const pane = document.createElement('section');
            pane.hidden = true;
            pane.innerHTML = '<div role="alertdialog" aria-label="Reject this?"></div>';
            document.querySelector('.jc-console')!.append(pane);
            escape();
            expect(stops(desktop)).toHaveLength(1);
            expect(box().value).toBe('More');
            pane.remove();
        });

        it('leaves the turn running while a dialog is shown, which takes the Escape', async () => {
            const { desktop } = await running();
            type('More');
            const pane = document.createElement('section');
            pane.innerHTML = '<div role="alertdialog" aria-label="Reject this?"></div>';
            document.querySelector('.jc-console')!.append(pane);
            escape();
            expect(stops(desktop)).toHaveLength(0);
            pane.remove();
            escape();
            expect(stops(desktop)).toHaveLength(1);
        });

        it('leaves the turn running when Escape closes a menu open while the focus is in the box', async () => {
            const { desktop, id } = await running();
            type('More');
            const side = within(document.querySelector<HTMLElement>('.side')!);
            fireEvent.click(side.getByRole('button', { name: 'Filter' }));
            await waitFor(() => side.getByRole('menu', { name: 'Show' }));
            box().focus();
            expect(document.activeElement).toBe(box());
            escape();
            await waitFor(() => expect(side.queryByRole('menu', { name: 'Show' })).toBeNull());
            expect(stops(desktop)).toHaveLength(0);
            expect(chats.value[id]!.running).toBe(true);
            expect(box().value).toBe('More');
            // With nothing else open, the next Escape is the box's.
            escape();
            expect(stops(desktop)).toHaveLength(1);
            expect(box().value).toBe('More');
        });

        it('ends as a turn that could not start when no host is left to send it to, and goes back to the box', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            setup.value = { ...setup.value!, hosts: [] };
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() =>
                expect(document.querySelector('.chat-error')?.textContent).toContain(
                    'No AI host that drives the chat was found on this machine.',
                ),
            );
            expect(sends(desktop)).toHaveLength(1);
            expect(queued()).toBeNull();
            expect(box().value).toBe('Later');
            expect(chats.value[id]!.running).toBe(false);
            expect(Array.from(document.querySelectorAll('.thread .m.user'), (each) => each.textContent)).toEqual([
                'Go',
                'Later',
            ]);
        });

        it('ends with the desktop sentence that holds the host when the only host that drives the chat is held', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            const unreadable = `The version of Claude Code could not be read, so the chat does not run on it. Run \`claude --version\` in a terminal to see it. ${hostile}`;
            setup.value = {
                ...setup.value!,
                hosts: setup.value!.hosts.map((each) =>
                    each.id === 'claude' ? { ...each, version: null, belowFloor: unreadable } : each,
                ),
            };
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(document.querySelector('.chat-error')?.textContent).toContain(unreadable));
            expect(document.querySelector('.chat-error')?.textContent).not.toContain('No AI host');
            expect(document.querySelector('.chat-error img')).toBeNull();
            expect(sends(desktop)).toHaveLength(1);
            expect(box().value).toBe('Later');
        });

        it('comes back above what is typed when the turn fails while the box holds other words', async () => {
            const { desktop, id } = await running();
            type('Then this');
            enter();
            await waitFor(() => expect(queued()).toBeTruthy());
            type('And more');
            flush(() => desktop.emit(id, { kind: 'done', error: `Not logged in ${hostile}` }));
            await waitFor(() => expect(box().value).toBe(['Then this', 'And more'].join(blank)));
            expect(queued()).toBeNull();
            expect(screen.queryByText(/Not sent/)).toBeNull();
            expect(sends(desktop)).toHaveLength(1);
            expect(document.querySelector('.chat-error')!.textContent).toContain(`Not logged in ${hostile}`);
        });
    });

    it('answers a permission once, and says so when the desktop no longer holds it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        flush(() => {
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_1',
                title: 'Run a command',
                detail: 'git status',
                remember: 'git status \u2026',
            });
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_2',
                title: 'Write a file',
                detail: 'notes.md',
                remember: null,
            });
        });
        await waitFor(() => screen.getByText('Write a file'));
        // The "always" button says what it grants, and a card that can only cover itself has none.
        expect(screen.getAllByRole('button', { name: /^Allow for this chat/ })).toHaveLength(1);
        fireEvent.click(screen.getByRole('button', { name: 'Allow for this chat: git status \u2026' }));
        await waitFor(() => screen.getByText('Allowed for this chat'));
        expect(desktop.calls.find((each) => each.name === 'chat.answer')!.args).toEqual([
            id,
            'req_1',
            { allow: true, always: true },
        ]);
        flush(() => desktop.emit(id, { kind: 'permission_gone', request: 'req_2' }));
        await waitFor(() => screen.getByText('This request has ended'));
        expect(screen.queryByRole('button', { name: 'Allow' })).toBeNull();
    });

    it('says under a Full access card why it asks, as text, and nothing under a card of any other mode', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        flush(() => {
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_1',
                title: 'Full access asks about this one',
                detail: 'ls *.md',
                remember: null,
                why: { reason: 'command.glob' },
            });
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_2',
                title: 'Full access asks about this one',
                detail: '{"x":"<b>y</b>"}',
                remember: null,
                why: { reason: 'request.unknown_tool', tool: '<b>Frob</b>' },
            });
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_3',
                title: 'Run a command',
                detail: 'git status',
                remember: null,
            });
        });
        await waitFor(() => screen.getByText('Run a command'));
        const cards = [...document.querySelectorAll<HTMLElement>('.ask.perm')];
        expect(cards.map((card) => card.querySelector('.perm-why')?.textContent ?? null)).toEqual([
            'This command has a wildcard (*, ? or [ ]), which the shell turns into file names only as it runs, so Jason cannot read plainly what it would touch.',
            'Jason does not know the tool <b>Frob</b>, so it cannot tell what it would do.',
            null,
        ]);
        // The line sits between the title and the request, and a tool's name in it is text, never markup.
        expect(cards[0]!.querySelector('.ask-h + .perm-why + .perm-detail')).toBeTruthy();
        expect(cards[1]!.querySelector('.perm-why b')).toBeNull();
    });

    it('draws a Full access card whose why this page has no words for exactly as before: no line, the card and its buttons', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        // A code a newer desktop names, and values not in the declared shape, as they could come over the seam.
        const unknown = [
            { reason: 'command.not_known_here' },
            { reason: 'request.unknown_tool' },
            { reason: 'command.glob', tool: 'Bash' },
            { reason: 7 },
            'command.glob',
        ] as unknown as DesktopFullAccessWhy[];
        flush(() => {
            unknown.forEach((why, at) =>
                desktop.emit(id, {
                    kind: 'permission',
                    request: `req_${at + 1}`,
                    title: 'Full access asks about this one',
                    detail: `ls ${at}`,
                    remember: null,
                    why,
                }),
            );
        });
        await waitFor(() => expect(document.querySelectorAll('.ask.perm')).toHaveLength(unknown.length));
        for (const card of document.querySelectorAll<HTMLElement>('.ask.perm')) {
            expect(card.querySelector('.perm-why')).toBeNull();
            expect(card.querySelector('.ask-h + .perm-detail')).toBeTruthy();
            expect(within(card).getByRole('button', { name: 'Allow' })).toBeTruthy();
            expect(within(card).getByRole('button', { name: "Don't allow" })).toBeTruthy();
        }
    });

    it('says a turn parked on a permission card waits for the person, and thinks again once it is answered', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        expect(screen.getByText('Thinking…')).toBeTruthy();
        flush(() => {
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_1',
                title: 'Run a command',
                detail: 'jason x',
                remember: null,
            });
        });
        await waitFor(() => screen.getByText('Run a command'));
        const line = document.querySelector<HTMLElement>('.typing')!;
        expect(line.textContent!.trim()).toBe('Waiting for your answer on the card above.');
        expect(line.querySelector('.spin')).toBeNull();
        expect(screen.queryByText('Thinking…')).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Allow' }));
        await waitFor(() => screen.getByText('Thinking…'));
        expect(screen.queryByText(/Waiting for your answer/)).toBeNull();
    });

    it('brings a sent message into view at the foot of the conversation, and leaves a merely opened thread alone', async () => {
        // A thread 900 px high in a view of 500, with nothing under it: its foot is the box's, 400 down.
        const layout = layOutChats(500, 4, (part) => (part.classList.contains('thread') ? 900 : undefined));
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
            const box = () => document.querySelector<HTMLElement>('.chat-scroll')!;
            expect(box().scrollTop).toBe(0);
            fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(box().scrollTop).toBe(400));
            expect(fromFoot(box())).toBe(0);
            expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true);
        } finally {
            layout.restore();
        }
    });

    it('follows a new permission card at the foot, even in the update that answers the one before it', async () => {
        const layout = layOutChats();
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            const id = await sendFromFoot(desktop, 'Go');
            flush(() =>
                desktop.emit(id, {
                    kind: 'permission',
                    request: 'req_1',
                    title: 'Run a command',
                    detail: 'a'.repeat(300),
                    remember: null,
                }),
            );
            await waitFor(() => expect(thread().textContent).toContain('Run a command'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            // The first card ends and a second one arrives in one update: the count of open cards is the same.
            flush(() => {
                desktop.emit(id, { kind: 'permission_gone', request: 'req_1' });
                desktop.emit(id, {
                    kind: 'permission',
                    request: 'req_2',
                    title: 'Write a file',
                    detail: 'b'.repeat(600),
                    remember: null,
                });
            });
            await waitFor(() => expect(thread().textContent).toContain('Write a file'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
        } finally {
            layout.restore();
        }
    });

    it('takes a permission card that asks to the foot from wherever the person reads, as a message they send', async () => {
        const layout = layOutChats();
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            const id = await sendFromFoot(desktop, 'Go');
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 1: ${'words '.repeat(200)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 1:'));
            // Reading from the top, well above the measure: the card that asks is the turn waiting for them.
            scrollAs(thread(), 0);
            flush(() =>
                desktop.emit(id, {
                    kind: 'permission',
                    request: 'req_1',
                    title: 'Run a command',
                    detail: 'd'.repeat(300),
                    remember: null,
                }),
            );
            await waitFor(() => expect(thread().textContent).toContain('Run a command'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            expect(thread().scrollTop).toBeGreaterThan(0);
        } finally {
            layout.restore();
        }
    });

    it('follows the AI’s words and a card as they come while the person stays at the foot', async () => {
        const layout = layOutChats();
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            const id = await sendFromFoot(desktop, 'Go');
            for (const part of [1, 2, 3]) {
                flush(() => desktop.emit(id, { kind: 'text', text: `Part ${part}: ${'words '.repeat(80)}` }));
                await waitFor(() => expect(thread().textContent).toContain(`Part ${part}:`));
                await waitFor(() => expect(fromFoot(thread())).toBe(0));
            }
            // The answer is taller than the view: the thread moved to keep its foot in view.
            expect(thread().scrollTop).toBeGreaterThan(1000);
            flush(() =>
                desktop.emit(id, {
                    kind: 'permission',
                    request: 'req_1',
                    title: 'Run a command',
                    detail: 'c'.repeat(300),
                    remember: null,
                }),
            );
            await waitFor(() => expect(thread().textContent).toContain('Run a command'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
        } finally {
            layout.restore();
        }
    });

    it('keeps the person’s place once they scroll more than 120 px above the foot, and follows again once they are back', async () => {
        const layout = layOutChats();
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            const id = await sendFromFoot(desktop, 'Go');
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 1: ${'words '.repeat(200)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 1:'));
            scrollAs(thread(), thread().scrollHeight);
            // 120 px above the foot is still at it: what comes is followed.
            scrollAs(thread(), thread().scrollTop - 120);
            expect(fromFoot(thread())).toBe(120);
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 2: ${'words '.repeat(80)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 2:'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            // One pixel further and the place is the person's: the AI's words do not move it.
            scrollAs(thread(), thread().scrollTop - 121);
            const kept = thread().scrollTop;
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 3: ${'words '.repeat(80)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 3:'));
            await settle();
            expect(thread().scrollTop).toBe(kept);
            expect(fromFoot(thread())).toBeGreaterThan(120);
            // Back at the foot, the thread follows again.
            scrollAs(thread(), thread().scrollHeight);
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 4: ${'words '.repeat(80)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 4:'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
        } finally {
            layout.restore();
        }
    });

    it('takes a message the person sends from further up to the foot, one that waits for the turn included, and follows from there', async () => {
        const layout = layOutChats();
        try {
            const fake = pending(new FakeApi());
            const { desktop } = renderConsole(fake);
            const id = await sendFromFoot(desktop, 'Go');
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 1: ${'words '.repeat(200)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 1:'));
            // Read from the top while the turn runs; what is sent then waits for the turn, and goes to the foot.
            scrollAs(thread(), 0);
            fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'And then' } });
            fireEvent.keyDown(screen.getByRole('textbox', { name: 'Your message' }), { key: 'Enter' });
            await waitFor(() => expect(thread().textContent).toContain('And then'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            flush(() => desktop.emit(id, { kind: 'text', text: `Part 2: ${'words '.repeat(80)}` }));
            await waitFor(() => expect(thread().textContent).toContain('Part 2:'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            // The turn ends, the waiting message is sent and answered, and the person reads from the top again; the
            // next message goes to the foot as well.
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'chat.send')).toHaveLength(2));
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(screen.queryByText('Thinking…')).toBeNull());
            scrollAs(thread(), 0);
            fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Last one' } });
            fireEvent.keyDown(screen.getByRole('textbox', { name: 'Your message' }), { key: 'Enter' });
            await waitFor(() => expect(thread().textContent).toContain('Last one'));
            await waitFor(() => expect(fromFoot(thread())).toBe(0));
            expect(thread().scrollTop).toBeGreaterThan(0);
        } finally {
            layout.restore();
        }
    });

    it('shows a failed sign-in in the host own words, with how to sign in under it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        const id = desktop.calls.find((each) => each.name === 'chat.send')!.args[0] as string;
        flush(() => desktop.emit(id, { kind: 'done', error: `Not logged in ${hostile}` }));
        await waitFor(() => screen.getByText(`Not logged in ${hostile}`));
        const error = document.querySelector<HTMLElement>('.chat-error')!;
        expect(error.querySelector('img')).toBeNull();
        expect(error.textContent).toContain(
            'Sign in once by running claude in a terminal; the chat uses that sign-in.',
        );
    });

    describe('a Claude Code below the floor', () => {
        const old = (extra = '') =>
            `Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running \`claude update\` in a terminal, then check again.${extra}`;
        const withClaude = (belowFloor: string | null, codexFound: boolean) => ({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, version: belowFloor === null ? '2.1.300' : '2.1.200', belowFloor }
                    : each.id === 'codex'
                      ? { ...each, found: codexFound, version: codexFound ? '0.80.0' : null, install: null }
                      : each,
            ),
        });

        it('is listed with the desktop sentence and cannot be chosen, and the turn goes to a host that drives the chat', async () => {
            const fake = pending(new FakeApi());
            const desktop = chatDesktop(withClaude(old(), true));
            renderConsole(fake, desktop);
            await waitForMenus(() => expect(currentOf('Model', 'host')).toBe('codex'));
            expect(choicesOf('Model', 'host').find((each) => each.value === 'claude')).toMatchObject({
                text: 'Claude Code',
                disabled: true,
                said: 'Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running claude update in a terminal, then check again.',
            });
            fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
            expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
                'Go',
                { host: 'codex' },
            ]);
        });

        it('as the only host that drives the chat, says why no turn starts, as text, and checks again on a click', async () => {
            const fake = pending(new FakeApi());
            const desktop = chatDesktop(withClaude(old(` ${hostile}`), false));
            renderConsole(fake, desktop);
            await waitFor(() => expect(document.querySelector('.composer .host-held')).toBeTruthy());
            const note = document.querySelector<HTMLElement>('.composer .host-held')!;
            expect(words(note)).toContain(
                'Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running claude update in a terminal, then check again.',
            );
            expect(note.querySelector('code')?.textContent).toBe('claude update');
            expect(note.textContent).toContain(hostile);
            expect(note.querySelector('img')).toBeNull();
            expect(document.querySelector('.composer')?.textContent).not.toContain(
                'No AI host that drives the chat was found',
            );
            expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);

            const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
            desktop.status = withClaude(null, false);
            fireEvent.click(within(note).getByRole('button', { name: 'Check again' }));
            await waitFor(() =>
                expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(read + 1),
            );
            await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', false));
            expect(currentOf('Model', 'host')).toBe('claude');
            expect(document.querySelector('.composer .host-held')).toBeNull();
        });

        describe('held under a chat whose host it is, while another host drives the chat', () => {
            const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
            const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
            const hostChosen = () => currentOf('Model', 'host');
            const unreadable =
                'The version of Claude Code could not be read, so the chat does not run on it. Run `claude --version` in a terminal to see it; the chat needs 2.1.257 or newer, and `claude update` updates it. Then check again.';

            /** A chat whose first turn ran on Claude Code, with Codex found too, and then Claude Code held by `sentence`. */
            async function heldAfterATurn(sentence: string): Promise<{ desktop: StubDesktop; id: string }> {
                const desktop = chatDesktop(withClaude(null, true));
                renderConsole(pending(new FakeApi()), desktop);
                await waitForMenus(() => expect(hostChosen()).toBe('claude'));
                fireEvent.change(box(), { target: { value: 'Go' } });
                fireEvent.click(screen.getByRole('button', { name: 'Send' }));
                await waitFor(() => expect(sends(desktop)).toHaveLength(1));
                const id = sends(desktop)[0]!.args[0] as string;
                expect(sends(desktop)[0]!.args.slice(1)).toEqual(['Go', { host: 'claude' }]);
                flush(() => desktop.emit(id, { kind: 'done', error: null }));
                await waitFor(() => expect(chats.value[id]!.running).toBe(false));
                const held = withClaude(sentence, true);
                desktop.status = held;
                flush(() => {
                    setup.value = { ...setup.value!, hosts: held.hosts.map((each) => ({ ...each })) };
                });
                return { desktop, id };
            }

            for (const [name, sentence, said] of [
                [
                    'below the floor',
                    old(),
                    'Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running claude update in a terminal, then check again.',
                ],
                [
                    'with a version that could not be read',
                    unreadable,
                    'The version of Claude Code could not be read, so the chat does not run on it. Run claude --version in a terminal to see it; the chat needs 2.1.257 or newer, and claude update updates it. Then check again.',
                ],
            ] as const) {
                it(`${name}: runs no turn on another host, and shows the note with Check again`, async () => {
                    const { desktop } = await heldAfterATurn(sentence);
                    const note = await waitFor(() => {
                        const element = document.querySelector<HTMLElement>('.composer .host-held');
                        expect(element).toBeTruthy();
                        return element!;
                    });
                    expect(words(note)).toContain(said);
                    expect(within(note).getByRole('button', { name: 'Check again' })).toBeTruthy();
                    // The menu still names the chat's own host; nothing moved it to Codex.
                    expect(hostChosen()).toBe('claude');
                    expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);
                    fireEvent.change(box(), { target: { value: 'Next' } });
                    fireEvent.keyDown(box(), { key: 'Enter' });
                    fireEvent.click(screen.getByRole('button', { name: 'Send' }));
                    expect(sends(desktop)).toHaveLength(1);
                    expect(box().value).toBe('Next');
                });
            }

            it("after Check again finds it updated, runs the turn on the chat's own host", async () => {
                const { desktop } = await heldAfterATurn(old());
                const note = await waitFor(() => {
                    const element = document.querySelector<HTMLElement>('.composer .host-held');
                    expect(element).toBeTruthy();
                    return element!;
                });
                desktop.status = withClaude(null, true);
                fireEvent.click(within(note).getByRole('button', { name: 'Check again' }));
                await waitFor(() =>
                    expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', false),
                );
                expect(document.querySelector('.composer .host-held')).toBeNull();
                expect(hostChosen()).toBe('claude');
                fireEvent.change(box(), { target: { value: 'Next' } });
                fireEvent.click(screen.getByRole('button', { name: 'Send' }));
                await waitFor(() => expect(sends(desktop)).toHaveLength(2));
                expect(sends(desktop)[1]!.args.slice(1)).toEqual(['Next', { host: 'claude' }]);
            });

            it('runs the turn on another host the person chooses', async () => {
                const { desktop } = await heldAfterATurn(old());
                await waitFor(() => expect(document.querySelector('.composer .host-held')).toBeTruthy());
                chooseIn('Model', 'host', 'codex');
                await waitForMenus(() => expect(hostChosen()).toBe('codex'));
                expect(document.querySelector('.composer .host-held')).toBeNull();
                fireEvent.change(box(), { target: { value: 'Next' } });
                fireEvent.click(screen.getByRole('button', { name: 'Send' }));
                await waitFor(() => expect(sends(desktop)).toHaveLength(2));
                expect(sends(desktop)[1]!.args.slice(1)).toEqual(['Next', { host: 'codex' }]);
            });
        });

        it('is marked on the Connect card with the same sentence, its command as code', async () => {
            const fake = pending(new FakeApi());
            renderConsole(fake, chatDesktop(withClaude(old(), false)));
            await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
            show('Connect');
            const connect = await waitFor(() => panel('Connect'));
            const row = within(connect).getByText('Claude Code', { selector: 'b' }).closest<HTMLElement>('.setrow')!;
            expect(words(row)).toContain(
                'Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running claude update in a terminal, then check again.',
            );
            expect(Array.from(row.querySelectorAll('code'), (each) => each.textContent)).toContain('claude update');
        });
    });

    it('connects: lists the hosts found or not, installs one, and configures a plugin through the desktop', async () => {
        const fake = pending(new FakeApi());
        const desktop = chatDesktop();
        renderConsole(fake, desktop);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        const connect = await waitFor(() => panel('Connect'));
        expect(within(connect).getByText('Drives the chat')).toBeTruthy();
        expect(within(connect).getByText('Runs campaign work, not the chat yet')).toBeTruthy();
        expect(within(connect).getByText('Not found')).toBeTruthy();
        expect(connect.textContent).toContain(
            'Sign in once by running claude in a terminal; the chat uses that sign-in.',
        );
        fireEvent.click(within(connect).getByRole('button', { name: 'Install' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.installProgram')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'setup.installProgram')!.args).toEqual(['codex']);
        await waitFor(() => within(panel('Connect')).getByLabelText('Bot token'));
        const plugins = within(panel('Connect'));
        expect(plugins.getByText('Plugins')).toBeTruthy();
        expect(plugins.getAllByText('Configured')).toHaveLength(2);
        expect(plugins.getByText('Not configured')).toBeTruthy();
        expect(panel('Connect').textContent).toContain('The runtime reads it as valid, with 1 grant.');
        expect(panel('Connect').textContent).toContain(
            'The runtime does not take its package or configuration as they stand.',
        );
        expect(panel('Connect').textContent).toContain('It has no binding yet: set its chat id.');
        expect(panel('Connect').textContent).toContain(setupFound.plugins[1]!.note!);
        // A plugin is configured, never said to be connected; the word is the Reply card's, for the state Reply reports.
        const pluginsCard = plugins.getByText('Plugins', { selector: 'b' }).closest<HTMLElement>('.card')!;
        expect(pluginsCard.textContent).not.toMatch(/connected/i);
        expect(plugins.queryByRole('button', { name: /test/i })).toBeNull();
        fireEvent.input(plugins.getByLabelText('Bot token'), { target: { value: ' 123:abc ' } });
        fireEvent.input(plugins.getByLabelText('Chat id'), { target: { value: ' -1001 ' } });
        const telegram = panel('Connect').querySelector<HTMLElement>('.plugin[data-id="telegram"]')!;
        const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
        fireEvent.click(within(telegram).getByRole('button', { name: 'Configure' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.configurePlugin')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'setup.configurePlugin')!.args).toEqual([
            'telegram',
            { token: '123:abc', target: '-1001' },
        ]);
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(read + 1));
    });

    describe('the person’s own MCP servers', () => {
        const withServers = (claude: string[], codex: string[], codexFound = true) => ({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, ownServers: claude }
                    : each.id === 'codex'
                      ? {
                            ...each,
                            found: codexFound,
                            version: codexFound ? '0.130.0' : null,
                            install: null,
                            ownServers: codex,
                        }
                      : each,
            ),
        });
        const rowOf = (connect: HTMLElement, label: string) =>
            within(connect).getByText(label, { selector: 'b' }).closest<HTMLElement>('.setrow')!;
        const openConnect = async (desktop: StubDesktop) => {
            renderConsole(pending(new FakeApi()), desktop);
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            show('Connect');
            return waitFor(() => panel('Connect'));
        };

        it('names them in one line under each host that has them: one, five, and the first five then how many more', async () => {
            const connect = await openConnect(
                chatDesktop(withServers(['github'], ['one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight'])),
            );
            expect(rowOf(connect, 'Claude Code').querySelector('.own-servers')?.textContent).toBe(
                'Your MCP servers: github. For now they work when you open Jason in Claude Code, from Open in.',
            );
            expect(rowOf(connect, 'Codex').querySelector('.own-servers')?.textContent).toBe(
                'Your MCP servers: one, two, three, four, five and 3 more. For now they work when you open Jason in Codex, from Open in.',
            );
            cleanup();
            const five = await openConnect(
                chatDesktop(withServers(['github', 'linear', 'sentry', 'notion', 'figma'], [])),
            );
            expect(rowOf(five, 'Claude Code').querySelector('.own-servers')?.textContent).toBe(
                'Your MCP servers: github, linear, sentry, notion, figma. For now they work when you open Jason in Claude Code, from Open in.',
            );
            expect(rowOf(five, 'Codex').querySelector('.own-servers')).toBeNull();
        });

        it('says nothing at all for a host with none, a host not found, or another host', async () => {
            const connect = await openConnect(chatDesktop(withServers([], ['kept'], false)));
            expect(connect.querySelectorAll('.own-servers')).toHaveLength(0);
            expect(connect.textContent).not.toContain('MCP');
        });

        it('are read by the Connect row alone: no other source of the page names the field', () => {
            const readers = (dir: string): string[] =>
                readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
                    const path = `${dir}/${entry.name}`;
                    if (entry.isDirectory()) return entry.name === 'test' ? [] : readers(path);
                    return /\.tsx?$/.test(entry.name) &&
                        !/\.test\.tsx?$/.test(entry.name) &&
                        !entry.name.endsWith('.d.ts')
                        ? [path]
                        : [];
                });
            // The run starts in ui/. The words' keys (`connect.ownServers`) are no read of the field.
            const naming = readers('src').filter((path) => /(?<!connect\.)ownServers/.test(readFileSync(path, 'utf8')));
            expect(naming).toEqual(['src/console/connect.ts']);
        });

        it('draws a name as text, and gives none to the chat', async () => {
            const desktop = chatDesktop(withServers([hostile, 'made-up-server'], []));
            renderConsole(pending(new FakeApi()), desktop);
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.status')).toBe(true));
            fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
            const toChat = JSON.stringify(desktop.calls.filter((each) => each.name.startsWith('chat.')));
            expect(toChat).not.toContain('made-up-server');
            expect(toChat).not.toContain('onerror');
            show('Connect');
            const connect = await waitFor(() => panel('Connect'));
            const line = rowOf(connect, 'Claude Code').querySelector('.own-servers')!;
            expect(line.textContent).toContain(`${hostile}, made-up-server`);
            expect(line.querySelector('img')).toBeNull();
            expect((window as { hit?: number }).hit).toBeUndefined();
        });
    });

    describe("the line that says the agents' roles run on a host", () => {
        const withRoles = (claude: string | null, codex: string | null) => ({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'claude'
                    ? { ...each, runsRoles: claude }
                    : each.id === 'codex'
                      ? { ...each, found: true, version: '0.130.0', install: null, runsRoles: codex }
                      : each,
            ),
        });
        const rowOf = (connect: HTMLElement, label: string) =>
            within(connect).getByText(label, { selector: 'b' }).closest<HTMLElement>('.setrow')!;
        const openConnect = async (desktop: StubDesktop) => {
            renderConsole(pending(new FakeApi()), desktop);
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            show('Connect');
            return waitFor(() => panel('Connect'));
        };

        it('is one line under the host the default profile runs on, naming the profile, and under no other', async () => {
            const connect = await openConnect(chatDesktop(withRoles('claude-code', null)));
            expect(rowOf(connect, 'Claude Code').querySelector('.runs-roles')?.textContent).toBe(
                "Your agents' roles run on Claude Code, by the execution profile claude-code.",
            );
            expect(rowOf(connect, 'Codex').querySelector('.runs-roles')).toBeNull();
            expect(connect.querySelectorAll('.runs-roles')).toHaveLength(1);
        });

        it('is not drawn under a Claude Code below its floor, which the chat cannot use', async () => {
            const below = withRoles('claude-code', null);
            const held = {
                ...below,
                hosts: below.hosts.map((each) =>
                    each.id === 'claude'
                        ? {
                              ...each,
                              version: '2.1.200',
                              belowFloor: 'Claude Code 2.1.200 is older than the chat needs.',
                          }
                        : each,
                ),
            };
            const connect = await openConnect(chatDesktop(held));
            expect(connect.querySelectorAll('.runs-roles')).toHaveLength(0);
        });

        it('says nothing where no host runs them', async () => {
            const connect = await openConnect(chatDesktop(withRoles(null, null)));
            expect(connect.querySelectorAll('.runs-roles')).toHaveLength(0);
            expect(connect.textContent).not.toContain('roles run on');
        });
    });

    it("says under the versions line, as text, that the runtime is not the bundle's version", async () => {
        const sentence = `The runtime is 0.33.0 and this desktop carries 0.34.0: run jason update. ${hostile}`;
        renderConsole(
            pending(new FakeApi()),
            chatDesktop({ ...setupFound, runtimeVersion: '0.33.0', versionsDiffer: sentence }),
        );
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        const connect = await waitFor(() => panel('Connect'));
        const versions = await waitFor(() => connect.querySelector<HTMLElement>('.versions')!);
        const under = versions.nextElementSibling!;
        expect(under.classList.contains('versions-differ')).toBe(true);
        expect(under.textContent!.trim()).toBe(sentence);
        expect(connect.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it("draws nothing under the versions line when the runtime is the bundle's version", async () => {
        renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        const connect = await waitFor(() => panel('Connect'));
        await waitFor(() => connect.querySelector('.versions')!);
        expect(connect.querySelector('.versions-differ')).toBeNull();
    });

    it('refuses to configure a plugin over a settings file it cannot read, and reloads with no values on Reload', async () => {
        const fake = pending(new FakeApi());
        const reason = `The settings file is not valid JSON at line 3. ${hostile}`;
        const desktop = chatDesktop({ ...setupFound, settingsRefused: reason });
        renderConsole(fake, desktop);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        const connect = await waitFor(() => panel('Connect'));
        await waitFor(() => within(connect).getByRole('alert'));
        const alert = within(connect).getByRole('alert');
        expect(alert.textContent).toContain(`${reason} Edit it by hand, then press Reload.`);
        expect(alert.querySelector('img')).toBeNull();
        expect(within(connect).queryByRole('button', { name: 'Configure' })).toBeNull();
        expect(within(connect).queryByLabelText('Bot token')).toBeNull();
        fireEvent.click(within(alert).getByRole('button', { name: 'Reload' }));
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(2));
        expect(desktop.calls.filter((each) => each.name === 'setup.configurePlugin').map((each) => each.args)).toEqual([
            ['telegram', {}],
        ]);
    });

    it('draws a server field for ntfy alone, sends it only when filled, and removes it by an act of its own', async () => {
        const fake = pending(new FakeApi());
        const desktop = chatDesktop();
        renderConsole(fake, desktop);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        await waitFor(() => within(panel('Connect')).getByLabelText('Topic'));
        const card = (id: string) => panel('Connect').querySelector<HTMLElement>(`.plugin[data-id="${id}"]`)!;
        expect(within(card('ntfy')).getByLabelText('Server (optional)')).toBeTruthy();
        expect(within(card('ntfy')).queryByLabelText(/token/i)).toBeNull();
        expect(card('ntfy').textContent).toContain('A server that needs a token takes it as the plugin README says.');
        for (const other of ['telegram', 'journal-forwarder']) {
            expect(within(card(other)).queryByLabelText(/server/i)).toBeNull();
            expect(within(card(other)).queryByRole('button', { name: 'Use the public server' })).toBeNull();
        }
        const configured = () =>
            desktop.calls.filter((each) => each.name === 'setup.configurePlugin').map((each) => each.args);

        fireEvent.input(within(card('ntfy')).getByLabelText('Topic'), { target: { value: 'alerts' } });
        fireEvent.input(within(card('ntfy')).getByLabelText('Server (optional)'), {
            target: { value: ' https://ntfy.example.test ' },
        });
        fireEvent.click(within(card('ntfy')).getByRole('button', { name: 'Configure' }));
        await waitFor(() => expect(configured()).toHaveLength(1));
        await waitFor(() => expect(card('ntfy').querySelector('button[disabled]')).toBeNull());

        fireEvent.input(within(card('ntfy')).getByLabelText('Topic'), { target: { value: 'alerts' } });
        fireEvent.input(within(card('ntfy')).getByLabelText('Server (optional)'), { target: { value: '  ' } });
        fireEvent.click(within(card('ntfy')).getByRole('button', { name: 'Configure' }));
        await waitFor(() => expect(configured()).toHaveLength(2));
        await waitFor(() => expect(card('ntfy').querySelector('button[disabled]')).toBeNull());

        fireEvent.click(within(card('ntfy')).getByRole('button', { name: 'Use the public server' }));
        await waitFor(() => expect(configured()).toHaveLength(3));
        expect(configured()).toEqual([
            ['ntfy', { target: 'alerts', server: 'https://ntfy.example.test' }],
            ['ntfy', { target: 'alerts' }],
            ['ntfy', { server: '' }],
        ]);
    });

    it('sends each screen command to the address its registered screen gives', async () => {
        renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        const screens: [DesktopCommand, string][] = [
            ['navigate.performance', 'performance'],
            ['navigate.workitems', 'workitems'],
            ['navigate.people', 'people'],
            ['navigate.system', 'system'],
            ['navigate.settings', 'settings'],
        ];
        for (const [command, id] of screens) {
            const entry = registry.list('screen').find((each) => each.id === id)!;
            expect(commandTarget(command)).toEqual(parseRoute(entry.href()));
        }
        expect(commandTarget('navigate.attention')).toBe('needs');
        expect(commandTarget('navigate.connect')).toBe('connect');
    });

    it('opens Performance from the desktop command, drawn from the runtime', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
        try {
            const fake = pending(new FakeApi())
                .answer('campaign.activity', 'campaign.activity/all-7-days')
                .answer(
                    'campaign.activity_by_period',
                    'campaign.activity_by_period/contacted-7-days',
                    'campaign.activity_by_period/replied-7-days',
                );
            const { desktop } = renderConsole(fake);
            await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
            flush(() => desktop.command('navigate.performance'));
            await waitFor(() => expect(route.value.screen).toBe('performance'));
            await waitFor(() => expect(fake.sent('campaign.activity_by_period')).toBeTruthy());
            await waitFor(() => expect(document.querySelector('.harness .content')!.textContent).toContain('People'));
        } finally {
            vi.useRealTimers();
        }
    });

    it('keeps what is typed into a plugin card through a repaint and a refusal, and forgets it once accepted', async () => {
        const { desktop } = renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        show('Connect');
        await waitFor(() => within(panel('Connect')).getByLabelText('Chat id'));
        const input = (label: string) => within(panel('Connect')).getByLabelText<HTMLInputElement>(label);
        fireEvent.input(input('Chat id'), { target: { value: '-1001' } });
        fireEvent.input(input('Bot token'), { target: { value: '123:abc' } });

        flush(() => {
            setupBusy.value = 'Installing codex…';
        });
        flush(() => {
            setupBusy.value = null;
        });
        flush(() => desktop.changed());
        await waitFor(() => expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(2));
        expect(input('Chat id').value).toBe('-1001');
        expect(input('Bot token').value).toBe('123:abc');

        desktop.refuse.set('setup.configurePlugin', 'The chat id is not one Telegram takes.');
        const telegram = () => panel('Connect').querySelector<HTMLElement>('.plugin[data-id="telegram"]')!;
        fireEvent.click(within(telegram()).getByRole('button', { name: 'Configure' }));
        await waitFor(() => expect(panel('Connect').textContent).toContain('The chat id is not one Telegram takes.'));
        expect(input('Chat id').value).toBe('-1001');
        expect(input('Bot token').value).toBe('123:abc');

        desktop.refuse.delete('setup.configurePlugin');
        await waitFor(() => expect(telegram().querySelector('button[disabled]')).toBeNull());
        fireEvent.click(within(telegram()).getByRole('button', { name: 'Configure' }));
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'setup.configurePlugin')).toHaveLength(2),
        );
        await waitFor(() => expect(input('Chat id').value).toBe(''));
        expect(input('Bot token').value).toBe('');
        expect(desktop.calls.filter((each) => each.name === 'setup.configurePlugin').map((each) => each.args)).toEqual([
            ['telegram', { token: '123:abc', target: '-1001' }],
            ['telegram', { token: '123:abc', target: '-1001' }],
        ]);
    });

    it('draws a hostile question and a hostile contact name in Needs you as text', async () => {
        renderConsole(pending(new FakeApi()));
        const needs = await openNeeds();
        // A prospect may be called anything, and an agent may ask anything; the next read brings it back.
        flush(() => {
            attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: hostile }));
            attention.questions.value = attention.questions.value.map((each) => ({ ...each, question: hostile }));
        });
        await waitFor(() => expect(within(needs).getAllByText(hostile).length).toBeGreaterThanOrEqual(2));
        expect(needs.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('reads what waits and the campaigns again once, a moment after journal lines arrive', async () => {
        const fake = pending(new FakeApi());
        const { journal } = renderConsole(fake);
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        const approvals = asked(fake, 'approval.list');
        const listed = asked(fake, 'campaign.list');
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_101' }));
            journal.send(journalLine({ id: 'jrn_102' }));
            journal.send(journalLine({ id: 'jrn_103' }));
        });
        expect(asked(fake, 'approval.list')).toBe(approvals);
        await waitFor(() => expect(asked(fake, 'campaign.list')).toBe(listed + 1));
        expect(asked(fake, 'approval.list')).toBe(approvals + 1);
        await new Promise((resolve) => setTimeout(resolve, 2 * reloadDelay));
        expect(asked(fake, 'approval.list')).toBe(approvals + 1);
        expect(asked(fake, 'campaign.list')).toBe(listed + 1);
    });

    it('keeps the Campaigns page in the area at its own table while a journal line re-reads the panel', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-16T09:00:00Z'));
        try {
            const fake = pending(new FakeApi()).answer('campaign.activity', 'campaign.activity/last-7-days');
            const { journal } = renderConsole(fake);
            await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
            show('Settings');
            flush(() => navigate(parseRoute('#/campaigns?view=activity')));
            const week = classicCampaigns.keyOf({ view: 'activity', period: '7d', filters: {} });
            await waitFor(() => expect(classicCampaigns.shown.value).toBe(week));
            const page = classicCampaigns.campaigns.value;
            const counted = classicCampaigns.activity.value;
            const listed = asked(fake, 'campaign.list');
            flush(() => {
                journal.open();
                journal.send(journalLine({ id: 'jrn_201' }));
            });
            await waitFor(() => expect(asked(fake, 'campaign.summary')).toBe(2));
            expect(asked(fake, 'campaign.list')).toBe(listed + 1);
            expect(classicCampaigns.shown.value).toBe(week);
            expect(classicCampaigns.campaigns.value).toBe(page);
            expect(classicCampaigns.activity.value).toBe(counted);
            expect(consoleCampaigns.campaigns.value.length).toBeGreaterThan(0);
        } finally {
            vi.useRealTimers();
        }
    });

    it('keeps the Attention page at its decided lists while a journal line re-reads Needs you', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/decided')
            .answer('decision.list', 'decision.list/pending', 'decision.list/decided')
            .answer('preference.list', 'preference.list/set')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all');
        const { journal } = renderConsole(fake);
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        const pendingIds = attention.approvals.value.map((each) => each.id);
        show('Settings');
        flush(() => navigate(parseRoute('#/attention?decided=1')));
        const decided = fixtures['approval.list/decided'].response.items.map((each) => each.id);
        await waitFor(() => expect(classicAttention.approvals.value.map((each) => each.id)).toEqual(decided));
        const page = classicAttention.approvals.value;
        const questions = classicAttention.questions.value;
        const approvals = asked(fake, 'approval.list');
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_301' }));
        });
        await waitFor(() => expect(asked(fake, 'approval.list')).toBe(approvals + 1));
        await waitFor(() => expect(asked(fake, 'campaign.summary')).toBe(2));
        expect(classicAttention.approvals.value).toBe(page);
        expect(classicAttention.questions.value).toBe(questions);
        expect(attention.approvals.value.map((each) => each.id)).toEqual(pendingIds);
    });

    it('keeps the field a person is typing in focused, with its text and caret, through the clock and a journal line', async () => {
        const fake = pending(new FakeApi()).answer('decision.get', 'decision.get/pending-with-options');
        const { journal } = renderConsole(fake);
        await openNeeds();
        await waitFor(() => screen.getAllByText('Grace Hopper'));
        // A field of the pane: the reason beside a question's answer.
        fireEvent.click(panel('Needs you').querySelector<HTMLElement>('[data-row="dec_1"]')!);
        await waitFor(() => panel('Needs you').querySelector('.needs-pane[data-id="dec_1"]'));
        const reason = () =>
            within(
                panel('Needs you').querySelector<HTMLElement>('.needs-pane[data-id="dec_1"]')!,
            ).getByRole<HTMLInputElement>('textbox', { name: 'Reason (optional)' });
        reason().focus();
        fireEvent.input(reason(), { target: { value: 'not this quarter' } });
        reason().setSelectionRange(4, 8);
        const before = reason();

        flush(() => {
            clock.value = clock.value + 30_000;
        });
        expect(reason()).not.toBe(before);
        expect(document.activeElement).toBe(reason());
        expect([reason().selectionStart, reason().selectionEnd]).toEqual([4, 8]);

        const approvals = asked(fake, 'approval.list');
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_401' }));
        });
        await waitFor(() => expect(asked(fake, 'approval.list')).toBe(approvals + 1));
        await waitFor(() => expect(asked(fake, 'campaign.summary')).toBe(2));
        expect(document.activeElement).toBe(reason());
        expect(reason().value).toBe('not this quarter');
        expect([reason().selectionStart, reason().selectionEnd]).toEqual([4, 8]);
    });

    it('gives the sidebar room for the macOS window buttons, and every control the console draws a focus ring', async () => {
        const desktop = Object.assign(chatDesktop(), { platform: 'darwin' as const });
        renderConsole(pending(new FakeApi()), desktop);
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        const side = document.querySelector('.jc-console .side')!;
        const mac = rules().filter((rule) => rule.selector.includes('desktop-mac'));
        expect(mac.length).toBeGreaterThan(0);
        for (const rule of mac) expect(side.matches(rule.selector)).toBe(true);

        const focus = rules().filter((rule) => rule.selector === '.jc-console :focus-visible');
        expect(focus.map((rule) => /outline:\s*(?!none|0)[^;]+;/.test(rule.body))).toContain(true);
        const send = screen.getByRole('button', { name: 'Send' });
        expect(send.matches('.jc-console *')).toBe(true);
    });

    it('puts the mark and the wordmark at the head of the sidebar, the dark wordmark shown only on a dark ground', async () => {
        renderConsole(pending(new FakeApi()));
        const brand = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.jc-console .side .brand');
            expect(found).not.toBeNull();
            return found!;
        });
        const [mark, light, dark] = Array.from(brand.querySelectorAll('img'));
        expect(mark!.getAttribute('src')).toMatch(/favicon[.]svg$/);
        expect(mark!.getAttribute('alt')).toBe('');
        expect(light!.getAttribute('src')).toMatch(/brand-wordmark[.]svg$/);
        expect(light!.getAttribute('alt')).toBe('Jason AI, GTM Harness');
        expect(dark!.getAttribute('src')).toMatch(/brand-wordmark-dark[.]svg$/);
        expect(dark!.getAttribute('alt')).toBe('Jason AI, GTM Harness');
        expect(brand.querySelector('.glyph')).toBeNull();
        expect(brand.querySelector('.alpha')?.textContent).toBe('Alpha');
        const shown = (selector: string) =>
            rules()
                .filter((rule) => rule.selector === selector)
                .map((rule) => rule.body)
                .join(';');
        expect(shown('.jc-console .brand .wordmark.dark')).toContain('display: none');
        expect(shown(":root:not([data-theme='light']) .jc-console .brand .wordmark.dark")).toContain('display: inline');
        expect(shown(":root[data-theme='dark'] .jc-console .brand .wordmark.light")).toContain('display: none');
    });

    it('holds only Settings in the sidebar foot, and not the runtime version', async () => {
        renderConsole(pending(new FakeApi()));
        const foot = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.jc-console .side .sfoot');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(words(foot).trim()).toBe('Settings');
        expect(document.querySelector('.jc-console .side')!.textContent).not.toContain(
            fixtures['system.info/with-ui'].response.runtime_version,
        );
    });

    it('says after a fresh start that lines written in between are not shown, and keeps the lines it has', async () => {
        const { journal } = renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_501' }));
        });
        show('Activity');
        const activity = await waitFor(() => panel('Activity'));
        await waitFor(() => expect(lines.value.map((each) => each.id)).toEqual(['jrn_501']));
        const shown = activity.querySelectorAll('.ev').length;
        flush(() => {
            streamState.value = { ...streamState.value, fresh: true };
        });
        await waitFor(() =>
            expect(words(panel('Activity'))).toContain(
                'The runtime no longer holds the last line this window saw, so lines written in between are not shown.',
            ),
        );
        expect(words(panel('Activity'))).not.toContain('starts again');
        expect(panel('Activity').querySelectorAll('.ev').length).toBe(shown);
        expect(shown).toBe(1);
    });
});
