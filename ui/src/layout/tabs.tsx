import { href, navigate, type Route } from '../router';

/** A panel's tabs, each one an address of its own: a click moves there without reloading, and Back comes back. */
export function Tabs<T extends string>({
    tabs,
    active,
    to,
}: {
    tabs: readonly { key: T; label: string }[];
    active: T;
    to: (key: T) => Route;
}) {
    return (
        <div class="tabs" role="tablist">
            {tabs.map((each) => (
                <a
                    key={each.key}
                    role="tab"
                    aria-selected={active === each.key}
                    class={active === each.key ? 'active' : ''}
                    href={href(to(each.key))}
                    onClick={(event) => {
                        // A click that asks for another browser tab or window is the browser's to follow.
                        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                        event.preventDefault();
                        navigate(to(each.key));
                    }}
                >
                    {each.label}
                </a>
            ))}
        </div>
    );
}
