import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { noDesktop, setPlace } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversationsFailure, conversations, lines, setup, setupError } from './state';

// The console in a browser tab with no desktop: the same console, over an object that does nothing of the desktop's. It
// reads the runtime as it does everywhere; where the chat would be it says where the chat is; every control the desktop
// alone could answer is drawn off, in its place, with one sentence.

const needs = 'This needs Jason Desktop.';

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const side = () => within(document.querySelector<HTMLElement>('.side')!);

async function renderNone(): Promise<FakeApi> {
    const none = noDesktop();
    setPlace('none', none);
    await loadMode(none);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two')
        .answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answerAnyRequest('preference.set', 'preference.set/sender')
        .answerAnyRequest('campaign.activity', 'campaign.activity/all-30-days')
        .answerAnyRequest('reply.count', 'reply.count/every-campaign');
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
    await session.bootstrap();
    render(<Console api={fake} desktop={none} stream={stubStream().stream} bootstrapped />);
    await waitFor(() => document.querySelector('.side'));
    return fake;
}

beforeEach(() => {
    setup.value = null;
    setupError.value = null;
    chats.value = {};
    conversations.value = [];
    conversationsFailure.value = null;
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

afterEach(() => {
    cleanup();
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe('The console in a tab with no desktop', () => {
    it('says where the chat is, with the sentence and the way to get Jason Desktop, and draws no box', async () => {
        await renderNone();
        await waitFor(() => screen.getByRole('heading', { name: 'Chat', level: 1 }));
        expect(screen.getAllByRole('note').some((each) => words(each) === needs)).toBe(true);
        expect(screen.getByText(/The chat with the AI is part of the desktop application/)).toBeTruthy();
        const link = screen.getByRole('link', { name: 'Get Jason Desktop from the releases page' });
        expect(link.getAttribute('href')).toBe('https://github.com/reply-team/jason-ai/releases');
        expect(screen.queryByRole('form', { name: 'Start' })).toBeNull();
        expect(screen.queryByRole('textbox', { name: 'Who do you want to reach?' })).toBeNull();
    });

    it('draws the New chat entry off with the sentence, and says nothing failed', async () => {
        await renderNone();
        fireEvent.click(side().getByRole('button', { name: 'More to create' }));
        const entry = within(screen.getByRole('menu', { name: 'Create' })).getByRole('menuitem', {
            name: /New chat/,
        }) as HTMLButtonElement;
        expect(entry.disabled).toBe(true);
        expect(entry.title).toBe(needs);
        expect(conversationsFailure.value).toBeNull();
    });

    it('draws the Demo | Live switch off with the sentence, and no Open in', async () => {
        await renderNone();
        const group = screen.getByRole('group', { name: 'Data' });
        for (const each of within(group).getAllByRole('button') as HTMLButtonElement[]) {
            expect(each.disabled).toBe(true);
            expect(each.title).toBe(needs);
        }
        expect(document.querySelector('[data-slot="open-in"]')?.children).toHaveLength(0);
    });

    it('reads the runtime as ever: the agents are listed, and their Chat buttons are off', async () => {
        await renderNone();
        fireEvent.click(side().getByRole('button', { name: /^Agents/ }));
        await waitFor(() => expect(document.querySelectorAll('table.t tbody tr').length).toBeGreaterThan(0));
        for (const chat of screen.getAllByRole('button', { name: 'Chat' }) as HTMLButtonElement[]) {
            expect(chat.disabled).toBe(true);
            expect(chat.title).toBe(needs);
        }
        for (const open of screen.getAllByRole('button', { name: 'Open' }) as HTMLButtonElement[])
            expect(open.disabled).toBe(false);
    });

    it('says in Connect that it needs Jason Desktop, and offers no act', async () => {
        await renderNone();
        fireEvent.click(side().getByRole('button', { name: 'More' }));
        fireEvent.click(side().getByRole('menuitem', { name: /^Connect/ }));
        await waitFor(() => expect(screen.getAllByText(needs).length).toBeGreaterThan(0));
        expect(document.querySelector('.plugin')).toBeNull();
    });

    it('draws every way to a chat off with the sentence: New GTM agent, the palette, the Dashboard', async () => {
        await renderNone();
        // The sidebar's own button and the Create menu's entry.
        const button = side().getByRole('button', { name: 'New GTM agent' }) as HTMLButtonElement;
        expect(button.disabled).toBe(true);
        expect(button.title).toBe(needs);
        fireEvent.click(side().getByRole('button', { name: 'More to create' }));
        const entry = within(screen.getByRole('menu', { name: 'Create' })).getByRole('menuitem', {
            name: /New GTM agent/,
        }) as HTMLButtonElement;
        expect(entry.disabled).toBe(true);
        expect(entry.title).toBe(needs);
        // The palette lists them, off, and choosing one does nothing but keep the palette open.
        fireEvent.click(side().getAllByRole('button', { name: /^Search/ })[0]!);
        const box = await screen.findByRole('dialog', { name: 'Search' });
        fireEvent.change(within(box).getByRole('combobox'), { target: { value: 'New' } });
        const options = await waitFor(() => {
            const found = within(box)
                .getAllByRole('option')
                .filter((each) => /New (GTM agent|chat)/.test(words(each)));
            expect(found).toHaveLength(2);
            return found;
        });
        for (const option of options) {
            expect(option.getAttribute('aria-disabled')).toBe('true');
            expect(words(option)).toContain(needs);
            fireEvent.click(option);
        }
        expect(screen.queryByRole('dialog', { name: 'Search' })).not.toBeNull();
        expect(screen.queryByRole('form', { name: 'Start' })).toBeNull();
        fireEvent.keyDown(box, { key: 'Escape' });
        // The Dashboard's New.
        fireEvent.click(side().getByRole('button', { name: 'Dashboard' }));
        const dash = await waitFor(() => screen.getAllByRole('button', { name: /^New/ }));
        for (const each of dash as HTMLButtonElement[]) expect(each.disabled, words(each)).toBe(true);
    });

    it('leaves live everything that does not go through the desktop: the agents, Needs you, Settings, System', async () => {
        await renderNone();
        for (const name of ['Dashboard', /^Needs you/, 'Agents']) {
            const each = side().getByRole('button', { name }) as HTMLButtonElement;
            expect(each.disabled, String(name)).toBe(false);
        }
        fireEvent.click(side().getByRole('button', { name: 'More' }));
        for (const item of within(screen.getByRole('menu', { name: 'More' })).getAllByRole(
            'menuitem',
        ) as HTMLButtonElement[]) {
            if (/^Connect/.test(words(item))) continue;
            expect(item.disabled, words(item)).toBe(false);
        }
    });

    it('draws the name and tone form and saves it, since it needs only the Runtime API', async () => {
        const fake = await renderNone();
        fireEvent.click(side().getByRole('button', { name: 'Settings' }));
        const who = await waitFor(() => screen.getByRole('form', { name: 'Who writes' }));
        await waitFor(() =>
            expect((within(who).getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('Marta Lind'),
        );
        fireEvent.change(within(who).getByRole('textbox', { name: 'Company' }), {
            target: { value: 'Example Analytics Ltd' },
        });
        fireEvent.click(within(who).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(who).getByText('Saved.'));
        expect(fake.calls.filter((each) => each.operation === 'preference.set')).toHaveLength(1);
    });
});
