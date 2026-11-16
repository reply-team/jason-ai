import { describe, expect, it } from 'vitest';
import type { Approval, SenderAccount, SenderListResponse } from '../api/types';
import {
    SENDER_PAGES,
    nextSenderRequest,
    senderAsked,
    senderLine,
    senderWords,
    withFailure,
    withPage,
    type SenderLookup,
} from './senders-model';

const account = (externalId: string, name: string | null): SenderAccount => ({
    external_id: externalId,
    channel: 'email',
    name,
    address: `${externalId}@example.test`,
    status: 'active',
    owner: 'own',
});

const lookup = (over: Partial<SenderLookup>): SenderLookup => ({
    provider: 'reply',
    accounts: [],
    pages: 1,
    hasMore: false,
    failed: null,
    ...over,
});

const approval = (args: unknown): Approval =>
    ({ campaign_id: 'cmp_1', subject: { input: { args } } }) as unknown as Approval;

describe('what an approval’s sender line reads', () => {
    it('is the approval’s campaign, the channel its send consumes and the account it names', () => {
        expect(senderAsked(approval({ channel: 'linkedin', sender: { external_id: 's_34', address: null } }))).toEqual({
            campaignId: 'cmp_1',
            channel: 'linkedin',
            externalId: 's_34',
        });
    });

    it('is nothing for an approval whose input names no account, or no channel the read takes', () => {
        expect(senderAsked(undefined)).toBeNull();
        expect(senderAsked(approval({ body: 'Thanks' }))).toBeNull();
        expect(senderAsked(approval({ channel: 'email' }))).toBeNull();
        expect(senderAsked(approval({ channel: 'phone', sender: { external_id: 's_1' } }))).toBeNull();
        expect(senderAsked(approval({ channel: 'email', sender: { external_id: '' } }))).toBeNull();
    });
});

describe('what the line says', () => {
    it('names the account’s sending name, and says the provider holds none for an empty one', () => {
        expect(senderLine(lookup({ accounts: [account('s_1', 'Ada Example')] }), 's_1')).toEqual({
            kind: 'named',
            provider: 'Reply',
            name: 'Ada Example',
        });
        expect(senderLine(lookup({ accounts: [account('s_1', null)] }), 's_1')?.kind).toBe('unnamed');
        expect(senderLine(lookup({ accounts: [account('s_1', '  ')] }), 's_1')?.kind).toBe('unnamed');
    });

    it('says nothing while more pages may hold the account, and that it is not listed once none do', () => {
        expect(senderLine(undefined, 's_1')).toBeNull();
        expect(senderLine(lookup({ hasMore: true }), 's_1')).toBeNull();
        expect(senderLine(lookup({ hasMore: false }), 's_1')?.kind).toBe('unlisted');
    });

    it('says it could not read the name when the pages it reads run out before the list does, never that it is not listed', () => {
        expect(senderLine(lookup({ hasMore: true, pages: SENDER_PAGES }), 's_1')).toEqual({
            kind: 'unread',
            said: null,
        });
    });

    it('keeps an account found on an earlier page when a later page failed', () => {
        const failed = { code: 'no_route', message: 'No route' };
        const read = lookup({ accounts: [account('s_1', 'Ada Example')], hasMore: true, failed });
        expect(senderLine(read, 's_1')?.kind).toBe('named');
        expect(senderLine(read, 's_2')).toEqual({ kind: 'unread', said: failed });
    });

    it('names a provider Jason does not ship by its plugin’s id', () => {
        const line = senderLine(lookup({ provider: 'acme-mail', accounts: [account('s_1', null)] }), 's_1')!;
        expect(senderWords(line).text).toBe('acme-mail holds no sending name for this mailbox.');
    });
});

const asked = { campaignId: 'cmp_1', channel: 'email', externalId: 's_7' };
const first = { campaign_id: 'cmp_1', owners: 'team', sender_channel: 'email', limit: 100 };
const page = (items: SenderAccount[], more: boolean): SenderListResponse => ({
    plugin_id: 'reply',
    items,
    has_more: more,
});

/** The requests a line reads for an account, given the pages in turn, until it is decided or the pages run out. */
function read(externalId: string, pages: (SenderListResponse | null)[], start?: SenderLookup) {
    const sent: object[] = [];
    let lookup = start;
    for (const each of pages) {
        const body = nextSenderRequest(lookup, { ...asked, externalId });
        if (body === null) break;
        sent.push(body);
        lookup =
            each === null ? withFailure(lookup, { code: 'no_route', message: 'No route' }) : withPage(lookup, each);
    }
    return { sent, lookup, next: nextSenderRequest(lookup, { ...asked, externalId }) };
}

describe('reading the accounts', () => {
    it('reads the whole team’s first page, then the next by offset while the account is not found and there are more', () => {
        const done = read('s_7', [page([account('s_1', 'One')], true), page([account('s_7', 'Seven')], true)]);
        expect(done.sent).toEqual([first, { ...first, offset: 100 }]);
        expect(done.next).toBeNull();
        expect(senderLine(done.lookup, 's_7')?.kind).toBe('named');
    });

    it('stops after five pages', () => {
        const done = read(
            's_7',
            Array.from({ length: 6 }, () => page([account('s_1', 'One')], true)),
        );
        expect(done.sent).toHaveLength(SENDER_PAGES);
        expect(done.next).toBeNull();
    });

    it('reads nothing more for an account the pages read already hold, and reads on for one they do not', () => {
        const held = read('s_1', [page([account('s_1', 'One'), account('s_2', null)], true)]).lookup!;
        expect(nextSenderRequest(held, { ...asked, externalId: 's_2' })).toBeNull();
        expect(nextSenderRequest(held, asked)).toEqual({ ...first, offset: 100 });
    });

    it('keeps what the runtime said when a page fails, and asks no more', () => {
        const done = read('s_7', [null, page([account('s_7', 'Seven')], false)]);
        expect(done.sent).toEqual([first]);
        expect(done.next).toBeNull();
        expect(senderWords(senderLine(done.lookup, 's_7')!)).toEqual({
            text: 'The sending name could not be read now.',
            code: 'no_route',
            when: false,
        });
    });
});
