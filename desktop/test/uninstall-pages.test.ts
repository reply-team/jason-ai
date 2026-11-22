// The pages that take Jason off name, for each platform, the door the application offers there and no other: Uninstall
// Jason… where `uninstallOffered` says the item is offered, Remove What Jason Placed… where `removalOffered` says that act
// is, and Installed apps on Windows, where neither is. The labels are the menu's own constants, so a door moved in the code
// turns these rows red until the pages move with it.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { removalOffered, removeLabel } from '../src/remove-placed.ts';
import { uninstallLabel, uninstallOffered } from '../src/uninstall-door.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

function page(...path: string[]): string {
    return readFileSync(join(root, ...path), 'utf8').replace(/\r\n/g, '\n');
}

/** The text from a line that starts with `start` to the next line that starts with one of `ends`, whitespace flattened. */
function between(text: string, start: string, ends: string[]): string {
    const from = text.indexOf(`\n${start}`);
    assert.ok(from >= 0, `no line starts with ${start}`);
    let to = text.length;
    for (const end of ends) {
        const at = text.indexOf(`\n${end}`, from + 1);
        if (at >= 0 && at < to) {
            to = at;
        }
    }
    return text.slice(from, to).replace(/\s+/g, ' ');
}

/** The install page's two paragraphs under §7's "Jason Desktop", by their opening words. */
const oneUninstallParagraph = 'On macOS and Windows the application goes';
const linuxParagraph = 'On Linux the one uninstall is the same:';

const platforms: { platform: NodeJS.Platform; bullet: string }[] = [
    { platform: 'win32', bullet: '- **Windows.**' },
    { platform: 'darwin', bullet: '- **macOS.**' },
    { platform: 'linux', bullet: '- **Linux.**' },
];

/** The desktop page's paragraph for a platform under "Taking it off". */
function desktopPage(bullet: string): string {
    return between(page('docs', 'desktop.md'), bullet, ['- **', '\n', '#']);
}

/** The install page's paragraph under §7's "Jason Desktop". */
function installPage(start: string): string {
    const text = page('docs', 'INSTALL.md');
    const section = text.slice(text.indexOf('\n### Jason Desktop\n', text.indexOf('\n## 7. ')));
    return between(section, start, ['\n']);
}

for (const { platform, bullet } of platforms) {
    test(`the desktop page names the door offered on ${platform} and not the other`, () => {
        const text = desktopPage(bullet);
        assert.equal(text.includes(uninstallLabel), uninstallOffered(platform), `${bullet} and ${uninstallLabel}`);
        assert.equal(text.includes(removeLabel), removalOffered(platform), `${bullet} and ${removeLabel}`);
        assert.equal(text.includes('Installed apps'), platform === 'win32', `${bullet} and Installed apps`);
    });
}

test('the install page names Uninstall Jason… for macOS and for Linux, as the code offers it', () => {
    const oneUninstall = installPage(oneUninstallParagraph);
    assert.ok(uninstallOffered('darwin') && !removalOffered('darwin'));
    assert.ok(oneUninstall.includes(`${uninstallLabel}** in the Jason menu on macOS`), oneUninstall);
    assert.ok(oneUninstall.includes('Installed apps in Settings on Windows'), oneUninstall);
    assert.ok(!oneUninstall.includes(removeLabel), oneUninstall);

    const linux = installPage(linuxParagraph);
    assert.ok(uninstallOffered('linux') && !removalOffered('linux'));
    assert.ok(linux.includes(`${uninstallLabel} in the Jason menu`), linux);
    assert.ok(!linux.includes(removeLabel), linux);
});

test('no door is offered on Windows, where Installed apps is the one', () => {
    assert.equal(uninstallOffered('win32'), false);
    assert.equal(removalOffered('win32'), false);
});
