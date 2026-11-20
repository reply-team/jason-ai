import type { JasonDesktop } from './platform/desktop';
import { windowDesktop } from './platform/window-desktop';
import { start } from './start';

// One build, two things it draws: the console inside Jason Desktop, and one message in any other browser tab. The
// choice is `start.ts`'s. Each loads its own stylesheet and its own code, so a plain tab loads nothing of the console.
//
// Each is imported from a function of its own. The build hands a dynamic import the stylesheets its chunk needs;
// written as one `if`/`else` of two imports, the minifier folded them into a single import call that carried one side's
// stylesheets alone, and the console drew unstyled. The browser smoke checks a style only the console's stylesheet
// gives, against the built page.
const element = document.getElementById('app')!;

async function startConsole(desktop: JasonDesktop): Promise<void> {
    const shell = await import('./console/mount');
    await shell.mount(element, desktop);
}

async function showOwnPlace(): Promise<void> {
    const place = await import('./own-place');
    place.showOwnPlace(element);
}

void start({ windowDesktop, startConsole, showOwnPlace });
