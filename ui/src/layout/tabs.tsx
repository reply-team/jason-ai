import type { KeyboardEvent } from 'react';
import { href, navigate, type Route } from '../router';

/**
 * A panel's tabs, each one an address of its own: a click moves there without reloading, and Back comes back. From the
 * keyboard, Left and Right, Home and End move to a tab and open it, as the tabs of a tab list do. A tab may carry a count
 * after its name.
 */
export function Tabs<T extends string>({
    tabs,
    active,
    to,
}: {
    tabs: readonly { key: T; label: string; count?: string }[];
    active: T;
    to: (key: T) => Route;
}) {
    const keys = (event: KeyboardEvent<HTMLDivElement>) => {
        const all = [...event.currentTarget.querySelectorAll<HTMLAnchorElement>('[role="tab"]')];
        const at = all.indexOf(document.activeElement as HTMLAnchorElement);
        if (at < 0) return;
        const next =
            event.key === 'ArrowRight'
                ? (at + 1) % all.length
                : event.key === 'ArrowLeft'
                  ? (at - 1 + all.length) % all.length
                  : event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? all.length - 1
                      : null;
        if (next === null) return;
        event.preventDefault();
        all[next]!.focus();
        const tab = tabs[next];
        if (tab && tab.key !== active) navigate(to(tab.key));
    };
    return (
        <div className="tabs" role="tablist" onKeyDown={keys}>
            {tabs.map((each) => (
                <a
                    key={each.key}
                    role="tab"
                    aria-selected={active === each.key}
                    tabIndex={active === each.key || !tabs.some((tab) => tab.key === active) ? 0 : -1}
                    className={active === each.key ? 'active' : ''}
                    href={href(to(each.key))}
                    onClick={(event) => {
                        // A click that asks for another browser tab or window is the browser's to follow.
                        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                        event.preventDefault();
                        navigate(to(each.key));
                    }}
                >
                    {each.label}
                    {each.count !== undefined && <span className="tab-count">{` ${each.count}`}</span>}
                </a>
            ))}
        </div>
    );
}
