import { useState } from 'react';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { t } from '../../platform/i18n';
import { askedKinds, modeWords, modes, remember, type Kind, type Mode } from './model';
import { openSettings } from './open-settings';

type Props = {
    /** The card's `send_modes_unset`: the preferences nobody has set yet. */
    unset: string[] | undefined;
    /** Tells the card which approval this is, so two cards never share a choice. */
    approvalId: string;
};

/**
 * The installation's open question on an approval's card: for each kind of person not yet answered, whether every
 * message waits for the person or goes on its own. Nothing is chosen for them, and a choice is kept only by the
 * person's own press of "Remember this", which sets the preference as the paired person. The card's Approve and Reject
 * never wait on it.
 */
export function SendModesQuestion({ unset, approvalId }: Props) {
    const api = useApi();
    const asked = askedKinds(unset);
    const [chosen, setChosen] = useState<Partial<Record<Kind, Mode>>>({});
    const [kept, setKept] = useState<Partial<Record<Kind, Mode>>>({});
    const [busy, setBusy] = useState<Kind | null>(null);
    const [refused, setRefused] = useState<string | null>(null);
    if (asked.length === 0) return null;
    const open = asked.filter((kind) => kept[kind] === undefined);

    async function keep(kind: Kind) {
        const mode = chosen[kind];
        if (mode === undefined) return;
        setBusy(kind);
        setRefused(null);
        try {
            await remember(api, kind, mode);
            setKept((now) => ({ ...now, [kind]: mode }));
        } catch (error) {
            if (!(error instanceof Unpaired)) setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(null);
        }
    }

    return (
        <div className="section send-modes">
            <h3>{t('modes.question.heading')}</h3>
            {open.length > 0 && <p className="hint">{t('modes.question.lead')}</p>}
            {asked.map((kind) => {
                const done = kept[kind];
                if (done !== undefined)
                    return (
                        <p key={kind} className="muted" role="status">
                            {t('modes.question.remembered')} {t(`modes.kind.${kind}`)}: {modeWords(done).name}.
                        </p>
                    );
                return (
                    <fieldset key={kind} className="send-mode-choice" disabled={busy !== null}>
                        <legend>{t(`modes.kind.${kind}`)}</legend>
                        {modes.map((mode) => (
                            <label key={mode} className="send-mode-option">
                                <input
                                    type="radio"
                                    name={`${approvalId}-${kind}`}
                                    checked={chosen[kind] === mode}
                                    onChange={() => setChosen((now) => ({ ...now, [kind]: mode }))}
                                />{' '}
                                <span>{modeWords(mode).name}</span>
                                <span className="hint"> {modeWords(mode).note}</span>
                            </label>
                        ))}
                        <button
                            className="btn"
                            type="button"
                            disabled={chosen[kind] === undefined}
                            onClick={() => void keep(kind)}
                        >
                            {t('modes.question.remember')}
                        </button>
                    </fieldset>
                );
            })}
            {refused && (
                <p className="notice" role="alert">
                    {t('modes.question.refused', { why: refused })}
                </p>
            )}
            {open.some((kind) => chosen[kind] === 'automatic') && (
                <p className="hint">
                    {t('modes.question.voice')}{' '}
                    <button className="btn ghost" type="button" onClick={openSettings}>
                        {t('modes.settings.open')}
                    </button>
                </p>
            )}
        </div>
    );
}
