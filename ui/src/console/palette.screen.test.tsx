import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, feedId } from './state';

// The search palette: its row in the sidebar and the key it names, the shortcut, the box, what it finds and where each
// kind of entry goes. The people it finds come from `contact.list` as the pickers ask it, recorded by the runtime's own
// tests, and only once something is typed.

const placeholder = 'Search agents, people, pages and settings';

function renderConsole(options: { platform?: 'darwin' | 'win32'; desktop?: StubDesktop } = {}) {
    const desktop = options.desktop ?? stubDesktop();
    if (options.platform) Object.assign(desktop, { platform: options.platform });
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer(
            'campaign.summary',
            'campaign.summary/all',
            'campaign.summary/some',
            'campaign.summary/everyone-without-archived',
        )
        .answer(
            'contact.list',
            'contact.list/campaign-picker-zoe',
            'contact.list/query-email',
            'contact.list/by-activity',
        )
        .answer('contact.get', 'contact.get/unrouted')
        .answer('campaign.get', 'campaign.get/details')
        .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
        .answer('journal.list', 'journal.list/by-campaign');
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

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const menu = () => side().getByRole('navigation', { name: 'Menu' });
const box = () => screen.queryByRole('dialog', { name: 'Search' });
const input = () => screen.getByPlaceholderText(placeholder);
const rows = () => {
    const list = screen.queryByRole('listbox');
    return list === null ? [] : within(list).getAllByRole('option');
};
const labels = () => rows().map((each) => each.querySelector('.palette-label')?.textContent);
const kinds = () => rows().map((each) => each.querySelector('.palette-kind')?.textContent);
const picked = () => rows().findIndex((each) => each.getAttribute('aria-selected') === 'true');
const inFront = () =>
    Array.from(document.querySelectorAll('.dv-tab.dv-active-tab')).map((tab) => tab.textContent?.trim());
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const type = (text: string) => fireEvent.change(input(), { target: { value: text } });
const opened = async () => {
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    await waitFor(() => expect(box()).not.toBeNull());
};

/** Two conversations the desktop kept: an agent's and a set-up chat. */
function kept(): StubDesktop {
    const desktop = stubDesktop();
    desktop.keep({ id: 'conv_01JDKEPT0000000000000000A1', campaignId: 'cmp_2', title: 'Autumn outreach' });
    desktop.keep({ id: 'conv_01JDKEPT0000000000000000A2', title: 'Pricing questions' });
    return desktop;
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.detail.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the Search row', () => {
    it('is the first row of the menu, before Feed, with the key named as Ctrl K here', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        await waitFor(() => within(menu()).getByRole('button', { name: /^Feed/ }));
        const names = within(menu())
            .getAllByRole('button')
            .map((each) => each.textContent?.replace(/\s+/g, ' ').trim().replace(/ \d+$/, ''));
        expect(names.slice(0, 2)).toEqual(['SearchCtrl K', 'Feed']);
        const row = within(menu()).getByRole('button', { name: /^Search/ });
        expect(row.querySelector('.kbd')?.textContent).toBe('Ctrl K');
        expect(row.getAttribute('aria-keyshortcuts')).toBe('Control+K');
    });

    it('names the key with the Command sign where the window is on macOS', async () => {
        renderConsole({ platform: 'darwin' });
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const row = await within(menu()).findByRole('button', { name: /^Search/ });
        expect(row.querySelector('.kbd')?.textContent).toBe('⌘K');
        expect(row.getAttribute('aria-keyshortcuts')).toBe('Meta+K');
    });

    it('opens the palette when clicked', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(await within(menu()).findByRole('button', { name: /^Search/ }));
        await waitFor(() => expect(box()).not.toBeNull());
        expect(document.activeElement).toBe(input());
    });
});

describe('the shortcut', () => {
    it('opens with Ctrl+K and with Meta+K, and Escape closes it', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(box()).toBeNull();
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        fireEvent.keyDown(window, { key: 'Escape' });
        await waitFor(() => expect(box()).toBeNull());
        fireEvent.keyDown(window, { key: 'K', metaKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        fireEvent.keyDown(input(), { key: 'Escape' });
        await waitFor(() => expect(box()).toBeNull());
        // K alone, or with Shift, is a letter, not the shortcut.
        fireEvent.keyDown(window, { key: 'k' });
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true, shiftKey: true });
        expect(box()).toBeNull();
    });

    it('closes on a click beside the box, and opens empty again', async () => {
        renderConsole();
        await opened();
        type('peo');
        fireEvent.mouseDown(document.querySelector('.palette-away')!);
        await waitFor(() => expect(box()).toBeNull());
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect((input() as HTMLInputElement).value).toBe(''));
    });
});

describe('the palette', () => {
    it('has one input with the placeholder, and lists every entry but people while nothing is typed', async () => {
        const { fake } = renderConsole({ desktop: kept() });
        await opened();
        expect(input().getAttribute('placeholder')).toBe(placeholder);
        await waitFor(() => expect(labels()).toContain('Autumn outreach'));
        // At most twelve rows, the agents and chats first; each says its kind on the right.
        expect(rows()).toHaveLength(12);
        expect(kinds().slice(0, 3).sort()).toEqual(['Agent', 'Agent', 'Chat']);
        expect(kinds()).not.toContain('Person');
        expect(fake.calls.filter((each) => each.operation === 'contact.list')).toEqual([]);
    });

    it('has a Page, Settings and Action entry for each place and act the sidebar has, and none of what is not here', async () => {
        renderConsole({ desktop: kept() });
        await opened();
        await waitFor(() => expect(conversations.value.length).toBe(3));
        await waitFor(() => expect(feedId.value).not.toBeNull());
        // Every page the sidebar and More reach, each found by its own name, with its kind on the right.
        for (const [name, word] of [
            ['Feed', 'Page'],
            ['Dashboard', 'Page'],
            ['Needs you', 'Page'],
            ['People', 'Page'],
            ['Agents', 'Page'],
            ['Accounts', 'Page'],
            ['Performance', 'Page'],
            ['Activity', 'Page'],
            ['Work items', 'Page'],
            ['Connect', 'Page'],
            ['System', 'Page'],
            ['Settings', 'Settings'],
            ['New GTM agent', 'Action'],
            ['New chat', 'Action'],
        ] as const) {
            type(name);
            expect(labels()[0], name).toBe(name);
            expect(kinds()[0], name).toBe(word);
        }
        // What nothing of ours stands behind is not drawn.
        for (const left of ['Marketplace', 'Plugin', 'Creator agent', 'App', 'Command', 'Create your plugin']) {
            type(left);
            expect(labels(), left).not.toContain(left);
        }
    });

    it('finds a page, an agent, a chat and a person by what is typed, and says Nothing found otherwise', async () => {
        const { fake } = renderConsole({ desktop: kept() });
        await opened();
        await waitFor(() => expect(conversations.value.length).toBe(3));

        type('work');
        expect(labels()).toEqual(['Work items']);
        expect(kinds()).toEqual(['Page']);

        // An agent of the Agents panel with no conversation is found by its name.
        type('spring');
        expect(labels()).toEqual(['Spring']);
        expect(kinds()).toEqual(['Agent']);

        // An agent that has a conversation is found by the conversation's title, and by its campaign's name too.
        type('autumn');
        expect(labels()).toEqual(['Autumn outreach']);
        expect(kinds()).toEqual(['Agent']);
        type('Spring outreach');
        expect(labels()).toEqual([]);

        type('pricing');
        expect(labels()).toEqual(['Pricing questions']);
        expect(kinds()).toEqual(['Chat']);

        // A word of the kind: Settings.
        type('settings');
        expect(labels()).toEqual(['Settings']);

        // A person is asked for only once something is typed, after a pause.
        expect(fake.calls.filter((each) => each.operation === 'contact.list')).toEqual([]);
        type('zoe');
        await waitFor(() => expect(labels()).toEqual(['Zoe Example, Zoe Labs']));
        expect(kinds()).toEqual(['Person']);
        expect(fake.calls.filter((each) => each.operation === 'contact.list').map((each) => each.body)).toEqual([
            { query: 'zoe', limit: 10 },
        ]);

        type('nothing like it');
        await waitFor(() => screen.getByText('Nothing found'));
        expect(screen.queryByRole('listbox')).toBeNull();
    });

    it('asks once for the text typed after the last key, and the latest answer wins', async () => {
        const { fake } = renderConsole();
        await opened();
        const release = fake.hold('contact.list', (body) => (body as { query?: string }).query === 'zoe');
        type('z');
        type('zo');
        type('zoe');
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'contact.list')).toHaveLength(1));
        // The first answer is still on its way when the text changes to another.
        type('aguilar@');
        await waitFor(() => expect(labels()).toEqual(['Tomás Aguilar, Andina Tech']));
        // The first answer comes in after the second: it must change nothing (without the guard on a stale answer,
        // the person row of the newer text disappears).
        flush(() => release());
        await flush(async () => {
            await new Promise((resolve) => setTimeout(resolve, 30));
        });
        expect(labels()).toEqual(['Tomás Aguilar, Andina Tech']);
        expect(fake.calls.filter((each) => each.operation === 'contact.list').map((each) => each.body)).toEqual([
            { query: 'zoe', limit: 10 },
            { query: 'aguilar@', limit: 10 },
        ]);
    });

    it('shows at most twelve rows', async () => {
        const desktop = stubDesktop();
        for (let at = 0; at < 14; at++)
            desktop.keep({
                id: `conv_01JDKEPT00000000000000${String(at).padStart(4, '0')}`,
                title: `Chat number ${at}`,
            });
        renderConsole({ desktop });
        await opened();
        await waitFor(() => expect(conversations.value.length).toBeGreaterThanOrEqual(14));
        type('');
        expect(rows()).toHaveLength(12);
        type('chat number');
        expect(rows()).toHaveLength(12);
    });

    it('moves with the arrows, selects on hover and goes with Enter', async () => {
        renderConsole();
        await opened();
        type('a');
        expect(picked()).toBe(0);
        fireEvent.keyDown(input(), { key: 'ArrowDown' });
        expect(picked()).toBe(1);
        fireEvent.keyDown(input(), { key: 'ArrowUp' });
        fireEvent.keyDown(input(), { key: 'ArrowUp' });
        expect(picked()).toBe(rows().length - 1);
        fireEvent.mouseMove(rows()[2]!);
        expect(picked()).toBe(2);
        type('activity');
        expect(picked()).toBe(0);
        fireEvent.keyDown(input(), { key: 'Enter' });
        await waitFor(() => expect(box()).toBeNull());
        await waitFor(() => expect(inFront()).toContain('Activity'));
    });
});

describe('the box as a modal', () => {
    const composer = () => document.querySelector<HTMLTextAreaElement>('textarea')!;
    const asked = (fake: FakeApi) => fake.calls.filter((each) => each.operation === 'contact.list').length;

    it('opens with Ctrl+K while the focus is in another input, and Escape gives the focus back to it', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        composer().focus();
        fireEvent.keyDown(composer(), { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        expect(document.activeElement).toBe(input());
        fireEvent.keyDown(input(), { key: 'Escape' });
        await waitFor(() => expect(box()).toBeNull());
        expect(document.activeElement).toBe(composer());
    });

    it('gives the focus back after a click beside the box, but not after choosing an entry', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        composer().focus();
        fireEvent.keyDown(composer(), { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        fireEvent.mouseDown(document.querySelector('.palette-away')!);
        await waitFor(() => expect(box()).toBeNull());
        expect(document.activeElement).toBe(composer());

        fireEvent.keyDown(composer(), { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('activity');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Activity'));
        expect(box()).toBeNull();
        expect(document.activeElement).not.toBe(composer());
    });

    it('keeps Tab and Shift+Tab inside the box', async () => {
        renderConsole();
        await opened();
        expect(fireEvent.keyDown(input(), { key: 'Tab' })).toBe(false);
        expect(fireEvent.keyDown(input(), { key: 'Tab', shiftKey: true })).toBe(false);
        expect(document.activeElement).toBe(input());
    });

    it('asks for no one once the box is cleared again', async () => {
        const { fake } = renderConsole();
        await opened();
        type('zoe');
        await waitFor(() => expect(labels()).toEqual(['Zoe Example, Zoe Labs']));
        expect(asked(fake)).toBe(1);
        type('');
        await flush(async () => {
            await new Promise((resolve) => setTimeout(resolve, 400));
        });
        expect(asked(fake)).toBe(1);
        expect(kinds()).not.toContain('Person');
    });

    it('does not show the people of one opening in the next', async () => {
        const { fake } = renderConsole();
        await opened();
        type('zoe');
        await waitFor(() => expect(kinds()).toEqual(['Person']));
        fireEvent.keyDown(window, { key: 'Escape' });
        await waitFor(() => expect(box()).toBeNull());
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        // The answer to the second asking is held, so only a leftover could show.
        fake.hold('contact.list');
        type('zoe');
        await flush(async () => {
            await new Promise((resolve) => setTimeout(resolve, 400));
        });
        expect(kinds()).not.toContain('Person');
    });
});

describe('where each kind goes', () => {
    it('opens a panel as its row does, and a screen at the address it registers', async () => {
        renderConsole();
        await opened();
        type('needs');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Needs you'));
        expect(box()).toBeNull();

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('people');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(route.value.screen).toBe('people'));

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('settings');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(route.value.screen).toBe('settings'));

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('connect');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Connect'));
    });

    it('opens the actions as the button and the caret do: Start, and a new chat', async () => {
        const { desktop } = renderConsole();
        await opened();
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toContain('Agents'));
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('new gtm');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Start'));
        expect(calls(desktop, 'chat.create')).toEqual([]);

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('new chat');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(calls(desktop, 'chat.create')).toHaveLength(1));
    });

    it('opens an agent’s chat and a chat as a Recent row opens them, and an agent with none at its page', async () => {
        renderConsole({ desktop: kept() });
        await opened();
        await waitFor(() => expect(conversations.value.length).toBe(3));
        type('autumn');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Autumn outreach'));

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('pricing');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(inFront()).toContain('Pricing questions'));

        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        await waitFor(() => expect(box()).not.toBeNull());
        type('spring');
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns', id: 'cmp_1' }));
    });

    it('opens a person as the People screen does', async () => {
        renderConsole();
        await opened();
        type('aguilar@');
        await waitFor(() => expect(kinds()).toEqual(['Person']));
        fireEvent.click(rows()[0]!);
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'people', id: 'cnt_3' }));
        // The person's own screen reads more than any recording holds for this person; what it reads is that screen's
        // test's matter, and this row asks only where the click went.
        watch(new FakeApi());
        expect(box()).toBeNull();
    });
});
