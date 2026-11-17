import { useEffect, useState } from 'react';
import type { DesktopUpdateItem } from '../../platform/desktop';
import { t } from '../../platform/i18n';
import { can, currentDesktop } from '../../platform/places';

/**
 * The foot of Settings' list in the desktop's own window: the desktop's name and version, and beside it the desktop's
 * update item as its menu shows it, the same label, enabled when the menu's is, and a press of it a click in the menu.
 * The item is the desktop's to say: the page reads it again whenever the desktop says it changed and never guesses a
 * step between, and with no item it shows the version alone. Nothing at all in a tab, and from a desktop of an earlier
 * version, which has no such item.
 */
export function UpdateFooter() {
    const [shown, setShown] = useState<DesktopUpdateItem | null>(null);
    useEffect(() => {
        const desktop = currentDesktop();
        if (desktop === undefined || !can('updates.item') || !can('updates.onChanged')) return;
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
    const press = () => {
        const desktop = currentDesktop();
        if (desktop === undefined || !can('updates.press')) return;
        // What comes of the press, a box or a refusal, is the desktop's own; the item follows through `onChanged`.
        desktop.updates.press().catch(() => undefined);
    };
    return (
        <div className="settings-update">
            <p className="settings-version">{t('settings.version', { version: shown.version })}</p>
            {shown.item !== null && (
                <button className="btn" type="button" disabled={!shown.item.enabled} onClick={press}>
                    {shown.item.label}
                </button>
            )}
        </div>
    );
}
