import { createRoot } from 'react-dom/client';
import { App } from './app';
import { activatePlugins } from './platform/plugin';
import { startLocale } from './platform/i18n';
import { loadMode } from './platform/mode';
import { startTheme } from './platform/theme';
import { browserPlugins } from './plugins';
import './styles/app.css';

/** The browser's shell: the screens and their sidebar. */
export async function mount(element: HTMLElement): Promise<void> {
    activatePlugins(browserPlugins);
    startTheme();
    startLocale();
    // Live or Demo, from the address alone, before anything is drawn.
    await loadMode();
    createRoot(element).render(<App />);
}
