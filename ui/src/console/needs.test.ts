import { describe, expect, it } from 'vitest';
import { fixtures } from '../test/fixtures';
import { toApprovalDetail, toApprovalRow } from './live';

describe('what Needs you reads from an approval', () => {
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
});
