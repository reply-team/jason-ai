import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { toApprovalDetail } from '../../console/live';
import { fixtures } from '../../test/fixtures';
import { MessageCard } from './message-card';

const card = (preview: Record<string, unknown>) => render(<MessageCard detail={toApprovalDetail(preview)} />);
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
        expect(screen.getByText(/the provider adds its signature and its tracking when it sends/i)).toBeTruthy();
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

    it('shows nothing for an approval that is no message', () => {
        card(fixtures['approval.get/pending-with-preview'].response.preview as unknown as Record<string, unknown>);
        expect(document.querySelector('.message-card')).toBeNull();
    });
});
