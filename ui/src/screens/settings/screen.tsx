import { html } from '../../console/html';
import { svg } from '../../console/icons';
import { Painted } from '../../console/painted';
import { modelsView } from '../../console/connect';
import { t } from '../../platform/i18n';
import { mode } from '../../platform/mode';
import * as shell from '../../shell/shell';
import { RuntimeCard } from '../system/runtime-card';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';
import { SendModesSettings } from '../send-modes/settings-section';
import { VoiceSection } from './voice';

const themes: readonly ThemeChoice[] = ['light', 'dark', 'system'];

// Each section's heading with its icon, drawn from the console's markup by a stable view, so it is painted again
// when the words are drawn again.
const appearance = () => html`<h2>${svg('theme', 16, 'settings-icon')} <span>${t('settings.appearance')}</span></h2>`;

const models = () => html`<h2>${svg('plug', 16, 'settings-icon')} <span>${t('settings.models')}</span></h2>`;

/** What this browser keeps for itself: the theme. The runtime never hears of it. */
export function SettingsScreen() {
    // Live gains what Connect holds for the AI and what System holds of the runtime, beside the sections it has; the
    // demo has both pages in its sidebar already.
    const live = mode.value === 'live';
    const info = shell.info.value;
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
            <SendModesSettings />
            {live && (
                <section data-section="models">
                    <Painted view={models} className="settings-heading" />
                    <Painted view={modelsView} className="settings-models" />
                </section>
            )}
            {live && info !== null && (
                <div data-section="runtime">
                    <RuntimeCard info={info} now={new Date()} />
                </div>
            )}
        </div>
    );
}
