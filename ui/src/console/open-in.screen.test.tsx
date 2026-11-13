import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations, lines, setup, setupError } from './state';
import { startDraft, startMaking, startWaiting } from './start';

// "Open in", the split button of the console's top bar: in Live and in Demo, the main part opens the default place, the
// arrow lists the places the desktop says this computer has, and the page sends a place's name and a campaign's id, never
// a path. The places, the default and the folder's name are the desktop's, as the stand-in answers them.

function api(): FakeApi {
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
        .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
        .answer('campaign.get', 'campaign.get/details')
        .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
        .answer('journal.list', 'journal.list/by-campaign')
        .answer('route.resolve', 'route.resolve/global-default');
}

async function renderConsole(kept: 'live' | 'demo', desktop: StubDesktop = chatDesktop()): Promise<StubDesktop> {
    desktop.modeKept = kept;
    await loadMode(desktop);
    const fake = api();
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
    await waitFor(() => expect(setup.value).not.toBeNull());
    return desktop;
}

const opens = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'openIn.open');
const targets = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'openIn.targets');
const split = () => screen.getByRole('group', { name: 'Open in' });
const mainPart = () => within(split()).getAllByRole('button')[0]!;
const arrow = () => screen.getByRole('button', { name: 'More places to open' });
const menu = () => screen.getByRole('menu');
const items = () =>
    within(menu())
        .getAllByRole('menuitem')
        .map((each) => each.querySelector('span')?.textContent);

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    setupError.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    startDraft.value = '';
    startWaiting.value = null;
    startMaking.value = null;
});

afterEach(() => {
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
});

describe.each(['live', 'demo'] as const)('Open in, in %s', (kept) => {
    it('sits in the open-in slot, left of the Demo | Live switch, above the sidebar and the dock', async () => {
        await renderConsole(kept);
        await waitFor(() => split());
        const slot = split().closest('[data-slot="open-in"]')!;
        const bar = slot.parentElement!;
        expect(bar.classList.contains('mode-strip')).toBe(true);
        expect(bar.parentElement).toBe(document.querySelector('.jc-console'));
        expect(bar.nextElementSibling?.classList.contains('shell')).toBe(true);
        expect(slot.nextElementSibling).toBe(within(bar).getByRole('group', { name: 'Data' }));
        expect(document.querySelector('.top-bar')).toBeNull();
    });

    it('opens the default place from the main part, with the harness root as the folder and no path sent', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['claude', undefined]);
    });

    it('lists the places under his headers, with his subtitles, and marks the default', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        expect(items()).toEqual([
            'Claude Code',
            'Codex',
            'VS Code',
            'Zed',
            'New terminal',
            'File manager',
            'Copy path',
        ]);
        expect([...menu().children].map((each) => each.className)).toEqual([
            'open-in-header',
            'on',
            '',
            'open-in-header',
            '',
            '',
            '',
            '',
            '',
            ...(kept === 'demo' ? ['open-in-note'] : []),
        ]);
        expect(within(menu()).getByText('Agents')).not.toBeNull();
        expect(within(menu()).getByText('Editors')).not.toBeNull();
        expect(within(menu()).getAllByText('A new session in a terminal')).toHaveLength(2);
        // Copy path carries the folder as its subtitle; the arrow carries his title.
        expect(within(within(menu()).getByRole('menuitem', { name: /^Copy path/ })).getByText('Jason')).not.toBeNull();
        expect(arrow().getAttribute('title')).toBe('Open in…');
        expect(menu().getAttribute('aria-label')).toBe('Open in…');
        expect(arrow().getAttribute('aria-expanded')).toBe('true');
        expect(opens(desktop)).toEqual([]);
        fireEvent.keyDown(document, { key: 'Escape' });
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('shows only the places the desktop says are there', async () => {
        const desktop = chatDesktop();
        desktop.openInKept = {
            places: [
                { id: 'terminal', label: 'Terminal' },
                { id: 'cursor', label: 'Cursor' },
                { id: 'copy', label: 'Copy path' },
            ],
            default: 'terminal',
            folder: 'Jason',
        };
        await renderConsole(kept, desktop);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Terminal'));
        fireEvent.click(arrow());
        expect(items()).toEqual(['Cursor', 'New terminal', 'Copy path']);
        expect(within(menu()).queryByText('Agents')).toBeNull();
    });

    it('opens a place chosen from the menu, closes the menu, and the place used last becomes the main part', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^VS Code/ }));
        expect(screen.queryByRole('menu')).toBeNull();
        await waitFor(() => expect(mainPart().textContent).toBe('Open in VS Code'));
        expect(opens(desktop).map((each) => each.args)).toEqual([['vscode', undefined]]);
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(2));
        expect(opens(desktop)[1]!.args).toEqual(['vscode', undefined]);
    });

    it('copies the path from the menu without making Copy path the default', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^Copy path/ }));
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['copy', undefined]);
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(1));
        expect(mainPart().textContent).toBe('Open in Claude Code');
    });

    it('says nothing of data after the file manager in the demo, and the same sentence as ever in Live', async () => {
        await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^File manager/ }));
        await waitFor(() =>
            expect(document.querySelector('.open-in-toast')?.textContent).toBe(
                kept === 'demo'
                    ? 'Opened Jason in File manager.'
                    : 'Opened Jason in File manager. Jason there works with the same data as this window.',
            ),
        );
    });

    it('carries our note in the demo’s menu, and none in Live', async () => {
        await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        const note = menu().querySelector('.open-in-note');
        if (kept === 'demo')
            expect(note?.textContent).toBe("Opens the demo's own folder. A terminal there works with the demo's data.");
        else expect(note).toBeNull();
    });

    it('says what was opened after it opens, and that Jason there works with the same data where that is true', async () => {
        await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(mainPart());
        await waitFor(() => expect(document.querySelector('.open-in-toast')).not.toBeNull());
        const toast = document.querySelector<HTMLElement>('.open-in-toast')!;
        expect(toast.textContent).toBe(
            'Opened Jason in Claude Code. Jason there works with the same data as this window.',
        );
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^VS Code/ }));
        // An editor already running keeps its own environment, so in the demo the second sentence is left off.
        await waitFor(() =>
            expect(document.querySelector('.open-in-toast')?.textContent).toBe(
                kept === 'demo'
                    ? "Opened Jason in VS Code. An editor's own terminal may work with your own installation, not the demo."
                    : 'Opened Jason in VS Code. Jason there works with the same data as this window.',
            ),
        );
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^Copy path/ }));
        await waitFor(() =>
            expect(document.querySelector('.open-in-toast')?.textContent).toBe('Copied the path of Jason.'),
        );
    });

    it('says the desktop’s refusal beside the button and keeps it there', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        desktop.refuse.set('openIn.open', 'Cursor could not be started.');
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^New terminal/ }));
        const alert = await waitFor(() => within(split().parentElement!).getByRole('alert'));
        expect(alert.textContent).toBe('Cursor could not be started.');
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(1));
        expect(within(split().parentElement!).getByRole('alert').textContent).toBe('Cursor could not be started.');
    });

    it('is disabled, with the desktop’s sentence, when the folder cannot be named', async () => {
        const desktop = chatDesktop();
        desktop.refuse.set('openIn.targets', 'Jason’s folder is not there yet; it is made with the first agent.');
        await renderConsole(kept, desktop);
        const alert = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.open-in-problem');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(alert.textContent).toBe('Jason’s folder is not there yet; it is made with the first agent.');
        expect((mainPart() as HTMLButtonElement).disabled).toBe(true);
        expect((arrow() as HTMLButtonElement).disabled).toBe(true);
    });
});

describe('Open in with a campaign’s chat in front', () => {
    it('sends the campaign’s id with the act and asks for its folder, and never a path', async () => {
        const desktop = await renderConsole('live');
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Agents' }));
        const row = (await waitFor(() => screen.getByText('Spring'))).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Open' }));
        const tabs = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.harness .campaign-tabs');
            expect(found).not.toBeNull();
            return found!;
        });
        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(targets(desktop).some((each) => each.args[0] === 'cmp_1')).toBe(true));
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['claude', 'cmp_1']);
        for (const call of desktop.calls.filter((each) => each.name.startsWith('openIn.'))) {
            expect(JSON.stringify(call.args)).not.toMatch(/[\\/]/);
        }
    });
});
