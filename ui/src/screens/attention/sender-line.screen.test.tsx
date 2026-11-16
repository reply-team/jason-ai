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
function renderPanel(fake: FakeApi): void {
    fake.answer('workitem.get', 'workitem.get/card-message-parked').answer('preference.list', 'preference.list/set');
    watch(fake);
    const approval = fixtures['approval.get/card-message'].response as unknown as Approval;
    render(
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
        </ApiProvider>,
    );
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
});
