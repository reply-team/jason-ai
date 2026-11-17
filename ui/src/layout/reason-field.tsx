import type { TextareaHTMLAttributes } from 'react';

/**
 * An optional reason the journal keeps: a field of several lines that is one line high at rest and grows with what is
 * typed to about six, then scrolls, so it never reads as the main thing a dialog asks for. Enter is a new line in it.
 * Where a browser cannot grow a field to its content it stays one line and scrolls. Its class, `reason`, is what the
 * dialog's focus passes over as it opens.
 */
export function ReasonField(props: Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'className' | 'rows'>) {
    return <textarea {...props} className="textarea reason" rows={1} />;
}
