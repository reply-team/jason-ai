import type { ComponentChildren } from 'preact';
import { useEffect, useRef } from 'preact/hooks';

/** What a dialog focuses as it opens: its first field or picker, so Escape and typing reach it at once. */
const firstControl = 'button.select, input, textarea';

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
    title: ComponentChildren;
    label: string;
    onClose: () => void;
    dismiss?: string;
    buttons: ComponentChildren;
    children: ComponentChildren;
}) {
    const box = useRef<HTMLDivElement>(null);
    useEffect(() => box.current?.querySelector<HTMLElement>(firstControl)?.focus(), []);
    return (
        <div
            class="overlay"
            onKeyDown={(e) => {
                // A list open inside the dialog takes Escape first; the next Escape is the dialog's.
                if (e.key === 'Escape' && !e.defaultPrevented) onClose();
            }}
        >
            <div class="dialog" role="dialog" aria-label={label} ref={box}>
                <h2>{title}</h2>
                {children}
                <div class="btn-row">
                    <button class="btn ghost" type="button" onClick={onClose}>
                        {dismiss}
                    </button>
                    {buttons}
                </div>
            </div>
        </div>
    );
}
