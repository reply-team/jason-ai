import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, journalLine, stubStream, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import { nothingWaitsInChats } from '../test/waiting';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { ModeStrip } from './mode-strip';
import { chats, conversations, lines, setup } from './state';
import { unavailablePlugins } from './unavailable';

// A plugin a route names that the runtime holds back is said in the mode strip, in Live: read as the console starts and
// again on a `plugins_reloaded` line, against the recorded Reply package listed unavailable for a Reply CLI older than it
// asks for, and the global route to it.

const sentence =
    "Reply (reply) is unavailable, so its work does not run: 'reply' reports 0.21.0; the manifest asks for 0.22.0 or later. Install 'reply' 0.22.0 or later where the runtime can see it and run `jason plugin reload`.";

function api(plugins: FixtureName[], routes: FixtureName = 'route.list/card-accounts-reply'): FakeApi {
    return nothingWaitsInChats(new FakeApi())
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui')
        .answer('route.list', routes)
        .answer('plugin.list', ...plugins);
}

async function renderConsole(kept: 'live' | 'demo', fake: FakeApi): Promise<StubStream> {
    const desktop = chatDesktop();
    desktop.modeKept = kept;
    await loadMode(desktop);
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
    const stream = stubStream();
    render(<Console api={fake} desktop={desktop} stream={stream.stream} />);
    await waitFor(() => expect(setup.value).not.toBeNull());
    return stream;
}

const strip = () => screen.getByRole('region', { name: 'Mode bar' });
const alerts = () => within(strip()).queryAllByRole('alert');

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    unavailablePlugins.value = [];
});

afterEach(() => {
    cleanup();
    mode.value = 'live';
    unavailablePlugins.value = [];
});

describe('A routed plugin the runtime holds back, in the mode strip', () => {
    it('is said in Live as an alert, by the Accounts card’s name and the runtime’s problem with its repair', async () => {
        await renderConsole('live', api(['plugin.list/reply-unavailable']));
        const alert = await waitFor(() => {
            const [only, ...rest] = alerts();
            expect(rest).toEqual([]);
            return only!;
        });
        expect(alert.textContent).toBe(`${sentence} Accounts`);
        // Its link goes to Accounts, and nothing closes the line while the cause stands.
        fireEvent.click(within(alert).getByRole('button', { name: 'Accounts' }));
        await waitFor(() => expect(route.value).toEqual({ screen: 'accounts' }));
        expect(within(alert).getAllByRole('button')).toHaveLength(1);
        expect(alerts()).toHaveLength(1);
    });

    it('is not said where no route names the plugin', async () => {
        const fake = api(['plugin.list/reply-unavailable'], 'route.list/empty');
        await renderConsole('live', fake);
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(1));
        await new Promise((done) => setTimeout(done, 20));
        expect(alerts()).toEqual([]);
    });

    it('is never said in the demo, whose runtime is not the person’s installation', async () => {
        await renderConsole('demo', api(['plugin.list/reply-unavailable']));
        await new Promise((done) => setTimeout(done, 20));
        expect(alerts()).toEqual([]);
    });

    it('is read again on a plugins_reloaded line, and goes by itself once the plugin is available', async () => {
        const fake = api(['plugin.list/reply-unavailable', 'plugin.list/card-accounts-reply']);
        const stream = await renderConsole('live', fake);
        await waitFor(() => expect(alerts()).toHaveLength(1));
        // A line of another kind reads nothing again.
        flush(() => {
            stream.open();
            stream.send(journalLine({ id: 'jrn_other', kind: 'campaign_updated' }));
        });
        await new Promise((done) => setTimeout(done, 20));
        expect(fake.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(1);
        flush(() => stream.send(journalLine({ id: 'jrn_reload', kind: 'plugins_reloaded' })));
        await waitFor(() => expect(alerts()).toEqual([]));
        expect(fake.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(2);
    });
});

describe('More than two routed plugins held back', () => {
    it('are said in one line that counts them, each of up to two in a line of its own', () => {
        const one = { id: 'a', label: 'A (a)', problem: 'Problem a.' };
        unavailablePlugins.value = [one, { id: 'b', label: 'B (b)', problem: 'Problem b.' }];
        render(<ModeStrip />);
        expect(alerts().map((each) => each.textContent)).toEqual([
            'A (a) is unavailable, so its work does not run: Problem a. Accounts',
            'B (b) is unavailable, so its work does not run: Problem b. Accounts',
        ]);
        flush(() => {
            unavailablePlugins.value = [
                ...unavailablePlugins.value,
                { id: 'c', label: 'C (c)', problem: 'Problem c.' },
            ];
        });
        expect(alerts().map((each) => each.textContent)).toEqual([
            '3 plugins your routes name are unavailable, so their work does not run. Accounts',
        ]);
    });
});
