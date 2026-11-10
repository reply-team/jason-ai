import { createRoot } from 'react-dom/client';
import 'dockview-react/dist/styles/dockview.css';
import type { JasonDesktop } from '../platform/desktop';
import { activatePlugins } from '../platform/plugin';
import { startTheme } from '../platform/theme';
import { browserPlugins } from '../plugins';
import { Console } from './console';
import './console.css';
import './harness.css';

/** The desktop's shell, mounted when the page finds the desktop's seam. */
export function mount(element: HTMLElement, desktop: JasonDesktop): void {
    activatePlugins(browserPlugins);
    startTheme();
    createRoot(element).render(<Console desktop={desktop} />);
}
