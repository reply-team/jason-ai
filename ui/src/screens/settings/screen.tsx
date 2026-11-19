import { useEffect, useRef, useState } from 'react';
import { html } from '../../console/html';
import { svg } from '../../console/icons';
import { Painted } from '../../console/painted';
import { escapeFromSettings, leaveSettings } from './way-out';
import { t } from '../../platform/i18n';
import { consoleShell, essentials } from '../../platform/console-shell';
import { setTheme, themeChoice, type ThemeChoice } from '../../platform/theme';
import * as shell from '../../shell/shell';
import { SendModesSettings } from '../send-modes/settings-section';
import { chosenSection, type SettingsSection } from './chosen';
import { NotificationsSection } from './notifications';
import { UpdateFooter } from './update-footer';
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
    notifications: () => t('settings.notifications'),
    runtime: () => 'Runtime',
};

// Each section's mark in the list at the left, on the console's 24-unit grid, stroked in the current colour.
const marks: Record<SettingsSection, string[]> = {
    appearance: ['M12 3a9 9 0 100 18 9 9 0 000-18z', 'M12 3a9 9 0 010 18z'],
    voice: ['M4 5h16v11H9l-5 4z'],
    modes: ['M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z'],
    models: ['M9 2v6M15 2v6M6 8h12v4a6 6 0 01-12 0zM12 18v4'],
    notifications: ['M6 9a6 6 0 0112 0c0 6 2 7 2 7H4s2-1 2-7', 'M10 20a2 2 0 004 0'],
    runtime: ['M3 12h4l3-8 4 16 3-8h4'],
};

function SectionMark({ name }: { name: SettingsSection }) {
    return (
        <svg
            className="settings-mark"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            {marks[name].map((each) => (
                <path key={each} d={each} />
            ))}
        </svg>
    );
}

type Held = { id: string; text: string };
let alertIds = 0;

/**
 * The sections, other than the one shown, that hold an alert: a save that failed after another section was chosen says
 * so in a card the sheet hides, which is outside the accessibility tree. The list's button for that section is marked
 * and titled by the alert, and the newest such alert's text is given again in one region of the list that is always
 * there, emptied once its card is chosen.
 */
function useHiddenAlerts(root: React.RefObject<HTMLDivElement | null>, chosen: SettingsSection) {
    const [held, setHeld] = useState<Partial<Record<SettingsSection, Held>>>({});
    const [said, setSaid] = useState<{ name: SettingsSection; text: string } | null>(null);
    const before = useRef<Partial<Record<SettingsSection, Held>>>({});
    useEffect(() => {
        const element = root.current;
        if (!element) return;
        const read = () => {
            const found: Partial<Record<SettingsSection, Held>> = {};
            for (const card of element.querySelectorAll<HTMLElement>(':scope > [data-section]')) {
                const name = card.dataset.section as SettingsSection;
                const alert = name === chosen ? null : card.querySelector<HTMLElement>('[role="alert"]');
                if (!alert) continue;
                alert.id ||= `settings-alert-${(alertIds += 1)}`;
                found[name] = { id: alert.id, text: alert.textContent?.trim() ?? '' };
            }
            const was = before.current;
            before.current = found;
            if (JSON.stringify(was) === JSON.stringify(found)) return;
            setHeld(found);
            const newest = (Object.keys(found) as SettingsSection[])
                .filter((name) => JSON.stringify(was[name]) !== JSON.stringify(found[name]))
                .at(-1);
            setSaid((kept) =>
                newest ? { name: newest, text: found[newest]!.text } : kept && found[kept.name] ? kept : null,
            );
        };
        read();
        const watch = new MutationObserver(read);
        watch.observe(element, { childList: true, subtree: true, characterData: true });
        return () => watch.disconnect();
    }, [root, chosen]);
    return { held, said };
}

/** What this browser keeps for itself: the theme. The runtime never hears of it. */
export function SettingsScreen() {
    // the console in Live gains what Connect holds for the AI and what System holds of the runtime, beside the sections it
    // has; the demo has both pages in its sidebar already, and the classic shell is not the console.
    const Added = essentials() ? consoleShell.settingsSections : null;
    // the console in Live lists its sections at the left and shows the chosen one alone; the sheet hides the rest, so the
    // page keeps every section in its markup.
    const listed: SettingsSection[] = ['appearance', 'voice', 'modes'];
    listed.push('notifications');
    if (Added) listed.push('models');
    if (Added && shell.info.value !== null) listed.push('runtime');
    const chosen = listed.includes(chosenSection.value) ? chosenSection.value : listed[0]!;
    const root = useRef<HTMLDivElement>(null);
    const { held, said } = useHiddenAlerts(root, chosen);
    // Escape leaves Settings where nothing else takes the key; the console's Live has a place to leave to.
    const withWayOut = essentials();
    useEffect(() => {
        if (!withWayOut) return;
        window.addEventListener('keydown', escapeFromSettings);
        return () => window.removeEventListener('keydown', escapeFromSettings);
    }, [withWayOut]);
    return (
        <div className="settings" ref={root} data-chosen={essentials() ? chosen : undefined}>
            {withWayOut && (
                <div className="settings-back">
                    <button type="button" onClick={leaveSettings}>
                        {/* Inline: svg() is markup for Painted, whose div wrapper would break the button's svg-and-text row. */}
                        <svg
                            width="14"
                            height="14"
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="2"
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            aria-hidden="true"
                        >
                            <path d="M6 9l6 6 6-6" />
                        </svg>
                        {t('settings.back')}
                    </button>
                </div>
            )}
            <h1>{t('settings.title')}</h1>
            {essentials() && (
                <nav className="settings-list" aria-label={t('settings.title')}>
                    <p className="settings-group">{t('settings.group')}</p>
                    {listed.map((name) => (
                        <button
                            key={name}
                            type="button"
                            aria-current={name === chosen ? 'true' : undefined}
                            data-flagged={held[name] ? '' : undefined}
                            aria-describedby={held[name]?.id}
                            title={held[name]?.text}
                            onClick={() => {
                                chosenSection.value = name;
                            }}
                        >
                            <SectionMark name={name} />
                            {names[name]()}
                        </button>
                    ))}
                    <p className="settings-announce" role="alert">
                        {said?.text}
                    </p>
                    <UpdateFooter />
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
            <NotificationsSection />
            {Added && <Added />}
        </div>
    );
}
