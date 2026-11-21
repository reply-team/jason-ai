import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiProvider } from '../../api/context';
import { consoleShell } from '../../platform/console-shell';
import type { DesktopUpdateItem } from '../../platform/desktop';
import { mode } from '../../platform/mode';
import { noDesktop, setPlace, tabDesktop } from '../../platform/places';
import { Bridge } from '../../platform/bridge';
import { standInBridge, standInSecret } from '../../test/bridge';
import { stubDesktop, type StubDesktop } from '../../test/desktop';
import { FakeApi } from '../../test/fake-api';
import { chosenSection } from './chosen';
import { SettingsScreen } from './screen';

// Update, a section of Settings of its own in the desktop's window: the desktop's name and version, and its update item as
// the menu shows it. The label and whether it can be pressed are the desktop's own; the page draws what it is answered and
// reads it again whenever the desktop says it changed. A version the desktop knows of and has not installed marks the
// section's row in the list and its item, quietly. Nowhere but the desktop's own window in Live.

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

const section = () => document.querySelector<HTMLElement>('[data-section="update"]');
const listed = () => screen.getByRole('navigation', { name: 'Settings' });
const row = () => within(listed()).queryByRole('button', { name: /^Update/ });
const mark = (inside: Element | null) => inside?.querySelector<HTMLElement>('.dot.w') ?? null;

async function sectionShown() {
    await screen.findByText('Jason Desktop 0.48.5');
    return within(section()!);
}

describe('Update, a section of Settings', () => {
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

    it('is a row of the list of its own, last, that shows the version and the menu’s item, whose press is a click', async () => {
        const desktop = open();
        const shown = await sectionShown();
        const names = within(listed())
            .getAllByRole('button')
            .map((each) => each.textContent);
        expect(names.at(-1)).toBe('Update');
        // No foot under the list any more: the item is the section's.
        expect(document.querySelector('.settings-list > .settings-update')).toBeNull();
        fireEvent.click(row()!);
        expect(chosenSection.value).toBe('update');
        expect(document.querySelector('.settings')!.getAttribute('data-chosen')).toBe('update');
        expect(shown.getByRole('heading', { name: 'Update' })).toBeTruthy();
        const button = shown.getByRole('button', { name: 'Check for Updates…' });
        expect(button).toHaveProperty('disabled', false);
        fireEvent.click(button);
        expect(desktop.calls.filter((each) => each.name === 'updates.press')).toEqual([
            { name: 'updates.press', args: ['Check for Updates…'] },
        ]);
        // The press changes nothing on the page by itself: the item stays as the desktop last said it.
        expect(shown.getByRole('button', { name: 'Check for Updates…' })).toBe(button);
    });

    it('draws the item disabled where the menu’s is, and a click on it presses nothing', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Downloading Jason 0.48.6…', enabled: false },
            said: null,
        };
        open(desktop);
        const button = (await sectionShown()).getByRole('button', { name: 'Downloading Jason 0.48.6…' });
        expect(button).toHaveProperty('disabled', true);
        fireEvent.click(button);
        expect(desktop.calls.filter((each) => each.name === 'updates.press')).toEqual([]);
    });

    it('draws the item again, as the desktop says it, whenever the desktop says it changed', async () => {
        const desktop = open();
        const shown = await sectionShown();
        expect(shown.getByRole('button', { name: 'Check for Updates…' })).toBeTruthy();
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Download Jason 0.48.6', enabled: true },
                said: null,
            }),
        );
        const download = await shown.findByRole('button', { name: 'Download Jason 0.48.6' });
        expect(download).toHaveProperty('disabled', false);
        // A press names the label the page shows, which the desktop acts on only while its item still says it.
        fireEvent.click(download);
        expect(desktop.calls.filter((each) => each.name === 'updates.press').map((each) => each.args)).toEqual([
            ['Download Jason 0.48.6'],
        ]);
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Restart Jason and Its Runtime to Install 0.48.6', enabled: true },
                said: null,
            }),
        );
        await shown.findByRole('button', { name: 'Restart Jason and Its Runtime to Install 0.48.6' });
        expect(shown.queryByRole('button', { name: 'Download Jason 0.48.6' })).toBeNull();
    });

    it('says what the person’s last check found under the item, as the desktop answers it, and drops it when the desktop does', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Check for Updates…', enabled: true },
            said: 'Jason Desktop 0.48.5 is the latest version.',
        };
        open(desktop);
        const shown = await sectionShown();
        const line = shown.getByRole('status');
        expect(line.textContent).toBe('Jason Desktop 0.48.5 is the latest version.');
        // Under the item, the last thing in the section.
        expect(section()!.lastElementChild).toBe(line);
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Download Jason 0.48.6', enabled: true },
                said: 'Version 0.48.6 is available.',
            }),
        );
        await shown.findByText('Version 0.48.6 is available.');
        expect(shown.getAllByRole('status')).toHaveLength(1);
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Downloading Jason 0.48.6…', enabled: false },
                said: null,
            }),
        );
        await shown.findByRole('button', { name: 'Downloading Jason 0.48.6…' });
        expect(shown.queryByRole('status')).toBeNull();
    });

    it('shows no line and no mark from a desktop of an earlier version, whose answer carries neither', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Download Jason 0.48.6', enabled: true },
        } as unknown as DesktopUpdateItem;
        open(desktop);
        const shown = await sectionShown();
        expect(shown.getByRole('button', { name: 'Download Jason 0.48.6' })).toBeTruthy();
        expect(shown.queryByRole('status')).toBeNull();
        expect(mark(section())).toBeNull();
        expect(mark(row())).toBeNull();
    });

    it('marks its row and its item while the desktop knows of a version it has not installed, from the offer to the install', async () => {
        const desktop = open();
        await sectionShown();
        expect(mark(row())).toBeNull();
        expect(mark(section())).toBeNull();
        // A look the desktop made by itself found a version: the mark comes with it, quietly, with no count.
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Download Jason 0.48.6', enabled: true },
                said: null,
                newer: '0.48.6',
            }),
        );
        const onRow = await within(listed()).findByLabelText('Jason Desktop 0.48.6 is available');
        expect(row()!.contains(onRow)).toBe(true);
        expect(onRow.textContent).toBe('');
        const onItem = mark(section())!;
        expect(onItem.getAttribute('aria-label')).toBe('Jason Desktop 0.48.6 is available');
        expect(onItem.previousElementSibling?.textContent).toBe('Download Jason 0.48.6');
        // Downloaded, Not Now chosen: still known, still marked.
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Restart Jason and Its Runtime to Install 0.48.6', enabled: true },
                said: null,
                newer: '0.48.6',
            }),
        );
        await within(section()!).findByRole('button', { name: 'Restart Jason and Its Runtime to Install 0.48.6' });
        expect(mark(row())).not.toBeNull();
        expect(mark(section())).not.toBeNull();
        // Installed: the new version runs, and nothing is marked.
        act(() =>
            desktop.updateMoved({
                version: '0.48.6',
                item: { label: 'Check for Updates…', enabled: true },
                said: null,
                newer: null,
            }),
        );
        await screen.findByText('Jason Desktop 0.48.6');
        expect(mark(row())).toBeNull();
        expect(mark(section())).toBeNull();
    });

    it('says for the deb that the system’s packages update it, with no button, and offers its restart once the system did', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Updates Come Through the Package', enabled: false },
            said: null,
            newer: null,
        };
        open(desktop);
        const shown = await sectionShown();
        expect(shown.queryByRole('button')).toBeNull();
        expect(section()!.textContent).toContain(
            'Your system updates Jason Desktop and the jason it carries: Software Updater offers it, as sudo apt update && sudo apt upgrade does in a terminal.',
        );
        act(() =>
            desktop.updateMoved({
                version: '0.48.5',
                item: { label: 'Restart Jason and Its Runtime to Finish the Update to 0.48.6', enabled: true },
                said: null,
                newer: '0.48.6',
            }),
        );
        const restart = await shown.findByRole('button', {
            name: 'Restart Jason and Its Runtime to Finish the Update to 0.48.6',
        });
        expect(mark(section())).not.toBeNull();
        expect(section()!.textContent).not.toContain('Software Updater');
        fireEvent.click(restart);
        expect(desktop.calls.filter((each) => each.name === 'updates.press').map((each) => each.args)).toEqual([
            ['Restart Jason and Its Runtime to Finish the Update to 0.48.6'],
        ]);
    });

    it('is neither listed nor drawn where the desktop has no update item, as in its Demo run', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = { version: '0.48.5', item: null, said: null, newer: null };
        open(desktop);
        await screen.findByRole('navigation', { name: 'Settings' });
        await act(async () => undefined);
        expect(row()).toBeNull();
        expect(section()).toBeNull();
        expect(screen.queryByText(/Jason Desktop/)).toBeNull();
    });

    it('is not drawn in the Demo, where Settings has no list', async () => {
        mode.value = 'demo';
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Download Jason 0.48.6', enabled: true },
            said: null,
            newer: '0.48.6',
        };
        open(desktop);
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        await act(async () => undefined);
        expect(screen.queryByRole('navigation')).toBeNull();
        expect(section()).toBeNull();
        expect(document.querySelector('.dot.w')).toBeNull();
        expect(screen.queryByText(/Jason Desktop/)).toBeNull();
    });

    it('is not drawn in a tab with no desktop, which is asked nothing of it', async () => {
        setPlace('none', noDesktop());
        render(
            <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
                <SettingsScreen />
            </ApiProvider>,
        );
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        await act(async () => undefined);
        expect(section()).toBeNull();
        expect(screen.queryByRole('button', { name: /^Update/ })).toBeNull();
    });

    it('is not drawn in a tab the desktop opened: the bridge carries no update item', async () => {
        const desktop = stubDesktop();
        desktop.updateKept = {
            version: '0.48.5',
            item: { label: 'Download Jason 0.48.6', enabled: true },
            said: null,
            newer: '0.48.6',
        };
        const bridge = new Bridge({ port: 43210, secret: standInSecret }, standInBridge(desktop).fetch);
        setPlace('tab', tabDesktop(bridge));
        render(
            <ApiProvider api={new FakeApi().answer('preference.list', 'preference.list/set')}>
                <SettingsScreen />
            </ApiProvider>,
        );
        await screen.findByRole('navigation', { name: 'Settings' });
        await act(async () => undefined);
        expect(section()).toBeNull();
        expect(row()).toBeNull();
        expect(document.querySelector('.dot.w')).toBeNull();
        expect(desktop.calls.filter((each) => each.name.startsWith('updates.'))).toEqual([]);
    });
});
