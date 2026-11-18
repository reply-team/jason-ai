import { cleanup, act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { chatDesktop, journalLine, stubStream, type StubStream } from '../test/desktop';
import { shown } from '../test/area';
import { Unpaired } from '../api/client';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import type { FixtureName } from '../test/fixtures';
import { SENDER_ASKED, SENDER_WHEN_WORDS, senderWordings } from '../test/senders';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';
import { forgetWaiting } from './waiting';

// The line under the address an approval's send goes from: the sending name the provider holds now for that mailbox,
// read with `sender.list` of the approval's campaign on the send's channel when the card is drawn, and found by the
// account the send names. Four wordings, one for each answer, on an agent's chat's card and on Needs you's panel; none
// of them changes Approve, Reject or the gate they follow.

const chat = 'conv_01JDKEPT0000000000000000W1';

/** What the runtime lists as waiting in the chat's campaign: the recorded message, until a test says it was decided. */
let listed: FixtureName = 'approval.list/card-message-pending';

/** The console with one agent's chat, whose campaign waits on one message the runtime recorded. */
function renderChat(fake: FakeApi, journal: StubStream = stubStream()): FakeApi {
    fake.answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer('approval.get', 'approval.get/card-message', 'approval.get/card-message-reasked')
        .answerAnyRequest('approval.list', (body) => ('campaign_id' in body ? listed : 'approval.list/empty'))
        .answerAnyRequest('decision.list', (body) =>
            'campaign_id' in body ? 'decision.list/summer-pending' : 'decision.list/empty',
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

async function openCard(id = 'apr_1'): Promise<HTMLElement> {
    const side = () =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });
    await waitFor(() => {
        fireEvent.click(within(side()).getByRole('button', { name: 'Spring' }));
        expect(shown()).toBe(`chat:${chat}`);
    });
    const pane = document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
    return waitFor(() => {
        const card = pane.querySelector<HTMLElement>(`.waiting-card[data-id="${id}"]`);
        expect(card).not.toBeNull();
        expect(within(card!).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
        return card!;
    });
}

const asks = (fake: FakeApi, operation: string) => fake.calls.filter((each) => each.operation === operation).length;
const lineOf = (card: HTMLElement) => card.querySelector('.sender-line .sender-now');

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

beforeEach(() => {
    listed = 'approval.list/card-message-pending';
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    attention.detail.value = null;
    forgetWaiting();
});

describe('the sender line on an agent’s chat’s card', () => {
    for (const wording of senderWordings)
        it(`says ${wording.name}, under the address, and leaves Approve as it was`, async () => {
            const fake = renderChat(new FakeApi().answer('sender.list', wording.answer));
            const card = await openCard();
            const line = await waitFor(() => {
                const found = card.querySelector('.sender-line');
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
            // Directly under the header that names the address, before why the approval waits.
            expect(card.querySelector('.ask-h')!.nextElementSibling).toBe(line);
            expect(words(card.querySelector('.ask-h')!)).toContain('from owner@example.test');
            // Read once, for the approval's campaign on the send's channel, the whole team's accounts.
            expect(fake.calls.filter((each) => each.operation === 'sender.list').map((each) => each.body)).toEqual([
                SENDER_ASKED,
            ]);
            // Nothing in it says what a recipient sees, and it holds no act.
            expect(words(line)).not.toMatch(/recipient|will see/i);
            expect(line.querySelector('button, a, input')).toBeNull();
            const acts = within(card)
                .getAllByRole('button')
                .map((button) => [button.textContent, button.hasAttribute('disabled')]);
            expect(acts).toEqual([
                ['Approve', false],
                ['Edit', false],
                ['Reject', false],
                ['Details', false],
            ]);
        });

    it('draws no line while the accounts are read, and Approve is live all the same', async () => {
        const fake = new FakeApi().answer('sender.list', 'sender.list/card-message-sender');
        const release = fake.hold('sender.list');
        renderChat(fake);
        const card = await openCard();
        await waitFor(() => expect(fake.calls.some((each) => each.operation === 'sender.list')).toBe(true));
        // Nothing is said, but the line place is held at its height, so the acts below do not move when it arrives.
        expect(card.querySelector('.sender-line:not(.sender-hold)')).toBeNull();
        const hold = card.querySelector<HTMLElement>('.sender-line.sender-hold')!;
        expect(hold.getAttribute('aria-hidden')).toBe('true');
        expect(hold.querySelectorAll('.sender-now, .sender-when')).toHaveLength(2);
        expect(hold.nextElementSibling?.classList.contains('ask-w')).toBe(true);
        expect(within(card).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
        release();
        await waitFor(() =>
            expect(words(card.querySelector('.sender-line .sender-now')!)).toBe(
                'Sending name at Reply now: Owner Example',
            ),
        );
    });

    it('reads the accounts once while the chat is drawn, however often the lists are read again', async () => {
        const journal = stubStream();
        const fake = renderChat(new FakeApi().answer('sender.list', 'sender.list/card-message-sender'), journal);
        const card = await openCard();
        await waitFor(() => expect(lineOf(card)).not.toBeNull());
        const lists = asks(fake, 'approval.list');
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_301', kind: 'approval_requested' }));
        });
        await waitFor(() => expect(asks(fake, 'approval.list')).toBeGreaterThan(lists));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(asks(fake, 'sender.list')).toBe(1);
        expect(words(lineOf(card)!)).toBe('Sending name at Reply now: Owner Example');
    });

    it('reads the accounts again when the chat is drawn again, as a reload draws it, and says what they are then', async () => {
        const fake = new FakeApi().answer(
            'sender.list',
            'sender.list/card-message-sender',
            'sender.list/card-sender-other',
        );
        renderChat(fake);
        const first = await openCard();
        await waitFor(() => expect(words(lineOf(first)!)).toBe('Sending name at Reply now: Owner Example'));
        cleanup();
        chats.value = {};
        conversations.value = [];
        forgetWaiting();
        renderChat(fake);
        const again = await openCard();
        await waitFor(() => expect(words(lineOf(again)!)).toBe('Reply does not list this mailbox now.'));
        expect(asks(fake, 'sender.list')).toBe(2);
        expect(within(again).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
    });

    it('leaves with its card when the approval is decided elsewhere, as in a second window', async () => {
        const journal = stubStream();
        const fake = renderChat(new FakeApi().answer('sender.list', 'sender.list/card-message-sender'), journal);
        const card = await openCard();
        await waitFor(() => expect(lineOf(card)).not.toBeNull());
        listed = 'approval.list/card-message-after-edit';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_302', kind: 'approval_approved' }));
        });
        await waitFor(() => expect(document.querySelector('.waiting-card[data-id="apr_1"]')).toBeNull());
        expect(document.querySelector('.sender-line')).toBeNull();
        expect(asks(fake, 'sender.list')).toBe(1);
    });

    /** Waits for a card that comes after the chat was drawn, with its buttons on. */
    async function laterCard(id: string): Promise<HTMLElement> {
        return waitFor(
            () => {
                const card = document.querySelector<HTMLElement>(
                    `.area > .pane:not([hidden]) .waiting-card[data-id="${id}"]`,
                );
                expect(card).not.toBeNull();
                expect(within(card!).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
                return card!;
            },
            { timeout: 3000 },
        );
    }

    it('reads the accounts again for a card that comes after the chat was drawn, as a message asked again does', async () => {
        const journal = stubStream();
        const fake = renderChat(
            new FakeApi().answer('sender.list', 'sender.list/card-message-sender', 'sender.list/card-sender-other'),
            journal,
        );
        const card = await openCard();
        await waitFor(() => expect(words(lineOf(card)!)).toBe('Sending name at Reply now: Owner Example'));
        listed = 'approval.list/card-message-reasked';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_303', kind: 'approval_requested' }));
        });
        const later = await laterCard('apr_2');
        await waitFor(() => expect(words(lineOf(later)!)).toBe('Reply does not list this mailbox now.'));
        expect(asks(fake, 'sender.list')).toBe(2);
    });

    it('reads the accounts again for a later card after a read that failed, and says what they are then', async () => {
        const journal = stubStream();
        const fake = renderChat(
            new FakeApi().answer('sender.list', 'sender.list/card-sender-unread', 'sender.list/card-message-sender'),
            journal,
        );
        const card = await openCard();
        await waitFor(() => expect(words(lineOf(card)!)).toContain('The sending name could not be read.'));
        listed = 'approval.list/card-message-reasked';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_304', kind: 'approval_requested' }));
        });
        const later = await laterCard('apr_2');
        await waitFor(() => expect(words(lineOf(later)!)).toBe('Sending name at Reply now: Owner Example'));
        expect(asks(fake, 'sender.list')).toBe(2);
    });

    it('draws no line for an invitation, and asks for no accounts', async () => {
        listed = 'approval.list/card-invitation-pending';
        const fake = new FakeApi().answer('approval.get', 'approval.get/card-invitation');
        renderChat(fake);
        fake.answer('approval.get', 'approval.get/card-invitation');
        const card = await openCard();
        expect(words(card.querySelector('.ask-h')!)).toContain('from https://www.linkedin.com/in/owner-example');
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(card.querySelector('.sender-line')).toBeNull();
        expect(card.querySelector('.sender-hold')).toBeNull();
        expect(asks(fake, 'sender.list')).toBe(0);
    });

    it('says a reply in a thread goes from the account the conversation is in, draws no line, and asks for no accounts', async () => {
        listed = 'approval.list/card-reply-pending';
        const fake = new FakeApi().answer('approval.get', 'approval.get/card-reply');
        renderChat(fake);
        fake.answer('approval.get', 'approval.get/card-reply');
        const card = await openCard();
        expect(words(card.querySelector('.ask-h')!)).toContain('Email · from the account this conversation is in');
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(card.querySelector('.sender-line')).toBeNull();
        expect(card.querySelector('.sender-hold')).toBeNull();
        expect(asks(fake, 'sender.list')).toBe(0);
    });

    it('draws nothing when the pairing ends while the accounts are read, and leaves Approve as it was', async () => {
        const fake = new FakeApi();
        const call = fake.call.bind(fake);
        // The runtime says the pairing has ended: the session answers for that, and the line keeps nothing.
        fake.call = async (operation, body, kind) => {
            if (operation === 'sender.list') {
                fake.calls.push({ operation, body: body ?? {}, kind: kind ?? 'read' });
                throw new Unpaired();
            }
            return call(operation, body, kind);
        };
        renderChat(fake);
        const card = await openCard();
        await waitFor(() => expect(asks(fake, 'sender.list')).toBe(1));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(card.querySelector('.sender-line:not(.sender-hold)')).toBeNull();
        expect(within(card).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
    });
});
