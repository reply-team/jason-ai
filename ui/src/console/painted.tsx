import { effect } from '@preact/signals-react';
import { useEffect, useRef } from 'react';
import { paint, type Html } from './html';

/**
 * A region the console paints from its markup: `view` is run again whenever a signal it read changes. Pass a function
 * whose identity is stable, or the region is painted afresh on every render.
 */
export function Painted({ view, className }: { view: () => Html; className?: string }) {
    const ref = useRef<HTMLDivElement>(null);
    useEffect(
        () =>
            effect(() => {
                const markup = view();
                if (ref.current) paint(ref.current, markup);
            }),
        [view],
    );
    return <div ref={ref} className={className} />;
}
