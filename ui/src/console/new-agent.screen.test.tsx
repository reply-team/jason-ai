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
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import { agentFacts } from './agent-facts';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { agentFailure, agentMaking } from './new-agent';
import { closeMenus } from './side';
import { forgetWaiting } from './waiting';
import { activePanel, chats, conversations, feedId, here, lines, setup, setupError } from './state';

// New GTM agent makes the agent at the click: the desktop makes its campaign and its conversation, and the agent's place
// opens on its Chat, where the empty chat asks Start's question with Start's box, pickers and chips. A draft with no
// brief and no one in it offers no Start yet. A click the desktop refused says why in the sidebar, with Try again, which
// sends the same click.

type Place = 'window' | 'tab';

/** Autumn is a draft with an empty brief; the counts the list reads say who is in it. */
function api(summary: FixtureName = 'campaign.summary/inbox-all'): FakeApi {
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', summary)
        .answerAnyRequest('campaign.get', 'campaign.get/unlinked')
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

async function renderAt(place: Place, fake: FakeApi = api()): Promise<{ desktop: StubDesktop; fake: FakeApi }> {
    const desktop = stubDesktop({ ...setupFound });
    // The campaign the runtime makes for the click, as the recorded draft names it.
    desktop.agentCampaign = { id: 'cmp_2', name: 'Autumn' };
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
        expect(head()?.querySelector('h2')?.textContent).toBe('Autumn');
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
        expect(location.hash).toBe('#/campaigns/cmp_2/chat');
        expect(within(opening).getByRole('heading', { name: 'Who should Autumn reach?', level: 2 })).toBeTruthy();
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
                    name: 'Autumn',
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
    });

    it('offers no Start while the draft has no brief and no one in it, and Start once someone is in it', async () => {
        await renderAt(place);
        click();
        const { header } = await opened();
        expect(within(header).getByText('Draft')).toBeTruthy();
        await waitFor(() => expect(agentFacts.value['cmp_2']).toBeDefined());
        expect(within(header).queryByRole('button', { name: 'Start' })).toBeNull();
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
