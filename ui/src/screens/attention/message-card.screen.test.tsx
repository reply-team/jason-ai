import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { toApprovalDetail } from '../../console/live';
import { fixtures } from '../../test/fixtures';
import { ApiProvider } from '../../api/context';
import { FakeApi } from '../../test/fake-api';
import { t } from '../../platform/i18n';
import { MessageCard } from './message-card';

const card = (preview: Record<string, unknown>, reply = false) =>
    render(<MessageCard detail={toApprovalDetail(preview)} reply={reply} />);
const email = fixtures['approval.get/email-message'].response.preview as unknown as Record<string, unknown>;
const terms = () => Array.from(document.querySelectorAll('.message-card dt'), (dt) => dt.textContent);

describe('the message card', () => {
    it('shows an email as sender, recipient, subject and body, in that order, with the provider sentence', () => {
        card(email);
        expect(screen.getByRole('heading', { name: 'The message as written' })).toBeTruthy();
        expect(terms()).toEqual(['From', 'To', 'Subject', 'Body']);
        const values = Array.from(document.querySelectorAll('.message-card dd'), (dd) => dd.textContent);
        expect(values).toEqual([
            'owner@example.test',
            'Ines Duarte, ines@example.test',
            'Notes from the meetup',
            'Hi Ines, thanks for the talk at the meetup.',
        ]);
        expect(
            screen.getByText(/the sender's signature kept at the provider, and the provider's tracking/i),
        ).toBeTruthy();
        expect(document.body.textContent).not.toMatch(/as the recipient/i);
    });

    it('shows a LinkedIn message as its text', () => {
        card(fixtures['approval.get/linkedin-message'].response.preview as unknown as Record<string, unknown>);
        expect(terms()).toEqual(['Text']);
        expect(screen.getByText('Thanks for connecting, Noor.')).toBeTruthy();
    });

    it('shows a template with its example labelled as one the provider fills in', () => {
        card({
            highlights: {
                '/args/channel': 'email',
                '/args/message': { format: 'email', compose: 'template', subject: 'Hi {{x}}', body: 'Hello {{y}}' },
            },
            example: { subject: 'Hi Ines', body: 'Hello [sender.first_name]', unfilled: ['sender.first_name'] },
        });
        expect(screen.getByText('An example the provider fills in')).toBeTruthy();
        expect(screen.getByText('Hello [sender.first_name]')).toBeTruthy();
        expect(screen.getByText('Hello {{y}}')).toBeTruthy();
    });

    it('draws markup, a script tag and an entity as characters, and keeps its line breaks', () => {
        const body = 'Line one\n<b>bold</b> <script>alert(1)</script> &amp; done';
        card({
            highlights: { '/args/channel': 'email', '/args/message': { format: 'email', subject: '<i>s</i>', body } },
        });
        const shown = Array.from(document.querySelectorAll('.message-card .message-text')).at(-1)!;
        expect(shown.textContent).toBe(body);
        expect(document.querySelector('.message-card b, .message-card script, .message-card i')).toBeNull();
        expect(shown.children.length).toBe(0);
    });

    it('shows a reply as who it goes to, the channel and the text', () => {
        card(
            {
                contact: { name: 'Ines Duarte', value: 'ines@example.test' },
                highlights: { '/args/channel': 'email', '/args/body': 'Thanks, Tuesday works.' },
            },
            true,
        );
        expect(terms()).toEqual(['To', 'Channel', 'Text']);
        expect(screen.getByText('Thanks, Tuesday works.')).toBeTruthy();
    });

    it('shows a reply in a thread as going from the account the conversation is in, naming none', () => {
        card(fixtures['approval.get/card-reply'].response.preview as unknown as Record<string, unknown>, true);
        expect(terms()).toEqual(['From', 'To', 'Channel', 'Text']);
        const values = Array.from(document.querySelectorAll('.message-card dd'), (dd) => dd.textContent);
        expect(values.slice(0, 3)).toEqual([
            'the account this conversation is in',
            'Kai Lindt, kai@example.test',
            'Email',
        ]);
    });

    it('shows nothing for an approval that is no message', () => {
        card(fixtures['approval.get/pending-with-preview'].response.preview as unknown as Record<string, unknown>);
        expect(document.querySelector('.message-card')).toBeNull();
    });
});

describe('the message card, where the letter follows one', () => {
    const withFollows = (follows: unknown) => ({
        ...email,
        highlights: { ...(email['highlights'] as Record<string, unknown>), '/follows': follows },
    });

    it('says a new letter and why, and marks no subject', () => {
        card(withFollows({ work_item: null, operation: null, reference: null, because: 'not_offered' }));
        expect(terms()).toEqual(['From', 'To', 'Follows', 'Subject', 'Body']);
        expect(document.body.textContent).toMatch(
            /A new letter: this provider's plugin does not offer a reply in a thread/,
        );
    });

    it('says a reply goes under the followed letter, read from its recorded work item, and marks the subject as not sent', async () => {
        const fake = new FakeApi().answer('workitem.get', 'workitem.get/card-follow-letter');
        const preview = fixtures['approval.get/card-follow'].response.preview as unknown as Record<string, unknown>;
        render(
            <ApiProvider api={fake}>
                <MessageCard detail={toApprovalDetail(preview)} />
            </ApiProvider>,
        );
        await waitFor(() =>
            screen.getByText(
                /A reply to "Notes from the meetup" of \d+ \w+( 2026)?, \d\d:\d\d, the letter this campaign's thread/,
            ),
        );
        expect(terms()).toEqual(['From', 'To', 'Follows', 'Subject (not sent)', 'Body']);
        expect(fake.sent('workitem.get')).toEqual({ work_item_id: 'wi_1' });
    });

    it('dates the followed letter by its send_after where it has one', async () => {
        const letter = fixtures['workitem.get/card-follow-letter'].response;
        const fake = new FakeApi();
        fake.call = async () =>
            ({
                ...letter,
                context: { ...letter.context, input: { ...letter.context.input, send_after: '2020-03-04T09:30:00Z' } },
            }) as never;
        const preview = fixtures['approval.get/card-follow'].response.preview as unknown as Record<string, unknown>;
        render(
            <ApiProvider api={fake}>
                <MessageCard detail={toApprovalDetail(preview)} />
            </ApiProvider>,
        );
        await waitFor(() =>
            screen.getByText(/A reply to "Notes from the meetup" of 4 Mar 2020, \d\d:\d\d, the letter/),
        );
    });

    it('says the reply as first written where the followed letter cannot be read', async () => {
        const fake = new FakeApi();
        fake.call = async () => {
            throw new Error('unreachable');
        };
        const preview = fixtures['approval.get/card-follow'].response.preview as unknown as Record<string, unknown>;
        render(
            <ApiProvider api={fake}>
                <MessageCard detail={toApprovalDetail(preview)} />
            </ApiProvider>,
        );
        // The words are the same before the read ends, so the read is let end (it is refused) before they are held to.
        await waitFor(() => expect(fake.call).toBeDefined());
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(document.body.textContent).toContain(t('follows.reply.unread'));
        expect(document.body.textContent).not.toContain('Notes from the meetup" of');
    });

    it('shows nothing for a send that is not an email', () => {
        card(withFollows({ work_item: null, operation: null, reference: null, because: 'not_offered' }), true);
        expect(terms()).not.toContain('Follows');
    });
});
