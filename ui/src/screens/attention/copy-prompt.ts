import type { Api } from '../../api/client';
import type { Campaign, Project, SystemInfo } from '../../api/types';

/**
 * The text that briefs an agent session on one item that waits for a person, the same words `jason approval get --prompt`
 * and `jason decision get --prompt` print (docs/prompts/approval.txt and question.txt hold both to one text). It is made
 * of identifiers and a folder alone: nothing of the item, because the session reads the item live.
 */

export type PromptKind = 'approval' | 'question';
export type PromptFolderLevel = 'campaign' | 'project' | 'harness_root';
export type PromptFolder = { level: PromptFolderLevel; path: string };

/** The hosts `jason skills install --host` takes, as the command line lists them; the published texts hold this line to the command line's. */
export const agentHosts = 'claude_code, codex, copilot_cli, cursor_cli, gemini_cli, antigravity_cli';

const folderLines: Record<PromptFolderLevel, { label: string; whose: string }> = {
    campaign: { label: 'Campaign folder', whose: 'campaign' },
    project: { label: 'Project folder (the campaign has none recorded)', whose: 'project' },
    harness_root: { label: 'Harness root (no folder is recorded for the campaign or its project)', whose: 'client' },
};

export function composePrompt(
    kind: PromptKind,
    itemId: string,
    campaignId: string,
    workItemId: string,
    folder: PromptFolder | null,
): string {
    const approval = kind === 'approval';
    const lines = [
        approval
            ? `Jason is waiting for a person's decision on an approval: ${itemId}, in campaign ${campaignId}.`
            : `Jason is waiting for a person's answer to a question: ${itemId}, in campaign ${campaignId}.`,
    ];
    if (folder) {
        const { label, whose } = folderLines[folder.level];
        lines.push(
            '',
            `${label}: ${folder.path}`,
            `Read its memory file, AGENTS.md: it holds what the person keeps about this ${whose}. If you can, work from that folder.`,
        );
    }
    lines.push(
        '',
        `Use the skill approvals-and-questions. If your agent host does not have it, install the skills for the host you are running in with jason skills install --host <host>, where <host> is one of: ${agentHosts}. Then start the skill.`,
        '',
        'What to do:',
        `1. Read the ${approval ? 'approval' : 'question'}: jason ${approval ? 'approval' : 'decision'} get ${itemId}`,
        `2. Read the campaign and its timeline: jason campaign get ${campaignId} and jason journal list --campaign ${campaignId}`,
        `3. Read the work item that raised it: jason workitem get ${workItemId}; it names its role. Then that role's note, jason rolenote get --role <role> --campaign ${campaignId}, and the other roles' as you need them (jason rolenote list --campaign ${campaignId}).`,
        '4. Research whatever else you need to understand the request.',
        `5. Come back to the person with a short summary and a proposed ${approval ? 'decision' : 'answer'}, with your reasons.`,
        '',
        "You decide nothing. Approving, rejecting and answering stay with the person, and so does a standing approval: setting one is the person's own act. So does how messages are sent: a campaign's send mode and the preferences send_mode.cold and send_mode.warm are the person's to set. Do not run jason approval approve, jason approval reject, jason approval set-standing, jason decision answer, jason workitem review, jason campaign set-send-mode, jason preference set or jason preference unset.",
    );
    return lines.join('\n');
}

/**
 * Where the session should stand: the campaign's recorded folder, else its project's, else the harness root the runtime
 * reports. Each step is a read that exists, asked only as far as needed. Only an answer that carries no folder falls
 * through to the next level; a read that did not answer ends the resolution, because a lower level named after it
 * would be labelled "no folder is recorded", which nobody has said. No folder at all is a prompt without the folder
 * lines, never a failure. The root is named only when it is a folder with the root's marker.
 */
export async function resolveFolder(api: Api, campaignId: string): Promise<PromptFolder | null> {
    const campaign = await tryRead<Campaign>(api, 'campaign.get', { campaign_id: campaignId });
    if (!campaign) return null;
    if (campaign.folder_path) return { level: 'campaign', path: campaign.folder_path };
    if (campaign.project_id) {
        const project = await tryRead<Project>(api, 'project.get', { project_id: campaign.project_id });
        if (!project) return null;
        if (project.folder_path) return { level: 'project', path: project.folder_path };
    }
    const info = await tryRead<SystemInfo>(api, 'system.info');
    const harness = info?.harness;
    return harness?.root && harness.root_state === 'marked' ? { level: 'harness_root', path: harness.root } : null;
}

async function tryRead<T>(api: Api, operation: string, body?: object): Promise<T | null> {
    try {
        return await api.call<T>(operation, body);
    } catch {
        return null;
    }
}
