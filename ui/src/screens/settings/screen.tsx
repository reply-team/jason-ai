import { locale, locales, setLocale, t } from '../../platform/i18n';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';

const themes: readonly ThemeChoice[] = ['system', 'light', 'dark'];

/** What this browser keeps for itself: the theme and the language. The runtime never hears of either. */
export function SettingsScreen() {
    return (
        <div className="settings">
            <h1>{t('settings.title')}</h1>
            <section>
                <h2>{t('settings.appearance')}</h2>
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
                <h2>{t('settings.language')}</h2>
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
