import { readFileSync } from 'node:fs';
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
import { styleOf, tokenOf } from '../test/stylesheet';
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
        expect(styleOf(needs)).toMatchObject({ background: 'var(--sel)', 'font-weight': '500' });
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
            background: 'var(--count-bg)',
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

    it('draws Needs you as a check in a circle, Settings as a cog, and Recent’s filter as two sliders', async () => {
        renderConsole(stubDesktop(), 'live');
        await settled();
        // Each icon's lines, in the order the icon table draws them.
        const drawn = (icon: Element) =>
            [...icon.children].map((each) =>
                each.tagName === 'circle'
                    ? `circle ${each.getAttribute('cx')},${each.getAttribute('cy')} r${each.getAttribute('r')}`
                    : `path ${each.getAttribute('d')}`,
            );
        const needs = within(menu()).getByRole('button', { name: /^Needs you/ });
        expect(drawn(needs.querySelector('svg')!)).toEqual(['circle 12,12 r10', 'path M8.5 11.8l2.6 2.6 4.6-5']);
        const settings = side().getByRole('button', { name: 'Settings' });
        expect(drawn(settings.querySelector('svg')!)).toEqual([
            'path M9.6 4.4L10.4 1.5L13.6 1.5L14.4 4.4L15.7 4.9L18.3 3.5L20.5 5.7L19.1 8.3L19.6 9.6L22.5 10.4L22.5 13.6' +
                'L19.6 14.4L19.1 15.7L20.5 18.3L18.3 20.5L15.7 19.1L14.4 19.6L13.6 22.5L10.4 22.5L9.6 19.6L8.3 19.1' +
                'L5.7 20.5L3.5 18.3L4.9 15.7L4.4 14.4L1.5 13.6L1.5 10.4L4.4 9.6L4.9 8.3L3.5 5.7L5.7 3.5L8.3 4.9z',
            'circle 12,12 r3.5',
        ]);
        const filter = side().getByRole('button', { name: 'Filter' });
        expect(drawn(filter.querySelector('svg')!)).toEqual([
            'path M4 7h10M18 7h2M4 17h2M10 17h10',
            'circle 16,7 r2',
            'circle 8,17 r2',
        ]);
    });

    it('gives the count on Needs you a ground its white figures read on, in the light theme and both dark ones', async () => {
        renderConsole(stubDesktop(), 'live');
        // The sidebar is painted afresh as what it shows changes: the count's style is read while it is drawn.
        const style = await waitFor(() => {
            const found = within(menu())
                .getByRole('button', { name: /^Needs you/ })
                .querySelector('.count');
            expect(found).not.toBeNull();
            const read = styleOf(found!);
            expect(read.background).toBeDefined();
            return read;
        });
        // Each dark theme's tokens, read from the file: the one the system asks for sits under `@media`, which the
        // cascade reader leaves out. The run starts in ui/.
        const sheet = readFileSync('src/console/console.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
        const darkTokens = (head: string) => {
            const at = sheet.indexOf(head + ' {');
            expect(at, head).toBeGreaterThanOrEqual(0);
            const body = sheet.slice(sheet.indexOf('{', at) + 1, sheet.indexOf('}', at));
            return Object.fromEntries(
                [...body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((each) => [each[1]!, each[2]!.trim()]),
            );
        };
        const system = darkTokens("    :root:not([data-theme='light']) .jc-console");
        const chosen = darkTokens(":root[data-theme='dark'] .jc-console");
        // The dark theme is the same whether the system or the person chose it, the strip's tokens included.
        expect(system).toEqual(chosen);
        const resolve = (value: string, dark: Record<string, string>): string => {
            const named = /^var\((--[\w-]+)\)$/.exec(value);
            return named ? resolve(dark[named[1]!] ?? tokenOf(named[1]!) ?? '', dark) : value;
        };
        const luminance = (hex: string) => {
            const six = hex.length === 4 ? [...hex.slice(1)].map((c) => c + c).join('') : hex.slice(1);
            const [r, g, b] = [0, 2, 4].map((at) => {
                const c = parseInt(six.slice(at, at + 2), 16) / 255;
                return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
            });
            return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!;
        };
        for (const [theme, dark] of [
            ['light', {}],
            ['dark', chosen],
        ] as const) {
            const ground = resolve(style.background!, dark);
            const figures = resolve(style.color!, dark);
            expect([ground, figures], theme).toEqual([
                expect.stringMatching(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i),
                expect.stringMatching(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i),
            ]);
            const [hi, lo] = [luminance(ground), luminance(figures)].sort((x, y) => y - x);
            expect((hi! + 0.05) / (lo! + 0.05), theme).toBeGreaterThanOrEqual(4.5);
        }
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

    it('draws no band on Windows or Linux, whose buttons stand over the strip', async () => {
        for (const platform of ['win32', 'linux'] as const) {
            renderConsole(Object.assign(stubDesktop(), { platform }), 'live');
            await screen.findByRole('heading', { name: 'Who do you want to reach?' });
            expect(document.querySelector('.jc-console')!.classList.contains('desktop-mac')).toBe(false);
            expect(band()).toBeNull();
            cleanup();
        }
    });
});

describe('the sidebar where the window is narrow', () => {
    it("is brought over the area by the strip's button and put away by a choice in it or a tap beside it", async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const aside = () => document.querySelector<HTMLElement>('.jc-console .side')!;
        const button = () => screen.getByRole('button', { name: 'Sidebar' });
        expect(aside().classList.contains('open')).toBe(false);
        expect(button().getAttribute('aria-expanded')).toBe('false');
        expect(document.querySelector('.side-away')).toBeNull();
        fireEvent.click(button());
        expect(aside().classList.contains('open')).toBe(true);
        expect(button().getAttribute('aria-expanded')).toBe('true');
        fireEvent.click(document.querySelector('.side-away')!);
        expect(aside().classList.contains('open')).toBe(false);
        fireEvent.click(button());
        fireEvent.click(within(menu()).getByRole('button', { name: /^Needs you/ }));
        expect(aside().classList.contains('open')).toBe(false);
    });

    it('takes the focus when opened, makes the page behind unreachable, and Escape puts it away and gives the focus back', async () => {
        renderConsole(stubDesktop(), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const area = () => document.querySelector<HTMLElement>('.jc-console .mainc > .area')!;
        const button = screen.getByRole('button', { name: 'Sidebar' });
        expect(area().hasAttribute('inert')).toBe(false);
        fireEvent.click(button);
        expect(area().hasAttribute('inert')).toBe(true);
        expect(document.querySelector('.jc-console .side')!.contains(document.activeElement)).toBe(true);
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(document.querySelector('.jc-console .side')!.classList.contains('open')).toBe(false);
        expect(area().hasAttribute('inert')).toBe(false);
        expect(document.activeElement).toBe(button);
    });

    it('leaves nothing standing when the window grows past the narrow edge with it open', async () => {
        const listeners = new Set<() => void>();
        const list = {
            matches: false,
            addEventListener: (_: string, fn: () => void) => listeners.add(fn),
            removeEventListener: (_: string, fn: () => void) => listeners.delete(fn),
        };
        const original = window.matchMedia;
        window.matchMedia = (() => list) as unknown as typeof window.matchMedia;
        try {
            renderConsole(stubDesktop(), 'live');
            await screen.findByRole('heading', { name: 'Who do you want to reach?' });
            fireEvent.click(screen.getByRole('button', { name: 'Sidebar' }));
            expect(document.querySelector('.jc-console .side')!.classList.contains('open')).toBe(true);
            flush(() => {
                list.matches = true;
                for (const fn of [...listeners]) fn();
            });
            expect(document.querySelector('.jc-console .side')!.classList.contains('open')).toBe(false);
            expect(document.querySelector('.jc-console .mainc > .area')!.hasAttribute('inert')).toBe(false);
        } finally {
            window.matchMedia = original;
        }
    });
});

describe('the strip in the Windows and Linux desktop’s window, whose caption buttons stand over its right end', () => {
    const overlaid = ['win32', 'linux'] as const;
    const on = (platform: (typeof overlaid)[number]) => Object.assign(stubDesktop(), { platform });
    const strip = () => screen.getByRole('region', { name: 'Mode bar' });

    it('leaves the buttons’ width and the desktop’s band free at its right, read from the overlay’s own geometry', async () => {
        for (const platform of overlaid) {
            renderConsole(on(platform), 'live');
            await screen.findByRole('heading', { name: 'Who do you want to reach?' });
            const root = document.querySelector<HTMLElement>('.jc-console')!;
            expect(root.classList.contains('desktop-overlay'), platform).toBe(true);
            expect(root.classList.contains('desktop-mac')).toBe(false);
            // The free title bar area ends where the buttons begin; the desktop's band is 40 px left of them.
            expect(styleOf(strip())).toMatchObject({
                'padding-right':
                    'calc(12px + 40px + 100vw - env(titlebar-area-x, 0px) - env(titlebar-area-width, 100vw))',
                '-webkit-app-region': 'drag',
            });
            // Nothing is kept over the sidebar: the buttons are at the window's right, not its left.
            expect(styleOf(document.querySelector('.jc-console .side')!)['padding-top']).toBeUndefined();
            expect(document.querySelector('.jc-console > .title-drag')).toBeNull();
            cleanup();
        }
    });

    it('keeps Open in and the Demo | Live switch clickable inside the strip that drags the window', async () => {
        for (const platform of overlaid) {
            renderConsole(on(platform), 'demo');
            await waitFor(() => expect(strip()).not.toBeNull());
            const switches = within(strip()).getByRole('group', { name: 'Data' });
            expect(styleOf(switches)['-webkit-app-region'], platform).toBe('no-drag');
            for (const button of within(switches).getAllByRole('button'))
                expect(styleOf(button)['-webkit-app-region']).toBe('no-drag');
            const slot = strip().querySelector<HTMLElement>('[data-slot="open-in"]')!;
            const made = document.createElement('button');
            slot.append(made);
            expect(styleOf(made)['-webkit-app-region']).toBe('no-drag');
            cleanup();
        }
    });

    it('leaves nothing in a browser tab on Windows or Linux, nor in the Mac’s window', async () => {
        for (const platform of overlaid) {
            setPlace('tab', on(platform));
            renderConsole(on(platform), 'live');
            await waitFor(() => expect(document.querySelector('.jc-console .side')).not.toBeNull());
            expect(document.querySelector('.jc-console')!.classList.contains('desktop-overlay'), platform).toBe(false);
            cleanup();
        }
        setPlace('window');
        renderConsole(Object.assign(stubDesktop(), { platform: 'darwin' as const }), 'live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        expect(document.querySelector('.jc-console')!.classList.contains('desktop-overlay')).toBe(false);
    });
});
