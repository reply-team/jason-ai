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
    /** The pending approvals of the item as the runtime lists them; a test adds the new one when it arrives. */
    pending: string[] = ['apr_1'];
    constructor(public input: Record<string, unknown>) {}
    async call<T>(operation: string, body: object = {}): Promise<T> {
        this.calls.push({ operation, body: body as Record<string, unknown> });
        if (operation === 'workitem.get') return this.item() as T;
        if (operation === 'approval.list') return { items: this.pending.map((id) => ({ id })), next_cursor: null } as T;
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

function mount(
    held: Held,
    approval: Approval,
    onApprove = vi.fn(),
    onReasked = vi.fn(),
    reaskPoll?: { tries?: number; delayMs?: number },
) {
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
                onReasked={onReasked}
                reaskPoll={reaskPoll}
            />
        </ApiProvider>,
    );
    return onApprove;
}

const reply = (): Approval => {
    const a = email();
    a.operation = 'conversation.reply';
    a.subject = {
        input: { args: { channel: 'email', thread: { external_id: 't_1' }, body: 'Thanks, Tuesday works.' } },
    };
    a.preview = {
        highlights: { '/args/channel': 'email', '/args/body': 'Thanks, Tuesday works.' },
        contact: { name: 'Ines Duarte', value: 'ines@example.test' },
    };
    return a;
};

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

    it('is not offered when anything but the text changed: the send time, or a reply thread', async () => {
        const approval = email();
        const input = structuredClone(argsOf(approval));
        input['send_after'] = '2026-12-01T09:00:00+00:00';
        const view = render(
            <ApiProvider api={new Held(input)}>
                <ApprovalPanel
                    approval={approval}
                    now={new Date()}
                    zone="UTC"
                    busy={false}
                    remaining=""
                    onApprove={() => {}}
                    onReject={() => {}}
                    onSkip={() => {}}
                    onClose={() => {}}
                />
            </ApiProvider>,
        );
        await waitFor(() => expect(screen.getByText(stale)).toBeTruthy());
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
        view.unmount();
        const answer = reply();
        const moved = structuredClone(argsOf(answer));
        moved['thread'] = { external_id: 't_2' };
        mount(new Held(moved), answer);
        await waitFor(() => expect(screen.getByText(stale)).toBeTruthy());
        expect(approveButton()?.hasAttribute('disabled')).toBe(true);
    });

    it('keeps Approve for a reply nobody touched', async () => {
        const answer = reply();
        mount(new Held(structuredClone(argsOf(answer))), answer);
        await waitFor(() => expect(approveButton()?.hasAttribute('disabled')).toBe(false));
        expect(screen.queryByText(stale)).toBeNull();
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
        await waitFor(() =>
            expect(screen.getByText('Jason asks again with the new text when it next takes this work.')).toBeTruthy(),
        );
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
        expect(approveButton()).toBeNull();
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
        expect(screen.queryByText('Jason asks again with the new text when it next takes this work.')).toBeNull();
    });

    it('refuses to write over text that changed while the editor was open, and keeps what was typed', async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        mount(held, approval);
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'What the person typed' } });
        (held.input['message'] as Record<string, unknown>)['body'] = 'What a chat wrote meanwhile';
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(screen.getByText('The text changed while you were editing; here is the new text.')).toBeTruthy(),
        );
        expect(held.calls.some((c) => c.operation === 'workitem.update')).toBe(false);
        expect(screen.getAllByText('What a chat wrote meanwhile').length).toBeGreaterThan(0);
        expect((screen.getByLabelText('Body') as HTMLTextAreaElement).value).toBe('What the person typed');
        // Save is still there, and now writes, because the editor has seen the new text.
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(held.calls.some((c) => c.operation === 'workitem.update')).toBe(true));
        expect(
            (
                held.calls.find((c) => c.operation === 'workitem.update')!.body as {
                    set: { input: { message: { body: string } } };
                }
            ).set.input.message.body,
        ).toBe('What the person typed');
    });

    it('removes the old card after Save, and keeps the sentence when the new approval never comes', async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        const onReasked = vi.fn();
        mount(held, approval, vi.fn(), onReasked, { tries: 2, delayMs: 0 });
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => screen.getByText('Jason asks again with the new text when it next takes this work.'));
        expect(approveButton()).toBeNull();
        expect(screen.queryByRole('button', { name: 'Reject' })).toBeNull();
        expect(screen.queryByText('The message as written')).toBeNull();
        await waitFor(() => expect(held.calls.filter((c) => c.operation === 'approval.list')).toHaveLength(2));
        await new Promise((resolve) => setTimeout(resolve, 20));
        expect(onReasked).not.toHaveBeenCalled();
        expect(screen.getByText('Jason asks again with the new text when it next takes this work.')).toBeTruthy();
    });

    it('hands on the new approval once the runtime has asked again, and not before', async () => {
        const approval = email();
        const held = new Held(structuredClone(argsOf(approval)));
        const onReasked = vi.fn();
        mount(held, approval, vi.fn(), onReasked);
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'New body' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() => screen.getByText('Jason asks again with the new text when it next takes this work.'));
        expect(onReasked).not.toHaveBeenCalled();
        held.pending = ['apr_9'];
        await waitFor(() => expect(onReasked).toHaveBeenCalledWith('apr_9'), { timeout: 3000 });
    });

    it('edits a reply by its body alone, with the thread and every other argument put back', async () => {
        const approval = reply();
        const input = structuredClone(argsOf(approval));
        input['reply_to_message'] = { external_id: 'm_1' };
        const held = new Held(input);
        mount(held, approval);
        await waitFor(() => screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        expect(screen.queryByLabelText('Subject')).toBeNull();
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'Thursday is better.' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(screen.getByText('Jason asks again with the new text when it next takes this work.')).toBeTruthy(),
        );
        expect(held.calls.find((c) => c.operation === 'workitem.update')!.body).toEqual({
            work_item_id: 'wi_1',
            set: { input: { ...input, body: 'Thursday is better.' } },
            reason: 'Message text edited on the approval card.',
        });
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
