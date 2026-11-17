import type { TextareaHTMLAttributes } from 'react';

/** The class that marks an optional reason: one line at rest, and never what a dialog focuses as it opens. */
export const REASON_CLASS = 'reason';

/**
 * An optional reason the journal keeps: a field of several lines that is one line high at rest and grows with what is
 * typed to about six, then scrolls, so it never reads as the main thing a dialog asks for. Enter is a new line in it.
 * Where a browser cannot grow a field to its content it stays one line and scrolls.
 */
export function ReasonField(props: Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'className' | 'rows'>) {
    return <textarea {...props} className={`textarea ${REASON_CLASS}`} rows={1} />;
}
