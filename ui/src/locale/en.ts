/** The shell's words. One table, so a language can be added beside it. */
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
    'settings.title': 'Settings',
    'settings.appearance': 'Appearance',
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
