import { act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { QUEUED_SENTENCE } from '../screens/attention/reasking-model';
import { noteAsked, noteEnded } from '../screens/attention/reasking';
import { chatDesktop, journalLine, stubStream, type StubStream } from '../test/desktop';
import { shown } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { fixtures, type FixtureName } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention, forgetNeeds } from './needs';
import { chats, conversations } from './state';
import { forgetWaiting } from './waiting';

// A message edited on its card in an agent's chat: the save withdraws the approval, and the card stays where it was,
// with the words saved and its state in words, until the runtime asks again; then the card is the new approval, in
// the same place, and Approve approves it. One story the runtime recorded (`CardExchangesTests`), nothing else.

const chat = 'conv_01JDKEPT0000000000000000W1';
const edited = 'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?';

/** Where the runtime stands in the story: the message waiting, its edit saved, or the message asked again. */
let stage: 'waiting' | 'saved' | 'asked' = 'waiting';

const byStage = (waiting: FixtureName, saved: FixtureName, asked: FixtureName): FixtureName =>
    stage === 'waiting' ? waiting : stage === 'saved' ? saved : asked;

/** A read of every approval of the item, in every status (where a kept row stands), or of its pending ones alone. */
const everyStatus = (body: object) => ((body as { statuses?: string[] }).statuses ?? []).length > 1;

function renderChat(journal: StubStream): FakeApi {
    const fake = new FakeApi()
        .answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('sender.list', 'sender.list/card-message-sender')
        .answer('approval.get', 'approval.get/card-message', 'approval.get/card-message-reasked')
        .answer('workitem.update', 'workitem.update/card-message-edited')
        .answer('approval.approve', 'approval.approve/card-message-reasked')
        .answerAnyRequest('approval.list', (body) =>
            'campaign_id' in body
                ? byStage(
                      'approval.list/card-message-pending',
                      'approval.list/card-message-after-edit',
                      'approval.list/card-message-reasked',
                  )
                : 'work_item_id' in body
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
                        'approval.list/card-message-all-pending',
                        'approval.list/card-message-all-after-edit',
                        'approval.list/card-message-all-reasked',
                    ),
        )
        // The story raised no question: its campaign's questions are none, as the recorded empty list says.
        .answerAnyRequest('decision.list', 'decision.list/empty');
    // The item is read at Edit, at Save before it is written, by a list that keeps the row, and at Approve; it is
    // queued again from the moment the edit is written.
    fake.answerAnyRequest('workitem.get', () =>
        stage === 'asked'
            ? 'workitem.get/card-message-reparked'
            : fake.calls.some((each) => each.operation === 'workitem.update')
              ? 'workitem.get/card-message-requeued'
              : 'workitem.get/card-message-parked',
    );
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
    const desktop = chatDesktop();
    desktop.keep({
        id: chat,
        campaignId: 'cmp_1',
        title: 'Spring',
        entries: [{ kind: 'user', text: 'How is it going?' }],
    });
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return fake;
}

async function openChat(): Promise<HTMLElement> {
    const side = () =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });
    await waitFor(() => {
        fireEvent.click(within(side()).getByRole('button', { name: 'Spring' }));
        expect(shown()).toBe(`chat:${chat}`);
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

const card = (pane: HTMLElement, id: string) => pane.querySelector<HTMLElement>(`.waiting-card[data-id="${id}"]`);
const cards = (pane: HTMLElement) =>
    [...pane.querySelectorAll<HTMLElement>('.waiting-card')].map((each) => each.dataset['id']);
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const sent = (fake: FakeApi, operation: string) =>
    fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
const withoutActor = (request: object) =>
    Object.fromEntries(Object.entries(request).filter(([key]) => key !== 'actor'));

/** The Approve of an approval's card once it is live: read whole, nothing under way. */
const liveApprove = (pane: HTMLElement, id: string) =>
    waitFor(() => {
        const button = within(card(pane, id)!).getByRole('button', { name: 'Approve' });
        expect(button.hasAttribute('disabled')).toBe(false);
        return button;
    });

beforeEach(() => {
    stage = 'waiting';
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    attention.detail.value = null;
    forgetWaiting();
    forgetNeeds();
});

describe('a message edited on its card in an agent’s chat', () => {
    it('stays where it was with the words saved, becomes the approval asked again in place, and Approve approves that', async () => {
        const journal = stubStream();
        const fake = renderChat(journal);
        const pane = await openChat();
        await liveApprove(pane, 'apr_1');
        const questionsFirst = cards(pane);
        expect(questionsFirst.at(-1)).toBe('apr_1');

        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Edit' }));
        fireEvent.change(await waitFor(() => within(card(pane, 'apr_1')!).getByRole('textbox', { name: 'Body' })), {
            target: { value: edited },
        });
        stage = 'saved';
        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(sent(fake, 'workitem.update')).toEqual([
                withoutActor(fixtures['workitem.update/card-message-edited'].request),
            ]),
        );

        // The runtime lists nothing for the campaign now; the card stays, in its place, and says where it stands.
        await waitFor(() => within(card(pane, 'apr_1')!).getByText(QUEUED_SENTENCE));
        expect(cards(pane)).toEqual(questionsFirst);
        expect(words(card(pane, 'apr_1')!)).toContain(edited);
        for (const name of ['Approve', 'Edit', 'Reject'])
            expect(within(card(pane, 'apr_1')!).queryByRole('button', { name })).toBeNull();
        expect(sent(fake, 'approval.approve')).toEqual([]);

        // The runtime asks again and says so in the journal: the card is the new approval, where the old one stood.
        stage = 'asked';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_edit_1', kind: 'approval_requested' }));
        });
        const approve = await liveApprove(pane, 'apr_2');
        expect(card(pane, 'apr_1')).toBeNull();
        expect(cards(pane)).toEqual([...questionsFirst.slice(0, -1), 'apr_2']);
        expect(within(card(pane, 'apr_2')!).queryByText(QUEUED_SENTENCE)).toBeNull();
        expect(words(card(pane, 'apr_2')!)).toContain(edited);

        fireEvent.click(approve);
        await waitFor(() =>
            expect(sent(fake, 'approval.approve')).toEqual([
                withoutActor(fixtures['approval.approve/card-message-reasked'].request),
            ]),
        );
    });

    /** Edits and saves the message on its card, and waits for the card to say where it stands. */
    async function editAndSave(fake: FakeApi, pane: HTMLElement): Promise<void> {
        await liveApprove(pane, 'apr_1');
        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Edit' }));
        fireEvent.change(await waitFor(() => within(card(pane, 'apr_1')!).getByRole('textbox', { name: 'Body' })), {
            target: { value: edited },
        });
        stage = 'saved';
        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(card(pane, 'apr_1')!).getByText(QUEUED_SENTENCE));
        expect(sent(fake, 'workitem.update')).toHaveLength(1);
    }

    it('offers no act on the withdrawn approval once another list on the page has it asked again, before this chat reads its own', async () => {
        const fake = renderChat(stubStream());
        const pane = await openChat();
        await editAndSave(fake, pane);
        // Needs you, say, read the approval asked again first; this chat has not read its lists since.
        flush(() => noteAsked('wi_1', 'apr_2'));
        await waitFor(() =>
            within(card(pane, 'apr_1')!).getByText(/this one was withdrawn by your edit and cannot be approved/),
        );
        expect(words(card(pane, 'apr_1')!)).toContain(edited);
        for (const name of ['Approve', 'Edit', 'Reject'])
            expect(within(card(pane, 'apr_1')!).queryByRole('button', { name })).toBeNull();
        expect(await attention.approve(fake, 'apr_1')).toBe(false);
        expect(sent(fake, 'approval.approve')).toEqual([]);
    });

    it('says the work ended and offers no act once its work ended another way', async () => {
        const fake = renderChat(stubStream());
        const pane = await openChat();
        await editAndSave(fake, pane);
        flush(() => noteEnded('wi_1', 'cancelled'));
        await waitFor(() =>
            within(card(pane, 'apr_1')!).getByText(
                'Your new text was saved, but this work ended before Jason asked again: it was cancelled. Nothing here can be approved.',
            ),
        );
        for (const name of ['Approve', 'Edit', 'Reject'])
            expect(within(card(pane, 'apr_1')!).queryByRole('button', { name })).toBeNull();
        expect(await attention.reject(fake, 'apr_1')).toBe(false);
        expect(sent(fake, 'approval.reject')).toEqual([]);
    });
});
