import { session } from './api/session';
import type { JasonDesktop } from './platform/desktop';
import { noDesktop, setPlace, tabDesktop } from './platform/places';
import { windowDesktop } from './platform/window-desktop';
import { start } from './start';

// One build, two shells: inside Jason Desktop, and in a tab the desktop opened with a bridge, the console; in any other
// browser the classic shell. The choice is `start.ts`'s. Each loads its own stylesheet, and its own code but for nine
// small modules of the console the classic shell shares: the html tag, its painter and the icons, which draw the
// Settings headings, the live words, which name an approval's sender and channel, `state.ts`, which the send modes'
// `open-settings.ts` reads, `waiting.ts` with `waiting-model.ts`, the gate an approval is approved by, which the
// Work items page approves through as the chat's cards and Needs you do, and `senders.ts` with `senders-model.ts`, the
// sending name an approval's card reads under the address it goes from, on the chat's cards and on Needs you's panel.
// Most are markup and words; `state.ts` holds signals, `waiting.ts` holds the gate's reads and timers and `senders.ts`
// the reads of the accounts. The rest of the console stays out of the classic chunk, and
// `ui/tests/guards/classic-chunk.test.ts` fails the build when a file beyond the nine is reached.
//
// Each shell is imported from a function of its own. The build hands a dynamic import the stylesheets its chunk needs;
// written as one `if`/`else` of two imports, the minifier folded them into a single import call that carried the
// classic shell's stylesheets alone, and the console drew unstyled. The browser smoke checks a style only the console's
// stylesheet gives, against the built page.
const element = document.getElementById('app')!;

async function startConsole(desktop: JasonDesktop, bootstrapped: boolean): Promise<void> {
    const shell = await import('./console/mount');
    await shell.mount(element, desktop, bootstrapped);
}

void start({ windowDesktop, session, tabDesktop, noDesktop, setPlace, startConsole });
