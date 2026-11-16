import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Unpaired, type Api } from '../api/client';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { loadMode, mode } from '../platform/mode';
import { noDesktop, releasesPage, setPlace, tabDesktop, type PlaceKind } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, setupFound, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { Console } from './console';

// A browser tab with no desktop never draws the console: one page of text and the Demo | Live switch, whatever the
// installation holds and whatever the address names. The desktop's window and a tab the desktop opened draw the console.

const page = () => document.querySelector<HTMLElement>('.no-desktop');
const consoleDrawn = () => document.querySelector('.side') !== null;

async function renderIn(kind: PlaceKind, hash = '', fake?: FakeApi | Api) {
    const desktop = chatDesktop({ ...setupFound });
    const stand = standInBridge(desktop);
    const object =
        kind === 'window'
            ? desktop
            : kind === 'tab'
              ? tabDesktop(new Bridge({ port: 43210, secret: standInSecret }, stand.fetch))
              : noDesktop();
    setPlace(kind, object);
    await loadMode(object);
    const api =
        fake ??
        new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/set')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('plugin.list', 'plugin.list/none')
            .answer('route.list', 'route.list/empty')
            .answer('ui.tokens', 'ui.tokens/two')
            .answer('system.info', 'system.info/with-ui');
    if (api instanceof FakeApi) watch(api);
    activatePlugins(browserPlugins);
    localStorage.clear();
    history.replaceState(null, '', '/ui/' + hash);
    route.value = hash === '' ? { screen: 'attention' } : parseRoute(hash);
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    render(<Console api={api as Api} desktop={object} stream={stubStream().stream} bootstrapped />);
}

beforeEach(() => {
    mode.value = 'live';
});

afterEach(() => {
    cleanup();
    setPlace('window');
    history.replaceState(null, '', '/ui/');
});

describe('a tab with no desktop', () => {
    it('says in three sentences that Live Mode is the desktop’s, and links to the releases', async () => {
        await renderIn('none');
        await waitFor(() => expect(page()).not.toBeNull());
        expect(screen.getByRole('heading', { level: 1 }).textContent).toBe(
            'Live Mode is only available in Jason Desktop.',
        );
        const paragraphs = Array.from(page()!.querySelectorAll('main p:not(.no-desktop-waiting)')).map(
            (each) => each.textContent,
        );
        expect(paragraphs).toEqual([
            'To access Live Mode, download Jason Desktop. The desktop app starts an AI session locally on your computer.',
            'Alternatively, you can build your own web interface or work directly with Jason from a terminal session with your AI using the installed Jason skills.',
        ]);
        const link = screen.getByRole('link', { name: 'download Jason Desktop' });
        expect(link.getAttribute('href')).toBe(releasesPage);
        expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('draws nothing else: no console, no sidebar, no Settings, only the Demo | Live switch, Live pressed', async () => {
        await renderIn('none');
        await waitFor(() => expect(page()).not.toBeNull());
        expect(consoleDrawn()).toBe(false);
        for (const selector of ['.side', '.shell', '.mode-strip', '.area', '[data-pane]', 'nav', 'aside', 'textarea'])
            expect(document.querySelector(selector), selector).toBeNull();
        expect(screen.queryByText('Settings')).toBeNull();
        const group = screen.getByRole('group', { name: 'Data' });
        expect(
            within(group)
                .getAllByRole('button')
                .map((each) => [each.textContent, each.getAttribute('aria-pressed')]),
        ).toEqual([
            ['Demo', 'false'],
            ['Live', 'true'],
        ]);
        expect(page()!.querySelectorAll('button')).toHaveLength(2);
    });

    it('keeps the address it was given, whichever screen it names', async () => {
        await renderIn('none', '#/campaigns/cmp_1/people');
        await waitFor(() => expect(page()).not.toBeNull());
        expect(location.hash).toBe('#/campaigns/cmp_1/people');
        expect(consoleDrawn()).toBe(false);
    });

    it('shows the demo from the switch, and Live brings the page back', async () => {
        await renderIn('none');
        await waitFor(() => expect(page()).not.toBeNull());
        fireEvent.click(screen.getByRole('button', { name: 'Demo' }));
        await waitFor(() => expect(document.querySelector('.demo-view')).not.toBeNull());
        expect(page()).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Live' }));
        await waitFor(() => expect(page()).not.toBeNull());
        expect(consoleDrawn()).toBe(false);
    });
});

describe('the line under the words', () => {
    const line = () => document.querySelector('.no-desktop-waiting');

    it('says what waits and where to decide it, in the words of the notification pointer', async () => {
        await renderIn('none');
        await waitFor(() => expect(line()).not.toBeNull());
        expect(line()!.textContent).toBe(
            '2 approvals and 2 questions are waiting in Jason. Decide in Jason Desktop, or ask your agent what Jason is waiting for, or run jason approval list and jason decision list.',
        );
    });

    it('is not drawn when nothing waits', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/empty')
            .answer('decision.list', 'decision.list/empty');
        await renderIn('none', '', fake);
        await waitFor(() =>
            expect(fake.calls.map((each) => each.operation).sort()).toEqual(['approval.list', 'decision.list']),
        );
        await waitFor(() => expect(page()).not.toBeNull());
        expect(line()).toBeNull();
    });

    it('is not drawn when the read fails, and the page is the three sentences alone', async () => {
        await renderIn('none', '', { call: () => Promise.reject(new Error('down')) } as unknown as Api);
        await waitFor(() => expect(page()).not.toBeNull());
        await new Promise((done) => setTimeout(done, 20));
        expect(line()).toBeNull();
        expect(page()!.querySelectorAll('main p')).toHaveLength(2);
    });
});

describe('a token that was revoked', () => {
    it('lands on pair-again: the page read answers unpaired, the session is cleared and the page gives way', async () => {
        await renderIn('none', '', { call: () => Promise.reject(new Unpaired()) } as unknown as Api);
        await waitFor(() => expect(screen.getByText(/isn't paired/)).toBeTruthy());
        expect(page()).toBeNull();
        expect(localStorage.getItem('jason.ui.session')).toBeNull();
    });
});

describe('the places that draw the console', () => {
    it.each(['window', 'tab'] as const)('is drawn in the %s, and the page is not', async (kind) => {
        await renderIn(kind);
        await waitFor(() => expect(consoleDrawn()).toBe(true));
        expect(page()).toBeNull();
    });
});
