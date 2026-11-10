import type { MessageKey } from './en';

/** The language's name, in its own words, as the Settings screen offers it. */
export const name = 'Українська';

export const uk: Record<MessageKey, string> = {
    'nav.attention': 'Увага',
    'nav.performance': 'Результати',
    'nav.campaigns': 'Кампанії',
    'campaign.tab.sequence': 'Послідовність',
    'campaign.tab.people': 'Люди',
    'campaign.tab.peopleActivity': 'Активність людей',
    'campaign.tab.inbox': 'Вхідні',
    'campaign.tab.results': 'Результати',
    'campaign.tab.settings': 'Налаштування',
    'campaign.tab.chat': 'Чат',
    'nav.workitems': 'Завдання',
    'nav.people': 'Люди',
    'nav.system': 'Система',
    'nav.settings': 'Налаштування',
    'nav.chat': 'Чат',
    'nav.aria': 'Екрани',
    'nav.mark': 'щось потребує вашої уваги',
    'settings.title': 'Налаштування',
    'settings.appearance': 'Вигляд',
    'settings.language': 'Мова',
    'settings.theme.system': 'Як у системі',
    'settings.theme.light': 'Світла',
    'settings.theme.dark': 'Темна',
    'chat.title': 'Чат',
    'chat.desktop':
        'Чат з вашим агентом є частиною настільного застосунку. Він запускає агента на цьому компʼютері й питає вас, перш ніж агент щось зробить, а вкладка браузера цього не вміє.',
    'chat.get': 'Завантажити Jason Desktop зі сторінки випусків',
    'chat.agent': 'Усе, що є тут, працює й із сесії з вашим агентом у терміналі, з навичками, які розгорнув Jason.',
    'shell.version':
        'Цей інтерфейс зібрано для API {ui}, а runtime працює з {runtime}. Перевстановіть інтерфейс із відповідного випуску.',
};
