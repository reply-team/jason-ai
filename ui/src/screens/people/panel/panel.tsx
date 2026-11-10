import type { Contact } from '../../../api/types';
import type { MessageKey } from '../../../locale/en';
import { t } from '../../../platform/i18n';
import type { PersonTab } from '../../../router';
import { Details } from '../details';
import { linked, ThreadsTab } from '../threads';
import { Timeline } from '../timeline';
import { NextTab } from './next-tab';
import { SequencesTab } from './sequences-tab';

const names: Record<PersonTab, MessageKey> = {
    details: 'person.tab.details',
    timeline: 'person.tab.timeline',
    sequences: 'person.tab.sequences',
    threads: 'person.tab.threads',
    next: 'person.tab.next',
    research: 'person.tab.research',
};

/** The panel's tabs for this person, in their order; Messages only where a provider knows them. */
export function panelTabs(person: Contact): { key: PersonTab; label: string }[] {
    const keys: PersonTab[] = [
        'details',
        'timeline',
        'sequences',
        ...(linked(person) ? ['threads' as const] : []),
        'next',
    ];
    return keys.map((key) => ({ key, label: t(names[key]) }));
}

/** The tab an address asks for, or Activity when this person's panel has no such tab. */
export function shownTab(person: Contact | null, asked: PersonTab): PersonTab {
    return person && !panelTabs(person).some((each) => each.key === asked) ? 'timeline' : asked;
}

type Props = { person: Contact; tab: PersonTab; me: string; now: Date; zone: string };

/** What the person panel shows under its tabs. */
export function PanelTab({ person, tab, me, now, zone }: Props) {
    switch (tab) {
        case 'details':
            return <Details person={person} now={now} zone={zone} />;
        case 'sequences':
            return <SequencesTab person={person} now={now} zone={zone} />;
        case 'threads':
            return <ThreadsTab person={person} zone={zone} />;
        case 'next':
            return <NextTab person={person} now={now} zone={zone} />;
        default:
            return <Timeline person={person} me={me} now={now} zone={zone} />;
    }
}
