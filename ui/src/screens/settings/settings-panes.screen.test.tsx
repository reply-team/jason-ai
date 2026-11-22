import { readFileSync } from 'node:fs';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { ApiProvider } from '../../api/context';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import * as consoleCampaigns from '../../console/campaigns';
import { attention } from '../../console/needs';
import { chats, conversations } from '../../console/state';
import { consoleShell } from '../../platform/console-shell';
import { mode } from '../../platform/mode';
import * as shell from '../../shell/shell';
import { SettingsScreen } from './screen';
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

const names = ['Appearance', 'Your name and tone', 'How messages go', 'Notifications', 'Models', 'Runtime', 'Update'];

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
    const fake = api();
    render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Settings' }));
    await screen.findByRole('heading', { name: 'Settings', level: 1 });
    await waitFor(() => expect(document.querySelector('[data-section="runtime"] .card')).not.toBeNull());
    await waitFor(() => expect(document.querySelector('.send-mode-choice')).not.toBeNull());
    return fake;
}

const list = () => within(screen.getByRole('navigation', { name: 'Settings' }));
const region = () => document.querySelector('.settings-announce')!;
const chosen = () => document.querySelector('.settings')!.getAttribute('data-chosen');

describe('the sheet of the two panes', () => {
    it('hides the sections that are not chosen, by display, never off-screen', () => {
        const hidden = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g)).filter((rule) =>
            /display: none/.test(rule[2]!),
        );
        expect(hidden.length).toBe(1);
        const selectors = hidden[0]![1]!.split(',').map((each) => each.trim());
        expect(selectors).toHaveLength(7);
        for (const section of ['appearance', 'voice', 'modes', 'notifications', 'models', 'runtime', 'update'])
            expect(selectors).toContain(
                `${scope}[data-chosen='${section}'] > [data-section]:not([data-section='${section}'])`,
            );
        expect(sheet).not.toMatch(/left: -\d|clip:|text-indent: -/);
    });

    it('keeps the bar at the top while the content scrolls: the page scrolls, and a card clears the bar', () => {
        expect(sheet).toMatch(/\.settings\[data-chosen\] > h1 \{\s*position: sticky;\s*top: 0;/);
        // At both widths: no narrower rule moves the bar's offset.
        // Sticky are the bar, the list and the way out held at the bar's left, and no narrower rule gives any a different offset.
        expect(sheet.match(/position: sticky/g)).toHaveLength(3);
        expect(sheet).not.toMatch(/h1 \{[^}]*top: -/);
        // The harness page's inner content is no scroller of its own while Settings is in two panes.
        expect(sheet).toMatch(/\.harness \.content:has\(> \.settings\[data-chosen\]\) \{\s*overflow: visible;\s*\}/);
        expect(sheet).toMatch(/\[data-section\] \{[^}]*scroll-margin-top: 84px;/);
        expect(sheet).toMatch(/\[data-section\] button \{\s*scroll-margin-top: 84px;/);
    });

    it('draws the list as the console draws a list: ink text, the chosen entry on the hover ground at 600', () => {
        const rule = (selector: string) =>
            Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g))
                .filter((each) => each[1]!.split(',').some((one) => one.trim() === selector))
                .map((each) => each[2]!.replace(/\s+/g, ' '))
                .join(' ');
        expect(rule(`${scope} > .settings-list`)).toMatch(/width: 236px/);
        expect(rule(`${scope} > .settings-list > button`)).toMatch(/padding: 7px 10px; border: 0; border-radius: 9px;/);
        expect(rule(`${scope} > .settings-list > button`)).toMatch(/color: var\(--ink\)/);
        expect(rule(`${scope} > .settings-list > button:hover`)).toMatch(/background: var\(--hover\)/);
        expect(rule(`${scope} > .settings-list > button[aria-current='true']`)).toMatch(
            /background: var\(--hover\); font-weight: 600/,
        );
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
        expect(buttons.map((each) => each.getAttribute('aria-current'))).toEqual([
            'true',
            null,
            null,
            null,
            null,
            null,
            null,
        ]);
        expect(chosen()).toBe('appearance');
        expect(
            Array.from(document.querySelectorAll('.settings > [data-section]')).map((each) =>
                each.getAttribute('data-section'),
            ),
        ).toEqual(['appearance', 'voice', 'modes', 'notifications', 'models', 'runtime', 'update']);
    });

    it('names the list with a group label and gives each entry its own mark', async () => {
        await openConsoleSettings();
        expect(document.querySelector('.settings-list > .settings-group')?.textContent).toBe('Jason');
        const buttons = list().getAllByRole('button');
        expect(buttons.map((each) => each.querySelectorAll('svg.settings-mark').length)).toEqual([1, 1, 1, 1, 1, 1, 1]);
        const shapes = buttons.map((each) => each.querySelector('svg.settings-mark')!.innerHTML);
        expect(new Set(shapes).size).toBe(7);
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
        const was = Element.prototype.scrollIntoView;
        Element.prototype.scrollIntoView = scrolled;
        onTestFinished(() => {
            Element.prototype.scrollIntoView = was;
        });
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

    it('marks the name of a section that holds an alert while another is shown, and describes it by the alert', async () => {
        await openConsoleSettings();
        const card = document.querySelector('.settings > [data-section="voice"]')!;
        const alert = document.createElement('p');
        alert.setAttribute('role', 'alert');
        alert.textContent = 'The save did not go through.';
        act(() => {
            card.appendChild(alert);
        });
        const entry = list().getByRole('button', { name: 'Your name and tone' });
        await waitFor(() => expect(entry.hasAttribute('data-flagged')).toBe(true));
        expect(document.getElementById(entry.getAttribute('aria-describedby')!)).toBe(alert);
        expect(entry.getAttribute('title')).toBe('The save did not go through.');
        expect(region().textContent).toBe('The save did not go through.');
        expect(chosen()).toBe('appearance');
        expect(list().getByRole('button', { name: 'Appearance' }).hasAttribute('data-flagged')).toBe(false);
        // Chosen, the card shows the alert itself, so the mark goes.
        fireEvent.click(entry);
        await waitFor(() => expect(entry.hasAttribute('data-flagged')).toBe(false));
        expect(region().textContent).toBe('');
    });

    it('says a real failed save of name and tone in the region of the list, though its card is hidden', async () => {
        const fake = await openConsoleSettings();
        fireEvent.click(list().getByRole('button', { name: 'Your name and tone' }));

        // The runtime does not answer the save: the screen's own refusal path, which says it in an alert.
        fake.unreachable('preference.set');
        fireEvent.click(
            within(document.querySelector<HTMLElement>('[data-section="voice"]')!).getAllByRole('button', {
                name: 'Save',
            })[0]!,
        );
        fireEvent.click(list().getByRole('button', { name: 'Appearance' }));
        await waitFor(() => expect(region().textContent).not.toBe(''));
        const entry = list().getByRole('button', { name: 'Your name and tone' });
        expect(entry.getAttribute('title')).toBe(region().textContent);
        expect(entry.hasAttribute('data-flagged')).toBe(true);
        expect(region().getAttribute('role')).toBe('alert');
        // The card is hidden by the sheet alone, and the view did not move.
        expect(chosen()).toBe('appearance');
    });

    const alone = () =>
        render(
            <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
                <SettingsScreen />
            </ApiProvider>,
        );

    it('draws no list and no chosen section in the demo', async () => {
        mode.value = 'demo';
        alone();
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        expect(screen.queryByRole('navigation')).toBeNull();
        expect(document.querySelector('.settings')!.hasAttribute('data-chosen')).toBe(false);
    });

    it('draws no list and no chosen section in the classic shell, where the console is not loaded', async () => {
        const loaded = consoleShell.loaded;
        consoleShell.loaded = false;
        onTestFinished(() => {
            consoleShell.loaded = loaded;
        });
        alone();
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        expect(screen.queryByRole('navigation')).toBeNull();
        expect(document.querySelector('.settings')!.hasAttribute('data-chosen')).toBe(false);
    });

    it('lists no Runtime while the runtime has not been read', () => {
        const added = consoleShell.settingsSections;
        consoleShell.settingsSections = () => null;
        const info = shell.info.value;
        shell.info.value = null;
        onTestFinished(() => {
            consoleShell.settingsSections = added;
            shell.info.value = info;
        });
        alone();
        expect(
            list()
                .getAllByRole('button')
                .map((each) => each.textContent),
        ).toEqual(['Appearance', 'Your name and tone', 'How messages go', 'Notifications', 'Models']);
    });
});
