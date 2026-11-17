import { act, cleanup, render, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { Dialog } from './dialog';
import { ReasonField } from './reason-field';

// What a dialog focuses as it opens: never its optional reason, never the button that acts.

afterEach(cleanup);

async function opened(children: ReactNode) {
    render(
        <Dialog
            title="Stop Spring"
            label="Stop Spring"
            onClose={() => undefined}
            buttons={
                <button className="btn primary" type="button">
                    Stop
                </button>
            }
        >
            {children}
            <div className="field">
                <label htmlFor="reason">Reason (optional, kept in the journal)</label>
                <ReasonField id="reason" aria-label="Reason" maxLength={2000} />
            </div>
        </Dialog>,
    );
    await act(async () => undefined);
    return screen.getByRole('dialog', { name: 'Stop Spring' });
}

describe('a dialog that asks an optional reason', () => {
    it('draws the reason one line high and puts the focus on Cancel, neither on the reason nor on the button that acts', async () => {
        const dialog = await opened(null);
        const reason = within(dialog).getByRole<HTMLTextAreaElement>('textbox', { name: 'Reason' });
        expect(reason.tagName).toBe('TEXTAREA');
        expect(reason.getAttribute('rows')).toBe('1');
        expect(reason.classList.contains('reason')).toBe(true);
        expect(reason.getAttribute('maxlength')).toBe('2000');
        expect(document.activeElement).not.toBe(reason);
        expect(document.activeElement).not.toBe(within(dialog).getByRole('button', { name: 'Stop' }));
        expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Cancel' }));
    });

    it('puts the focus on the first other field that can take one, past a field turned off', async () => {
        const dialog = await opened(
            <>
                <input aria-label="Turned off" disabled />
                <input aria-label="A name" />
            </>,
        );
        expect(document.activeElement).toBe(within(dialog).getByRole('textbox', { name: 'A name' }));
    });
});
