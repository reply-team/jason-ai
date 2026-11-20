import { useEffect, useState } from 'react';
import type { DesktopUpdateItem } from '../../platform/desktop';
import { t } from '../../platform/i18n';
import { windowDesktop } from '../../platform/window-desktop';

/**
 * The foot of Settings' list in the desktop's own window: the desktop's name and version, and beside it the desktop's
 * update item as its menu shows it, the same label, enabled when the menu's is, and a press of it a click in the menu.
 * The item is the desktop's to say: the page reads it again whenever the desktop says it changed and never guesses a
 * step between, and with no item it shows the version alone. Under the item, what the check the person last asked for
 * found, as the desktop words it and for as long as its answer carries it; a desktop of an earlier version carries none.
 * Nothing at all from a desktop of an earlier version, which has no such item.
 */
export function UpdateFooter() {
    const [shown, setShown] = useState<DesktopUpdateItem | null>(null);
    useEffect(() => {
        const desktop = windowDesktop();
        if (desktop === undefined) return;
        if (typeof desktop.updates?.item !== 'function' || typeof desktop.updates.onChanged !== 'function') return;
        let live = true;
        // Only the last read stands: an answer that comes back after a later one was asked for is dropped.
        let asked = 0;
        const read = () => {
            const mine = ++asked;
            desktop.updates
                .item()
                .then((answer) => {
                    if (live && mine === asked) setShown(answer);
                })
                .catch(() => undefined);
        };
        const stop = desktop.updates.onChanged(read);
        read();
        return () => {
            live = false;
            stop();
        };
    }, []);
    if (shown === null) return null;
    const item = shown.item;
    const said = typeof shown.said === 'string' && shown.said !== '' ? shown.said : null;
    const press = (label: string) => {
        const desktop = windowDesktop();
        if (desktop === undefined) return;
        // The press names the label drawn, which the desktop acts on only while its item still says it; what comes of it,
        // a box or a refusal, is the desktop's own, and the item follows through `onChanged`.
        desktop.updates.press(label).catch(() => undefined);
    };
    return (
        <div className="settings-update">
            <p className="settings-version">{t('settings.version', { version: shown.version })}</p>
            {item !== null && (
                <button className="btn" type="button" disabled={!item.enabled} onClick={() => press(item.label)}>
                    {item.label}
                </button>
            )}
            {said !== null && (
                <p className="settings-said" role="status">
                    {said}
                </p>
            )}
        </div>
    );
}
