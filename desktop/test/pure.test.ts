import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { delimiter, join, resolve } from 'node:path';
import { test } from 'node:test';
import { nextCheckDelay, updateCheckInterval } from '../src/backoff.ts';
import { planCommand, windowsShim, wrapperScript } from '../src/command-line.ts';
import { loopbackOrigin, pairingLink, runtimeOrigin } from '../src/descriptor.ts';
import { feedArguments, verbOf } from '../src/installation.ts';
import { dataDirectory, desktopPaths } from '../src/paths.ts';
import { permissionAllowed } from '../src/permissions.ts';
import { findOnPath, launchOf, npmScript } from '../src/program-path.ts';
import { pathOf, withPath } from '../src/programs.ts';
import { hosts, keyProviders, notTheChatYet, providerEnvironment, signInSentence } from '../src/providers-model.ts';
import { isFeed, parseSettings } from '../src/settings.ts';
import { versionsDifferSentence } from '../src/setup.ts';
import { pluginValid, runtimeVersionIn, statusFacts, versionAtLeast, versionIn } from '../src/setup-model.ts';
import { loginShellOptions, pathFromOutput, withUsualPlaces } from '../src/shell-path.ts';

// ---- One installation ----------------------------------------------------------------------------------------------

test('the data directory is JASON_DATA_DIR when it is set, and ~/.jason otherwise', () => {
    const home = resolve('/home/someone');
    assert.equal(dataDirectory({}, home), join(home, '.jason'));
    assert.equal(dataDirectory({ JASON_DATA_DIR: '  ' }, home), join(home, '.jason'));
    assert.equal(dataDirectory({ JASON_DATA_DIR: resolve('/elsewhere/data') }, home), resolve('/elsewhere/data'));
});

test('the desktop’s own files are under <data>/desktop and its log beside the runtime’s', () => {
    const data = resolve('/d');
    const paths = desktopPaths(data);
    assert.equal(paths.settings, join(data, 'desktop', 'settings.json'));
    assert.equal(paths.chat, join(data, 'desktop', 'chat'));
    assert.equal(paths.providerKeys, join(data, 'desktop', 'provider-keys'));
    assert.equal(paths.logs, join(data, 'logs'));
    assert.equal(paths.installationSettings, join(data, 'config', 'settings.json'));
    assert.equal(paths.runFile, join(data, 'run', 'runtime.json'));
});

test('Electron keeps its own files under <data>/desktop/electron and its logs in electron/logs, set before the lock and before ready', () => {
    const data = resolve('/d');
    assert.equal(desktopPaths(data).electron, join(data, 'desktop', 'electron'));
    assert.equal(desktopPaths(data).electronLogs, join(data, 'desktop', 'electron', 'logs'));
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const lock = main.indexOf('app.requestSingleInstanceLock()');
    const ready = main.indexOf('app.whenReady()');
    for (const set of ["app.setPath('userData', early.electron)", "app.setPath('sessionData', early.electron)", 'app.setAppLogsPath(early.electronLogs)']) {
        const at = main.indexOf(set);
        assert.ok(at > 0, `main.ts runs ${set}`);
        assert.ok(at < lock && at < ready, `${set} comes before the lock, which lives in that folder, and before ready`);
    }
});

test('the page is granted one permission, writing to the clipboard, and only at the runtime’s origin', () => {
    const origin = 'http://127.0.0.1:41234';
    assert.equal(permissionAllowed('clipboard-sanitized-write', origin, origin), true);
    assert.equal(permissionAllowed('clipboard-sanitized-write', `${origin}/ui/#/attention`, origin), true);
    assert.equal(permissionAllowed('clipboard-sanitized-write', 'http://127.0.0.1:9999', origin), false);
    assert.equal(permissionAllowed('clipboard-sanitized-write', 'file:///C:/app/shell/starting.html', origin), false);
    assert.equal(permissionAllowed('clipboard-sanitized-write', origin, null), false);
    assert.equal(permissionAllowed('clipboard-sanitized-write', 'not a url', origin), false);
    for (const other of ['clipboard-read', 'media', 'notifications', 'geolocation', 'openExternal', 'fullscreen']) {
        assert.equal(permissionAllowed(other, origin, origin), false, other);
    }
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    assert.ok(main.includes('setPermissionRequestHandler((_contents, permission, callback, details) => callback(permissionAllowed(permission, details.requestingUrl, origin())))'));
    assert.ok(main.includes('setPermissionCheckHandler((_contents, permission, requestingOrigin) => permissionAllowed(permission, requestingOrigin, origin()))'));
});

// ---- The run file and the pairing link -----------------------------------------------------------------------------

test('the run file gives the loopback origin and nothing else', () => {
    const file = JSON.stringify({ api_version: 'v1', base_url: 'http://127.0.0.1:41234', token: 'secret-run-token', pid: 1 });
    assert.equal(runtimeOrigin(file), 'http://127.0.0.1:41234');
    assert.equal(runtimeOrigin('not json'), null);
    assert.equal(runtimeOrigin(JSON.stringify({ base_url: 'http://example.com:1' })), null);
    assert.equal(runtimeOrigin(JSON.stringify({ base_url: 'https://127.0.0.1:1' })), null);
    assert.equal(runtimeOrigin(JSON.stringify({ base_url: 'http://user:pass@127.0.0.1:1' })), null);
    assert.equal(runtimeOrigin(JSON.stringify({ base_url: 'http://localhost:1' })), null);
    assert.equal(loopbackOrigin('http://127.0.0.1:5/v1/'), 'http://127.0.0.1:5');
});

test('the pairing link is taken only when it is a UI page at the runtime’s origin', () => {
    const origin = 'http://127.0.0.1:41234';
    const link = `${origin}/ui/#pair=abc`;
    assert.equal(pairingLink(JSON.stringify({ url: link, expires_at: 'x' }), origin), link);
    assert.equal(pairingLink(JSON.stringify({ url: 'http://127.0.0.1:9/ui/#pair=abc' }), origin), null);
    assert.equal(pairingLink(JSON.stringify({ url: `${origin}/v1/system.info` }), origin), null);
    assert.equal(pairingLink(JSON.stringify({ url: 'https://example.com/ui/' }), origin), null);
    assert.equal(pairingLink('{', origin), null);
});

// ---- What the CLI said ---------------------------------------------------------------------------------------------

test('jason status is read by its checks: the runtime up, and whether it is the bundle’s version', () => {
    const status = (runtime: string, version: string) =>
        JSON.stringify({ ready: false, checks: [{ name: 'runtime', state: runtime }, { name: 'runtime_version', state: version }] });
    assert.deepEqual(statusFacts(status('ok', 'ok')), { runtimeUp: true, sameVersion: true });
    assert.deepEqual(statusFacts(status('ok', 'failed')), { runtimeUp: true, sameVersion: false });
    assert.deepEqual(statusFacts(status('failed', 'unknown')), { runtimeUp: false, sameVersion: false });
    assert.deepEqual(statusFacts('jason: not json'), { runtimeUp: false, sameVersion: false });
    assert.equal(runtimeVersionIn(JSON.stringify({ runtime_version: '0.34.0-rc.1' })), '0.34.0-rc.1');
    assert.equal(runtimeVersionIn(''), null);
    assert.equal(pluginValid(JSON.stringify({ plugins: [{ id: 'reply', status: 'valid' }] }), 'reply'), true);
    assert.equal(pluginValid(JSON.stringify({ plugins: [{ id: 'reply', status: 'unavailable' }] }), 'reply'), false);
});

test('the system screen’s sentence names jason update when the versions differ, and nothing when they agree', () => {
    assert.equal(versionsDifferSentence('0.34.0-rc.1', '0.34.0-rc.1'), null);
    assert.equal(versionsDifferSentence(null, '0.34.0-rc.1'), null);
    // The runtime reports its build after a plus, as the executable the desktop carries does; that is the one version.
    assert.equal(versionsDifferSentence('0.34.0-rc.1+14315ba2bcc0ac52ba8eeebbd1b52d9306913d5e', '0.34.0-rc.1'), null);
    assert.notEqual(versionsDifferSentence('0.34.0-rc.2+14315ba2', '0.34.0-rc.1'), null);
    const sentence = versionsDifferSentence('0.33.0', '0.34.0-rc.1') ?? '';
    assert.match(sentence, /0\.33\.0/);
    assert.match(sentence, /0\.34\.0-rc\.1/);
    assert.match(sentence, /`jason update`/);
});

test('a log names a CLI verb by its closed list, never by its arguments', () => {
    assert.equal(verbOf(['status']), 'status');
    assert.equal(verbOf(['plugin', 'install', 'slack', '--feed', 'http://127.0.0.1:1/m.json']), 'plugin install');
    assert.equal(verbOf(['skills', 'install', '--root', '/private/path']), 'skills install');
    assert.equal(verbOf(['secret-word']), 'other');
});

test('the feed setting is passed as --feed, and a feed the CLI would refuse is never kept', () => {
    assert.deepEqual(feedArguments({ feed: null, skillsDeployedFor: null }), []);
    assert.deepEqual(feedArguments({ feed: 'http://127.0.0.1:8/manifest.json', skillsDeployedFor: null }), ['--feed', 'http://127.0.0.1:8/manifest.json']);
    assert.equal(isFeed('https://example.com/manifest.json'), true);
    assert.equal(isFeed('http://localhost:9/manifest.json'), true);
    assert.equal(isFeed('http://example.com/manifest.json'), false);
    assert.equal(isFeed('file:///manifest.json'), false);
    assert.deepEqual(parseSettings(JSON.stringify({ feed: 'http://example.com/m.json', skills_deployed_for: '0.34.0-rc.1' })), {
        feed: null,
        skillsDeployedFor: '0.34.0-rc.1',
    });
    assert.deepEqual(parseSettings('{'), { feed: null, skillsDeployedFor: null });
});

// ---- Finding and starting programs ---------------------------------------------------------------------------------

test('a program is found on the PATH by the PATH itself, on Windows by PATHEXT', () => {
    const files = new Set(['C:\\tools\\reply.CMD'.toLowerCase(), 'C:\\bin\\claude.exe'.toLowerCase()]);
    const windows = { platform: 'win32' as const, path: '"C:\\bin";C:\\tools;', pathext: '.EXE;.CMD', isProgram: (file: string) => files.has(file.toLowerCase()) };
    assert.equal(findOnPath('claude', windows), 'C:\\bin\\claude.exe');
    assert.equal(findOnPath('reply', windows), 'C:\\tools\\reply.cmd');
    assert.equal(findOnPath('codex', windows), null);
    const posix = { platform: 'darwin' as const, path: '/usr/bin:/opt/homebrew/bin', isProgram: (file: string) => file === '/opt/homebrew/bin/claude' };
    assert.equal(findOnPath('claude', posix), '/opt/homebrew/bin/claude');
});

test('a batch file npm wrote is started as the script it names with Node, and any other batch file is refused', () => {
    const shim = '@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\n...\r\n"%_prog%"  "%dp0%\\node_modules\\reply-cli\\dist\\index.js" %*\r\n';
    assert.equal(npmScript(`${shim}IF EXIST "%dp0%\\node.exe" (SET "_prog=%dp0%\\node.exe")`), 'node_modules\\reply-cli\\dist\\index.js');
    assert.equal(npmScript(':: Created by npm\r\nSET "NPM_CLI_JS=%~dp0\\node_modules\\npm\\bin\\npm-cli.js"\r\n"%NODE_EXE%" "%NPM_CLI_JS%" %*'), 'node_modules\\npm\\bin\\npm-cli.js');
    assert.equal(npmScript('node "%dp0%\\..\\evil.js" %*'), null);
    assert.equal(npmScript('"%_prog%"  "%dp0%\\..\\evil.js" %*'), null);
    assert.equal(npmScript('del /q C:\\'), null);
    // The script is the one npm's own program variable starts, never a quoted path that merely sits under node_modules.
    assert.equal(npmScript('"%dp0%\\node_modules\\x\\cli.js" %*'), null);
    assert.equal(npmScript('"%other%"  "%dp0%\\node_modules\\x\\cli.js" %*'), null);
    assert.equal(npmScript('SET "NPM_CLI_JS=%~dp0\\node_modules\\npm\\bin\\npm-cli.js"\r\nrun.exe "%NPM_CLI_JS%" %*'), null);

    const text = `${shim}node.exe`;
    const launch = launchOf('C:\\npm\\reply.cmd', 'win32', () => text, 'C:\\node\\node.exe', () => false);
    assert.deepEqual(launch, { file: 'C:\\node\\node.exe', prefix: ['C:\\npm\\node_modules\\reply-cli\\dist\\index.js'] });
    assert.deepEqual(launchOf('C:\\npm\\reply.cmd', 'win32', () => text, null, (file) => file === 'C:\\npm\\node.exe'), {
        file: 'C:\\npm\\node.exe',
        prefix: ['C:\\npm\\node_modules\\reply-cli\\dist\\index.js'],
    });
    assert.ok('refused' in launchOf('C:\\x\\other.bat', 'win32', () => 'echo hi', 'C:\\node\\node.exe', () => false));
    assert.ok('refused' in launchOf('C:\\npm\\reply.cmd', 'win32', () => text, null, () => false));
    assert.deepEqual(launchOf('/usr/local/bin/reply', 'darwin', () => null, null, () => false), { file: '/usr/local/bin/reply', prefix: [] });
});

test('the PATH is one entry, whatever case Windows wrote its name in', () => {
    assert.equal(pathOf({ Path: 'a;b' }), 'a;b');
    assert.deepEqual(withPath({ Path: 'old', HOME: 'h', EMPTY: undefined }, 'new'), { HOME: 'h', PATH: 'new' });
});

test('the login shell is killed outright at its time limit, since an interactive shell may ignore a polite signal', () => {
    assert.equal(loginShellOptions.killSignal, 'SIGKILL');
    assert.equal(loginShellOptions.timeout, 5000);
});

test('the login shell’s PATH is read between its two markers, and the usual places are added once', () => {
    assert.equal(pathFromOutput('motd noise\n__JASON_PATH__/usr/bin:/bin__JASON_PATH_END__'), '/usr/bin:/bin');
    assert.equal(pathFromOutput('__JASON_PATH__/usr/bin:/bin__JASON_PATH_END__\nlogout: saving session\n'), '/usr/bin:/bin', 'a logout script’s output is cut off');
    assert.equal(pathFromOutput('__JASON_PATH__/usr/bin:/bin\nlogout noise'), null, 'without the closing marker nothing is taken');
    assert.equal(pathFromOutput('no marker'), null);
    const home = resolve('/Users/x');
    const path = withUsualPlaces(['/usr/bin', '/opt/homebrew/bin'].join(delimiter), home);
    assert.equal(path.split(delimiter).filter((entry) => entry === '/opt/homebrew/bin').length, 1);
    assert.ok(path.split(delimiter).includes(join(home, '.local', 'bin')));
});

// ---- Hosts and providers -------------------------------------------------------------------------------------------

test('Claude Code and Codex drive the chat; every other host runs campaign work, not the chat yet', () => {
    assert.deepEqual(hosts.filter((host) => host.drivesChat).map((host) => host.id), ['claude', 'codex']);
    assert.deepEqual(hosts.map((host) => host.id), hosts.map((host) => host.program));
    assert.equal(notTheChatYet, 'Runs campaign work, not the chat yet.');
    assert.equal(signInSentence('claude'), 'Sign in once by running `claude` in a terminal; the chat uses that sign-in.');
});

test('a provider’s key reaches the host’s environment under the variable the host reads, and nothing without a key', () => {
    const anthropic = keyProviders.find((provider) => provider.id === 'claude-api-key');
    const deepseek = keyProviders.find((provider) => provider.id === 'deepseek');
    assert.ok(anthropic && deepseek);
    assert.deepEqual(providerEnvironment(anthropic, 'k', ''), { ANTHROPIC_API_KEY: 'k' });
    assert.deepEqual(providerEnvironment(deepseek, undefined, ''), {});
    const env = providerEnvironment(deepseek, 'k', '');
    assert.equal(env.ANTHROPIC_AUTH_TOKEN, 'k');
    assert.equal(env.ANTHROPIC_BASE_URL, 'https://api.deepseek.com/anthropic');
    assert.equal(env.ANTHROPIC_MODEL, 'deepseek-v4-pro');
});

test('a version is read from what a program printed and compared part by part', () => {
    assert.equal(versionIn('reply-cli 0.19.2 (abc)'), '0.19.2');
    assert.equal(versionIn('nothing'), null);
    assert.equal(versionAtLeast('0.19.0', '0.18.0'), true);
    assert.equal(versionAtLeast('0.9.9', '0.18.0'), false);
    assert.equal(versionAtLeast('0.18.0', '0.18.0'), true);
});

// ---- The command line and the update check's pace ------------------------------------------------------------------

test('jason is put on the PATH only where it shadows no other jason', () => {
    assert.deepEqual(planCommand(null, '/w/jason'), { name: 'jason' });
    assert.deepEqual(planCommand('/w/jason', '/w/jason'), { name: 'jason' });
    assert.deepEqual(planCommand('/usr/local/bin/jason', '/w/jason'), { name: 'jason-desktop', shadows: '/usr/local/bin/jason' });
    const script = wrapperScript("/Apps/it's/jason");
    assert.match(script, /exec '\/Apps\/it'\\''s\/jason' "\$@"/);
    assert.doesNotMatch(script, /JASON_DATA_DIR|Harness/);
    assert.equal(windowsShim('C:\\J\\jason.exe'), '@echo off\r\n"C:\\J\\jason.exe" %*\r\n');
    assert.throws(() => windowsShim('C:\\%PATH%\\jason.exe'));
});

test('a failed update check waits a minute, doubles each time, and never longer than a day', () => {
    assert.equal(nextCheckDelay(0), updateCheckInterval);
    assert.equal(nextCheckDelay(1), 60_000);
    assert.equal(nextCheckDelay(2), 120_000);
    assert.equal(nextCheckDelay(40), 24 * 60 * 60 * 1000);
});
