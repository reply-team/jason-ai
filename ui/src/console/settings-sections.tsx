import { html } from './html';
import { svg } from './icons';
import { Painted } from './painted';
import { modelsView } from './connect';
import { t } from '../platform/i18n';
import * as shell from '../shell/shell';
import { RuntimeCard } from '../screens/system/runtime-card';

const models = () => html`<h2>${svg('plug', 16, 'settings-icon')} <span>${t('settings.models')}</span></h2>`;

/** What the console's Settings adds in Live: Models, what Connect holds, and Runtime, what System holds. */
export function LiveSettingsSections() {
    const info = shell.info.value;
    return (
        <>
            <section data-section="models">
                <Painted view={models} className="settings-heading" />
                <Painted view={modelsView} className="settings-models" />
            </section>
            {info !== null && (
                <div data-section="runtime">
                    <RuntimeCard info={info} now={new Date()} />
                </div>
            )}
        </>
    );
}
