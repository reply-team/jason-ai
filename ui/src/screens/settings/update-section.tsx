import { t } from '../../platform/i18n';
import { can, currentDesktop } from '../../platform/places';
import { packageItemLabel, updateMark, updateShown } from './update-state';

/**
 * Update, a section of Settings in the desktop's own window: the desktop's name and version, and the desktop's update item
 * as its menu shows it, the same label, enabled when the menu's is, and a press of it a click in the menu. The item is the
 * desktop's to say: the page reads it again whenever the desktop says it changed and never guesses a step between. Under
 * the item, what the check the person last asked for found, as the desktop words it and for as long as its answer carries
 * it. While the desktop knows of a version it has not installed, one quiet mark beside the item. The deb's item, which
 * checks nothing itself, is said as words: the system's packages update it; its restart, once the system did, is a button.
 * Nothing at all in a tab, in the Demo, and from a desktop that shows no item.
 */
export function UpdateSection() {
    const shown = updateShown();
    if (shown === null) return null;
    const item = shown.item;
    const said = typeof shown.said === 'string' && shown.said !== '' ? shown.said : null;
    const newer = updateMark();
    const press = (label: string) => {
        const desktop = currentDesktop();
        if (desktop === undefined || !can('updates.press')) return;
        // The press names the label drawn, which the desktop acts on only while its item still says it; what comes of it,
        // a box or a refusal, is the desktop's own, and the item follows through `onChanged`.
        desktop.updates.press(label).catch(() => undefined);
    };
    return (
        <section data-section="update" className="settings-update">
            <h2>{t('settings.update')}</h2>
            <p className="settings-version">{t('settings.version', { version: shown.version })}</p>
            {item.label === packageItemLabel ? (
                <p className="hint">
                    Your system updates Jason Desktop and the jason it carries: Software Updater offers it, as{' '}
                    <code>sudo apt update && sudo apt upgrade</code> does in a terminal.
                </p>
            ) : (
                <p className="settings-update-item">
                    <button className="btn" type="button" disabled={!item.enabled} onClick={() => press(item.label)}>
                        {item.label}
                    </button>
                    {newer !== null && (
                        <span className="dot w" aria-label={t('settings.update.newer', { version: newer })} />
                    )}
                </p>
            )}
            {said !== null && (
                <p className="settings-said" role="status">
                    {said}
                </p>
            )}
        </section>
    );
}
