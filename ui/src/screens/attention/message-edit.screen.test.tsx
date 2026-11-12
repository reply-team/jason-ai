import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError, type Api } from '../../api/client';
import { ApiProvider } from '../../api/context';
import type { Approval, WorkItem } from '../../api/types';
import { fixtures } from '../../test/fixtures';
import { ApprovalPanel } from './approval-panel';

/** An Api whose work item is held here, so a test can change it from outside between a render and a press. */
class Held implements Api {
    readonly calls: { operation: string; body: Record<string, unknown> }[] = [];
    updateFails: ApiError | null = null;
    constructor(public input: Record<string, unknown>) {}
    async call<T>(operation: string, body: object = {}): Promise<T> {
        this.calls.push({ operation, body: body as Record<string, unknown> });
        if (operation === 'workitem.get') return this.item() as T;
        if (operation === 'workitem.update') {
            if (this.updateFails) throw this.updateFails;
            this.input = (body as { set: { input: Record<string, unknown> } }).set.input;
            return this.item() as T;
        }
        throw new Error(`unexpected ${operation}`);
    }
    item(): WorkItem {
        return { id: 'wi_1', context: { input: structuredClone(this.input) } } as unknown as WorkItem;
    }
}

const email = () => structuredClone(fixtures['approval.get/email-message'].response) as unknown as Approval;
const argsOf = (a: Approval) => (a.subject['input'] as { args: Record<string, unknown> }).args;

function mount(held: Held, approval: Approval, onApprove = vi.fn()) {
    render(
        <ApiProvider api={held}>
            <ApprovalPanel
                approval={approval}
                now={new Date('2026-10-15T12:05:00.000Z')}
                zone="UTC"
                busy={false}
                remaining="1 more"
                onApprove={onApprove}
                onReject={() => {}}
                onSkip={() => {}}
                onClose={() => {}}
            />
        </ApiProvider>,
    );
    return onApprove;
}

const approveButton = () => screen.queryByRole('button', { name: 'Approve' });
const stale = /changed after this approval was asked/;

describe('approving a message', () => {
    it('is offered once the work item still holds the text that was asked about, and approves', async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        const onApprove = mount(held, approval);
        await waitFor(() => expect(approveButton()?.hasAttribute('disabled')).toBe(false));
        fireEvent.click(approveButton()!);
        await waitFor(() => expect(onApprove).toHaveBeenCalledTimes(1));
        expect(screen.queryByText(stale)).toBeNull();
    });

    it('is not offered when the text was changed from outside before the card was drawn', async () => {
        const approval = email();
        const input = structuredClone(argsOf(approval));
        (input['message'] as Record<string, unknown>)['body'] = 'A chat changed this.';
        mount(new Held(input), approval);
        await waitFor(() => expect(screen.getByText(stale)).toBeTruthy());
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
    });

    it('is refused at the press when the text was changed between the render and the press', async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        const onApprove = mount(held, approval);
        await waitFor(() => expect(approveButton()?.hasAttribute('disabled')).toBe(false));
        (held.input['message'] as Record<string, unknown>)['subject'] = 'Changed meanwhile';
        fireEvent.click(approveButton()!);
        await waitFor(() => expect(screen.getByText(stale)).toBeTruthy());
        expect(onApprove).not.toHaveBeenCalled();
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
    });

    it('is withheld when the work item cannot be read', async () => {
        const approval = email();
        const held = new Held({});
        held.call = async () => {
            throw new ApiError('not_found', 'No such work item.', false, 404);
        };
        mount(held, approval);
        await waitFor(() => expect(screen.getByText(/No such work item\./)).toBeTruthy());
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
    });
});

describe('editing a message before it is approved', () => {
    it('saves only the subject and the body by workitem.update, approves nothing, and says Jason asks again', async () => {
        const approval = email();
        const input = structuredClone(argsOf(approval));
        input['send_after'] = '2026-12-01T09:00:00+00:00';
        const held = new Held(input);
        const onApprove = mount(held, approval);
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'New subject' } });
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'New body\nline two' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(screen.getByText('Jason asks again with the new text.')).toBeTruthy());
        const update = held.calls.find((c) => c.operation === 'workitem.update')!;
        expect(update.body).toEqual({
            work_item_id: 'wi_1',
            set: {
                input: {
                    ...input,
                    message: { ...(input['message'] as object), subject: 'New subject', body: 'New body\nline two' },
                },
            },
            reason: 'Message text edited on the approval card.',
        });
        expect(held.calls.filter((c) => c.operation === 'workitem.update')).toHaveLength(1);
        expect(onApprove).not.toHaveBeenCalled();
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
    });

    it("shows the runtime's sentence for a refusal and keeps the edit in the box", async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        held.updateFails = new ApiError('validation_failed', 'The subject must be one line.', false, 422);
        mount(held, approval);
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'Typed but refused' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(screen.getByText('The subject must be one line.')).toBeTruthy());
        expect((screen.getByLabelText('Body') as HTMLTextAreaElement).value).toBe('Typed but refused');
        expect(screen.queryByText('Jason asks again with the new text.')).toBeNull();
    });

    it('is not offered for a template', async () => {
        const approval = email();
        (approval.subject['input'] as { args: { message: Record<string, unknown> } }).args.message['compose'] =
            'template';
        const held = new Held(structuredClone(argsOf(approval)));
        mount(held, approval);
        await waitFor(() => expect(approveButton()?.hasAttribute('disabled')).toBe(false));
        expect(screen.queryByRole('button', { name: 'Edit message' })).toBeNull();
    });

    it('is not offered for an approval already decided', async () => {
        const approval = email();
        approval.status = 'approved';
        const held = new Held(structuredClone(argsOf(approval)));
        mount(held, approval);
        expect(screen.queryByRole('button', { name: 'Edit message' })).toBeNull();
        expect(held.calls).toHaveLength(0);
    });
});
