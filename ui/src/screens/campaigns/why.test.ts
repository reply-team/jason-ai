import { describe, expect, it } from 'vitest';
import type { MembershipWhy } from '../../api/types';
import { happened } from './why';

const why = (code: string | null, text: string | null = null): MembershipWhy => ({
    kind: 'activity',
    text,
    code,
    at: '2026-10-16T08:50:00.000Z',
    approval_id: null,
    decision_id: null,
});

describe('what a count says happened', () => {
    it('says a message the account sent from the account, under Contacted and under People', () => {
        expect(happened(why('message_sent'), 'contacted')).toBe('sent from the account');
        expect(happened(why('message_sent', 'contacted'), 'people')).toBe('sent from the account');
    });

    it('keeps the operation of a contacting act, and says contacted where the why names no code', () => {
        expect(happened(why('message.send'), 'contacted')).toBe('message.send');
        expect(happened(why(null, 'contacted'), 'people')).toBe('contacted');
    });
});
