// The pages say what the Command Line item is on each system, and what a start does when an update holds the lock, in
// the terms the desktop's own code decides them by: on macOS, Windows and Linux the item is the machine set-up's status
// (`commandIsSetup`), with its Put Back act; and `install_in_progress` and
// `update_in_progress` leave the set-up for later (`laterCodes`), never a failure.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { commandIsSetup, laterCodes, putBackLabel } from '../src/machine-setup.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

function flat(...path: string[]): string {
    return readFileSync(join(root, ...path), 'utf8').replace(/\s+/g, ' ');
}

const desktop = flat('docs', 'desktop.md');
const readme = flat('README.md');

/** The desktop page's bullet that starts with `start`, up to the next bullet. */
function bullet(start: string): string {
    const from = desktop.indexOf(start);
    assert.ok(from >= 0, `the desktop page has no bullet starting ${start}`);
    const to = desktop.indexOf(' - **', from + start.length);
    return desktop.slice(from, to < 0 ? undefined : to);
}

test('the item is the set-up on macOS, Windows and Linux', () => {
    assert.equal(commandIsSetup('darwin'), true);
    assert.equal(commandIsSetup('win32'), true);
    assert.equal(commandIsSetup('linux'), true);

    const setup = bullet('- **Command Line**, on macOS, Windows and Linux,');
    assert.ok(setup.includes('shows what the machine set-up placed'), setup);
    assert.ok(setup.includes(putBackLabel.replace('…', '')), setup);
    assert.ok(!desktop.includes('- **Command Line**, on Linux,'), 'the page still has a Linux Command Line item of its own');
});

test('the README and the desktop page say the first start sets the machine up on macOS and Windows', () => {
    assert.ok(readme.includes('on macOS and Windows the first start of each version puts `jason` there and gives Claude Code and Codex Jason\'s skills, each where it is found, and the Command Line item of its menu says what that placed and puts it back; on Linux that item puts a command there'));
    assert.ok(desktop.includes('installed by hand: the application\'s first start of each version then puts `jason` in your terminal'));
    assert.ok(desktop.includes('## The command line, and the login items'));
});

test('a set-up the update\'s lock holds is left for later, as the page says', () => {
    assert.deepEqual([...laterCodes].sort(), ['install_in_progress', 'update_in_progress']);
    assert.ok(desktop.includes('(`install_in_progress`, `update_in_progress`), nothing is set up and nothing recorded, the item says it is left for the next start, and it is no failure.'));
});
