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
import { resetRecent } from './recent';
import { rowRename } from './row-rename';
import { closeMenus } from './side';
import { activePanel, chats, conversations, feedId, here, lines } from './state';

// Rename in the row: an agent's row renames its campaign through `campaign.update`, with the person as actor; a chat's
// row renames its conversation through the desktop. The name becomes a field in place: Enter keeps it, Escape and losing
// the focus leave it as it was, and a refusal is said under the field, which stays open with what was typed. No chat is
// shown. Read against the recorded draft "New agent 1" (cmp_1) and its rename.

type Place = 'window' | 'tab';

function api(update: FixtureName = 'campaign.update/new-agent-rename'): FakeApi {
    return new FakeApi()
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/new-agent')
        .answerAnyRequest('campaign.summary', 'campaign.summary/new-agent')
        .answerAnyRequest('campaign.get', 'campaign.get/new-agent')
        .answerAnyRequest('campaign.update', update)
        .answerAnyRequest('campaign.list_contacts', 'campaign.list_contacts/summary-waiting')
        .answerAnyRequest('campaign.activity', 'campaign.activity/all-7-days')
        .answerAnyRequest('reply.count', 'reply.count/period-none')
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign')
        .answerAnyRequest('plugin.list', 'plugin.list/none')
        .answerAnyRequest('route.list', 'route.list/empty')
        .answerAnyRequest('ui.tokens', 'ui.tokens/two')
        .answerAnyRequest('system.info', 'system.info/with-ui');
}

/** The console over the draft, its agent with a conversation unless `spoken` is false, and a chat about no agent. */
async function renderAt(
    place: Place,
    fake: FakeApi = api(),
    spoken = true,
): Promise<{ desktop: StubDesktop; fake: FakeApi }> {
    const desktop = stubDesktop({ ...setupFound });
    if (spoken) desktop.keep({ id: 'conv_a', campaignId: 'cmp_1', title: 'New agent 1' });
    desktop.keep({ id: 'conv_n', campaignId: null, title: 'Help me start' });
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
const agentsList = () => side().getByRole('group', { name: 'Agents' });
const chatsList = () => side().getByRole('group', { name: 'Chats' });
const rowIn = (list: () => HTMLElement, name: string) => waitFor(() => within(list()).getByRole('button', { name }));
const updates = (fake: FakeApi) =>
    fake.calls.filter((each) => each.operation === 'campaign.update').map((each) => [each.body, each.kind]);
const renames = (desktop: StubDesktop) =>
    desktop.calls.filter((each) => each.name === 'chat.rename').map((each) => each.args);

/** Opens the row's menu by its three dots and chooses the act named. */
async function chooseFor(list: () => HTMLElement, name: string, act: string): Promise<void> {
    await rowIn(list, name);
    fireEvent.click(within(list()).getByRole('button', { name: `More for ${name}` }));
    const menu = await waitFor(() => document.querySelector<HTMLElement>('.row-menu')!);
    fireEvent.click(within(menu).getByRole('menuitem', { name: act }));
}

const field = (name: string) => side().getByRole<HTMLInputElement>('textbox', { name });

beforeEach(() => {
    closeMenus();
    resetRecent();
    rowRename.value = null;
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    feedId.value = null;
    agentFacts.value = {};
    attention.loaded.value = false;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.summary.value = {};
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    classicCampaigns.campaign.value = null;
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    session.bridge.value = null;
});

describe.each<Place>(['window', 'tab'])('An agent’s row renamed in place, in the %s', (place) => {
    it('renames the campaign with the person as actor, shows no chat, and the row keeps the focus', async () => {
        const { desktop, fake } = await renderAt(place);
        const before = activePanel.value;
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        expect(document.activeElement).toBe(box);
        expect([box.value, box.selectionStart, box.selectionEnd]).toEqual(['New agent 1', 0, 'New agent 1'.length]);
        fireEvent.input(box, { target: { value: 'Polish finance' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await rowIn(agentsList, 'Polish finance');
        // A write: the client names the paired person as its actor.
        expect(updates(fake)).toEqual([[{ campaign_id: 'cmp_1', name: 'Polish finance' }, 'write']]);
        expect(side().queryByRole('textbox')).toBeNull();
        // The row is painted anew with its name: the focus is on it, as drawn now.
        await waitFor(() =>
            expect(document.activeElement).toBe(within(agentsList()).getByRole('button', { name: 'Polish finance' })),
        );
        expect(activePanel.value).toBe(before);
        expect(renames(desktop)).toEqual([]);
    });
});

describe('An agent’s row renamed in place', () => {
    it('is renamed in the row, in the agent’s header and on the Agents page at once, with no list read again', async () => {
        const { fake } = await renderAt('window');
        fireEvent.click(await rowIn(agentsList, 'New agent 1'));
        await waitFor(() => expect(document.querySelector('.agent-head h2')?.textContent).toBe('New agent 1'));
        const lists = fake.calls.filter((each) => each.operation === 'campaign.list').length;
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: 'Polish finance' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await rowIn(agentsList, 'Polish finance');
        await waitFor(() => expect(document.querySelector('.agent-head h2')?.textContent).toBe('Polish finance'));
        expect(fake.calls.filter((each) => each.operation === 'campaign.list')).toHaveLength(lists);
        flush(() => here().dock.show('campaigns'));
        await waitFor(() =>
            expect(
                within(document.querySelector<HTMLElement>('[data-pane="campaigns"]')!).getByText('Polish finance'),
            ).toBeTruthy(),
        );
    });

    it('says the runtime’s refusal under the field, keeps what was typed, and Escape puts the old name back', async () => {
        const { fake } = await renderAt('window', api('campaign.update/new-agent-name-too-long'));
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        const long = 'n'.repeat(201);
        fireEvent.input(box, { target: { value: long } });
        fireEvent.keyDown(box, { key: 'Enter' });
        const alert = await waitFor(() => side().getByRole('alert'));
        expect(alert.textContent).toBe('That name is too long. Keep it to a short line.');
        // The field is described by what was refused, drawn under it.
        const described = document.getElementById(field('Agent name').getAttribute('aria-describedby') ?? '');
        expect(described?.textContent).toBe(alert.textContent);
        expect(field('Agent name').parentElement?.contains(described ?? null)).toBe(true);
        expect(field('Agent name').value).toBe(long);
        expect(document.activeElement).toBe(field('Agent name'));
        expect(updates(fake)).toHaveLength(1);
        fireEvent.keyDown(field('Agent name'), { key: 'Escape' });
        await rowIn(agentsList, 'New agent 1');
        expect(side().queryByRole('textbox')).toBeNull();
        expect(side().queryByRole('alert')).toBeNull();
        expect(document.activeElement).toBe(within(agentsList()).getByRole('button', { name: 'New agent 1' }));
    });

    it('closes with no request on an unchanged name, and refuses an empty one in the row', async () => {
        const { fake } = await renderAt('window');
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        fireEvent.keyDown(await waitFor(() => field('Agent name')), { key: 'Enter' });
        await waitFor(() => expect(side().queryByRole('textbox')).toBeNull());
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: '   ' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        expect((await waitFor(() => side().getByRole('alert'))).textContent).toBe(
            'A name needs at least one character that shows.',
        );
        expect(updates(fake)).toEqual([]);
    });

    it('keeps nothing when the field loses the focus: a stray click renames nothing', async () => {
        const { fake } = await renderAt('window');
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: 'Polish finance' } });
        const elsewhere = side().getByRole('button', { name: 'New chat' });
        elsewhere.focus();
        fireEvent.focusOut(box, { relatedTarget: elsewhere });
        await waitFor(() => expect(side().queryByRole('textbox')).toBeNull());
        await rowIn(agentsList, 'New agent 1');
        expect(updates(fake)).toEqual([]);
    });

    it('renames an agent with no conversation yet: its rename is never held', async () => {
        const { fake } = await renderAt('window', api(), false);
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: 'Polish finance' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await rowIn(agentsList, 'Polish finance');
        expect(updates(fake)).toHaveLength(1);
    });
});

describe('An agent’s rename while its request is in flight, and the row painted again', () => {
    async function sent(update: FixtureName, typing: string) {
        const fake = api(update);
        const release = fake.hold('campaign.update');
        await renderAt('window', fake);
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: typing } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await waitFor(() => expect(field('Agent name').getAttribute('aria-busy')).toBe('true'));
        return { fake, release };
    }

    it('stays open through a lost focus while the request waits, and says the refusal that then comes', async () => {
        const { release } = await sent('campaign.update/new-agent-name-too-long', 'n'.repeat(201));
        const elsewhere = side().getByRole('button', { name: 'New chat' });
        elsewhere.focus();
        fireEvent.focusOut(field('Agent name'), { relatedTarget: elsewhere });
        await new Promise((done) => setTimeout(done, 5));
        expect(field('Agent name').value).toBe('n'.repeat(201));
        release();
        const alert = await waitFor(() => side().getByRole('alert'));
        expect(alert.textContent).toBe('That name is too long. Keep it to a short line.');
        expect(field('Agent name').value).toBe('n'.repeat(201));
        expect(field('Agent name').getAttribute('aria-busy')).toBeNull();
    });

    it('stays open through Escape while the request waits, and closes on the answer with the row focused', async () => {
        const { release } = await sent('campaign.update/new-agent-rename', 'Polish finance');
        fireEvent.keyDown(field('Agent name'), { key: 'Escape' });
        await new Promise((done) => setTimeout(done, 5));
        expect(field('Agent name')).toBeTruthy();
        release();
        await waitFor(() => expect(side().queryByRole('textbox')).toBeNull());
        await waitFor(() =>
            expect(document.activeElement).toBe(within(agentsList()).getByRole('button', { name: 'Polish finance' })),
        );
    });

    it('says any other refusal in the runtime’s own words', async () => {
        await renderAt('window', api('campaign.get/unknown'));
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: 'Polish finance' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        expect((await waitFor(() => side().getByRole('alert'))).textContent).toBe("No campaign with id 'cmp_3'.");
    });

    it('keeps the field, its text, its focus and its refusal’s one node when the lists change under it', async () => {
        await renderAt('window', api('campaign.update/new-agent-name-too-long'));
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        const box = await waitFor(() => field('Agent name'));
        fireEvent.input(box, { target: { value: 'n'.repeat(201) } });
        fireEvent.keyDown(box, { key: 'Enter' });
        const alert = await waitFor(() => side().getByRole('alert'));
        fireEvent.input(field('Agent name'), { target: { value: 'Half typed' } });
        flush(() => {
            consoleCampaigns.campaigns.value = consoleCampaigns.campaigns.value.map((each) => ({ ...each }));
            conversations.value = [...conversations.value];
        });
        expect(field('Agent name').value).toBe('Half typed');
        expect(document.activeElement).toBe(field('Agent name'));
        expect(side().getByRole('alert')).toBe(alert);
    });

    it('is closed when its row is no longer drawn', async () => {
        await renderAt('window', api(), false);
        await chooseFor(agentsList, 'New agent 1', 'Rename');
        await waitFor(() => field('Agent name'));
        flush(() => {
            consoleCampaigns.campaigns.value = [];
        });
        await waitFor(() => expect(rowRename.value).toBeNull());
    });
});

describe('A chat’s row renamed in place', () => {
    it('renames the conversation through the desktop, with no campaign changed and no chat shown', async () => {
        const { desktop, fake } = await renderAt('window');
        const before = activePanel.value;
        await chooseFor(chatsList, 'Help me start', 'Rename conversation');
        const box = await waitFor(() => field('Conversation name'));
        expect(box.value).toBe('Help me start');
        fireEvent.input(box, { target: { value: 'Dentists in Ohio' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        await rowIn(chatsList, 'Dentists in Ohio');
        expect(renames(desktop)).toEqual([['conv_n', 'Dentists in Ohio']]);
        expect(updates(fake)).toEqual([]);
        await waitFor(() =>
            expect(document.activeElement).toBe(within(chatsList()).getByRole('button', { name: 'Dentists in Ohio' })),
        );
        expect(activePanel.value).toBe(before);
    });

    it('says the desktop’s refusal under the field in its own words, and keeps what was typed', async () => {
        const { desktop } = await renderAt('window');
        desktop.refuse.set('chat.rename', 'That name is too long. Keep it to a short line.');
        await chooseFor(chatsList, 'Help me start', 'Rename conversation');
        const box = await waitFor(() => field('Conversation name'));
        fireEvent.input(box, { target: { value: 'Dentists' } });
        fireEvent.keyDown(box, { key: 'Enter' });
        expect((await waitFor(() => side().getByRole('alert'))).textContent).toBe(
            'That name is too long. Keep it to a short line.',
        );
        expect(field('Conversation name').value).toBe('Dentists');
        expect(chats.value['conv_n']?.entries ?? []).toEqual([]);
    });

    it('cuts what is typed at the conversation name’s sixty characters', async () => {
        await renderAt('window');
        await chooseFor(chatsList, 'Help me start', 'Rename conversation');
        const box = await waitFor(() => field('Conversation name'));
        fireEvent.input(box, { target: { value: 'x'.repeat(70) } });
        expect(field('Conversation name').value).toBe('x'.repeat(60));
    });
});
