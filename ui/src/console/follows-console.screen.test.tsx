import { cleanup, act as flush, fireEvent, render, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { t } from '../platform/i18n';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { agentHeads } from '../test/agent-head';
import { shown } from '../test/area';
import { chatDesktop, stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { forgetFollowed } from './followed';
import { attention, forgetNeeds, reads } from './needs';
import { chatWaits } from './needs-chats';
import { chats, clock, conversations, lines } from './state';
import { forgetWaiting } from './waiting';

// What an email send follows, in the console: the Follows row and the subject marked as not sent, on Needs you's pane and
// on an agent's chat's card, against the exchanges the runtime recorded. apr_1 is a letter of its own, apr_2 a follow-up
// under the letter wi_1; the follow-up's letter is read with `workitem.get`, and every assertion waits for that read.

const chat = 'conv_01JDKEPT0000000000000000W1';
const LETTER =
    /A reply to "Notes from the meetup" of .+, the letter this campaign's latest thread with this person began with\./;
const OWN = "A new letter: this provider's plugin does not offer a reply in a thread on this channel.";

const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const reading = (fake: FakeApi) => fake.calls.filter((each) => each.operation === 'workitem.get');

/** The runtime as it was recorded: both approvals wait, and each work item answers as recorded. */
function runtime(fake: FakeApi): FakeApi {
    return fake
        .answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answerAnyRequest('approval.get', (body) =>
            (body as { approval_id?: string }).approval_id === 'apr_2'
                ? 'approval.get/card-follow'
                : 'approval.get/card-message',
        )
        .answerAnyRequest('approval.list', () => 'approval.list/messages-pending')
        .answerAnyRequest('decision.list', () => 'decision.list/empty')
        .answerAnyRequest('workitem.get', (body) =>
            (body as { work_item_id?: string }).work_item_id === 'wi_2'
                ? 'workitem.get/card-follow-parked'
                : 'workitem.get/card-follow-letter',
        )
        .answerAnyRequest('sender.list', () => 'sender.list/card-message-sender');
}

function pair(): void {
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
}

async function renderNeeds(fake: FakeApi): Promise<void> {
    runtime(fake);
    watch(fake);
    pair();
    render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
}

const pane = () => document.querySelector<HTMLElement>('.needs-pane')!;

/** Needs you's pane on the approval `id`, once the whole approval has been read and the pane draws it. */
async function choose(id: string): Promise<HTMLElement> {
    await waitFor(() => {
        const row = document.querySelector<HTMLElement>(`.needs-row[data-row="${id}"]`);
        expect(row).not.toBeNull();
        fireEvent.click(row!);
        expect(within(pane()).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
        expect(pane().querySelector('.ask-s')).not.toBeNull();
    });
    return pane();
}

function renderChat(fake: FakeApi): void {
    runtime(fake);
    watch(agentHeads(fake));
    pair();
    const desktop = chatDesktop();
    desktop.keep({
        id: chat,
        campaignId: 'cmp_1',
        title: 'Spring',
        entries: [{ kind: 'user', text: 'How is it going?' }],
    });
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
}

async function openCard(id: string): Promise<HTMLElement> {
    const side = () =>
        within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });
    await waitFor(() => {
        fireEvent.click(within(side()).getByRole('button', { name: 'Spring' }));
        expect(shown()).toBe(`chat:${chat}`);
    });
    const area = document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
    return waitFor(() => {
        const card = area.querySelector<HTMLElement>(`.waiting-card[data-id="${id}"]`);
        expect(card).not.toBeNull();
        expect(within(card!).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
        return card!;
    });
}

beforeEach(() => {
    cleanup();
    chats.value = {};
    conversations.value = [];
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
    forgetFollowed();
    chatWaits.value = null;
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});

describe('the Follows row in Needs you’s pane', () => {
    it('names the letter a follow-up goes under, read before it says so, and marks the subject as not sent', async () => {
        const fake = new FakeApi();
        await renderNeeds(fake);
        const detail = await choose('apr_2');
        await waitFor(() => expect(words(detail.querySelector('.needs-follows')!)).toMatch(LETTER));
        // The followed letter's work item was read, once, by its identifier, before the row said its subject.
        expect(reading(fake).map((each) => each.body)).toEqual([{ work_item_id: 'wi_1' }]);
        expect(words(detail.querySelector('.needs-follows')!)).toMatch(/^Follows A reply to "Notes from the meetup"/);
        const mark = detail.querySelector('.needs-not-sent')!;
        expect(words(mark)).toBe('Subject (not sent)');
        // The mark is directly above the subject it is about.
        expect(mark.nextElementSibling?.classList.contains('ask-s')).toBe(true);
        expect(words(mark.nextElementSibling!)).toBe('One question about the meetup');
    });

    it('says the unread sentence while the read is out, then the letter once it has arrived', async () => {
        const fake = new FakeApi();
        const release = fake.hold('workitem.get');
        await renderNeeds(fake);
        const detail = await choose('apr_2');
        await waitFor(() => expect(reading(fake)).toHaveLength(1));
        expect(words(detail.querySelector('.needs-follows')!)).toBe(`Follows ${t('follows.reply.unread')}`);
        expect(detail.querySelector('.needs-not-sent')).not.toBeNull();
        release();
        await waitFor(() => expect(words(pane().querySelector('.needs-follows')!)).toMatch(LETTER));
    });

    it('says the unread sentence when the followed letter’s read fails', async () => {
        const fake = new FakeApi();
        fake.unreachable('workitem.get');
        await renderNeeds(fake);
        const detail = await choose('apr_2');
        await waitFor(() => expect(reading(fake).length).toBeGreaterThan(0));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(words(detail.querySelector('.needs-follows')!)).toBe(`Follows ${t('follows.reply.unread')}`);
        expect(words(detail.querySelector('.needs-not-sent')!)).toBe('Subject (not sent)');
    });

    it('says a letter of its own in the row’s own words, reads nothing, and marks no subject', async () => {
        const fake = new FakeApi();
        await renderNeeds(fake);
        const detail = await choose('apr_1');
        expect(words(detail.querySelector('.needs-follows')!)).toBe(`Follows ${OWN}`);
        expect(detail.querySelector('.needs-not-sent')).toBeNull();
        expect(words(detail.querySelector('.ask-s')!)).toBe('Notes from the meetup');
        expect(reading(fake)).toHaveLength(0);
    });

    it('labels the edit form’s subject as not sent on a follow-up, and keeps it editable', async () => {
        const fake = new FakeApi();
        await renderNeeds(fake);
        const detail = await choose('apr_2');
        fireEvent.click(within(detail).getByRole('button', { name: 'Edit' }));
        const field = await waitFor(() => {
            const input = pane().querySelector<HTMLInputElement>('.needs-edit input[type="text"]');
            expect(input).not.toBeNull();
            return input!;
        });
        expect(words(field.closest('label')!)).toBe('Subject (not sent)');
        expect(field.hasAttribute('disabled')).toBe(false);
    });

    it('labels the edit form’s subject plainly on a letter of its own', async () => {
        const fake = new FakeApi();
        await renderNeeds(fake);
        const detail = await choose('apr_1');
        fireEvent.click(within(detail).getByRole('button', { name: 'Edit' }));
        const field = await waitFor(() => {
            const input = pane().querySelector<HTMLInputElement>('.needs-edit input[type="text"]');
            expect(input).not.toBeNull();
            return input!;
        });
        expect(words(field.closest('label')!)).toBe('Subject');
    });

    it('asks a failed read again at the next tick and says the letter once it answers', async () => {
        const fake = new FakeApi();
        const back = fake.unreachable('workitem.get');
        await renderNeeds(fake);
        await choose('apr_2');
        await waitFor(() => expect(reading(fake).length).toBeGreaterThan(0));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(words(pane().querySelector('.needs-follows')!)).toBe(`Follows ${t('follows.reply.unread')}`);
        back();
        flush(() => {
            clock.value += 30_000;
        });
        await waitFor(() => expect(words(pane().querySelector('.needs-follows')!)).toMatch(LETTER));
    });

    it('words the letter’s moment when it is drawn: a read made on one day names the date on the next', async () => {
        const fake = new FakeApi();
        clock.value = Date.parse('2026-10-16T12:00:00Z');
        await renderNeeds(fake);
        await choose('apr_2');
        await waitFor(() => expect(words(pane().querySelector('.needs-follows')!)).toMatch(/of 09:00, the letter/));
        flush(() => {
            clock.value = Date.parse('2026-10-17T12:00:00Z');
        });
        await waitFor(() =>
            expect(words(pane().querySelector('.needs-follows')!)).toMatch(/of 16 Oct, 09:00, the letter/),
        );
        expect(reading(fake)).toHaveLength(1);
    });
});

describe('the Follows row on an agent’s chat’s card', () => {
    it('names the letter a follow-up goes under, read before it says so, and marks the subject as not sent', async () => {
        const fake = new FakeApi();
        renderChat(fake);
        const card = await openCard('apr_2');
        await waitFor(() => expect(words(card.querySelector('.waiting-follows')!)).toMatch(LETTER));
        expect(reading(fake).map((each) => each.body)).toEqual([{ work_item_id: 'wi_1' }]);
        const mark = card.querySelector('.waiting-not-sent')!;
        expect(words(mark)).toBe('Subject (not sent)');
        expect(mark.nextElementSibling?.classList.contains('ask-s')).toBe(true);
    });

    it('says the unread sentence when the followed letter’s read fails', async () => {
        const fake = new FakeApi();
        fake.unreachable('workitem.get');
        renderChat(fake);
        const card = await openCard('apr_2');
        await waitFor(() => expect(reading(fake).length).toBeGreaterThan(0));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(words(card.querySelector('.waiting-follows')!)).toBe(`Follows ${t('follows.reply.unread')}`);
        expect(card.querySelector('.waiting-not-sent')).not.toBeNull();
    });

    it('says a letter of its own in the row’s own words and marks no subject', async () => {
        const fake = new FakeApi();
        renderChat(fake);
        const card = await openCard('apr_1');
        expect(words(card.querySelector('.waiting-follows')!)).toBe(`Follows ${OWN}`);
        expect(card.querySelector('.waiting-not-sent')).toBeNull();
    });
    it('asks a failed read again at the next tick and says the letter once it answers', async () => {
        const fake = new FakeApi();
        const back = fake.unreachable('workitem.get');
        renderChat(fake);
        const card = await openCard('apr_2');
        await waitFor(() => expect(reading(fake).length).toBeGreaterThan(0));
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(words(card.querySelector('.waiting-follows')!)).toBe(`Follows ${t('follows.reply.unread')}`);
        back();
        flush(() => {
            clock.value += 30_000;
        });
        await waitFor(() => expect(words(card.querySelector('.waiting-follows')!)).toMatch(LETTER));
    });

    it('labels the edit form’s subject as not sent on a follow-up, and keeps it editable', async () => {
        const fake = new FakeApi();
        renderChat(fake);
        const card = await openCard('apr_2');
        fireEvent.click(within(card).getByRole('button', { name: 'Edit' }));
        const field = await waitFor(() => {
            const input = card.querySelector<HTMLInputElement>('.message-editor input');
            expect(input).not.toBeNull();
            return input!;
        });
        expect(words(card.querySelector(`label[for="${field.id}"]`)!)).toBe('Subject (not sent)');
        expect(field.hasAttribute('disabled')).toBe(false);
    });

    it('labels the edit form’s subject plainly on a letter of its own', async () => {
        const fake = new FakeApi();
        renderChat(fake);
        const card = await openCard('apr_1');
        fireEvent.click(within(card).getByRole('button', { name: 'Edit' }));
        const field = await waitFor(() => {
            const input = card.querySelector<HTMLInputElement>('.message-editor input');
            expect(input).not.toBeNull();
            return input!;
        });
        expect(words(card.querySelector(`label[for="${field.id}"]`)!)).toBe('Subject');
    });
});
