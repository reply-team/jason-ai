import type { ReactNode } from 'react';

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
    title: ReactNode;
    sub: ReactNode;
    lead?: ReactNode;
    under?: ReactNode;
    onClose: () => void;
}) {
    const heading = (
        <>
            <h2>{title}</h2>
            <div className="sub">{sub}</div>
            {under}
        </>
    );
    return (
        <div className="panel-header">
            {lead ? (
                <div className="heading person-head">
                    {lead}
                    <div>{heading}</div>
                </div>
            ) : (
                <div className="heading">{heading}</div>
            )}
            <button className="iconbtn close" type="button" title="Close" aria-label="Close" onClick={onClose}>
                ×
            </button>
        </div>
    );
}
