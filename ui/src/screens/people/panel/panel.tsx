import { useEffect, useState } from 'react';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Contact } from '../../../api/types';
import { Notice } from '../../../layout/notice';
import type { PersonTab, Route } from '../../../router';
import { AddDialog } from '../add-dialog';
import { PersonCard } from '../card';
import * as people from '../store';
import { linked, ThreadsTab } from '../threads';
import { Timeline } from '../timeline';
import { DetailsTab } from './details-tab';
import { NextTab } from './next-tab';
import { ResearchTab } from './research-tab';
import { SequencesTab } from './sequences-tab';
import * as panel from './store';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

const names: Record<PersonTab, string> = {
    details: 'Details',
    timeline: 'Activity',
    sequences: 'Sequences',
    threads: 'Messages',
    next: 'Next',
    research: 'Research',
};

/** The panel's tabs for this person, in their order; Messages only where a provider knows them. */
export function panelTabs(person: Contact): { key: PersonTab; label: string }[] {
    const keys: PersonTab[] = [
        'details',
        'timeline',
        'sequences',
        ...(linked(person) ? ['threads' as const] : []),
        'next',
        'research',
    ];
    return keys.map((key) => ({ key, label: names[key] }));
}

/** The tab asked for, or Activity when this person's panel has no such tab. */
export function shownTab(person: Contact | null, asked: PersonTab): PersonTab {
    return person && !panelTabs(person).some((each) => each.key === asked) ? 'timeline' : asked;
}

export type PersonPanelProps = {
    /** The person to show. */
    contactId: string;
    /** The campaign the panel was opened from, when it was. */
    campaignId?: string | undefined;
    onClose: () => void;
    /**
     * The tab on show and where each tab is, for a host that keeps the tab in its address. Without them the panel
     * keeps its tab itself, opening on Activity.
     */
    tab?: PersonTab | undefined;
    to?: ((tab: PersonTab) => Route) | undefined;
};

/**
 * One person beside whatever list opened them: who they are, their tabs and what can be done for them. It reads the
 * person itself and assumes nothing of the screen it is mounted on.
 */
export function PersonPanel({ contactId, campaignId, onClose, tab, to }: PersonPanelProps) {
    const api = useApi();
    const [own, setOwn] = useState<PersonTab>('timeline');
    const [adding, setAdding] = useState(false);
    useEffect(() => {
        setAdding(false);
        panel.forget();
        void people.open(api, contactId);
    }, [api, contactId]);
    useEffect(
        () => () => {
            people.close();
            panel.forget();
        },
        [],
    );

    const person = people.person.value;
    const shown = shownTab(person, tab ?? own);
    const now = new Date();
    return (
        <div className="panel person-panel">
            {people.personFailure.value && <Notice danger>{people.personFailure.value}</Notice>}
            {person && person.id === contactId && (
                <>
                    <PersonCard
                        person={person}
                        tab={shown}
                        to={to}
                        onTab={setOwn}
                        now={now}
                        zone={zone}
                        onAdd={() => setAdding(true)}
                        onClose={onClose}
                        tabs={panelTabs(person)}
                    />
                    <PanelTab
                        person={person}
                        tab={shown}
                        campaignId={campaignId}
                        me={session.actorId.value ?? ''}
                        now={now}
                        zone={zone}
                    />
                    {adding && <AddDialog person={person} onClose={() => setAdding(false)} />}
                </>
            )}
        </div>
    );
}

type Props = {
    person: Contact;
    tab: PersonTab;
    campaignId: string | undefined;
    me: string;
    now: Date;
    zone: string;
};

/** What the person panel shows under its tabs. */
function PanelTab({ person, tab, campaignId, me, now, zone }: Props) {
    switch (tab) {
        case 'details':
            return <DetailsTab person={person} campaignId={campaignId} now={now} zone={zone} />;
        case 'sequences':
            return <SequencesTab person={person} now={now} zone={zone} />;
        case 'threads':
            return <ThreadsTab person={person} zone={zone} />;
        case 'next':
            return <NextTab person={person} now={now} zone={zone} />;
        case 'research':
            return <ResearchTab person={person} campaignId={campaignId} now={now} zone={zone} />;
        default:
            return <Timeline person={person} me={me} now={now} zone={zone} />;
    }
}
