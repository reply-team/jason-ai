import { useEffect, useRef, useState } from 'react';
import { html } from '../../console/html';
import { svg } from '../../console/icons';
import { Painted } from '../../console/painted';
import { t } from '../../platform/i18n';
import { consoleShell, essentials } from '../../platform/console-shell';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';
import * as shell from '../../shell/shell';
import { SendModesSettings } from '../send-modes/settings-section';
import { chosenSection, type SettingsSection } from './chosen';
import { VoiceSection } from './voice';

const themes: readonly ThemeChoice[] = ['light', 'dark', 'system'];

// Each section's heading with its icon, drawn from the console's markup by a stable view, so it is painted again
// when the words are drawn again.
const appearance = () => html`<h2>${svg('theme', 16, 'settings-icon')} <span>${t('settings.appearance')}</span></h2>`;

const names: Record<SettingsSection, () => string> = {
    appearance: () => t('settings.appearance'),
    voice: () => t('voice.heading'),
    modes: () => t('modes.settings.heading'),
    models: () => t('settings.models'),
    runtime: () => 'Runtime',
};

/**
 * The sections, other than the one shown, that hold an alert: a save that failed after another section was chosen says
 * so in a card the sheet hides. The list's button for that section is marked and described by the alert, which a
 * hidden element still gives as a description.
 */
function useHiddenAlerts(root: React.RefObject<HTMLDivElement | null>, chosen: SettingsSection) {
    const [flags, setFlags] = useState<Partial<Record<SettingsSection, string>>>({});
    useEffect(() => {
        const element = root.current;
        if (!element) return;
        let next = 0;
        const read = () => {
            const found: Partial<Record<SettingsSection, string>> = {};
            for (const card of element.querySelectorAll<HTMLElement>(':scope > [data-section]')) {
                const name = card.dataset.section as SettingsSection;
                const alert = name === chosen ? null : card.querySelector<HTMLElement>('[role="alert"]');
                if (!alert) continue;
                alert.id ||= `settings-alert-${(next += 1)}`;
                found[name] = alert.id;
            }
            setFlags((was) => (JSON.stringify(was) === JSON.stringify(found) ? was : found));
        };
        read();
        const watch = new MutationObserver(read);
        watch.observe(element, { childList: true, subtree: true, characterData: true });
        return () => watch.disconnect();
    }, [root, chosen]);
    return flags;
}

/** What this browser keeps for itself: the theme. The runtime never hears of it. */
export function SettingsScreen() {
    // the console in Live gains what Connect holds for the AI and what System holds of the runtime, beside the sections it
    // has; the demo has both pages in its sidebar already, and the classic shell is not the console.
    const Added = essentials() ? consoleShell.settingsSections : null;
    // the console in Live lists its sections at the left and shows the chosen one alone; the sheet hides the rest, so the
    // page keeps every section in its markup.
    const listed: SettingsSection[] = ['appearance', 'voice', 'modes'];
    if (Added) listed.push('models');
    if (Added && shell.info.value !== null) listed.push('runtime');
    const chosen = listed.includes(chosenSection.value) ? chosenSection.value : listed[0]!;
    const root = useRef<HTMLDivElement>(null);
    const alerts = useHiddenAlerts(root, chosen);
    return (
        <div className="settings" ref={root} data-chosen={essentials() ? chosen : undefined}>
            <h1>{t('settings.title')}</h1>
            {essentials() && (
                <nav className="settings-list" aria-label={t('settings.title')}>
                    {listed.map((name) => (
                        <button
                            key={name}
                            type="button"
                            aria-current={name === chosen ? 'true' : undefined}
                            data-flagged={alerts[name] ? '' : undefined}
                            aria-describedby={alerts[name]}
                            onClick={() => {
                                chosenSection.value = name;
                            }}
                        >
                            {names[name]()}
                        </button>
                    ))}
                </nav>
            )}
            <section data-section="appearance">
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
            {Added && <Added />}
        </div>
    );
}
