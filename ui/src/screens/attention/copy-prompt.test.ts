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
});

describe('composePrompt', () => {
    it('leaves out the folder lines and the memory file sentence when there is no folder', () => {
        const text = composePrompt('approval', 'apr_1', 'cmp_1', 'wi_1', null);
        expect(text).not.toMatch(/folder|AGENTS\.md/i);
        expect(text).toContain('jason approval get apr_1');
    });

    it('names the level of a folder above the campaign', () => {
        expect(composePrompt('question', 'dec_1', 'cmp_1', 'wi_1', { level: 'project', path: '/p' })).toContain(
            'Project folder (the campaign has none recorded): /p',
        );
        expect(composePrompt('question', 'dec_1', 'cmp_1', 'wi_1', { level: 'harness_root', path: '/h' })).toContain(
            'about this client',
        );
    });
});
