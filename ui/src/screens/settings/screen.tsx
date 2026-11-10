import { html } from '../../console/html';
import { svg } from '../../console/icons';
import { Painted } from '../../console/painted';
import { locale, locales, setLocale, t } from '../../platform/i18n';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';

const themes: readonly ThemeChoice[] = ['system', 'light', 'dark'];

// Each section's heading with its icon, drawn from the console's markup by a stable view, so it is painted again
// when the language changes.
const appearance = () => html`<h2>${svg('theme', 16, 'settings-icon')} <span>${t('settings.appearance')}</span></h2>`;
const language = () => html`<h2>${svg('globe', 16, 'settings-icon')} <span>${t('settings.language')}</span></h2>`;

/** What this browser keeps for itself: the theme and the language. The runtime never hears of either. */
export function SettingsScreen() {
    return (
        <div className="settings">
            <h1>{t('settings.title')}</h1>
            <section>
                <Painted view={appearance} className="settings-heading" />
                <div className="choices">
                    {themes.map((theme) => (
                        <button
                            key={theme}
                            className="btn"
                            type="button"
                            aria-pressed={themeChoice.value === theme}
                            onClick={() => setTheme(theme)}
                        >
                            {t(`settings.theme.${theme}`)}
                        </button>
                    ))}
                </div>
            </section>
            <section>
                <Painted view={language} className="settings-heading" />
                <div className="choices">
                    {locales.map((each) => (
                        <button
                            key={each.id}
                            className="btn"
                            type="button"
                            lang={each.id}
                            aria-pressed={locale.value === each.id}
                            onClick={() => setLocale(each.id)}
                        >
                            {each.name}
                        </button>
                    ))}
                </div>
            </section>
        </div>
    );
}
