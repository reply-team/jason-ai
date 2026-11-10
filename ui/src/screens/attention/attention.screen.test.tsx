import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';

function pendingFake() {
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('approval.get', 'approval.get/pending-with-preview')
        .answer('decision.get', 'decision.get/pending-with-options');
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
        expect(screen.getByText('Approvals')).toBeTruthy();
        expect(screen.getByText('Questions')).toBeTruthy();
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
            .answer('approval.get', 'approval.get/email-message');
        renderApp(fake);
        await waitFor(() => screen.getByText('message.send Ines Duarte'));
        fireEvent.click(screen.getByText('message.send Ines Duarte'));
        await waitFor(() => screen.getByText('owner@example.test'));
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
