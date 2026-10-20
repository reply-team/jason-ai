import { useEffect, useRef } from 'preact/hooks';

/** The next page: what a list calls when its end comes into view, and the answer it waits for before looking again. */
export type LoadMore = () => void | Promise<void>;

/**
 * Loads the next page when the sentinel scrolls into view. Without IntersectionObserver (a test DOM), the sentinel
 * is a control the test clicks; either way the page never shows page buttons.
 */
export function useLoadMoreOnScroll(sentinel: { current: HTMLElement | null }, more: LoadMore | null): void {
    const latest = useRef(more);
    latest.current = more;
    useEffect(() => {
        const node = sentinel.current;
        if (!node || typeof IntersectionObserver === 'undefined') return;
        // An observer speaks only when the sentinel crosses into view. After a page arrives the sentinel can still be in
        // view (a tall window, or a browser keeping it where it was while the rows above it grew), and then it would
        // never cross again; so once the page is in, the sentinel is watched afresh, which reports where it is now.
        const observer = new IntersectionObserver((entries) => {
            if (!entries.some((e) => e.isIntersecting)) return;
            const pending = latest.current?.();
            if (pending instanceof Promise) {
                void pending.then(() => {
                    observer.unobserve(node);
                    observer.observe(node);
                });
            }
        });
        observer.observe(node);
        return () => observer.disconnect();
        // The sentinel exists only while there is more to load, so the watch starts again whenever that changes: a list
        // that mounts empty and gets its cursor later is watched from the moment its sentinel appears.
    }, [sentinel, more !== null]);
}
