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
    'attention.filter.label': 'Approvals or questions',
    'attention.filter.all': 'All',
    'attention.filter.approvals': 'Approvals',
    'attention.filter.questions': 'Questions',
    'attention.filter.shown': 'Shown: {count}',
    'attention.filter.noneWaiting': 'No approvals waiting.',
    'attention.filter.noneOpen': 'No questions for you.',
    'attention.filter.noneDecided': 'No approval has been decided yet.',
    'attention.filter.noneAnswered': 'No question has been answered yet.',
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
