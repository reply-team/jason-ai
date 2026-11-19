/**
 * How far above the followed place a conversation still counts as at its foot, in pixels: within that, the thread follows
 * what is added at the end of the conversation; further up, the place is the person's. The Demo's thread measures the
 * same 120 px (`desktop/demo/index.html`, `THREAD_AT`): the two share the measure, while what each follows is its own.
 */
export const footSlack = 120;

/**
 * A scrolling box as the follow rule reads it: what it holds (`scrollHeight`), its view (`clientHeight`), the end of the
 * conversation in its own coordinates (`foot`: the bottom of the thread and the line queued under it, never the cards
 * that wait under them) and how much of the conversation is kept in view above the cards (`keep`, half the view).
 */
export type Laid = { scrollHeight: number; clientHeight: number; foot: number; keep: number };

/**
 * The place a followed conversation stands at: the box's own foot while nothing tall stands under the conversation, and
 * otherwise the conversation's last `keep` at the top of the view with the first cards under it; never above the top.
 */
export function followedTop({ scrollHeight, clientHeight, foot, keep }: Laid): number {
    return Math.max(0, Math.min(scrollHeight - clientHeight, foot - keep));
}

/**
 * Whether a box scrolled to `scrollTop` stands at the foot of its conversation: no more than `footSlack` above the
 * followed place (`followedTop`). Below it, reading what waits under the conversation, is at it too.
 */
export function atFoot(scrollTop: number, followed: number): boolean {
    return followed - scrollTop <= footSlack;
}
