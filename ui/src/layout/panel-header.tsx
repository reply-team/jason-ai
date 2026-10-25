import type { ComponentChildren } from 'preact';

/**
 * The top of a panel: what it shows, the line under it, and the close. A person's card puts their avatar before the
 * heading (`lead`) and their campaigns under the line (`under`).
 */
export function PanelHeader({
    title,
    sub,
    lead,
    under,
    onClose,
}: {
    title: ComponentChildren;
    sub: ComponentChildren;
    lead?: ComponentChildren;
    under?: ComponentChildren;
    onClose: () => void;
}) {
    const heading = (
        <>
            <h2>{title}</h2>
            <div class="sub">{sub}</div>
            {under}
        </>
    );
    return (
        <div class="panel-header">
            {lead ? (
                <div class="heading person-head">
                    {lead}
                    <div>{heading}</div>
                </div>
            ) : (
                <div class="heading">{heading}</div>
            )}
            <button class="iconbtn close" type="button" title="Close" aria-label="Close" onClick={onClose}>
                ×
            </button>
        </div>
    );
}
