/** The language's name, in its own words, as the Settings screen offers it. */
export const name = 'English';

/** The shell's words. A language file must hold every key here; the compiler says when one does not. */
export const en = {
    'nav.attention': 'Attention',
    'nav.performance': 'Performance',
    'nav.campaigns': 'Campaigns',
    'nav.workitems': 'Work items',
    'nav.people': 'People',
    'nav.system': 'System',
    'nav.settings': 'Settings',
    'nav.chat': 'Chat',
    'nav.aria': 'Screens',
    'nav.mark': 'something needs you',
    'person.tab.details': 'Details',
    'person.tab.timeline': 'Activity',
    'person.tab.sequences': 'Sequences',
    'person.tab.threads': 'Messages',
    'person.tab.next': 'Next',
    'person.tab.research': 'Research',
    'person.sequences.none': 'Not in any campaign.',
    'person.sequences.added': 'added {date}',
    'person.sequences.changed': 'changed {date}',
    'person.state.enrolled': 'Enrolled',
    'person.state.paused': 'Paused',
    'person.state.finished': 'Finished',
    'person.state.excluded': 'Excluded',
    'person.next.none': 'No open work for this person.',
    'person.next.more': 'All open work for {name}',
    'person.next.status.created': 'Planned',
    'person.next.status.awaiting_approval': 'Waiting for approval',
    'person.next.status.scheduled': 'Scheduled',
    'person.next.status.processing': 'Running',
    'person.next.notBefore': 'not before {time}',
    'person.next.retryAfter': 'retry after {time}',
    'person.next.due': 'due {time}',
    'settings.title': 'Settings',
    'settings.appearance': 'Appearance',
    'settings.language': 'Language',
    'settings.theme.system': 'Same as the system',
    'settings.theme.light': 'Light',
    'settings.theme.dark': 'Dark',
    'chat.title': 'Chat',
    'chat.desktop':
        'The chat with your agent is part of the desktop application. It starts an agent on this computer and asks you before the agent acts, which a browser tab cannot do.',
    'chat.get': 'Get Jason Desktop from the releases page',
    'chat.agent':
        'Everything here also works from a session with your agent in a terminal, with the skills Jason deployed.',
    'shell.version':
        'This UI was built for API {ui}; the runtime speaks {runtime}. Reinstall the UI from a release that matches.',
} as const;

export type MessageKey = keyof typeof en;
