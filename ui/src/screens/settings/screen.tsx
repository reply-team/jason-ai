import { html } from '../../console/html';
import { svg } from '../../console/icons';
import { Painted } from '../../console/painted';
import { t } from '../../platform/i18n';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';
import { VoiceSection } from './voice';

const themes: readonly ThemeChoice[] = ['light', 'dark', 'system'];

// Each section's heading with its icon, drawn from the console's markup by a stable view, so it is painted again
// when the words are drawn again.
const appearance = () => html`<h2>${svg('theme', 16, 'settings-icon')} <span>${t('settings.appearance')}</span></h2>`;

/** What this browser keeps for itself: the theme. The runtime never hears of it. */
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
            <VoiceSection />
        </div>
    );
}
