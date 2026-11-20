// Pure: a campaign's move by its status, and the word each move is said by. The agent's header, a campaign's chat and
// the console's menu on an agent's row offer the same move by the same word (`moves.tsx`, `pause-dialog.tsx`).

/** Pause stops an active campaign's work; Resume releases a paused one's again; Start releases a draft's for the first time. */
export type Transition = 'pause' | 'resume' | 'start';

/** Each move by the word its button and its dialog say. */
export const verbs: Record<Transition, string> = { pause: 'Pause', resume: 'Resume', start: 'Start' };

/**
 * What the runtime lets this campaign's status move to: an active one pauses, a paused one resumes and a draft starts,
 * each through `campaign.pause` or `campaign.start`; an archived campaign is over and moves nowhere.
 */
export function transitions(campaign: { status: string }): Transition[] {
    if (campaign.status === 'active') return ['pause'];
    if (campaign.status === 'paused') return ['resume'];
    if (campaign.status === 'draft') return ['start'];
    return [];
}
