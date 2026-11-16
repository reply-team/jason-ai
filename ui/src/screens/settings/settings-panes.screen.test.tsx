import { readFileSync } from 'node:fs';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiProvider } from '../../api/context';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import * as consoleCampaigns from '../../console/campaigns';
import { attention } from '../../console/needs';
import { chats, conversations } from '../../console/state';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { route } from '../../router';
import { stubDesktop, stubStream } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import { openSettings } from '../send-modes/open-settings';
import { ReleasedBySendMode } from '../send-modes/released';
import { reminderKey } from '../send-modes/reminder';
import { VoiceReminder } from '../send-modes/voice-reminder';
import { chosenSection } from './chosen';

// Settings in two panes, in the console's Live alone: the sections named in a list at the left, the one chosen shown
// beside it. The page keeps every section in its markup and the sheet hides the others.

const sheet = readFileSync('src/console/settings.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
const scope = '.jc-console .harness .settings';

const names = ['Appearance', 'Your name and tone', 'How messages go', 'Models', 'Runtime'];

function api() {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui');
    watch(fake);
    return fake;
}

async function openConsoleSettings() {
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    render(<Console api={api()} desktop={stubDesktop()} stream={stubStream().stream} />);
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Settings' }));
    await screen.findByRole('heading', { name: 'Settings', level: 1 });
    await waitFor(() => expect(document.querySelector('[data-section="runtime"] .card')).not.toBeNull());
    await waitFor(() => expect(document.querySelector('.send-mode-choice')).not.toBeNull());
}

const list = () => within(screen.getByRole('navigation', { name: 'Settings' }));
const chosen = () => document.querySelector('.settings')!.getAttribute('data-chosen');

describe('the sheet of the two panes', () => {
    it('hides the sections that are not chosen, by display, never off-screen', () => {
        const hidden = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g)).filter((rule) =>
            /display: none/.test(rule[2]!),
        );
        expect(hidden.length).toBe(1);
        const selectors = hidden[0]![1]!.split(',').map((each) => each.trim());
        expect(selectors).toHaveLength(5);
        for (const section of ['appearance', 'voice', 'modes', 'models', 'runtime'])
            expect(selectors).toContain(
                `${scope}[data-chosen='${section}'] > [data-section]:not([data-section='${section}'])`,
            );
        expect(sheet).not.toMatch(/left: -\d|clip:|text-indent: -/);
    });

    it('keeps the bar at the top while the content scrolls', () => {
        expect(sheet).toMatch(/\.settings\[data-chosen\] > h1 \{\s*position: sticky;\s*top: -22px;/);
    });
});

describe('Settings in two panes', () => {
    beforeEach(() => {
        chats.value = {};
        conversations.value = [];
        attention.loaded.value = false;
        attention.approvals.value = [];
        attention.questions.value = [];
        consoleCampaigns.campaigns.value = [];
        consoleCampaigns.read.value = false;
        mode.value = 'live';
        chosenSection.value = 'appearance';
        localStorage.removeItem(reminderKey);
    });
    afterEach(() => cleanup());

    it('lists the sections as buttons in a navigation, the first chosen, every section kept in the markup', async () => {
        await openConsoleSettings();
        const buttons = list().getAllByRole('button');
        expect(buttons.map((each) => each.textContent)).toEqual(names);
        expect(buttons.map((each) => each.getAttribute('aria-current'))).toEqual(['true', null, null, null, null]);
        expect(chosen()).toBe('appearance');
        expect(
            Array.from(document.querySelectorAll('.settings > [data-section]')).map((each) =>
                each.getAttribute('data-section'),
            ),
        ).toEqual(['appearance', 'voice', 'modes', 'models', 'runtime']);
    });

    it('chooses a section by its button, and the choice follows with aria-current', async () => {
        await openConsoleSettings();
        fireEvent.click(list().getByRole('button', { name: 'Models' }));
        expect(chosen()).toBe('models');
        expect(list().getByRole('button', { name: 'Models' }).getAttribute('aria-current')).toBe('true');
        expect(list().getByRole('button', { name: 'Appearance' }).getAttribute('aria-current')).toBeNull();
        fireEvent.click(list().getByRole('button', { name: 'Runtime' }));
        expect(chosen()).toBe('runtime');
    });

    it('opens at the name-and-tone card, selected and in view, from openSettings', async () => {
        const scrolled = vi.fn();
        Element.prototype.scrollIntoView = scrolled;
        await openConsoleSettings();
        fireEvent.click(list().getByRole('button', { name: 'Models' }));
        act(() => openSettings());
        await waitFor(() => expect(scrolled).toHaveBeenCalled());
        expect(chosen()).toBe('voice');
        expect(list().getByRole('button', { name: 'Your name and tone' }).getAttribute('aria-current')).toBe('true');
        expect(scrolled.mock.contexts.at(-1)).toBe(document.getElementById('settings-voice'));
    });

    it('is reached by every caller of openSettings: the released card and the reminder', async () => {
        const released = render(<ReleasedBySendMode preview={{}} />);
        fireEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
        expect(chosenSection.value).toBe('voice');
        released.unmount();

        chosenSection.value = 'models';
        const fake = new FakeApi().answer('preference.list', 'preference.list/mode-automatic-voice-unset');
        render(
            <ApiProvider api={fake}>
                <VoiceReminder campaignId="cmp_1" />
            </ApiProvider>,
        );
        fireEvent.click(await screen.findByRole('button', { name: 'Open Settings' }));
        expect(chosenSection.value).toBe('voice');
    });
});
