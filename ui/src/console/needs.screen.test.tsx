import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { whyWords } from '../screens/send-modes/doubts';
import { journalLine, stubDesktop, stubStream, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention, filter, reads } from './needs';
import { chats, lines } from './state';
import { forgetWaiting } from './waiting';

const hostile = '<img src=x onerror="window.hit=1">';

async function renderConsole(fake: FakeApi, journal: StubStream = stubStream()): Promise<void> {
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
    fake.answer('system.info', 'system.info/with-ui')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    render(<Console api={fake} desktop={stubDesktop()} stream={journal.stream} />);
    // Needs you is a page of the sidebar's menu, chosen as a person chooses it once the window has read what waits.
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
}

const needs = () => screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
const row = (id: string) => needs().querySelector<HTMLElement>(`[data-id="${id}"]`)!;
/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

beforeEach(() => {
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    reads.approvals.value = {};
    reads.unread.value = {};
    forgetWaiting();
    filter.value = 'all';
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('Needs you', () => {
    const show = (name: 'All' | 'Approvals' | 'Questions') =>
        fireEvent.click(within(needs()).getByRole('button', { name }));
    const pressed = () =>
        within(needs())
            .getAllByRole('button', { pressed: true })
            .map((each) => each.textContent?.trim());

    it('says so in a card when nothing is waiting', async () => {
        await renderConsole(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        expect(words(needs().querySelector('.card.needs-empty .bd')!)).toBe("You're all caught up");
        expect(needs().querySelector('.ask')).toBeNull();
    });

    it('offers the campaigns from the card, and its one control opens their panel', async () => {
        await renderConsole(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        const card = needs().querySelector<HTMLElement>('.card.needs-empty')!;
        expect(
            within(card)
                .getAllByRole('button')
                .map((each) => words(each)),
        ).toEqual(['Open your agents']);
        expect(screen.queryByRole('heading', { name: 'Agents', level: 1 })).toBeNull();

        fireEvent.click(within(card).getByRole('button', { name: 'Open your agents' }));
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Agents', level: 1 })).toBeTruthy());
    });

    it('filters to approvals or to questions, and says in a line of its own when a filter holds nothing', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        await renderConsole(fake);
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        expect(pressed()).toEqual(['All']);
        const asked = fake.calls.length;
        const hash = location.hash;

        show('Questions');
        await waitFor(() => expect(words(needs().querySelector('.needs-none')!)).toBe('No questions for you.'));
        expect(pressed()).toEqual(['Questions']);
        expect(needs().querySelector('[data-id="apr_1"]')).toBeNull();
        expect(needs().querySelector('.needs-empty')).toBeNull();

        show('Approvals');
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(needs().querySelector('.needs-none')).toBeNull();
        expect(fake.calls).toHaveLength(asked);
        expect(location.hash).toBe(hash);
    });

    it('shows everything again once nothing waited, so an approval that arrives then is drawn', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/messages-pending')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message'),
        );
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        const waiting = attention.approvals.value;
        show('Questions');
        await waitFor(() => expect(needs().querySelector('.needs-none')).toBeTruthy());

        attention.approvals.value = [];
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        attention.approvals.value = waiting;
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(needs().querySelector('.needs-none')).toBeNull();
        expect(pressed()).toEqual(['All']);
    });

    it('says no approval is waiting under Approvals while a question is open', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/empty')
                .answer('decision.list', 'decision.list/pending'),
        );
        await waitFor(() => expect(row('dec_1')).toBeTruthy());
        show('Approvals');
        await waitFor(() => expect(words(needs().querySelector('.needs-none')!)).toBe('No approvals waiting.'));
        expect(needs().querySelector('[data-id="dec_1"]')).toBeNull();
        show('All');
        await waitFor(() => expect(row('dec_1')).toBeTruthy());
    });

    it('says on each row who the message goes to, from what the row carries, before its whole read answers', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        const release = fake.hold('approval.get');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('to Ines Duarte');
        expect(words(row('apr_2').querySelector('.needs-who')!)).toBe('to Noor Haddad');
        // Each row is read whole once, for its Approve; the name never waits for that read.
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'approval.get')).toHaveLength(2));
        release();
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
    });

    it('names a row that carries no contact by the address its own preview highlights, once its details are open', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: null }));
        await waitFor(() => expect(needs().querySelector('.needs-who')).toBeNull());

        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_1')).getByText('Notes from the meetup'));
        expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('to ines@example.test');
        expect(row('apr_2').querySelector('.needs-who')).toBeNull();
        expect(needs().querySelectorAll('.needs-who')).toHaveLength(1);
    });

    it('names the person an approval that sends nothing is about, with no word before the name', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/pending')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending'),
        );
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('Ada Lovelace');
        expect(words(row('apr_2').querySelector('.needs-who')!)).toBe('Grace Hopper');
        expect(words(row('apr_1').querySelector('.ask-h')!)).toContain('Add to sequence Ada Lovelace');
        expect(words(row('apr_2').querySelector('.ask-h')!)).not.toContain('to Grace');
    });

    it("shows a message's sender and its channel in words beside the recipient, subject and body", async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());

        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_1')).getByText('Notes from the meetup'));
        const email = row('apr_1');
        expect(words(email.querySelector('.needs-from')!)).toBe('From owner@example.test');
        expect(words(email.querySelector('.needs-channel')!)).toBe('Channel Email');
        expect(within(email).getByText('To Ines Duarte, ines@example.test')).toBeTruthy();
        expect(within(email).getByText('Hi Ines, thanks for the talk at the meetup.')).toBeTruthy();

        fireEvent.click(within(row('apr_2')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_2')).getByText('Thanks for connecting, Noor.'));
        const linkedIn = row('apr_2');
        expect(words(linkedIn.querySelector('.needs-from')!)).toBe('From https://www.linkedin.com/in/owner-example');
        expect(words(linkedIn.querySelector('.needs-channel')!)).toBe('Channel LinkedIn');
    });

    it('shows nothing in the place of a sender or a channel the preview does not carry', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_1')).getByText('Put Ada Lovelace into the campaign at the provider.'));
        expect(row('apr_1').querySelector('.needs-from')).toBeNull();
        expect(row('apr_1').querySelector('.needs-channel')).toBeNull();
        expect(words(row('apr_1'))).not.toMatch(/From|Channel|unknown/);
    });

    it('draws a hostile sender, channel and recipient as text', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        const recorded = fixtures['approval.get/email-message'].response;
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: hostile }));
        attention.detail.value = {
            kind: 'approvals',
            item: {
                ...recorded,
                preview: {
                    ...recorded.preview,
                    highlights: {
                        ...recorded.preview.highlights,
                        '/args/sender': { external_id: 's_12', address: hostile },
                        '/args/channel': hostile,
                    },
                },
            },
        };
        await waitFor(() => expect(within(row('apr_1')).getAllByText(hostile).length).toBe(3));
        expect(needs().querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    describe('approves only what it has read whole', () => {
        const approve = (id: string) => within(row(id)).getByRole('button', { name: 'Approve' });
        const details = (id: string) => within(row(id)).getByRole('button', { name: 'Details' });
        const live = (button: HTMLElement) => !button.hasAttribute('disabled');
        const sent = (fake: FakeApi, operation: string) =>
            fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
        const reads = (fake: FakeApi, id: string) =>
            sent(fake, 'approval.get').filter((body) => (body as { approval_id: string }).approval_id === id);
        /** A journal line, as the runtime writes one when something waits: the console reads what waits again. */
        const line = (journal: StubStream, id: string) =>
            flush(() => {
                journal.open();
                journal.send(journalLine({ id, kind: 'approval_requested' }));
            });
        const messages = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/messages-pending')
                .answer('decision.list', 'decision.list/empty');

        it('holds Approve on a doubted message until the doubt is drawn on its row, above Approve', async () => {
            const fake = messages().answer(
                'approval.get',
                'approval.get/park-doubted',
                'approval.get/linkedin-message',
            );
            await renderConsole(fake);
            await waitFor(() => expect(row('apr_1')).toBeTruthy());
            const { doubts } = fixtures['approval.get/park-doubted'].response.preview as { doubts: { note: string }[] };
            const doubted = `The critic role doubted this: ${whyWords('unsupported_claim')}`;
            // Whenever Approve is live, the doubt already stands on the row.
            if (live(approve('apr_1'))) expect(within(row('apr_1')).queryByText(doubted)).not.toBeNull();
            const doubt = await waitFor(() => within(row('apr_1')).getByText(doubted));
            expect(within(row('apr_1')).getByText(`Their words: ${doubts[0]!.note}`)).toBeTruthy();
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            expect(doubt.compareDocumentPosition(approve('apr_1')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        });

        it('says why a message it could not read cannot be approved, keeps Details, and reads it again at the next reload', async () => {
            const journal = stubStream();
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const reachable = fake.unreachable(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake, journal);
            await waitFor(() => expect(row('apr_1')).toBeTruthy());
            const said = await waitFor(() =>
                within(row('apr_1')).getByText(/could not be read, so it cannot be approved here/),
            );
            expect(words(said)).toContain('Failed to fetch');
            expect(live(approve('apr_1'))).toBe(false);
            expect(live(details('apr_1'))).toBe(true);
            fireEvent.click(approve('apr_1'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);

            reachable();
            line(journal, 'jrn_301');
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true), { timeout: 3000 });
            expect(within(row('apr_1')).queryByText(/could not be read/)).toBeNull();
        });

        it('says a message is being read while its read is unanswered, keeps Details, and asks again at the next reload', async () => {
            const journal = stubStream();
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const release = fake.hold(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake, journal);
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            expect(words(row('apr_1'))).toContain('cannot be approved until it is shown here whole');
            expect(live(approve('apr_1'))).toBe(false);
            expect(live(details('apr_1'))).toBe(true);
            fireEvent.click(approve('apr_1'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);
            expect(reads(fake, 'apr_1')).toHaveLength(1);

            line(journal, 'jrn_302');
            await waitFor(() => expect(reads(fake, 'apr_1')).toHaveLength(2), { timeout: 3000 });
            release();
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
        });

        const enrolments = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/pending')
                .answer('decision.list', 'decision.list/empty');

        // The rule is the row's, not a list of kinds: whatever an approval does, Approve waits for its whole read.
        it('holds Approve on an enrolment it could not read, says why, keeps Details, and approves nothing', async () => {
            const fake = enrolments().answer('approval.get', 'approval.get/pending-with-preview');
            fake.unreachable('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            const said = await waitFor(() =>
                within(row('apr_2')).getByText(/could not be read, so it cannot be approved here/),
            );
            expect(words(said)).toContain('Details reads it again');
            expect(words(said)).toContain('Failed to fetch');
            expect(live(approve('apr_2'))).toBe(false);
            expect(live(details('apr_2'))).toBe(true);
            fireEvent.click(approve('apr_2'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);
        });

        it('holds Approve on an enrolment while its read is unanswered, and says so', async () => {
            const fake = enrolments().answer(
                'approval.get',
                'approval.get/pending-with-preview',
                'approval.get/other-pending',
            );
            const release = fake.hold(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_2',
            );
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            expect(words(row('apr_2'))).toContain('cannot be approved until it is shown here whole');
            expect(live(approve('apr_2'))).toBe(false);
            expect(live(details('apr_2'))).toBe(true);
            release();
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            expect(words(row('apr_2'))).not.toContain('cannot be approved');
        });

        it('draws the doubts of an enrolment a person was asked to look at, as it draws a message’s', async () => {
            const fake = enrolments().answer('approval.get', 'approval.get/with-doubts', 'approval.get/other-pending');
            await renderConsole(fake);
            const asked = await waitFor(() =>
                within(row('apr_1')).getByText(`A person asked you to look at this: ${whyWords('sensitive_topic')}`),
            );
            expect(within(row('apr_1')).getByText('Their words: She asks about a price.')).toBeTruthy();
            expect(live(approve('apr_1'))).toBe(true);
            expect(asked.compareDocumentPosition(approve('apr_1')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
            expect(row('apr_2').querySelector('.send-doubts')).toBeNull();
        });

        it('approves a message it has read through approval.approve, after checking it against its work item', async () => {
            const fake = messages()
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message')
                .answer('workitem.get', 'workitem.get/parked-send')
                .answer('approval.approve', 'approval.approve/approved');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            fake.answer('approval.list', 'approval.list/empty');
            fireEvent.click(approve('apr_1'));
            await waitFor(() => expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]));
            expect(sent(fake, 'workitem.get')).toEqual([{ work_item_id: 'wi_1' }]);
            await waitFor(() => expect(needs().querySelector('[data-id="apr_1"]')).toBeNull());
        });

        it('does not approve a message whose text moved since it was asked, and says so on its row', async () => {
            const fake = messages()
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message')
                .answer('workitem.get', 'workitem.get/parked-edited-send');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            fireEvent.click(approve('apr_1'));
            await waitFor(() => within(row('apr_1')).getByText(/changed after this approval was asked/));
            expect(sent(fake, 'approval.approve')).toEqual([]);
            expect(live(approve('apr_1'))).toBe(true);
        });

        it('approves a row whose own read failed once Details has read it whole', async () => {
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const reachable = fake.unreachable(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake);
            await waitFor(() => within(row('apr_1')).getByText(/could not be read/));
            reachable();
            fireEvent.click(details('apr_1'));
            await waitFor(() => within(row('apr_1')).getByText('Notes from the meetup'));
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            expect(within(row('apr_1')).queryByText(/could not be read/)).toBeNull();
        });
    });
});
