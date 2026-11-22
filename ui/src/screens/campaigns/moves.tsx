import { useState, type ReactNode } from 'react';
import { TransitionDialog, transitions, verbs, type Movable, type Transition } from './pause-dialog';

/**
 * A campaign's moves: the one its status allows, Start, Pause or Resume, as a button that opens its confirmation. The
 * agent's page draws it in its header and a campaign's chat in its bar, so both offer the same move with the same
 * dialog. `buttonClass` gives each place its own look for a move's button; `frame` is where the dialog is drawn, for a
 * place whose stylesheet is not the dialog's. `onDone` follows a move the runtime took.
 */
export function CampaignMoves({
    campaign,
    buttonClass,
    frame = (dialog) => dialog,
    onDone,
    people,
}: {
    campaign: Movable;
    /** How many people the campaign holds, where the place read it: with an empty brief, none holds Start back. */
    people?: number | null;
    buttonClass: (move: Transition) => string;
    frame?: (dialog: ReactNode) => ReactNode;
    onDone: () => void;
}) {
    const [acting, setActing] = useState<{ id: string; move: Transition } | null>(null);
    // A dialog opened for one campaign is not kept for another the place shows next.
    const open = acting?.id === campaign.id ? acting.move : null;
    return (
        <>
            {transitions(campaign, people).map((move) => (
                <button
                    key={move}
                    className={buttonClass(move)}
                    type="button"
                    onClick={() => setActing({ id: campaign.id, move })}
                >
                    {verbs[move]}
                </button>
            ))}
            {open &&
                frame(
                    <TransitionDialog
                        campaign={campaign}
                        kind={open}
                        onDone={() => {
                            setActing(null);
                            onDone();
                        }}
                        onClose={() => setActing(null)}
                    />,
                )}
        </>
    );
}
