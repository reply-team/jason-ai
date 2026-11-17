import { act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import { attention } from '../../console/needs';
import { forgetWaiting } from '../../console/waiting';
import { readingSentence } from '../../console/waiting-model';
import { chats, conversations, here } from '../../console/state';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { href, route, type Route } from '../../router';
import { stubDesktop, stubStream } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { linesOf, searchIntent, firstSearchLines } from '../../test/search-words';
import { nothingWaitsInChats } from '../../test/waiting';
import { STALE_SENTENCE } from '../attention/message-edit';
import { doubtWords } from '../send-modes/model';
import * as store from './store';

// The Work items page decides the approval a waiting item waits for by the rule every place that approves keeps: Approve
// is live only once that approval has been read whole, a doubted send's doubt is drawn first, and a message is checked
// against its work item at the press. It is a rule of who may approve what, not a look, so it holds wherever the page is
// drawn: in the classic browser shell and in the console's area. Each story is read against exchanges one runtime
// recorded, so the list's row, the item, the approval listed and the approval read are one and the same.

const recordedAt = new Date('2026-10-16T09:00:00Z');
const item = fixtures['workitem.get/panel-doubted'].response;
const approval = fixtures['approval.list/panel-doubted'].response.items[0]!;
const doubted = fixtures['approval.get/panel-doubted'].response;
const body = 'Hi Ines, thanks for the talk at the meetup.';
const address: Route = { screen: 'workitems', tab: 'now', filters: {}, id: item.id };

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
    forgetWaiting();
    store.closeItem();
    // The page's own holder of whole reads outlives a closed item, so each row starts with none kept.
    store.reads.approvals.value = {};
    store.reads.unread.value = {};
});
afterEach(() => {
    vi.useRealTimers();
});

/** The exchanges the page reads to show the doubted send's panel, and those it writes and reads when it is approved. */
function doubtedSend(listed: readonly FixtureName[]): FakeApi {
    return (
        new FakeApi()
            .answer('role.list', 'role.list/all')
            .answer('workitem.list', 'workitem.list/panel-doubted-now', 'workitem.list/panel-doubted-approved-now')
            .answer(
                'workitem.count',
                'workitem.count/panel-doubted-now',
                'workitem.count/panel-doubted-planned',
                'workitem.count/panel-doubted-done',
                'workitem.count/panel-doubted-approved-now',
                'workitem.count/panel-doubted-approved-planned',
                'workitem.count/panel-doubted-approved-done',
            )
            // Read as the panel opens, again at the press of Approve to check the message, and once more after it.
            .answer(
                'workitem.get',
                'workitem.get/panel-doubted',
                'workitem.get/panel-doubted',
                'workitem.get/panel-doubted-approved',
            )
            .answer('journal.list', 'journal.list/panel-doubted', 'journal.list/panel-doubted-approved')
            .answer('approval.list', ...listed, 'approval.list/panel-doubted')
            .answer('approval.get', 'approval.get/panel-doubted')
            .answer('approval.approve', 'approval.approve/panel-doubted')
    );
}

/** The send whose text another hand moves after the panel read its approval: the press finds it moved. */
function movedSend(listed: readonly FixtureName[]): FakeApi {
    return new FakeApi()
        .answer('role.list', 'role.list/all')
        .answer('workitem.list', 'workitem.list/panel-moved-now', 'workitem.list/panel-moved-edited-now')
        .answer(
            'workitem.count',
            'workitem.count/panel-moved-now',
            'workitem.count/panel-moved-planned',
            'workitem.count/panel-moved-done',
            'workitem.count/panel-moved-edited-now',
            'workitem.count/panel-moved-edited-planned',
            'workitem.count/panel-moved-edited-done',
        )
        .answer('workitem.get', 'workitem.get/panel-moved', 'workitem.get/panel-moved-edited')
        .answer('journal.list', 'journal.list/panel-moved', 'journal.list/panel-moved-edited')
        .answer('approval.list', ...listed, 'approval.list/panel-moved')
        .answer('approval.get', 'approval.get/panel-moved');
}

/** A paid search waiting: what the page reads to show its panel. */
function waitingSearch(listed: readonly FixtureName[]): FakeApi {
    return new FakeApi()
        .answer('role.list', 'role.list/all')
        .answer('workitem.list', 'workitem.list/search-now')
        .answer(
            'workitem.count',
            'workitem.count/search-now',
            'workitem.count/search-planned',
            'workitem.count/search-done',
        )
        .answer('workitem.get', 'workitem.get/search-first')
        .answer('journal.list', 'journal.list/search-first')
        .answer('approval.list', ...listed, 'approval.list/search-first-item')
        .answer('approval.get', 'approval.get/search-first');
}

/** The browser's own shell, opened at the item's address. */
async function inClassic(fake: FakeApi, at: Route = address): Promise<void> {
    renderApp(fake);
    location.hash = href(at);
    route.value = at;
}

/** The console, with the page brought into its one area at the item's address, as a link or the palette brings it. */
async function inConsole(fake: FakeApi, at: Route = address): Promise<void> {
    mode.value = 'live';
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    const shell = nothingWaitsInChats(fake)
        .answer('decision.list', 'decision.list/empty')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('system.info', 'system.info/with-ui');
    watch(shell);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    render(<Console api={shell} desktop={stubDesktop()} stream={stubStream().stream} />);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    flush(() => here().dock.page(at));
}

const panel = () => document.querySelector<HTMLElement>('.panel')!;
const approveButton = () => within(panel()).getByRole<HTMLButtonElement>('button', { name: 'Approve' });
const rejectButton = () => within(panel()).getByRole<HTMLButtonElement>('button', { name: 'Reject' });
const words = (element: Element) => element.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const sent = (fake: FakeApi, operation: string) =>
    fake.calls.filter((call) => call.operation === operation).map((call) => call.body);
const before = (a: Node, b: Node) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;

// Needs you reads what waits everywhere as the console starts: nothing does, in these stories, but the item's approval.
const shells: [string, readonly FixtureName[], (fake: FakeApi, at?: Route) => Promise<void>][] = [
    ['the classic browser shell', [], inClassic],
    ["the console's area", ['approval.list/empty'], inConsole],
];

describe.each(shells)('Work items in %s: Approve by the rule', (_, listed, open) => {
    it('holds Approve until the approval is read whole, then draws the doubt first and approves a checked message', async () => {
        const fake = doubtedSend(listed);
        const release = fake.hold('approval.get');
        await open(fake);

        // The row the tab lists is the item the panel reads, and the approval it waits for is the one read whole.
        await waitFor(() => within(panel()).getByRole('link', { name: /Waiting for your approval/ }));
        expect(fixtures['workitem.list/panel-doubted-now'].response.items.map((row) => row.id)).toContain(item.id);
        expect(approval.work_item_id).toBe(item.id);
        expect(sent(fake, 'approval.get')).toEqual([{ approval_id: approval.id }]);

        // While the read runs Approve is not live, and the panel says why in the gate's words; Reject needs no read.
        expect(words(panel())).toContain(readingSentence('needs'));
        expect(approveButton().disabled).toBe(true);
        expect(rejectButton().disabled).toBe(false);
        fireEvent.click(approveButton());
        expect(sent(fake, 'approval.approve')).toEqual([]);

        release();
        await waitFor(() => expect(approveButton().disabled).toBe(false));
        expect(words(panel())).not.toContain(readingSentence('needs'));
        const heading = within(panel()).getByText(doubtWords(doubted.preview)!.heading);
        const message = within(panel()).getByText(body);
        expect(before(heading, message)).toBe(true);
        expect(before(heading, approveButton())).toBe(true);
        expect(before(message, approveButton())).toBe(true);

        const reads = sent(fake, 'workitem.get').length;
        fireEvent.click(approveButton());
        await waitFor(() => expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: approval.id }]));
        // The press read the work item once more, to check the message, before it approved.
        expect(sent(fake, 'workitem.get').slice(reads, reads + 1)).toEqual([{ work_item_id: item.id }]);
        await waitFor(() => expect(within(panel()).queryByRole('button', { name: 'Approve' })).toBeNull());
        expect(words(panel())).toMatch(/in Spring launch · \S+ created/);
    });

    it('names a paid search waiting in words, and shows its definition and arguments as written under its intent', async () => {
        const fake = waitingSearch(listed);
        await open(fake, { screen: 'workitems', tab: 'now', filters: {}, id: 'wi_1' });
        await waitFor(() => expect(approveButton().disabled).toBe(false));
        expect(within(panel()).getByRole('heading', { level: 2 }).textContent).toBe('Find people');
        expect(within(panel()).getByText(searchIntent)).toBeTruthy();
        expect(linesOf(panel(), '.search-lines')).toEqual(firstSearchLines);
        expect(sent(fake, 'workitem.get')).toEqual([{ work_item_id: 'wi_1' }]);
    });

    it('holds Approve when the approval could not be read, says so with what failed, and still lets Reject act', async () => {
        const fake = doubtedSend(listed);
        fake.unreachable('approval.get');
        await open(fake);

        await waitFor(() => expect(words(panel())).toContain('This approval could not be read'));
        expect(words(panel())).toContain('Failed to fetch');
        expect(approveButton().disabled).toBe(true);
        expect(within(panel()).queryByText(doubtWords(doubted.preview)!.heading)).toBeNull();
        expect(within(panel()).queryByText(body)).toBeNull();
        expect(rejectButton().disabled).toBe(false);
    });

    it('says what a person does to read an unread approval again, and clears the old failure once that read starts', async () => {
        const fake = doubtedSend(listed);
        const reachable = fake.unreachable('approval.get');
        await open(fake);
        await waitFor(() => expect(words(panel())).toContain('Failed to fetch'));
        expect(words(panel())).toContain('close this item and open it again to read it again');

        reachable();
        const release = fake.hold('approval.get');
        await flush(async () => {
            store.closeItem();
            await store.openItem(fake, item.id);
        });
        await waitFor(() => expect(words(panel())).toContain(readingSentence('needs')));
        expect(words(panel())).not.toContain('Failed to fetch');
        expect(approveButton().disabled).toBe(true);
        release();
        await waitFor(() => expect(approveButton().disabled).toBe(false));
    });

    it('approves nothing when the message moved after its approval was read, says so, and reads the item again', async () => {
        const fake = movedSend(listed);
        await open(fake);

        await waitFor(() => expect(approveButton().disabled).toBe(false));
        fireEvent.click(approveButton());
        await waitFor(() => expect(words(panel())).toContain(STALE_SENTENCE));
        expect(sent(fake, 'approval.approve')).toEqual([]);
        // The item as it stands now: back in the queue under the new text, no longer waiting for this approval.
        await waitFor(() => expect(within(panel()).queryByRole('button', { name: 'Approve' })).toBeNull());
        expect(words(panel())).toContain(STALE_SENTENCE);
        // And the tab, which no longer lists it as waiting.
        await waitFor(() => expect(sent(fake, 'workitem.list')).toHaveLength(2));
    });
});

describe('Work items: the gate it approves by', () => {
    it('keeps the approval it read in the shared gate’s holder of whole reads, which the Work items page owns', async () => {
        const fake = doubtedSend([]);
        await inClassic(fake);
        await waitFor(() => expect(store.reads.approvals.value[approval.id]?.id).toBe(approval.id));
        expect(store.reads.store.approvals.value.map((row) => row.id)).toEqual([approval.id]);
        expect(store.reads.key).toBe('workitems');
    });
});
