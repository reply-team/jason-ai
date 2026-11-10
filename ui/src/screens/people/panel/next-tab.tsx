import { useEffect } from 'react';
import { useApi } from '../../../api/context';
import type { Contact, WorkItemSummary } from '../../../api/types';
import { Notice } from '../../../layout/notice';
import type { MessageKey } from '../../../locale/en';
import { t } from '../../../platform/i18n';
import { href, type Tab } from '../../../router';
import { when } from '../../../time/format';
import { mark, what } from '../../workitems/labels';
import { displayName } from '../labels';
import * as panel from './store';

type Props = { person: Contact; now: Date; zone: string };

const statusWords: Record<string, MessageKey> = {
    created: 'person.next.status.created',
    awaiting_approval: 'person.next.status.awaiting_approval',
    scheduled: 'person.next.status.scheduled',
    processing: 'person.next.status.processing',
};

/** Where an open item opens: Planned for what has not started, Now for what is running or waiting. */
function tabOf(item: WorkItemSummary): Tab {
    return item.status === 'created' ? 'planned' : 'now';
}

/** The moments the runtime holds for the item, and only those: a start it may not pass, a retry, a due time. */
function moments(item: WorkItemSummary, now: Date, zone: string): string[] {
    const at = (moment: string) => when(new Date(moment), now, zone);
    return [
        item.not_before ? t('person.next.notBefore', { time: at(item.not_before) }) : null,
        item.retry_after ? t('person.next.retryAfter', { time: at(item.retry_after) }) : null,
        item.due_at ? t('person.next.due', { time: at(item.due_at) }) : null,
    ].filter((each): each is string => each !== null);
}

/** Next: the work open for the person, by kind and state, as the runtime answers it; nothing of ours predicts a plan. */
export function NextTab({ person, now, zone }: Props) {
    const api = useApi();
    useEffect(() => {
        void panel.loadNext(api, person.id);
    }, [api, person.id]);
    const shown = panel.next.value?.contactId === person.id ? panel.next.value : null;
    return (
        <div className="panel-body">
            {panel.nextFailure.value && <Notice danger>{panel.nextFailure.value}</Notice>}
            {shown && shown.items.length === 0 && <div className="empty">{t('person.next.none')}</div>}
            {shown && shown.items.length > 0 && (
                <div className="rows">
                    {shown.items.map((item) => {
                        const status = statusWords[item.status];
                        return (
                            <a
                                key={item.id}
                                className="row"
                                href={href({ screen: 'workitems', tab: tabOf(item), id: item.id, filters: {} })}
                            >
                                <span className={`mark ${mark(item.status).kind}`}>{mark(item.status).glyph}</span>
                                <span className="what">
                                    <span className="name">{what(item)}</span>
                                    <span className="in">{item.campaign_name}</span>
                                </span>
                                <span className="meta">
                                    <span className="badge outline">{status ? t(status) : item.status}</span>
                                    {moments(item, now, zone).join(' · ')}
                                </span>
                            </a>
                        );
                    })}
                    {shown.more && (
                        <a
                            className="end"
                            href={href({ screen: 'workitems', tab: 'planned', filters: { contact: person.id } })}
                        >
                            {t('person.next.more', { name: displayName(person).name })} →
                        </a>
                    )}
                </div>
            )}
        </div>
    );
}
