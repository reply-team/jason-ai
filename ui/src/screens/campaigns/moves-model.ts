// Pure: a campaign's move by its status, and the word each move is said by. The agent's header, a campaign's chat and
// the console's menu on an agent's row offer the same move by the same word (`moves.tsx`, `pause-dialog.tsx`).

/** Pause stops an active campaign's work; Resume releases a paused one's again; Start releases a draft's for the first time. */
export type Transition = 'pause' | 'resume' | 'start';

/** Each move by the word its button and its dialog say. */
export const verbs: Record<Transition, string> = { pause: 'Pause', resume: 'Resume', start: 'Start' };

/**
 * Whether a draft's Start is held back: while the campaign has no brief, its context read and empty, and no one in it,
 * its count read and zero. There is nothing yet for a start to hand the manager. A draft with a brief keeps Start, with
 * people or without, since finding the audience is work the manager plans; a context or a count not read holds nothing
 * back. The rule is the page's: the runtime's `campaign.start` is unchanged.
 */
export function startHeld(
    campaign: { status: string; context?: Record<string, unknown> },
    people?: number | null,
): boolean {
    return (
        campaign.status === 'draft' &&
        campaign.context !== undefined &&
        campaign.context !== null &&
        Object.keys(campaign.context).length === 0 &&
        people === 0
    );
}

/**
 * What the runtime lets this campaign's status move to: an active one pauses, a paused one resumes and a draft starts,
 * each through `campaign.pause` or `campaign.start`; an archived campaign is over and moves nowhere. A draft with no brief
 * and no one in it offers no move yet (`startHeld`), where the place knows both.
 */
export function transitions(
    campaign: { status: string; context?: Record<string, unknown> },
    people?: number | null,
): Transition[] {
    if (campaign.status === 'active') return ['pause'];
    if (campaign.status === 'paused') return ['resume'];
    if (campaign.status === 'draft') return startHeld(campaign, people) ? [] : ['start'];
    return [];
}
