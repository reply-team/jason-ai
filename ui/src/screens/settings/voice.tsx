import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../../api/context';
import { ApiError } from '../../api/client';
import { t } from '../../platform/i18n';
import { mode } from '../../platform/mode';
import {
    emptySender,
    emptyTone,
    formalities,
    genderForms,
    readPreferences,
    saveSender,
    saveTone,
    senderCount,
    senderForm,
    senderKey,
    toneForm,
    toneKey,
    valueOf,
    type Formality,
    type GenderForms,
    type SenderForm,
    type ToneForm,
} from './voice-model';

import { voiceSectionId } from './voice-id';

type Said = { kind: 'saved' | 'empty' | 'refused'; text: string } | null;

const messageOf = (error: unknown): string => (error instanceof ApiError ? error.message : t('voice.refused'));

/** "Your name and tone": who writes and how they write, each part saved on its own. Live only; the demo says where. */
export function VoiceSection() {
    const api = useApi();
    const demo = mode.value === 'demo';
    const [read, setRead] = useState<'reading' | 'ready' | 'unreadable'>('reading');
    const [sender, setSender] = useState<SenderForm>(emptySender);
    const [tone, setTone] = useState<ToneForm>(emptyTone);
    const [senders, setSenders] = useState(0);
    const [said, setSaid] = useState<{ sender: Said; tone: Said }>({ sender: null, tone: null });

    useEffect(() => {
        if (demo) return;
        let current = true;
        readPreferences(api).then(
            (items) => {
                if (!current) return;
                setSender(senderForm(valueOf(items, senderKey)));
                setSenders(senderCount(valueOf(items, senderKey)));
                setTone(toneForm(valueOf(items, toneKey)));
                setRead('ready');
            },
            () => current && setRead('unreadable'),
        );
        return () => {
            current = false;
        };
    }, [api, demo]);

    const tell = (part: 'sender' | 'tone', what: Said) => setSaid((now) => ({ ...now, [part]: what }));

    const saveWho = async (event: FormEvent) => {
        event.preventDefault();
        tell('sender', null);
        try {
            const wrote = await saveSender(api, sender);
            tell(
                'sender',
                wrote ? { kind: 'saved', text: t('voice.saved') } : { kind: 'empty', text: t('voice.sender.empty') },
            );
        } catch (error) {
            tell('sender', { kind: 'refused', text: messageOf(error) });
        }
    };
    const saveHow = async (event: FormEvent) => {
        event.preventDefault();
        tell('tone', null);
        try {
            const wrote = await saveTone(api, tone);
            tell(
                'tone',
                wrote ? { kind: 'saved', text: t('voice.saved') } : { kind: 'empty', text: t('voice.tone.empty') },
            );
        } catch (error) {
            tell('tone', { kind: 'refused', text: messageOf(error) });
        }
    };

    const line = (what: Said) =>
        what && (
            <p
                className={what.kind === 'saved' ? 'muted' : 'voice-problem'}
                role={what.kind === 'saved' ? 'status' : 'alert'}
            >
                {what.text}
            </p>
        );

    return (
        <section id={voiceSectionId} aria-labelledby="settings-voice-heading">
            <h2 id="settings-voice-heading">{t('voice.heading')}</h2>
            {demo && <p className="muted">{t('voice.demo')}</p>}
            {!demo && read === 'reading' && <p className="muted">{t('voice.reading')}</p>}
            {!demo && read === 'unreadable' && <p className="voice-problem">{t('voice.unreadable')}</p>}
            {!demo && read === 'ready' && (
                <>
                    <form className="voice-part" aria-labelledby="voice-who" onSubmit={(event) => void saveWho(event)}>
                        <h3 id="voice-who">{t('voice.sender.heading')}</h3>
                        {senders > 1 && <p className="muted">{t('voice.sender.more', { count: String(senders) })}</p>}
                        <label>
                            <span>{t('voice.sender.name')}</span>
                            <input
                                value={sender.name}
                                onChange={(event) => setSender({ ...sender, name: event.target.value })}
                            />
                        </label>
                        <label>
                            <span>{t('voice.sender.title')}</span>
                            <input
                                value={sender.title}
                                onChange={(event) => setSender({ ...sender, title: event.target.value })}
                            />
                        </label>
                        <label>
                            <span>{t('voice.sender.company')}</span>
                            <input
                                value={sender.company}
                                onChange={(event) => setSender({ ...sender, company: event.target.value })}
                            />
                        </label>
                        <label className="voice-small">
                            <span>{t('voice.sender.gender')}</span>
                            <select
                                value={sender.gender}
                                onChange={(event) =>
                                    setSender({ ...sender, gender: event.target.value as GenderForms })
                                }
                            >
                                {genderForms.map((each) => (
                                    <option key={each} value={each}>
                                        {t(`voice.sender.gender.${each}`)}
                                    </option>
                                ))}
                            </select>
                        </label>
                        <p className="muted voice-small">{t('voice.sender.gender.note')}</p>
                        <button className="btn" type="submit">
                            {t('voice.save')}
                        </button>
                        {line(said.sender)}
                    </form>
                    <form className="voice-part" aria-labelledby="voice-how" onSubmit={(event) => void saveHow(event)}>
                        <h3 id="voice-how">{t('voice.tone.heading')}</h3>
                        <label>
                            <span>{t('voice.tone.summary')}</span>
                            <textarea
                                rows={3}
                                value={tone.summary}
                                onChange={(event) => setTone({ ...tone, summary: event.target.value })}
                            />
                        </label>
                        <label>
                            <span>{t('voice.tone.formality')}</span>
                            <select
                                value={tone.formality}
                                onChange={(event) =>
                                    setTone({ ...tone, formality: event.target.value as Formality | '' })
                                }
                            >
                                <option value="">{t('voice.tone.formality.none')}</option>
                                {formalities.map((each) => (
                                    <option key={each} value={each}>
                                        {t(`voice.tone.formality.${each}`)}
                                    </option>
                                ))}
                            </select>
                        </label>
                        <button className="btn" type="submit">
                            {t('voice.save')}
                        </button>
                        {line(said.tone)}
                    </form>
                </>
            )}
        </section>
    );
}
