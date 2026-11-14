import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
import { activePanel, changeChat, chats, conversations, currentChat, lines, setup, setupError } from './state';
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
                { id: 'terminal', label: '' },
                { id: 'cursor', label: 'Cursor' },
                { id: 'copy', label: '' },
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
        expect((arrow() as HTMLButtonElement).disabled).toBe(false);
    });

    it('reads the places again when the arrow is opened, and recovers after a refusal', async () => {
        const desktop = chatDesktop();
        desktop.refuse.set('openIn.targets', 'Jason’s folder is not there yet; it is made with the first agent.');
        await renderConsole(kept, desktop);
        await waitFor(() => expect(document.querySelector('.open-in-problem')).not.toBeNull());
        expect((mainPart() as HTMLButtonElement).disabled).toBe(true);
        desktop.refuse.delete('openIn.targets');
        const before = targets(desktop).length;
        fireEvent.click(arrow());
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(before));
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        expect((mainPart() as HTMLButtonElement).disabled).toBe(false);
        expect(document.querySelector('.open-in-problem')).toBeNull();
        expect(within(menu()).getAllByRole('menuitem').length).toBeGreaterThan(1);
    });

    it('names the plain terminal by the main part’s word in its toast', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        fireEvent.click(arrow());
        fireEvent.click(within(menu()).getByRole('menuitem', { name: /^New terminal/ }));
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Terminal'));
        await waitFor(() =>
            expect(document.querySelector('.open-in-toast')?.textContent).toBe(
                'Opened Jason in Terminal. Jason there works with the same data as this window.',
            ),
        );
    });

    it('draws no menu and attaches no outside-click or Escape listener while the places are still refused', async () => {
        const desktop = chatDesktop();
        desktop.refuse.set('openIn.targets', 'Jason’s folder is not there yet; it is made with the first agent.');
        await renderConsole(kept, desktop);
        await waitFor(() => expect(document.querySelector('.open-in-problem')).not.toBeNull());
        const added = vi.spyOn(document, 'addEventListener');
        fireEvent.click(arrow());
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(1));
        expect(screen.queryByRole('menu')).toBeNull();
        expect(added.mock.calls.filter(([type]) => type === 'mousedown' || type === 'keydown')).toEqual([]);
        added.mockRestore();
    });

    it('reads the places again when the window regains focus', async () => {
        const desktop = await renderConsole(kept);
        await waitFor(() => expect(mainPart().textContent).toBe('Open in Claude Code'));
        const before = targets(desktop).length;
        fireEvent.focus(window);
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(before));
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
        expect(mainPart().title).toBe('Open Jason in Claude Code');
        for (const call of desktop.calls.filter((each) => each.name.startsWith('openIn.'))) {
            expect(JSON.stringify(call.args)).not.toMatch(/[\\/]/);
        }
    });

    it('opens the harness root again once Agents is in front after a chat', async () => {
        const desktop = await renderConsole('live');
        await waitFor(() => expect(consoleCampaigns.read.value).toBe(true));
        const side = () => within(document.querySelector<HTMLElement>('.side')!);
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        const row = (await waitFor(() => screen.getByText('Spring'))).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Open' }));
        const tabs = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.harness .campaign-tabs');
            expect(found).not.toBeNull();
            return found!;
        });
        fireEvent.click(within(tabs).getByRole('button', { name: 'Chat' }));
        await waitFor(() => expect(targets(desktop).some((each) => each.args[0] === 'cmp_1')).toBe(true));
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(targets(desktop).at(-1)!.args[0]).toBeUndefined());
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['claude', undefined]);
    });
});

describe('Continue this chat in a terminal', () => {
    /** The chat the console opened as it started, once it is the panel in front. */
    async function chatInFront(): Promise<string> {
        return waitFor(() => {
            const id = currentChat.value;
            expect(id).not.toBeNull();
            expect(activePanel.value).toBe('chat:' + id);
            return id!;
        });
    }
    const continues = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'openIn.continueChat');
    const entry = () => within(menu()).getByRole('menuitem', { name: /^Continue this chat in/ });
    const note =
        "It continues in your terminal under Claude Code's own permission settings. This window's permission cards do not follow it, and this window does not show what happens there.";

    it('names the chat in front when it reads the places, and draws no entry where the desktop offers none', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        await waitFor(() => expect(targets(desktop).some((each) => each.args[1] === id)).toBe(true));
        fireEvent.click(arrow());
        await waitFor(() => expect(within(menu()).getAllByRole('menuitem').length).toBeGreaterThan(1));
        expect(within(menu()).queryByRole('menuitem', { name: /^Continue this chat/ })).toBeNull();
        expect(menu().querySelector('.open-in-continue')).toBeNull();
    });

    it.each(['live', 'demo'] as const)(
        'draws the entry last, with its sentence, and continues the chat by its id alone, in %s',
        async (kept) => {
            const desktop = await renderConsole(kept);
            const id = await chatInFront();
            desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: false });
            fireEvent.click(arrow());
            await waitFor(() => expect(entry()).not.toBeNull());
            const all = within(menu()).getAllByRole('menuitem');
            expect(all.at(-1)).toBe(entry());
            expect(entry().querySelector('span')?.textContent).toBe('Continue this chat in Claude Code');
            expect(entry().querySelector('small')?.textContent).toBe(note);
            expect((entry() as HTMLButtonElement).disabled).toBe(false);
            expect(entry().className).not.toBe('on');
            // Its group comes after every place, the demo's note included.
            expect(menu().lastElementChild?.classList.contains('open-in-continue')).toBe(true);
            fireEvent.click(entry());
            expect(screen.queryByRole('menu')).toBeNull();
            await waitFor(() => expect(continues(desktop)).toHaveLength(1));
            expect(continues(desktop)[0]!.args).toEqual([id]);
            expect(opens(desktop)).toEqual([]);
            await waitFor(() =>
                expect(document.querySelector('.open-in-toast')?.textContent).toBe(
                    'Continued this chat in Claude Code, in a new terminal. Jason there works with the same data as this window.',
                ),
            );
        },
    );

    it('is never the default: after it, the main part opens the place it opened before', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        desktop.openInContinue.set(id, { host: 'codex', label: 'Codex', running: false });
        fireEvent.click(arrow());
        await waitFor(() => expect(entry().querySelector('span')?.textContent).toBe('Continue this chat in Codex'));
        fireEvent.click(entry());
        await waitFor(() => expect(continues(desktop)).toHaveLength(1));
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(2));
        expect(mainPart().textContent).toBe('Open in Claude Code');
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['claude', undefined]);
        expect(continues(desktop)).toHaveLength(1);
    });

    it('is drawn disabled, with one sentence, while the desktop says a turn of the chat runs or the page sees one run', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: true });
        fireEvent.click(arrow());
        await waitFor(() => expect((entry() as HTMLButtonElement).disabled).toBe(true));
        expect(entry().querySelector('small')?.textContent).toBe(
            'A turn of this chat is running. Stop it or wait for it to end.',
        );
        fireEvent.click(entry());
        expect(continues(desktop)).toEqual([]);
        fireEvent.keyDown(document, { key: 'Escape' });
        expect(screen.queryByRole('menu')).toBeNull();
        // The desktop says it does not run, the page's chat does: still disabled.
        desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: false });
        changeChat(id, (chat) => ({ ...chat, running: true }));
        fireEvent.click(arrow());
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(2));
        await waitFor(() => expect((entry() as HTMLButtonElement).disabled).toBe(true));
        // The turn ends: the places are read again, and the entry can be used.
        const before = targets(desktop).length;
        changeChat(id, (chat) => ({ ...chat, running: false }));
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(before));
        await waitFor(() => expect((entry() as HTMLButtonElement).disabled).toBe(false));
        expect(entry().querySelector('small')?.textContent).toBe(note);
    });

    it('says the desktop’s refusal beside the button, in its own sentence, and draws no toast', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: false });
        desktop.refuse.set('openIn.continueChat', 'No terminal was found on this computer, so nothing was started.');
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        const alert = await waitFor(() => within(split().parentElement!).getByRole('alert'));
        expect(alert.textContent).toBe('No terminal was found on this computer, so nothing was started.');
        expect(document.querySelector('.open-in-toast')).toBeNull();
    });

    it('names no chat once another panel is in front, and draws no entry then, whatever an answer carries', async () => {
        const desktop = await renderConsole('live');
        await chatInFront();
        // An answer that carries an entry whatever it was asked: with no chat in front the page still draws none.
        const answered = desktop.openIn.targets;
        desktop.openIn.targets = async (campaignId, chatId) => {
            const answer = await answered(campaignId, chatId);
            return 'refused' in answer
                ? answer
                : { ...answer, continue: { host: 'claude', label: 'Claude Code', running: false } };
        };
        fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(targets(desktop).at(-1)!.args).toEqual([undefined, undefined]));
        fireEvent.click(arrow());
        await waitFor(() => expect(within(menu()).getAllByRole('menuitem').length).toBeGreaterThan(1));
        expect(within(menu()).queryByRole('menuitem', { name: /^Continue this chat/ })).toBeNull();
        expect(menu().querySelector('.open-in-continue')).toBeNull();
    });

    it('keeps the entry to the chat it was read for: a slow read for one chat never shows, or overwrites, the next chat’s', async () => {
        const desktop = await renderConsole('live');
        const first = await chatInFront();
        const second = 'conv_01JD0000000000000000000099';
        desktop.openInContinue.set(first, { host: 'claude', label: 'Claude Code', running: false });
        desktop.openInContinue.set(second, { host: 'codex', label: 'Codex', running: false });
        // The first chat's next read waits until the test lets it land.
        const answered = desktop.openIn.targets;
        let land: () => void = () => undefined;
        desktop.openIn.targets = async (campaignId, chatId) => {
            const answer = await answered(campaignId, chatId);
            if (chatId === first) await new Promise<void>((resolve) => (land = resolve));
            return answer;
        };
        const before = targets(desktop).length;
        fireEvent.focus(window);
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(before));
        // The second chat comes to the front while the first chat's read is still out.
        activePanel.value = 'chat:' + second;
        currentChat.value = second;
        await waitFor(() => expect(targets(desktop).some((each) => each.args[1] === second)).toBe(true));
        fireEvent.click(arrow());
        await waitFor(() => expect(entry().querySelector('span')?.textContent).toBe('Continue this chat in Codex'));
        // The first chat's answer lands late, after the second chat's: it is dropped, and the entry stays the second's.
        land();
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(within(menu()).getAllByRole('menuitem', { name: /^Continue this chat/ })).toHaveLength(1);
        expect(entry().querySelector('span')?.textContent).toBe('Continue this chat in Codex');
        fireEvent.click(entry());
        await waitFor(() => expect(continues(desktop)).toHaveLength(1));
        expect(continues(desktop)[0]!.args).toEqual([second]);
    });

    it('draws no entry of the chat that was in front while the next chat’s read is out', async () => {
        const desktop = await renderConsole('live');
        const first = await chatInFront();
        desktop.openInContinue.set(first, { host: 'claude', label: 'Claude Code', running: false });
        fireEvent.click(arrow());
        await waitFor(() => expect(entry()).not.toBeNull());
        // The next chat's read never lands: the first chat's entry is not drawn for it meanwhile.
        const answered = desktop.openIn.targets;
        desktop.openIn.targets = (campaignId, chatId) =>
            chatId === first ? answered(campaignId, chatId) : new Promise(() => undefined);
        activePanel.value = 'chat:conv_01JD0000000000000000000099';
        currentChat.value = 'conv_01JD0000000000000000000099';
        await waitFor(() => expect(within(menu()).queryByRole('menuitem', { name: /^Continue this chat/ })).toBeNull());
    });

    it('says a rejected act beside the button, in the rejection’s words, and draws no toast', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: false });
        desktop.openIn.continueChat = () => Promise.reject(new Error('This window may not ask that.'));
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        const alert = await waitFor(() => within(split().parentElement!).getByRole('alert'));
        expect(alert.textContent).toBe('This window may not ask that.');
        expect(document.querySelector('.open-in-toast')).toBeNull();
    });

    it('reads the places again when a turn of the chat in front starts', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        await waitFor(() => expect(targets(desktop).some((each) => each.args[1] === id)).toBe(true));
        const before = targets(desktop).length;
        changeChat(id, (chat) => ({ ...chat, running: true }));
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(before));
        expect(targets(desktop).at(-1)!.args[1]).toBe(id);
    });

    it('names its group by the entry', async () => {
        const desktop = await renderConsole('live');
        const id = await chatInFront();
        desktop.openInContinue.set(id, { host: 'codex', label: 'Codex', running: false });
        fireEvent.click(arrow());
        await waitFor(() => expect(entry()).not.toBeNull());
        expect(within(menu()).getByRole('group', { name: 'Continue this chat in Codex' })).not.toBeNull();
    });
});

describe('Change Jason itself', () => {
    const changes = (desktop: StubDesktop) => desktop.calls.filter((each) => each.name === 'openIn.changeJason');
    const entry = () => within(menu()).getByRole('menuitem', { name: /^Change Jason itself/ });
    const line = "Open your own AI on Jason's roles and plugins, and tell it what to change.";
    /** A console whose desktop offers the entry, as the person's own installation does. */
    async function offered(): Promise<StubDesktop> {
        const desktop = chatDesktop();
        desktop.openInKept.changeJason = true;
        return renderConsole('live', desktop);
    }

    it('draws the entry with its line where the desktop offers it, naming no host, and none where it offers none', async () => {
        await offered();
        fireEvent.click(arrow());
        await waitFor(() => expect(entry()).not.toBeNull());
        expect(entry().querySelector('span')?.textContent).toBe('Change Jason itself');
        expect(entry().querySelector('small')?.textContent).toBe(line);
        expect(entry().textContent).not.toMatch(/Claude Code|Codex/);
        expect((entry() as HTMLButtonElement).disabled).toBe(false);
        expect(entry().className).not.toBe('on');
        expect(within(menu()).getByRole('group', { name: 'Change Jason itself' })).not.toBeNull();
    });

    it.each(['live', 'demo'] as const)('draws no entry where the desktop offers none, in %s', async (kept) => {
        await renderConsole(kept);
        fireEvent.click(arrow());
        await waitFor(() => expect(within(menu()).getAllByRole('menuitem').length).toBeGreaterThan(1));
        expect(within(menu()).queryByRole('menuitem', { name: /^Change Jason itself/ })).toBeNull();
        expect(menu().querySelector('.open-in-change')).toBeNull();
    });

    it('opens through the desktop with nothing sent, closes the menu and says what opened', async () => {
        const desktop = await offered();
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        expect(screen.queryByRole('menu')).toBeNull();
        await waitFor(() => expect(changes(desktop)).toHaveLength(1));
        expect(changes(desktop)[0]!.args).toEqual([]);
        expect(opens(desktop)).toEqual([]);
        await waitFor(() =>
            expect(document.querySelector('.open-in-toast')?.textContent).toBe(
                "Opened your own AI in a new terminal, on Jason's roles and plugins. Tell it what you want changed.",
            ),
        );
    });

    it('is never the default: after it, the main part opens the place it opened before', async () => {
        const desktop = await offered();
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        await waitFor(() => expect(changes(desktop)).toHaveLength(1));
        await waitFor(() => expect(targets(desktop).length).toBeGreaterThan(2));
        expect(mainPart().textContent).toBe('Open in Claude Code');
        fireEvent.click(mainPart());
        await waitFor(() => expect(opens(desktop)).toHaveLength(1));
        expect(opens(desktop)[0]!.args).toEqual(['claude', undefined]);
    });

    it('says the desktop’s refusal beside the button, in its own sentence, and draws no toast', async () => {
        const desktop = await offered();
        desktop.refuse.set('openIn.changeJason', 'Change Jason itself needs Claude Code or Codex on this computer.');
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        const alert = await waitFor(() => within(split().parentElement!).getByRole('alert'));
        expect(alert.textContent).toBe('Change Jason itself needs Claude Code or Codex on this computer.');
        expect(document.querySelector('.open-in-toast')).toBeNull();
    });

    it('sits in its own group after the places and above Continue this chat', async () => {
        const desktop = await offered();
        const id = await waitFor(() => {
            const chat = currentChat.value;
            expect(chat).not.toBeNull();
            expect(activePanel.value).toBe('chat:' + chat);
            return chat!;
        });
        desktop.openInContinue.set(id, { host: 'claude', label: 'Claude Code', running: false });
        fireEvent.click(arrow());
        // The entry may be drawn from the answer read before; Continue only from the read the arrow asks for.
        await waitFor(() =>
            expect(within(menu()).getByRole('menuitem', { name: /^Continue this chat in/ })).not.toBeNull(),
        );
        const all = within(menu()).getAllByRole('menuitem');
        expect(all.at(-2)).toBe(entry());
        expect(all.at(-1)?.querySelector('span')?.textContent).toBe('Continue this chat in Claude Code');
        const groups = [...menu().children].map((each) => each.className);
        expect(groups.slice(-2)).toEqual(['open-in-change', 'open-in-continue']);
    });
});

describe('Change Jason itself while one is opening', () => {
    it('opens nothing more for a second choice while the first is still being opened', async () => {
        const desktop = chatDesktop();
        desktop.openInKept.changeJason = true;
        await renderConsole('live', desktop);
        let asked = 0;
        let finish: (value: null) => void = () => undefined;
        desktop.openIn.changeJason = () => {
            asked++;
            return new Promise<null>((resolve) => {
                finish = resolve;
            });
        };
        const entry = () => within(menu()).getByRole('menuitem', { name: /^Change Jason itself/ });
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        await waitFor(() => expect(asked).toBe(1));
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        expect(asked).toBe(1);
        finish(null);
        await waitFor(() => expect(document.querySelector('.open-in-toast')).not.toBeNull());
        // Once it has answered, the entry opens again.
        fireEvent.click(arrow());
        fireEvent.click(await waitFor(() => entry()));
        await waitFor(() => expect(asked).toBe(2));
        finish(null);
    });
});
