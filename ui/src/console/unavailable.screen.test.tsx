import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { loadMode, mode } from '../platform/mode';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import type { DesktopSetup } from '../platform/desktop';
import { chatDesktop, journalLine, setupFound, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import type { FixtureName } from '../test/fixtures';
import { nothingWaitsInChats } from '../test/waiting';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { ModeStrip } from './mode-strip';
import { chats, conversations, lines, setup, setupError, setupReadFailed } from './state';
import { unavailablePlugins } from './unavailable';

// A plugin a route names that the runtime holds back is said in the mode strip, in Live: read as the console starts and
// again on a `plugins_reloaded` line, against the recorded Reply package listed unavailable for a Reply CLI older than it
// asks for, and the global route to it.

const sentence =
    "Reply (reply) is unavailable, so its work does not run: 'reply' reports 0.23.0; the manifest asks for 0.23.2 or later. Install 'reply' 0.23.2 or later where the runtime can see it and run `jason plugin reload`.";

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

/** The desktop's state with its start's check of the Reply CLI where it stands. */
const checkAt = (startCheck: 'pending' | 'ended'): DesktopSetup => ({
    ...setupFound,
    reply: { ...setupFound.reply, startCheck },
});

async function renderConsole(
    kept: 'live' | 'demo',
    fake: FakeApi,
    desktop: StubDesktop = chatDesktop(),
    read = true,
): Promise<StubStream> {
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
    if (read) await waitFor(() => expect(setup.value).not.toBeNull());
    return stream;
}

const strip = () => screen.getByRole('region', { name: 'Mode bar' });
const alerts = () => within(strip()).queryAllByRole('alert');
const said = () => alerts().map((each) => each.textContent);

/** A console gone, and what it read forgotten, for the next one a row draws. */
function forget(): void {
    cleanup();
    setup.value = null;
    setupError.value = null;
    setupReadFailed.value = false;
    unavailablePlugins.value = [];
}

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    lines.value = [];
    setup.value = null;
    setupError.value = null;
    setupReadFailed.value = false;
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
    it('is said in Live as an alert, by the Accounts card’s name and the runtime’s problem with its repair; Reply’s own only once the desktop’s start check of the Reply CLI has ended, another’s at every moment', async () => {
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
        forget();

        // A start still checking its Reply CLI: Reply's line is held before the desktop's state is read and while it
        // says the check has not ended, a line for another plugin is said throughout, and once the check has ended with
        // the plugin still held back, Reply's is said, after the state is read again.
        const other = { id: 'tool', label: 'tool', problem: 'Problem.' };
        const otherLine = 'tool is unavailable, so its work does not run: Problem. Accounts';
        let answer: (state: DesktopSetup) => void = () => undefined;
        const starting = chatDesktop(checkAt('pending'));
        starting.setup.status = () => new Promise((resolve) => (answer = resolve));
        await renderConsole('live', api(['plugin.list/reply-unavailable']), starting, false);
        await waitFor(() => expect(unavailablePlugins.value.map((each) => each.id)).toEqual(['reply']));
        expect(setup.value).toBeNull();
        expect(said()).toEqual([]);
        flush(() => {
            unavailablePlugins.value = [...unavailablePlugins.value, other];
        });
        expect(said()).toEqual([otherLine]);
        flush(() => answer(checkAt('pending')));
        await waitFor(() => expect(setup.value?.reply.startCheck).toBe('pending'));
        expect(said()).toEqual([otherLine]);
        flush(() => starting.changed());
        expect(said()).toEqual([otherLine]);
        flush(() => answer(checkAt('ended')));
        await waitFor(() => expect(said()).toEqual([`${sentence} Accounts`, otherLine]));
        forget();

        // An act the desktop refused says so on Connect, and is no failed read: the line stays held. A read of the state
        // that failed holds nothing, one after the check was told or the first; a read that answers after it holds again
        // while it says the check has not ended.
        const later = chatDesktop(checkAt('pending'));
        await renderConsole('live', api(['plugin.list/reply-unavailable']), later);
        await waitFor(() => expect(unavailablePlugins.value).toHaveLength(1));
        expect(said()).toEqual([]);
        flush(() => {
            setupError.value = 'The desktop refused that.';
        });
        expect(said()).toEqual([]);
        later.setup.status = () => Promise.reject(new Error('The desktop could not answer.'));
        flush(() => later.changed());
        await waitFor(() => expect(said()).toEqual([`${sentence} Accounts`]));
        forget();
        const failing = chatDesktop(checkAt('pending'));
        failing.setup.status = () => Promise.reject(new Error('The desktop could not answer.'));
        await renderConsole('live', api(['plugin.list/reply-unavailable']), failing, false);
        await waitFor(() => expect(said()).toEqual([`${sentence} Accounts`]));
        failing.setup.status = () => Promise.resolve(checkAt('pending'));
        flush(() => failing.changed());
        await waitFor(() => expect(said()).toEqual([]));
        forget();

        // A desktop of an earlier version sends no such word: the line is said once its state is read.
        const older: Partial<DesktopSetup['reply']> = { ...setupFound.reply };
        delete older.startCheck;
        await renderConsole(
            'live',
            api(['plugin.list/reply-unavailable']),
            chatDesktop({ ...setupFound, reply: older as DesktopSetup['reply'] }),
        );
        await waitFor(() => expect(said()).toEqual([`${sentence} Accounts`]));
        forget();

        // A strip with no desktop behind it holds nothing, at once.
        unavailablePlugins.value = [{ id: 'reply', label: 'Reply (reply)', problem: 'Problem.' }];
        render(<ModeStrip />);
        expect(said()).toEqual(['Reply (reply) is unavailable, so its work does not run: Problem. Accounts']);
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

    it('is read again on a plugins_reloaded line, and goes by itself once the plugin is available; never said at all where the desktop’s own start update mends it', async () => {
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
        forget();

        // A start whose own update of the Reply CLI mends the plugin: the line is never said, from the page's first
        // read through the reload to the check's end.
        const mending = chatDesktop(checkAt('pending'));
        const mended = api(['plugin.list/reply-unavailable', 'plugin.list/card-accounts-reply']);
        const again = await renderConsole('live', mended, mending);
        await waitFor(() => expect(unavailablePlugins.value.map((each) => each.id)).toEqual(['reply']));
        expect(alerts()).toEqual([]);
        flush(() => {
            again.open();
            again.send(journalLine({ id: 'jrn_mended', kind: 'plugins_reloaded' }));
        });
        await waitFor(() => expect(mended.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(2));
        await waitFor(() => expect(unavailablePlugins.value).toEqual([]));
        expect(alerts()).toEqual([]);
        mending.setup.status = () => Promise.resolve(checkAt('ended'));
        flush(() => mending.changed());
        await waitFor(() => expect(setup.value?.reply.startCheck).toBe('ended'));
        expect(alerts()).toEqual([]);
    });
});

describe('The line after the stream comes back', () => {
    it('is read again when the stream reconnects, a restart’s plugins_reloaded line never seen', async () => {
        const fake = api(['plugin.list/card-accounts-reply', 'plugin.list/reply-unavailable']);
        const stream = await renderConsole('live', fake);
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(1));
        // The first open follows the read the console made as it started, and reads nothing again.
        flush(() => stream.open());
        await new Promise((done) => setTimeout(done, 20));
        expect(fake.calls.filter((each) => each.operation === 'plugin.list')).toHaveLength(1);
        expect(alerts()).toEqual([]);
        // The runtime restarts: the stream closes, connects again and opens, and no line about the load is sent.
        flush(() => stream.close());
        await waitFor(() => expect(stream.opened).toHaveLength(2), { timeout: 3000 });
        flush(() => stream.open());
        await waitFor(() => expect(alerts()).toHaveLength(1));
    });

    it('is read when the stream first opens, where the read as the console started failed', async () => {
        const fake = api(['plugin.list/reply-unavailable']);
        const back = fake.unreachable('plugin.list');
        const stream = await renderConsole('live', fake);
        await new Promise((done) => setTimeout(done, 20));
        expect(alerts()).toEqual([]);
        back();
        flush(() => stream.open());
        await waitFor(() => expect(alerts()).toHaveLength(1));
    });
});

describe('More than two routed plugins held back', () => {
    it('says one with no problem listed in a sentence of its own, with no colon left bare', () => {
        unavailablePlugins.value = [{ id: 'tool', label: 'tool', problem: '' }];
        render(<ModeStrip />);
        expect(alerts().map((each) => each.textContent)).toEqual([
            'tool is unavailable, so its work does not run. Accounts',
        ]);
    });

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
