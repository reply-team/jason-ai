import { act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { QUEUED_SENTENCE } from '../screens/attention/reasking-model';
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

// A reply drafted with markup before the update, waiting on its card in an agent's chat: Approve is refused with the
// document's own reason and the card stays as it was, editable; the text edited without the tags withdraws the question,
// and once the runtime asks again, Approve approves. One story the runtime recorded (`CardExchangesTests`), nothing else.

const chat = 'conv_01JDKEPT0000000000000000W1';
const drafted = 'Thanks Kai,<br><br>Thursday at ten works for me.';
const plain = 'Thanks Kai,\n\nThursday at ten works for me.';

/** Where the runtime stands in the story: the reply waiting, its edit saved, or the reply asked again. */
let stage: 'waiting' | 'saved' | 'asked' = 'waiting';

const byStage = (waiting: FixtureName, saved: FixtureName, asked: FixtureName): FixtureName =>
    stage === 'waiting' ? waiting : stage === 'saved' ? saved : asked;

/** A read of every approval of the item, in every status, or of its pending ones alone. */
const everyStatus = (body: object) => ((body as { statuses?: string[] }).statuses ?? []).length > 1;

function renderChat(journal: StubStream): FakeApi {
    const fake = new FakeApi()
        .answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('approval.get', 'approval.get/card-reply-marked', 'approval.get/card-reply-marked-reasked')
        .answer('workitem.update', 'workitem.update/card-reply-marked-edited')
        .answer(
            'approval.approve',
            'approval.approve/card-reply-marked-refused',
            'approval.approve/card-reply-marked-reasked',
        )
        .answerAnyRequest('approval.list', (body) =>
            'campaign_id' in body
                ? byStage(
                      'approval.list/card-reply-marked-pending',
                      'approval.list/card-reply-marked-after-edit',
                      'approval.list/card-reply-marked-reasked',
                  )
                : 'work_item_id' in body
                  ? everyStatus(body)
                      ? byStage(
                            'approval.list/card-reply-marked-item-all-after-edit',
                            'approval.list/card-reply-marked-item-all-after-edit',
                            'approval.list/card-reply-marked-item-all-reasked',
                        )
                      : byStage(
                            'approval.list/card-reply-marked-item-after-edit',
                            'approval.list/card-reply-marked-item-after-edit',
                            'approval.list/card-reply-marked-item-reasked',
                        )
                  : byStage(
                        'approval.list/card-reply-marked-all-pending',
                        'approval.list/card-reply-marked-all-after-edit',
                        'approval.list/card-reply-marked-all-reasked',
                    ),
        )
        .answerAnyRequest('decision.list', 'decision.list/empty');
    // The item is read at Approve, at Edit, at Save before it is written and by a list that keeps the row; it is queued
    // again from the moment the edit is written.
    fake.answerAnyRequest('workitem.get', () =>
        stage === 'asked'
            ? 'workitem.get/card-reply-marked-reparked'
            : fake.calls.some((each) => each.operation === 'workitem.update')
              ? 'workitem.get/card-reply-marked-requeued'
              : 'workitem.get/card-reply-marked-parked',
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
const squeeze = (text: string) => text.replace(/\s+/g, ' ').trim();
const words = (element: Element) => squeeze(element.textContent ?? '');
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

describe('a reply drafted with markup before the update, on its card in an agent’s chat', () => {
    it('is refused its yes with the reason and stays editable; edited without the tags and asked again, Approve approves', async () => {
        const journal = stubStream();
        const fake = renderChat(journal);
        const pane = await openChat();
        fireEvent.click(await liveApprove(pane, 'apr_1'));

        // The runtime's refusal, with its code, its summary and the document's own reason after it.
        const refused = fixtures['approval.approve/card-reply-marked-refused'].response.error;
        const alert = await waitFor(() =>
            within(pane.querySelector<HTMLElement>('.chat-waiting-for')!).getByRole('alert'),
        );
        expect(words(alert)).toBe(squeeze(`${refused.code} ${refused.message} ${refused.details[0]!.message}`));
        expect(words(alert)).toContain('never a tag and never an entity');

        // The question still waits: the card stays, with the drafted text, and can be edited.
        await liveApprove(pane, 'apr_1');
        expect(words(card(pane, 'apr_1')!)).toContain(drafted);
        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Edit' }));
        fireEvent.change(await waitFor(() => within(card(pane, 'apr_1')!).getByRole('textbox', { name: 'Body' })), {
            target: { value: plain },
        });
        stage = 'saved';
        fireEvent.click(within(card(pane, 'apr_1')!).getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(sent(fake, 'workitem.update')).toEqual([
                withoutActor(fixtures['workitem.update/card-reply-marked-edited'].request),
            ]),
        );
        await waitFor(() => within(card(pane, 'apr_1')!).getByText(QUEUED_SENTENCE));

        // The runtime asks again about the plain text, and the yes goes through.
        stage = 'asked';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_drafted_1', kind: 'approval_requested' }));
        });
        fireEvent.click(await liveApprove(pane, 'apr_2'));
        await waitFor(() =>
            expect(sent(fake, 'approval.approve')).toEqual([
                withoutActor(fixtures['approval.approve/card-reply-marked-refused'].request),
                withoutActor(fixtures['approval.approve/card-reply-marked-reasked'].request),
            ]),
        );
    });
});
