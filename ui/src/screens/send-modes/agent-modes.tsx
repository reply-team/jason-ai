import { useEffect, useState } from 'react';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import type { Campaign } from '../../api/types';
import type { MessageKey } from '../../locale/en';
import { t } from '../../platform/i18n';
import { campaign as open } from '../campaigns/store';
import { kinds, modeWords, modes, type Kind, type Mode } from './model';

type Pick = Mode | '';

/** Where a mode comes from, in words; a source this build does not know is shown as it is. */
export const sourceWords = (source: string): string =>
    ['campaign', 'preference', 'default'].includes(source) ? t(`modes.source.${source}` as MessageKey) : source;

/**
 * An agent's own send modes: what each kind goes under now and where that comes from, and a switch that sets the
 * agent's own or gives it back to the choice for every agent. The set is a person's word, so it goes as the paired
 * person; an agent read from a runtime older than the send modes shows nothing.
 */
export function AgentSendModes({ campaign }: { campaign: Campaign }) {
    const api = useApi();
    const inForce = campaign.send_modes_in_force;
    const own = campaign.send_modes;
    const [picked, setPicked] = useState<Partial<Record<Kind, Pick>>>({});
    const [busy, setBusy] = useState<Kind | null>(null);
    const [said, setSaid] = useState<{ kind: Kind; text: string; ok: boolean } | null>(null);
    useEffect(() => {
        setPicked({});
        setSaid(null);
    }, [campaign.id]);
    if (!inForce || !own) return null;

    async function apply(kind: Kind, to: Pick) {
        setBusy(kind);
        setSaid(null);
        try {
            const answer = await api.call<Campaign>(
                'campaign.set_send_mode',
                { campaign_id: campaign.id, [kind]: to === '' ? null : to },
                'write',
            );
            if (open.value?.id === answer.id) open.value = answer;
            setPicked((now) => ({ ...now, [kind]: undefined }));
            setSaid({ kind, text: t('modes.agent.applied'), ok: true });
        } catch (error) {
            if (!(error instanceof Unpaired))
                setSaid({
                    kind,
                    ok: false,
                    text: t('modes.agent.refused', { why: error instanceof ApiError ? error.message : String(error) }),
                });
        } finally {
            setBusy(null);
        }
    }

    return (
        <div className="section agent-send-modes">
            <h3>{t('modes.agent.heading')}</h3>
            {kinds.map((kind) => {
                const now = inForce[kind];
                const stored: Pick = modes.find((each) => each === own[kind]) ?? '';
                const value = picked[kind] ?? stored;
                return (
                    <div key={kind} className="send-mode-row">
                        <p>
                            <strong>{t(`modes.kind.${kind}`)}</strong>: {modeWords(now.mode).name} ·{' '}
                            <span className="muted">{sourceWords(now.source)}</span>
                        </p>
                        <select
                            aria-label={t(`modes.kind.${kind}`)}
                            disabled={busy !== null}
                            value={value}
                            onChange={(event) => setPicked((was) => ({ ...was, [kind]: event.target.value as Pick }))}
                        >
                            <option value="">{t('modes.agent.use')}</option>
                            {modes.map((mode) => (
                                <option key={mode} value={mode}>
                                    {modeWords(mode).name}
                                </option>
                            ))}
                        </select>{' '}
                        <button
                            className="btn"
                            type="button"
                            disabled={busy !== null || value === stored}
                            onClick={() => void apply(kind, value)}
                        >
                            {t('modes.agent.apply')}
                        </button>
                        {said?.kind === kind && (
                            <p className={said.ok ? 'muted' : 'notice'} role={said.ok ? 'status' : 'alert'}>
                                {said.text}
                            </p>
                        )}
                    </div>
                );
            })}
        </div>
    );
}
