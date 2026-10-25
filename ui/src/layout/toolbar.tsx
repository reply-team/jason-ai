import type { ComponentChildren } from 'preact';

/**
 * The top of every screen: its title, what sits beside it (a switch), and at the far right the refresh, turning and held
 * while the screen reads again.
 */
export function Toolbar({
    title,
    children,
    refresh,
}: {
    title: string;
    children?: ComponentChildren;
    refresh?: { busy: boolean; onRefresh: () => void };
}) {
    return (
        <div class="toolbar">
            <h1>{title}</h1>
            {children}
            <span class="spacer" />
            {refresh && (
                <button
                    class={`iconbtn${refresh.busy ? ' spin' : ''}`}
                    type="button"
                    title="Refresh"
                    disabled={refresh.busy}
                    onClick={refresh.onRefresh}
                >
                    <span class="glyph">⟳</span>
                </button>
            )}
        </div>
    );
}
