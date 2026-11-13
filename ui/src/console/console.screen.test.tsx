import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { DesktopCommand } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { registry } from '../platform/slots';
import { browserPlugins } from '../plugins';
import { navigate, parseRoute, route } from '../router';
import * as classicAttention from '../screens/attention/store';
import * as classicCampaigns from '../screens/campaigns/store';
import { journalLine, setupFound, chatDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { readFileSync } from 'node:fs';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { commandTarget, Console, reloadDelay } from './console';
import { attention } from './needs';
import { chats, clock, conversations, lines, setup, setupBusy, streamState } from './state';

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(fake: FakeApi, desktop: StubDesktop = chatDesktop(), journal: StubStream = stubStream()) {
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
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { desktop, journal };
}

function pending(fake: FakeApi): FakeApi {
    return fake
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
}

/** Shows a panel of the dock the way a person does, from the sidebar. */
function show(name: string): void {
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name }));
}

const panel = (name: string) => screen.getByRole('heading', { name, level: 1 }).closest<HTMLElement>('.painted')!;

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
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

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('the console', () => {
    it('opens a chat, when New chat is chosen, beside what needs the person, the campaigns and the activity, read from the runtime', async () => {
        const fake = pending(new FakeApi());
        renderConsole(fake);
        // Each read answers when it answers: the approvals, the questions and the campaigns arrive in no fixed order, so the
        // screen is read once every one of them has been asked and has landed.
        await waitFor(() => {
            expect(screen.getAllByText('Ada Lovelace').length).toBeGreaterThan(0);
            expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
            expect(screen.getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
            expect(fake.calls.map((each) => each.operation).sort()).toEqual(
                ['approval.list', 'campaign.list', 'campaign.summary', 'decision.list', 'system.info'].sort(),
            );
        });
    });

    it('approves and rejects with a reason through the runtime, and reads the list again', async () => {
        const fake = pending(new FakeApi())
            .answer('approval.approve', 'approval.approve/approved')
            .answer('approval.reject', 'approval.reject/rejected');
        renderConsole(fake);
        await waitFor(() => screen.getAllByText('Grace Hopper'));
        const needs = panel('Needs you');
        const row = (id: string) => needs.querySelector<HTMLElement>(`[data-id="${id}"]`)!;
        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Approve' }));
        await waitFor(() => expect(fake.sent('approval.approve')).toEqual({ approval_id: 'apr_1' }));
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'approval.list')).toHaveLength(2));
        const reason = within(row('apr_2')).getByRole('textbox', { name: 'Reason (optional)' });
        fireEvent.input(reason, { target: { value: 'not this quarter' } });
        fireEvent.click(within(row('apr_2')).getByRole('button', { name: 'Reject' }));
        await waitFor(() =>
            expect(fake.sent('approval.reject')).toMatchObject({ approval_id: 'apr_2', reason: 'not this quarter' }),
        );
    });

    it('answers a question with one of its options and a reason', async () => {
        const fake = pending(new FakeApi())
            .answer('decision.get', 'decision.get/pending-with-options')
            .answer('decision.answer', 'decision.answer/answered');
        renderConsole(fake);
        await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        const row = () => panel('Needs you').querySelector<HTMLElement>('[data-id="dec_1"]')!;
        fireEvent.click(within(row()).getByRole('button', { name: 'Answer' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Campaigns' }));
        show('Campaigns');
        await waitFor(() => within(panel('Campaigns')).getByText('Summer referrals'));
        const spring = within(panel('Campaigns')).getByText('Spring').closest('tr')!;
        expect(within(spring).getByText('Active')).toBeTruthy();
        expect(within(spring).getByText('22')).toBeTruthy();
        fireEvent.click(within(spring).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: 'What should Jason do in Spring?' }));
    });

    it('says a Campaigns panel with nothing in it and how a campaign starts, and opens a new chat from it', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('campaign.list', 'campaign.list/screen-empty');
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Campaigns' }));
        show('Campaigns');
        await waitFor(() =>
            expect(words(panel('Campaigns'))).toContain(
                'No campaigns yet. A campaign is set up in a chat with Jason: say who you want to reach, and the agent sets it up with you.',
            ),
        );
        expect(asked(fake, 'campaign.summary')).toBe(0);
        const made = () => desktop.calls.filter((each) => each.name === 'chat.create');
        await waitFor(() => expect(made()).toHaveLength(1), { timeout: 2000 });
        fireEvent.click(within(panel('Campaigns')).getByRole('button', { name: 'New chat' }));
        await waitFor(() => expect(made()).toHaveLength(2));
        expect(made()[1]!.args).toEqual([]);
    });

    it('draws a hostile campaign name as text, in the list and in a chat about it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Campaigns' }));
        show('Campaigns');
        await waitFor(() => within(panel('Campaigns')).getByText('Spring'));
        // A person may name a campaign anything; the next read brings that name back, to the desktop as well.
        desktop.names.set('cmp_1', hostile);
        flush(() => {
            consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.map((each) =>
                each.id === 'cmp_1' ? { ...each, name: hostile } : each,
            );
        });
        const row = within(panel('Campaigns')).getByText(hostile).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: `What should Jason do in ${hostile}?` }));
        // The only images of the page are the brand's own, at the head of the sidebar.
        expect(Array.from(document.querySelectorAll('img')).filter((img) => !img.closest('.brand'))).toEqual([]);
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

    it('picks a model and, where the model takes one, an effort beside the host, and sends both with the turn', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        const model = await waitFor(() => screen.getByRole('combobox', { name: 'Model' }));
        expect(model).toHaveProperty('value', '');
        expect(
            within(model)
                .getAllByRole('option')
                .map((each) => each.textContent),
        ).toEqual(['Default (recommended)', 'Opus', 'Sonnet', 'Haiku', 'Another model…']);
        // The host's own model takes no effort the desktop knows of: none is offered.
        expect(screen.queryByRole('combobox', { name: 'Effort' })).toBeNull();
        fireEvent.change(model, { target: { value: 'opus' } });
        const effort = await waitFor(() => screen.getByRole('combobox', { name: 'Effort' }));
        expect(
            within(effort)
                .getAllByRole('option')
                .map((each) => each.textContent),
        ).toEqual(['Default effort', 'Low', 'Medium', 'High', 'Extra high', 'Max']);
        fireEvent.change(effort, { target: { value: 'xhigh' } });
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Think hard.' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'chat.send')!.args.slice(1)).toEqual([
            'Think hard.',
            { host: 'claude', model: 'opus', effort: 'xhigh' },
        ]);
        // A model that takes no effort hides the menu, and the next turn names none.
        fireEvent.change(screen.getByRole('combobox', { name: 'Model' }), { target: { value: 'haiku' } });
        expect(screen.queryByRole('combobox', { name: 'Effort' })).toBeNull();
    });

    it('takes a model’s full name typed on Claude Code, with no effort', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        const model = await waitFor(() => screen.getByRole('combobox', { name: 'Model' }));
        fireEvent.change(model, { target: { value: 'opus' } });
        fireEvent.change(await waitFor(() => screen.getByRole('combobox', { name: 'Effort' })), {
            target: { value: 'high' },
        });
        fireEvent.change(model, { target: { value: '-another' } });
        const name = await waitFor(() => screen.getByRole('textbox', { name: 'Model name' }));
        expect(screen.queryByRole('combobox', { name: 'Effort' })).toBeNull();
        // With the name empty the turn is not sent, and Enter sends nothing either.
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);
        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Your message' }), { key: 'Enter' });
        expect(desktop.calls.some((each) => each.name === 'chat.send')).toBe(false);
        fireEvent.change(name, { target: { value: ' claude-opus-5-5 ' } });
        expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', false);
        expect(screen.getByRole('combobox', { name: 'Model' })).toHaveProperty('value', '-another');
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
        await waitFor(() =>
            expect(screen.getByRole('combobox', { name: 'Agent host' })).toHaveProperty('value', 'codex'),
        );
        expect(screen.queryByRole('combobox', { name: 'Model' })).toBeNull();
        expect(screen.queryByRole('combobox', { name: 'Effort' })).toBeNull();
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
        const model = await waitFor(() => screen.getByRole('combobox', { name: 'Model' }));
        expect(model).toHaveProperty('disabled', true);
        expect(
            within(model)
                .getAllByRole('option')
                .map((each) => each.textContent),
        ).toEqual(['Default (recommended)']);
        expect(document.querySelector('.composer .model-unread')?.textContent).toBe(sentence);
        expect(screen.queryByRole('combobox', { name: 'Effort' })).toBeNull();
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
        await waitFor(() =>
            expect(screen.getByRole('combobox', { name: 'Agent host' })).toHaveProperty('value', 'claude'),
        );
        const host = screen.getByRole('combobox', { name: 'Agent host' });
        expect(
            within(host).getByRole('option', { name: 'Gemini CLI: runs campaign work, not the chat yet' }),
        ).toHaveProperty('disabled', true);
        expect(within(host).queryByRole('option', { name: 'Codex' })).toBeNull();
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

    it('shows the Windows check of Codex’s sandbox, whole and open, while the turn waits on it', async () => {
        const fake = pending(new FakeApi());
        const desktop = chatDesktop({
            ...setupFound,
            hosts: setupFound.hosts.map((each) =>
                each.id === 'codex' ? { ...each, found: true, version: '0.167.0', install: null } : each,
            ),
        });
        renderConsole(fake, desktop);
        const host = await waitFor(() => screen.getByRole('combobox', { name: 'Agent host' }));
        fireEvent.change(host, { target: { value: 'codex' } });
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        expect(drawn.querySelectorAll('a, b, img, script, iframe, svg, [href], [src]')).toHaveLength(0);
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
        const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
        const waiting = () => screen.queryByRole('status', { name: 'Waiting message' });

        /** A chat whose first turn, "Go", runs. */
        async function running(): Promise<{ desktop: StubDesktop; id: string }> {
            const { desktop } = renderConsole(pending(new FakeApi()));
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            type('Go');
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(sends(desktop)).toHaveLength(1));
            return { desktop, id: sends(desktop)[0]!.args[0] as string };
        }

        it('waits, one at a time, and is sent once as the next turn when the turn ends', async () => {
            const { desktop, id } = await running();
            flush(() => desktop.emit(id, { kind: 'step', text: 'Ran jason status' }));
            const steps = await waitFor(() => {
                const element = document.querySelector('.thread details');
                expect(element).toBeTruthy();
                return element!;
            });
            type('And then?');
            fireEvent.click(screen.getByRole('button', { name: 'Send when the turn ends' }));
            await waitFor(() => expect(waiting()).toBeTruthy());
            expect(waiting()!.textContent).toContain('And then?');
            expect(box().value).toBe('');
            // Typing does not draw the thread again.
            type('Third');
            expect(document.querySelector('.thread details')).toBe(steps);
            enter();
            expect(box().value).toBe('Third');
            expect(waiting()!.textContent).toContain('And then?');
            expect(sends(desktop)).toHaveLength(1);
            flush(() => {
                desktop.emit(id, { kind: 'done', error: null });
                desktop.emit(id, { kind: 'done', error: null });
            });
            await waitFor(() => expect(sends(desktop)).toHaveLength(2));
            expect(sends(desktop)[1]!.args).toEqual([id, 'And then?', { host: 'claude' }]);
            expect(waiting()).toBeNull();
            expect(box().value).toBe('Third');
            expect(Array.from(document.querySelectorAll('.thread .m.user'), (each) => each.textContent)).toEqual([
                'Go',
                'And then?',
            ]);
        });

        it('is swapped with the box by Replace, and taken back only into an empty box', async () => {
            const { desktop, id } = await running();
            type('First');
            enter();
            await waitFor(() => expect(waiting()).toBeTruthy());
            expect(screen.queryByRole('button', { name: 'Replace' })).toBeNull();
            type('Second');
            fireEvent.click(screen.getByRole('button', { name: 'Replace' }));
            await waitFor(() => expect(waiting()!.textContent).toContain('Second'));
            expect(box().value).toBe('First');
            // Take back never overwrites the box: it waits for the box to be empty.
            expect(screen.getByRole('button', { name: 'Take back' })).toHaveProperty('disabled', true);
            type('');
            fireEvent.click(screen.getByRole('button', { name: 'Take back' }));
            await waitFor(() => expect(waiting()).toBeNull());
            expect(box().value).toBe('Second');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            expect(sends(desktop)).toHaveLength(1);
        });

        it('goes back to the box the moment the person stops the turn, and the end sends nothing', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(waiting()).toBeTruthy());
            fireEvent.click(screen.getByRole('button', { name: 'Stop' }));
            expect(desktop.calls.some((each) => each.name === 'chat.stop')).toBe(true);
            await waitFor(() => expect(box().value).toBe('Later'));
            expect(waiting()).toBeNull();
            expect(screen.getByRole('button', { name: 'Send when the turn ends' })).toHaveProperty('disabled', true);
            // The person types again before the stopped turn's end arrives.
            type('Something else');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            expect(box().value).toBe('Something else');
            expect(waiting()).toBeNull();
            expect(sends(desktop)).toHaveLength(1);
        });

        it('ends as a turn that could not start when no host is left to send it to, and goes back to the box', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(waiting()).toBeTruthy());
            setup.value = { ...setup.value!, hosts: [] };
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() =>
                expect(document.querySelector('.chat-error')?.textContent).toContain(
                    'No agent host that drives the chat was found on this machine.',
                ),
            );
            expect(sends(desktop)).toHaveLength(1);
            expect(waiting()).toBeNull();
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
            await waitFor(() => expect(waiting()).toBeTruthy());
            const unreadable = `The version of Claude Code could not be read, so the chat does not run on it. Run \`claude --version\` in a terminal to see it. ${hostile}`;
            setup.value = {
                ...setup.value!,
                hosts: setup.value!.hosts.map((each) =>
                    each.id === 'claude' ? { ...each, version: null, belowFloor: unreadable } : each,
                ),
            };
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => expect(document.querySelector('.chat-error')?.textContent).toContain(unreadable));
            expect(document.querySelector('.chat-error')?.textContent).not.toContain('No agent host');
            expect(document.querySelector('.chat-error img')).toBeNull();
            expect(sends(desktop)).toHaveLength(1);
            expect(box().value).toBe('Later');
        });

        it('stays in sight, not sent, when the turn stops or fails while the box holds other words', async () => {
            const { desktop, id } = await running();
            type('Later');
            enter();
            await waitFor(() => expect(waiting()).toBeTruthy());
            type('More');
            fireEvent.click(screen.getByRole('button', { name: 'Stop' }));
            await waitFor(() => expect(waiting()!.textContent).toContain('Not sent'));
            expect(waiting()!.textContent).toContain('Later');
            expect(box().value).toBe('More');
            flush(() => desktop.emit(id, { kind: 'done', error: null }));
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
            expect(waiting()!.textContent).toContain('Later');
            expect(sends(desktop)).toHaveLength(1);

            type('');
            fireEvent.click(screen.getByRole('button', { name: 'Take back' }));
            await waitFor(() => expect(box().value).toBe('Later'));
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
            await waitFor(() => expect(sends(desktop)).toHaveLength(2));
            type('Then this');
            enter();
            await waitFor(() => expect(waiting()).toBeTruthy());
            type('And more');
            flush(() => desktop.emit(id, { kind: 'done', error: `Not logged in ${hostile}` }));
            await waitFor(() => expect(waiting()!.textContent).toContain('Not sent'));
            expect(waiting()!.textContent).toContain('Then this');
            expect(box().value).toBe('And more');
            expect(screen.getByRole('button', { name: 'Take back' })).toHaveProperty('disabled', true);
            expect(sends(desktop)).toHaveLength(2);
            expect(document.querySelector('.chat-error')!.textContent).toContain(`Not logged in ${hostile}`);
        });
    });

    it('answers a permission once, and says so when the desktop no longer holds it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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

    it('shows a failed sign-in in the host own words, with how to sign in under it', async () => {
        const fake = pending(new FakeApi());
        const { desktop } = renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
            await waitFor(() =>
                expect(screen.getByRole('combobox', { name: 'Agent host' })).toHaveProperty('value', 'codex'),
            );
            const host = screen.getByRole('combobox', { name: 'Agent host' });
            const option = within(host).getByRole('option', {
                name: 'Claude Code: Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on. Update Claude Code, for example by running claude update in a terminal, then check again.',
            });
            expect(option).toHaveProperty('disabled', true);
            // The sentence is long; the closed list keeps to the composer's row.
            expect(rules().find((rule) => rule.selector === '.jc-console .composer select')?.body).toMatch(
                /max-width:\s*60%/,
            );
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
                'No agent host that drives the chat was found',
            );
            expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', true);

            const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
            desktop.status = withClaude(null, false);
            fireEvent.click(within(note).getByRole('button', { name: 'Check again' }));
            await waitFor(() =>
                expect(desktop.calls.filter((each) => each.name === 'setup.status').length).toBe(read + 1),
            );
            await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).toHaveProperty('disabled', false));
            expect(screen.getByRole('combobox', { name: 'Agent host' })).toHaveProperty('value', 'claude');
            expect(document.querySelector('.composer .host-held')).toBeNull();
        });

        describe('held under a chat whose host it is, while another host drives the chat', () => {
            const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
            const box = () => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
            const hostMenu = () => screen.getByRole<HTMLSelectElement>('combobox', { name: 'Agent host' });
            const unreadable =
                'The version of Claude Code could not be read, so the chat does not run on it. Run `claude --version` in a terminal to see it; the chat needs 2.1.257 or newer, and `claude update` updates it. Then check again.';

            /** A chat whose first turn ran on Claude Code, with Codex found too, and then Claude Code held by `sentence`. */
            async function heldAfterATurn(sentence: string): Promise<{ desktop: StubDesktop; id: string }> {
                const desktop = chatDesktop(withClaude(null, true));
                renderConsole(pending(new FakeApi()), desktop);
                await waitFor(() => expect(hostMenu()).toHaveProperty('value', 'claude'));
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
                    expect(hostMenu().value).toBe('claude');
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
                expect(hostMenu().value).toBe('claude');
                fireEvent.change(box(), { target: { value: 'Next' } });
                fireEvent.click(screen.getByRole('button', { name: 'Send' }));
                await waitFor(() => expect(sends(desktop)).toHaveLength(2));
                expect(sends(desktop)[1]!.args.slice(1)).toEqual(['Next', { host: 'claude' }]);
            });

            it('runs the turn on another host the person chooses', async () => {
                const { desktop } = await heldAfterATurn(old());
                await waitFor(() => expect(document.querySelector('.composer .host-held')).toBeTruthy());
                fireEvent.change(hostMenu(), { target: { value: 'codex' } });
                await waitFor(() => expect(hostMenu().value).toBe('codex'));
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
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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

    it("says under the versions line, as text, that the runtime is not the bundle's version", async () => {
        const sentence = `The runtime is 0.33.0 and this desktop carries 0.34.0: run jason update. ${hostile}`;
        renderConsole(
            pending(new FakeApi()),
            chatDesktop({ ...setupFound, runtimeVersion: '0.33.0', versionsDiffer: sentence }),
        );
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
            await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        // The dock draws its panels in a render of its own once it is ready, which the runtime's answer can come before:
        // the read having ended says nothing of the panel being there yet.
        const needs = await waitFor(() => panel('Needs you'));
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

    it('keeps the Campaigns page in the page panel at its own table while a journal line re-reads the panel', async () => {
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
        const fake = pending(new FakeApi());
        const { journal } = renderConsole(fake);
        await waitFor(() => screen.getAllByText('Grace Hopper'));
        const reason = () =>
            within(panel('Needs you').querySelector<HTMLElement>('[data-id="apr_2"]')!).getByRole<HTMLInputElement>(
                'textbox',
                { name: 'Reason (optional)' },
            );
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
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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

    it("cuts the sidebar's version line with an ellipsis rather than scrolling, and keeps its whole text in the title", async () => {
        renderConsole(pending(new FakeApi()));
        const version = `Jason ${fixtures['system.info/with-ui'].response.runtime_version}`;
        const foot = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.jc-console .side .sfoot')!;
            expect(words(found).trim()).toBe(version);
            return found;
        });
        expect(foot.title).toBe(version);
        // The sidebar is drawn again as what it lists arrives, so the foot is read where it stands now.
        const now = document.querySelector<HTMLElement>('.jc-console .side .sfoot')!;
        const body = rules()
            .filter((rule) => rule.selector.includes('sfoot') && now.matches(rule.selector))
            .map((rule) => rule.body)
            .join(';');
        expect(body).toMatch(/white-space:\s*nowrap/);
        expect(body).toMatch(/overflow:\s*hidden/);
        expect(body).toMatch(/text-overflow:\s*ellipsis/);
    });

    it('says after a fresh start that lines written in between are not shown, and keeps the lines it has', async () => {
        const { journal } = renderConsole(pending(new FakeApi()));
        await waitFor(() => screen.getByRole('button', { name: 'Send' }));
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
