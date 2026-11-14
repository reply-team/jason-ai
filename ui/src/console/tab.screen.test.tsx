import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { fullAccessStanding } from '../platform/full-access';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { newChat } from './chat-model';
import { Console } from './console';
import { attention } from './needs';
import { startDraft, startMaking, startMode, startPick, startWaiting, submitStart } from './start';
import { chats, conversations, lines, setup, setupError } from './state';

// The console in a browser tab the desktop opened: the console over the tab's object, drawn as the window draws it.
// What the bridge's table does not carry is drawn off, in its place, with one sentence; Open in is not drawn at all; and
// no member outside the table is ever asked of the bridge. A tab never says yes: no card has a button, the mode is a
// label, and a chat outside Ask first and Plan only takes no message from it.

const elsewhere = "Done in Jason Desktop's own window.";

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

const table = new Set([
    'mode.get',
    'theme.get',
    'fullAccess.standing',
    'setup.status',
    'chat.list',
    'chat.open',
    'chat.create',
    'chat.send',
    'chat.stop',
    'chat.rename',
]);

interface Options {
    /** The panel to open: Connect, from the sidebar's More menu, or the one the window opens on. */
    panel?: 'Connect' | 'start';
    /** Conversations the desktop kept before the tab opened. */
    keep?: Parameters<StubDesktop['keep']>[0][];
    standing?: boolean;
}

async function renderTab({ panel = 'Connect', keep = [], standing = false }: Options = {}) {
    const desktop = chatDesktop({
        ...setupFound,
        plugins: setupFound.plugins.map((each) => (each.id === 'ntfy' ? { ...each, binding: true } : each)),
    });
    for (const each of keep) desktop.keep(each);
    if (standing) desktop.standing = { givenAt: '2026-11-11T09:00:00.000Z', newChats: true };
    const stand = standInBridge(desktop);
    const tab = tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch));
    setPlace('tab', tab);
    await loadMode(tab);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('route.resolve', 'route.resolve/global-default')
        .answer('ui.tokens', 'ui.tokens/two')
        .answer('system.info', 'system.info/with-ui');
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
    // The page's start reads the pairing before it starts the console in a tab.
    await session.bootstrap();
    render(<Console api={fake} desktop={tab} stream={stubStream().stream} bootstrapped />);
    const side = () => within(document.querySelector<HTMLElement>('.side')!);
    if (panel === 'Connect') {
        await waitFor(() => side().getByRole('button', { name: 'More' }));
        fireEvent.click(side().getByRole('button', { name: 'More' }));
        fireEvent.click(side().getByRole('menuitem', { name: /^Connect/ }));
        await waitFor(() => expect(document.querySelector('.plugin')).not.toBeNull());
    } else {
        await waitFor(() => screen.getByRole('form', { name: 'Start' }));
        await waitFor(() => expect(setup.value).not.toBeNull());
    }
    return { desktop, stand };
}

/** Opens a kept conversation from the sidebar's list, as a person does. */
async function openChat(title: string): Promise<void> {
    const list = await waitFor(() =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' }),
    );
    fireEvent.click(await waitFor(() => within(list).getByText(title).closest('button')!));
    await waitFor(() => screen.getByRole('textbox', { name: 'Your message' }));
}

const calls = (stand: { asked: { member?: string }[] }, member: string) =>
    stand.asked.filter((each) => each.member === member);

beforeEach(() => {
    setup.value = null;
    setupError.value = null;
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
    startDraft.value = '';
    startWaiting.value = null;
    startMaking.value = null;
    startMode.value = null;
    startPick.value = newChat('', null);
    fullAccessStanding.value = null;
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('The console in a tab', () => {
    it('is drawn, with the mode bar, and has no Open in', async () => {
        await renderTab();
        expect(screen.getAllByRole('region', { name: 'Mode bar' })).toHaveLength(1);
        expect(document.querySelector('[data-slot="open-in"]')?.children).toHaveLength(0);
    });

    it('draws the Demo | Live switch off, with its sentence on each button', async () => {
        await renderTab();
        const group = screen.getByRole('group', { name: 'Data' });
        const buttons = within(group).getAllByRole('button') as HTMLButtonElement[];
        expect(buttons.length).toBe(2);
        for (const each of buttons) {
            expect(each.disabled).toBe(true);
            expect(each.title).toBe(elsewhere);
        }
    });

    it('asks the bridge only for members of its table while the console starts', async () => {
        const { stand } = await renderTab();
        await waitFor(() => expect(stand.asked.some((each) => each.member === 'setup.status')).toBe(true));
        const members = stand.asked.filter((each) => each.path === '/call').map((each) => each.member ?? '');
        for (const member of members) expect(table.has(member), member).toBe(true);
    });
});

describe('Connect in a tab', () => {
    it('shows what was found, and draws every act of the Reply, mailbox and LinkedIn rows off, with the one sentence', async () => {
        const { stand } = await renderTab();
        const notes = screen.getAllByRole('note').filter((each) => words(each) === elsewhere);
        expect(notes.length).toBeGreaterThanOrEqual(2);
        const reply = within(document.body).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        // Checking again is a read, which the bridge carries.
        // Add account and Check again only change what the page shows, so they stay live; Add account's form is below.
        const acts = (within(reply).getAllByRole('button') as HTMLButtonElement[]).filter(
            (each) => !['Check again', 'Add account'].includes(words(each)),
        );
        expect(acts.length).toBeGreaterThan(1);
        for (const each of acts) {
            expect(each.disabled, words(each)).toBe(true);
            fireEvent.click(each);
        }
        expect(words(reply)).toContain('Sales inbox');
        const rows = reply.querySelectorAll<HTMLElement>('.acct');
        expect(rows.length).toBeGreaterThan(0);
        for (const row of rows)
            for (const each of within(row).getAllByRole('button') as HTMLButtonElement[])
                expect(each.disabled, words(each)).toBe(true);
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(
            stand.asked.filter((each) => (each.member ?? '').startsWith('setup.') && each.member !== 'setup.status'),
        ).toEqual([]);
    });

    it('draws the plugin cards and the AI card off too', async () => {
        const { stand } = await renderTab();
        const plugins = [...document.querySelectorAll<HTMLElement>('.plugin')];
        expect(plugins.length).toBeGreaterThan(0);
        for (const row of plugins)
            for (const each of within(row).queryAllByRole('button') as HTMLButtonElement[])
                // Disconnect only asks for the confirmation, which is the act and is drawn off.
                expect(each.disabled, words(each)).toBe(words(each) !== 'Disconnect');
        const ai = screen.getAllByText('AI', { selector: 'b' })[0]!.closest<HTMLElement>('.card')!;
        for (const each of within(ai).queryAllByRole('button') as HTMLButtonElement[])
            expect(each.disabled, words(each)).toBe(true);
        expect(calls(stand, 'setup.installProgram')).toEqual([]);
        expect(calls(stand, 'setup.configurePlugin')).toEqual([]);
    });
});

describe('Connect in a tab: each control by the member it calls', () => {
    it('keeps Add account live, since it only opens a form, and draws what the form does off', async () => {
        const { stand } = await renderTab();
        // The card is drawn again on each change, so it is found afresh each time.
        const reply = () => within(document.body).getByText('Reply', { selector: 'b' }).closest<HTMLElement>('.card')!;
        const add = within(reply()).getByRole('button', { name: 'Add account' }) as HTMLButtonElement;
        expect(add.disabled).toBe(false);
        fireEvent.click(add);
        const form = await waitFor(() => {
            const found = reply().querySelector<HTMLElement>('.add-account');
            expect(found).not.toBeNull();
            return found!;
        });
        expect((within(form).getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(false);
        const choices = (within(form).getAllByRole('button') as HTMLButtonElement[]).filter(
            (each) => !['Cancel', 'LinkedIn'].includes(words(each)),
        );
        expect(choices.length).toBeGreaterThan(1);
        for (const each of choices) {
            expect(each.disabled, words(each)).toBe(true);
            fireEvent.click(each);
        }
        // LinkedIn only asks for a name, which stays live; making the link is the act, and is off.
        const linkedin = within(form).getByRole('button', { name: 'LinkedIn' }) as HTMLButtonElement;
        expect(linkedin.disabled).toBe(false);
        fireEvent.click(linkedin);
        const make = await waitFor(() => within(reply()).getByRole('button', { name: 'Make the link' }));
        expect((make as HTMLButtonElement).disabled).toBe(true);
        fireEvent.click(make);
        fireEvent.click(within(reply()).getByRole('button', { name: 'Cancel' }));
        await waitFor(() => expect(reply().querySelector('.add-account')).toBeNull());
        expect(calls(stand, 'setup.replyConnect')).toEqual([]);
    });

    it('keeps Disconnect live, since it only asks for the confirmation, and draws the confirmation off', async () => {
        const { stand } = await renderTab();
        // The card is drawn again on each change, so the row is found afresh each time.
        const rowOf = () =>
            [...document.querySelectorAll<HTMLElement>('.plugin')].find(
                (each) =>
                    within(each).queryByRole('button', { name: 'Disconnect' }) ??
                    within(each).queryByRole('alertdialog'),
            )!;
        const disconnect = within(rowOf()).getByRole('button', { name: 'Disconnect' }) as HTMLButtonElement;
        expect(disconnect.disabled).toBe(false);
        fireEvent.click(disconnect);
        const dialog = await waitFor(() => within(rowOf()).getByRole('alertdialog'));
        const confirm = within(dialog).getByRole('button', { name: /^Disconnect / }) as HTMLButtonElement;
        expect(confirm.disabled).toBe(true);
        expect((within(dialog).getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(false);
        fireEvent.click(confirm);
        expect(calls(stand, 'setup.disconnectPlugin')).toEqual([]);
    });
});

describe('A chat in a tab', () => {
    async function shown(extra: Partial<Parameters<StubDesktop['keep']>[0]> = {}) {
        const made = await renderTab({
            panel: 'start',
            keep: [{ id: 'conv_1', title: 'Spring outreach', ...extra }],
        });
        await openChat('Spring outreach');
        await waitFor(() => expect(made.stand.asked.some((each) => each.path === '/events')).toBe(true));
        return made;
    }

    it('draws a permission card with no buttons and one sentence that it is answered in Jason Desktop', async () => {
        const { desktop, stand } = await shown();
        flush(() =>
            desktop.emit('conv_1', {
                kind: 'permission',
                request: 'req_1',
                title: 'Run a command',
                detail: 'git status',
                remember: 'git status …',
            }),
        );
        await waitFor(() => screen.getByText('Run a command'));
        expect(screen.getByText('This is answered in Jason Desktop.')).toBeTruthy();
        for (const name of [/^Allow/, /Don't allow/]) expect(screen.queryByRole('button', { name })).toBeNull();
        expect(calls(stand, 'chat.answer')).toEqual([]);
    });

    it('shows the mode as a label, not a control', async () => {
        await shown();
        expect(screen.queryByRole('combobox', { name: 'Access mode' })).toBeNull();
        const label = screen.getByLabelText('Access mode');
        expect(label.tagName).toBe('SPAN');
        expect(label.textContent).toBe('Ask first');
    });

    it.each([
        ['ask_first', false, null],
        ['plan_only', false, null],
        ['auto_edit', true, 'This chat is in Auto-edit. Send messages to it from Jason Desktop.'],
        ['full_access', true, 'This chat is in Full access. Send messages to it from Jason Desktop.'],
        ['some_new_mode', true, 'This chat is in a mode a tab cannot send to. Send messages to it from Jason Desktop.'],
    ])('with the chat in %s the box is locked: %s', async (chatMode, locked, sentence) => {
        const { stand } = await shown({ mode: chatMode as never });
        const box = screen.getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement;
        const send = screen.getByRole('button', { name: 'Send' }) as HTMLButtonElement;
        expect(box.disabled).toBe(locked);
        expect(send.disabled).toBe(locked);
        if (sentence === null) expect(screen.queryByText(/Send messages to it from Jason Desktop/)).toBeNull();
        else expect(screen.getByText(sentence)).toBeTruthy();
        if (locked) expect(calls(stand, 'chat.send')).toEqual([]);
    });

    it('draws no Start afresh, Remove, Open folder, Connect or Not now, and keeps Rename', async () => {
        await shown({ entries: [{ kind: 'notice', text: 'Made a folder', folder: 'spring' }] as never });
        expect(screen.getByText('Made a folder')).toBeTruthy();
        for (const name of ['Start afresh', 'Remove', 'Open folder', 'Not now'])
            expect(screen.queryByRole('button', { name })).toBeNull();
        expect(screen.getByRole('button', { name: 'Rename' })).toBeTruthy();
    });

    it('asks nothing outside the table on any of it', async () => {
        const { stand } = await shown();
        const outside = stand.asked.filter(
            (each) => each.member !== undefined && each.member !== '(not known)' && !table.has(each.member),
        );
        expect(outside).toEqual([]);
    });
});

describe('The start screen in a tab', () => {
    it('offers only the mode the chat starts in: the other access modes are off, with the sentence', async () => {
        await renderTab({ panel: 'start' });
        const form = screen.getByRole('form', { name: 'Start' });
        fireEvent.click(within(form).getByRole('button', { name: /Access mode/ }));
        const menu = screen.getByRole('menu');
        const items = within(menu).getAllByRole('menuitemradio') as HTMLButtonElement[];
        const current = items.find((each) => each.getAttribute('aria-checked') === 'true')!;
        expect(words(current)).toContain('Ask first');
        for (const each of items.filter((one) => one !== current)) {
            expect(each.disabled, words(each)).toBe(true);
            expect(words(each)).toContain(elsewhere);
        }
    });

    it('with a standing choice of Full access for new chats still starts Ask first and sends the first message', async () => {
        const { desktop, stand } = await renderTab({ panel: 'start', standing: true });
        await waitFor(() => expect(fullAccessStanding.value).not.toBeNull());
        const form = screen.getByRole('form', { name: 'Start' });
        expect(within(form).getByRole('button', { name: 'Access mode: Ask first' })).toBeTruthy();
        submitStart('Heads of sales at logistics firms in Poland');
        await waitFor(() => expect(calls(stand, 'chat.send')).toHaveLength(1));
        // No host is named, so the desktop starts the chat in Ask first.
        const created = desktop.calls.filter((each) => each.name === 'chat.create');
        expect(created).toHaveLength(1);
        expect(created[0]!.args.every((each) => each === null || each === undefined)).toBe(true);
        expect(startDraft.value).toBe('');
    });

    it('keeps the words in the box and says why when the chat it made is not one a tab may send to', async () => {
        const { desktop, stand } = await renderTab({ panel: 'start' });
        const create = desktop.chat.create;
        // A desktop that made the chat in Full access in spite of the tab's ask.
        desktop.chat.create = async (...args) => {
            const made = await create(...args);
            if ('refused' in made) return made;
            desktop.kept.get(made.id)!.mode = 'full_access';
            return { ...made, mode: 'full_access' };
        };
        submitStart('Heads of sales at logistics firms in Poland');
        await waitFor(() => expect(startDraft.value).toBe('Heads of sales at logistics firms in Poland'));
        expect(calls(stand, 'chat.send')).toEqual([]);
        await waitFor(() =>
            expect(screen.getAllByText(/Send messages to it from Jason Desktop/).length).toBeGreaterThan(0),
        );
    });
});
