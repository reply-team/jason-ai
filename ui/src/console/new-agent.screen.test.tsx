import { act as flush, cleanup, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { loadMode, mode } from '../platform/mode';
import { setPlace, tabDesktop } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import type { DesktopSetup } from '../platform/desktop';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { openingWaiting } from './chat';
import { agentFailure, agentMaking } from './new-agent';
import { closeMenus } from './side';
import { startWaiting } from './start';
import { forgetWaiting } from './waiting';
import { activePanel, chats, conversations, feedId, here, lines, setup, setupError } from './state';

// New GTM agent makes the agent at the click: the desktop makes its campaign and its conversation, and the agent's place
// opens on its Chat, where the empty chat asks Start's question with Start's box, pickers and chips. A draft with no
// brief and no one in it offers no Start yet. A click the desktop refused says why in the sidebar, with Try again, which
// sends the same click.

type Place = 'window' | 'tab';

const noHost: DesktopSetup = { ...setupFound, hosts: setupFound.hosts.map((each) => ({ ...each, found: false })) };

/**
 * The draft the click made, as the runtime's own tests recorded it: "New agent 1", no brief and no one in it. A test
 * may give it a brief, or counts that hold people.
 */
function api(
    summary: FixtureName = 'campaign.summary/new-agent',
    get: FixtureName = 'campaign.get/new-agent',
): FakeApi {
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/new-agent')
        .answerAnyRequest('campaign.summary', summary)
        .answerAnyRequest('campaign.get', get)
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('campaign.activity', 'campaign.activity/all-7-days')
        .answerAnyRequest('reply.count', 'reply.count/period-none')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

async function renderAt(
    place: Place,
    fake: FakeApi = api(),
    status: DesktopSetup = setupFound,
): Promise<{ desktop: StubDesktop; fake: FakeApi }> {
    const desktop = stubDesktop({ ...status });
    // The campaign the runtime makes for the click, as the recorded draft names it.
    desktop.agentCampaign = { id: 'cmp_1', name: 'New agent 1' };
    let used: Parameters<typeof Console>[0]['desktop'] = desktop;
    let held: Bridge | null = null;
    if (place === 'tab') {
        held = new Bridge({ port: 43210, secret: standInSecret }, standInBridge(desktop).fetch);
        used = tabDesktop(held);
        setPlace('tab', used);
    } else setPlace('window');
    await loadMode(used);
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'chat' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    session.bridge.value = held;
    render(<Console api={fake} desktop={used} stream={stubStream().stream} bootstrapped />);
    await waitFor(() => expect(document.querySelector('[data-pane]')).not.toBeNull());
    await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
    return { desktop, fake };
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const head = () => document.querySelector<HTMLElement>('.agent-head');
const made = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'chat.createAgent');
const shownPane = () => document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
const click = () => fireEvent.click(side().getByRole('button', { name: 'New GTM agent' }));

/** The agent's place once the click made it: its header, and its Chat asking Start's question. */
async function opened(): Promise<{ header: HTMLElement; opening: HTMLElement }> {
    const header = await waitFor(() => {
        expect(head()?.querySelector('h2')?.textContent).toBe('New agent 1');
        return head()!;
    });
    const opening = await waitFor(() => {
        const found = shownPane().querySelector<HTMLElement>('.agent-opening');
        expect(found).not.toBeNull();
        return found!;
    });
    return { header, opening };
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
    agentFailure.value = null;
    agentMaking.value = false;
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
    startWaiting.value = null;
    openingWaiting.value = {};
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    session.bridge.value = null;
});

describe.each<Place>(['window', 'tab'])('New GTM agent in the %s', (place) => {
    it("makes the agent at the click and opens its Chat, which asks Start's question with Start's box", async () => {
        const { desktop } = await renderAt(place);
        click();
        const { opening } = await opened();
        expect(made(desktop)).toHaveLength(1);
        expect(desktop.calls.some((each) => each.name === 'chat.create')).toBe(false);
        expect(location.hash).toBe('#/campaigns/cmp_1/chat');
        expect(within(opening).getByRole('heading', { name: 'Who should New agent 1 reach?', level: 2 })).toBeTruthy();
        // Start's box, with its two menus and its arrow, under the question; the chips under it; no second box below.
        const box = within(opening).getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
        expect(box.placeholder).toBe(
            'Tell Jason what you sell and who you want to reach. A website link is enough to start.',
        );
        expect(within(opening).getByRole('button', { name: 'Send' })).toBeTruthy();
        expect(shownPane().querySelectorAll('textarea[aria-label="Your message"]')).toHaveLength(1);
        expect(shownPane().querySelector('.sugs')).not.toBeNull();
        expect(opening.compareDocumentPosition(shownPane().querySelector('.sugs')!)).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
        // The conversation is kept from the click: Recent lists it at once.
        await waitFor(() =>
            expect(
                within(side().getByRole('navigation', { name: 'Conversations' })).getByRole('button', {
                    name: 'New agent 1',
                }),
            ).toBeTruthy(),
        );
    });

    it('sends the first message from the question into the agent’s own conversation, and the chat takes its place', async () => {
        const { desktop } = await renderAt(place);
        click();
        const { opening } = await opened();
        const conversation = [...desktop.kept.values()].find((each) => each.madeAtClick === true)?.id;
        expect(conversation).toBeDefined();
        const box = within(opening).getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
        fireEvent.change(box, { target: { value: 'Heads of finance at Polish SaaS firms' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'chat.send').map((each) => each.args[0])).toEqual([
                conversation,
            ]),
        );
        await waitFor(() => expect(shownPane().querySelector('.agent-opening')).toBeNull());
        expect(shownPane().querySelectorAll('textarea[aria-label="Your message"]')).toHaveLength(1);
        // The box the person typed in keeps the focus where the chat takes the opening's place.
        await waitFor(() =>
            expect(document.activeElement).toBe(
                within(shownPane()).getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }),
            ),
        );
    });

    it('offers no Start while the draft has no brief and no one in it', async () => {
        await renderAt(place);
        click();
        const { header } = await opened();
        expect(within(header).getByText('Draft')).toBeTruthy();
        await waitFor(() => expect(agentFacts.value['cmp_1']).toBeDefined());
        expect(within(header).queryByRole('button', { name: 'Start' })).toBeNull();
    });

    it('keeps Start for a draft with a brief and no one in it', async () => {
        await renderAt(place, api('campaign.summary/new-agent', 'campaign.get/new-agent-brief'));
        click();
        const { header } = await opened();
        await waitFor(() => expect(within(header).getByRole('button', { name: 'Start' })).toBeTruthy());
    });

    it('keeps Start for a draft with no brief once people are in it', async () => {
        await renderAt(place, api('campaign.summary/all'));
        click();
        const { header } = await opened();
        await waitFor(() => expect(within(header).getByRole('button', { name: 'Start' })).toBeTruthy());
    });

    it('says why a click made no agent, with Try again that sends the same click, and opens nothing meanwhile', async () => {
        const { desktop } = await renderAt(place);
        const before = activePanel.value;
        desktop.refuse.set('chat.createAgent', 'Jason is not running. Start it, then try again.');
        click();
        const alert = await waitFor(() => side().getByRole('alert'));
        expect(alert.textContent).toContain('Jason is not running. Start it, then try again.');
        expect(activePanel.value).toBe(before);
        expect(conversations.value.filter((each) => each.campaignId !== null)).toEqual([]);
        desktop.refuse.delete('chat.createAgent');
        fireEvent.click(within(alert).getByRole('button', { name: 'Try again' }));
        await opened();
        const [first, second] = made(desktop).map((each) => each.args);
        expect(second).toEqual(first);
        expect(side().queryByRole('alert')).toBeNull();
    });
});

describe('New GTM agent after a lost answer', () => {
    it.each<Place>(['window', 'tab'])('in the %s, Close then a new click makes one agent, not two', async (place) => {
        const { desktop } = await renderAt(place);
        const real = desktop.chat.createAgent;
        let lose = true;
        // The first create goes through, and its answer is lost on the way back.
        desktop.chat.createAgent = async (...args: Parameters<typeof real>) => {
            const made = await real(...args);
            if (lose) {
                lose = false;
                throw new Error('The answer was lost.');
            }
            return made;
        };
        click();
        const alert = await waitFor(() => side().getByRole('alert'));
        fireEvent.click(within(alert).getByRole('button', { name: 'Close' }));
        expect(side().queryByRole('alert')).toBeNull();
        click();
        await opened();
        const ids = made(desktop).map((each) => each.args[0]);
        expect(ids).toHaveLength(2);
        expect(ids[1]).toBe(ids[0]);
        expect([...desktop.kept.values()].filter((each) => each.madeAtClick === true)).toHaveLength(1);
        // The click that made it is done with: the next is a new one.
        click();
        await waitFor(() => expect(made(desktop)).toHaveLength(3));
        expect(made(desktop)[2]!.args[0]).not.toBe(ids[0]);
    });
});

describe('New GTM agent with no AI connected', () => {
    const connect = (desktop: StubDesktop) => {
        desktop.status = setupFound;
        flush(() => desktop.changed());
    };
    const sends = (desktop: StubDesktop) =>
        desktop.calls.filter((each) => each.name === 'chat.send').map((each) => [each.args[0], each.args[1]]);

    it('shows the AI card in the opening; its first message waits, shown, with Take back, and goes once an AI is connected', async () => {
        const { desktop } = await renderAt('window', api(), noHost);
        click();
        const { opening } = await opened();
        expect(within(opening).getByRole('region', { name: 'Connect an AI' })).toBeTruthy();
        const box = () => within(opening).getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' });
        fireEvent.change(box(), { target: { value: 'Heads of finance at Polish SaaS firms' } });
        fireEvent.keyDown(box(), { key: 'Enter' });
        const note = await waitFor(() => within(opening).getByRole('status', { name: 'Waiting message' }));
        expect(note.textContent).toContain('Waiting for an AI to be connected.');
        expect(note.textContent).toContain('Heads of finance at Polish SaaS firms');
        expect(box().disabled).toBe(true);
        expect(sends(desktop)).toEqual([]);
        // Taken back, the words are in the box again; sent again, they wait again.
        fireEvent.click(within(note).getByRole('button', { name: 'Take back' }));
        expect(box().value).toBe('Heads of finance at Polish SaaS firms');
        fireEvent.keyDown(box(), { key: 'Enter' });
        await waitFor(() => within(opening).getByRole('status', { name: 'Waiting message' }));
        connect(desktop);
        const conversation = [...desktop.kept.values()].find((each) => each.madeAtClick === true)!.id;
        await waitFor(() => expect(sends(desktop)).toEqual([[conversation, 'Heads of finance at Polish SaaS firms']]));
    });

    it('carries a message Start holds into the new agent’s opening, where it waits and goes, never into a set-up chat', async () => {
        const { desktop } = await renderAt('window', api(), noHost);
        const start = await waitFor(() => document.querySelector<HTMLFormElement>('form[aria-label="Start"]')!);
        const startBox = within(start).getByRole<HTMLTextAreaElement>('textbox');
        fireEvent.change(startBox, { target: { value: 'Founders of seed-stage fintechs' } });
        fireEvent.keyDown(startBox, { key: 'Enter' });
        await waitFor(() => expect(startWaiting.value).toBe('Founders of seed-stage fintechs'));
        click();
        const { opening } = await opened();
        const note = await waitFor(() => within(opening).getByRole('status', { name: 'Waiting message' }));
        expect(note.textContent).toContain('Founders of seed-stage fintechs');
        expect(startWaiting.value).toBeNull();
        connect(desktop);
        const conversation = [...desktop.kept.values()].find((each) => each.madeAtClick === true)!.id;
        await waitFor(() => expect(sends(desktop)).toEqual([[conversation, 'Founders of seed-stage fintechs']]));
        expect(desktop.calls.some((each) => each.name === 'chat.create')).toBe(false);
    });
});

describe('New GTM agent with no AI connected, while the click is made or a chip is chosen', () => {
    const connect = (desktop: StubDesktop) => {
        desktop.status = setupFound;
        flush(() => desktop.changed());
    };
    const sends = (desktop: StubDesktop) =>
        desktop.calls.filter((each) => each.name === 'chat.send').map((each) => [each.args[0], each.args[1]]);

    it('a message Start holds goes into the agent when an AI connects while the click is being made, never a set-up chat', async () => {
        const { desktop } = await renderAt('window', api(), noHost);
        const start = await waitFor(() => document.querySelector<HTMLFormElement>('form[aria-label="Start"]')!);
        const startBox = within(start).getByRole<HTMLTextAreaElement>('textbox');
        fireEvent.change(startBox, { target: { value: 'Founders of seed-stage fintechs' } });
        fireEvent.keyDown(startBox, { key: 'Enter' });
        await waitFor(() => expect(startWaiting.value).toBe('Founders of seed-stage fintechs'));
        // The desktop answers the click only once the AI has connected.
        let answer!: () => void;
        const answered = new Promise<void>((resolve) => (answer = resolve));
        const real = desktop.chat.createAgent;
        desktop.chat.createAgent = (...args: Parameters<typeof real>) => answered.then(() => real(...args));
        click();
        await waitFor(() => expect(agentMaking.value).toBe(true));
        connect(desktop);
        await waitFor(() => expect(setup.value?.hosts.some((each) => each.found)).toBe(true));
        await flush(async () => {
            await Promise.resolve();
        });
        expect(desktop.calls.some((each) => each.name === 'chat.create')).toBe(false);
        answer();
        // An AI is there by now, so the held message goes at once and the opening gives way to the chat.
        await waitFor(() => expect(head()?.querySelector('h2')?.textContent).toBe('New agent 1'));
        const conversation = [...desktop.kept.values()].find((each) => each.madeAtClick === true)!.id;
        await waitFor(() => expect(sends(desktop)).toEqual([[conversation, 'Founders of seed-stage fintechs']]));
        expect(desktop.calls.some((each) => each.name === 'chat.create')).toBe(false);
    });

    it('a chip in the opening is the first message: it waits, shown, with Take back, and goes once an AI is connected', async () => {
        const { desktop } = await renderAt('window', api(), noHost);
        click();
        const { opening } = await opened();
        const chip = shownPane().querySelector<HTMLButtonElement>('.sugs button')!;
        const words = chip.textContent!.trim();
        fireEvent.click(chip);
        const note = await waitFor(() => within(opening).getByRole('status', { name: 'Waiting message' }));
        expect(note.textContent).toContain(words);
        expect(within(note).getByRole('button', { name: 'Take back' })).toBeTruthy();
        expect(sends(desktop)).toEqual([]);
        connect(desktop);
        const conversation = [...desktop.kept.values()].find((each) => each.madeAtClick === true)!.id;
        await waitFor(() => expect(sends(desktop).map(([id]) => id)).toEqual([conversation]));
    });
});

describe('The bar’s rule in its other places, on the recorded draft', () => {
    it('offers no Start in Recent’s row menu', async () => {
        await renderAt('window');
        click();
        await opened();
        await waitFor(() => expect(agentFacts.value['cmp_1']).toBeDefined());
        const recent = within(side().getByRole('navigation', { name: 'Conversations' }));
        fireEvent.contextMenu(await waitFor(() => recent.getByRole('button', { name: 'New agent 1' })));
        const menu = await waitFor(() => document.querySelector<HTMLElement>('.row-menu')!);
        expect(within(menu).getByRole('menuitem', { name: 'Open' })).toBeTruthy();
        expect(within(menu).queryByRole('menuitem', { name: 'Start' })).toBeNull();
    });

    it('holds the Dashboard’s New GTM agent, busy, while an agent is being made', async () => {
        const { desktop } = await renderAt('window');
        flush(() => here().dock.show('dashboard'));
        const dash = () => document.querySelector<HTMLElement>('[data-pane="dashboard"]')!;
        await waitFor(() => expect(activePanel.value).toBe('dashboard'));
        const button = await waitFor(() =>
            within(dash()).getByRole<HTMLButtonElement>('button', { name: 'New GTM agent' }),
        );
        let answer!: () => void;
        const answered = new Promise<void>((resolve) => (answer = resolve));
        const real = desktop.chat.createAgent;
        desktop.chat.createAgent = (...args: Parameters<typeof real>) => answered.then(() => real(...args));
        fireEvent.click(button);
        await waitFor(() => expect(agentMaking.value).toBe(true));
        const held = within(dash()).getByRole<HTMLButtonElement>('button', { name: 'New GTM agent' });
        expect([held.disabled, held.getAttribute('aria-busy')]).toEqual([true, 'true']);
        answer();
        await opened();
    });
});

describe('The agent made at the click, everywhere the console lists agents', () => {
    it('is a plain draft in Agents, one draft row in Recent, not among the Dashboard’s running agents, and nothing in Needs you', async () => {
        await renderAt('window');
        click();
        await opened();
        const recent = within(side().getByRole('navigation', { name: 'Conversations' }));
        await waitFor(() => expect(recent.getAllByRole('button', { name: 'New agent 1' })).toHaveLength(1));
        // Its row's dot is the draft's, once the list of agents holds it.
        await waitFor(() =>
            expect(recent.getByRole('button', { name: 'New agent 1' }).querySelector('.rdot.draft')).not.toBeNull(),
        );
        // The Dashboard's agents are the running ones: a draft is not among them.
        flush(() => here().dock.show('dashboard'));
        await waitFor(() => expect(activePanel.value).toBe('dashboard'));
        expect(within(shownPane()).queryByText('New agent 1')).toBeNull();
        flush(() => here().dock.show('needs'));
        await waitFor(() => expect(shownPane().querySelector('.needs-empty')).not.toBeNull());
    });
});

describe('New GTM agent with a desktop older than the agent made at the click', () => {
    it('shows Start, as that desktop did', async () => {
        const { desktop } = await renderAt('window');
        delete (desktop.chat as Partial<StubDesktop['chat']>).createAgent;
        flush(() => here().dock.show('campaigns'));
        await waitFor(() => expect(activePanel.value).toBe('campaigns'));
        click();
        await waitFor(() => expect(activePanel.value).toBe('start'));
        expect(made(desktop)).toEqual([]);
    });
});
