// The desktop's links, pure: the closed table of addresses and the page each opens, the parse that refuses anything
// else by a closed code, the arguments a start may carry a link in, which forms of the application take links at all,
// and the post that holds a link until the page takes it.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { configuration } from '../electron-builder.config.mjs';
import { templates } from '../src/log.ts';
import { linkIn, linkLimit, linkScheme, LinkPost, linksTaken, linkRegistration, parseLink, type LinkTarget } from '../src/deep-link.ts';

const id = (prefix: string, n = 1) => `${prefix}_${String(n).padStart(26, '0')}`;

test('every address of the table opens its page, and nothing else is an address', () => {
    assert.equal(linkScheme, 'jason-desktop');
    const table: [string, LinkTarget][] = [
        ['jason-desktop://feed', { page: 'feed' }],
        ['jason-desktop://needs', { page: 'needs' }],
        ['jason-desktop://campaigns', { page: 'campaigns' }],
        [`jason-desktop://campaign/${id('cmp')}`, { page: 'campaign', id: id('cmp') }],
        [`jason-desktop://approval/${id('apr')}`, { page: 'approval', id: id('apr') }],
        [`jason-desktop://question/${id('dec')}`, { page: 'question', id: id('dec') }],
        [`jason-desktop://workitem/${id('wi')}`, { page: 'workitem', id: id('wi') }],
        [`jason-desktop://person/${id('cnt')}`, { page: 'person', id: id('cnt') }],
        // What a toast's click hands on where the desktop is installed: the runtime's attention links, rebuilt from their
        // parsed parts by the command line, each opening Needs you.
        ['jason-desktop://attention', { page: 'needs' }],
        [`jason-desktop://attention/approvals/${id('apr')}`, { page: 'approval', id: id('apr') }],
        [`jason-desktop://attention/questions/${id('dec')}`, { page: 'question', id: id('dec') }],
    ];
    for (const [address, target] of table) {
        assert.deepEqual(parseLink(address), target, address);
        assert.deepEqual(parseLink(`${address}/`), target, `${address} with one trailing slash`);
        assert.deepEqual(parseLink(address.replace('jason:', 'JASON:')), target, 'the scheme is compared without case');
    }
});

test('an address out of the table is refused by a closed code, and never opens anything', () => {
    const refused: [unknown, string][] = [
        [`jason-desktop://campaign/${id('cmp')}${'x'.repeat(40)}`, 'too_long'],
        ['jason-desktop://' + 'a'.repeat(60), 'too_long'],
        ['https://example.com/', 'not_a_link'],
        ['jason-desktop:needs', 'not_a_link'],
        ['jason-desktop:/needs', 'not_a_link'],
        ['jason-desktopx://needs', 'not_a_link'],
        // The runtime's own scheme, which its toasts use on Windows, is never the desktop's.
        ['jason://needs', 'not_a_link'],
        ['', 'not_a_link'],
        [42, 'not_a_link'],
        [null, 'not_a_link'],
        ['jason-desktop://Needs', 'unknown_page'],
        ['jason-desktop://settings', 'unknown_page'],
        ['jason-desktop://chat', 'unknown_page'],
        ['jason-desktop://needs?mode=full', 'unknown_page'],
        ['jason-desktop://needs#x', 'unknown_page'],
        ['jason-desktop://needs//', 'unknown_page'],
        ['jason-desktop://user@needs', 'unknown_page'],
        ['jason-desktop://needs:80', 'unknown_page'],
        ['jason-desktop://%6Eeeds', 'unknown_page'],
        ['jason-desktop://needs/x', 'unknown_page'],
        ['jason-desktop://campaign', 'bad_id'],
        ['jason-desktop://campaign/', 'bad_id'],
        [`jason-desktop://campaign/${id('apr')}`, 'bad_id'],
        [`jason-desktop://campaign/cmp_01jd${'0'.repeat(22)}`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}I`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}`, 'bad_id'],
        [`jason-desktop://campaign/${id('cmp')}/${id('cmp', 2)}`, 'too_long'],
        [`jason-desktop://campaign/${id('cmp')}/x`, 'bad_id'],
        [`jason-desktop://campaign/${id('cmp')}?x=1`, 'bad_id'],
        [`jason-desktop://campaign/../${id('cmp')}`, 'bad_id'],
        [`jason-desktop://campaign/%63mp_${'0'.repeat(26)}`, 'bad_id'],
        ['jason-desktop://campaign/cmp_01\u0000', 'bad_id'],
        ['jason-desktop://needs" --inspect', 'unknown_page'],
        ['jason-desktop://Attention', 'unknown_page'],
        ['jason-desktop://attention/campaigns', 'unknown_page'],
        [`jason-desktop://attention/Approvals/${id('apr')}`, 'unknown_page'],
        [`jason-desktop://attention/elsewhere/${id('apr')}`, 'unknown_page'],
        ['jason-desktop://attention/approvals', 'bad_id'],
        ['jason-desktop://attention/approvals/', 'bad_id'],
        [`jason-desktop://attention/approvals/${id('dec')}`, 'bad_id'],
        [`jason-desktop://attention/questions/${id('apr')}`, 'bad_id'],
        [`jason-desktop://attention/approvals/${id('apr')}/x`, 'bad_id'],
        [`jason-desktop://attention/approvals/${id('apr')}/prompt`, 'too_long'],
        [`jason-desktop://attention/questions/${id('dec')}/session`, 'too_long'],
        [`jason-desktop://attention/approvals/apr_${'0'.repeat(25)}I`, 'bad_id'],
        [`jason-desktop://attention/approvals/${id('apr')}?x=1`, 'bad_id'],
        [`jason-desktop://attention//approvals/${id('apr')}`, 'unknown_page'],
    ];
    for (const [address, code] of refused) assert.deepEqual(parseLink(address), { refused: code }, String(address));
});

test('a link is taken from a start\'s arguments alone, after the registration\'s "--" or bare, and nothing beside it', () => {
    const link = `jason-desktop://campaign/${id('cmp')}`;
    assert.deepEqual(linkIn([]), { kind: 'none' });
    assert.deepEqual(linkIn(['--hidden']), { kind: 'none' }, 'a start with no link is not this rule\'s');
    assert.deepEqual(linkIn([link]), { kind: 'link', raw: link }, 'as a deb\'s menu entry starts it');
    assert.deepEqual(linkIn(['--', link]), { kind: 'link', raw: link }, 'as the Windows registration starts it');
    assert.deepEqual(linkIn(['--', 'JASON-DESKTOP://needs']), { kind: 'link', raw: 'JASON-DESKTOP://needs' });
    // A runtime link handed to the desktop is no link of its own: a start that carries it alone is a plain start.
    assert.deepEqual(linkIn(['jason://attention']), { kind: 'none' });
});

/** Hand-written lists a start's arguments could be: each opens nothing. */
test('the arguments a crafted address would give a start open nothing: a quote, a space, --no-sandbox, --inspect, a second link', () => {
    const crafted: [string[], string][] = [
        [['--', 'jason-desktop://needs', '--no-sandbox', ''], 'beside_flag'],
        [['--', 'jason-desktop://needs', '--inspect=9229', ''], 'beside_flag'],
        [['--', 'jason-desktop://needs', '--gpu-launcher=cmd', ''], 'beside_flag'],
        [['--', 'jason-desktop://needs', '--uninstall'], 'beside_flag'],
        [['--', 'jason-desktop://needs', '--stop-runtime'], 'beside_flag'],
        [['--', 'jason-desktop://needs', '--hidden'], 'beside_flag'],
        [['--', 'jason-desktop://needs', 'more', 'words'], 'beside_flag'],
        [['--no-sandbox', 'jason-desktop://needs'], 'beside_flag'],
        [['--inspect', '--', 'jason-desktop://needs'], 'beside_flag'],
        [['jason-desktop://needs', '--'], 'beside_flag'],
        [['--', '--', 'jason-desktop://needs'], 'beside_flag'],
        [['.', 'jason-desktop://needs'], 'beside_flag'],
        [['--', 'jason-desktop://needs', 'jason-desktop://campaigns'], 'two_links'],
        [['jason-desktop://needs', 'jason-desktop://needs'], 'two_links'],
        [['--', 'jason-desktop://needs" --inspect "x'], 'link'],
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
    assert.deepEqual(linkRegistration('win32', true, 'C:\\Program Files\\Jason\\Jason.exe'), { scheme: 'jason-desktop', path: 'C:\\Program Files\\Jason\\Jason.exe', args: ['--'] });
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
    const decided = main.indexOf('const fromArgs = linkIn(process.argv.slice(1), process.platform);');
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
    assert.deepEqual(built.mac.protocols, [{ name: 'Jason', schemes: ['jason-desktop'] }]);
    assert.deepEqual(built.deb.mimeTypes, ['x-scheme-handler/jason-desktop']);
    for (const none of [built.protocols, built.linux.protocols, built.linux.mimeTypes, built.appImage.mimeTypes]) assert.equal(none, undefined);
});

test('the desktop\'s scheme is jason-desktop on every platform, and the runtime\'s jason is never registered, declared or removed by it', () => {
    assert.equal(linkScheme, 'jason-desktop');
    assert.equal(linkRegistration('win32', true, 'C:\\Jason\\Jason.exe')?.scheme, 'jason-desktop');
    const built = configuration({ version: '0.36.0' }) as { mac: { protocols?: { schemes: string[] }[] }; deb: { mimeTypes?: string[] } };
    assert.deepEqual(built.mac.protocols?.flatMap((each) => each.schemes), ['jason-desktop']);
    assert.deepEqual(built.deb.mimeTypes, ['x-scheme-handler/jason-desktop']);
    const config = JSON.stringify(built);
    assert.doesNotMatch(config, /"jason"|x-scheme-handler\/jason"/, 'no builder option names the runtime\'s scheme');
    // No call into Electron's registration names a scheme of its own: each takes linkRegistration's, which is the desktop's.
    const sources = ['main.ts', 'uninstall-start.ts', 'uninstall.ts', 'deep-link.ts'].map((file) => readFileSync(join(import.meta.dirname, '..', 'src', file), 'utf8')).join('\n');
    // The code's own calls, the comments that quote them left out.
    const code = sources
        .split('\n')
        .filter((line) => !/^\s*(?:\*|\/\/|\/\*)/.test(line))
        .join('\n');
    const calls = code.match(/app\.(?:set|remove)AsDefaultProtocolClient\([^)]*\)|app\.isDefaultProtocolClient\([^)]*\)/g) ?? [];
    assert.equal(calls.length, 3, calls.join(' | '));
    for (const call of calls) {
        assert.match(call, /\((?:registration|written)\.scheme, (?:registration|written)\.path, (?:registration|written)\.args\)/, call);
    }
    assert.ok(sources.includes('HKEY_CURRENT_USER\\\\Software\\\\Classes\\\\jason-desktop'), 'the uninstall names the desktop\'s key');
    assert.doesNotMatch(sources, /Classes\\\\jason(?!-desktop)/, 'nothing names the runtime\'s key');
});

/** A valid body of 26 Crockford characters that holds every letter the alphabet takes. */
const lettered = ['0123456789ABCDEFGHJKMNPQRS', 'TVWXYZ00000000000000000000', 'ZZZZZZZZZZZZZZZZZZZZZZZZZZ'];

test('an id is held at its edges: 26 Crockford characters with every letter they take, and nothing else', () => {
    for (const body of lettered) {
        assert.deepEqual(parseLink(`jason-desktop://campaign/cmp_${body}`), { page: 'campaign', id: `cmp_${body}` }, body);
        assert.deepEqual(parseLink(`jason-desktop://workitem/wi_${body}`), { page: 'workitem', id: `wi_${body}` }, body);
    }
    const refused: [string, string][] = [
        [`jason-desktop://campaign/cmp_${'0'.repeat(27)}`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}L`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}O`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}U`, 'bad_id'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}I`, 'bad_id'],
        [`jason-desktop://campaign/CMP_${'0'.repeat(26)}`, 'bad_id'],
        ['jason-desktop://campaign/cmp_', 'bad_id'],
        [`jason-desktop://campaign/cmp${'0'.repeat(26)}`, 'bad_id'],
        [` jason-desktop://needs`, 'not_a_link'],
        [`jason-desktop://needs `, 'unknown_page'],
        ['\tjason-desktop://needs', 'not_a_link'],
        ['jason-desktop://needs\n', 'unknown_page'],
        ['jason-desktop://needs\\', 'unknown_page'],
        ['jason-desktop://ne\u0000eds', 'unknown_page'],
        ['jason-desktop://need\u0455', 'unknown_page'],
        ['ja\u017Fon-desktop://needs', 'not_a_link'],
        ['jason\u2010desktop://needs', 'not_a_link'],
        [`jason-desktop://campaign/cmp_${'0'.repeat(25)}\u0000`, 'bad_id'],
    ];
    for (const [address, code] of refused) assert.deepEqual(parseLink(address), { refused: code }, JSON.stringify(address));
    // The bound: an address of 72 characters is read, one of 73 is not; the longest in the table is within it.
    assert.equal(linkLimit, 72);
    const at72 = `jason-desktop://${'a'.repeat(72 - 'jason-desktop://'.length)}`;
    assert.equal(at72.length, 72);
    assert.deepEqual(parseLink(at72), { refused: 'unknown_page' });
    assert.deepEqual(parseLink(`${at72}a`), { refused: 'too_long' });
    const longest = `jason-desktop://attention/questions/${id('dec')}/`;
    assert.ok(longest.length <= linkLimit);
    assert.deepEqual(parseLink(longest), { page: 'question', id: id('dec') });
});

/**
 * The command Electron 44.10.2 writes for the registration, by its own rule (shell/browser/browser_win.cc,
 * GetProtocolLaunchPath, and shell/common/command_line_util_win.cc, AddQuoteForArg): each part quoted only when it holds
 * a space, a backslash or a quote, then ` "%1"`; Windows then puts the address where `%1` stands.
 */
function registeredCommand(registration: { path: string; args: string[] }, address: string): string {
    const quoted = (arg: string) => (/[ \\"]/.test(arg) ? `"${arg}"` : arg);
    return [quoted(registration.path), ...registration.args.map(quoted), `"${address}"`].join(' ');
}

/**
 * Windows' split of a command line into argv, by Microsoft's rules ("Parsing C command-line arguments"): arguments are
 * delimited by spaces or tabs; a quote opens or closes a quoted part; 2n backslashes before a quote give n backslashes
 * and the quote acts; 2n+1 give n and a literal quote; backslashes elsewhere are literal; a line that ends inside quotes
 * gives what was read as the last argument.
 */
function windowsSplit(line: string): string[] {
    const out: string[] = [];
    let current = '';
    let inQuotes = false;
    let started = false;
    for (let i = 0; i < line.length; i++) {
        const c = line[i]!;
        if (c === '\\') {
            let n = 0;
            while (line[i] === '\\') {
                n++;
                i++;
            }
            if (line[i] === '"') {
                current += '\\'.repeat(Math.floor(n / 2));
                if (n % 2 === 1) current += '"';
                else inQuotes = !inQuotes;
                started = true;
            } else {
                current += '\\'.repeat(n);
                started = true;
                i--;
            }
        } else if (c === '"') {
            inQuotes = !inQuotes;
            started = true;
        } else if ((c === ' ' || c === '\t') && !inQuotes) {
            if (started) out.push(current);
            current = '';
            started = false;
        } else {
            current += c;
            started = true;
        }
    }
    if (started) out.push(current);
    return out;
}

test('a crafted address, put in the command the registration writes and split as Windows splits it, opens nothing', () => {
    const registration = linkRegistration('win32', true, 'C:\\Program Files\\Jason\\Jason.exe')!;
    // The command as Electron writes it: the executable quoted, the "--" bare, the address quoted.
    assert.equal(registeredCommand(registration, '%1'), '"C:\\Program Files\\Jason\\Jason.exe" -- "%1"');
    const rows: [string, string[], string][] = [
        ['jason-desktop://needs', ['--', 'jason-desktop://needs'], 'link'],
        ['jason-desktop://needs" --no-sandbox "', ['--', 'jason-desktop://needs', '--no-sandbox', ''], 'beside_flag'],
        ['jason-desktop://needs" --inspect=0.0.0.0:9229 "', ['--', 'jason-desktop://needs', '--inspect=0.0.0.0:9229', ''], 'beside_flag'],
        ['jason-desktop://needs" --gpu-launcher="calc.exe" "', ['--', 'jason-desktop://needs', '--gpu-launcher=calc.exe', ''], 'beside_flag'],
        ['jason-desktop://needs" --uninstall "', ['--', 'jason-desktop://needs', '--uninstall', ''], 'beside_flag'],
        ['jason-desktop://needs" --stop-runtime "', ['--', 'jason-desktop://needs', '--stop-runtime', ''], 'beside_flag'],
        ['jason-desktop:" --uninstall', ['--', 'jason-desktop:', '--uninstall'], 'beside_flag'],
        ['jason-desktop://needs" "jason-desktop://campaigns', ['--', 'jason-desktop://needs', 'jason-desktop://campaigns'], 'two_links'],
        ['jason-desktop://needs" "jason://attention', ['--', 'jason-desktop://needs', 'jason://attention'], 'beside_flag'],
        ['jason-desktop://needs x', ['--', 'jason-desktop://needs x'], 'link'],
        ['jason-desktop://needs\\', ['--', 'jason-desktop://needs"'], 'link'],
        ['jason-desktop://needs\\" --no-sandbox', ['--', 'jason-desktop://needs" --no-sandbox'], 'link'],
    ];
    for (const [address, argv, said] of rows) {
        const split = windowsSplit(registeredCommand(registration, address));
        assert.equal(split[0], 'C:\\Program Files\\Jason\\Jason.exe', address);
        assert.deepEqual(split.slice(1), argv, address);
        const taken = linkIn(split.slice(1), 'win32');
        if (said === 'link') {
            assert.equal(taken.kind, 'link', address);
            // A whole address that is not one of the table's opens nothing; only the first row is.
            if (address !== 'jason-desktop://needs') assert.ok('refused' in parseLink((taken as { raw: string }).raw), address);
        } else assert.deepEqual(taken, { kind: 'refused', reason: said }, address);
    }
});

test('macOS takes a link from open-url alone: one in a start\'s arguments is refused there', () => {
    assert.deepEqual(linkIn(['jason-desktop://needs'], 'darwin'), { kind: 'refused', reason: 'unregistered' });
    assert.deepEqual(linkIn(['--', 'jason-desktop://needs'], 'darwin'), { kind: 'refused', reason: 'unregistered' });
    assert.deepEqual(linkIn([], 'darwin'), { kind: 'none' });
    assert.deepEqual(linkIn(['jason-desktop://needs'], 'linux'), { kind: 'link', raw: 'jason-desktop://needs' });
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('const fromArgs = linkIn(process.argv.slice(1), process.platform);'));
});

test('the deb\'s menu entry starts the program with "--" before the address', () => {
    const built = configuration({ version: '0.36.0' }) as { deb: { executableArgs?: string[] }; linux: { executableArgs?: string[] }; appImage: { executableArgs?: string[] } };
    assert.deepEqual(built.deb.executableArgs, ['--']);
    assert.deepEqual(built.linux.executableArgs, []);
    assert.equal(built.appImage.executableArgs, undefined);
});

test('a refused link on a second start shows the window, as every second start does, a development build\'s too', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const body = /function linkArrived\([^)]*\): void \{([\s\S]*?)\n\}/.exec(main)?.[1] ?? '';
    assert.match(body, /log\.warn\('deep_link_refused', \{ Code: 'unregistered' \}\);\n\s*showWindow\(\);\n\s*return;/);
});
