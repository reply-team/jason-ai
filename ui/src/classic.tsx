import { createRoot } from 'react-dom/client';
import { App } from './app';
import { activatePlugins } from './platform/plugin';
import { startTheme } from './platform/theme';
import { browserPlugins } from './plugins';
import './styles/app.css';

/** The browser's shell: the screens and their sidebar. */
export function mount(element: HTMLElement): void {
    activatePlugins(browserPlugins);
    startTheme();
    createRoot(element).render(<App />);
}
