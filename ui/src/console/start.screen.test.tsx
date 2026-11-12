import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { DesktopSetup } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { heldDialog, replyDismissed } from './connect';
import { chats, conversations, lines, setup, setupError } from './state';
import { startDraft, startMaking, startWaiting, submitStart } from './start';

// The window opens on the question who the person wants to reach. What is sent there starts a set-up chat; with no AI
// connected the AI card comes first and the message waits; Reply is asked for when the work needs a way to send.

const noHost: DesktopSetup = { ...setupFound, hosts: setupFound.hosts.map((each) => ({ ...each, found: false })) };
const noReply: DesktopSetup = {
    ...setupFound,
    reply: { ...setupFound.reply, signedIn: false, wired: false, user: null },
};

/** Enrolment goes where the global default sends it, which is Reply: the route a draft campaign's chat reads. */
const toReply: FixtureName = 'route.resolve/global-default';

function renderConsole(
    desktop: StubDesktop,
    routed: FixtureName | null = toReply,
    prepare: (fake: FakeApi) => void = () => undefined,
): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    if (routed) fake.answer('route.resolve', routed);
    prepare(fake);
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
const box = () =>
    within(startForm()).getByRole('textbox', { name: 'Who do you want to reach?' }) as HTMLTextAreaElement;
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
    startMaking.value = null;
    setupError.value = null;
    replyDismissed.value = [];
});

/** Opens the Campaigns panel and the chat of the campaign named, from its row, as a person does. */
async function openCampaignChat(desktop: StubDesktop, name: string): Promise<void> {
    flush(() => desktop.command('navigate.campaigns'));
    await waitFor(() => screen.getByText(name));
    fireEvent.click(within(screen.getByText(name).closest('tr')!).getByRole('button', { name: 'Chat' }));
    await waitFor(() => screen.getByRole('form', { name: 'Message' }));
}

/** A create that answers when the test lets it, so what the window shows while the chat is being made can be read. */
function slowCreate(desktop: StubDesktop, before?: () => void): () => void {
    const real = desktop.chat.create;
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    desktop.chat.create = async (...args: Parameters<typeof real>) => {
        const made = await real(...args);
        await held;
        before?.();
        return made;
    };
    return release;
}

describe('the start screen', () => {
    it('opens on the question who the person wants to reach, with no chat made yet', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?', level: 1 }));
        expect(startForm()).toBeTruthy();
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
    });

    it('says on the first screen that this interface is optional, and why', async () => {
        renderConsole(stubDesktop());
        await waitFor(() => screen.getByRole('heading', { name: 'Who do you want to reach?', level: 1 }));
        const note = screen.getByText(/This interface is optional/);
        expect(note.textContent).toMatch(/terminal/);
        expect(note.textContent).toMatch(/agent/);
        expect(note.textContent).toMatch(/CLI and the API/);
    });

    it('takes the focus into the box when the window opens, and says why Start does nothing for an empty box', async () => {
        renderConsole(stubDesktop());
        await waitFor(() => expect(document.activeElement).toBe(box()));
        expect(within(startForm()).getByRole('button', { name: 'Start' }).getAttribute('title')).toBe(
            'Write who you want to reach first',
        );
    });

    it('starts a set-up chat with what is sent, on the AI that is connected, and shows the chat', async () => {
        const desktop = renderConsole(stubDesktop());
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
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        ask('   ');
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        expect(startWaiting.value).toBeNull();
    });

    it('sends on Enter and not on Shift+Enter', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        fireEvent.change(box(), { target: { value: 'Recruiters in Lisbon' } });
        fireEvent.keyDown(box(), { key: 'Enter', shiftKey: true });
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        fireEvent.keyDown(box(), { key: 'Enter' });
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
    });

    it('gives the words back to the box when the desktop refuses to make the chat', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        desktop.refuse.set('chat.create', 'No chat could be made.');
        ask('Founders of seed-stage fintechs');
        await waitFor(() => expect(box().value).toBe('Founders of seed-stage fintechs'));
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
        expect(startMaking.value).toBeNull();
    });

    it('shows the AI card before anything is typed when no AI drives the chat, and not when one does', async () => {
        const desktop = renderConsole(stubDesktop(noHost));
        await waitFor(() => startForm());
        await waitFor(() => screen.getByRole('region', { name: 'Connect an AI' }));
        expect(box().disabled).toBe(false);
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
    });

    it('does not show the AI card when an AI drives the chat', async () => {
        renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        expect(screen.queryByRole('region', { name: 'Connect an AI' })).toBeNull();
    });

    it('keeps the message waiting with the AI card, and sends nothing', async () => {
        const desktop = renderConsole(stubDesktop(noHost));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        const card = await waitFor(() => screen.getByRole('region', { name: 'Connect an AI' }));
        expect(within(card).getByText('Claude Code')).toBeTruthy();
        const note = within(startForm().parentElement!).getByRole('status', { name: 'Waiting message' });
        expect(note.textContent).toContain('Founders of seed-stage fintechs');
        expect(note.textContent).toContain('while the window is open');
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
    });

    it('sends the waiting message once an AI is connected, and only once', async () => {
        const desktop = renderConsole(stubDesktop(noHost));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        desktop.status = setupFound;
        flush(() => desktop.changed());
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.send')[0]!.args[1]).toBe('Founders of seed-stage fintechs');
        flush(() => desktop.changed());
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        expect(calls(desktop, 'chat.create')).toHaveLength(1);
        expect(calls(desktop, 'chat.send')).toHaveLength(1);
    });

    it('gives the waiting message back to the box with Take back, and sends nothing when an AI comes', async () => {
        const desktop = renderConsole(stubDesktop(noHost));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        const note = await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        fireEvent.click(within(note).getByRole('button', { name: 'Take back' }));
        expect(box().value).toBe('Founders of seed-stage fintechs');
        expect(screen.queryByRole('status', { name: 'Waiting message' })).toBeNull();
        desktop.status = setupFound;
        flush(() => desktop.changed());
        await waitFor(() => expect(setup.value?.hosts.some((each) => each.found && each.id === 'claude')).toBe(true));
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
    });

    it('cannot be closed while a message waits, and can once it is taken back', async () => {
        const desktop = renderConsole(stubDesktop(noHost));
        await waitFor(() => startForm());
        await ready();
        ask('Founders of seed-stage fintechs');
        await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        const closeStart = () =>
            fireEvent.click(
                within(
                    screen.getByText('Start', { selector: '.dv-default-tab-content' }).closest<HTMLElement>('.dv-tab')!,
                ).getByRole('button', { hidden: true }),
            );
        closeStart();
        await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        expect(startWaiting.value).toBe('Founders of seed-stage fintechs');
        fireEvent.click(screen.getByRole('button', { name: 'Take back' }));
        closeStart();
        await waitFor(() => expect(screen.queryByRole('form', { name: 'Start' })).toBeNull());
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
    });

    it('says why a waiting message is not sent when the setup cannot be read, and gives the words back on Take back', async () => {
        const desktop = renderConsole(stubDesktop());
        desktop.refuse.set('setup.status', 'The desktop could not read what is set up.');
        await waitFor(() => startForm());
        await waitFor(() => expect(setupError.value).not.toBeNull());
        ask('Heads of sales in Poland');
        const note = await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        expect(note.textContent).toContain('could not be read');
        expect(note.textContent).not.toContain('Starting the chat');
        expect(screen.getByRole('region', { name: 'Connect an AI' }).textContent).toContain(
            'The desktop could not read what is set up.',
        );
        fireEvent.click(within(note).getByRole('button', { name: 'Take back' }));
        expect(box().value).toBe('Heads of sales in Poland');
        expect(calls(desktop, 'chat.create')).toHaveLength(0);
    });

    it('takes no second message while the chat is being made, and a second chat is never started', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        const release = slowCreate(desktop);
        ask('Heads of sales in Poland');
        const note = await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        expect(note.textContent).toContain('Starting the chat');
        expect(box().disabled).toBe(true);
        expect(within(startForm()).getByRole('button', { name: 'Start' }).hasAttribute('disabled')).toBe(true);
        flush(() => submitStart('A second message'));
        expect(startWaiting.value).toBeNull();
        release();
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.create')).toHaveLength(1);
        expect(calls(desktop, 'chat.send')[0]!.args[1]).toBe('Heads of sales in Poland');
    });

    it('gives the words back, with the reason, when the AI goes while the chat is being made', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        const release = slowCreate(desktop, () => {
            setup.value = { ...setupFound, hosts: setupFound.hosts.map((each) => ({ ...each, found: false })) };
        });
        ask('Heads of sales in Poland');
        await waitFor(() => screen.getByRole('status', { name: 'Waiting message' }));
        release();
        const message = await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
        await waitFor(() => expect((message as HTMLTextAreaElement).value).toBe('Heads of sales in Poland'));
        expect(screen.getAllByText(/No agent host that drives the chat was found/).length).toBeGreaterThan(0);
        expect(calls(desktop, 'chat.send')).toHaveLength(0);
    });
});

describe('Reply in a chat', () => {
    it('is not asked for at the start, even with no Reply sign-in', async () => {
        const desktop = renderConsole(stubDesktop(noReply));
        await waitFor(() => startForm());
        await ready();
        ask('Recruiters in Lisbon');
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
    });

    it('is asked for in the chat of a draft campaign when Reply is not connected, and can be dismissed for that chat', async () => {
        const desktop = renderConsole(stubDesktop(noReply));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        const card = await waitFor(() => screen.getByRole('region', { name: 'Connect Reply' }));
        expect(within(card).getByRole('button', { name: 'Sign in' })).toBeTruthy();
        fireEvent.click(screen.getByRole('button', { name: 'Not now' }));
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
    });

    it('drops a dialog its chat card showed when the card is dismissed, and when Reply is wired', async () => {
        const unwired: DesktopSetup = { ...setupFound, reply: { ...setupFound.reply, wired: false, accounts: [] } };
        for (const end of ['dismissed', 'wired'] as const) {
            replyDismissed.value = [];
            const desktop = renderConsole(stubDesktop(unwired));
            await waitFor(() => startForm());
            await ready();
            await openCampaignChat(desktop, 'Autumn');
            const card = () => screen.getByRole('region', { name: 'Connect Reply' });
            fireEvent.click(within(await waitFor(card)).getByRole('button', { name: 'Add account' }));
            fireEvent.click(await waitFor(() => within(card()).getByRole('button', { name: 'Gmail' })));
            await waitFor(() => expect(heldDialog()).not.toBeNull());
            expect(calls(desktop, 'setup.replyConnect')).toHaveLength(1);
            if (end === 'dismissed') {
                fireEvent.click(screen.getByRole('button', { name: 'Not now' }));
            } else {
                desktop.status = { ...unwired, reply: { ...unwired.reply, wired: true } };
                flush(() => desktop.changed());
            }
            await waitFor(() => expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull());
            expect(heldDialog(), end).toBeNull();
            expect(screen.queryByRole('dialog'), end).toBeNull();
            cleanup();
        }
    });

    it('appears in the set-up chat once its campaign is attached and is a draft', async () => {
        const desktop = renderConsole(stubDesktop(noReply));
        await waitFor(() => startForm());
        await ready();
        ask('Recruiters in Lisbon');
        await waitFor(() => screen.getByRole('form', { name: 'Message' }));
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
        const id = Object.keys(chats.value)[0]!;
        flush(() => desktop.emit(id, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' }));
        await waitFor(() => screen.getByRole('region', { name: 'Connect Reply' }));
    });

    it('is not asked for when Reply is connected', async () => {
        const desktop = renderConsole(stubDesktop());
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
    });

    it('is not asked for in the chat of a campaign that runs or is paused', async () => {
        const desktop = renderConsole(stubDesktop(noReply));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Spring');
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
        await openCampaignChat(desktop, 'Summer referrals');
        expect(screen.queryByRole('region', { name: 'Connect Reply' })).toBeNull();
    });
});

describe('Reply in a chat, by the route of enrolment', () => {
    const card = () => screen.queryByRole('region', { name: 'Connect Reply' });

    it('is asked for where enrolment is routed to Reply, and the question names the campaign and the operation', async () => {
        const fake: { value: FakeApi | null } = { value: null };
        const desktop = renderConsole(stubDesktop(noReply), toReply, (each) => (fake.value = each));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        await waitFor(() => expect(card()).not.toBeNull());
        expect(fake.value!.sent('route.resolve')).toEqual({ campaign_id: 'cmp_2', operation: 'campaign.enroll' });
    });

    it('is not asked for where enrolment is routed to another plugin', async () => {
        const fake: { value: FakeApi | null } = { value: null };
        const desktop = renderConsole(stubDesktop(noReply), 'route.resolve/elsewhere', (each) => (fake.value = each));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        await waitFor(() => expect(fake.value!.sent('route.resolve')).toBeDefined());
        await flush(() => Promise.resolve());
        expect(card()).toBeNull();
    });

    it('is asked for where no route sends enrolment anywhere yet: the sign-in is what writes the first one', async () => {
        const fake: { value: FakeApi | null } = { value: null };
        const desktop = renderConsole(stubDesktop(noReply), 'route.resolve/no-route', (each) => (fake.value = each));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        await waitFor(() => expect(card()).not.toBeNull());
    });

    it('is asked for when the route cannot be read, and the chat works on', async () => {
        const fake: { value: FakeApi | null } = { value: null };
        const desktop = renderConsole(stubDesktop(noReply), toReply, (each) => {
            fake.value = each;
            each.unreachable('route.resolve');
        });
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        await waitFor(() => expect(fake.value!.sent('route.resolve')).toBeDefined());
        await waitFor(() => expect(card()).not.toBeNull());
        expect(screen.getByRole('form', { name: 'Message' })).toBeTruthy();
    });

    it('waits for the route: nothing shows until the read answers', async () => {
        let release: () => void = () => undefined;
        const desktop = renderConsole(stubDesktop(noReply), toReply, (each) => {
            release = each.hold('route.resolve');
        });
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Autumn');
        expect(card()).toBeNull();
        release();
        await waitFor(() => expect(card()).not.toBeNull());
    });

    it('does not ask the runtime where a campaign that runs is routed', async () => {
        const fake: { value: FakeApi | null } = { value: null };
        const desktop = renderConsole(stubDesktop(noReply), null, (each) => (fake.value = each));
        await waitFor(() => startForm());
        await ready();
        await openCampaignChat(desktop, 'Spring');
        expect(fake.value!.calls.filter((each) => each.operation === 'route.resolve')).toHaveLength(0);
        expect(card()).toBeNull();
    });
});
