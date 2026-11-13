import { describe, expect, it } from 'vitest';
import { fixtures } from '../test/fixtures';
import { channelWords, operationWords, toApprovalDetail, toApprovalRow } from './live';

describe('what Needs you reads from an approval', () => {
    it('names the account a message goes from and the channel it goes on, from the preview the runtime recorded', () => {
        const email = toApprovalDetail(fixtures['approval.get/email-message'].response.preview);
        expect(email).toMatchObject({
            to: 'Ines Duarte, ines@example.test',
            from: 'owner@example.test',
            channel: 'Email',
        });
        const linkedIn = toApprovalDetail(fixtures['approval.get/linkedin-message'].response.preview);
        expect(linkedIn).toMatchObject({ from: 'https://www.linkedin.com/in/owner-example', channel: 'LinkedIn' });
    });

    it('says nothing of a sender or a channel a preview does not highlight', () => {
        const enrol = toApprovalDetail(fixtures['approval.get/pending-with-preview'].response.preview);
        expect(enrol).toMatchObject({ from: null, channel: null });
    });

    it('names a sender the provider holds no address for by its identifier', () => {
        expect(toApprovalDetail({ highlights: { '/args/sender': { external_id: 's_9', address: null } } }).from).toBe(
            's_9',
        );
        expect(toApprovalDetail({ highlights: { '/args/sender': { external_id: '', address: '' } } }).from).toBeNull();
    });

    it('takes the recipient from the highlighted address when the preview names no contact', () => {
        expect(toApprovalDetail({ highlights: { '/contacts/0/channels/0/value': 'someone@example.test' } }).to).toBe(
            'someone@example.test',
        );
    });

    it('names who a row goes to by the contact it carries, else by the address an open preview highlights', () => {
        const listed = fixtures['approval.list/messages-pending'].response.items[0]!;
        const opened = fixtures['approval.get/email-message'].response.preview;
        const now = Date.parse('2026-10-16T09:00:00.000Z');
        expect(toApprovalRow(listed, now).who).toBe('Ines Duarte');
        expect(toApprovalRow({ ...listed, contact_name: null }, now).who).toBeNull();
        expect(toApprovalRow({ ...listed, contact_name: null }, now, opened).who).toBe('ines@example.test');
    });

    it('says a row reaches its person by a message only for the operations that send one', () => {
        const listed = fixtures['approval.list/messages-pending'].response.items[0]!;
        const now = Date.parse('2026-10-16T09:00:00.000Z');
        for (const operation of ['message.send', 'conversation.reply', 'social_invitation.send']) {
            expect(toApprovalRow({ ...listed, operation }, now).byMessage).toBe(true);
        }
        for (const operation of ['campaign.enroll', 'campaign.activate', 'enrollment.stop', 'constructor', 'x.y']) {
            expect(toApprovalRow({ ...listed, operation }, now).byMessage).toBe(false);
        }
    });

    it('says a channel in words, and a code it has no words for as the code itself', () => {
        expect(channelWords('email')).toBe('Email');
        expect(channelWords('linkedin')).toBe('LinkedIn');
        expect(channelWords('<b>sms</b>')).toBe('<b>sms</b>');
        expect(toApprovalDetail({ highlights: { '/args/channel': 'whatsapp' } }).channel).toBe('whatsapp');
    });

    it("answers a code that names a property every object has with the code, never the object's property", () => {
        for (const code of ['constructor', '__proto__', 'toString', 'hasOwnProperty']) {
            expect(channelWords(code)).toBe(code);
            expect(operationWords(code)).toBe(code);
        }
    });
});

describe('what a message approval reads for its card', () => {
    it('reads a template and the example the runtime made of it, and nothing of a text message', () => {
        const text = toApprovalDetail(fixtures['approval.get/email-message'].response.preview);
        expect(text).toMatchObject({ compose: null, example: null });
        const template = toApprovalDetail({
            highlights: { '/args/message': { format: 'email', compose: 'template', subject: 'Hi [x]', body: 'B' } },
            example: { subject: 'Hi Ines', body: 'B', unfilled: ['sender.first_name', 7] },
        });
        expect(template.compose).toBe('template');
        expect(template.example).toEqual({ subject: 'Hi Ines', body: 'B', unfilled: ['sender.first_name'] });
    });
});
