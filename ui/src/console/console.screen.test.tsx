import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as attention from '../screens/attention/store';
import * as campaignStore from '../screens/campaigns/store';
import { journalLine, setupFound, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { Console } from './console';
import { chats, lines } from './state';

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(fake: FakeApi, desktop: StubDesktop = stubDesktop(), journal: StubStream = stubStream()) {
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
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
});

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('the console', () => {
    it('opens on a chat beside what needs the person, the campaigns and the activity, read from the runtime', async () => {
        const fake = pending(new FakeApi());
        renderConsole(fake);
        await waitFor(() => expect(screen.getAllByText('Ada Lovelace').length).toBeGreaterThan(0));
        expect(screen.getByRole('heading', { name: 'What should Jason work on?' })).toBeTruthy();
        expect(screen.getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
        const operations = fake.calls.map((each) => each.operation).sort();
        expect(operations).toEqual(
            ['approval.list', 'campaign.list', 'campaign.summary', 'decision.list', 'system.info'].sort(),
        );
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

    it('draws a hostile campaign name as text, in the list and in a chat about it', async () => {
        const fake = pending(new FakeApi());
        renderConsole(fake);
        await waitFor(() => screen.getByRole('button', { name: 'Campaigns' }));
        show('Campaigns');
        await waitFor(() => within(panel('Campaigns')).getByText('Spring'));
        // A person may name a campaign anything; the next read brings that name back.
        flush(() => {
            campaignStore.campaigns.value = campaignStore.campaigns.value.map((each) =>
                each.id === 'cmp_1' ? { ...each, name: hostile } : each,
            );
        });
        const row = within(panel('Campaigns')).getByText(hostile).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        await waitFor(() => screen.getByRole('heading', { name: `What should Jason do in ${hostile}?` }));
        expect(document.querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('follows the journal stream from its own origin, and draws a hostile line as text', async () => {
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
            desktop.emit(id, { kind: 'step', text: 'Ran jason approval list' });
            desktop.emit(id, { kind: 'text', text: hostile });
            desktop.emit(id, { kind: 'done', error: null });
        });
        await waitFor(() => screen.getByText(hostile));
        expect(document.querySelector('.thread img')).toBeNull();
        expect(screen.getByText('1 action')).toBeTruthy();
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
                detail: 'jason status',
                remember: true,
            });
            desktop.emit(id, {
                kind: 'permission',
                request: 'req_2',
                title: 'Write a file',
                detail: 'notes.md',
                remember: false,
            });
        });
        await waitFor(() => screen.getByText('Write a file'));
        fireEvent.click(screen.getByRole('button', { name: 'Allow for this chat' }));
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

    it('connects: lists the hosts found or not, installs one, and hands a token to the desktop', async () => {
        const fake = pending(new FakeApi());
        const desktop = stubDesktop();
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
        fireEvent.input(within(panel('Connect')).getByLabelText('Bot token'), { target: { value: ' 123:abc ' } });
        fireEvent.click(within(panel('Connect')).getAllByRole('button', { name: 'Connect' }).at(-1)!);
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.setPluginToken')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'setup.setPluginToken')!.args).toEqual([
            'telegram',
            '123:abc',
        ]);
        expect(setupFound.plugins[0]!.connected).toBe(false);
    });
});
