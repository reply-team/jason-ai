import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { loadMode, mode } from '../platform/mode';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { journalLine, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { fixtures, type FixtureName } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention, forgetNeeds, reads } from './needs';
import { NEEDS_LIFT_REASON } from './needs-held';
import { chatWaits } from './needs-chats';
import { chats, lines } from './state';
import { forgetWaiting } from './waiting';

// Needs you's line for work held at a daily limit of launches: drawn above what waits whenever the runtime says items
// are held, even when nothing else waits; its one button lifts the limits as the person; it is never counted.

/** The recorded reads of the limits: items held, and none. */
const HELD: FixtureName = 'launches.list/limits';
const NONE: FixtureName = 'launches.list/lifted';
/** The recorded lift Needs you makes: as the paired person, with Needs you's reason. */
const LIFT = 'launches.lift/from-needs-you' satisfies FixtureName;

async function renderConsole(fake: FakeApi, journal: StubStream = stubStream(), demo = false): Promise<StubDesktop> {
    const desktop = stubDesktop();
    if (demo) {
        desktop.modeKept = 'demo';
        await loadMode(desktop);
    }
    watch(agentHeads(fake));
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    fake.answer('system.info', 'system.info/with-ui')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    if (!fake.offers('sender.list')) fake.answerAnyRequest('sender.list', 'sender.list/card-message-sender');
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    showNeeds();
    return desktop;
}

const showNeeds = () =>
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
const needs = () => screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
const held = () => needs().querySelector<HTMLElement>('.needs-held');
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const chips = () => [...needs().querySelectorAll<HTMLElement>('.needs-filter .btn')].map((each) => words(each));
const badge = () => document.querySelector<HTMLElement>('.side .count');
const lift = () => within(held()!).getByRole('button', { name: "Lift today's limits" });
const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
const sent = (fake: FakeApi, operation: string) =>
    fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
/** A journal line, as the runtime writes one: the console reads what waits again. */
const line = (journal: StubStream, id: string) =>
    flush(() => {
        journal.open();
        journal.send(journalLine({ id, kind: 'launch_limit_lifted' }));
    });

/** Nothing waits on a decision. */
const empty = (...limits: FixtureName[]) =>
    new FakeApi()
        .answer('approval.list', 'approval.list/empty')
        .answer('decision.list', 'decision.list/empty')
        .answer('launches.list', ...limits);
/** One approval waits. */
const one = (...limits: FixtureName[]) =>
    new FakeApi()
        .answer('approval.list', 'approval.list/needs-two-after')
        .answer('decision.list', 'decision.list/empty')
        .answer('approval.get', 'approval.get/needs-two-second')
        .answer('launches.list', ...limits);

/** What the line says while items are held, and after the lift, as a person reads it. */
const HELD_WORDS =
    "2 work items wait on a daily limit of launches until 15:00 (UTC). A lift lets every role in every agent run past today's limits until then. Lift today's limits";
const LIFTED_WORDS = 'Lifted until 15:00 (UTC). 2 work items were let go.';

beforeEach(() => {
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.approvalsCursor.value = null;
    attention.questionsCursor.value = null;
    reads.approvals.value = {};
    reads.unread.value = {};
    forgetWaiting();
    forgetNeeds();
    chatWaits.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});

afterEach(() => {
    mode.value = 'live';
});

describe('the line for work held at a daily limit', () => {
    it('shows alone on an empty Needs you, above the card, which says nothing of being caught up', async () => {
        await renderConsole(empty(HELD));
        await waitFor(() => expect(held()).not.toBeNull());
        expect(words(held()!)).toBe(HELD_WORDS);
        // One button, the lift; the card below keeps its sentence about decisions and draws no "caught up" heading.
        expect(within(held()!).getAllByRole('button').map(words)).toEqual(["Lift today's limits"]);
        const card = needs().querySelector<HTMLElement>('.card.needs-empty')!;
        expect(card.querySelector('.bd')).toBeNull();
        expect(needs().textContent).not.toContain('caught up');
        expect(words(card.querySelector('.needs-none-says')!)).toBe('Nothing is waiting for your decision.');
        expect(within(card).getAllByRole('button').map(words)).toEqual(['New GTM agent', 'Open your agents']);
        expect(held()!.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows above one approval, at the top of the list', async () => {
        await renderConsole(one(HELD));
        await waitFor(() => expect(needs().querySelector('[data-row="apr_2"]')).not.toBeNull());
        await waitFor(() => expect(held()).not.toBeNull());
        expect(words(held()!)).toBe(HELD_WORDS);
        const list = needs().querySelector<HTMLElement>('.needs-bar')!;
        expect(held()!.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('is not drawn while nothing is held', async () => {
        const fake = empty(NONE);
        await renderConsole(fake);
        await waitFor(() => expect(sent(fake, 'launches.list')).toHaveLength(1));
        await settle();
        expect(held()).toBeNull();
        // The empty state is the one Needs you draws with nothing held: the heading, then the sentence.
        const card = needs().querySelector<HTMLElement>('.card.needs-empty')!;
        expect(words(card.querySelector('.bd')!)).toBe("You're all caught up");
        expect(words(card.querySelector('.needs-none-says')!)).toBe('Nothing is waiting for your decision.');
    });

    it('counts nowhere: not on Needs you, not in the sidebar', async () => {
        await renderConsole(one(HELD));
        await waitFor(() => expect(held()).not.toBeNull());
        await waitFor(() => expect(chips()[0]).toBe('All 1'));
        expect(words(badge()!)).toBe('1');
        expect(badge()!.getAttribute('aria-label')).toBe('1 waiting');
    });

    it('adds no count to an empty Needs you', async () => {
        await renderConsole(empty(HELD));
        await waitFor(() => expect(held()).not.toBeNull());
        expect(badge()).toBeNull();
        expect(needs().querySelector('.needs-filter')).toBeNull();
    });

    it('lifts as the paired person, with the reason that it was lifted from Needs you', async () => {
        const fake = empty(HELD).answer('launches.lift', LIFT);
        await renderConsole(fake);
        await waitFor(() => expect(held()).not.toBeNull());
        fireEvent.click(lift());
        await waitFor(() => expect(sent(fake, 'launches.lift')).toHaveLength(1));
        expect(sent(fake, 'launches.lift')[0]).toEqual({ reason: NEEDS_LIFT_REASON });
        expect(fake.calls.find((each) => each.operation === 'launches.lift')!.kind).toBe('write');
        expect(fixtures[LIFT].request).toEqual({
            actor: { type: 'human', id: 'ada' },
            reason: 'Lifted from Needs you.',
        });
    });

    it('says after the lift what was let go and until when, through the reads that follow, and is gone once Needs you is shown anew', async () => {
        const journal = stubStream();
        const fake = empty(HELD, NONE).answer('launches.lift', LIFT);
        const desktop = await renderConsole(fake, journal);
        await waitFor(() => expect(held()).not.toBeNull());
        fireEvent.click(lift());
        await waitFor(() => expect(words(held()!)).toBe(LIFTED_WORDS));
        expect(within(held()!).queryByRole('button')).toBeNull();

        // The lift's own lines read the limits again: nothing is held, and the line still says what the lift did.
        const asked = sent(fake, 'launches.list').length;
        await line(journal, 'jrn_lift_1');
        await waitFor(() => expect(sent(fake, 'launches.list').length).toBe(asked + 1));
        await settle();
        expect(words(held()!)).toBe(LIFTED_WORDS);

        // Away and back: the line is what the last read says, and nothing is held.
        flush(() => desktop.command('navigate.campaigns'));
        await waitFor(() =>
            expect(document.querySelector<HTMLElement>('section[data-pane="needs"]')!.hidden).toBe(true),
        );
        showNeeds();
        await waitFor(() =>
            expect(document.querySelector<HTMLElement>('section[data-pane="needs"]')!.hidden).toBe(false),
        );
        expect(held()).toBeNull();
    });

    it('drops a read that began before the lift and answered after it: the lifted line stays, with no button', async () => {
        const journal = stubStream();
        const fake = empty(HELD).answer('launches.lift', LIFT);
        await renderConsole(fake, journal);
        await waitFor(() => expect(held()).not.toBeNull());

        // A read begins and is held open, still saying two are held.
        const release = fake.hold('launches.list');
        const asked = sent(fake, 'launches.list').length;
        await line(journal, 'jrn_lift_stale');
        await waitFor(() => expect(sent(fake, 'launches.list').length).toBe(asked + 1));

        fireEvent.click(lift());
        await waitFor(() => expect(words(held()!)).toBe(LIFTED_WORDS));

        // It answers after the lift, with items held: what it says is old and is not drawn.
        release();
        await settle();
        await settle();
        expect(words(held()!)).toBe(LIFTED_WORDS);
        expect(within(held()!).queryByRole('button')).toBeNull();
    });

    it('says a refusal in the runtime’s own words, and stays with its button', async () => {
        const fake = empty(HELD).answerAnyRequest('launches.lift', 'launches.lift/not-human');
        await renderConsole(fake);
        await waitFor(() => expect(held()).not.toBeNull());
        fireEvent.click(lift());
        const said = await waitFor(() => within(held()!).getByRole('alert'));
        const refusal = fixtures['launches.lift/not-human'].response as { error: { code: string; message: string } };
        expect(words(said)).toBe(`The limits were not lifted. ${refusal.error.code} ${refusal.error.message}`);
        expect(said.querySelector('code')!.textContent).toBe(refusal.error.code);
        expect(words(held()!).startsWith(HELD_WORDS)).toBe(true);
        expect((lift() as HTMLButtonElement).disabled).toBe(false);
    });

    it('says a lift the runtime never answered, and stays with its button', async () => {
        const fake = empty(HELD).answer('launches.lift', LIFT);
        fake.unreachable('launches.lift');
        await renderConsole(fake);
        await waitFor(() => expect(held()).not.toBeNull());
        fireEvent.click(lift());
        const said = await waitFor(() => within(held()!).getByRole('alert'));
        expect(words(said)).toBe('The limits were not lifted. Failed to fetch');
        expect(words(held()!).startsWith(HELD_WORDS)).toBe(true);
        expect((lift() as HTMLButtonElement).disabled).toBe(false);
    });

    it('is not drawn in the demo, which reads no limits and lifts nothing', async () => {
        const fake = empty(HELD).answer('launches.lift', LIFT);
        await renderConsole(fake, stubStream(), true);
        await settle();
        expect(mode.value).toBe('demo');
        expect(sent(fake, 'launches.list')).toHaveLength(0);
        expect(sent(fake, 'launches.lift')).toHaveLength(0);
        expect(document.querySelector('.needs-held')).toBeNull();
    });
});
