import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { journalLine, setupFound, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fromFoot, layOutChats } from '../test/scroll';
import type { FixtureName } from '../test/fixtures';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { reloadConversations } from './chat';
import { closeMenus } from './side';
import { forgetWaiting } from './waiting';
import { activePanel, chats, conversations, feedId, here, lines, setup, setupError } from './state';

// An agent is one place: its chat and its page's tabs under one header, each tab an address of its own. The address is
// the truth for a reload, a deep link, Back and Forward; a pane with no address of its own stands at the chat's.

type Place = 'window' | 'tab';

const spring = 'conv_01JDKEPT0000000000000000S1';
const setupChat = 'conv_01JDKEPT0000000000000000U1';

/** The agents' facts by id, whatever is asked: Spring is a live Sequence, Autumn a draft, Running an Adaptive, Winter archived. */
const agents: Record<string, FixtureName> = {
    cmp_1: 'campaign.get/details',
    cmp_2: 'campaign.get/unlinked',
    cmp_4: 'campaign.get/archived',
};

function api(get: Record<string, FixtureName> = agents): FakeApi {
    const campaignOf = (body: object) => (body as { campaign_id?: string }).campaign_id;
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', (body) => {
            const ids = (body as { campaign_ids?: string[] }).campaign_ids ?? [];
            if (ids.length !== 1) return 'campaign.summary/all';
            return ids[0] === 'cmp_4' ? 'campaign.summary/archived' : 'campaign.summary/some';
        })
        .answerAnyRequest('campaign.get', (body) => get[campaignOf(body) ?? ''])
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('contact.threads', 'contact.threads/two-threads')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

async function renderAt(
    place: Place,
    address = '',
    fake: FakeApi = api(),
): Promise<{ desktop: StubDesktop; fake: FakeApi; journal: StubStream }> {
    const desktop = stubDesktop({ ...setupFound });
    desktop.keep({ id: spring, campaignId: 'cmp_1', title: 'Spring' });
    desktop.keep({ id: setupChat, title: 'Set-up' });
    let used: Parameters<typeof Console>[0]['desktop'] = desktop;
    let held: Bridge | null = null;
    if (place === 'tab') {
        const stand = standInBridge(desktop);
        held = new Bridge({ port: 43210, secret: standInSecret }, stand.fetch);
        used = tabDesktop(held);
        setPlace('tab', used);
    } else setPlace('window');
    await loadMode(used);
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = address;
    route.value = parseRoute(address);
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    session.bridge.value = held;
    const journal = stubStream();
    render(<Console api={fake} desktop={used} stream={journal.stream} bootstrapped />);
    await waitFor(() => expect(document.querySelector('[data-pane]')).not.toBeNull());
    return { desktop, fake, journal };
}

const head = () => document.querySelector<HTMLElement>('.agent-head');
/** The header once it says the agent's name. */
const headOf = (name: string) =>
    waitFor(() => {
        expect(head()?.querySelector('h2')?.textContent).toBe(name);
        return head()!;
    });
const tabsOf = (header: HTMLElement) =>
    within(header)
        .getAllByRole('tab')
        .map((each) => [each.textContent, each.getAttribute('aria-selected')]);
const tab = (name: RegExp | string) => within(head()!).getByRole('tab', { name });
const recent = () =>
    within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });
const springPane = () => document.querySelector<HTMLElement>(`[data-pane="chat:${spring}"]`)!;
const box = () => within(springPane()).getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement;
const back = async (to: string) => {
    flush(() => history.back());
    await waitFor(() => expect(location.hash).toBe(to));
};
const forward = async (to: string) => {
    flush(() => history.forward());
    await waitFor(() => expect(location.hash).toBe(to));
};
const springChatShown = () => waitFor(() => expect(activePanel.value).toBe(`chat:${spring}`));
const pageShown = () => waitFor(() => expect(activePanel.value).toBe('page'));

async function openSpring(): Promise<HTMLElement> {
    fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Spring' })));
    await springChatShown();
    return headOf('Spring');
}

beforeEach(() => {
    closeMenus();
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    feedId.value = null;
    agentFacts.value = {};
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.summary.value = {};
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    classicCampaigns.campaign.value = null;
    forgetWaiting();
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
    session.bridge.value = null;
});

describe.each<Place>(['window', 'tab'])('An agent in the %s', (place) => {
    it('opens from its row in Recent under its header, on its Chat tab, at its own address', async () => {
        await renderAt(place);
        const header = await openSpring();
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
        // The runtime's state and type, as pills; its one move; its tabs with the People tab's count.
        expect(within(header).getByText('Active')).toBeTruthy();
        expect(within(header).getByText('Sequence').dataset['kind']).toBe('sequence');
        expect(within(header).getByRole('button', { name: 'Pause' })).toBeTruthy();
        await waitFor(() =>
            expect(tabsOf(header)).toEqual([
                ['Chat', 'true'],
                ['People 22', 'false'],
                ['Inbox', 'false'],
                ['Results', 'false'],
                ['Settings', 'false'],
            ]),
        );
        // No Close; the chat's own bar holds nothing of the conversation's acts or of the agent.
        expect(within(header).queryByRole('button', { name: 'Close' })).toBeNull();
        expect(within(springPane()).queryByRole('button', { name: 'Rename' })).toBeNull();
        expect(within(springPane()).queryByRole('group', { name: 'This agent' })).toBeNull();
        // Recent marks the agent's row.
        expect(within(recent()).getByRole('button', { name: 'Spring' }).getAttribute('aria-current')).toBe('true');
    });

    it('keeps its header, its draft and the lines that came while a page tab was shown, through Back and Forward', async () => {
        const { desktop } = await renderAt(place);
        const header = await openSpring();
        fireEvent.change(box(), { target: { value: 'half a thought' } });
        fireEvent.click(tab(/^People/));
        await pageShown();
        expect(location.hash).toBe('#/campaigns/cmp_1/people');
        // One header for every tab: the same element, the People tab now chosen, and the page under it.
        expect(head()).toBe(header);
        expect(tab(/^People/).getAttribute('aria-selected')).toBe('true');
        await waitFor(() => expect(document.querySelector('.panel[data-panel="agent"] .people-tab')).not.toBeNull());
        flush(() => desktop.emit(spring, { kind: 'text', text: 'Arrived while People was shown' }));
        // People shown, the agent's row in Recent is still the one marked.
        expect(within(recent()).getByRole('button', { name: 'Spring' }).getAttribute('aria-current')).toBe('true');
        await back('#/campaigns/cmp_1/chat');
        await springChatShown();
        expect(head()).toBe(header);
        expect(box().value).toBe('half a thought');
        expect(within(springPane()).getByText('Arrived while People was shown')).toBeTruthy();
        await forward('#/campaigns/cmp_1/people');
        await pageShown();
        expect(tab(/^People/).getAttribute('aria-selected')).toBe('true');
    });

    it('keeps a running turn’s Thinking… and Stop through the People tab and Back, and Stop stops it', async () => {
        const { desktop } = await renderAt(place);
        // The desktop runs a turn of the agent's conversation.
        desktop.keep({
            id: spring,
            campaignId: 'cmp_1',
            title: 'Spring',
            running: true,
            entries: [{ kind: 'user', text: 'How is it doing?' }],
        });
        await openSpring();
        const running = () => {
            expect(within(springPane()).getByText('Thinking…')).toBeTruthy();
            return within(springPane()).getByRole('button', { name: 'Stop' });
        };
        await waitFor(running);
        fireEvent.click(tab(/^People/));
        await pageShown();
        fireEvent.click(tab('Chat'));
        await springChatShown();
        running();
        fireEvent.click(tab(/^People/));
        await pageShown();
        await back('#/campaigns/cmp_1/chat');
        await springChatShown();
        fireEvent.click(running());
        expect(desktop.calls.filter((each) => each.name === 'chat.stop').map((each) => each.args)).toEqual([[spring]]);
    });

    it('moves its People tab’s line and Add people into the tab, and draws no line of counts twice', async () => {
        await renderAt(place, '#/campaigns/cmp_1/people');
        await headOf('Spring');
        const panel = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.panel[data-panel="agent"] .people-head');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(panel.textContent).toContain('22 people · reply campaign 4711');
        expect(within(panel).getByRole('button', { name: 'Add people' })).toBeTruthy();
        expect(document.querySelector('.panel[data-panel="agent"] .panel-header')).toBeNull();
        expect(document.querySelector('.panel[data-panel="agent"] > .stats')).toBeNull();
    });

    it.each([
        ['#/campaigns/cmp_1', '#/campaigns/cmp_1/chat', 'chat'],
        ['#/campaigns/cmp_1/chat', '#/campaigns/cmp_1/chat', 'chat'],
        ['#/campaigns/cmp_1/people', '#/campaigns/cmp_1/people', 'page'],
        ['#/campaigns/cmp_1/inbox', '#/campaigns/cmp_1/inbox', 'page'],
        ['#/campaigns/cmp_1/settings', '#/campaigns/cmp_1/settings', 'page'],
        ['#/campaigns/cmp_1/details', '#/campaigns/cmp_1/settings', 'page'],
    ])('a reload or a deep link at %s lands at %s, under the header', async (address, lands, pane) => {
        await renderAt(place, address);
        await headOf('Spring');
        await waitFor(() => expect(location.hash).toBe(lands));
        if (pane === 'chat') await springChatShown();
        else await pageShown();
    });

    it('a set-up chat has no header, and stands under the agent’s header once it becomes that agent’s', async () => {
        const { desktop } = await renderAt(place);
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Set-up' })));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        expect(head()).toBeNull();
        // A chat with no agent names no place: the bar holds the window's own default address or the chat's.
        expect(['', '#/chat']).toContain(location.hash);
        desktop.kept.get(setupChat)!.campaignId = 'cmp_2';
        flush(() => desktop.emit(setupChat, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' }));
        await headOf('Autumn');
        await waitFor(() => expect(location.hash).toBe('#/campaigns/cmp_2/chat'));
        // A draft has no type the runtime says, so no type's pill.
        expect(within(head()!).getByText('Draft')).toBeTruthy();
        expect(head()!.querySelector('.agent-kind')).toBeNull();
        expect(within(head()!).getByRole('button', { name: 'Start' })).toBeTruthy();
    });

    it('leaving an agent’s tab for a set-up chat adds one entry, and Back comes back to that tab', async () => {
        await renderAt(place, '#/campaigns/cmp_1/people');
        await headOf('Spring');
        await pageShown();
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Set-up' })));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        expect(location.hash).toBe('#/chat');
        expect(head()).toBeNull();
        // A second pane with no address of its own adds no entry.
        flush(() => here().dock.show('campaigns'));
        expect(activePanel.value).toBe('campaigns');
        expect(location.hash).toBe('#/chat');
        await back('#/campaigns/cmp_1/people');
        await pageShown();
        await headOf('Spring');
        await forward('#/chat');
        await waitFor(() => expect(activePanel.value).toBe('campaigns'));
    });

    it('leaving an agent’s Chat for a panel of the console comes back to that Chat with Back', async () => {
        await renderAt(place);
        await openSpring();
        fireEvent.change(box(), { target: { value: 'kept' } });
        flush(() => here().dock.show('needs'));
        expect(location.hash).toBe('#/chat');
        expect(head()).toBeNull();
        await back('#/campaigns/cmp_1/chat');
        await springChatShown();
        expect(box().value).toBe('kept');
        await forward('#/chat');
        await waitFor(() => expect(activePanel.value).toBe('needs'));
    });

    it('moves between tabs from the keyboard, focus staying on the tab chosen', async () => {
        await renderAt(place);
        await openSpring();
        const chat = tab('Chat');
        chat.focus();
        fireEvent.keyDown(chat, { key: 'ArrowRight' });
        await pageShown();
        expect(location.hash).toBe('#/campaigns/cmp_1/people');
        expect(document.activeElement).toBe(tab(/^People/));
        fireEvent.keyDown(tab(/^People/), { key: 'Home' });
        await springChatShown();
        expect(document.activeElement).toBe(tab('Chat'));
        expect(tab('Chat').tabIndex).toBe(0);
        expect(tab(/^People/).tabIndex).toBe(-1);
    });
});

describe.each<Place>(['window', 'tab'])('An agent with no conversation yet, in the %s', (place) => {
    // Drawing an agent's Chat tab makes nothing: no conversation, no child process, no folder. Its conversation is made
    // at the person's first act that needs one: a send, a chip, or opening one of the composer's menus.
    const creates = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.create');
    const sends = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.send');
    const shown = () => document.querySelector<HTMLElement>(`[data-pane="${activePanel.value}"]`)!;
    const shownBox = () => within(shown()).getByRole('textbox', { name: 'Your message' }) as HTMLTextAreaElement;
    const autumnChatShown = () =>
        waitFor(() => {
            expect(activePanel.value?.startsWith('chat:')).toBe(true);
            expect(within(shown()).getByRole('heading', { name: 'What should Jason do in Autumn?' })).toBeTruthy();
        });
    const autumnApi = () => api().answerAnyRequest('route.resolve', 'route.resolve/no-route');
    const noAutumnRow = () => expect(within(recent()).queryByRole('button', { name: 'Autumn' })).toBeNull();

    it.each(['#/campaigns/cmp_2', '#/campaigns/cmp_2/chat'])(
        'at %s, by a reload or a deep link, draws its Chat with an empty thread and the composer, and makes nothing',
        async (address) => {
            const { desktop } = await renderAt(place, address, autumnApi());
            await headOf('Autumn');
            await autumnChatShown();
            expect(location.hash).toBe('#/campaigns/cmp_2/chat');
            expect(shownBox().value).toBe('');
            expect(creates(desktop)).toEqual([]);
            noAutumnRow();
        },
    );

    it('by its tab, Back and Forward, and the Agents list’s Chat, makes nothing', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/people', autumnApi());
        await headOf('Autumn');
        await pageShown();
        fireEvent.click(tab('Chat'));
        await autumnChatShown();
        fireEvent.click(tab(/^People/));
        await pageShown();
        await back('#/campaigns/cmp_2/chat');
        await autumnChatShown();
        await forward('#/campaigns/cmp_2/people');
        await pageShown();
        flush(() => here().dock.show('campaigns'));
        const row = await waitFor(() => {
            const found = [...document.querySelectorAll<HTMLElement>('[data-pane="campaigns"] tr')].find((each) =>
                each.textContent?.includes('Autumn'),
            );
            expect(found).toBeTruthy();
            return found!;
        });
        fireEvent.click(within(row).getByRole('button', { name: 'Chat' }));
        await autumnChatShown();
        expect(creates(desktop)).toEqual([]);
        noAutumnRow();
    });

    it('a keystroke makes nothing, and the words stay through the tabs', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'not yet' } });
        fireEvent.click(tab(/^People/));
        await pageShown();
        fireEvent.click(tab('Chat'));
        await autumnChatShown();
        expect(shownBox().value).toBe('not yet');
        expect(creates(desktop)).toEqual([]);
    });

    it('a send makes exactly one conversation and sends to it, which Recent lists from then on', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        // The desktop names a conversation it makes after the agent, as the runtime answers it.
        desktop.names.set('cmp_2', 'Autumn');
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'Find me ten founders' } });
        fireEvent.keyDown(shownBox(), { key: 'Enter' });
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        expect(creates(desktop).map((each) => each.args[0])).toEqual(['cmp_2']);
        expect(sends(desktop)[0]!.args[1]).toBe('Find me ten founders');
        await waitFor(() => expect(within(recent()).getByRole('button', { name: 'Autumn' })).toBeTruthy());
        // Still the agent's Chat, under its header, at its address.
        expect(location.hash).toBe('#/campaigns/cmp_2/chat');
        expect(head()?.querySelector('h2')?.textContent).toBe('Autumn');
    });

    it('quick sends make one conversation, the later ones waiting together for the first turn', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        for (const words of ['first', 'second', 'third']) {
            fireEvent.change(shownBox(), { target: { value: words } });
            fireEvent.keyDown(shownBox(), { key: 'Enter' });
        }
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        expect(creates(desktop)).toHaveLength(1);
        expect(sends(desktop)[0]!.args[1]).toBe('first');
        const queued = await waitFor(() => within(shown()).getByRole('group', { name: 'Queued message' }));
        expect(queued.querySelector('.bub')!.textContent).toBe('second' + String.fromCharCode(10, 10) + 'third');
    });

    it('a chip makes exactly one conversation and sends its words', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        const chip = shown().querySelector<HTMLButtonElement>('.sugs .sug')!;
        fireEvent.click(chip);
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        expect(creates(desktop)).toHaveLength(1);
        expect(sends(desktop)[0]!.args[1]).toBeTruthy();
    });

    it('opening the model menu makes exactly one conversation, then opens the menu in it', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        await waitFor(() => expect(within(shown()).getByRole('button', { name: /^Model: / })).toBeTruthy());
        fireEvent.click(within(shown()).getByRole('button', { name: /^Model: / }));
        await waitFor(() =>
            expect(
                within(shown())
                    .getByRole('button', { name: /^Model: / })
                    .getAttribute('aria-expanded'),
            ).toBe('true'),
        );
        expect(creates(desktop)).toHaveLength(1);
        expect(sends(desktop)).toEqual([]);
        expect(location.hash).toBe('#/campaigns/cmp_2/chat');
    });

    it('keeps the focus in the composer when the first send makes the conversation', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        shownBox().focus();
        fireEvent.change(shownBox(), { target: { value: 'Find me ten founders' } });
        fireEvent.keyDown(shownBox(), { key: 'Enter' });
        await waitFor(() => expect(sends(desktop)).toHaveLength(1));
        await waitFor(() => expect(activePanel.value?.startsWith('chat:conv_')).toBe(true));
        await waitFor(() => expect(document.activeElement).toBe(shownBox()));
    });

    it('takes the first message, which makes the conversation, to the foot, and follows the answer from there', async () => {
        const layout = layOutChats();
        try {
            const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
            await autumnChatShown();
            // A first message taller than the view.
            const text = `Find me ten founders. ${'In fintech, in Berlin, with a seed round behind them. '.repeat(6)}`;
            fireEvent.change(shownBox(), { target: { value: text } });
            fireEvent.keyDown(shownBox(), { key: 'Enter' });
            await waitFor(() => expect(sends(desktop)).toHaveLength(1));
            await waitFor(() => expect(activePanel.value?.startsWith('chat:conv_')).toBe(true));
            const scroller = () => shown().querySelector<HTMLElement>('.chat-scroll')!;
            await waitFor(() => expect(scroller().textContent).toContain('Find me ten founders.'));
            await waitFor(() => expect(fromFoot(scroller())).toBe(0));
            expect(scroller().scrollTop).toBeGreaterThan(0);
            const id = sends(desktop)[0]!.args[0] as string;
            flush(() => desktop.emit(id, { kind: 'text', text: `Here they are: ${'a founder, '.repeat(120)}` }));
            await waitFor(() => expect(scroller().textContent).toContain('Here they are:'));
            await waitFor(() => expect(fromFoot(scroller())).toBe(0));
        } finally {
            layout.restore();
        }
    });

    it('opens a composer menu once on Space, whose click comes after the key, and makes one conversation', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        const model = await waitFor(() => within(shown()).getByRole('button', { name: /^Model: / }));
        model.focus();
        // The made chat's menu is opened by a click of the page's own. A browser draws between two such clicks, so a
        // second would close the menu the first opened: they are counted.
        const opened: Element[] = [];
        const click = HTMLElement.prototype.click;
        HTMLElement.prototype.click = function (this: HTMLElement) {
            if (this.getAttribute('aria-haspopup') === 'menu') opened.push(this);
            click.call(this);
        };
        try {
            // A browser opens a button on Space with a click after the key is let go.
            fireEvent.keyDown(model, { key: ' ' });
            fireEvent.click(model);
            await waitFor(() =>
                expect(
                    within(shown())
                        .getByRole('button', { name: /^Model: / })
                        .getAttribute('aria-expanded'),
                ).toBe('true'),
            );
            await flush(() => new Promise((resolve) => setTimeout(resolve, 20)));
        } finally {
            HTMLElement.prototype.click = click;
        }
        expect(opened).toHaveLength(1);
        expect(creates(desktop)).toHaveLength(1);
    });

    it('closes its “…”, whose conversation’s acts all wait for a conversation, on Escape with the focus back, and on Tab', async () => {
        await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        const more = within(head()!).getByRole('button', { name: "More for this agent's conversation" });
        more.focus();
        fireEvent.click(more);
        const menu = within(head()!).getByRole('menu');
        // The agent's folder is there before its conversation is, in the window; a tab opens no folder.
        expect(
            within(menu)
                .getAllByRole('menuitem')
                .map((each) => [each.textContent, (each as HTMLButtonElement).disabled]),
        ).toEqual(
            place === 'window'
                ? [
                      ['Open in File manager', false],
                      ['Open Claude Code in terminal', false],
                      ['Rename conversation', true],
                      ['Start conversation fresh', true],
                      ['Remove conversation', true],
                  ]
                : [['Rename conversation', true]],
        );
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        expect(within(head()!).queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(more);
        fireEvent.click(more);
        expect(within(head()!).getByRole('menu')).toBeTruthy();
        fireEvent.keyDown(document.activeElement!, { key: 'Tab' });
        expect(within(head()!).queryByRole('menu')).toBeNull();
    });

    it('gives way to the conversation made for it elsewhere, carrying the words typed, through Recent and the tabs', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'typed before it was made' } });
        // Another place on the same agent sent, so the desktop holds its conversation, and the list says so.
        const autumn = 'conv_01JDKEPT0000000000000000A1';
        desktop.keep({ id: autumn, campaignId: 'cmp_2', title: 'Autumn' });
        await flush(() => reloadConversations());
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Autumn' })));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${autumn}`));
        fireEvent.click(tab(/^People/));
        await pageShown();
        fireEvent.click(tab('Chat'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${autumn}`));
        expect(location.hash).toBe('#/campaigns/cmp_2/chat');
        expect(shownBox().value).toBe('typed before it was made');
        expect(Object.keys(chats.value).filter((id) => id.startsWith('unmade:'))).toEqual([]);
        await back('#/campaigns/cmp_2/people');
        await back('#/campaigns/cmp_2/chat');
        await waitFor(() => expect(activePanel.value).toBe(`chat:${autumn}`));
        // Its "…" holds the made conversation's acts.
        fireEvent.click(within(head()!).getByRole('button', { name: "More for this agent's conversation" }));
        expect(within(head()!).getByRole('menuitem', { name: 'Rename conversation' })).toHaveProperty(
            'disabled',
            false,
        );
        expect(creates(desktop)).toEqual([]);
    });

    it('gives way to a set-up chat that becomes the agent’s, carrying the words typed', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'typed in the unmade chat' } });
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Set-up' })));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        // The set-up chat is chosen for the agent: it becomes the agent's conversation.
        desktop.kept.get(setupChat)!.campaignId = 'cmp_2';
        flush(() => desktop.emit(setupChat, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' }));
        await waitFor(() => expect(location.hash).toBe('#/campaigns/cmp_2/chat'));
        await waitFor(() => expect(Object.keys(chats.value).filter((id) => id.startsWith('unmade:'))).toEqual([]));
        fireEvent.click(tab(/^People/));
        await pageShown();
        fireEvent.click(tab('Chat'));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        expect(shownBox().value).toBe('typed in the unmade chat');
        expect(creates(desktop)).toEqual([]);
    });

    it('keeps the words of both sides when it gives way to a conversation that holds words of its own', async () => {
        const blank = String.fromCharCode(10, 10);
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'typed in the unmade chat' } });
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Set-up' })));
        await waitFor(() => expect(activePanel.value).toBe(`chat:${setupChat}`));
        fireEvent.change(shownBox(), { target: { value: 'typed in the set-up chat' } });
        // Each holds a message that waits, too.
        const unmade = Object.keys(chats.value).find((id) => id.startsWith('unmade:'))!;
        flush(() => {
            chats.value = {
                ...chats.value,
                [unmade]: { ...chats.value[unmade]!, waiting: 'queued in the unmade chat' },
                [setupChat]: { ...chats.value[setupChat]!, waiting: 'queued in the set-up chat' },
            };
        });
        desktop.kept.get(setupChat)!.campaignId = 'cmp_2';
        flush(() => desktop.emit(setupChat, { kind: 'attached', campaignId: 'cmp_2', title: 'Autumn' }));
        await waitFor(() => expect(Object.keys(chats.value).filter((id) => id.startsWith('unmade:'))).toEqual([]));
        expect(chats.value[setupChat]).toMatchObject({
            draft: ['typed in the unmade chat', 'typed in the set-up chat'].join(blank),
            waiting: ['queued in the unmade chat', 'queued in the set-up chat'].join(blank),
        });
    });

    it('a create refused leaves the words in the box and says why in the chat', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        desktop.refuse.set('chat.create', 'The desktop could not make the conversation.');
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'keep these words' } });
        fireEvent.keyDown(shownBox(), { key: 'Enter' });
        await waitFor(() => expect(within(shown()).getByText(/could not make the conversation/)).toBeTruthy());
        expect(shownBox().value).toBe('keep these words');
        expect(sends(desktop)).toEqual([]);
    });

    it('a create refused after a second send gives both messages back to the box, in the order they were sent', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_2/chat', autumnApi());
        desktop.refuse.set('chat.create', 'The desktop could not make the conversation.');
        await autumnChatShown();
        fireEvent.change(shownBox(), { target: { value: 'first thought' } });
        fireEvent.keyDown(shownBox(), { key: 'Enter' });
        fireEvent.change(shownBox(), { target: { value: 'second thought' } });
        fireEvent.keyDown(shownBox(), { key: 'Enter' });
        await waitFor(() => expect(within(shown()).getByText(/could not make the conversation/)).toBeTruthy());
        await waitFor(() => expect(shownBox().value).toBe('first thought\n\nsecond thought'));
        expect(within(shown()).queryByRole('group', { name: 'Queued message' })).toBeNull();
        expect(creates(desktop)).toHaveLength(1);
        expect(sends(desktop)).toEqual([]);
    });
});

describe('An archived agent’s “…”', () => {
    it('keeps the host in a terminal beside its folder', async () => {
        const { desktop } = await renderAt('window', '#/campaigns/cmp_4/chat');
        const header = await headOf('Winter');
        await waitFor(() => expect(within(header).getByText('Archived')).toBeTruthy());
        fireEvent.click(within(head()!).getByRole('button', { name: "More for this agent's conversation" }));
        fireEvent.click(within(head()!).getByRole('menuitem', { name: 'Open Claude Code in terminal' }));
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'openIn.open').map((each) => each.args)).toEqual([
                ['claude', 'cmp_4', { keep: false }],
            ]),
        );
    });
});

describe.each<Place>(['window', 'tab'])('An archived agent with no conversation, in the %s', (place) => {
    it('says on its Chat tab that it is archived, and draws nothing that could make a conversation', async () => {
        const { desktop } = await renderAt(place, '#/campaigns/cmp_4/chat');
        const header = await headOf('Winter');
        await waitFor(() => expect(within(header).getByText('Archived')).toBeTruthy());
        const line = await waitFor(() => {
            const page = document.querySelector<HTMLElement>('[data-pane="page"]:not([hidden])');
            expect(page).not.toBeNull();
            return within(page!).getByRole('status');
        });
        expect(line.textContent).toBe('This agent is archived.');
        expect(location.hash).toBe('#/campaigns/cmp_4/chat');
        expect(tab('Chat').getAttribute('aria-selected')).toBe('true');
        expect(document.querySelector('[data-pane^="chat:"]')).toBeNull();
        expect(Object.keys(chats.value).filter((id) => id.startsWith('unmade:'))).toEqual([]);
        // Nothing that makes a conversation: no composer, no host, model or access menu, no starter chip.
        const nothingToMake = () => {
            const area = document.querySelector<HTMLElement>('.area')!;
            const shownPane = area.querySelector<HTMLElement>('.pane:not([hidden])')!;
            expect(within(shownPane).queryByRole('textbox')).toBeNull();
            expect(within(shownPane).queryByRole('button')).toBeNull();
            expect(area.querySelector('[data-pane^="chat:"]')).toBeNull();
            expect(shownPane.querySelector('.sug, .crow, [aria-haspopup="menu"]')).toBeNull();
            // Open in the file manager opens the agent's folder, which is there, and the host in a terminal opens a
            // session there: neither makes a conversation.
            expect(
                [...head()!.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].some(
                    (each) =>
                        !each.disabled &&
                        each.textContent !== 'Open in File manager' &&
                        each.textContent !== 'Open Claude Code in terminal',
                ),
            ).toBe(false);
        };
        nothingToMake();
        // No act there makes one: its tab again, a page tab and back, Back and Forward, and its "…".
        fireEvent.click(tab('Chat'));
        fireEvent.click(tab(/^People/));
        await pageShown();
        fireEvent.click(tab('Chat'));
        await waitFor(() => expect(location.hash).toBe('#/campaigns/cmp_4/chat'));
        await back('#/campaigns/cmp_4/people');
        await forward('#/campaigns/cmp_4/chat');
        await waitFor(() =>
            expect(document.querySelector('[data-pane="page"]:not([hidden])')?.textContent).toBe(
                'This agent is archived.',
            ),
        );
        fireEvent.click(within(head()!).getByRole('button', { name: "More for this agent's conversation" }));
        nothingToMake();
        expect(desktop.calls.filter((each) => each.name === 'chat.create')).toEqual([]);
        expect(desktop.calls.filter((each) => each.name === 'chat.send')).toEqual([]);
    });
});

describe('An agent whose conversation the desktop could not open', () => {
    it('says so under its own header on its Chat tab, never another agent’s pane, and tries again', async () => {
        const { desktop } = await renderAt(
            'window',
            '#/campaigns/cmp_2/chat',
            api().answerAnyRequest('route.resolve', 'route.resolve/no-route'),
        );
        // Another agent's Chat was shown first.
        await waitFor(() => expect(activePanel.value).toBe('chat:unmade:cmp_2'));
        desktop.refuse.set('chat.open', 'The conversation’s file could not be read.');
        flush(() => {
            location.hash = '#/campaigns/cmp_1/chat';
        });
        const failure = await waitFor(() => {
            const page = document.querySelector<HTMLElement>('[data-pane="page"]:not([hidden])');
            expect(page).not.toBeNull();
            return within(page!).getByRole('alert');
        });
        expect(failure.textContent).toContain('The conversation’s file could not be read.');
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
        expect(head()?.querySelector('h2')?.textContent).not.toBe('Autumn');
        await waitFor(() => expect(tab('Chat').getAttribute('aria-selected')).toBe('true'));
        expect(document.querySelector('[data-pane^="chat:"]:not([hidden])')).toBeNull();
        desktop.refuse.delete('chat.open');
        fireEvent.click(
            within(failure.closest<HTMLElement>('[data-pane]')!).getByRole('button', { name: 'Try again' }),
        );
        await springChatShown();
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
    });
});

describe('An agent the runtime no longer knows', () => {
    it.each(['#/campaigns/cmp_gone', '#/campaigns/cmp_gone/chat'])(
        'at %s, says the agent no longer exists, draws no composer and makes nothing',
        async (address) => {
            const { desktop } = await renderAt('window', address, api({ ...agents, cmp_gone: 'campaign.get/gone' }));
            await waitFor(() =>
                expect(document.querySelector('[data-pane="page"]')?.textContent).toContain(
                    'The agent in this address no longer exists; it was closed.',
                ),
            );
            expect(activePanel.value).toBe('page');
            expect(location.hash.startsWith('#/campaigns')).toBe(true);
            expect(location.hash).not.toContain('cmp_gone');
            expect(document.querySelector('[data-pane^="chat:"]')).toBeNull();
            expect(Object.keys(chats.value).filter((id) => id.startsWith('unmade:'))).toEqual([]);
            expect(desktop.calls.filter((each) => each.name === 'chat.create')).toEqual([]);
        },
    );
});

describe('The conversation’s acts in the agent’s “…”', () => {
    const more = () => within(head()!).getByRole('button', { name: "More for this agent's conversation" });

    it('opens on the agent’s folder, names the conversation in each of its own acts, from the keyboard, and Escape gives the focus back', async () => {
        await renderAt('window');
        await openSpring();
        more().focus();
        fireEvent.keyDown(more(), { key: 'ArrowDown' });
        const menu = await waitFor(() => within(head()!).getByRole('menu'));
        expect(
            within(menu)
                .getAllByRole('menuitem')
                .map((each) => each.textContent),
        ).toEqual([
            'Open in File manager',
            'Open Claude Code in terminal',
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
        expect(document.activeElement).toBe(within(menu).getByRole('menuitem', { name: 'Open in File manager' }));
        fireEvent.keyDown(menu, { key: 'ArrowDown' });
        expect(document.activeElement).toBe(
            within(menu).getByRole('menuitem', { name: 'Open Claude Code in terminal' }),
        );
        fireEvent.keyDown(menu, { key: 'Escape' });
        expect(within(head()!).queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(more());
    });

    it('opens the agent’s folder in the file manager through Open in, and nothing else', async () => {
        const { desktop } = await renderAt('window');
        await openSpring();
        fireEvent.click(more());
        fireEvent.click(within(head()!).getByRole('menuitem', { name: 'Open in File manager' }));
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'openIn.open').map((each) => each.args)).toEqual([
                ['files', 'cmp_1', { keep: false }],
            ]),
        );
        expect(within(head()!).queryByRole('menu')).toBeNull();
        // The window's Open in keeps the place it had: one agent's item never moves it.
        expect(desktop.openInKept.default).toBe('claude');
    });

    it('opens the host the chat is set to use in a terminal at the agent’s folder, silently, leaving Open in where it was', async () => {
        const { desktop } = await renderAt('window');
        desktop.openInKept.default = 'vscode';
        await openSpring();
        fireEvent.click(more());
        const offered = within(head()!).getByRole('menuitem', { name: 'Open Claude Code in terminal' });
        expect(offered.title).toBe("Opens a new Claude Code session in a terminal at this agent's folder");
        // The chat is set to Codex now: the item follows it.
        await flush(async () => {
            chats.value = { ...chats.value, [spring]: { ...chats.value[spring]!, host: 'codex' } };
        });
        const codex = await waitFor(() => within(head()!).getByRole('menuitem', { name: 'Open Codex in terminal' }));
        fireEvent.click(codex);
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'openIn.open').map((each) => each.args)).toEqual([
                ['codex', 'cmp_1', { keep: false }],
            ]),
        );
        expect(within(head()!).queryByRole('menu')).toBeNull();
        expect(desktop.openInKept.default).toBe('vscode');
        await flush(() => new Promise((resolve) => setTimeout(resolve, 0)));
        expect(document.querySelector('.side [role="alert"]')).toBeNull();
        expect(document.querySelector('.open-in-toast')).toBeNull();
    });

    it('in a tab the desktop opened holds Rename conversation alone', async () => {
        await renderAt('tab');
        await openSpring();
        fireEvent.click(more());
        const menu = within(head()!).getByRole('menu');
        expect(
            within(menu)
                .getAllByRole('menuitem')
                .map((each) => each.textContent),
        ).toEqual(['Rename conversation']);
    });

    it('asks before Remove from a page tab, in the Chat tab, saying the agent stays and where its acts are', async () => {
        const { desktop } = await renderAt('window', '#/campaigns/cmp_1/settings');
        await headOf('Spring');
        await pageShown();
        fireEvent.click(more());
        fireEvent.click(within(head()!).getByRole('menuitem', { name: 'Remove conversation' }));
        await springChatShown();
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
        const asking = await waitFor(() => within(springPane()).getByRole('alertdialog'));
        expect(asking.textContent).toContain(
            'Remove this conversation? Only the chat goes: its record leaves this computer, and the AI host keeps its own transcript of it. The agent stays as it is, and keeps working if it is running. Pausing it is in its header; archiving it is at the end of its Settings tab.',
        );
        fireEvent.click(within(asking).getByRole('button', { name: 'Keep it' }));
        await waitFor(() => expect(document.activeElement).toBe(more()));
        expect(desktop.calls.filter((each) => each.name === 'chat.remove')).toEqual([]);
    });
});

describe('What the header says of the agent', () => {
    it('says no name, state or type it has not read, and follows the journal: an agent archived elsewhere has no move', async () => {
        const fake = api({ ...agents, cmp_1: 'campaign.get/archive-spring' });
        const release = fake.hold('campaign.get');
        const { journal } = await renderAt('window', '', fake);
        fireEvent.click(await waitFor(() => within(recent()).getByRole('button', { name: 'Spring' })));
        await springChatShown();
        // Unread, the header says the name the conversation gives, and no state, type or move.
        const header = await headOf('Spring');
        expect(header.querySelector('.pillc')).toBeNull();
        expect(within(header).queryByRole('button', { name: /Start|Pause|Resume/ })).toBeNull();
        flush(() => release());
        await headOf('Spring launch');
        expect(within(header).getByText('Active')).toBeTruthy();
        // Archived elsewhere: the journal's line reads the agent again.
        fake.answerAnyRequest('campaign.get', 'campaign.get/archive-spring-archived');
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_archived', kind: 'campaign_archived', campaign_id: 'cmp_1' }));
        });
        await waitFor(() => expect(within(header).getByText('Archived')).toBeTruthy(), { timeout: 3000 });
        expect(within(header).queryByRole('button', { name: /Start|Pause|Resume/ })).toBeNull();
        expect(tabsOf(header).map(([name]) => name)).toEqual(['Chat', 'People 22', 'Inbox', 'Results', 'Settings']);
    });

    it('says an archived agent is archived, offers no Start, and keeps its tabs readable', async () => {
        await renderAt('window', '#/campaigns/cmp_4/settings');
        const header = await headOf('Winter');
        expect(within(header).getByText('Archived')).toBeTruthy();
        expect(within(header).queryByRole('button', { name: /Start|Pause|Resume/ })).toBeNull();
        await waitFor(() =>
            expect(tabsOf(header)).toEqual([
                ['Chat', 'false'],
                ['People 0', 'false'],
                ['Inbox', 'false'],
                ['Results', 'false'],
                ['Settings', 'true'],
            ]),
        );
    });

    it('draws the Adaptive pill where the runtime says the agent runs with no provider campaign', async () => {
        await renderAt('window', '#/campaigns/cmp_2/settings', api({ ...agents, cmp_2: 'campaign.get/started' }));
        const header = await headOf('Running');
        await waitFor(() => expect(header.querySelector('.agent-kind')?.textContent).toBe('Adaptive'));
    });
});
