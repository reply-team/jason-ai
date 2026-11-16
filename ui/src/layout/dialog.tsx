import { useEffect, useRef, type ReactNode } from 'react';

/**
 * What a dialog focuses as it opens: its first field or picker, so Escape and typing reach it at once; a dialog with no
 * field focuses its way out, so Escape reaches it all the same.
 */
const firstControl = 'button.select, input, textarea';
const wayOut = 'button[data-dismiss]';

/**
 * A question over the page: its heading, what it asks for, and its buttons — the way out on the left, then what it
 * does. Escape is the way out too, once no list open inside it has taken the key.
 */
export function Dialog({
    title,
    label,
    onClose,
    dismiss = 'Cancel',
    buttons,
    children,
}: {
    title: ReactNode;
    label: string;
    onClose: () => void;
    dismiss?: string;
    buttons: ReactNode;
    children: ReactNode;
}) {
    const box = useRef<HTMLDivElement>(null);
    useEffect(
        () =>
            (
                box.current?.querySelector<HTMLElement>(firstControl) ?? box.current?.querySelector<HTMLElement>(wayOut)
            )?.focus(),
        [],
    );
    return (
        <div
            className="overlay"
            onKeyDown={(e) => {
                // A list open inside the dialog takes Escape first; the next Escape is the dialog's.
                if (e.key === 'Escape' && !e.defaultPrevented) onClose();
            }}
        >
            <div className="dialog" role="dialog" aria-label={label} ref={box}>
                <h2>{title}</h2>
                {children}
                <div className="btn-row">
                    <button className="btn ghost" type="button" data-dismiss onClick={onClose}>
                        {dismiss}
                    </button>
                    {buttons}
                </div>
            </div>
        </div>
    );
}
