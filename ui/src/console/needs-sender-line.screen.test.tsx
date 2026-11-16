import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { Unpaired } from '../api/client';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { SENDER_ASKED, SENDER_WHEN_WORDS, senderWordings } from '../test/senders';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention, forgetNeeds, reads, reload } from './needs';
import { chatWaits } from './needs-chats';
import { chats, lines } from './state';
import { forgetWaiting } from './waiting';

// The sender line in Needs you's pane: under the From of the approval the pane shows, the sending name the provider
// holds now for that mailbox, in the four wordings of an agent's chat's card (`chat-sender-line.screen.test.tsx`), read
// when the pane comes to show the approval. None of them changes Approve, Edit, Reject or the gate they follow.

/** The console on Needs you, which lists the recorded message as the one approval waiting. */
async function renderNeeds(fake: FakeApi): Promise<FakeApi> {
    fake.answer('system.info', 'system.info/with-ui')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('approval.get', 'approval.get/card-message')
        .answerAnyRequest('approval.list', () => 'approval.list/card-message-pending')
        .answerAnyRequest('decision.list', () => 'decision.list/empty');
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
    return fake;
}

const needs = () => screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
const pane = () => needs().querySelector<HTMLElement>('.needs-pane')!;
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const asks = (fake: FakeApi) => fake.calls.filter((each) => each.operation === 'sender.list').map((each) => each.body);
/** The pane's acts, each with whether it is disabled, once Approve is live. */
async function liveActs(): Promise<(string | boolean)[][]> {
    await waitFor(() =>
        expect(within(pane()).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false),
    );
    return within(pane())
        .getAllByRole('button')
        .map((button) => [words(button), button.hasAttribute('disabled')]);
}
const acts = [
    ['Approve', false],
    ['Edit', false],
    ['Reject', false],
    ['Open the chat', false],
];

beforeEach(() => {
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    attention.approvalsCursor.value = null;
    attention.questionsCursor.value = null;
    reads.approvals.value = {};
    reads.unread.value = {};
    forgetWaiting();
    forgetNeeds();
    chatWaits.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});

describe('the sender line in Needs you’s pane', () => {
    for (const wording of senderWordings)
        it(`says ${wording.name}, under the From, and leaves the pane’s acts as they were`, async () => {
            const fake = await renderNeeds(new FakeApi().answer('sender.list', wording.answer));
            const line = await waitFor(() => {
                const found = pane().querySelector<HTMLElement>('.sender-line');
                expect(found).not.toBeNull();
                return found!;
            });
            expect(words(line.querySelector('.sender-now')!)).toBe(
                [wording.line, wording.code, wording.message].filter(Boolean).join(' '),
            );
            expect(line.querySelector('code')?.textContent ?? null).toBe(wording.code ?? null);
            expect(line.querySelector('.sender-when')?.textContent ?? null).toBe(
                wording.when ? SENDER_WHEN_WORDS : null,
            );
            // Directly under the From it speaks of, before the channel.
            const from = pane().querySelector('.needs-from')!;
            expect(words(from)).toBe('From owner@example.test');
            expect(from.nextElementSibling).toBe(line);
            expect(line.nextElementSibling?.classList.contains('needs-channel')).toBe(true);
            expect(asks(fake)).toEqual([SENDER_ASKED]);
            expect(words(line)).not.toMatch(/recipient|will see/i);
            expect(line.querySelector('button, a, input')).toBeNull();
            expect(await liveActs()).toEqual(acts);
        });

    it('draws no line while the accounts are read, and Approve is live all the same', async () => {
        const fake = new FakeApi().answer('sender.list', 'sender.list/card-message-sender');
        const release = fake.hold('sender.list');
        await renderNeeds(fake);
        await waitFor(() => expect(asks(fake)).toHaveLength(1));
        expect(await liveActs()).toEqual(acts);
        expect(pane().querySelector('.sender-line')).toBeNull();
        release();
        await waitFor(() =>
            expect(words(pane().querySelector('.sender-line .sender-now')!)).toBe(
                'Sending name at Reply now: Owner Example',
            ),
        );
    });

    it('reads once while the pane keeps showing the approval, whatever reloads the lists', async () => {
        const fake = await renderNeeds(new FakeApi().answer('sender.list', 'sender.list/card-message-sender'));
        await waitFor(() => expect(pane().querySelector('.sender-line')).not.toBeNull());
        await reload();
        await reload();
        expect(words(pane().querySelector('.sender-line .sender-now')!)).toBe(
            'Sending name at Reply now: Owner Example',
        );
        expect(asks(fake)).toEqual([SENDER_ASKED]);
    });

    it('reads again when Needs you is drawn again, so no line is older than the pane that shows it', async () => {
        const fake = await renderNeeds(new FakeApi().answer('sender.list', 'sender.list/card-message-sender'));
        await waitFor(() => expect(pane().querySelector('.sender-line')).not.toBeNull());
        // The window is closed and opened again on Needs you, which shows the same approval.
        cleanup();
        await renderNeeds(fake.answer('sender.list', 'sender.list/card-message-sender'));
        await waitFor(() => expect(asks(fake)).toEqual([SENDER_ASKED, SENDER_ASKED]));
        await waitFor(() => expect(pane().querySelector('.sender-line')).not.toBeNull());
    });

    it('draws nothing when the pairing has ended during the read, and Approve is live all the same', async () => {
        const fake = new FakeApi();
        const call = fake.call.bind(fake);
        fake.call = async (operation, body, kind) => {
            if (operation === 'sender.list') {
                fake.calls.push({ operation, body: body ?? {}, kind: kind ?? 'read' });
                throw new Unpaired();
            }
            return call(operation, body, kind);
        };
        await renderNeeds(fake);
        await waitFor(() => expect(asks(fake)).toHaveLength(1));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(await liveActs()).toEqual(acts);
        expect(pane().querySelector('.sender-line')).toBeNull();
    });
});
