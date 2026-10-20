import { useRef } from 'preact/hooks';
import { useLoadMoreOnScroll, type LoadMore } from './use-cursor-list';

/** The end of a list that has more: it loads the next page when it scrolls into view, or when clicked. */
export function More({ more }: { more: LoadMore | null }) {
    const sentinel = useRef<HTMLDivElement>(null);
    useLoadMoreOnScroll(sentinel, more);
    if (!more) return null;
    return (
        <div ref={sentinel} class="end loading" role="button" tabIndex={0} onClick={() => void more()}>
            loading…
        </div>
    );
}
