import { toChildArray, type ComponentChildren } from 'preact';

/** The row of what a panel's subject can be asked to do; a panel with nothing to offer has no row at all. */
export function ActionsRow({ children }: { children?: ComponentChildren }) {
    if (toChildArray(children).length === 0) return null;
    return <div class="panel-actions">{children}</div>;
}
