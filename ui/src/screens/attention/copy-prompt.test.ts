import { describe, expect, it } from 'vitest';
import type { Api } from '../../api/client';
import { composePrompt, resolveFolder } from './copy-prompt';

/** An Api that answers each operation with what the test gives it, or fails it, and remembers the order it was asked in. */
function stub(answers: Record<string, unknown>) {
    const asked: string[] = [];
    const api: Api = {
        call<T>(operation: string): Promise<T> {
            asked.push(operation);
            return operation in answers
                ? Promise.resolve(answers[operation] as T)
                : Promise.reject(new Error('refused'));
        },
    };
    return { api, asked };
}

describe('resolveFolder', () => {
    it("takes the campaign's folder and asks nothing further", async () => {
        const { api, asked } = stub({ 'campaign.get': { project_id: 'prj_1', folder_path: '/c' } });
        expect(await resolveFolder(api, 'cmp_1')).toEqual({ level: 'campaign', path: '/c' });
        expect(asked).toEqual(['campaign.get']);
    });

    it("falls back to the project's folder, then the harness root", async () => {
        const noCampaignFolder = { project_id: 'prj_1', folder_path: null };
        const project = stub({ 'campaign.get': noCampaignFolder, 'project.get': { folder_path: '/p' } });
        expect(await resolveFolder(project.api, 'cmp_1')).toEqual({ level: 'project', path: '/p' });
        expect(project.asked).toEqual(['campaign.get', 'project.get']);

        const root = stub({
            'campaign.get': noCampaignFolder,
            'project.get': { folder_path: null },
            'system.info': { harness: { root: '/h', root_state: 'marked' } },
        });
        expect(await resolveFolder(root.api, 'cmp_1')).toEqual({ level: 'harness_root', path: '/h' });
        expect(root.asked).toEqual(['campaign.get', 'project.get', 'system.info']);
    });

    it('is no folder, never a failure, when every read fails or the runtime has no harness section', async () => {
        expect(await resolveFolder(stub({}).api, 'cmp_1')).toBeNull();
        const older = stub({
            'campaign.get': { project_id: 'prj_1', folder_path: null },
            'project.get': { folder_path: null },
            'system.info': {},
        });
        expect(await resolveFolder(older.api, 'cmp_1')).toBeNull();
    });

    it('ends with no folder when a read did not answer, though a lower level would', async () => {
        const root = { harness: { root: '/h', root_state: 'marked' } };
        const campaignFailed = stub({ 'system.info': root });
        expect(await resolveFolder(campaignFailed.api, 'cmp_1')).toBeNull();
        expect(campaignFailed.asked).toEqual(['campaign.get']);

        const projectFailed = stub({ 'campaign.get': { project_id: 'prj_1', folder_path: null }, 'system.info': root });
        expect(await resolveFolder(projectFailed.api, 'cmp_1')).toBeNull();
        expect(projectFailed.asked).toEqual(['campaign.get', 'project.get']);
    });

    it.each(['absent', 'unmarked', 'taken', 'not_a_folder', 'unreadable'])(
        'does not name a harness root whose state is %s',
        async (rootState) => {
            const { api } = stub({
                'campaign.get': { project_id: 'prj_1', folder_path: null },
                'project.get': { folder_path: null },
                'system.info': { harness: { root: '/h', root_state: rootState } },
            });
            expect(await resolveFolder(api, 'cmp_1')).toBeNull();
        },
    );
});

describe('composePrompt', () => {
    it('leaves out the folder lines and the memory file sentence when there is no folder', () => {
        const text = composePrompt('approval', 'apr_1', 'cmp_1', 'wi_1', null);
        expect(text).not.toMatch(/folder|AGENTS\.md/i);
        expect(text).toContain('jason approval get apr_1');
    });

    // The same three label lines and three words as CopyPromptTests.cs holds.
    it.each([
        ['campaign', 'Campaign folder: /p', 'campaign'],
        ['project', 'Project folder (the campaign has none recorded): /p', 'project'],
        ['harness_root', 'Harness root (no folder is recorded for the campaign or its project): /p', 'client'],
    ] as const)('says which level a %s folder is', (level, line, whose) => {
        const text = composePrompt('question', 'dec_1', 'cmp_1', 'wi_1', { level, path: '/p' });
        expect(text).toContain(`\n${line}\n`);
        expect(text).toContain(
            `Read its memory file, AGENTS.md: it holds what the person keeps about this ${whose}. If you can, work from that folder.\n`,
        );
    });
});
