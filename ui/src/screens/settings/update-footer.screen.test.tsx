import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiProvider } from '../../api/context';
import { consoleShell } from '../../platform/console-shell';
import { mode } from '../../platform/mode';
import { noDesktop, setPlace, tabDesktop } from '../../platform/places';
import { Bridge } from '../../platform/bridge';
import { standInBridge, standInSecret } from '../../test/bridge';
import { stubDesktop, type StubDesktop } from '../../test/desktop';
import { FakeApi } from '../../test/fake-api';
import { chosenSection } from './chosen';
import { SettingsScreen } from './screen';

// The foot of Settings' list in the desktop's window: the desktop's name and version, and its update item as the menu
// shows it. The label and whether it can be pressed are the desktop's own; the page draws what it is answered and reads
// it again whenever the desktop says it changed. Nowhere but the desktop's own window in Live.

const loaded = consoleShell.loaded;
const sections = consoleShell.settingsSections;

function open(desktop: StubDesktop = stubDesktop()) {
    setPlace('window', desktop);
    render(
        <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
            <SettingsScreen />
        </ApiProvider>,
    );
    return desktop;
}

const foot = () => document.querySelector<HTMLElement>('.settings-list > .settings-update');

async function footShown() {
    await screen.findByText('Jason Desktop 0.48.5');
    return within(foot()!);
}

describe('the update item at the foot of Settings', () => {
    beforeEach(() => {
        consoleShell.loaded = true;
        consoleShell.settingsSections = null;
        mode.value = 'live';
        chosenSection.value = 'appearance';
    });
    afterEach(() => {
        cleanup();
        consoleShell.loaded = loaded;
        consoleShell.settingsSections = sections;
        mode.value = 'live';
    });

    it('shows the desktop’s name and version under the list, and the menu’s item, enabled, whose press is a click', async () => {
        const desktop = open();
        const shown = await footShown();
        expect(foot()!.parentElement).toBe(screen.getByRole('navigation', { name: 'Settings' }));
        expect(foot()!.previousElementSibling?.className).toBe('settings-announce');
        const button = shown.getByRole('button', { name: 'Check for Updates…' });
        expect(button).toHaveProperty('disabled', false);
        fireEvent.click(button);
        expect(desktop.calls.filter((each) => each.name === 'updates.press')).toEqual([
            { name: 'updates.press', args: [] },
        ]);
        // The press changes nothing on the page by itself: the item stays as the desktop last said it.
        expect(shown.getByRole('button', { name: 'Check for Updates…' })).toBe(button);
    });

    it('draws the item disabled where the menu’s is, and a click on it presses nothing', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = { version: '0.48.5', item: { label: 'Downloading Jason 0.48.6…', enabled: false } };
        open(desktop);
        const button = (await footShown()).getByRole('button', { name: 'Downloading Jason 0.48.6…' });
        expect(button).toHaveProperty('disabled', true);
        fireEvent.click(button);
        expect(desktop.calls.filter((each) => each.name === 'updates.press')).toEqual([]);
    });

    it('shows the version alone where the desktop has no item', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = { version: '0.48.5', item: null };
        open(desktop);
        const shown = await footShown();
        expect(shown.queryByRole('button')).toBeNull();
        expect(foot()!.textContent).toBe('Jason Desktop 0.48.5');
    });

    it('draws the item again, as the desktop says it, whenever the desktop says it changed', async () => {
        const desktop = open();
        const shown = await footShown();
        expect(shown.getByRole('button', { name: 'Check for Updates…' })).toBeTruthy();
        act(() => desktop.updateMoved({ version: '0.48.5', item: { label: 'Download Jason 0.48.6', enabled: true } }));
        expect(await shown.findByRole('button', { name: 'Download Jason 0.48.6' })).toHaveProperty('disabled', false);
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Restart Jason and Its Runtime to Install 0.48.6', enabled: true },
            }),
        );
        await shown.findByRole('button', { name: 'Restart Jason and Its Runtime to Install 0.48.6' });
        expect(shown.queryByRole('button', { name: 'Download Jason 0.48.6' })).toBeNull();
    });

    it('is not drawn in the Demo, where Settings has no list', async () => {
        mode.value = 'demo';
        open();
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        expect(screen.queryByRole('navigation')).toBeNull();
        expect(document.querySelector('.settings-update')).toBeNull();
        expect(screen.queryByText(/Jason Desktop/)).toBeNull();
    });

    it('is not drawn in a tab with no desktop, which is asked nothing of it', async () => {
        const none = noDesktop();
        setPlace('none', none);
        render(
            <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
                <SettingsScreen />
            </ApiProvider>,
        );
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        await act(async () => undefined);
        expect(document.querySelector('.settings-update')).toBeNull();
    });

    it('is not drawn in a tab the desktop opened: the bridge carries no update item', async () => {
        const desktop = stubDesktop();
        const bridge = new Bridge({ port: 43210, secret: standInSecret }, standInBridge(desktop).fetch);
        setPlace('tab', tabDesktop(bridge));
        render(
            <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
                <SettingsScreen />
            </ApiProvider>,
        );
        await screen.findByRole('navigation', { name: 'Settings' });
        await act(async () => undefined);
        expect(document.querySelector('.settings-update')).toBeNull();
        expect(desktop.calls.filter((each) => each.name.startsWith('updates.'))).toEqual([]);
    });
});
