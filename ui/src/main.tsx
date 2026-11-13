import type { JasonDesktop } from './platform/desktop';

// One build, two shells: inside Jason Desktop the console, with the Live chat as its home; in a browser the classic
// shell. Each loads its own stylesheet, and its own code but for four small modules of the console the classic shell
// shares: the html tag, its painter and the icons, which draw the Settings headings, and the live words, which name an
// approval's sender and channel. None of the four imports a stylesheet or the console's state.
//
// Each shell is imported from a function of its own. The build hands a dynamic import the stylesheets its chunk needs;
// written as one `if`/`else` of two imports, the minifier folded them into a single import call that carried the
// classic shell's stylesheets alone, and the console drew unstyled. The browser smoke checks a style only the console's
// stylesheet gives, against the built page.
const element = document.getElementById('app')!;

async function startConsole(desktop: JasonDesktop): Promise<void> {
    const shell = await import('./console/mount');
    await shell.mount(element, desktop);
}

async function startClassic(): Promise<void> {
    const shell = await import('./classic');
    await shell.mount(element);
}

const desktop = window.jasonDesktop;
void (desktop ? startConsole(desktop) : startClassic());
