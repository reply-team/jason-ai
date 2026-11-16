import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { Bridge } from '../platform/bridge';
import { demoFrame, demoPage } from '../platform/demo-frame';
import { loadMode, mode, modeRefusal, modeUnopened } from '../platform/mode';
import { noDesktop, setPlace, tabDesktop, type PlaceKind } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { parseRoute, route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { standInBridge, standInSecret } from '../test/bridge';
import { chatDesktop, setupFound, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations, lines, setup, setupError } from './state';

// The demo in a browser tab is a view of that tab alone: the demo page in a frame of its own, sealed, and the Demo | Live
// switch drawn over the page's own strip. Choosing it moves the address to #/demo and asks the desktop nothing, so the
// desktop's mode and its file stay as they were; Live, Back or the address bring the console back. In the desktop's
// own window the switch still asks the desktop, which starts again on the other installation.

const frame = () => document.querySelector<HTMLIFrameElement>('iframe');
const switchIn = (where: HTMLElement | Document = document) =>
    within(where as HTMLElement).getByRole('group', { name: 'Data' });
const button = (name: 'Demo' | 'Live') => within(switchIn()).getByRole('button', { name }) as HTMLButtonElement;
const consoleShown = () => document.querySelector('.side') !== null;

let present = true;
const realPresent = demoPage.present;

async function renderIn(kind: PlaceKind, hash = '') {
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
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('plugin.list', 'plugin.list/none')
        .answer('route.list', 'route.list/empty')
        .answer('ui.tokens', 'ui.tokens/two')
        .answer('system.info', 'system.info/with-ui');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    history.replaceState(null, '', `/ui/${hash}`);
    route.value = { screen: 'attention' };
    if (hash !== '') route.value = parseRoute(hash);
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    await session.bootstrap();
    render(<Console api={fake} desktop={object} stream={stubStream().stream} bootstrapped />);
    return { desktop, stand };
}

beforeEach(() => {
    present = true;
    demoPage.present = () => Promise.resolve(present);
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    setupError.value = null;
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
    demoPage.present = realPresent;
    setPlace('window');
    mode.value = 'live';
    modeRefusal.value = null;
    modeUnopened.value = null;
    history.replaceState(null, '', '/ui/');
});

describe.each(['tab', 'none'] as const)('the demo in a browser tab (%s)', (kind) => {
    it('is offered by the switch, on, with what it does in its title', async () => {
        await renderIn(kind);
        await waitFor(() => expect(consoleShown()).toBe(true));
        expect(button('Demo').disabled).toBe(false);
        expect(button('Demo').title).toBe('Shows the demo in this tab only. Your installation is unchanged.');
        expect(button('Live').getAttribute('aria-pressed')).toBe('true');
    });

    it('shows the page in a frame with exactly its attributes, at #/demo, and asks the desktop nothing', async () => {
        const { desktop, stand } = await renderIn(kind);
        await waitFor(() => expect(consoleShown()).toBe(true));
        fireEvent.click(button('Demo'));
        await waitFor(() => expect(frame()).not.toBeNull());
        expect(location.hash).toBe('#/demo');
        const shown = frame()!;
        expect(Object.fromEntries(Array.from(shown.attributes, (each) => [each.name, each.value]))).toEqual({
            class: 'demo-frame',
            src: demoFrame.src,
            sandbox: demoFrame.sandbox,
            allow: demoFrame.allow,
            referrerpolicy: demoFrame.referrerPolicy,
            loading: demoFrame.loading,
            title: demoFrame.title,
        });
        expect(demoFrame).toMatchObject({ src: 'sealed/demo.html', sandbox: 'allow-scripts', allow: '' });
        expect(consoleShown()).toBe(false);
        expect(button('Demo').getAttribute('aria-pressed')).toBe('true');
        expect(desktop.calls.filter((each) => each.name.startsWith('mode.set'))).toEqual([]);
        expect(stand.asked.filter((each) => each.member === 'mode.set')).toEqual([]);
        expect(mode.value).toBe('live');
    });

    it('draws only the switch over the page, at the right end of its strip', async () => {
        await renderIn(kind, '#/demo');
        await waitFor(() => expect(frame()).not.toBeNull());
        const over = document.querySelector<HTMLElement>('.demo-over')!;
        expect(
            within(over)
                .getAllByRole('button')
                .map((each) => each.textContent),
        ).toEqual(['Demo', 'Live']);
        expect(over.textContent).toBe('DemoLive');
        expect(over.style.getPropertyValue('--demo-strip')).toBe('38px');
    });

    it('goes back to where the console was on Live, and the frame is gone', async () => {
        await renderIn(kind, '#/system');
        await waitFor(() => expect(consoleShown()).toBe(true));
        fireEvent.click(button('Demo'));
        await waitFor(() => expect(frame()).not.toBeNull());
        expect(button('Live').title).toBe(
            'Back to your installation. The demo was shown in this tab only, and nothing of your installation changed.',
        );
        fireEvent.click(button('Live'));
        await waitFor(() => expect(consoleShown()).toBe(true));
        expect(frame()).toBeNull();
        expect(location.hash).toBe('#/system');
    });

    it('leaves the demo when the address does, as Back moves it', async () => {
        await renderIn(kind);
        await waitFor(() => expect(consoleShown()).toBe(true));
        fireEvent.click(button('Demo'));
        await waitFor(() => expect(frame()).not.toBeNull());
        flush(() => {
            location.hash = '#/attention';
            dispatchEvent(new HashChangeEvent('hashchange'));
        });
        await waitFor(() => expect(consoleShown()).toBe(true));
        expect(frame()).toBeNull();
    });

    it('opens on the demo when the address names it, as after a reload', async () => {
        await renderIn(kind, '#/demo');
        await waitFor(() => expect(frame()).not.toBeNull());
        expect(consoleShown()).toBe(false);
        fireEvent.click(button('Live'));
        await waitFor(() => expect(consoleShown()).toBe(true));
        expect(location.hash).toBe('#/attention');
    });

    it('says so, and draws no frame, where the build carries no demo page', async () => {
        present = false;
        await renderIn(kind, '#/demo');
        await waitFor(() => screen.getByText('This build of the UI carries no demo.'));
        expect(frame()).toBeNull();
        expect(button('Live').disabled).toBe(false);
    });
});

describe('the switch in the desktop’s own window', () => {
    it('still asks the desktop to start again on the demo, and draws no frame', async () => {
        const { desktop } = await renderIn('window');
        await waitFor(() => expect(consoleShown()).toBe(true));
        fireEvent.click(button('Demo'));
        await waitFor(() =>
            expect(desktop.calls.filter((each) => each.name === 'mode.set')).toEqual([
                { name: 'mode.set', args: ['demo'] },
            ]),
        );
        expect(frame()).toBeNull();
        expect(location.hash).not.toBe('#/demo');
    });

    it('shows the console at #/demo: the window’s demo is the desktop’s own view', async () => {
        await renderIn('window', '#/demo');
        await waitFor(() => expect(consoleShown()).toBe(true));
        expect(frame()).toBeNull();
    });
});
