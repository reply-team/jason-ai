import type { ComponentType } from 'react';
import { mode } from './mode';

// Which shell this page is. The console, in the desktop's window, loads its own code and sets `console` and its extra
// Settings sections as that code loads; the classic shell (`src/app.tsx`), which the screen tests draw, never loads it,
// so these stay as they are here. "Live shows the essentials" is the console's rule alone: the classic shell reads
// `essentials` and finds it false.

export const consoleShell: { loaded: boolean; settingsSections: ComponentType | null } = {
    loaded: false,
    settingsSections: null,
};

/** True in the console while it runs Live; never in the classic shell, whatever the mode. */
export const essentials = (): boolean => consoleShell.loaded && mode.value === 'live';
