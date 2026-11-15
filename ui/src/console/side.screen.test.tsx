import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { registry } from '../platform/slots';
import { setPlace } from '../platform/places';
import { mode, type Mode } from '../platform/mode';
import { parseRoute, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { stubDesktop, stubStream, type StubDesktop } from '../test/desktop';
import { inFront } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { styleOf } from '../test/stylesheet';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, here } from './state';

// The sidebar: the button that makes an agent and its caret's menu, the rows in their order, the number on Needs you,
// More and what is under it, the conversations under Recent, and Settings in the foot. Read against the recorded
// exchanges of the runtime's own tests.

function renderConsole(desktop: StubDesktop = stubDesktop(), where: Mode = 'demo'): StubDesktop {
    mode.value = where;
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        // Needs you reads each approval it lists whole, for its row's Approve.
        .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/none');
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
    return desktop;
}

const side = () => within(document.querySelector<HTMLElement>('.side')!);
const menu = () => side().getByRole('navigation', { name: 'Menu' });
const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const caret = () => side().getByRole('button', { name: 'More to create' });
const more = () => side().getByRole('button', { name: 'More' });
const entries = (name: string) => within(side().getByRole('menu', { name })).getAllByRole('menuitem');

beforeEach(() => {
    mode.value = 'live';
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.detail.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('the sidebar in Live', () => {
    it('draws the brand, New GTM agent, Search, Needs you with its count, Recent and Settings, and nothing else', async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        await waitFor(() => expect(side().getByText('Recent')).toBeTruthy());
        expect(side().getAllByRole('img', { name: 'Jason AI, GTM Harness', hidden: true }).length).toBeGreaterThan(0);
        expect(side().getByRole('button', { name: 'New GTM agent' })).toBeTruthy();
        const names = () =>
            within(menu())
                .getAllByRole('button')
                .map((each) => each.textContent?.replace(/\s+/g, ' ').trim().replace(/ \d+$/, ''));
        await waitFor(() =>
            expect(
                within(menu())
                    .getByRole('button', { name: /^Needs you/ })
                    .querySelector('.count')?.textContent,
            ).toBe('4'),
        );
        expect(names()).toEqual(['SearchCtrl K', 'Needs you']);
        for (const gone of ['Feed', 'Dashboard', 'People', 'Agents', 'Accounts', 'More'])
            expect(side().queryByRole('button', { name: gone }), gone).toBeNull();
        expect(side().getByRole('navigation', { name: 'Conversations' })).toBeTruthy();
        expect(document.querySelector('.sfoot')?.textContent?.trim()).toBe('Settings');
    });

    it('keeps every page it does not draw at its route, and its panels one link away', async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        // A page the sidebar does not draw opens at the address its screen registers.
        const address = registry
            .list('screen')
            .find((each) => each.id === 'system')!
            .href();
        flush(() => here().dock.page(parseRoute(address)));
        await waitFor(() => expect(inFront()).toBe('page:system'));
        flush(() => here().dock.show('campaigns'));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        flush(() => here().dock.show('connect'));
        await waitFor(() => expect(inFront()).toBe('Connect'));
    });
});

describe('the sidebar', () => {
    it('lists Search, Feed, Dashboard, Needs you, People, Agents, Accounts and More in that order, then Recent, with Settings in the foot', async () => {
        renderConsole();
        await waitFor(() => within(menu()).getByRole('button', { name: /^Feed/ }));
        const names = within(menu())
            .getAllByRole('button')
            .map((each) => each.textContent?.replace(/\s+/g, ' ').trim().replace(/ \d+$/, ''));
        // Search comes first, with the name of its key beside it.
        expect(names).toEqual([
            'SearchCtrl K',
            'Feed',
            'Dashboard',
            'Needs you',
            'People',
            'Agents',
            'Accounts',
            'More',
        ]);
        // The two groups are gone; the conversations sit under their header.
        expect(side().queryByRole('navigation', { name: 'Console' })).toBeNull();
        expect(side().queryByRole('navigation', { name: 'Pages' })).toBeNull();
        const recent = side().getByText('Recent');
        const conversationsNav = side().getByRole('navigation', { name: 'Conversations' });
        expect(recent.compareDocumentPosition(conversationsNav) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        // The foot holds Settings alone.
        const settings = side().getByRole('button', { name: 'Settings' });
        const version = document.querySelector<HTMLElement>('.sfoot')!;
        expect(version.textContent?.trim()).toBe('Settings');
        expect(version.contains(settings)).toBe(true);
    });

    it('opens Start from New GTM agent, bringing it forward, and makes no chat', async () => {
        const desktop = renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        fireEvent.click(side().getByRole('button', { name: 'New GTM agent' }));
        await waitFor(() => expect(inFront()).toBe('Start'));
        expect(calls(desktop, 'chat.create')).toEqual([]);
    });

    it('has a caret of three entries, each a title and a line, and nothing else', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(side().queryByRole('menu')).toBeNull();
        fireEvent.click(caret());
        expect(caret().getAttribute('aria-expanded')).toBe('true');
        const titles = entries('Create').map((each) => each.querySelector('.mt')?.textContent);
        expect(titles).toEqual(['New GTM agent', 'New chat', 'Connect an account']);
        expect(entries('Create').map((each) => each.querySelector('.ms')?.textContent)).toEqual([
            'Plan and run outreach',
            'Talk with Jason, with no agent yet',
            'Link the accounts Jason works through',
        ]);
        // Escape and a click beside it close it.
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(side().queryByRole('menu')).toBeNull();
    });

    it('leads the caret’s entries to Start, to a new chat, and to the Accounts screen', async () => {
        const desktop = renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        fireEvent.click(caret());
        fireEvent.click(entries('Create')[0]!);
        await waitFor(() => expect(inFront()).toBe('Start'));
        expect(calls(desktop, 'chat.create')).toEqual([]);
        expect(side().queryByRole('menu')).toBeNull();

        fireEvent.click(caret());
        fireEvent.click(entries('Create')[1]!);
        await waitFor(() => expect(calls(desktop, 'chat.create')).toHaveLength(1));
        expect(side().queryByRole('menu')).toBeNull();

        fireEvent.click(caret());
        fireEvent.click(entries('Create')[2]!);
        await waitFor(() => expect(route.value.screen).toBe('accounts'));
        expect(inFront()).toBe('page:accounts');
    });

    it('shows no number on Needs you while nothing waits, and the count of approvals and questions when something does', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const needs = () => within(menu()).getByRole('button', { name: /^Needs you/ });
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        // The recorded pending lists: two approvals and two questions.
        expect(attention.approvals.value).toHaveLength(2);
        await waitFor(() => expect(needs().querySelector('.count')?.textContent).toBe('4'));
        // Two approvals and one question: three.
        flush(() => {
            attention.questions.value = attention.questions.value.slice(0, 1);
        });
        await waitFor(() => expect(needs().querySelector('.count')?.textContent).toBe('3'));
        expect(needs().querySelector('.count')?.getAttribute('aria-label')).toBe('3 waiting');
        expect(within(menu()).getByRole('button', { name: 'Needs you 3 waiting' })).toBeTruthy();
        // Nothing waits: no number.
        flush(() => {
            attention.approvals.value = [];
            attention.questions.value = [];
        });
        await waitFor(() => expect(needs().querySelector('.count')).toBeNull());
    });

    it('holds Performance, Activity, Work items, Connect and System under More, and shows More active while one is in front', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(more().getAttribute('aria-current')).toBeNull();
        fireEvent.click(more());
        const titles = entries('More').map((each) => each.querySelector('.mt')?.textContent);
        expect(titles).toEqual(['Performance', 'Activity', 'Work items', 'Connect', 'System']);
        for (const each of entries('More')) expect(each.querySelector('.ms')?.textContent?.length).toBeGreaterThan(0);

        fireEvent.click(entries('More')[1]!);
        await waitFor(() => expect(inFront()).toBe('Activity'));
        expect(side().queryByRole('menu')).toBeNull();
        expect(more().getAttribute('aria-current')).toBe('true');

        fireEvent.click(side().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toBe('Needs you'));
        expect(more().getAttribute('aria-current')).toBeNull();

        fireEvent.click(more());
        fireEvent.click(entries('More')[3]!);
        await waitFor(() => expect(inFront()).toBe('Connect'));
        expect(more().getAttribute('aria-current')).toBe('true');

        // Leaving Connect for another panel makes More inactive again.
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        expect(more().getAttribute('aria-current')).toBeNull();
    });

    it('makes More inactive again once Activity or Connect is left for Needs you or the Feed', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(more());
        fireEvent.click(entries('More')[1]!);
        await waitFor(() => expect(inFront()).toBe('Activity'));
        expect(more().getAttribute('aria-current')).toBe('true');
        fireEvent.click(side().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toBe('Needs you'));
        expect(more().getAttribute('aria-current')).toBeNull();
        fireEvent.click(more());
        fireEvent.click(entries('More')[3]!);
        await waitFor(() => expect(inFront()).toBe('Connect'));
        expect(more().getAttribute('aria-current')).toBe('true');
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        expect(more().getAttribute('aria-current')).toBeNull();
    });

    it('still reaches every screen it reached before: a panel in front, a page at the address its registered screen gives', async () => {
        renderConsole();
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const addressOf = (id: string) =>
            registry
                .list('screen')
                .find((each) => each.id === id)!
                .href();
        const argsOf = (button: HTMLElement) => JSON.parse(button.dataset['args']!) as unknown[];
        // The pages: each row and each entry of More carries the address the screen itself registers.
        const row = (name: string) => side().getByRole('button', { name });
        expect(argsOf(row('People'))).toEqual([addressOf('people')]);
        expect(argsOf(row('Accounts'))).toEqual([addressOf('accounts')]);
        expect(argsOf(row('Settings'))).toEqual([addressOf('settings')]);
        fireEvent.click(more());
        expect(entries('More').map((each) => argsOf(each))).toEqual([
            [addressOf('performance')],
            ['activity'],
            [addressOf('workitems')],
            ['connect'],
            [addressOf('system')],
        ]);
        fireEvent.click(more());
        // The panels of the console's own.
        fireEvent.click(side().getByRole('button', { name: 'Agents' }));
        await waitFor(() => expect(inFront()).toBe('Agents'));
        fireEvent.click(more());
        fireEvent.click(entries('More')[1]!);
        await waitFor(() => expect(inFront()).toBe('Activity'));
        fireEvent.click(more());
        fireEvent.click(entries('More')[3]!);
        await waitFor(() => expect(inFront()).toBe('Connect'));
        fireEvent.click(side().getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toBe('Needs you'));
    });
});

describe('the sidebar’s look', () => {
    /** The console drawn, and what waits read: the sidebar is painted afresh no more. */
    const settled = async () => {
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        await waitFor(() => expect(attention.loaded.value).toBe(true));
        await waitFor(() => side().getByText('Recent'));
    };

    it('is 284 px wide, and marks the row in front bold on the hover ground', async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const aside = document.querySelector<HTMLElement>('.jc-console .side')!;
        expect(styleOf(aside)).toMatchObject({ width: '284px', flex: '0 0 284px', background: 'var(--side-bg)' });
        fireEvent.click(within(menu()).getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toBe('Needs you'));
        const needs = within(menu()).getByRole('button', { name: /^Needs you/ });
        expect(needs.getAttribute('aria-current')).toBe('true');
        expect(styleOf(needs)).toMatchObject({ background: 'var(--hover)', 'font-weight': '600' });
        expect(styleOf(within(menu()).getByRole('button', { name: /^Search/ }))['font-weight']).toBeUndefined();
    });

    it('counts what waits on Needs you in red with white figures, and a conversation’s in amber', async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const count = await waitFor(() => {
            const found = within(menu())
                .getByRole('button', { name: /^Needs you/ })
                .querySelector('.count');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(styleOf(count)).toMatchObject({
            background: 'var(--red)',
            color: '#fff',
            'font-size': '11px',
            'font-weight': '700',
            'border-radius': '9px',
            'line-height': '17px',
        });
        const row = document.createElement('button');
        row.className = 'item recent-row';
        row.innerHTML = '<span class="count">1</span>';
        document.querySelector('.jc-console .side nav[aria-label="Conversations"]')!.append(row);
        expect(styleOf(row.querySelector('.count')!)).toMatchObject({
            background: 'var(--amber-2)',
            color: 'var(--amber)',
            'font-weight': '700',
        });
    });

    it('heads the sidebar with the mark and the wordmark at the brand’s height', async () => {
        renderConsole(stubDesktop(), 'live');
        await settled();
        // The sidebar is painted afresh as what it shows changes: each element is read once it has settled.
        const brand = document.querySelector<HTMLElement>('.jc-console .side .brand')!;
        expect(styleOf(brand)).toMatchObject({ padding: '18px 18px 12px' });
        expect(styleOf(brand.querySelector('.mark')!)).toMatchObject({ width: '20px', height: '20px' });
        expect(styleOf(brand.querySelector('.wordmark.light')!)).toMatchObject({ height: '19px' });
    });

    it('draws New GTM agent as one full-width box, its caret a quiet end of the same box', async () => {
        renderConsole(stubDesktop(), 'live');
        await settled();
        const button = () => side().getByRole('button', { name: 'New GTM agent' });
        const row = () => button().closest<HTMLElement>('.newrow')!;
        expect(row().contains(caret())).toBe(true);
        // Start is in front: the whole box is marked, the caret's end with it.
        expect(button().getAttribute('aria-current')).toBe('true');
        expect(styleOf(row())).toMatchObject({ background: 'var(--hover)', 'border-color': 'var(--accent-line)' });
        fireEvent.click(within(menu()).getByRole('button', { name: /^Needs you/ }));
        await waitFor(() => expect(inFront()).toBe('Needs you'));
        expect(styleOf(row())).toMatchObject({
            margin: '2px 14px 8px',
            border: '1px solid var(--line)',
            'border-radius': '12px',
            background: 'var(--panel)',
        });
        expect(styleOf(button())).toMatchObject({ border: '0', background: 'none', flex: '1' });
        expect(styleOf(caret())).toMatchObject({ border: '0', background: 'none' });
    });

    it('heads Recent quietly, scrolls the rows, and keeps Settings at the foot of the window', async () => {
        renderConsole(stubDesktop(), 'live');
        await waitFor(() => side().getByText('Recent'));
        const header = document.querySelector<HTMLElement>('.jc-console .side .recent')!;
        expect(styleOf(header)).toMatchObject({
            padding: '14px 10px 4px',
            color: 'var(--faint)',
            'font-size': '13px',
            'font-weight': '400',
        });
        const body = document.querySelector<HTMLElement>('.jc-console .side .side-body')!;
        expect(styleOf(body)).toMatchObject({ display: 'flex', 'flex-direction': 'column', flex: '1' });
        const scroll = body.querySelector<HTMLElement>(':scope > .sscroll')!;
        expect(styleOf(scroll)).toMatchObject({ flex: '1', 'overflow-y': 'auto', padding: '0 8px 10px' });
        expect(scroll.contains(menu())).toBe(true);
        expect(scroll.contains(side().getByRole('navigation', { name: 'Conversations' }))).toBe(true);
        expect(body.lastElementChild?.classList.contains('sfoot')).toBe(true);
    });
});

describe('the sidebar in the Mac desktop’s window, which has no title bar', () => {
    const mac = () => Object.assign(stubDesktop(), { platform: 'darwin' as const });
    const band = () => document.querySelector<HTMLElement>('.jc-console > .title-drag');

    it('leaves the window’s buttons 26 px over the sidebar and drags the window by a 284 x 26 band there', async () => {
        renderConsole(mac(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const root = document.querySelector<HTMLElement>('.jc-console')!;
        expect(root.classList.contains('desktop-mac')).toBe(true);
        expect(styleOf(document.querySelector('.jc-console .side')!)).toMatchObject({ 'padding-top': '26px' });
        expect(band()).not.toBeNull();
        expect(band()!.getAttribute('aria-hidden')).toBe('true');
        expect(band()!.childElementCount).toBe(0);
        expect(styleOf(band()!)).toMatchObject({
            position: 'fixed',
            top: '0',
            left: '0',
            width: '284px',
            height: '26px',
            '-webkit-app-region': 'drag',
        });
        // The strip beside it is the rest of the window's drag region.
        expect(styleOf(screen.getByRole('region', { name: 'Mode bar' }))['-webkit-app-region']).toBe('drag');
    });

    it('keeps whatever floats over the drag regions out of them: menus, the palette and dialogs', async () => {
        renderConsole(mac(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(caret());
        const floating = [
            side().getByRole('menu', { name: 'Create' }),
            document.querySelector('.jc-console .menu-away')!,
        ];
        for (const each of floating) expect(styleOf(each)['-webkit-app-region'], each.className).toBe('no-drag');
        for (const name of ['palette-away', 'palette', 'overlay', 'dialog', 'act-dialog']) {
            const made = document.createElement('div');
            made.className = name;
            document.querySelector('.jc-console .area')!.append(made);
            expect(styleOf(made)['-webkit-app-region'], name).toBe('no-drag');
        }
    });

    it('draws no band and no room in a browser tab on a Mac, which keeps its own title bar', async () => {
        setPlace('tab', mac());
        renderConsole(mac(), 'live');
        await waitFor(() => expect(document.querySelector('.jc-console .side')).not.toBeNull());
        expect(document.querySelector('.jc-console')!.classList.contains('desktop-mac')).toBe(false);
        expect(band()).toBeNull();
    });

    it('draws no band on Windows and Linux, whose windows keep their frame', async () => {
        for (const platform of ['win32', 'linux'] as const) {
            renderConsole(Object.assign(stubDesktop(), { platform }), 'live');
            await screen.findByRole('heading', { name: 'Who do you want to reach?' });
            expect(document.querySelector('.jc-console')!.classList.contains('desktop-mac')).toBe(false);
            expect(band()).toBeNull();
            cleanup();
        }
    });
});
