import { act as flush, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { ApiProvider } from '../../api/context';
import type { Approval, Campaign, WorkItem } from '../../api/types';
import { FakeApi } from '../../test/fake-api';
import { fixtures, type FixtureName } from '../../test/fixtures';
import { noteEdit } from '../attention/reasking';
import { ASKING_MARK } from '../attention/reasking-model';
import * as store from './store';
import { CampaignTimeline } from './timeline';

// A campaign's Sequence keeps a message edited in its approval panel where it stood under Waiting for you, as every
// list of approvals does, until the runtime asks again; then the row is the new approval. The approvals are one story
// the runtime recorded (`CardExchangesTests`); the timeline's other reads answer with recorded empty lists, no ids.

let stage: 'waiting' | 'saved' | 'asked' = 'waiting';
const byStage = (waiting: FixtureName, saved: FixtureName, asked: FixtureName): FixtureName =>
    stage === 'waiting' ? waiting : stage === 'saved' ? saved : asked;
const everyStatus = (body: object) => ((body as { statuses?: string[] }).statuses ?? []).length > 1;

function story(): FakeApi {
    const fake = new FakeApi()
        .answerAnyRequest('approval.list', (body) =>
            'work_item_id' in body
                ? everyStatus(body)
                    ? byStage(
                          'approval.list/card-message-item-all-after-edit',
                          'approval.list/card-message-item-all-after-edit',
                          'approval.list/card-message-item-all-reasked',
                      )
                    : byStage(
                          'approval.list/card-message-item-after-edit',
                          'approval.list/card-message-item-after-edit',
                          'approval.list/card-message-item-reasked',
                      )
                : byStage(
                      'approval.list/card-message-pending',
                      'approval.list/card-message-after-edit',
                      'approval.list/card-message-reasked',
                  ),
        )
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('workitem.list', 'workitem.list/empty')
        .answerAnyRequest('journal.list', 'journal.list/by-contact-empty');
    return fake.answerAnyRequest('workitem.get', () =>
        stage === 'asked' ? 'workitem.get/card-message-reparked' : 'workitem.get/card-message-requeued',
    );
}

const waitingRows = () => {
    const group = screen.getByText('Waiting for you').parentElement as HTMLElement;
    return within(group).getAllByRole('button');
};

beforeEach(() => {
    stage = 'waiting';
    store.pendingOf.value = null;
    store.pendingApprovals.value = [];
});

describe('a message edited from a campaign’s Sequence', () => {
    it('keeps its row under Waiting for you, marked, then the row is the approval asked again', async () => {
        const fake = story();
        render(
            <ApiProvider api={fake}>
                <CampaignTimeline
                    campaign={{ id: 'cmp_1' } as Campaign}
                    me="ada"
                    now={new Date('2026-10-15T12:05:00.000Z')}
                    zone="UTC"
                    onWaiting={() => {}}
                />
            </ApiProvider>,
        );
        await waitFor(() => expect(waitingRows()).toHaveLength(1));
        expect(store.pendingApprovals.value.map((row) => row.id)).toEqual(['apr_1']);
        expect(waitingRows()[0]!.textContent).toMatch(/waiting since/);

        // The panel saved the edit and reads the list again (`onSuperseded`): the runtime lists nothing now.
        flush(() =>
            noteEdit(
                fixtures['approval.get/card-message'].response as unknown as Approval,
                fixtures['workitem.update/card-message-edited'].response as unknown as WorkItem,
            ),
        );
        stage = 'saved';
        await flush(() => store.loadPending(fake, 'cmp_1'));
        expect(store.pendingApprovals.value.map((row) => row.id)).toEqual(['apr_1']);
        expect(waitingRows()).toHaveLength(1);
        expect(waitingRows()[0]!.textContent).toContain(ASKING_MARK);

        // The runtime asks again: the row is the new approval.
        stage = 'asked';
        await flush(() => store.loadPending(fake, 'cmp_1'));
        expect(store.pendingApprovals.value.map((row) => row.id)).toEqual(['apr_2']);
        expect(waitingRows()).toHaveLength(1);
        expect(waitingRows()[0]!.textContent).toMatch(/waiting since/);
        expect(waitingRows()[0]!.textContent).not.toContain(ASKING_MARK);
    });
});
