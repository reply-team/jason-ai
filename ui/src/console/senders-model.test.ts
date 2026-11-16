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
    type SenderAsked,
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
    started: 1,
    provider: 'reply',
    accounts: [],
    pages: 1,
    hasMore: false,
    failed: null,
    ...over,
});

const approval = (args: unknown, pluginId = 'reply'): Approval =>
    ({ campaign_id: 'cmp_1', plugin_id: pluginId, subject: { input: { args } } }) as unknown as Approval;

/** What a line asks for an email sent by the Reply plugin from the account named. */
const of = (externalId: string): SenderAsked => ({
    campaignId: 'cmp_1',
    channel: 'email',
    externalId,
    pluginId: 'reply',
});

describe('what an approval’s sender line reads', () => {
    it('is the approval’s campaign, its email, the account it names and the plugin that sends it', () => {
        expect(
            senderAsked(approval({ channel: 'email', sender: { external_id: 's_12', address: 'o@example.test' } })),
        ).toEqual({ campaignId: 'cmp_1', channel: 'email', externalId: 's_12', pluginId: 'reply' });
    });

    it('is nothing for an invitation or a LinkedIn message, whose account is no mailbox', () => {
        expect(
            senderAsked(approval({ channel: 'linkedin', sender: { external_id: 's_34', address: null } })),
        ).toBeNull();
    });

    it('is nothing for an approval whose input names no account', () => {
        expect(senderAsked(undefined)).toBeNull();
        expect(senderAsked(approval({ body: 'Thanks' }))).toBeNull();
        expect(senderAsked(approval({ channel: 'email' }))).toBeNull();
        expect(senderAsked(approval({ channel: 'email', sender: { external_id: '' } }))).toBeNull();
    });
});

describe('what the line says', () => {
    it('names the account’s sending name, and says the provider holds none for an empty one', () => {
        expect(senderLine(lookup({ accounts: [account('s_1', 'Ada Example')] }), of('s_1'))).toEqual({
            kind: 'named',
            provider: 'Reply',
            name: 'Ada Example',
        });
        expect(senderLine(lookup({ accounts: [account('s_1', null)] }), of('s_1'))?.kind).toBe('unnamed');
        expect(senderLine(lookup({ accounts: [account('s_1', '  ')] }), of('s_1'))?.kind).toBe('unnamed');
    });

    it('says nothing while more pages may hold the account, and that it is not listed once none do', () => {
        expect(senderLine(undefined, of('s_1'))).toBeNull();
        expect(senderLine(lookup({ hasMore: true }), of('s_1'))).toBeNull();
        expect(senderLine(lookup({ hasMore: false }), of('s_1'))?.kind).toBe('unlisted');
    });

    it('says why it could not read the name when the pages it reads run out before the list does, never that it is not listed', () => {
        const line = senderLine(lookup({ hasMore: true, pages: SENDER_PAGES }), of('s_1'))!;
        expect(line).toEqual({ kind: 'unread', said: null });
        expect(senderWords(line)?.text).toBe(
            'The sending name could not be read: the provider lists more accounts than the 5 pages read.',
        );
    });

    it('keeps an account found on an earlier page when a later page failed', () => {
        const failed = { code: 'no_route', message: 'No route' };
        const read = lookup({ accounts: [account('s_1', 'Ada Example')], hasMore: true, failed });
        expect(senderLine(read, of('s_1'))?.kind).toBe('named');
        expect(senderLine(read, of('s_2'))).toEqual({ kind: 'unread', said: failed });
    });

    it('names no account another plugin listed than the one that sends, even one with the same identifier', () => {
        const other = lookup({ provider: 'acme-mail', accounts: [account('s_1', 'Ada Example')] });
        const line = senderLine(other, of('s_1'))!;
        expect(line).toEqual({ kind: 'elsewhere' });
        expect(senderWords(line)).toBeNull();
    });

    it('names a provider Jason does not ship by its plugin’s id', () => {
        const asked = { ...of('s_1'), pluginId: 'acme-mail' };
        const line = senderLine(lookup({ provider: 'acme-mail', accounts: [account('s_1', null)] }), asked)!;
        expect(senderWords(line)?.text).toBe('acme-mail holds no sending name for this mailbox.');
    });
});

const first = { campaign_id: 'cmp_1', owners: 'team', sender_channel: 'email', limit: 100 };
const page = (items: SenderAccount[], more: boolean): SenderListResponse => ({
    plugin_id: 'reply',
    items,
    has_more: more,
});

/** The requests a line reads for an account, given the pages in turn, until it is decided or the pages run out. */
function read(externalId: string, pages: (SenderListResponse | null)[], start?: SenderLookup) {
    const sent: object[] = [];
    let found = start;
    for (const each of pages) {
        const body = nextSenderRequest(found, of(externalId));
        if (body === null) break;
        sent.push(body);
        found =
            each === null ? withFailure(found, { code: 'no_route', message: 'No route' }, 1) : withPage(found, each, 1);
    }
    return { sent, lookup: found, next: nextSenderRequest(found, of(externalId)) };
}

describe('reading the accounts', () => {
    it('reads the whole team’s first page, then the next at the count of accounts received while the account is not found', () => {
        const full = Array.from({ length: 100 }, (_, n) => account(`s_${n + 100}`, 'One'));
        const done = read('s_7', [page(full, true), page([account('s_7', 'Seven')], true)]);
        expect(done.sent).toEqual([first, { ...first, offset: 100 }]);
        expect(done.next).toBeNull();
        expect(senderLine(done.lookup, of('s_7'))?.kind).toBe('named');
    });

    it('reads on from a short page at the count it returned, not at a page’s length', () => {
        const done = read('s_7', [page([account('s_1', 'One')], true), page([account('s_2', 'Two')], true)]);
        expect(done.sent).toEqual([first, { ...first, offset: 1 }]);
        expect(done.next).toEqual({ ...first, offset: 2 });
    });

    it('stops after five pages', () => {
        const done = read(
            's_7',
            Array.from({ length: 6 }, (_, n) => page([account(`s_${n + 100}`, 'One')], true)),
        );
        expect(done.sent).toHaveLength(SENDER_PAGES);
        expect(done.next).toBeNull();
    });

    it('reads nothing more for an account the pages read already hold, and reads on for one they do not', () => {
        const held = read('s_1', [page([account('s_1', 'One'), account('s_2', null)], true)]).lookup!;
        expect(nextSenderRequest(held, of('s_2'))).toBeNull();
        expect(nextSenderRequest(held, of('s_7'))).toEqual({ ...first, offset: 2 });
    });

    it('keeps what the runtime said when a page fails, says it, and asks no more', () => {
        const done = read('s_7', [null, page([account('s_7', 'Seven')], false)]);
        expect(done.sent).toEqual([first]);
        expect(done.next).toBeNull();
        expect(senderWords(senderLine(done.lookup, of('s_7'))!)).toEqual({
            text: 'The sending name could not be read. The runtime said:',
            code: 'no_route',
            message: 'No route',
            when: false,
        });
    });

    it('marks a read with the moment it began, and keeps that mark on every page after the first', () => {
        const once = withPage(undefined, page([account('s_1', 'One')], true), 7);
        expect(withPage(once, page([], false), 9).started).toBe(7);
        expect(withFailure(once, { code: null, message: 'x' }, 9).started).toBe(7);
    });
});
