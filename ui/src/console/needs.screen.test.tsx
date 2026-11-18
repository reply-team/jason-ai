import { readFileSync } from 'node:fs';
import { act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import type { Approval, ApprovalSummary, WorkItem } from '../api/types';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { attentionStore } from '../screens/attention/store';
import { whyWords } from '../screens/send-modes/doubts';
import { journalLine, stubDesktop, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { agentHeads } from '../test/agent-head';
import { fixtures, type FixtureName } from '../test/fixtures';
import { linesOf, searchIntent, firstSearchLines, topUpArguments } from '../test/search-words';
import { ASKING_MARK, QUEUED_SENTENCE } from '../screens/attention/reasking-model';
import { noteEdit } from '../screens/attention/reasking';
import { here } from './state';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention, forgetNeeds, reads, reload } from './needs';
import { chatWaits } from './needs-chats';
import { chats, lines } from './state';
import { READ_LIMIT_MS, fillApprovals, forgetWaiting, wholeReads } from './waiting';

const hostile = '<img src=x onerror="window.hit=1">';

async function renderConsole(fake: FakeApi, journal: StubStream = stubStream()): Promise<StubDesktop> {
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
    fake.answer('system.info', 'system.info/with-ui')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    // The sending name under an email's From is read for every approval the pane shows (its own rows are in
    // `needs-sender-line.screen.test.tsx`); a test that says nothing of it is answered with a name.
    if (!fake.offers('sender.list')) fake.answerAnyRequest('sender.list', 'sender.list/card-message-sender');
    const desktop = stubDesktop();
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    // Needs you is a page of the sidebar's menu, chosen as a person chooses it once the window has read what waits.
    await waitFor(() => expect(attention.loaded.value).toBe(true));
    fireEvent.click(within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }));
    return desktop;
}

const needs = () => screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
/** A line of the list on the left, by the id of what it shows. */
const row = (id: string) => needs().querySelector<HTMLElement>(`[data-row="${id}"]`)!;
/** The pane on the right: the item chosen, whole. */
const pane = () => needs().querySelector<HTMLElement>('.needs-pane')!;
/** Chooses a line of the list, and waits for the pane to show it. */
async function choose(id: string): Promise<HTMLElement> {
    fireEvent.click(row(id));
    return waitFor(() => {
        expect(pane().dataset['id']).toBe(id);
        return pane();
    });
}
/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
/** The filters drawn, each with its count, as a person reads them. */
const chips = () => [...needs().querySelectorAll<HTMLElement>('.needs-filter .btn')].map((each) => words(each));

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

afterEach(() => {
    vi.useRealTimers();
    delete (window as { hit?: number }).hit;
});

/** The console's sheet, its comments taken out: read from the file, since the test run hands a stylesheet import back empty. */
const sheet = readFileSync('src/console/console.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
const declared = (selector: string): string =>
    Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g))
        .filter((rule) =>
            rule[1]!
                .split(',')
                .map((each) => each.trim())
                .includes(selector),
        )
        .map((rule) => rule[2]!.replace(/\s+/g, ' ').trim())
        .join(' ');

describe('the sheet of Needs you', () => {
    /** The measure the panels drawn for a narrow column keep. */
    const measure = /max-width: (\d+px)/.exec(declared('.jc-console .pane.measure .panel-body'))![1]!;
    /** The text a person reads in the pane, by the selector the sheet caps it with. */
    const read = [
        '.jc-console .needs-pane .needs-words',
        '.jc-console .needs-pane .needs-facts',
        '.jc-console .needs-pane.q .needs-title',
        '.jc-console .needs-pane .ui-opts',
        '.jc-console .needs-pane .needs-said',
        '.jc-console .needs-pane .needs-edit',
        '.jc-console .needs-pane .send-doubts',
        '.jc-console .needs-pane .ask-a',
        '.jc-console .needs-pane .ask-w',
        '.jc-console .needs-pane > .note',
    ];

    it('draws Needs you across the whole area, its list at one width beside a pane that takes the rest', () => {
        expect(measure).toBe('880px');
        expect(declared('.jc-console .pane.measure .panel-body.needs-panel')).toContain('max-width: none');
        expect(declared('.jc-console .needs-panes')).toContain(
            'grid-template-columns: minmax(260px, 420px) minmax(0, 1fr)',
        );
    });

    it('holds the text a person reads in the pane to the measure the other panels keep, at the pane’s left', () => {
        for (const selector of read) {
            expect(declared(selector), selector).toContain(`max-width: ${measure}`);
            expect(declared(selector), selector).not.toMatch(/margin[^;]*auto/);
        }
    });

    it('caps the very elements the pane draws a message, a question and its options in', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/needs-questions')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply')
                .answer('decision.get', 'decision.get/needs-question'),
        );
        const pane = () => document.querySelector<HTMLElement>('.needs-pane')!;
        const capped = (element: Element) => read.some((selector) => element.matches(selector));
        await waitFor(() => expect(pane().querySelector('.needs-words')).toBeTruthy());
        expect(capped(pane().querySelector('.needs-words')!)).toBe(true);
        expect(capped(pane().querySelector('.needs-facts')!)).toBe(true);
        fireEvent.click(document.querySelector<HTMLElement>('[data-row="dec_1"]')!);
        await waitFor(() => expect(pane().querySelector('.ui-opts')).toBeTruthy());
        expect(capped(pane().querySelector('.needs-title')!)).toBe(true);
        expect(capped(pane().querySelector('.ui-opts')!)).toBe(true);
        expect(capped(pane().querySelector('.ask-a')!)).toBe(true);
    });

    it('caps the doubt shown before Approve', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/needs-questions')
                .answer('approval.get', 'approval.get/park-doubted', 'approval.get/other-pending'),
        );
        await waitFor(() => expect(document.querySelector('.needs-pane .send-doubts')).toBeTruthy());
        const doubt = document.querySelector('.needs-pane .send-doubts')!;
        expect(read.some((selector) => doubt.matches(selector))).toBe(true);
    });
});

describe('Needs you', () => {
    const show = (name: string) =>
        fireEvent.click(
            within(needs().querySelector<HTMLElement>('.needs-filter')!).getByRole('button', {
                name: new RegExp(`^${name} `),
            }),
        );
    const pressed = () =>
        within(needs())
            .getAllByRole('button', { pressed: true })
            .map((each) => words(each));
    const messages = () =>
        new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
    const sent = (fake: FakeApi, operation: string) =>
        fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
    /** A recorded request as the screen gives it: without the actor the client adds to a write. */
    const asGiven = (request: object) => Object.fromEntries(Object.entries(request).filter(([key]) => key !== 'actor'));
    const type = (box: HTMLElement, value: string) => fireEvent.input(box, { target: { value } });
    /** Lets every answer the fake gave be taken in: its answers are promises, all settled within one task. */
    const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));

    it('says so in a card when nothing is waiting', async () => {
        await renderConsole(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        expect(words(needs().querySelector('.card.needs-empty .bd')!)).toBe("You're all caught up");
        // The state is a centred card with a check disc, and says only what is true of the runtime's answer.
        expect(needs().querySelector('.card.needs-empty .needs-disc svg')).toBeTruthy();
        expect(words(needs().querySelector('.card.needs-empty .needs-none-says')!)).toBe(
            'Nothing is waiting for your decision.',
        );
        expect(needs().textContent).not.toMatch(/minute|every/);
        expect(needs().querySelector('.needs-pane')).toBeNull();
        expect(needs().querySelector('.needs-filter')).toBeNull();
    });

    it('offers the campaigns from the card, and its one control opens their panel', async () => {
        await renderConsole(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        const card = needs().querySelector<HTMLElement>('.card.needs-empty')!;
        expect(
            within(card)
                .getAllByRole('button')
                .map((each) => words(each)),
        ).toEqual(['New GTM agent', 'Open your agents']);
        expect(screen.queryByRole('heading', { name: 'Agents', level: 1 })).toBeNull();

        fireEvent.click(within(card).getByRole('button', { name: 'Open your agents' }));
        await waitFor(() => expect(screen.getByRole('heading', { name: 'Agents', level: 1 })).toBeTruthy());
    });

    it('starts a new agent from the card, as the sidebar’s New GTM agent does', async () => {
        await renderConsole(
            new FakeApi().answer('approval.list', 'approval.list/empty').answer('decision.list', 'decision.list/empty'),
        );
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        fireEvent.click(
            within(needs().querySelector<HTMLElement>('.card.needs-empty')!).getByRole('button', {
                name: 'New GTM agent',
            }),
        );
        await waitFor(() => expect(screen.getByRole('form', { name: 'Start' })).toBeTruthy());
    });

    it('lists what waits on the left and shows the first, whole, on the right; choosing a line shows that one', async () => {
        await renderConsole(messages());
        await waitFor(() => within(pane()).getByText('Hi Ines, thanks for the talk at the meetup.'));
        expect(pane().dataset['id']).toBe('apr_1');
        expect(row('apr_1').getAttribute('aria-current')).toBe('true');
        expect(row('apr_2').hasAttribute('aria-current')).toBe(false);
        expect(words(row('apr_1'))).toContain('Approval · Spring launch');

        await choose('apr_2');
        await waitFor(() => within(pane()).getByText('Thanks for connecting, Noor.'));
        expect(row('apr_2').getAttribute('aria-current')).toBe('true');
        expect(within(pane()).queryByText('Hi Ines, thanks for the talk at the meetup.')).toBeNull();
    });

    it('counts each filter from the lists the runtime gave, draws no Chats while no chat was read, and says when a filter holds nothing', async () => {
        const fake = messages();
        await renderConsole(fake);
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        expect(chips()).toEqual(['All 2', 'Approvals 2', 'Questions 0', 'Inbox 0']);
        expect(pressed()).toEqual(['All 2']);
        const asked = fake.calls.length;
        const hash = location.hash;

        show('Questions');
        await waitFor(() => expect(words(needs().querySelector('.needs-none')!)).toBe('No questions for you.'));
        expect(pressed()).toEqual(['Questions 0']);
        expect(needs().querySelector('[data-row="apr_1"]')).toBeNull();
        expect(needs().querySelector('.needs-pane')).toBeNull();
        expect(needs().querySelector('.needs-empty')).toBeNull();

        show('Inbox');
        await waitFor(() =>
            expect(words(needs().querySelector('.needs-none')!)).toBe('Nothing about a reply waits for you.'),
        );

        show('Approvals');
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(needs().querySelector('.needs-none')).toBeNull();
        expect(fake.calls).toHaveLength(asked);
        expect(location.hash).toBe(hash);
    });

    it('holds under Inbox the answers to replies alone, by the operation the runtime listed', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply'),
        );
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        expect(chips()).toEqual(['All 2', 'Approvals 2', 'Questions 0', 'Inbox 1']);

        show('Inbox');
        await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
        expect(needs().querySelector('[data-row="apr_1"]')).toBeNull();
        expect(words(row('apr_2'))).toContain('Reply to Kai Lindt');
        expect(within(pane()).getByText('Thanks Kai, Thursday at ten works for me.')).toBeTruthy();
        expect(words(pane().querySelector('.needs-title')!)).toBe('Reply to Kai Lindt');
    });

    it('holds under Inbox a question the runtime names a thread for, and leaves one about no thread under Questions', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/needs-inbox-questions')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply')
                .answer('decision.get', 'decision.get/needs-inbox-question'),
        );
        await waitFor(() => expect(row('dec_2')).toBeTruthy());
        expect(chips()).toEqual(['All 4', 'Approvals 2', 'Questions 2', 'Inbox 2']);

        show('Inbox');
        await waitFor(() => expect(needs().querySelector('[data-row="apr_1"]')).toBeNull());
        expect(needs().querySelector('[data-row="dec_1"]')).toBeNull();
        expect(row('apr_2')).toBeTruthy();
        fireEvent.click(row('dec_2'));
        await waitFor(() => expect(pane().dataset['id']).toBe('dec_2'));
        expect(words(pane().querySelector('.needs-title')!)).toBe(
            fixtures['decision.get/needs-inbox-question'].response.question,
        );

        show('Questions');
        await waitFor(() => expect(row('dec_1')).toBeTruthy());
        expect(row('dec_2')).toBeTruthy();
    });

    it('narrows the list and every count to one agent, and back to all of them', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply'),
        );
        await waitFor(() => expect(row('apr_2')).toBeTruthy());
        const picker = () => within(needs()).getByRole('combobox', { name: 'Agent' }) as HTMLSelectElement;
        expect([...picker().options].map((each) => each.textContent?.trim())).toEqual([
            'All agents',
            'Spring launch',
            'Autumn outreach',
        ]);

        fireEvent.change(picker(), { target: { value: 'cmp_1' } });
        await waitFor(() => expect(needs().querySelector('[data-row="apr_2"]')).toBeNull());
        expect(chips()).toEqual(['All 1', 'Approvals 1', 'Questions 0', 'Inbox 0']);
        expect(pane().dataset['id']).toBe('apr_1');
        expect(picker().value).toBe('cmp_1');

        fireEvent.change(picker(), { target: { value: '' } });
        await waitFor(() => expect(row('apr_2')).toBeTruthy());
        expect(chips()).toEqual(['All 2', 'Approvals 2', 'Questions 0', 'Inbox 1']);
    });

    it('shows everything again once nothing waited, so an approval that arrives then is drawn', async () => {
        await renderConsole(messages());
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        const waiting = attention.approvals.value;
        show('Questions');
        await waitFor(() => expect(needs().querySelector('.needs-none')).toBeTruthy());

        attention.approvals.value = [];
        await waitFor(() => expect(needs().querySelector('.needs-empty')).toBeTruthy());
        attention.approvals.value = waiting;
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(needs().querySelector('.needs-none')).toBeNull();
        expect(pressed()).toEqual(['All 2']);
    });

    it('shows a question with its options, and answers it with the option pressed and the reason typed', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/empty')
            .answer('decision.list', 'decision.list/pending')
            .answer('decision.get', 'decision.get/pending-with-options')
            .answer('decision.answer', 'decision.answer/answered');
        await renderConsole(fake);
        await waitFor(() => within(pane()).getByRole('button', { name: 'Pause until the news' }));
        expect(words(pane().querySelector('.needs-title')!)).toBe(
            'Pause outreach to Vireo Labs until their funding news?',
        );
        expect(words(row('dec_1'))).toContain('Question · Autumn outreach');
        show('Approvals');
        await waitFor(() => expect(words(needs().querySelector('.needs-none')!)).toBe('No approvals waiting.'));
        show('All');
        await waitFor(() => within(pane()).getByRole('button', { name: 'Pause until the news' }));

        type(within(pane()).getByRole('textbox', { name: 'Reason (optional)' }), 'their CFO is leaving');
        fake.answer('decision.list', 'decision.list/empty');
        fireEvent.click(within(pane()).getByRole('button', { name: 'Pause until the news' }));
        await waitFor(() =>
            expect(sent(fake, 'decision.answer')).toEqual([asGiven(fixtures['decision.answer/answered'].request)]),
        );
    });

    it('says on each line who the message goes to, from what the list carries, before its whole read answers', async () => {
        const fake = messages();
        const release = fake.hold('approval.get');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('to Ines Duarte');
        expect(words(row('apr_2').querySelector('.needs-who')!)).toBe('to Noor Haddad');
        // Each approval is read whole once, for its Approve; the name never waits for that read.
        await waitFor(() => expect(fake.calls.filter((each) => each.operation === 'approval.get')).toHaveLength(2));
        release();
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
    });

    it('names a line that carries no contact by the address its own preview highlights, once read whole', async () => {
        await renderConsole(messages());
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: null }));
        await waitFor(() => expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('to ines@example.test'));
        expect(words(row('apr_2').querySelector('.needs-who')!)).toBe('to https://www.linkedin.com/in/noor-example');
    });

    it('names the person an approval that sends nothing is about, with no word before the name', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/pending')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending'),
        );
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(words(row('apr_1').querySelector('.needs-who')!)).toBe('Ada Lovelace');
        expect(words(row('apr_2').querySelector('.needs-who')!)).toBe('Grace Hopper');
        expect(words(row('apr_1').querySelector('.needs-row-t')!)).toBe('Add to sequence Ada Lovelace');
        expect(words(row('apr_2'))).not.toContain('to Grace');
    });

    describe('a paid search of the provider’s people', () => {
        const searches = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/search-pending')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/search-first', 'approval.get/search-top-up');

        it('is named in words, and shows its definition as written and its arguments in plain words under its intent', async () => {
            const fake = searches();
            await renderConsole(fake);
            await waitFor(() => expect(pane().querySelector('.needs-search')).toBeTruthy());
            expect(words(row('apr_1').querySelector('.needs-row-t')!)).toBe('Find people');
            expect(words(pane().querySelector('.needs-title')!)).toBe('Find people');
            expect(words(pane().querySelector('.needs-intent')!)).toBe(searchIntent);
            expect(linesOf(pane(), '.needs-search')).toEqual(firstSearchLines);
            // Nothing else of the card moves: the reason, and the three properties as they always read.
            expect(words(pane().querySelector('.ask-w')!)).toBe(
                'Approval is required for every operation of this kind for this agent.',
            );
            expect([...pane().querySelectorAll('.needs-facts > *')].map((each) => words(each))).toEqual([
                'Contacts nobody: it acts in your provider account only',
                'Cannot be undone',
                "Spends your provider's allowance or credits",
            ]);
            expect(words(pane())).not.toContain(fixtures['approval.get/search-first'].response.preview.summary);
            expect(sent(fake, 'workitem.get')).toEqual([]);
        });

        it('approves a top-up, which shows the audience it adds to among its arguments', async () => {
            const fake = searches().answer('approval.approve', 'approval.approve/search-top-up');
            await renderConsole(fake);
            await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
            await choose('apr_2');
            await waitFor(() => expect(linesOf(pane(), '.needs-search').at(-1)).toBe(topUpArguments));
            fake.answer('approval.list', 'approval.list/search-after-top-up');
            fireEvent.click(within(pane()).getByRole('button', { name: 'Approve' }));
            await waitFor(() => expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_2' }]));
            await waitFor(() => expect(needs().querySelector('[data-row="apr_2"]')).toBeNull());
        });
    });

    it("shows a message's sender and its channel in words beside the recipient, subject and the whole body", async () => {
        await renderConsole(messages());
        await waitFor(() => within(pane()).getByText('Notes from the meetup'));
        const email = pane();
        expect(words(email.querySelector('.needs-from')!)).toBe('From owner@example.test');
        expect(words(email.querySelector('.needs-channel')!)).toBe('Channel Email');
        expect(within(email).getByText('To Ines Duarte, ines@example.test')).toBeTruthy();
        expect(within(email).getByText('Hi Ines, thanks for the talk at the meetup.')).toBeTruthy();

        await choose('apr_2');
        await waitFor(() => within(pane()).getByText('Thanks for connecting, Noor.'));
        expect(words(pane().querySelector('.needs-from')!)).toBe('From https://www.linkedin.com/in/owner-example');
        expect(words(pane().querySelector('.needs-channel')!)).toBe('Channel LinkedIn');
    });

    it('shows nothing in the place of a sender or a channel the preview does not carry', async () => {
        await renderConsole(
            new FakeApi()
                .answer('approval.list', 'approval.list/pending')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending'),
        );
        await waitFor(() => within(pane()).getByText('Put Ada Lovelace into the campaign at the provider.'));
        expect(pane().querySelector('.needs-from')).toBeNull();
        expect(pane().querySelector('.needs-channel')).toBeNull();
        expect(words(pane())).not.toMatch(/From|Channel|unknown/);
    });

    it('says a reply in a thread goes from the account the conversation is in, names none, and reads no sending name', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/needs-inbox')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply');
        await renderConsole(fake);
        await waitFor(() => expect(row('apr_2')).toBeTruthy());
        // The message beside it still names the mailbox it goes from, with the sending name under it.
        await choose('apr_1');
        await waitFor(() => expect(words(pane().querySelector('.needs-from')!)).toBe('From owner@example.test'));
        await choose('apr_2');
        await waitFor(() => within(pane()).getByText('Thanks Kai, Thursday at ten works for me.'));
        expect(words(pane().querySelector('.needs-from')!)).toBe('From the account this conversation is in');
        expect(pane().querySelector('.needs-from')!.nextElementSibling?.classList.contains('sender-line')).not.toBe(
            true,
        );
        expect(pane().querySelector('.sender-line')).toBeNull();
        const asked = fake.calls.filter((each) => each.operation === 'sender.list').length;
        await settle();
        expect(fake.calls.filter((each) => each.operation === 'sender.list').length).toBe(asked);
    });

    it('draws a hostile sender, channel and recipient as text', async () => {
        await renderConsole(messages());
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        const recorded = fixtures['approval.get/email-message'].response;
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: hostile }));
        reads.approvals.value = {
            ...reads.approvals.value,
            apr_1: {
                ...recorded,
                preview: {
                    ...recorded.preview,
                    highlights: {
                        ...recorded.preview.highlights,
                        '/args/sender': { external_id: 's_12', address: hostile },
                        '/args/channel': hostile,
                    },
                },
            },
        };
        await waitFor(() => expect(within(pane()).getAllByText(hostile).length).toBe(3));
        expect(needs().querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });

    it('shows in the pane what an agent’s chat opened with Details, whatever the filter showed', async () => {
        const fake = messages();
        await renderConsole(fake);
        await waitFor(() => expect(Object.keys(reads.approvals.value)).toHaveLength(2));
        show('Questions');
        await waitFor(() => expect(needs().querySelector('.needs-none')).toBeTruthy());
        await flush(() => attention.open(fake, 'approvals', 'apr_2'));
        await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
        expect(pressed()).toEqual(['All 2']);
    });

    it('opens the agent’s own chat from the pane, and decides nothing', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
            .answer('decision.list', 'decision.list/empty', 'decision.list/by-campaign-pending')
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            // The chat, once shown, reads its own questions whole for its cards: what it reads is not this test's.
            .answerAnyRequest('decision.get', 'decision.get/for-campaign-row');
        const desktop = await renderConsole(fake);
        // The pane is painted again as its read answers, so the button is found once the reads are in.
        await waitFor(() => expect(Object.keys(reads.approvals.value).length).toBeGreaterThan(0));
        await settle();
        fireEvent.click(within(pane()).getByRole('button', { name: 'Open the chat' }));
        // The agent's Chat is drawn, under its header; its conversation is made at the person's first act in it.
        await waitFor(() => expect(document.querySelector('.agent-head h2')?.textContent).toBe('Spring'));
        expect(desktop.calls.filter((each) => each.name === 'chat.create')).toEqual([]);
        expect(
            fake.calls.filter((each) => /^(approval\.(approve|reject)|decision\.answer)$/.test(each.operation)),
        ).toEqual([]);
    });

    describe('the count', () => {
        const badge = () => document.querySelector<HTMLElement>('.side .count')!;

        it('is the number All shows, on the sidebar too', async () => {
            await renderConsole(
                new FakeApi()
                    .answer('approval.list', 'approval.list/needs-inbox')
                    .answer('decision.list', 'decision.list/pending')
                    .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply')
                    .answer('decision.get', 'decision.get/pending-with-options'),
            );
            await waitFor(() => expect(chips()[0]).toBe('All 4'));
            expect(words(badge())).toBe('4');
            expect(badge().getAttribute('aria-label')).toBe('4 waiting');
        });

        it('carries a plus while the runtime holds another page, and Show more reads that page', async () => {
            const fake = new FakeApi()
                .answer('approval.list', 'approval.list/needs-first-page', 'approval.list/needs-next-page')
                .answer('decision.list', 'decision.list/empty')
                .answerAnyRequest('approval.get', 'approval.get/needs-message');
            await renderConsole(fake);
            await waitFor(() => expect(chips()[0]).toBe('All 100+'));
            expect(words(badge())).toBe('100+');
            expect(chips()).toEqual(['All 100+', 'Approvals 100+', 'Questions 0', 'Inbox 1+']);

            fireEvent.click(within(needs()).getByRole('button', { name: 'Show more' }));
            await waitFor(() => expect(chips()[0]).toBe('All 102'));
            expect(words(badge())).toBe('102');
            expect(sent(fake, 'approval.list').at(-1)).toEqual(fixtures['approval.list/needs-next-page'].request);
            expect(within(needs()).queryByRole('button', { name: 'Show more' })).toBeNull();
        });
    });

    describe('stays where the person is', () => {
        const badge = () => document.querySelector<HTMLElement>('.side .count')!;
        /** A journal line, as the runtime writes one when something waits: the console reads what waits again. */
        const line = (journal: StubStream, id: string) =>
            flush(() => {
                journal.open();
                journal.send(journalLine({ id, kind: 'approval_requested' }));
            });
        const inbox = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply');
        const paged = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-first-page', 'approval.list/needs-next-page')
                .answer('decision.list', 'decision.list/empty')
                .answerAnyRequest('approval.get', (body) =>
                    (body as { approval_id?: string }).approval_id === 'apr_102'
                        ? 'approval.get/needs-page-two'
                        : 'approval.get/needs-message',
                );
        const more = () => fireEvent.click(within(needs()).getByRole('button', { name: 'Show more' }));

        it('keeps the page Show more read through a reload, with the item chosen on it', async () => {
            const journal = stubStream();
            const fake = paged();
            await renderConsole(fake, journal);
            await waitFor(() => expect(chips()[0]).toBe('All 100+'));
            more();
            await waitFor(() => expect(chips()[0]).toBe('All 102'));
            await choose('apr_103');
            const asked = sent(fake, 'approval.list').length;

            await line(journal, 'jrn_needs_1');
            await waitFor(() => expect(sent(fake, 'approval.list').length).toBe(asked + 2));
            await settle();
            expect(sent(fake, 'approval.list').at(-1)).toEqual(fixtures['approval.list/needs-next-page'].request);
            expect(chips()[0]).toBe('All 102');
            expect(words(badge())).toBe('102');
            expect(pane().dataset['id']).toBe('apr_103');
            expect(row('apr_103').getAttribute('aria-current')).toBe('true');
        });

        it('reads the second page again after an act on it, keeps the rest of it, and puts the keyboard on the next line', async () => {
            const fake = paged()
                .answer(
                    'approval.list',
                    'approval.list/needs-first-page',
                    'approval.list/needs-next-page',
                    'approval.list/needs-next-page-after',
                )
                .answer('workitem.get', 'workitem.get/needs-page-two-parked')
                .answer('approval.approve', 'approval.approve/needs-page-two-approved');
            await renderConsole(fake);
            await waitFor(() => expect(chips()[0]).toBe('All 100+'));
            more();
            await waitFor(() => expect(chips()[0]).toBe('All 102'));
            await choose('apr_102');
            const approve = await waitFor(() => {
                const button = within(pane()).getByRole('button', { name: 'Approve' });
                expect(button.hasAttribute('disabled')).toBe(false);
                return button;
            });
            // A browser puts the keyboard on the button pressed; the button leaves with its item.
            approve.focus();
            fireEvent.click(approve);
            await waitFor(() =>
                expect(sent(fake, 'approval.approve')).toEqual([
                    asGiven(fixtures['approval.approve/needs-page-two-approved'].request),
                ]),
            );
            await waitFor(() => expect(needs().querySelector('[data-row="apr_102"]')).toBeNull());
            await settle();
            expect(chips()[0]).toBe('All 101');
            expect(pane().dataset['id']).toBe('apr_103');
            expect(document.activeElement).toBe(row('apr_103'));
            expect(needs().querySelector('.needs-left')).toBeNull();
        });

        it('shows the next item, and says why, when the one chosen was decided elsewhere', async () => {
            const journal = stubStream();
            const fake = inbox();
            await renderConsole(fake, journal);
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_1'));
            fake.answer('approval.list', 'approval.list/needs-after-edit');
            await line(journal, 'jrn_needs_2');
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            expect(words(needs().querySelector('.needs-left')!)).toBe(
                'What you had open is no longer waiting: it was decided or withdrawn elsewhere. This is the next one.',
            );
            fireEvent.click(row('apr_2'));
            await waitFor(() => expect(needs().querySelector('.needs-left')).toBeNull());
        });

        it('stays on the question shown when an approval arrives above it', async () => {
            const journal = stubStream();
            const fake = new FakeApi()
                .answer('approval.list', 'approval.list/empty')
                .answer('decision.list', 'decision.list/needs-questions')
                .answer('decision.get', 'decision.get/needs-question')
                .answer('approval.get', 'approval.get/needs-reply');
            await renderConsole(fake, journal);
            await waitFor(() => expect(pane().dataset['id']).toBe('dec_1'));
            fake.answer('approval.list', 'approval.list/needs-after-edit');
            await line(journal, 'jrn_needs_3');
            await waitFor(() => expect(row('apr_2')).toBeTruthy());
            await settle();
            expect(pane().dataset['id']).toBe('dec_1');
            expect(needs().querySelector('.needs-left')).toBeNull();
        });

        it('lets a question its own read answered late change neither the filter nor the agent', async () => {
            const fake = inbox()
                .answer('decision.list', 'decision.list/needs-questions')
                .answer('decision.get', 'decision.get/needs-question');
            const release = fake.hold('decision.get');
            await renderConsole(fake);
            await waitFor(() => expect(row('dec_1')).toBeTruthy());
            await choose('dec_1');
            show('Approvals');
            await waitFor(() => expect(pressed()).toEqual(['Approvals 2']));
            await flush(async () => release());
            await settle();
            expect(pressed()).toEqual(['Approvals 2']);
            expect(pane().dataset['id']).toBe('apr_1');
        });

        it('closes Reject’s question when its item leaves, and sends no reject', async () => {
            const journal = stubStream();
            const fake = inbox();
            await renderConsole(fake, journal);
            // The pane is painted again as its read answers, so the button is found once the reads are in.
            await waitFor(() => expect(Object.keys(reads.approvals.value).length).toBeGreaterThan(0));
            await settle();
            fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
            const asked = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
            // The question takes the keyboard on Cancel, neither on its optional reason, a field of one line, nor on
            // Reject: Enter as it opens rejects nothing.
            expect(within(asked).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }).tagName).toBe(
                'INPUT',
            );
            expect(document.activeElement).toBe(within(asked).getByRole('button', { name: 'Cancel' }));
            fake.answer('approval.list', 'approval.list/needs-after-edit');
            await line(journal, 'jrn_needs_4');
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            expect(needs().querySelector('[role="alertdialog"]')).toBeNull();
            expect(sent(fake, 'approval.reject')).toEqual([]);
        });

        it('gives the keyboard back to Reject when its question is cancelled', async () => {
            await renderConsole(inbox());
            // The pane is painted again as its read answers, so the button is found once the reads are in.
            await waitFor(() => expect(Object.keys(reads.approvals.value).length).toBeGreaterThan(0));
            await settle();
            fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
            const asked = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
            fireEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
            await waitFor(() => expect(within(pane()).queryByRole('alertdialog')).toBeNull());
            expect(document.activeElement).toBe(within(pane()).getByRole('button', { name: 'Reject' }));
        });

        /** Two first messages of one campaign, each read whole; the first is approved and the second is left. */
        const two = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/needs-two', 'approval.list/needs-two-after')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/needs-two-first', 'approval.get/needs-two-second')
                .answer('workitem.get', 'workitem.get/needs-two-first-parked')
                .answer('approval.approve', 'approval.approve/needs-two-first-approved');
        const approveIn = () => within(pane()).getByRole('button', { name: 'Approve' });
        const ready = () =>
            waitFor(() => {
                expect(Object.keys(reads.approvals.value)).toHaveLength(2);
                expect(approveIn().hasAttribute('disabled')).toBe(false);
            });

        it('takes the first click of a double click on Approve alone, so the second never lands on the next item', async () => {
            const fake = two();
            await renderConsole(fake);
            await ready();
            await settle();
            fireEvent.click(approveIn(), { detail: 1 });
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            await settle();
            // The next item's Approve is live where the first one was; the second click of the same double click lands on it.
            expect(approveIn().hasAttribute('disabled')).toBe(false);
            fireEvent.click(approveIn(), { detail: 2 });
            await settle();
            expect(sent(fake, 'approval.approve')).toEqual([
                asGiven(fixtures['approval.approve/needs-two-first-approved'].request),
            ]);
        });

        it('holds the pane’s acts from the press of Approve until the lists read after it have answered', async () => {
            const fake = two();
            await renderConsole(fake);
            await ready();
            await settle();
            const release = fake.hold('approval.list');
            fireEvent.click(approveIn());
            await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
            await settle();
            expect(pane().dataset['id']).toBe('apr_1');
            expect(approveIn().hasAttribute('disabled')).toBe(true);
            expect(within(pane()).getByRole('button', { name: 'Reject' }).hasAttribute('disabled')).toBe(true);
            await flush(async () => release());
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            await waitFor(() => expect(approveIn().hasAttribute('disabled')).toBe(false));
        });

        it('leaves the keyboard on a field the person moved to while the act was under way', async () => {
            const fake = two();
            await renderConsole(fake);
            await ready();
            await settle();
            const release = fake.hold('approval.list');
            fireEvent.click(approveIn());
            await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
            const picker = needs().querySelector<HTMLSelectElement>('.needs-agent')!;
            picker.focus();
            await flush(async () => release());
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            await settle();
            expect(document.activeElement).toBe(needs().querySelector('.needs-agent'));
        });

        it('drops an older read of the lists that answers after a newer one', async () => {
            const fake = two();
            await renderConsole(fake);
            await waitFor(() => expect(row('apr_1')).toBeTruthy());
            // The older read is held and takes the list that still holds the first; the newer takes the one without it.
            fake.answer('approval.list', 'approval.list/needs-two-after', 'approval.list/needs-two');
            let calls = 0;
            const release = fake.hold('approval.list', () => ++calls === 1);
            const older = reload();
            await flush(() => reload());
            await waitFor(() => expect(needs().querySelector('[data-row="apr_1"]')).toBeNull());
            await flush(async () => {
                release();
                await older;
            });
            await settle();
            expect(needs().querySelector('[data-row="apr_1"]')).toBeNull();
            expect(chips()[0]).toBe('All 1');
        });

        it('holds the pane, and says nothing of an item leaving, when a reload starts during the act’s own read and answers after it', async () => {
            const fake = two();
            await renderConsole(fake);
            await ready();
            await settle();
            // The act's own read of the lists waits on its approvals; the reload started meanwhile waits on its questions.
            let approvalReads = 0;
            let questionReads = 0;
            const actsRead = fake.hold('approval.list', () => ++approvalReads === 1);
            const reloadsRead = fake.hold('decision.list', () => ++questionReads === 2);
            fireEvent.click(approveIn());
            await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
            await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(1));
            const reloading = reload();
            await waitFor(() => expect(sent(fake, 'decision.list').length).toBeGreaterThan(2));

            // The act's read answers first and is not the newest: the pane stays held on the item until the newest answers.
            await flush(async () => actsRead());
            await settle();
            expect(pane().dataset['id']).toBe('apr_1');
            expect(approveIn().hasAttribute('disabled')).toBe(true);
            expect(needs().querySelector('.needs-left')).toBeNull();

            await flush(async () => {
                reloadsRead();
                await reloading;
            });
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            await settle();
            expect(needs().querySelector('.needs-left')).toBeNull();
            expect(sent(fake, 'approval.approve')).toHaveLength(1);
        });

        it('says nothing of an item leaving when a link opened one on a page not read yet', async () => {
            const journal = stubStream();
            const fake = paged();
            await renderConsole(fake, journal);
            await waitFor(() => expect(chips()[0]).toBe('All 100+'));
            await flush(() => attention.open(fake, 'approvals', 'apr_102'));
            await line(journal, 'jrn_needs_5');
            await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(1));
            await settle();
            expect(needs().querySelector('.needs-left')).toBeNull();
        });

        it('keeps the lists whole when a page fails midway through being read again', async () => {
            const journal = stubStream();
            const fake = paged();
            await renderConsole(fake, journal);
            await waitFor(() => expect(chips()[0]).toBe('All 100+'));
            more();
            await waitFor(() => expect(chips()[0]).toBe('All 102'));
            fake.unreachable('approval.list', (body) => 'cursor' in body);
            const asked = sent(fake, 'approval.list').length;
            await line(journal, 'jrn_needs_6');
            await waitFor(() => expect(sent(fake, 'approval.list').length).toBe(asked + 2));
            await settle();
            expect(chips()[0]).toBe('All 102');
            expect(row('apr_103')).toBeTruthy();
        });

        it('closes Reject’s question on Escape and gives the keyboard back to Reject', async () => {
            await renderConsole(inbox());
            await waitFor(() => expect(Object.keys(reads.approvals.value).length).toBeGreaterThan(0));
            await settle();
            fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
            const asked = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
            fireEvent.keyDown(within(asked).getByRole('textbox'), { key: 'Escape' });
            await waitFor(() => expect(within(pane()).queryByRole('alertdialog')).toBeNull());
            expect(document.activeElement).toBe(within(pane()).getByRole('button', { name: 'Reject' }));
        });

        it('names no plugin in the pane’s header, and says what the act is in plain words', async () => {
            await renderConsole(inbox());
            await waitFor(() => expect(pane().querySelector('.needs-facts')).toBeTruthy());
            expect(words(pane().querySelector('.ask-h .meta')!)).toMatch(/^Spring launch · [^·]+$/);
            expect(words(pane().querySelector('.ask-h')!)).not.toMatch(/\breply\b/);
            // A message's cost depends on its channel, so its line is the one sentence for message.send's cost.
            expect([...pane().querySelectorAll('.needs-facts .meta')].map((each) => each.textContent)).toEqual([
                'Contacts the person: something is sent or shown to them',
                'Cannot be undone',
                'Spends your message allowance on channels that meter sending; costs nothing on the others.',
            ]);
            expect(pane().querySelector('.needs-facts')!.textContent).not.toContain('`');
            expect(pane().querySelectorAll('.needs-facts code')).toHaveLength(0);
        });
    });

    describe('edits a message as the person’s own words, and approves nothing', () => {
        /** The message's work item as the runtime holds it: parked for the approval, then queued again once it is saved. */
        const edits = () => {
            const fake = new FakeApi()
                .answer('approval.list', 'approval.list/needs-inbox')
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/needs-message', 'approval.get/needs-reply')
                .answer('workitem.update', 'workitem.update/needs-message-edited');
            return fake.answerAnyRequest('workitem.get', () =>
                sent(fake, 'workitem.update').length > 0
                    ? 'workitem.get/needs-message-requeued'
                    : 'workitem.get/needs-message-parked',
            );
        };

        /**
         * Presses Edit once the pane has painted what it reads for the approval, the sending name last: a paint
         * replaces the pane's buttons, so a press is made on the one drawn last.
         */
        async function pressEdit(): Promise<void> {
            await waitFor(() => within(pane()).getByText(/^Sending name at Reply now:/));
            fireEvent.click(within(pane()).getByRole('button', { name: 'Edit' }));
        }

        it('opens the boxes on the words the work item holds, saves them through the work item, and keeps the row where it was with them', async () => {
            const fake = edits();
            await renderConsole(fake);
            await pressEdit();
            const body = await waitFor(() => within(pane()).getByRole('textbox', { name: 'Body' }));
            expect((body as HTMLTextAreaElement).value).toBe('Hi Ines, thanks for the talk at the meetup.');
            expect((within(pane()).getByRole('textbox', { name: 'Subject' }) as HTMLInputElement).value).toBe(
                'Notes from the meetup',
            );
            expect(sent(fake, 'workitem.get')).toEqual([{ work_item_id: 'wi_1' }]);

            // The pane is painted again as the check ends, so the box is found afresh before it is typed into.
            await settle();
            type(
                within(pane()).getByRole('textbox', { name: 'Body' }),
                'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?',
            );
            fake.answer(
                'approval.list',
                'approval.list/needs-after-edit',
                'approval.list/needs-message-all-after-edit',
            );
            fireEvent.click(within(pane()).getByRole('button', { name: 'Save' }));
            await waitFor(() =>
                expect(sent(fake, 'workitem.update')).toEqual([
                    asGiven(fixtures['workitem.update/needs-message-edited'].request),
                ]),
            );
            // The runtime lists the reply alone now; the edited row stays first, chosen, and says where it stands.
            await waitFor(() => within(pane()).getByText(QUEUED_SENTENCE));
            expect([...needs().querySelectorAll<HTMLElement>('.needs-row')].map((each) => each.dataset['row'])).toEqual(
                ['apr_1', 'apr_2'],
            );
            expect(pane().dataset['id']).toBe('apr_1');
            expect(words(row('apr_1'))).toContain(ASKING_MARK);
            expect(words(pane().querySelector('.needs-body')!)).toBe(
                'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?',
            );
            // No act on the withdrawn approval: Approve, Edit and Reject come back with the approval asked again.
            for (const name of ['Approve', 'Edit', 'Reject'])
                expect(within(pane()).queryByRole('button', { name })).toBeNull();
            expect(within(pane()).getByRole('button', { name: 'Open the chat' })).toBeTruthy();
            // The list's own count does not fall because of the edit.
            expect(chips()[0]).toBe('All 2');
            expect(needs().querySelector('.needs-left')).toBeNull();
            expect(sent(fake, 'approval.approve')).toEqual([]);
            // The keyboard is on the line the pane shows, never on a button.
            expect(document.activeElement).toBe(row('apr_1'));
        });

        it('lets the kept row go as one decided elsewhere once its work ended before Jason asked again', async () => {
            const fake = edits();
            await renderConsole(fake);
            await pressEdit();
            await waitFor(() => within(pane()).getByRole('textbox', { name: 'Body' }));
            await settle();
            type(
                within(pane()).getByRole('textbox', { name: 'Body' }),
                'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?',
            );
            fake.answer(
                'approval.list',
                'approval.list/needs-after-edit',
                'approval.list/needs-message-all-after-edit',
            );
            fireEvent.click(within(pane()).getByRole('button', { name: 'Save' }));
            await waitFor(() => within(pane()).getByText(QUEUED_SENTENCE));

            // The work is cancelled elsewhere before the runtime asked again: the next read of the list reads the item
            // ended, and the row goes; the pane says what it said of a row decided elsewhere and shows the next one.
            fake.answerAnyRequest('workitem.get', 'workitem.get/needs-message-cancelled');
            fake.answer('approval.list', 'approval.list/needs-after-edit', 'approval.list/needs-message-all-cancelled');
            await flush(() => reload());
            await waitFor(() =>
                expect(
                    [...needs().querySelectorAll<HTMLElement>('.needs-row')].map((each) => each.dataset['row']),
                ).toEqual(['apr_2']),
            );
            expect(words(needs().querySelector('.needs-left')!)).toMatch(/^What you had open is no longer waiting/);
            expect(chips()[0]).toBe('All 1');
            expect(sent(fake, 'approval.approve')).toEqual([]);
        });

        it('says before Save that saving approves nothing', async () => {
            await renderConsole(edits());
            await pressEdit();
            await waitFor(() => within(pane()).getByRole('textbox', { name: 'Body' }));
            expect(words(pane().querySelector('.needs-save-says')!)).toBe(
                'Saving changes the words only and approves nothing: Jason asks you again.',
            );
        });

        it('writes nothing and says nothing of asking again when Save is pressed on the words as they were', async () => {
            const fake = edits();
            await renderConsole(fake);
            await pressEdit();
            await waitFor(() => within(pane()).getByRole('textbox', { name: 'Body' }));
            await settle();
            fireEvent.click(within(pane()).getByRole('button', { name: 'Save' }));
            await waitFor(() => expect(within(pane()).queryByRole('textbox', { name: 'Body' })).toBeNull());
            await settle();
            expect(sent(fake, 'workitem.update')).toEqual([]);
            expect(within(pane()).queryByText(QUEUED_SENTENCE)).toBeNull();
            expect(within(pane()).getByRole('button', { name: 'Approve' })).toBeTruthy();
        });

        it('closes the boxes on Cancel having written nothing', async () => {
            const fake = edits();
            await renderConsole(fake);
            await pressEdit();
            await waitFor(() => within(pane()).getByRole('textbox', { name: 'Body' }));
            fireEvent.click(within(pane()).getByRole('button', { name: 'Cancel' }));
            await waitFor(() => expect(within(pane()).queryByRole('textbox', { name: 'Body' })).toBeNull());
            expect(sent(fake, 'workitem.update')).toEqual([]);
        });
    });

    describe('keeps a message edited on this page until Jason asks again, then becomes the new approval in place', () => {
        // One story the runtime recorded: a message waiting, its text edited, the approval withdrawn, the item queued
        // again, and the claim asking again with the edited text under a new approval of the same work item.
        let listed: FixtureName = 'approval.list/card-message-all-pending';
        let item: FixtureName = 'workitem.get/card-message-parked';
        let ofItem: FixtureName = 'approval.list/card-message-item-after-edit';
        let allOfItem: FixtureName = 'approval.list/card-message-item-all-after-edit';
        const everyStatus = (body: object) => ((body as { statuses?: string[] }).statuses ?? []).length > 1;
        const story = () =>
            new FakeApi()
                .answerAnyRequest('approval.list', (body) =>
                    'work_item_id' in body ? (everyStatus(body) ? allOfItem : ofItem) : listed,
                )
                .answer('decision.list', 'decision.list/empty')
                .answer('approval.get', 'approval.get/card-message', 'approval.get/card-message-reasked')
                .answerAnyRequest('workitem.get', () => item)
                .answer('approval.approve', 'approval.approve/card-message-reasked');
        const edited = 'Hi Ines, thanks for the talk at the meetup. Shall we talk next week?';
        const live = (button: HTMLElement) => !button.hasAttribute('disabled');
        const asked = (journal: StubStream, id: string) =>
            flush(() => {
                journal.open();
                journal.send(journalLine({ id, kind: 'approval_requested' }));
            });
        beforeEach(() => {
            listed = 'approval.list/card-message-all-pending';
            item = 'workitem.get/card-message-parked';
            ofItem = 'approval.list/card-message-item-after-edit';
            allOfItem = 'approval.list/card-message-item-all-after-edit';
        });

        it('stays, chosen, with the words saved, and becomes the approval asked again, which Approve then approves', async () => {
            const journal = stubStream();
            const fake = story();
            await renderConsole(fake, journal);
            await waitFor(() => expect(live(within(pane()).getByRole('button', { name: 'Approve' }))).toBe(true));
            expect(pane().dataset['id']).toBe('apr_1');

            // The edit as the page saved it (the save itself is the test above): the approval withdrawn, the item queued.
            noteEdit(
                fixtures['approval.get/card-message'].response as unknown as Approval,
                fixtures['workitem.update/card-message-edited'].response as unknown as WorkItem,
            );
            listed = 'approval.list/card-message-all-after-edit';
            item = 'workitem.get/card-message-requeued';
            await flush(() => reload());
            await waitFor(() => within(pane()).getByText(QUEUED_SENTENCE));
            expect(pane().dataset['id']).toBe('apr_1');
            expect(words(pane().querySelector('.needs-body')!)).toBe(edited);
            expect(within(pane()).queryByRole('button', { name: 'Approve' })).toBeNull();
            expect(chips()[0]).toBe('All 1');

            // The runtime asks again: the row is the new approval, where the old one stood, still chosen, and Approve
            // approves the new one once it has been read whole and checked against its work item.
            listed = 'approval.list/card-message-all-reasked';
            item = 'workitem.get/card-message-reparked';
            ofItem = 'approval.list/card-message-item-reasked';
            allOfItem = 'approval.list/card-message-item-all-reasked';
            await asked(journal, 'jrn_reasked_1');
            await waitFor(() => expect(pane().dataset['id']).toBe('apr_2'));
            expect([...needs().querySelectorAll<HTMLElement>('.needs-row')].map((each) => each.dataset['row'])).toEqual(
                ['apr_2'],
            );
            expect(needs().querySelector('.needs-left')).toBeNull();
            expect(within(pane()).queryByText(QUEUED_SENTENCE)).toBeNull();
            const approve = await waitFor(() => {
                const button = within(pane()).getByRole('button', { name: 'Approve' });
                expect(live(button)).toBe(true);
                return button;
            });
            expect(words(pane().querySelector('.needs-body')!)).toBe(edited);
            fireEvent.click(approve);
            await waitFor(() =>
                expect(sent(fake, 'approval.approve')).toEqual([
                    asGiven(fixtures['approval.approve/card-message-reasked'].request),
                ]),
            );
        });

        it('acts on nothing while it asks again, even when Approve of the withdrawn approval is pressed by a stale paint', async () => {
            const fake = story();
            await renderConsole(fake);
            await waitFor(() => expect(live(within(pane()).getByRole('button', { name: 'Approve' }))).toBe(true));
            noteEdit(
                fixtures['approval.get/card-message'].response as unknown as Approval,
                fixtures['workitem.update/card-message-edited'].response as unknown as WorkItem,
            );
            expect(await attention.approve(here().api, 'apr_1')).toBe(false);
            expect(await attention.reject(here().api, 'apr_1')).toBe(false);
            expect(sent(fake, 'approval.approve')).toEqual([]);
            expect(sent(fake, 'approval.reject')).toEqual([]);
        });
    });

    it('rejects only once the person confirms, with the reason typed', async () => {
        const fake = messages().answer('approval.reject', 'approval.reject/rejected');
        await renderConsole(fake);
        await choose('apr_2');
        fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
        const asked = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
        fireEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
        await waitFor(() => expect(within(pane()).queryByRole('alertdialog')).toBeNull());

        fireEvent.click(within(pane()).getByRole('button', { name: 'Reject' }));
        const again = await waitFor(() => within(pane()).getByRole('alertdialog', { name: 'Reject this?' }));
        type(within(again).getByRole('textbox'), 'not this quarter');
        expect(sent(fake, 'approval.reject')).toEqual([]);
        fireEvent.click(within(again).getByRole('button', { name: 'Reject' }));
        await waitFor(() =>
            expect(sent(fake, 'approval.reject')).toEqual([asGiven(fixtures['approval.reject/rejected'].request)]),
        );
    });

    describe('approves only what it has read whole', () => {
        /** The pane showing this approval: chosen from the list when another is shown. */
        const at = (id: string) => {
            if (pane()?.dataset['id'] !== id) fireEvent.click(row(id));
            return pane();
        };
        const approve = (id: string) => within(at(id)).getByRole('button', { name: 'Approve' });
        const readAgain = (id: string) => within(at(id)).getByRole('button', { name: 'Read again' });
        const live = (button: HTMLElement) => !button.hasAttribute('disabled');
        const sent = (fake: FakeApi, operation: string) =>
            fake.calls.filter((each) => each.operation === operation).map((each) => each.body);
        const reads = (fake: FakeApi, id: string) =>
            sent(fake, 'approval.get').filter((body) => (body as { approval_id: string }).approval_id === id);
        /** Lets every answer the fake gave be taken in: its answers are promises, all settled within one task. */
        const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        /** A journal line, as the runtime writes one when something waits: the console reads what waits again. */
        const line = (journal: StubStream, id: string) =>
            flush(() => {
                journal.open();
                journal.send(journalLine({ id, kind: 'approval_requested' }));
            });
        const messages = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/messages-pending')
                .answer('decision.list', 'decision.list/empty');

        it('holds Approve on a doubted message until the doubt is drawn on its row, above Approve', async () => {
            const fake = messages().answer(
                'approval.get',
                'approval.get/park-doubted',
                'approval.get/linkedin-message',
            );
            const release = fake.hold(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            const { doubts } = fixtures['approval.get/park-doubted'].response.preview as { doubts: { note: string }[] };
            const doubted = `The critic role flagged this: ${whyWords('unsupported_claim')}`;
            // Before the whole read Approve is held and there is no doubt to draw; the moment it is live, the doubt stands.
            expect(live(approve('apr_1'))).toBe(false);
            expect(within(at('apr_1')).queryByText(doubted)).toBeNull();
            release();
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            const doubt = within(at('apr_1')).getByText(doubted);
            expect(within(at('apr_1')).getByText(`Their words: ${doubts[0]!.note}`)).toBeTruthy();
            expect(doubt.compareDocumentPosition(approve('apr_1')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        });

        it('says why a message it could not read cannot be approved, offers Read again, and reads it again at the next reload', async () => {
            const journal = stubStream();
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const reachable = fake.unreachable(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake, journal);
            await waitFor(() => expect(row('apr_1')).toBeTruthy());
            const said = await waitFor(() =>
                within(at('apr_1')).getByText(/could not be read, so it cannot be approved here/),
            );
            expect(words(said)).toContain('Failed to fetch');
            expect(live(approve('apr_1'))).toBe(false);
            expect(live(readAgain('apr_1'))).toBe(true);
            fireEvent.click(approve('apr_1'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);

            reachable();
            line(journal, 'jrn_301');
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true), { timeout: 3000 });
            expect(within(at('apr_1')).queryByText(/could not be read/)).toBeNull();
        });

        // The clock is a fake one that also moves on its own, so waiting for the page still works and the limit is jumped.
        it('says a message is being read while its read is unanswered, does not ask again while it is, and asks once at the next reload after its time ran out', async () => {
            vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'], shouldAdvanceTime: true });
            const journal = stubStream();
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const release = fake.hold(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake, journal);
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            expect(words(at('apr_1'))).toContain('cannot be approved until it has been read whole');
            expect(words(at('apr_1'))).not.toContain('shown here');
            expect(live(approve('apr_1'))).toBe(false);
            expect(within(at('apr_1')).queryByRole('button', { name: 'Read again' })).toBeNull();
            fireEvent.click(approve('apr_1'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);
            expect(reads(fake, 'apr_1')).toHaveLength(1);

            const listed = sent(fake, 'approval.list').length;
            line(journal, 'jrn_302');
            await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(listed), { timeout: 3000 });
            await settle();
            expect(reads(fake, 'apr_1')).toHaveLength(1);

            await flush(() => vi.advanceTimersByTimeAsync(READ_LIMIT_MS));
            expect(words(at('apr_1'))).toContain(
                'This approval could not be read, so it cannot be approved here; Read again asks for it once more. The runtime did not answer within 30 seconds.',
            );
            expect(words(at('apr_1'))).not.toContain('The runtime said');
            expect(live(approve('apr_1'))).toBe(false);
            expect(live(readAgain('apr_1'))).toBe(true);
            expect(reads(fake, 'apr_1')).toHaveLength(1);
            line(journal, 'jrn_303');
            await waitFor(() => expect(reads(fake, 'apr_1')).toHaveLength(2), { timeout: 3000 });
            await settle();
            expect(reads(fake, 'apr_1')).toHaveLength(2);
            release();
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
        });

        const enrolments = () =>
            new FakeApi()
                .answer('approval.list', 'approval.list/pending')
                .answer('decision.list', 'decision.list/empty');

        // The rule is the row's, not a list of kinds: whatever an approval does, Approve waits for its whole read.
        it('holds Approve on an enrolment it could not read, says why, offers Read again, and approves nothing', async () => {
            const fake = enrolments().answer('approval.get', 'approval.get/pending-with-preview');
            fake.unreachable('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            const said = await waitFor(() =>
                within(at('apr_2')).getByText(/could not be read, so it cannot be approved here/),
            );
            expect(words(said)).toContain('Read again asks for it once more');
            expect(words(said)).toContain('Failed to fetch');
            expect(live(approve('apr_2'))).toBe(false);
            expect(live(readAgain('apr_2'))).toBe(true);
            fireEvent.click(approve('apr_2'));
            await flush(() => Promise.resolve());
            expect(sent(fake, 'approval.approve')).toEqual([]);
        });

        it('holds Approve on an enrolment while its read is unanswered, and says so', async () => {
            const fake = enrolments().answer(
                'approval.get',
                'approval.get/pending-with-preview',
                'approval.get/other-pending',
            );
            const release = fake.hold(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_2',
            );
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            expect(words(at('apr_2'))).toContain('cannot be approved until it has been read whole');
            expect(words(at('apr_2'))).not.toContain('shown here');
            expect(live(approve('apr_2'))).toBe(false);
            expect(within(at('apr_2')).queryByRole('button', { name: 'Read again' })).toBeNull();
            release();
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            expect(words(at('apr_2'))).not.toContain('cannot be approved');
        });

        it('draws the doubts of an enrolment a person was asked to look at, as it draws a message’s', async () => {
            const fake = enrolments().answer('approval.get', 'approval.get/with-doubts', 'approval.get/other-pending');
            await renderConsole(fake);
            const asked = await waitFor(() =>
                within(at('apr_1')).getByText(`A person asked you to look at this: ${whyWords('sensitive_topic')}`),
            );
            expect(within(at('apr_1')).getByText('Their words: She asks about a price.')).toBeTruthy();
            expect(live(approve('apr_1'))).toBe(true);
            expect(asked.compareDocumentPosition(approve('apr_1')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
            expect(row('apr_2').querySelector('.send-doubts')).toBeNull();
        });

        it('approves a message it has read through approval.approve, after checking it against its work item', async () => {
            const fake = messages()
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message')
                .answer('workitem.get', 'workitem.get/parked-send')
                .answer('approval.approve', 'approval.approve/approved');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            fake.answer('approval.list', 'approval.list/empty');
            fireEvent.click(approve('apr_1'));
            await waitFor(() => expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]));
            expect(sent(fake, 'workitem.get')).toEqual([{ work_item_id: 'wi_1' }]);
            await waitFor(() => expect(needs().querySelector('[data-row="apr_1"]')).toBeNull());
        });

        it('does not approve a message whose text moved since it was asked, and says so on its row', async () => {
            const fake = messages()
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message')
                .answer('workitem.get', 'workitem.get/parked-edited-send');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            fireEvent.click(approve('apr_1'));
            await waitFor(() => within(at('apr_1')).getByText(/changed after this approval was asked/));
            expect(sent(fake, 'approval.approve')).toEqual([]);
            expect(live(approve('apr_1'))).toBe(true);
        });

        it('approves an approval whose own read failed once Read again has read it whole', async () => {
            const fake = messages().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/linkedin-message',
            );
            const reachable = fake.unreachable(
                'approval.get',
                (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
            );
            await renderConsole(fake);
            await waitFor(() => within(at('apr_1')).getByText(/could not be read/));
            reachable();
            fireEvent.click(readAgain('apr_1'));
            await waitFor(() => within(at('apr_1')).getByText('Notes from the meetup'));
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            expect(within(at('apr_1')).queryByText(/could not be read/)).toBeNull();
        });

        it('says on the row being checked that it is, and holds every act of the pane until the press has ended', async () => {
            const fake = messages()
                .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message')
                .answer('workitem.get', 'workitem.get/parked-send')
                .answer('approval.approve', 'approval.approve/approved');
            const release = fake.hold('workitem.get');
            await renderConsole(fake);
            await waitFor(() => expect(live(approve('apr_2'))).toBe(true));
            await waitFor(() => expect(live(approve('apr_1'))).toBe(true));
            fireEvent.click(approve('apr_1'));
            await waitFor(() => expect(sent(fake, 'workitem.get')).toHaveLength(1));
            await settle();
            expect(live(approve('apr_1'))).toBe(false);
            expect(words(at('apr_1'))).toContain('This message is being checked against its work item');
            // Another item chosen meanwhile is not being checked, and its Approve waits for the press under way to end.
            expect(words(at('apr_2'))).not.toContain('being checked');
            expect(live(approve('apr_2'))).toBe(false);
            fake.answer('approval.list', 'approval.list/empty');
            release();
            await waitFor(() => expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]));
            await waitFor(() => expect(needs().querySelector('[data-row="apr_1"]')).toBeNull());
        });

        it('reads at most eight rows whole at once, and reads every row in turn', async () => {
            const fake = new FakeApi().answerAnyRequest('approval.get', 'approval.get/email-message');
            const release = fake.hold('approval.get');
            const store = attentionStore();
            const [first] = fixtures['approval.list/messages-pending'].response.items as ApprovalSummary[];
            store.approvals.value = Array.from({ length: 20 }, (_, index) => ({ ...first!, id: `apr_${index + 1}` }));
            const filled = fillApprovals(fake, wholeReads('long-list', store));
            await settle();
            expect(sent(fake, 'approval.get')).toHaveLength(8);
            release();
            await flush(() => filled);
            expect(sent(fake, 'approval.get').map((body) => (body as { approval_id: string }).approval_id)).toEqual(
                Array.from({ length: 20 }, (_, index) => `apr_${index + 1}`),
            );
        });

        // The limit, staged on a fake clock: the first read is held past it, the read sent after it answers at once, and
        // the first answers last. Only the read under way may write; one whose time ran out writes nothing, ever.
        it('lets a read unanswered within the limit hold its row, counts it not answered after, sends it once again, and keeps no late answer', async () => {
            vi.useFakeTimers();
            const fake = new FakeApi().answer(
                'approval.get',
                'approval.get/email-message',
                'approval.get/park-doubted',
            );
            let calls = 0;
            const release = fake.hold('approval.get', () => ++calls === 1);
            const store = attentionStore();
            const [first] = fixtures['approval.list/messages-pending'].response.items as ApprovalSummary[];
            store.approvals.value = [first!];
            const held = wholeReads('limit', store);
            let filled = false;
            void fillApprovals(fake, held).then(() => (filled = true));
            await vi.advanceTimersByTimeAsync(READ_LIMIT_MS - 1);
            await fillApprovals(fake, held);
            expect(reads(fake, 'apr_1')).toHaveLength(1);
            expect(held.unread.value).toEqual({});
            expect(filled).toBe(false);

            await vi.advanceTimersByTimeAsync(1);
            expect(filled).toBe(true);
            expect(held.unread.value['apr_1']).toMatchObject({
                code: null,
                message: 'The runtime did not answer within 30 seconds.',
                unanswered: true,
            });
            expect(held.approvals.value).toEqual({});

            await fillApprovals(fake, held);
            expect(reads(fake, 'apr_1')).toHaveLength(2);
            expect(held.approvals.value['apr_1']).toEqual(fixtures['approval.get/email-message'].response);
            expect(held.unread.value).toEqual({});

            release();
            await vi.advanceTimersByTimeAsync(0);
            expect(held.approvals.value['apr_1']).toEqual(fixtures['approval.get/email-message'].response);
            expect(held.unread.value).toEqual({});
        });

        it('never makes Approve live on an answer that came after its time ran out', async () => {
            vi.useFakeTimers();
            const fake = new FakeApi().answer('approval.get', 'approval.get/email-message');
            const release = fake.hold('approval.get');
            const store = attentionStore();
            const [first] = fixtures['approval.list/messages-pending'].response.items as ApprovalSummary[];
            store.approvals.value = [first!];
            const held = wholeReads('late', store);
            void fillApprovals(fake, held);
            await vi.advanceTimersByTimeAsync(READ_LIMIT_MS);
            release();
            await vi.advanceTimersByTimeAsync(0);
            expect(held.approvals.value).toEqual({});
            expect(held.unread.value['apr_1']?.unanswered).toBe(true);
        });
    });
});
