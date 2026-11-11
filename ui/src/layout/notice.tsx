import type { ReactNode } from 'react';

/**
 * A line above what it is about. A failure is announced as it appears (`danger`); a list of failures said at once is
 * not (`alert={false}`); a line that follows the page's state is a `status`.
 */
export function Notice({
    danger = false,
    alert = danger,
    status = false,
    children,
}: {
    danger?: boolean;
    alert?: boolean;
    status?: boolean;
    children: ReactNode;
}) {
    return (
        <div className={danger ? 'notice danger' : 'notice'} role={alert ? 'alert' : status ? 'status' : undefined}>
            {children}
        </div>
    );
}
