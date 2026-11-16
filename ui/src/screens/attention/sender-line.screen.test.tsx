import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ApiProvider } from '../../api/context';
import type { Approval } from '../../api/types';
import { FakeApi, watch } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { SENDER_ASKED, SENDER_WHEN_WORDS, senderWordings } from '../../test/senders';
import { ApprovalPanel } from './approval-panel';

// Needs you's panel draws, under the address a send goes from, the sending name the provider holds now for that
// mailbox: the same piece an agent's chat's card draws, with the same four wordings, none of which changes Approve or
// Reject.

/** Needs you's panel over the approval the runtime recorded, its message checked against its parked work item. */
const recorded = (name: 'approval.get/card-message' | 'approval.get/card-message-reasked') =>
    fixtures[name].response as unknown as Approval;

function panel(fake: FakeApi, approval: Approval) {
    return (
        <ApiProvider api={fake}>
            <ApprovalPanel
                approval={approval}
                now={new Date('2026-10-16T09:05:00.000Z')}
                zone="UTC"
                busy={false}
                remaining=""
                onApprove={() => undefined}
                onReject={() => undefined}
                onSkip={() => undefined}
                onClose={() => undefined}
            />
        </ApiProvider>
    );
}

function renderPanel(fake: FakeApi) {
    fake.answer('workitem.get', 'workitem.get/card-message-parked').answer('preference.list', 'preference.list/set');
    watch(fake);
    return render(panel(fake, recorded('approval.get/card-message')));
}

describe('the sender line on Needs you’s panel', () => {
    for (const wording of senderWordings)
        it(`says ${wording.name}, under From, and leaves Approve as it was`, async () => {
            const fake = new FakeApi().answer('sender.list', wording.answer);
            renderPanel(fake);
            const line = await screen.findByText(wording.line, { selector: '.sender-now', exact: false });
            const from = line.closest('dd')!;
            expect(from.previousElementSibling?.textContent).toBe('From');
            expect(from.firstChild?.textContent).toBe('owner@example.test');
            expect(line.querySelector('code')?.textContent ?? null).toBe(wording.code ?? null);
            expect(from.querySelector('.sender-when')?.textContent ?? null).toBe(
                wording.when ? SENDER_WHEN_WORDS : null,
            );
            expect(fake.calls.filter((each) => each.operation === 'sender.list').map((each) => each.body)).toEqual([
                SENDER_ASKED,
            ]);
            await waitFor(() =>
                expect(screen.getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false),
            );
            expect(screen.getByRole('button', { name: 'Reject' }).hasAttribute('disabled')).toBe(false);
        });

    it('holds the line height in the From while the accounts are read', async () => {
        const fake = new FakeApi().answer('sender.list', 'sender.list/card-message-sender');
        const release = fake.hold('sender.list');
        renderPanel(fake);
        const hold = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.sender-line.sender-hold');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(hold.getAttribute('aria-hidden')).toBe('true');
        expect(hold.querySelectorAll('.sender-now, .sender-when')).toHaveLength(2);
        expect(hold.closest('dd')?.previousElementSibling?.textContent).toBe('From');
        release();
        await screen.findByText('Sending name at Reply now: Owner Example', { selector: '.sender-now' });
        expect(document.querySelector('.sender-hold')).toBeNull();
    });

    it('reads the accounts again when the panel goes on to another item, and says what they are then', async () => {
        const fake = new FakeApi().answer(
            'sender.list',
            'sender.list/card-message-sender',
            'sender.list/card-sender-other',
        );
        const { rerender } = renderPanel(fake);
        await screen.findByText('Sending name at Reply now: Owner Example', { selector: '.sender-now' });
        rerender(panel(fake, recorded('approval.get/card-message-reasked')));
        await screen.findByText('Reply does not list this mailbox now.', { selector: '.sender-now' });
        expect(screen.queryByText('Sending name at Reply now: Owner Example')).toBeNull();
        expect(fake.calls.filter((each) => each.operation === 'sender.list')).toHaveLength(2);
    });
});
