import { describe, expect, it } from 'vitest';
import { createBody } from '../screens/campaigns/kind';
import { archivedLeftOut } from '../screens/campaigns/store';
import { agentHeads } from './agent-head';
import { FakeApi } from './fake-api';
import { fixtures } from './fixtures';

// The header's reads are answered for tests of something else, in the very shapes the header asks them; any other read
// of the same operations stays the test's to answer, and unanswered fails it.

describe('the header’s reads, answered for tests of something else', () => {
    it('answers the work-item list only in the shape the header asks whether a provider campaign is being made', async () => {
        const fake = agentHeads(new FakeApi());

        expect(await fake.call('workitem.list', createBody('cmp_2'))).toEqual(
            fixtures['workitem.list/creating-none'].response,
        );
        await expect(fake.call('workitem.list', { ...createBody('cmp_2'), limit: 10 })).rejects.toThrow(
            /nothing recorded/,
        );
        await expect(fake.call('workitem.list', { campaign_id: 'cmp_2', kind: 'provider_op' })).rejects.toThrow(
            /nothing recorded/,
        );
        expect(fake.unanswered).toHaveLength(2);
    });

    it('answers one agent’s people alone in the header’s shape, and no other summary', async () => {
        const fake = agentHeads(new FakeApi());

        await fake.call('campaign.summary', { campaign_ids: ['cmp_4'], ...archivedLeftOut });
        await expect(fake.call('campaign.summary', { campaign_ids: ['cmp_4'] })).rejects.toThrow(/nothing recorded/);
        await expect(
            fake.call('campaign.summary', { campaign_ids: ['cmp_1', 'cmp_2'], ...archivedLeftOut }),
        ).rejects.toThrow(/nothing recorded/);
        expect(fake.unanswered).toHaveLength(2);
    });

    it('reads an agent by its id alone, an id it does not know as gone, and nothing else of campaign.get', async () => {
        const fake = agentHeads(new FakeApi());

        expect(await fake.call('campaign.get', { campaign_id: 'cmp_1' })).toEqual(
            fixtures['campaign.get/details'].response,
        );
        await expect(fake.call('campaign.get', { campaign_id: 'cmp_gone' })).rejects.toMatchObject({ status: 404 });
        await expect(fake.call('campaign.get', { campaign_id: 'cmp_1', with: 'numbers' })).rejects.toThrow(
            /nothing recorded/,
        );
        expect(fake.unanswered).toHaveLength(1);
    });
});
