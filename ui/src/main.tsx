import { createRoot } from 'react-dom/client';
import { App } from './app';
import { activatePlugins } from './platform/plugin';
import { startTheme } from './platform/theme';
import { browserPlugins } from './plugins';
import './styles/app.css';

activatePlugins(browserPlugins);
startTheme();
createRoot(document.getElementById('app')!).render(<App />);
