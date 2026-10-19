import { useEffect, useRef } from 'preact/hooks';

/**
 * Loads the next page when the sentinel scrolls into view. Without IntersectionObserver (a test DOM), the sentinel
 * is a control the test clicks; either way the page never shows page buttons.
 */
export function useLoadMoreOnScroll(sentinel: { current: HTMLElement | null }, more: (() => void) | null): void {
    const latest = useRef(more);
    latest.current = more;
    useEffect(() => {
        const node = sentinel.current;
        if (!node || typeof IntersectionObserver === 'undefined') return;
        const observer = new IntersectionObserver((entries) => {
            if (entries.some((e) => e.isIntersecting)) latest.current?.();
        });
        observer.observe(node);
        return () => observer.disconnect();
    }, [sentinel]);
}
