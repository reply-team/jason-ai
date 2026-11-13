import { createRoot } from 'react-dom/client';
import 'dockview-react/dist/styles/dockview.css';
import type { JasonDesktop } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { startLocale } from '../platform/i18n';
import { loadMode } from '../platform/mode';
import { bindDesktopTheme, startTheme, type DesktopThemeSeam } from '../platform/theme';
import { browserPlugins } from '../plugins';
import { Console } from './console';
import './console.css';
import './harness.css';

/** The desktop's shell, mounted when the page finds the desktop's seam. */
export async function mount(element: HTMLElement, desktop: JasonDesktop): Promise<void> {
    activatePlugins(browserPlugins);
    // The desktop's kept theme is read before the first paint, so the page opens in it.
    await bindDesktopTheme((desktop as { theme?: DesktopThemeSeam }).theme);
    // Live or Demo, read once from the desktop before anything is drawn.
    await loadMode(desktop);
    startTheme();
    startLocale();
    createRoot(element).render(<Console desktop={desktop} />);
}
