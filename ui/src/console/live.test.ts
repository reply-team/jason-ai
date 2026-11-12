import { describe, expect, it } from 'vitest';
import type { JournalEntry } from '../api/types';
import type { DesktopHost, DesktopSetup } from '../platform/desktop';
import {
    ageOf,
    chatHosts,
    isSignInFailure,
    signInSentence,
    toActivity,
    toApprovalDetail,
    toApprovalRow,
    toCampaignRow,
    toQuestionRow,
} from './live';

const now = Date.parse('2026-10-16T09:00:00.000Z');

const host = (fields: Partial<DesktopHost>): DesktopHost => ({
    id: 'claude',
    label: 'Claude Code',
    kind: 'program',
    found: true,
    version: '2.1.300',
    drivesChat: true,
    install: null,
    models: [],
    typedModel: false,
    modelsUnread: null,
    belowFloor: null,
    fullAccess: null,
    ...fields,
});

describe('what the console draws from the runtime', () => {
    it('says how long ago, in the console words', () => {
        expect(ageOf('2026-10-16T08:59:40.000Z', now)).toBe('now');
        expect(ageOf('2026-10-16T08:15:00.000Z', now)).toBe('45 min');
        expect(ageOf('2026-10-16T04:00:00.000Z', now)).toBe('5 h');
        expect(ageOf('2026-10-13T09:00:00.000Z', now)).toBe('3 d');
        expect(ageOf(null, now)).toBe('');
    });

    it('shows a campaign by its state and its counts, and a campaign nobody counted as zero', () => {
        expect(
            toCampaignRow(
                { id: 'cmp_1', name: 'Autumn outreach', status: 'active' },
                {
                    campaign_id: 'cmp_1',
                    people: 23,
                    waiting: 2,
                    failed: 3,
                    working: 3,
                    new: 3,
                    idle: 6,
                    sequence: 2,
                    out: 6,
                    removed: 1,
                    sequence_linked: true,
                },
            ),
        ).toEqual({
            id: 'cmp_1',
            name: 'Autumn outreach',
            state: 'Active',
            tone: 'green',
            people: 23,
            waiting: 2,
            failed: 3,
        });
        expect(toCampaignRow({ id: 'cmp_2', name: 'B', status: 'paused' }, undefined)).toMatchObject({
            state: 'Paused',
            tone: 'amber',
            people: 0,
            waiting: 0,
        });
        expect(toCampaignRow({ id: 'cmp_3', name: 'C', status: 'something_new' }, undefined).state).toBe('Draft');
    });

    it('names an approval by what it does, to whom and where', () => {
        const row = toApprovalRow(
            {
                id: 'apr_1',
                work_item_id: 'wi_1',
                campaign_id: 'cmp_1',
                operation: 'campaign.enroll',
                status: 'pending',
                reason: 'approval_required',
                plugin_id: 'reply',
                requested_at: '2026-10-15T12:00:00.000Z',
                decided_at: null,
                decided_by: null,
                campaign_name: 'Autumn outreach',
                contact_name: 'A Person',
            },
            now,
        );
        expect(row).toEqual({
            id: 'apr_1',
            what: 'Add to sequence',
            who: 'A Person',
            byMessage: false,
            campaign: 'Autumn outreach',
            via: 'reply',
            age: '21 h',
        });
    });

    it('reads what an approval would do from its preview, and nothing it does not carry', () => {
        expect(
            toApprovalDetail({
                intent: 'Put a person into the campaign at the provider.',
                contact: { name: 'A Person', channel: 'email', value: 'person@example.test' },
                reach: { value: 'act', conditional: true, detail: 'bookkeeping until the campaign is live' },
                reversibility: { value: 'irreversible' },
            }),
        ).toEqual({
            intent: 'Put a person into the campaign at the provider.',
            to: 'A Person, person@example.test',
            from: null,
            channel: null,
            subject: null,
            body: null,
            facts: ['Reach: act (bookkeeping until the campaign is live)', 'Reversibility: irreversible'],
        });
        expect(
            toApprovalDetail({ highlights: { '/args/message': { subject: 'Hello', body: 'Two lines\nof text' } } }),
        ).toMatchObject({ subject: 'Hello', body: 'Two lines\nof text', to: null, intent: null });
    });

    it('asks a question as it was raised', () => {
        expect(
            toQuestionRow(
                {
                    id: 'dec_1',
                    campaign_id: 'cmp_1',
                    work_item_id: 'wi_1',
                    status: 'pending',
                    question: 'Pause?',
                    raised_at: '2026-10-16T08:00:00.000Z',
                    answered_at: null,
                    answered_by: null,
                    campaign_name: 'Autumn outreach',
                    contact_name: null,
                },
                now,
            ),
        ).toEqual({ id: 'dec_1', question: 'Pause?', campaign: 'Autumn outreach', age: '1 h' });
    });

    it('tells a journal line by its kind, its campaign, its person and who wrote it', () => {
        const line: JournalEntry = {
            id: 'jrn_1',
            ts: '2026-10-16T08:58:00.000Z',
            actor: { type: 'agent', id: 'manager' },
            kind: 'contact_added',
            key: null,
            old: null,
            new: null,
            reason: null,
            campaign_id: 'cmp_1',
            work_item_id: null,
            contact_id: 'cnt_1',
            campaign_name: 'Autumn outreach',
            contact_name: 'A Person',
            work_item_kind: null,
            work_item_role: null,
            work_item_operation: null,
            actor_role: null,
        };
        expect(toActivity(line, now)).toEqual({
            id: 'jrn_1',
            age: '2 min',
            what: 'Contact added',
            campaign: 'Autumn outreach',
            contact: 'A Person',
            who: 'manager',
        });
        expect(toActivity({ ...line, actor: { type: 'system', id: null }, contact_name: null }, now)).toMatchObject({
            who: 'system',
            contact: null,
        });
    });

    it('offers the chat the hosts that drive it, and lists the rest as running campaign work', () => {
        const setup: Pick<DesktopSetup, 'hosts'> = {
            hosts: [
                host({}),
                host({ id: 'gemini', label: 'Gemini CLI', drivesChat: false }),
                host({ id: 'codex', label: 'Codex', found: false, version: null, install: 'codex' }),
                host({ id: 'deepseek', label: 'DeepSeek', kind: 'provider', version: null }),
            ],
        };
        const { drive, other } = chatHosts(setup.hosts);
        expect(drive.map((each) => each.id)).toEqual(['claude', 'deepseek']);
        expect(other.map((each) => each.id)).toEqual(['gemini']);
    });

    it('holds aside, listed but not offered, a host the desktop marks below the floor, with its sentence', () => {
        const old = 'Claude Code 2.1.200 is older than 2.1.257, the oldest version the chat runs on.';
        const { drive, held, other } = chatHosts([
            host({ version: '2.1.200', belowFloor: old }),
            host({ id: 'claude-api-key', label: 'Anthropic', kind: 'provider', version: null, belowFloor: old }),
            host({ id: 'codex', label: 'Codex' }),
            host({ id: 'gemini', label: 'Gemini CLI', drivesChat: false }),
            host({ id: 'deepseek', label: 'DeepSeek', kind: 'provider', found: false, version: null, belowFloor: old }),
        ]);
        expect(drive.map((each) => each.id)).toEqual(['codex']);
        expect(held.map((each) => [each.id, each.belowFloor])).toEqual([
            ['claude', old],
            ['claude-api-key', old],
        ]);
        expect(other.map((each) => each.id)).toEqual(['gemini']);
    });

    it('says how a program is signed in to, and knows a turn that failed on it', () => {
        expect(signInSentence(host({}))).toBe(
            'Sign in once by running `claude` in a terminal; the chat uses that sign-in.',
        );
        expect(signInSentence(host({ kind: 'provider' }))).toBeNull();
        expect(signInSentence(host({ found: false }))).toBeNull();
        expect(isSignInFailure('Invalid API key · Please run /login')).toBe(true);
        expect(isSignInFailure('Not logged in')).toBe(true);
        expect(isSignInFailure('401 Unauthorized')).toBe(true);
        expect(isSignInFailure('The network is unreachable')).toBe(false);
    });
});
