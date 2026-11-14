import { t } from '../../platform/i18n';
import type { MessageKey } from '../../locale/en';
import { doubtsOf, type Doubt } from './model';

const known = [
    'unsupported_claim',
    'not_in_memory',
    'off_brief',
    'sensitive_topic',
    'instruction_in_text',
    'new_kind_of_person',
    'wrong_recipient',
    'other',
];

/** Why a send is doubted, in words; a code this build does not know is shown as it is. */
export const whyWords = (code: string): string => (known.includes(code) ? t(`modes.code.${code}` as MessageKey) : code);

function whoWords(doubt: Doubt): string {
    if (doubt.who) return doubt.who;
    if (doubt.role) return t('modes.doubts.who.role', { role: doubt.role });
    return doubt.who === '' ? t('modes.doubts.who.person') : t('modes.doubts.who.ai');
}

/**
 * Who doubted the send and why, on the card of the approval it parked: each doubt's reason in words, and the sentence
 * the doubter wrote as a text node, never as markup. Nothing is drawn where none stands.
 */
export function Doubts({ preview }: { preview: Record<string, unknown> }) {
    const doubts = doubtsOf(preview);
    if (doubts.length === 0) return null;
    return (
        <div className="section send-doubts">
            <h3>{t('modes.doubts.heading')}</h3>
            <ul>
                {doubts.map((doubt, index) => (
                    <li key={index}>
                        <p>
                            {t(doubt.source === 'ask_person' ? 'modes.doubts.asked' : 'modes.doubts.reviewed', {
                                who: whoWords(doubt),
                                why: whyWords(doubt.code),
                            })}
                        </p>
                        {doubt.note && <p className="message-text">{t('modes.doubts.note', { note: doubt.note })}</p>}
                    </li>
                ))}
            </ul>
        </div>
    );
}
