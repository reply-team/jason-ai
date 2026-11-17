import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { renderApp } from '../../test/render-app';
import { route } from '../../router';
import { ASKING_MARK, ASKING_SENTENCE } from './reasking-model';

// The browser's Attention: a message edited in its panel stays in the list where it was, chosen, with the words saved
// and its state in words, until the runtime asks again; then the row is the new approval, in the same place and still
// chosen, and Approve approves it. One story the runtime recorded (`CardExchangesTests`), nothing else.

const edited = 'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?';

/** Where the runtime stands in the story: the message waiting, its edit saved, or the message asked again. */
let stage: 'waiting' | 'saved' | 'asked' = 'waiting';
const byStage = (waiting: FixtureName, saved: FixtureName, asked: FixtureName): FixtureName =>
    stage === 'waiting' ? waiting : stage === 'saved' ? saved : asked;

function story(): FakeApi {
    const fake = new FakeApi()
        .answer('decision.list', 'decision.list/empty')
        .answer('preference.list', 'preference.list/set')
        .answer('approval.get', 'approval.get/card-message', 'approval.get/card-message-reasked')
        .answer('workitem.update', 'workitem.update/card-message-edited')
        .answer('approval.approve', 'approval.approve/card-message-reasked')
        .answerAnyRequest('sender.list', 'sender.list/card-message-sender')
        .answerAnyRequest('approval.list', (body) =>
            'work_item_id' in body
                ? byStage(
                      'approval.list/card-message-item-after-edit',
                      'approval.list/card-message-item-after-edit',
                      'approval.list/card-message-item-reasked',
                  )
                : byStage(
                      'approval.list/card-message-all-pending',
                      'approval.list/card-message-all-after-edit',
                      'approval.list/card-message-all-reasked',
                  ),
        );
    // The item is read before Approve is offered, at Save before it is written, by the list that keeps the row, and at
    // Approve; it is queued again from the moment the edit is written.
    return fake.answerAnyRequest('workitem.get', () =>
        stage === 'asked'
            ? 'workitem.get/card-message-reparked'
            : fake.calls.some((each) => each.operation === 'workitem.update')
              ? 'workitem.get/card-message-requeued'
              : 'workitem.get/card-message-parked',
    );
}

const rows = () => [...document.querySelectorAll<HTMLElement>('.rows a.row')];
const sent = (fake: FakeApi, operation: string) =>
    fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
const withoutActor = (request: object) =>
    Object.fromEntries(Object.entries(request).filter(([key]) => key !== 'actor'));
const liveApprove = () =>
    waitFor(() => {
        const button = screen.getByRole('button', { name: 'Approve' });
        expect(button.hasAttribute('disabled')).toBe(false);
        return button;
    });

beforeEach(() => {
    stage = 'waiting';
});

describe('a message edited on Attention', () => {
    it('stays in the list, chosen, with the words saved, then becomes the approval asked again in place, which Approve approves', async () => {
        const fake = story();
        renderApp(fake);
        await waitFor(() => expect(rows()).toHaveLength(1));
        fireEvent.click(rows()[0]!);
        await liveApprove();

        fireEvent.click(screen.getByRole('button', { name: 'Edit message' }));
        fireEvent.change(screen.getByLabelText('Body'), { target: { value: edited } });
        stage = 'saved';
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(sent(fake, 'workitem.update')).toEqual([
                withoutActor(fixtures['workitem.update/card-message-edited'].request),
            ]),
        );

        // The runtime lists nothing now; the row stays, chosen, says where it stands, and the panel shows the words saved.
        await waitFor(() => screen.getByText(ASKING_SENTENCE));
        await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThanOrEqual(2));
        expect(rows()).toHaveLength(1);
        expect(rows()[0]!.classList.contains('selected')).toBe(true);
        expect(within(rows()[0]!).getByText(ASKING_MARK)).toBeTruthy();
        expect(route.value).toEqual({ screen: 'attention', kind: 'approvals', id: 'apr_1' });
        expect(screen.getByText(edited)).toBeTruthy();
        for (const name of ['Approve', 'Reject', 'Edit message'])
            expect(screen.queryByRole('button', { name })).toBeNull();
        // The list's own count does not fall because of the edit.
        expect(document.querySelector('.shown')!.textContent).toBe('Shown: 1');

        // The runtime asks again: the row is the new approval, still chosen, and Approve approves it.
        stage = 'asked';
        await waitFor(() => expect(route.value).toEqual({ screen: 'attention', kind: 'approvals', id: 'apr_2' }), {
            timeout: 5000,
        });
        await waitFor(() => expect(screen.queryByText(ASKING_SENTENCE)).toBeNull());
        expect(rows()).toHaveLength(1);
        expect(rows()[0]!.classList.contains('selected')).toBe(true);
        expect(within(rows()[0]!).queryByText(ASKING_MARK)).toBeNull();
        expect(screen.getByText(edited)).toBeTruthy();
        fireEvent.click(await liveApprove());
        await waitFor(() =>
            expect(sent(fake, 'approval.approve')).toEqual([
                withoutActor(fixtures['approval.approve/card-message-reasked'].request),
            ]),
        );
    });
});
