import { useEffect, useRef, useState, type ReactNode } from 'react';

/**
 * A box around a table that scrolls sideways where the table is wider than the room. Only while it scrolls is it a named
 * region in the keyboard's order, so a keyboard reaches the columns it hides; where the table fits it is a plain box.
 */
export function ScrollBox({ label, children }: { label: string; children: ReactNode }) {
    const box = useRef<HTMLDivElement>(null);
    const [scrolls, setScrolls] = useState(false);
    useEffect(() => {
        const element = box.current;
        if (!element) return;
        const measure = () => setScrolls(element.scrollWidth > element.clientWidth + 1);
        measure();
        if (typeof ResizeObserver === 'undefined') return;
        const watch = new ResizeObserver(measure);
        watch.observe(element);
        if (element.firstElementChild) watch.observe(element.firstElementChild);
        return () => watch.disconnect();
    }, []);
    return (
        <div ref={box} className="tablewrap" {...(scrolls ? { role: 'region', tabIndex: 0, 'aria-label': label } : {})}>
            {children}
        </div>
    );
}
