/**
 * How far above its foot a conversation still counts as at it, in pixels: within that, the thread follows what is added
 * at its foot; further up, the place is the person's. The Demo's thread keeps the same measure (`desktop/demo/index.html`,
 * `THREAD_AT`): one rule, not two.
 */
export const footSlack = 120;

/** Whether a scrolling box stands at its foot: no more than `footSlack` of what it holds lies below its view. */
export function atFoot(box: { scrollHeight: number; clientHeight: number; scrollTop: number }): boolean {
    return box.scrollHeight - box.scrollTop - box.clientHeight <= footSlack;
}
