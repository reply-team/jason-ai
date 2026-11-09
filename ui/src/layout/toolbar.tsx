import type { ReactNode } from 'react';

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
    children?: ReactNode;
    refresh?: { busy: boolean; onRefresh: () => void };
}) {
    return (
        <div className="toolbar">
            <h1>{title}</h1>
            {children}
            <span className="spacer" />
            {refresh && (
                <button
                    className={`iconbtn${refresh.busy ? ' spin' : ''}`}
                    type="button"
                    title="Refresh"
                    disabled={refresh.busy}
                    onClick={refresh.onRefresh}
                >
                    <span className="glyph">⟳</span>
                </button>
            )}
        </div>
    );
}
