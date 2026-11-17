import { useEffect, useLayoutEffect, useRef, type ReactNode } from 'react';
import { REASON_CLASS } from './reason-field';

/**
 * What a dialog focuses as it opens: its first field or picker that can take a key, so Escape and typing reach it at
 * once, never an optional reason, which is not the thing a dialog asks for, and never the button that acts, so Enter
 * as it opens does nothing; a dialog with no other field focuses its way out, so Escape reaches it all the same. The
 * browser's own modal focus, on the first thing that can take it, is replaced by this one as the dialog opens.
 */
const firstControl = ['button.select', 'input', 'textarea']
    .map((control) => `${control}:not(:disabled):not(.${REASON_CLASS})`)
    .join(', ');
const wayOut = 'button[data-dismiss]';

/**
 * A question over the page: its heading, what it asks for, and its buttons — the way out on the left, then what it
 * does. Escape is the way out too, once no list open inside it has taken the key.
 *
 * It opens as the browser's modal dialog, in the top layer, wherever in the page it is drawn: nothing of the page is
 * painted over it, whatever stacking an element of the page makes for itself (a z-index, an opacity, a transform), and
 * the page under it takes no click and no focus. The layer is the dimmed ground (`.overlay`), the box sits in it. The
 * browser's own close on Escape is refused, so the dialog closes only through `onClose`, as the page decides.
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
    const layer = useRef<HTMLDialogElement>(null);
    const box = useRef<HTMLDivElement>(null);
    // Into the top layer before the first paint, so the dialog is never seen in the page's own stacking.
    useLayoutEffect(() => {
        const dialog = layer.current;
        if (dialog !== null && !dialog.open) dialog.showModal();
    }, []);
    useEffect(
        () =>
            (
                box.current?.querySelector<HTMLElement>(firstControl) ?? box.current?.querySelector<HTMLElement>(wayOut)
            )?.focus(),
        [],
    );
    return (
        <dialog
            ref={layer}
            className="overlay"
            aria-label={label}
            onCancel={(e) => e.preventDefault()}
            onKeyDown={(e) => {
                // A list open inside the dialog takes Escape first; the next Escape is the dialog's.
                if (e.key === 'Escape' && !e.defaultPrevented) onClose();
            }}
        >
            <div className="dialog" ref={box}>
                <h2>{title}</h2>
                {children}
                <div className="btn-row">
                    <button className="btn ghost" type="button" data-dismiss onClick={onClose}>
                        {dismiss}
                    </button>
                    {buttons}
                </div>
            </div>
        </dialog>
    );
}
