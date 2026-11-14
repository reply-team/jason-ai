import { useEffect, useState } from 'react';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { t } from '../../platform/i18n';
import { mode as pageMode } from '../../platform/mode';
import { readPreferences } from '../settings/voice-model';
import { forget, kinds, knownMode, modeWords, modes, remember, storedMode, type Kind, type Mode } from './model';

type Said = { kind: 'saved' | 'forgotten' | 'refused'; text: string } | null;
type Stored = Record<Kind, string | null>;

/**
 * "How messages go" in Settings: the installation's two send modes, for every agent. Each kind is saved on its own by
 * the person's press, as the paired person, and an answer can be given back, which leaves the kind unanswered: every
 * message then waits for the person and the approval cards ask again. Live only; the demo says where.
 */
export function SendModesSettings() {
    const api = useApi();
    const demo = pageMode.value === 'demo';
    const [read, setRead] = useState<'reading' | 'ready' | 'unreadable'>('reading');
    const [stored, setStored] = useState<Stored>({ cold: null, warm: null });
    const [chosen, setChosen] = useState<Record<Kind, Mode | null>>({ cold: null, warm: null });
    const [busy, setBusy] = useState<Kind | null>(null);
    const [said, setSaid] = useState<Partial<Record<Kind, Said>>>({});

    useEffect(() => {
        if (demo) return;
        let current = true;
        readPreferences(api).then(
            (items) => {
                if (!current) return;
                setStored({ cold: storedMode(items, 'cold'), warm: storedMode(items, 'warm') });
                setRead('ready');
            },
            () => current && setRead('unreadable'),
        );
        return () => {
            current = false;
        };
    }, [api, demo]);

    async function act(kind: Kind, change: () => Promise<void>, after: string | null, done: Said) {
        setBusy(kind);
        setSaid((now) => ({ ...now, [kind]: null }));
        try {
            await change();
            setStored((now) => ({ ...now, [kind]: after }));
            setChosen((now) => ({ ...now, [kind]: null }));
            setSaid((now) => ({ ...now, [kind]: done }));
        } catch (error) {
            if (!(error instanceof Unpaired)) {
                const why = error instanceof ApiError ? error.message : String(error);
                setSaid((now) => ({ ...now, [kind]: { kind: 'refused', text: t('modes.question.refused', { why }) } }));
            }
        } finally {
            setBusy(null);
        }
    }

    return (
        <section aria-labelledby="settings-modes-heading">
            <h2 id="settings-modes-heading">{t('modes.settings.heading')}</h2>
            {demo && <p className="muted">{t('modes.settings.demo')}</p>}
            {!demo && read === 'reading' && <p className="muted">{t('modes.settings.reading')}</p>}
            {!demo && read === 'unreadable' && <p className="voice-problem">{t('modes.settings.unreadable')}</p>}
            {!demo && read === 'ready' && (
                <>
                    <p className="hint">{t('modes.settings.lead')}</p>
                    {kinds.map((kind) => {
                        const now = stored[kind];
                        const known = now === null ? null : knownMode(now);
                        const shown = chosen[kind] ?? known;
                        const what = said[kind];
                        return (
                            <fieldset key={kind} className="send-mode-choice" disabled={busy !== null}>
                                <legend>{t(`modes.kind.${kind}`)}</legend>
                                {now === null && <p className="muted">{t('modes.settings.unset')}</p>}
                                {now !== null && known === null && (
                                    <p className="muted">{t('modes.settings.unknown', { mode: now })}</p>
                                )}
                                {modes.map((each) => (
                                    <label key={each} className="send-mode-option">
                                        <input
                                            type="radio"
                                            name={`settings-${kind}`}
                                            checked={shown === each}
                                            onChange={() => setChosen((was) => ({ ...was, [kind]: each }))}
                                        />{' '}
                                        <span>{modeWords(each).name}</span>
                                        <span className="hint"> {modeWords(each).note}</span>
                                    </label>
                                ))}
                                <button
                                    className="btn"
                                    type="button"
                                    disabled={chosen[kind] === null || chosen[kind] === now}
                                    onClick={() => {
                                        const picked = chosen[kind];
                                        if (picked === null) return;
                                        void act(kind, () => remember(api, kind, picked), picked, {
                                            kind: 'saved',
                                            text: t('modes.settings.saved'),
                                        });
                                    }}
                                >
                                    {t('modes.settings.save')}
                                </button>
                                {now !== null && (
                                    <button
                                        className="btn ghost"
                                        type="button"
                                        onClick={() =>
                                            void act(kind, () => forget(api, kind), null, {
                                                kind: 'forgotten',
                                                text: t('modes.settings.forgotten'),
                                            })
                                        }
                                    >
                                        {t('modes.settings.forget')}
                                    </button>
                                )}
                                {what && (
                                    <p
                                        className={what.kind === 'refused' ? 'notice' : 'muted'}
                                        role={what.kind === 'refused' ? 'alert' : 'status'}
                                    >
                                        {what.text}
                                    </p>
                                )}
                            </fieldset>
                        );
                    })}
                </>
            )}
        </section>
    );
}
