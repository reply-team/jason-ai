// The desktop's links, pure: the closed table of addresses and the page each opens, the parse that refuses anything
// else by a closed code, the arguments a start may carry a link in, which forms of the application take links at all,
// and the post that holds a link until the page takes it.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { configuration } from '../electron-builder.config.mjs';
import { templates } from '../src/log.ts';
import { linkIn, linkScheme, LinkPost, linksTaken, linkRegistration, parseLink, type LinkTarget } from '../src/deep-link.ts';

const id = (prefix: string, n = 1) => `${prefix}_${String(n).padStart(26, '0')}`;

test('every address of the table opens its page, and nothing else is an address', () => {
    assert.equal(linkScheme, 'jason');
    const table: [string, LinkTarget][] = [
        ['jason://needs', { page: 'needs' }],
        ['jason://campaigns', { page: 'campaigns' }],
        [`jason://campaign/${id('cmp')}`, { page: 'campaign', id: id('cmp') }],
        [`jason://approval/${id('apr')}`, { page: 'approval', id: id('apr') }],
        [`jason://question/${id('dec')}`, { page: 'question', id: id('dec') }],
        [`jason://workitem/${id('wi')}`, { page: 'workitem', id: id('wi') }],
        [`jason://person/${id('cnt')}`, { page: 'person', id: id('cnt') }],
    ];
    for (const [address, target] of table) {
        assert.deepEqual(parseLink(address), target, address);
        assert.deepEqual(parseLink(`${address}/`), target, `${address} with one trailing slash`);
        assert.deepEqual(parseLink(address.replace('jason:', 'JASON:')), target, 'the scheme is compared without case');
    }
});

test('an address out of the table is refused by a closed code, and never opens anything', () => {
    const refused: [unknown, string][] = [
        [`jason://campaign/${id('cmp')}${'x'.repeat(40)}`, 'too_long'],
        ['jason://' + 'a'.repeat(60), 'too_long'],
        ['https://example.com/', 'not_a_link'],
        ['jason:needs', 'not_a_link'],
        ['jason:/needs', 'not_a_link'],
        ['jasonx://needs', 'not_a_link'],
        ['', 'not_a_link'],
        [42, 'not_a_link'],
        [null, 'not_a_link'],
        ['jason://Needs', 'unknown_page'],
        ['jason://feed', 'unknown_page'],
        ['jason://settings', 'unknown_page'],
        ['jason://chat', 'unknown_page'],
        ['jason://needs?mode=full', 'unknown_page'],
        ['jason://needs#x', 'unknown_page'],
        ['jason://needs//', 'unknown_page'],
        ['jason://user@needs', 'unknown_page'],
        ['jason://needs:80', 'unknown_page'],
        ['jason://%6Eeeds', 'unknown_page'],
        ['jason://needs/x', 'unknown_page'],
        ['jason://campaign', 'bad_id'],
        ['jason://campaign/', 'bad_id'],
        [`jason://campaign/${id('apr')}`, 'bad_id'],
        [`jason://campaign/cmp_01jd${'0'.repeat(22)}`, 'bad_id'],
        [`jason://campaign/cmp_${'0'.repeat(25)}I`, 'bad_id'],
        [`jason://campaign/cmp_${'0'.repeat(25)}`, 'bad_id'],
        [`jason://campaign/${id('cmp')}/${id('cmp', 2)}`, 'too_long'],
        [`jason://campaign/${id('cmp')}/x`, 'bad_id'],
        [`jason://campaign/${id('cmp')}?x=1`, 'bad_id'],
        [`jason://campaign/../${id('cmp')}`, 'bad_id'],
        [`jason://campaign/%63mp_${'0'.repeat(26)}`, 'bad_id'],
        ['jason://campaign/cmp_01\u0000', 'bad_id'],
        ['jason://needs" --inspect', 'unknown_page'],
    ];
    for (const [address, code] of refused) assert.deepEqual(parseLink(address), { refused: code }, String(address));
});

test('a link is taken from a start\'s arguments alone, after the registration\'s "--" or bare, and nothing beside it', () => {
    const link = `jason://campaign/${id('cmp')}`;
    assert.deepEqual(linkIn([]), { kind: 'none' });
    assert.deepEqual(linkIn(['--hidden']), { kind: 'none' }, 'a start with no link is not this rule\'s');
    assert.deepEqual(linkIn([link]), { kind: 'link', raw: link }, 'as a deb\'s menu entry starts it');
    assert.deepEqual(linkIn(['--', link]), { kind: 'link', raw: link }, 'as the Windows registration starts it');
    assert.deepEqual(linkIn(['--', 'JASON://needs']), { kind: 'link', raw: 'JASON://needs' });
});

/**
 * The arguments a crafted address gives a start, as Windows splits the registration's command line
 * `"<exe>" "--" "%1"` when the address holds a quote and a space: each opens nothing.
 */
test('the arguments a crafted address would give a start open nothing: a quote, a space, --no-sandbox, --inspect, a second link', () => {
    const crafted: [string[], string][] = [
        [['--', 'jason://needs', '--no-sandbox', ''], 'beside_flag'],
        [['--', 'jason://needs', '--inspect=9229', ''], 'beside_flag'],
        [['--', 'jason://needs', '--gpu-launcher=cmd', ''], 'beside_flag'],
        [['--', 'jason://needs', '--uninstall'], 'beside_flag'],
        [['--', 'jason://needs', '--stop-runtime'], 'beside_flag'],
        [['--', 'jason://needs', '--hidden'], 'beside_flag'],
        [['--', 'jason://needs', 'more', 'words'], 'beside_flag'],
        [['--no-sandbox', 'jason://needs'], 'beside_flag'],
        [['--inspect', '--', 'jason://needs'], 'beside_flag'],
        [['jason://needs', '--'], 'beside_flag'],
        [['--', '--', 'jason://needs'], 'beside_flag'],
        [['.', 'jason://needs'], 'beside_flag'],
        [['--', 'jason://needs', 'jason://campaigns'], 'two_links'],
        [['jason://needs', 'jason://needs'], 'two_links'],
        [['--', 'jason://needs" --inspect "x'], 'link'],
    ];
    for (const [argv, said] of crafted) {
        const taken = linkIn(argv);
        if (said === 'link') {
            // One argument whole: the quote stayed inside the address, which the parse then refuses.
            assert.equal(taken.kind, 'link', JSON.stringify(argv));
            assert.deepEqual(parseLink((taken as { raw: string }).raw), { refused: 'unknown_page' });
        } else assert.deepEqual(taken, { kind: 'refused', reason: said }, JSON.stringify(argv));
    }
});

test('links are taken by a packaged application on Windows, macOS and the deb, and by no other form', () => {
    assert.equal(linksTaken({ packaged: true, platform: 'win32', form: null }), true);
    assert.equal(linksTaken({ packaged: true, platform: 'darwin', form: null }), true);
    assert.equal(linksTaken({ packaged: true, platform: 'linux', form: { form: 'deb' } }), true);
    assert.equal(linksTaken({ packaged: true, platform: 'linux', form: { form: 'appimage', file: '/home/a/Jason.AppImage' } }), false);
    assert.equal(linksTaken({ packaged: true, platform: 'linux', form: { form: 'folder' } }), false);
    assert.equal(linksTaken({ packaged: false, platform: 'win32', form: null }), false);
    assert.equal(linksTaken({ packaged: false, platform: 'darwin', form: null }), false);
});

test('Windows registers the scheme with "--" before the address, so nothing in an address is read as a switch; no other system registers at a start', () => {
    assert.deepEqual(linkRegistration('win32', true, 'C:\\Program Files\\Jason\\Jason.exe'), { scheme: 'jason', path: 'C:\\Program Files\\Jason\\Jason.exe', args: ['--'] });
    assert.equal(linkRegistration('win32', false, 'C:\\dev\\electron.exe'), null);
    assert.equal(linkRegistration('darwin', true, '/Applications/Jason.app/Contents/MacOS/Jason'), null);
    assert.equal(linkRegistration('linux', true, '/opt/Jason/jason-desktop-app'), null);
});

test('the post holds the newest link until a page takes it, sends at once while one listens, and holds again once it goes', () => {
    const sent: LinkTarget[] = [];
    const post = new LinkPost((target) => sent.push(target));
    post.arrived({ page: 'needs' });
    post.arrived({ page: 'campaigns' });
    assert.deepEqual(sent, []);
    assert.deepEqual(post.take(), { page: 'campaigns' }, 'a newer link replaces the one held');
    assert.equal(post.take(), null, 'taken once');
    post.arrived({ page: 'needs' });
    assert.deepEqual(sent, [{ page: 'needs' }]);
    post.away();
    post.arrived({ page: 'campaign', id: id('cmp') });
    assert.deepEqual(sent, [{ page: 'needs' }]);
    assert.deepEqual(post.take(), { page: 'campaign', id: id('cmp') });
});

test('the main process decides a start\'s link after the sandbox and before every other branch, registers on Windows, and hears macOS and a second start', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const sandbox = main.indexOf("const unsandboxed = sandboxRefusal(app.commandLine.hasSwitch('no-sandbox'), process.argv, linuxForm());");
    const start = main.indexOf('function startDesktop(): void {');
    const decided = main.indexOf('const fromArgs = linkIn(process.argv.slice(1));');
    assert.ok(sandbox > 0 && start > sandbox && decided > start, 'the link is decided inside the start the sandbox lets through');
    for (const later of ['uninstallStart(process.argv', 'stopRuntimeStart(process.argv', 'app.requestSingleInstanceLock(', 'app.whenReady().then(() => run(links, installationWasNew))']) {
        assert.ok(main.indexOf(later) > decided, `${later} comes after the link is decided`);
    }
    // A refused start does nothing else and quits.
    assert.ok(main.includes("    if (fromArgs.kind === 'refused') {\n        early.warn('deep_link_refused', { Code: fromArgs.reason });\n        app.exit(1);\n        return;\n    }"));
    // The lock carries a start's link to the running application, which reads it from additionalData alone.
    assert.ok(main.includes("app.requestSingleInstanceLock(fromArgs.kind === 'link' ? { link: fromArgs.raw } : undefined)"));
    assert.ok(main.includes("app.on('second-instance', (_event, argv, _directory, data) => {\n            const link = linkOf(data);"));
    // macOS: open-url is listened for before ready, in the start that holds the lock.
    const openUrl = main.indexOf("app.on('open-url', (event, url) => {");
    assert.ok(openUrl > main.indexOf("app.on('second-instance'") && openUrl < main.indexOf('void app.whenReady().then(() => run(links, installationWasNew));'));
    // Windows: registered at each start as linkRegistration says.
    assert.ok(main.includes('const registration = linkRegistration(process.platform, app.isPackaged, process.execPath);'));
    assert.ok(main.includes('!app.setAsDefaultProtocolClient(registration.scheme, registration.path, registration.args)'));
    // What the log says of a link: the page or the code, never the address.
    for (const call of main.match(/deep_link_\w+', \{[^}]*\}/g) ?? []) assert.doesNotMatch(call, /raw|url|link\b|data/, call);
});

test('the log\'s link templates hold a page or a code and never an address', () => {
    assert.equal(templates.deep_link_opened, 'A link opened the page {Page}');
    assert.equal(templates.deep_link_refused, 'A link opened nothing: {Code}');
    assert.equal(templates.deep_link_registration_failed, 'The jason link scheme could not be registered for this application');
});

test('the packages declare the scheme where links are taken: the macOS bundle and the deb, and the AppImage not at all', () => {
    const built = configuration({ version: '0.36.0' }) as { protocols?: unknown; mac: { protocols?: unknown }; deb: { mimeTypes?: unknown }; appImage: { mimeTypes?: unknown }; linux: { mimeTypes?: unknown; protocols?: unknown } };
    assert.deepEqual(built.mac.protocols, [{ name: 'Jason', schemes: ['jason'] }]);
    assert.deepEqual(built.deb.mimeTypes, ['x-scheme-handler/jason']);
    for (const none of [built.protocols, built.linux.protocols, built.linux.mimeTypes, built.appImage.mimeTypes]) assert.equal(none, undefined);
});
