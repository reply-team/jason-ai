import { effect } from '@preact/signals-react';
import { useEffect, useRef } from 'react';
import { paint, type Html } from './html';

/**
 * A region the console paints from its markup: `view` is run again whenever a signal it read changes. Pass a function
 * whose identity is stable, or the region is painted afresh on every render. `onGone` is told when the region stops
 * painting that view: it is taken off the page, or given another view.
 */
export function Painted({ view, className, onGone }: { view: () => Html; className?: string; onGone?: () => void }) {
    const ref = useRef<HTMLDivElement>(null);
    const gone = useRef(onGone);
    gone.current = onGone;
    useEffect(() => {
        const stop = effect(() => {
            const markup = view();
            if (ref.current) paint(ref.current, markup);
        });
        return () => {
            stop();
            gone.current?.();
        };
    }, [view]);
    return <div ref={ref} className={className} />;
}
