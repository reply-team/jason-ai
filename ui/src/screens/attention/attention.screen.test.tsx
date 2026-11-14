import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';
import * as store from './store';

function pendingFake() {
    // An open card's reminder about name and tone reads the preferences; with both set it says nothing.
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('approval.get', 'approval.get/pending-with-preview')
        .answer('decision.get', 'decision.get/pending-with-options')
        .answer('preference.list', 'preference.list/set');
}

/** The fact a panel states under a term, or undefined where it states none. */
const fact = (term: string) =>
    Array.from(document.querySelectorAll('.facts dt')).find((dt) => dt.textContent === term)?.nextElementSibling
        ?.textContent;

describe('Attention', () => {
    it('says nothing needs you when both lists are empty', async () => {
        renderApp(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(screen.getByText(/nothing needs you/i)).toBeTruthy());
    });

    it('lists pending approvals and questions by name, in two groups', async () => {
        renderApp(pendingFake());
        await waitFor(() => expect(screen.getByText('Enroll Ada Lovelace')).toBeTruthy());
        expect(screen.getByText('Enroll Grace Hopper')).toBeTruthy();
        expect(screen.getAllByText('in Autumn outreach').length).toBeGreaterThanOrEqual(2);
        expect(screen.getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
        expect(Array.from(document.querySelectorAll('.rows .group'), (group) => group.textContent)).toEqual([
            'Approvals',
            'Questions',
        ]);
    });

    it('opens an approval and shows what will happen first', async () => {
        renderApp(pendingFake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() =>
            expect(screen.getByText('Put Ada Lovelace into the campaign at the provider.')).toBeTruthy(),
        );
        expect(route.value).toEqual({ screen: 'attention', kind: 'approvals', id: 'apr_1' });
        expect(screen.getByText(/Approval is required/)).toBeTruthy();
        expect(screen.getByRole('button', { name: 'Approve' })).toBeTruthy();
        expect(screen.getByRole('button', { name: 'Reject' })).toBeTruthy();
    });

    it('approves with the recorded request, reloads, and moves to the next pending item', async () => {
        // The screen reads the next pending item once it moves there; React runs that read before the test ends.
        const fake = pendingFake()
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            .answer('approval.approve', 'approval.approve/approved');
        renderApp(fake);
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
        fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

        await waitFor(() => expect(fake.sent('approval.approve')).toBeTruthy());
        const expected = fixtures['approval.approve/approved'].request;
        expect(fake.sent('approval.approve')).toEqual({ approval_id: expected.approval_id });
        await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'approval.list').length).toBe(2));
        await waitFor(() => expect(route.value).toEqual({ screen: 'attention', kind: 'approvals', id: 'apr_2' }));
    });

    it("shows a message's sender and its channel in words among the facts", async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message')
            .answer('preference.list', 'preference.list/set');
        // A message approval is checked against its work item; no recording holds a message item, so this row holds it.
        const call = fake.call.bind(fake);
        fake.call = async (operation, body, kind) =>
            operation === 'workitem.get'
                ? ({ context: { input: fixtures['approval.get/email-message'].response.subject.input.args } } as never)
                : call(operation, body, kind);
        renderApp(fake);
        await waitFor(() => screen.getByText('message.send Ines Duarte'));
        fireEvent.click(screen.getByText('message.send Ines Duarte'));
        await waitFor(() => screen.getAllByText('owner@example.test'));
        expect(fact('From')).toBe('owner@example.test');
        expect(fact('Channel')).toBe('Email');
    });

    it('shows no sender and no channel where the preview carries none', async () => {
        renderApp(pendingFake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByText('Put Ada Lovelace into the campaign at the provider.'));
        expect(fact('From')).toBeUndefined();
        expect(fact('Channel')).toBeUndefined();
    });

    it("shows the runtime's refusal and reloads when a decision is no longer pending", async () => {
        const fake = pendingFake().answer('approval.approve', 'approval.approve/already-decided');
        renderApp(fake);
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
        fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
        await waitFor(() =>
            expect(screen.getByRole('alert').textContent).toContain(
                fixtures['approval.approve/already-decided'].response.error.message,
            ),
        );
        await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'approval.list').length).toBe(2));
    });

    it('rejects through the one dialog, with a reason, as recorded', async () => {
        const fake = pendingFake()
            .answer('approval.get', 'approval.get/other-pending')
            .answer('approval.reject', 'approval.reject/rejected');
        renderApp(fake);
        await waitFor(() => screen.getByText('Enroll Grace Hopper'));
        fireEvent.click(screen.getByText('Enroll Grace Hopper'));
        await waitFor(() => screen.getByRole('button', { name: 'Reject' }));
        fireEvent.click(screen.getByRole('button', { name: 'Reject' }));
        const dialog = await screen.findByRole('dialog');
        fireEvent.input(within(dialog).getByLabelText(/reason/i), { target: { value: 'not this quarter' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Reject' }));
        await waitFor(() => expect(fake.sent('approval.reject')).toBeTruthy());
        const expected = fixtures['approval.reject/rejected'].request;
        expect(fake.sent('approval.reject')).toEqual({ approval_id: expected.approval_id, reason: expected.reason });
    });

    it('answers a question with an option and a reason, as recorded', async () => {
        const fake = pendingFake().answer('decision.answer', 'decision.answer/answered');
        // The screen reads the next pending question once it moves there, and React runs that read before the test
        // ends; nothing recorded it, so it is left waiting, as it was when this test ended before the read was made.
        fake.hold('decision.get', (body) => (body as { decision_id?: string }).decision_id === 'dec_2');
        renderApp(fake);
        await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        fireEvent.click(screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        await waitFor(() => screen.getByText('Pause until the news'));
        fireEvent.click(screen.getByText('Pause until the news'));
        fireEvent.input(screen.getByLabelText(/why/i), { target: { value: 'their CFO is leaving' } });
        fireEvent.click(screen.getByRole('button', { name: 'Answer' }));
        await waitFor(() => expect(fake.sent('decision.answer')).toBeTruthy());
        const expected = fixtures['decision.answer/answered'].request;
        expect(fake.sent('decision.answer')).toEqual({
            decision_id: expected.decision_id,
            answer: expected.answer,
            option: expected.option,
            reason: expected.reason,
        });
    });

    describe('the filter row', () => {
        const row = () => screen.getByRole('group', { name: 'Approvals or questions' });
        const show = (name: 'All' | 'Approvals' | 'Questions') =>
            fireEvent.click(within(row()).getByRole('button', { name: new RegExp(`^${name}( [0-9]+)?$`) }));
        const pressed = () =>
            within(row())
                .getAllByRole('button', { pressed: true })
                .map((each) => each.textContent?.replace(/\s+/g, ' ').trim());
        const status = () => within(document.querySelector('.list')!).getByRole('status');

        it('narrows the list to approvals or to questions, asking the runtime nothing and storing nothing', async () => {
            const fake = pendingFake();
            renderApp(fake);
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            expect(pressed()).toEqual(['✓ All 4']);
            expect(status().textContent).toBe('Shown: 4');
            const asked = fake.calls.length;
            const hash = location.hash;
            const stored = Object.keys(localStorage).sort();

            show('Questions');
            await waitFor(() => expect(screen.queryByText('Enroll Ada Lovelace')).toBeNull());
            expect(screen.getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
            expect(pressed()).toEqual(['✓ Questions 2']);
            expect(status().textContent).toBe('Shown: 2');

            show('Approvals');
            await waitFor(() => expect(screen.getByText('Enroll Ada Lovelace')).toBeTruthy());
            expect(screen.getByText('Enroll Grace Hopper')).toBeTruthy();
            expect(screen.queryByText('Pause outreach to Vireo Labs until their funding news?')).toBeNull();
            expect(status().textContent).toBe('Shown: 2');

            show('All');
            await waitFor(() => expect(screen.getByText('Enroll Ada Lovelace')).toBeTruthy());
            expect(screen.getByText('Pause outreach to Vireo Labs until their funding news?')).toBeTruthy();
            expect(fake.calls).toHaveLength(asked);
            expect(location.hash).toBe(hash);
            expect(Object.keys(localStorage).sort()).toEqual(stored);
        });

        it('says in a line of its own when the kind chosen holds nothing', async () => {
            renderApp(
                new FakeApi()
                    .answer('approval.list', 'approval.list/pending')
                    .answer('decision.list', 'decision.list/empty'),
            );
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            show('Questions');
            await waitFor(() => expect(screen.getByText('No questions for you.')).toBeTruthy());
            expect(screen.queryByText('Enroll Ada Lovelace')).toBeNull();
            expect(screen.queryByText(/nothing needs you/i)).toBeNull();
            expect(status().textContent).toBe('Shown: 0');
        });

        it('counts each kind beside its option, so questions are seen to wait under an empty Approvals', async () => {
            renderApp(
                new FakeApi()
                    .answer('approval.list', 'approval.list/empty')
                    .answer('decision.list', 'decision.list/pending'),
            );
            await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
            const questions = store.questions.value.length;
            expect(questions).toBe(2);
            show('Approvals');
            await waitFor(() => expect(screen.getByText('No approvals waiting.')).toBeTruthy());
            const labels = within(row())
                .getAllByRole('button')
                .map((each) => each.textContent?.replace(/\s+/g, ' ').trim());
            expect(labels).toEqual(['All 2', '✓ Approvals 0', 'Questions 2']);
        });

        it('says beside an option that its count is of what is loaded where more pages wait', async () => {
            renderApp(pendingFake());
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            const labels = () =>
                within(row())
                    .getAllByRole('button')
                    .map((each) => each.textContent?.replace(/\s+/g, ' ').trim());
            expect(labels()).toEqual(['✓ All 4', 'Approvals 2', 'Questions 2']);

            // More approvals wait at the runtime: the kind and All say their counts are of what is loaded; Questions does not.
            store.approvalsCursor.value = 'next-page';
            await waitFor(() => expect(labels()).toEqual(['✓ All 4 loaded', 'Approvals 2 loaded', 'Questions 2']));

            store.questionsCursor.value = 'next-page';
            await waitFor(() =>
                expect(labels()).toEqual(['✓ All 4 loaded', 'Approvals 2 loaded', 'Questions 2 loaded']),
            );
        });

        it('shows everything again once nothing was listed, so an approval that arrives then is shown', async () => {
            renderApp(
                new FakeApi()
                    .answer('approval.list', 'approval.list/pending')
                    .answer('decision.list', 'decision.list/empty'),
            );
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            const listed = store.approvals.value;
            show('Questions');
            await waitFor(() => expect(screen.getByText('No questions for you.')).toBeTruthy());

            store.approvals.value = [];
            await waitFor(() => expect(screen.getByText(/nothing needs you/i)).toBeTruthy());
            store.approvals.value = listed;
            await waitFor(() => expect(screen.getByText('Enroll Ada Lovelace')).toBeTruthy());
            expect(pressed()).toEqual(['✓ All 2']);
        });

        it('is not drawn when nothing waits at all', async () => {
            renderApp(
                new FakeApi()
                    .answer('approval.list', 'approval.list/empty')
                    .answer('decision.list', 'decision.list/empty'),
            );
            await waitFor(() => expect(screen.getByText(/nothing needs you/i)).toBeTruthy());
            expect(screen.queryByRole('group', { name: 'Approvals or questions' })).toBeNull();
        });

        it('keeps the open item’s panel under its own kind and under All, and closes it under the other kind', async () => {
            const fake = pendingFake();
            renderApp(fake);
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
            await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
            const opened = route.value;

            show('Approvals');
            await waitFor(() => expect(pressed()).toEqual(['✓ Approvals 2']));
            expect(screen.getByRole('button', { name: 'Approve' })).toBeTruthy();
            expect(route.value).toEqual(opened);

            show('All');
            await waitFor(() => expect(pressed()).toEqual(['✓ All 4']));
            expect(screen.getByRole('button', { name: 'Approve' })).toBeTruthy();
            expect(route.value).toEqual(opened);

            show('Questions');
            await waitFor(() => expect(screen.queryByRole('button', { name: 'Approve' })).toBeNull());
            expect(route.value).toEqual({ screen: 'attention' });
            expect(pressed()).toEqual(['✓ Questions 2']);
        });

        it('counts in the open panel’s “more” only the kind it shows', async () => {
            renderApp(pendingFake());
            await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
            fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
            await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
            const more = () => document.querySelector('.panel .next')?.textContent;
            expect(more()).toBe('1 more approval and 2 questions after this one');

            show('Approvals');
            await waitFor(() => expect(more()).toBe('1 more approval after this one'));

            // The last approval listed: the questions still waiting are not this kind's, and nothing claims none waits.
            store.approvals.value = store.approvals.value.filter((each) => each.id === 'apr_1');
            await waitFor(() => expect(more()).toBe('No more approvals after this one'));
        });

        it('moves, after a decision, to the next item of the kind it shows', async () => {
            const fake = pendingFake()
                .answer('approval.get', 'approval.get/other-pending', 'approval.get/pending-with-preview')
                .answer('approval.reject', 'approval.reject/rejected');
            renderApp(fake);
            await waitFor(() => screen.getByText('Enroll Grace Hopper'));
            show('Approvals');
            fireEvent.click(screen.getByText('Enroll Grace Hopper'));
            await waitFor(() => screen.getByRole('button', { name: 'Reject' }));
            fireEvent.click(screen.getByRole('button', { name: 'Reject' }));
            const dialog = await screen.findByRole('dialog');
            fireEvent.input(within(dialog).getByLabelText(/reason/i), { target: { value: 'not this quarter' } });
            fireEvent.click(within(dialog).getByRole('button', { name: 'Reject' }));
            await waitFor(() => expect(fake.sent('approval.reject')).toBeTruthy());
            await waitFor(() => expect(fake.calls.filter((c) => c.operation === 'approval.list').length).toBe(2));
            await waitFor(() => expect(route.value).toEqual({ screen: 'attention', kind: 'approvals', id: 'apr_1' }));
        });
    });

    it('switches the filter to decided ones, keeps it in the address, and sends the statuses', async () => {
        const fake = pendingFake()
            .answer('approval.list', 'approval.list/pending', 'approval.list/decided')
            .answer('decision.list', 'decision.list/pending', 'decision.list/decided');
        renderApp(fake);
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByRole('button', { name: 'Decided' }));
        expect(route.value).toEqual({ screen: 'attention', decided: true });
        await waitFor(() =>
            expect(fake.sent('approval.list')).toEqual({
                statuses: ['approved', 'rejected', 'superseded', 'cancelled'],
            }),
        );
        await waitFor(() => expect(screen.getByText(/decided by ada/i)).toBeTruthy());
        expect(screen.getByText('superseded')).toBeTruthy();
    });
});
