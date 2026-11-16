import { cleanup, act as flush, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { posix } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { pendingMark } from '../shell/attention-mark';
import { reasonLabel } from '../screens/attention/labels';
import { whyWords } from '../screens/send-modes/doubts';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, journalLine, stubStream, type StubDesktop, type StubStream } from '../test/desktop';
import { shown } from '../test/area';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures, type FixtureName } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, conversations } from './state';
import { READ_LIMIT_MS, forgetWaiting } from './waiting';

// An agent's chat draws, under its conversation, the campaign's approvals and questions that wait on a person: read
// with `approval.list` and `decision.list` of that campaign, decided through the calls Needs you makes, and gone once
// the next read no longer lists them. Nothing the AI writes in the chat draws a card.

const spring = 'conv_01JDKEPT0000000000000000W1';
const summer = 'conv_01JDKEPT0000000000000000W3';

/** What the runtime lists for the chat's campaign: its recorded pending ones, until a test says they were decided. */
let springApprovals: FixtureName = 'approval.list/by-campaign-pending';
/** What the AI said in Spring's chat, besides the person's question. */
let springSays: string | null = null;
/** Whether anything waits outside the chats' campaigns, in the lists Needs you reads. */
let elsewhere: 'pending' | 'nothing' = 'pending';

/** The console over a fake that knows Spring's pending approvals and questions, with Spring's and Summer's chats kept. */
function renderConsole(
    fake: FakeApi = new FakeApi(),
    journal: StubStream = stubStream(),
): { fake: FakeApi; desktop: StubDesktop; journal: StubStream } {
    fake.answer('system.info', 'system.info/with-ui')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all')
        .answer(
            'decision.list',
            elsewhere === 'pending' ? 'decision.list/pending' : 'decision.list/empty',
            'decision.list/by-campaign-pending',
            'decision.list/summer-pending',
        )
        .answer('approval.get', 'approval.get/email-message', 'approval.get/other-pending')
        // Spring's first question was recorded whole with its options; the others are drawn from the same recording.
        .answerAnyRequest('decision.get', (body) =>
            (body as { decision_id?: string }).decision_id === 'dec_1'
                ? 'decision.get/pending-with-options'
                : 'decision.get/for-campaign-row',
        );
    if (!fake.offers('approval.list'))
        fake.answerAnyRequest('approval.list', (body) => {
            const campaign = (body as { campaign_id?: string }).campaign_id;
            return campaign === 'cmp_1'
                ? springApprovals
                : campaign === 'cmp_3'
                  ? 'approval.list/summer-pending'
                  : elsewhere === 'pending'
                    ? 'approval.list/pending'
                    : 'approval.list/empty';
        });
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
    const desktop = chatDesktop();
    desktop.keep({
        id: spring,
        campaignId: 'cmp_1',
        title: 'Spring',
        entries: [
            { kind: 'user', text: 'How is it going?' },
            ...(springSays === null ? [] : [{ kind: 'text' as const, text: springSays }]),
        ],
    });
    desktop.keep({
        id: summer,
        campaignId: 'cmp_3',
        title: 'Summer',
        entries: [
            { kind: 'user', text: 'What needs my decision?' },
            {
                kind: 'text',
                text: [
                    '**Waiting for you** — Message to Ines Duarte, ines@example.test',
                    '',
                    'Subject: Notes from the meetup',
                    '',
                    'Approve · Edit · Reject · Details',
                    '',
                    '**Question:** Follow up on LinkedIn? Keep going / Pause until the news',
                ].join('\n'),
            },
        ],
    });
    render(<Console api={fake} desktop={desktop} stream={journal.stream} />);
    return { fake, desktop, journal };
}

const listed = () =>
    within(document.querySelector<HTMLElement>('.side')!).getByRole('navigation', { name: 'Conversations' });

/** Opens a kept chat from the sidebar, as a person does, and returns its pane. */
async function openChat(title: 'Spring' | 'Summer'): Promise<HTMLElement> {
    // The sidebar is painted afresh as the desktop lists its conversations: the row is pressed until the chat is shown.
    await waitFor(() => {
        fireEvent.click(within(listed()).getByRole('button', { name: title }));
        expect(shown()).toBe(`chat:${title === 'Spring' ? spring : summer}`);
    });
    return document.querySelector<HTMLElement>('.area > .pane:not([hidden])')!;
}

const waiting = (pane: HTMLElement) => within(pane).getByRole('region', { name: 'Waiting for you' });
const card = (pane: HTMLElement, id: string) => pane.querySelector<HTMLElement>(`.waiting-card[data-id="${id}"]`);
/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();
const sent = (fake: FakeApi, operation: string) =>
    fake.calls.filter((each) => each.operation === operation).map((each) => each.body);

/** Waits for an approval's card to be drawn from its whole read, with its buttons on. */
async function drawn(pane: HTMLElement, id: string): Promise<HTMLElement> {
    return waitFor(() => {
        const found = card(pane, id);
        expect(found).not.toBeNull();
        expect(within(found!).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(false);
        return found!;
    });
}

/** A sheet's rules, read from the file (the test run hands a stylesheet import back empty; the run starts in ui/). */
function ruled(file: string): { file: string; selectors: string[]; body: string }[] {
    const text = readFileSync(file, 'utf8')
        .replace(/[/][*][^]*?[*][/]/g, '')
        .replace(/@(import|charset)[^;]*;/g, '');
    return Array.from(text.matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
        file,
        selectors: topLevel(rule[1]!),
        body: rule[2]!.replace(/\s+/g, ' ').trim(),
    }));
}

/** A selector list split at its own commas, not at those inside `:not(…)` or `:is(…)`. */
function topLevel(list: string): string[] {
    const parts: string[] = [];
    let depth = 0;
    let part = '';
    for (const each of list) {
        if (each === '(') depth++;
        if (each === ')') depth--;
        if (each === ',' && depth === 0) {
            parts.push(part.trim());
            part = '';
        } else part += each;
    }
    parts.push(part.trim());
    return parts;
}

/** What cuts an element's content and what hides it. */
const cuts = /line-clamp|max-height|overflow(-[xy])?: (hidden|clip)|text-overflow/;
const hides = /display: none|visibility: hidden|opacity: 0(?![.\d])|(^|[ ;])height: 0/;

/** The sheets the chat and its cards are drawn with: those the desktop's window and the browser tab's chat import. */
const sheetsOfTheChat = [
    ...new Set(
        ['src/console/mount.tsx', 'src/screens/chat/tab-chat.tsx'].flatMap((file) =>
            Array.from(readFileSync(file, 'utf8').matchAll(/^import '(\.[^']+\.css)';$/gm), (sheet) =>
                posix.join(posix.dirname(file), sheet[1]!),
            ),
        ),
    ),
];

/** The rules that could ever reach an element: in any state of it (hovered, focused, disabled), in either theme. A rule
 * of a pseudo-element never hides the element itself, and a selector this document cannot read is not one a browser
 * would apply to it either. */
function reaching(rules: ReturnType<typeof ruled>, element: Element): string[] {
    return rules.flatMap((rule) =>
        rule.selectors
            .filter((selector) => !selector.includes('::') && !selector.startsWith('@'))
            .filter((selector) => {
                const any = selector
                    .replace(/:not\(:[\w-]+\)/g, '')
                    .replace(/:(hover|focus-visible|focus-within|focus|active|disabled|enabled|checked)(?![\w-])/g, '');
                try {
                    return element.matches(any);
                } catch {
                    return false;
                }
            })
            .map((selector) => `${rule.file}: ${selector} { ${rule.body} }`),
    );
}

/** Every element of the drawn cards outside a message's clamped words, and each rule of the chat's sheets that cuts or
 * hides one: none may, the clamp of the words alone excepted. */
function cutting(pane: HTMLElement): string[] {
    const rules = sheetsOfTheChat.flatMap(ruled).filter((rule) => cuts.test(rule.body) || hides.test(rule.body));
    return [...pane.querySelectorAll('.waiting-card')]
        .flatMap((each) => [each, ...each.querySelectorAll('*')])
        .filter((element) => element.closest('.ask-b') === null)
        .flatMap((element) =>
            reaching(rules, element).map((rule) => `${element.className || element.tagName}: ${rule}`),
        );
}

/** What the console's modules hold between two drawings of it, as a window that is opened again starts from. */
function freshConsole(): void {
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
    attention.detail.value = null;
}

afterEach(() => {
    vi.useRealTimers();
});

beforeEach(() => {
    freshConsole();
    forgetWaiting();
    springApprovals = 'approval.list/by-campaign-pending';
    springSays = null;
    elsewhere = 'pending';
});

describe('what waits in an agent’s chat', () => {
    it('draws a card per pending approval and question of the chat’s campaign, from that campaign’s lists', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
            .answer('workitem.get', 'workitem.get/parked-send');
        renderConsole(fake);
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');

        // The two lists were asked for this campaign's pending ones, as the campaign's own page asks.
        expect(sent(fake, 'approval.list')).toContainEqual({ status: 'pending', campaign_id: 'cmp_1', limit: 50 });
        expect(sent(fake, 'decision.list')).toContainEqual({ status: 'pending', campaign_id: 'cmp_1', limit: 50 });
        // Under the conversation, in the chat's own scroll.
        const region = waiting(pane);
        expect(region.closest('.chat-scroll')?.querySelector('.thread')).not.toBeNull();

        // An approval's card: to whom, from which mailbox, the channel, the subject and the body, and its four acts.
        expect(words(email)).toContain('Waiting for you Message to Ines Duarte, ines@example.test Email');
        // The mailbox it goes from is in the header's meta, beside the channel, before any act of the card.
        const meta = email.querySelector<HTMLElement>('.ask-h .meta')!;
        expect(words(meta)).toMatch(/^Email · from owner@example\.test · /);
        expect(
            meta.compareDocumentPosition(within(email).getByRole('button', { name: 'Approve' })) &
                Node.DOCUMENT_POSITION_FOLLOWING,
        ).toBeTruthy();
        expect(within(email).getByText('Notes from the meetup')).toBeTruthy();
        expect(within(email).getByText('Hi Ines, thanks for the talk at the meetup.')).toBeTruthy();
        // Why it waits, in the sentence Needs you's panel gives.
        expect(words(email)).toContain(reasonLabel(fixtures['approval.get/email-message'].response.reason));
        expect(
            within(email)
                .getAllByRole('button')
                .map((each) => each.textContent),
        ).toEqual(['Approve', 'Edit', 'Reject', 'Details']);
        // An approval that is not a message says what will happen, and offers no Edit.
        const enroll = await drawn(pane, 'apr_2');
        expect(words(enroll)).toContain('Put Grace Hopper into the campaign at the provider.');
        expect(within(enroll).queryByRole('button', { name: 'Edit' })).toBeNull();

        // A question's card: its text, its options as buttons, and a free answer one press away.
        const question = await waitFor(() => {
            const found = card(pane, 'dec_1')!;
            within(found).getByRole('button', { name: 'Pause until the news' });
            return found;
        });
        expect(within(question).getByText('Follow up on LinkedIn?')).toBeTruthy();
        expect(within(question).getByRole('button', { name: 'Keep going' })).toBeTruthy();
        fireEvent.click(within(question).getByRole('button', { name: 'Something else…' }));
        expect(within(question).getByRole('textbox', { name: 'Your answer' })).toBeTruthy();
        expect(region.querySelectorAll('.waiting-card')).toHaveLength(5);
    });

    it('draws Approve green and Reject red, Details quiet at the end, the words clamped, and a question’s options as pills', async () => {
        const { fake } = renderConsole();
        fake.answer('workitem.get', 'workitem.get/parked-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        const button = (name: string) => within(email).getByRole('button', { name });
        expect(button('Approve').className).toBe('btn sm ok');
        expect(button('Edit').className).toBe('btn sm');
        expect(button('Reject').className).toBe('btn sm no');
        expect(button('Details').className).toBe('btn sm ghost lk');
        // The message's words are the card's body, which the sheet clamps to four lines.
        expect(within(email).getByText('Hi Ines, thanks for the talk at the meetup.').classList.contains('ask-b')).toBe(
            true,
        );
        // Asked once before it rejects: the question's Reject is red too.
        fireEvent.click(button('Reject'));
        const asking = within(email).getByRole('alertdialog');
        expect(within(asking).getByRole('button', { name: 'Reject' }).className).toBe('btn sm no');
        // A question's options are pills in a row of their own, with one more for an answer of one's own.
        const question = await waitFor(() => {
            const found = card(pane, 'dec_1')!;
            within(found).getByRole('button', { name: 'Keep going' });
            return found;
        });
        const options = question.querySelector('.ui-opts')!;
        expect([...options.querySelectorAll('button')].map((each) => [each.className, each.textContent])).toEqual([
            ['ui-opt', 'Pause until the news'],
            ['ui-opt', 'Keep going'],
            ['ui-opt', 'Something else…'],
        ]);
    });

    it('clamps a message longer than four lines, opens it in place on Show, and never holds Approve for it', async () => {
        // The page measures the clamp: here a body runs past its four lines until the card opens it.
        const runsOver = (box: HTMLElement) => box.classList.contains('ask-b') && !box.classList.contains('open');
        Object.defineProperty(HTMLElement.prototype, 'scrollHeight', {
            configurable: true,
            get(this: HTMLElement) {
                return runsOver(this) ? 240 : 0;
            },
        });
        Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
            configurable: true,
            get(this: HTMLElement) {
                return runsOver(this) ? 96 : 0;
            },
        });
        try {
            const { fake } = renderConsole();
            fake.answer('workitem.get', 'workitem.get/parked-send');
            const pane = await openChat('Spring');
            const email = await waitFor(() => {
                const found = card(pane, 'apr_1')!;
                within(found).getByRole('button', { name: 'Show the whole message' });
                return found;
            });
            // The gate is the whole read's alone: Approve, Edit, Reject and Details are live while the words are clamped.
            const live = () =>
                ['Approve', 'Edit', 'Reject', 'Details'].map(
                    (name) => !within(email).getByRole('button', { name }).hasAttribute('disabled'),
                );
            expect(live()).toEqual([true, true, true, true]);
            const body = within(email).getByText('Hi Ines, thanks for the talk at the meetup.');
            expect(body.classList.contains('open')).toBe(false);
            // Why it waits and the subject stand outside the clamp.
            expect(within(email).getByText('Notes from the meetup').closest('.ask-b')).toBeNull();

            fireEvent.click(within(email).getByRole('button', { name: 'Show the whole message' }));
            expect(
                within(email).getByText('Hi Ines, thanks for the talk at the meetup.').classList.contains('open'),
            ).toBe(true);
            expect(within(email).queryByRole('button', { name: 'Show the whole message' })).toBeNull();
            expect(live()).toEqual([true, true, true, true]);
        } finally {
            delete (HTMLElement.prototype as { scrollHeight?: number }).scrollHeight;
            delete (HTMLElement.prototype as { clientHeight?: number }).clientHeight;
        }
    });

    // Only a message's words are clamped: why it waits, the doubt, what will happen, the notes of a read and the acts
    // stand outside it.
    it('never clamps why it waits, the doubt, what will happen or the note of a read, and draws Approve outside the clamp', async () => {
        const { fake } = renderConsole();
        fake.answer('approval.get', 'approval.get/park-doubted', 'approval.get/other-pending');
        const release = fake.hold('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        const clamped = (element: Element) => element.closest('.ask-b') !== null;
        // Why it waits, which on a doubted message says the doubt is below.
        const why = email.querySelector('.ask-w')!;
        expect(words(why)).not.toBe('');
        expect(clamped(why)).toBe(false);
        expect(why.querySelector('.ask-b')).toBeNull();
        const doubts = email.querySelector('.send-doubts')!;
        expect(doubts).not.toBeNull();
        expect(clamped(doubts)).toBe(false);
        expect(clamped(within(email).getByText(/^The critic role doubted this/))).toBe(false);
        expect(clamped(within(email).getByText('Hi Ines, thanks for the talk at the meetup.'))).toBe(true);
        const approve = within(email).getByRole('button', { name: 'Approve' });
        expect(clamped(approve)).toBe(false);
        expect(approve.closest('.ask-a')).not.toBeNull();

        // The note of a read not yet answered, and then what will happen once it is.
        const enroll = card(pane, 'apr_2')!;
        const note = within(enroll).getByText(/cannot be approved until it is shown here whole/);
        expect(clamped(note)).toBe(false);
        release();
        await drawn(pane, 'apr_2');
        const intent = within(card(pane, 'apr_2')!).getByText('Put Grace Hopper into the campaign at the provider.');
        expect(clamped(intent)).toBe(false);
        expect(clamped(within(card(pane, 'apr_2')!).getByRole('button', { name: 'Approve' }))).toBe(false);
    });

    // Matched in the page, not by the names of a list: every rule of the sheets the chat is drawn with, in any state and
    // either theme, against every element of the cards drawn, a doubted message's, a held one's with its note, an
    // enrolment's with what will happen, and a question's with its own answer open.
    it('lets no rule of the chat’s sheets cut or hide a part of a card or its acts, the clamp of the words alone excepted', async () => {
        const { fake } = renderConsole();
        fake.answer('approval.get', 'approval.get/park-doubted', 'approval.get/other-pending');
        const release = fake.hold('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
        // Both shells' own sheets and the console's, read from their imports.
        expect(sheetsOfTheChat).toEqual(
            expect.arrayContaining([
                'src/console/console.css',
                'src/console/harness.css',
                'src/console/chat-cards.css',
                'src/screens/chat/tab-chat.css',
            ]),
        );
        expect(sheetsOfTheChat.length).toBeGreaterThanOrEqual(5);
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_1');
        within(card(pane, 'apr_2')!).getByText(/cannot be approved until it is shown here whole/);
        expect(cutting(pane)).toEqual([]);
        release();
        await drawn(pane, 'apr_2');
        const question = await waitFor(() => card(pane, 'dec_1')!);
        fireEvent.click(within(question).getByRole('button', { name: 'Something else…' }));
        within(question).getByRole('textbox', { name: 'Reason (optional)' });
        expect(cutting(pane)).toEqual([]);
    });

    // The message editor drawn in a card on Edit is Needs you's: the chat's sheet restyles the card's acts, not the
    // editor's buttons, its fields or its notes, in any state of them.
    it('leaves the message editor drawn in a card on Edit its own look: no rule of the chat’s sheet reaches it', async () => {
        const { fake } = renderConsole();
        fake.answer('workitem.get', 'workitem.get/parked-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Edit' }));
        await waitFor(() => within(email).getByRole('textbox', { name: 'Body' }));
        const editor = email.querySelector('.waiting-edit')!;
        within(editor as HTMLElement).getByRole('button', { name: 'Cancel' });
        const rules = ruled('src/console/chat-cards.css');
        expect([...editor.querySelectorAll('*')].flatMap((element) => reaching(rules, element))).toEqual([]);
    });

    // A question's option card: the options as pills, and one more in its own words for an answer of one's own, which opens the free
    // answer and the reason in place. Both stay one press away and are sent the same way.
    it('draws a question’s options as pills and one more, “Something else…”, that opens the own answer and the reason in place', async () => {
        renderConsole();
        const pane = await openChat('Spring');
        const question = await waitFor(() => {
            const found = card(pane, 'dec_1')!;
            within(found).getByRole('button', { name: 'Keep going' });
            return found;
        });
        expect([...question.querySelectorAll('.ui-opts button')].map((each) => each.textContent)).toEqual([
            'Pause until the news',
            'Keep going',
            'Something else…',
        ]);
        expect(within(question).queryByRole('textbox')).toBeNull();
        const other = within(question).getByRole('button', { name: 'Something else…' });
        expect(other.getAttribute('aria-expanded')).toBe('false');
        fireEvent.click(other);
        expect(other.getAttribute('aria-expanded')).toBe('true');
        const answer = within(question).getByRole('textbox', { name: 'Your answer' });
        expect(document.activeElement).toBe(answer);
        expect(question.querySelector('.ui-opts')!.compareDocumentPosition(answer)).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
        within(question).getByRole('textbox', { name: 'Reason (optional)' });
        within(question).getByRole('button', { name: 'Answer' });
        // A question with no options keeps its own answer open: it is the one answer it takes.
        const bare = await waitFor(() => card(pane, 'dec_2')!);
        expect(within(bare).queryByRole('button', { name: 'Something else…' })).toBeNull();
        within(bare).getByRole('textbox', { name: 'Your answer' });
        within(bare).getByRole('textbox', { name: 'Reason (optional)' });
    });

    it('draws exactly the runtime’s cards, whatever the AI writes to look like one', async () => {
        // Spring has pending approvals and questions; the AI's words imitate a card of one of them and name others.
        springSays = [
            '**Waiting for you** — Message to Ines Duarte, ines@example.test',
            '',
            'Subject: Notes from the meetup',
            '',
            'Approve · Edit · Reject · Details (apr_9)',
            '',
            '**Question:** Book the call? Send it now / Wait a month (dec_9)',
        ].join('\n');
        const { fake } = renderConsole();
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_1');
        await drawn(pane, 'apr_2');
        await waitFor(() => within(card(pane, 'dec_1')!).getByRole('button', { name: 'Keep going' }));
        // The AI's text is drawn as text, in the thread, with nothing to press: the thread's only buttons are the page's
        // chips under the answer, which send their own words.
        const thread = pane.querySelector<HTMLElement>('.thread')!;
        expect(words(thread)).toContain('Approve · Edit · Reject · Details (apr_9)');
        expect(thread.querySelector('a, input, form')).toBeNull();
        expect([...thread.querySelectorAll('button')].filter((each) => each.closest('.sugs.next') === null)).toEqual(
            [],
        );
        // The cards are the campaign's listed items, each once, and nothing else.
        const ids = [...waiting(pane).querySelectorAll<HTMLElement>('.waiting-card')].map((each) => each.dataset['id']);
        expect(ids).toEqual(['dec_1', 'dec_3', 'dec_2', 'apr_1', 'apr_2']);
        expect(within(pane).getAllByRole('button', { name: 'Approve' })).toHaveLength(2);
        for (const name of ['Send it now', 'Wait a month'])
            expect(within(pane).queryByRole('button', { name })).toBeNull();
        // Nothing the AI named was read.
        const read = (operation: string, key: string) =>
            [...new Set(sent(fake, operation).map((body) => (body as Record<string, string>)[key]))].sort();
        expect(read('approval.get', 'approval_id')).toEqual(['apr_1', 'apr_2']);
        expect(read('decision.get', 'decision_id')).toEqual(['dec_1', 'dec_2', 'dec_3']);
    });

    it('shows, above a doubted message, who doubted it and why, as Needs you’s panel does', async () => {
        const { fake } = renderConsole();
        fake.answer('approval.get', 'approval.get/park-doubted', 'approval.get/other-pending');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        const { doubts } = fixtures['approval.get/park-doubted'].response.preview as { doubts: { note: string }[] };
        const doubt = within(email).getByText(`Their words: ${doubts[0]!.note}`);
        expect(within(email).getByText(`The critic role doubted this: ${whyWords('unsupported_claim')}`)).toBeTruthy();
        // Why it waits says the doubt is below: it stands below that sentence, and above the message's words.
        const why = within(email).getByText(reasonLabel('doubted'));
        const body = within(email).getByText('Hi Ines, thanks for the talk at the meetup.');
        expect(why.compareDocumentPosition(doubt) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(doubt.compareDocumentPosition(body) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('says why a message it could not read cannot be approved, and reads it again at the next reload', async () => {
        springApprovals = 'approval.list/messages-pending';
        const journal = stubStream();
        const { fake } = renderConsole(undefined, journal);
        fake.answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        const reachable = fake.unreachable(
            'approval.get',
            (body) => (body as { approval_id?: string }).approval_id === 'apr_1',
        );
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_2');
        const email = card(pane, 'apr_1')!;
        const said = await waitFor(() => within(email).getByText(/could not be read, so it cannot be approved here/));
        expect(words(said)).toContain('Failed to fetch');
        expect(within(email).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(true);
        expect(within(email).getByRole('button', { name: 'Details' }).hasAttribute('disabled')).toBe(false);

        reachable();
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_202', kind: 'approval_requested' }));
        });
        await drawn(pane, 'apr_1');
        expect(within(card(pane, 'apr_1')!).queryByText(/could not be read/)).toBeNull();
    });

    // A read in flight is not sent beside itself, but its time runs out: the card then says it could not be read, and the
    // next reload reads it again, once. The clock is a fake one that also moves on its own, so the test waits for nothing.
    it('says a message is being read while its read is unanswered, does not read it again while it is, and reads it again at the next reload once its time ran out', async () => {
        vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'], shouldAdvanceTime: true });
        springApprovals = 'approval.list/messages-pending';
        const journal = stubStream();
        const { fake } = renderConsole(undefined, journal);
        fake.answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        const release = fake.hold('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_1');
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_2');
        const email = card(pane, 'apr_1')!;
        expect(words(email)).toContain('cannot be approved until it is shown here whole');
        expect(within(email).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(true);
        const reads = () =>
            sent(fake, 'approval.get').filter((body) => (body as { approval_id: string }).approval_id === 'apr_1');
        expect(reads()).toHaveLength(1);
        const listed = sent(fake, 'approval.list').length;
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_203', kind: 'approval_requested' }));
        });
        await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(listed), { timeout: 3000 });
        // Every answer the fake gave is taken in within one task: the reload's fill has run by then.
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(reads()).toHaveLength(1);

        await flush(() => vi.advanceTimersByTimeAsync(READ_LIMIT_MS));
        const late = card(pane, 'apr_1')!;
        expect(words(late)).toContain(
            'This approval could not be read, so it cannot be approved here; Details opens it in Needs you. The runtime did not answer within 30 seconds.',
        );
        expect(words(late)).not.toContain('The runtime said');
        expect(within(late).getByRole('button', { name: 'Approve' }).hasAttribute('disabled')).toBe(true);
        expect(reads()).toHaveLength(1);
        flush(() => journal.send(journalLine({ id: 'jrn_204', kind: 'approval_requested' })));
        await waitFor(() => expect(reads()).toHaveLength(2), { timeout: 3000 });
        await flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
        expect(reads()).toHaveLength(2);
        release();
        await drawn(pane, 'apr_1');
    });

    // The rule is the card's, not a list of kinds: whatever an approval does, Approve waits for its whole read.
    it('holds Approve on an enrolment it could not read, says why, and approves nothing', async () => {
        const { fake } = renderConsole();
        fake.unreachable('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_1');
        const enroll = card(pane, 'apr_2')!;
        const said = await waitFor(() => within(enroll).getByText(/could not be read, so it cannot be approved here/));
        expect(words(said)).toContain('Failed to fetch');
        const approve = within(enroll).getByRole('button', { name: 'Approve' });
        expect(approve.hasAttribute('disabled')).toBe(true);
        expect(within(enroll).getByRole('button', { name: 'Details' }).hasAttribute('disabled')).toBe(false);
        fireEvent.click(approve);
        await flush(() => Promise.resolve());
        expect(sent(fake, 'approval.approve')).toEqual([]);
    });

    it('holds Approve on an enrolment while its read is unanswered, says so, and approves nothing', async () => {
        const { fake } = renderConsole();
        const release = fake.hold('approval.get', (body) => (body as { approval_id?: string }).approval_id === 'apr_2');
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_1');
        const enroll = card(pane, 'apr_2')!;
        expect(words(enroll)).toContain('cannot be approved until it is shown here whole');
        const approve = within(enroll).getByRole('button', { name: 'Approve' });
        expect(approve.hasAttribute('disabled')).toBe(true);
        expect(within(enroll).getByRole('button', { name: 'Details' }).hasAttribute('disabled')).toBe(false);
        fireEvent.click(approve);
        await flush(() => Promise.resolve());
        expect(sent(fake, 'approval.approve')).toEqual([]);
        release();
        await drawn(pane, 'apr_2');
    });

    it('lets a refusal go with its card, at the next read that no longer lists it', async () => {
        const journal = stubStream();
        const { fake } = renderConsole(
            new FakeApi().answer('approval.approve', 'approval.approve/already-decided'),
            journal,
        );
        fake.answer('workitem.get', 'workitem.get/parked-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        // Another window decided it: the act is refused, and the reload after it no longer lists it.
        springApprovals = 'approval.list/empty';
        fireEvent.click(within(email).getByRole('button', { name: 'Approve' }));
        await waitFor(() => within(waiting(pane)).getByRole('alert'));
        expect(card(pane, 'apr_1')).toBeNull();
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_204', kind: 'approval_decided' }));
        });
        await waitFor(() => expect(within(waiting(pane)).queryByRole('alert')).toBeNull(), { timeout: 3000 });
        expect(card(pane, 'dec_1')).not.toBeNull();
    });

    it('says Jason asks again only in the drawing of the chat it was edited in', async () => {
        const first = renderConsole();
        first.fake.answer('workitem.get', 'workitem.get/parked-send');
        first.fake.answerAnyRequest('workitem.update', 'workitem.update/dates');
        let pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Edit' }));
        fireEvent.change(await waitFor(() => within(email).getByRole('textbox', { name: 'Body' })), {
            target: { value: 'Hi Ines, shall we talk next week?' },
        });
        springApprovals = 'approval.list/empty';
        fireEvent.click(within(email).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(waiting(pane)).getByText(/Jason asks again with the new text/));

        // The window is drawn again: the chat starts from what the runtime lists, and the edit's note went with it.
        cleanup();
        freshConsole();
        renderConsole();
        pane = await openChat('Spring');
        await waitFor(() => within(card(pane, 'dec_1')!).getByRole('button', { name: 'Keep going' }));
        expect(within(pane).queryByText(/Jason asks again with the new text/)).toBeNull();
    });

    it('never lets one campaign’s lists set the mark of what waits', async () => {
        elsewhere = 'nothing';
        pendingMark.value = false;
        const marks: boolean[] = [];
        const stop = pendingMark.subscribe((value) => marks.push(value));
        try {
            renderConsole();
            const pane = await openChat('Spring');
            await drawn(pane, 'apr_1');
            expect(marks).not.toContain(true);
        } finally {
            stop();
        }
    });

    it('approves through approval.approve after checking the work item, and the card leaves on the next read', async () => {
        const { fake } = renderConsole(new FakeApi().answer('approval.approve', 'approval.approve/approved'));
        fake.answer('workitem.get', 'workitem.get/parked-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        springApprovals = 'approval.list/empty';
        fireEvent.click(within(email).getByRole('button', { name: 'Approve' }));
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        expect(sent(fake, 'workitem.get')).toEqual([{ work_item_id: 'wi_1' }]);
        expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]);
        // Approving sends no reason, as Needs you does; the questions still wait.
        expect(card(pane, 'dec_1')).not.toBeNull();
    });

    it('does not approve a message whose text moved since it was asked, and says so', async () => {
        const { fake } = renderConsole();
        fake.answer('workitem.get', 'workitem.get/parked-edited-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Approve' }));
        await waitFor(() => within(email).getByText(/changed after this approval was asked/));
        expect(sent(fake, 'approval.approve')).toEqual([]);
    });

    it('shows the runtime’s code and words when an act is refused, and never reverts silently', async () => {
        const { fake } = renderConsole(new FakeApi().answer('approval.approve', 'approval.approve/already-decided'));
        fake.answer('workitem.get', 'workitem.get/parked-send');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Approve' }));
        const refused = await waitFor(() => within(waiting(pane)).getByRole('alert'));
        const { error } = fixtures['approval.approve/already-decided'].response;
        expect(words(refused)).toBe(`${error.code} ${error.message}`);
    });

    it('asks once before it rejects, and rejects through approval.reject with the reason typed', async () => {
        const { fake } = renderConsole(new FakeApi().answer('approval.reject', 'approval.reject/rejected'));
        const pane = await openChat('Spring');
        const enroll = await drawn(pane, 'apr_2');
        fireEvent.click(within(enroll).getByRole('button', { name: 'Reject' }));
        // The question: Cancel asks nothing of the runtime.
        let asking = within(enroll).getByRole('alertdialog', { name: 'Reject Enroll Oli Example?' });
        fireEvent.click(within(asking).getByRole('button', { name: 'Cancel' }));
        expect(within(enroll).queryByRole('alertdialog')).toBeNull();
        expect(sent(fake, 'approval.reject')).toEqual([]);

        fireEvent.click(within(enroll).getByRole('button', { name: 'Reject' }));
        asking = within(enroll).getByRole('alertdialog', { name: 'Reject Enroll Oli Example?' });
        fireEvent.change(within(asking).getByRole('textbox', { name: 'Reason (optional, kept in the journal)' }), {
            target: { value: 'not this quarter' },
        });
        fireEvent.click(within(asking).getByRole('button', { name: 'Reject' }));
        await waitFor(() =>
            expect(sent(fake, 'approval.reject')).toEqual([{ approval_id: 'apr_2', reason: 'not this quarter' }]),
        );
    });

    it('edits a message with Needs you’s editor and saves it through workitem.update, then says Jason asks again', async () => {
        const { fake } = renderConsole();
        fake.answer('workitem.get', 'workitem.get/parked-send');
        // No exchange was recorded for a message's edit; the request is read from what the card sent.
        fake.answerAnyRequest('workitem.update', 'workitem.update/dates');
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Edit' }));
        const body = await waitFor(() => within(email).getByRole('textbox', { name: 'Body' }));
        expect((body as HTMLTextAreaElement).value).toBe('Hi Ines, thanks for the talk at the meetup.');
        expect((within(email).getByRole('textbox', { name: 'Subject' }) as HTMLInputElement).value).toBe(
            'Notes from the meetup',
        );
        // Cancel closes the editor and writes nothing.
        fireEvent.click(within(email).getByRole('button', { name: 'Cancel' }));
        expect(within(email).queryByRole('textbox', { name: 'Body' })).toBeNull();

        fireEvent.click(within(email).getByRole('button', { name: 'Edit' }));
        fireEvent.change(await waitFor(() => within(email).getByRole('textbox', { name: 'Body' })), {
            target: { value: 'Hi Ines, shall we talk next week?' },
        });
        springApprovals = 'approval.list/empty';
        fireEvent.click(within(email).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(waiting(pane)).getByText(/Jason asks again with the new text/));
        expect(sent(fake, 'workitem.update')).toEqual([
            {
                work_item_id: 'wi_1',
                set: {
                    input: {
                        channel: 'email',
                        sender: { external_id: 's_12', address: 'owner@example.test' },
                        message: {
                            format: 'email',
                            subject: 'Notes from the meetup',
                            body: 'Hi Ines, shall we talk next week?',
                        },
                    },
                },
                reason: 'Message text edited on the approval card.',
            },
        ]);
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        expect(sent(fake, 'approval.approve')).toEqual([]);
    });

    it('opens an approval’s Details in Needs you, as its row there opens it', async () => {
        renderConsole();
        const pane = await openChat('Spring');
        const email = await drawn(pane, 'apr_1');
        fireEvent.click(within(email).getByRole('button', { name: 'Details' }));
        await waitFor(() => expect(shown()).toBe('needs'));
        await waitFor(() => expect(attention.detail.value?.item.id).toBe('apr_1'));
        const needs = screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
        await waitFor(() => expect(words(needs)).toContain('From owner@example.test'));
    });

    it('answers a question by its option through decision.answer, with the reason typed', async () => {
        const { fake } = renderConsole(new FakeApi().answer('decision.answer', 'decision.answer/answered'));
        const pane = await openChat('Spring');
        const question = await waitFor(() => {
            const found = card(pane, 'dec_1')!;
            within(found).getByRole('button', { name: 'Pause until the news' });
            return found;
        });
        // The reason sits with the answer of one's own, one press away; an option then carries it.
        fireEvent.click(within(question).getByRole('button', { name: 'Something else…' }));
        fireEvent.change(within(question).getByRole('textbox', { name: 'Reason (optional)' }), {
            target: { value: 'their CFO is leaving' },
        });
        fireEvent.click(within(question).getByRole('button', { name: 'Pause until the news' }));
        await waitFor(() =>
            expect(sent(fake, 'decision.answer')).toEqual([
                {
                    decision_id: 'dec_1',
                    answer: 'Pause until the news',
                    option: 'Pause until the news',
                    reason: 'their CFO is leaving',
                },
            ]),
        );
    });

    it('answers a question in the person’s own words through decision.answer', async () => {
        const { fake } = renderConsole();
        // No exchange was recorded for a free answer; the request is read from what the card sent.
        fake.answerAnyRequest('decision.answer', 'decision.answer/answered');
        const pane = await openChat('Spring');
        const question = await waitFor(() => card(pane, 'dec_2')!);
        const answer = within(question).getByRole('button', { name: 'Answer' });
        expect(answer.hasAttribute('disabled')).toBe(true);
        fireEvent.change(within(question).getByRole('textbox', { name: 'Your answer' }), {
            target: { value: 'Only to Pia, next week' },
        });
        fireEvent.click(answer);
        await waitFor(() =>
            expect(sent(fake, 'decision.answer')).toEqual([{ decision_id: 'dec_2', answer: 'Only to Pia, next week' }]),
        );
    });

    it('lets a card go once the next read no longer lists it, decided anywhere else', async () => {
        const journal = stubStream();
        renderConsole(undefined, journal);
        const pane = await openChat('Spring');
        await drawn(pane, 'apr_2');
        springApprovals = 'approval.list/empty';
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_201', kind: 'approval_decided' }));
        });
        await waitFor(() => expect(card(pane, 'apr_2')).toBeNull(), { timeout: 3000 });
        expect(card(pane, 'apr_1')).toBeNull();
        expect(card(pane, 'dec_1')).not.toBeNull();
    });
});

describe('a card’s acts, held from a press until the lists read after it have answered', () => {
    /** Spring with two first messages, each read whole: the first is approved, and the second is left waiting. */
    function two(journal: StubStream = stubStream(), fake: FakeApi = new FakeApi()) {
        springApprovals = 'approval.list/needs-two';
        elsewhere = 'nothing';
        if (!fake.offers('approval.approve'))
            fake.answer('approval.approve', 'approval.approve/needs-two-first-approved');
        const rendered = renderConsole(fake, journal);
        rendered.fake
            .answer('approval.get', 'approval.get/needs-two-first', 'approval.get/needs-two-second')
            .answer('workitem.get', 'workitem.get/needs-two-first-parked');
        return rendered;
    }
    const button = (pane: HTMLElement, id: string, name: string) =>
        within(card(pane, id)!).getByRole('button', { name });
    const usable = (pane: HTMLElement, id: string, name: string) => !button(pane, id, name).hasAttribute('disabled');
    /** Lets every answer the fake gave be taken in. */
    const settle = () => flush(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
    /** Holds the chat's next read of its campaign's approvals, and only that one, until released. */
    function holdNextList(fake: FakeApi): () => void {
        let seen = 0;
        return fake.hold('approval.list', (body) => 'campaign_id' in body && ++seen === 1);
    }
    async function bothDrawn(pane: HTMLElement): Promise<void> {
        await drawn(pane, 'apr_1');
        await drawn(pane, 'apr_2');
    }

    it('takes the second click of a double click on Approve as no press: one approval, never the next card’s', async () => {
        const { fake } = two();
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        await drawn(pane, 'apr_2');
        // The second click of the double click arrives once the first act has settled, on the card that moved up.
        fireEvent.click(button(pane, 'apr_2', 'Approve'), { detail: 2 });
        await settle();
        expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]);
    });

    it('holds the next card’s Approve, Edit and Reject, and a question’s options, from the approval’s answer until the lists’ answer', async () => {
        const { fake } = two();
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        await waitFor(() => button(pane, 'dec_1', 'Pause until the news'));
        springApprovals = 'approval.list/needs-two-after';
        const release = holdNextList(fake);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
        await settle();
        expect(card(pane, 'apr_1')).not.toBeNull();
        for (const name of ['Approve', 'Edit', 'Reject']) expect(usable(pane, 'apr_2', name)).toBe(false);
        expect(usable(pane, 'dec_1', 'Pause until the news')).toBe(false);
        await flush(async () => release());
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        await drawn(pane, 'apr_2');
        expect(usable(pane, 'dec_1', 'Pause until the news')).toBe(true);
        expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]);
    });

    it('holds a question’s options and Answer, and the approvals’ acts, from an answer until the lists’ answer', async () => {
        const { fake } = two();
        fake.answerAnyRequest('decision.answer', 'decision.answer/answered');
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        await waitFor(() => button(pane, 'dec_1', 'Pause until the news'));
        let seen = 0;
        const release = fake.hold('decision.list', (body) => 'campaign_id' in body && ++seen === 1);
        fireEvent.click(button(pane, 'dec_1', 'Pause until the news'), { detail: 1 });
        await waitFor(() => expect(sent(fake, 'decision.answer')).toHaveLength(1));
        await settle();
        expect(usable(pane, 'dec_1', 'Keep going')).toBe(false);
        fireEvent.change(within(card(pane, 'dec_2')!).getByRole('textbox', { name: 'Your answer' }), {
            target: { value: 'Only to Pia' },
        });
        expect(usable(pane, 'dec_2', 'Answer')).toBe(false);
        expect(usable(pane, 'apr_1', 'Approve')).toBe(false);
        await flush(async () => release());
        await waitFor(() => expect(usable(pane, 'dec_2', 'Answer')).toBe(true));
        await drawn(pane, 'apr_1');
        expect(sent(fake, 'decision.answer')).toHaveLength(1);
    });

    it('lets the acts go after an act the runtime refused', async () => {
        const { fake } = two(
            stubStream(),
            new FakeApi().answer('approval.approve', 'approval.approve/already-decided'),
        );
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => within(waiting(pane)).getByRole('alert'));
        await drawn(pane, 'apr_1');
        await drawn(pane, 'apr_2');
        expect(sent(fake, 'approval.approve')).toHaveLength(1);
    });

    it('lets the acts go after the lists read after an act failed, and keeps the lists it had', async () => {
        const { fake } = two();
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        fake.unreachable('approval.list', (body) => 'campaign_id' in body);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(words(within(waiting(pane)).getByRole('alert'))).toContain('Failed to fetch'));
        await drawn(pane, 'apr_2');
        expect(card(pane, 'apr_1')).not.toBeNull();
    });

    it('holds the acts through a reload the journal starts during the act’s own read, and never brings the approved card back', async () => {
        const journal = stubStream();
        const { fake } = two(journal);
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        const release = holdNextList(fake);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
        const lists = sent(fake, 'approval.list').length;
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_401', kind: 'approval_decided' }));
        });
        // The journal's reload answers first and draws the lists without the approved card; the act's own read still waits.
        await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(lists), { timeout: 3000 });
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        await settle();
        expect(usable(pane, 'apr_2', 'Approve')).toBe(false);
        springApprovals = 'approval.list/needs-two';
        await flush(async () => release());
        await drawn(pane, 'apr_2');
        expect(card(pane, 'apr_1')).toBeNull();
    });

    it('lets the acts go when the newest read of the lists fails during an act, and keeps the lists it had', async () => {
        const journal = stubStream();
        const { fake } = two(journal);
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        let seen = 0;
        fake.unreachable('approval.list', (body) => 'campaign_id' in body && ++seen >= 2);
        const release = fake.hold('approval.list', (body) => 'campaign_id' in body && seen === 1);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_402', kind: 'approval_decided' }));
        });
        await waitFor(() => expect(seen).toBeGreaterThanOrEqual(2), { timeout: 3000 });
        await settle();
        expect(usable(pane, 'apr_2', 'Approve')).toBe(false);
        await flush(async () => release());
        await drawn(pane, 'apr_2');
        expect(card(pane, 'apr_1')).not.toBeNull();
    });

    it('lets the acts go once the newest of two reloads during one act has answered', async () => {
        const journal = stubStream();
        const { fake } = two(journal);
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        const release = holdNextList(fake);
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 1 });
        await waitFor(() => expect(sent(fake, 'approval.approve')).toHaveLength(1));
        const lists = sent(fake, 'approval.list').length;
        flush(() => {
            journal.open();
            journal.send(journalLine({ id: 'jrn_403', kind: 'approval_decided' }));
        });
        await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(lists), { timeout: 3000 });
        const again = sent(fake, 'approval.list').length;
        flush(() => journal.send(journalLine({ id: 'jrn_404', kind: 'approval_decided' })));
        await waitFor(() => expect(sent(fake, 'approval.list').length).toBeGreaterThan(again), { timeout: 3000 });
        await settle();
        expect(usable(pane, 'apr_2', 'Approve')).toBe(false);
        await flush(async () => release());
        await drawn(pane, 'apr_2');
        expect(card(pane, 'apr_1')).toBeNull();
    });

    it('takes a lone click with a detail above one as no press on every act of a card', async () => {
        const { fake } = two();
        fake.answerAnyRequest('decision.answer', 'decision.answer/answered');
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        await waitFor(() => button(pane, 'dec_1', 'Pause until the news'));
        fireEvent.click(button(pane, 'apr_1', 'Approve'), { detail: 2 });
        fireEvent.click(button(pane, 'apr_2', 'Reject'), { detail: 1 });
        const asking = within(card(pane, 'apr_2')!).getByRole('alertdialog');
        fireEvent.click(within(asking).getByRole('button', { name: 'Reject' }), { detail: 2 });
        fireEvent.click(button(pane, 'dec_1', 'Pause until the news'), { detail: 3 });
        fireEvent.change(within(card(pane, 'dec_2')!).getByRole('textbox', { name: 'Your answer' }), {
            target: { value: 'Only to Pia' },
        });
        fireEvent.click(button(pane, 'dec_2', 'Answer'), { detail: 2 });
        fireEvent.click(button(pane, 'apr_1', 'Edit'), { detail: 1 });
        fireEvent.change(await waitFor(() => within(card(pane, 'apr_1')!).getByRole('textbox', { name: 'Body' })), {
            target: { value: 'Hi Ines, shall we talk next week?' },
        });
        fireEvent.click(button(pane, 'apr_1', 'Save'), { detail: 2 });
        await settle();
        for (const operation of ['approval.approve', 'approval.reject', 'decision.answer', 'workitem.update'])
            expect(sent(fake, operation)).toEqual([]);
    });

    it('approves once when Approve is pressed twice from the keyboard', async () => {
        const { fake } = two();
        const pane = await openChat('Spring');
        await bothDrawn(pane);
        springApprovals = 'approval.list/needs-two-after';
        const approve = button(pane, 'apr_1', 'Approve');
        fireEvent.click(approve, { detail: 0 });
        fireEvent.click(approve, { detail: 0 });
        await waitFor(() => expect(card(pane, 'apr_1')).toBeNull());
        await drawn(pane, 'apr_2');
        expect(sent(fake, 'approval.approve')).toEqual([{ approval_id: 'apr_1' }]);
    });
});

describe('the sheet of the chat and its cards', () => {
    // Read from the file: the test run hands a stylesheet import back empty. The run starts in ui/.
    const sheet = readFileSync('src/console/chat-cards.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
    const rules = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
        selectors: rule[1]!.split(',').map((each) => each.trim()),
        body: rule[2]!.replace(/\s+/g, ' ').trim(),
    }));
    const body = (selector: string, under = (each: string) => each === selector) =>
        rules.filter((rule) => rule.selectors.some(under)).map((rule) => rule.body);

    it('reaches nothing outside the console: every rule is under its root, the dark theme included', () => {
        expect(rules.length).toBeGreaterThan(0);
        for (const rule of rules)
            for (const selector of rule.selectors)
                expect(selector).toMatch(
                    /^(:root(:not\(\[data-theme='light'\]\)|\[data-theme='dark'\]) )?\.jc-console /,
                );
    });

    // A button, a card's parts, a box and a note are drawn by the harness's screens inside the console too (People,
    // Inbox, the threads, Needs you, the message editor): a rule here that names one reaches the waiting cards alone.
    it('restyles the harness’s shared pieces only on the waiting cards', () => {
        const shared = /\.(btn(?!\.(ok|no)(?![\w-]))|ask(-[achsw])?|inp|note|meta)(?![\w-])/;
        const outside = rules
            .flatMap((rule) => rule.selectors)
            .filter((each) => shared.test(each) && !/\.waiting-card(?![\w-])/.test(each));
        expect(outside).toEqual([]);
    });

    // The console's two sheets that draw the cards. The clamp is the body's alone: no rule that reaches the doubt, why it
    // waits, the subject, what will happen, a note of the card or its acts hides or cuts anything of them.
    it('clamps a message’s words and nothing else, and hides no part of a card nor its acts', () => {
        const parsed = (file: string) =>
            Array.from(
                readFileSync(file, 'utf8')
                    .replace(/[/][*][^]*?[*][/]/g, '')
                    .matchAll(/([^{}]+)[{]([^{}]*)[}]/g),
                (rule) => ({
                    selectors: rule[1]!.split(',').map((each) => each.trim()),
                    body: rule[2]!.replace(/\s+/g, ' ').trim(),
                }),
            );
        // The one clamp: the words of a message, in either theme.
        const clamp = /(^| )\.jc-console \.ask-b$/;
        // The parts of a card a person reads before an act, and the acts themselves.
        const kept =
            /\.(waiting-card|chat-waiting-for|ask|ask-h|ask-s|ask-w|ask-a|section|send-doubts|message-text|waiting-intent|note|btn\.ok|btn\.no)(?![\w-])/;
        const offending = (sheet: typeof rules, reaches: (selector: string) => boolean) =>
            sheet.flatMap((rule) =>
                rule.selectors
                    .filter(reaches)
                    .filter((each) => (cuts.test(rule.body) && !clamp.test(each)) || hides.test(rule.body))
                    .map((each) => `${each} { ${rule.body} }`),
            );
        // This sheet is the chat's own: nothing in it cuts but the clamp, wherever it reaches.
        expect(offending(rules, () => true)).toEqual([]);
        expect(rules.filter((rule) => cuts.test(rule.body)).flatMap((rule) => rule.selectors)).toEqual([
            '.jc-console .ask-b',
        ]);
        // The console's sheet, where it reaches a card's parts.
        expect(offending(parsed('src/console/console.css'), (each) => kept.test(each))).toEqual([]);
    });

    it('draws Approve green, Reject red and the body clamped to four lines, in both themes', () => {
        expect(body('.jc-console .btn.ok').join(' ')).toMatch(/background: var\(--green\)/);
        expect(body('.jc-console .btn.no').join(' ')).toMatch(/color: var\(--red\)/);
        expect(body('.jc-console .ask-b').join(' ')).toMatch(/-webkit-line-clamp: 4/);
        expect(body('.jc-console .ask-b.open').join(' ')).toMatch(/display: block/);
        // What the dark theme changes, it changes the same way whether the system or the person chose it.
        const dark = (selector: string, theme: 'system' | 'chosen') =>
            body(
                selector,
                (each) =>
                    each ===
                    (theme === 'system'
                        ? `:root:not([data-theme='light']) ${selector}`
                        : `:root[data-theme='dark'] ${selector}`),
            );
        for (const selector of [
            '.jc-console .btn.ok',
            '.jc-console .ask-b',
            '.jc-console .waiting-card .send-doubts',
            '.jc-console .waiting-card.ask.q',
        ]) {
            expect(dark(selector, 'system')).not.toEqual([]);
            expect(dark(selector, 'system')).toEqual(dark(selector, 'chosen'));
        }
    });
});
