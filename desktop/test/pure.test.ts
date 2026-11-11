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
import { hosts, keyedFolder, keyProviders, keyWord, notTheChatYet, providerEnvironment, signInSentence, withoutKey } from '../src/providers-model.ts';
import { isFeed, parseSettings } from '../src/settings.ts';
import { versionsDifferSentence } from '../src/setup.ts';
import { pluginValid, runtimeVersionIn, statusFacts, versionAtLeast, versionIn } from '../src/setup-model.ts';
import { loginShell, loginShellOptions, pathFromOutput, withUsualPlaces } from '../src/shell-path.ts';

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
    assert.equal(paths.turns, join(data, 'desktop', 'turns'));
    assert.equal(paths.rollback, join(data, 'desktop', 'rollback'));
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

test('jason status is read by its checks: the runtime up, whether it is the bundle’s version, and whether its UI needs installing', () => {
    const status = (runtime: string, version: string, ui = 'ok') =>
        JSON.stringify({ ready: false, checks: [{ name: 'runtime', state: runtime }, { name: 'runtime_version', state: version }, { name: 'ui_module', state: ui }] });
    assert.deepEqual(statusFacts(status('ok', 'ok')), { runtimeUp: true, sameVersion: true, uiNeeded: false, uiServed: true });
    assert.deepEqual(statusFacts(status('ok', 'failed')), { runtimeUp: true, sameVersion: false, uiNeeded: false, uiServed: true });
    // ui_module is failed only while a UI is served (with a problem, for another API, or a stale default); absent is none.
    assert.deepEqual(statusFacts(status('ok', 'ok', 'failed')), { runtimeUp: true, sameVersion: true, uiNeeded: true, uiServed: true });
    assert.deepEqual(statusFacts(status('ok', 'ok', 'absent')), { runtimeUp: true, sameVersion: true, uiNeeded: true, uiServed: false });
    assert.deepEqual(statusFacts(status('failed', 'unknown', 'unknown')), { runtimeUp: false, sameVersion: false, uiNeeded: true, uiServed: false });
    assert.deepEqual(statusFacts('jason: not json'), { runtimeUp: false, sameVersion: false, uiNeeded: true, uiServed: false });
    assert.equal(runtimeVersionIn(JSON.stringify({ runtime_version: '0.34.0-rc.1' })), '0.34.0-rc.1');
    assert.equal(runtimeVersionIn(''), null);
    assert.equal(pluginValid(JSON.stringify({ plugins: [{ id: 'reply', status: 'valid' }] }), 'reply'), true);
    assert.equal(pluginValid(JSON.stringify({ plugins: [{ id: 'reply', status: 'unavailable' }] }), 'reply'), false);
});

test('the system screen’s sentence names jason update when another installation’s runtime differs, and nothing when they agree', () => {
    for (const ours of [false, true]) {
        assert.equal(versionsDifferSentence('0.34.0-rc.1', '0.34.0-rc.1', ours), null);
        assert.equal(versionsDifferSentence(null, '0.34.0-rc.1', ours), null);
        // The runtime reports its build after a plus, as the executable the desktop carries does; that is the one version.
        assert.equal(versionsDifferSentence('0.34.0-rc.1+14315ba2bcc0ac52ba8eeebbd1b52d9306913d5e', '0.34.0-rc.1', ours), null);
        assert.notEqual(versionsDifferSentence('0.34.0-rc.2+14315ba2', '0.34.0-rc.1', ours), null);
    }
    const sentence = versionsDifferSentence('0.33.0', '0.34.0-rc.1', false) ?? '';
    assert.match(sentence, /0\.33\.0/);
    assert.match(sentence, /0\.34\.0-rc\.1/);
    assert.match(sentence, /`jason update`/);
    assert.doesNotMatch(sentence, /Check for Updates/);
});

test('the system screen’s sentence names Check for Updates when the runtime is the desktop’s own, which jason update refuses to change', () => {
    const sentence = versionsDifferSentence('0.35.0', '0.34.0', true) ?? '';
    assert.match(sentence, /0\.35\.0/);
    assert.match(sentence, /0\.34\.0/);
    assert.match(sentence, /Check for Updates…/);
    // A Windows or Linux window shows no menu bar, so the sentence names the tray's menu too, in the CLI's own words.
    assert.match(sentence, /in the desktop's Jason menu, or in the menu of its icon in the tray/);
    assert.doesNotMatch(sentence, /jason update/);
});

test('a log names a CLI verb by its closed list, never by its arguments', () => {
    assert.equal(verbOf(['status']), 'status');
    assert.equal(verbOf(['runtime', 'stop']), 'runtime stop');
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
    assert.equal(loginShellOptions({}).killSignal, 'SIGKILL');
    assert.equal(loginShellOptions({}).timeout, 5000);
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

test('on Linux the usual places are the same less Homebrew’s macOS folder, with Homebrew on Linux’s own', () => {
    const home = resolve('/home/x');
    const entries = withUsualPlaces('/usr/bin', home, 'linux').split(delimiter);
    assert.deepEqual(entries, ['/usr/bin', join(home, '.local', 'bin'), join(home, '.claude', 'local'), join(home, '.npm-global', 'bin'), '/usr/local/bin', '/home/linuxbrew/.linuxbrew/bin']);
    assert.ok(withUsualPlaces('/usr/bin', home, 'darwin').split(delimiter).includes('/opt/homebrew/bin'));
});

test('the login shell is asked on macOS and Linux, its default the one each starts a terminal with when SHELL names none', () => {
    assert.equal(loginShell('darwin', {}), '/bin/zsh');
    assert.equal(loginShell('linux', {}), '/bin/sh');
    assert.equal(loginShell('linux', { SHELL: '/usr/bin/fish' }), '/usr/bin/fish');
    assert.equal(loginShell('win32', { SHELL: '/usr/bin/bash' }), null);
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
    assert.deepEqual(providerEnvironment(anthropic, 'k', '', '/f'), { ANTHROPIC_API_KEY: 'k', CLAUDE_CONFIG_DIR: '/f' });
    assert.deepEqual(providerEnvironment(deepseek, undefined, '', '/f'), {});
    const env = providerEnvironment(deepseek, 'k', '', '/f');
    assert.equal(env.ANTHROPIC_AUTH_TOKEN, 'k');
    assert.equal(env.ANTHROPIC_BASE_URL, 'https://api.deepseek.com/anthropic');
    assert.equal(env.ANTHROPIC_MODEL, 'deepseek-v4-pro');
    assert.equal(env.CLAUDE_CONFIG_DIR, '/f');
});

test('the OpenAI key reaches codex exec as CODEX_API_KEY, the variable OpenAI documents for it, with a home of the desktop\'s own', () => {
    const openai = keyProviders.find((provider) => provider.id === 'codex-api-key');
    assert.ok(openai);
    assert.deepEqual(providerEnvironment(openai, 'k', '', '/f'), { CODEX_API_KEY: 'k', CODEX_HOME: '/f' });
});

test('GLM and MiniMax name the default model their own guides for Claude Code give, with no window suffix', () => {
    const models = (id: string) => keyProviders.find((provider) => provider.id === id)?.models;
    assert.deepEqual(models('zai'), ['glm-5.3', 'glm-5.3-flash']);
    assert.deepEqual(models('minimax'), ['MiniMax-M3.1-Flash']);
    for (const id of ['zai', 'minimax']) {
        const provider = keyProviders.find((each) => each.id === id);
        assert.ok(provider);
        const env = providerEnvironment(provider, 'k', '', '/f');
        for (const name of ['ANTHROPIC_MODEL', 'ANTHROPIC_DEFAULT_OPUS_MODEL', 'ANTHROPIC_DEFAULT_SONNET_MODEL', 'ANTHROPIC_DEFAULT_HAIKU_MODEL']) assert.equal(env[name], models(id)?.[0], `${id} ${name}`);
    }
});

test('a keyed turn\'s host folder is the desktop\'s own: one per provider for Claude Code, one for Codex', () => {
    const paths = desktopPaths(join('/home', 'a', '.jason'));
    assert.equal(paths.claudeKeyed, join(paths.desktop, 'claude-keyed'));
    assert.equal(paths.codexKeyed, join(paths.desktop, 'codex-keyed'));
    for (const provider of keyProviders) {
        const expected = provider.program === 'claude' ? join(paths.claudeKeyed, provider.id) : paths.codexKeyed;
        assert.equal(keyedFolder(paths, provider), expected, provider.id);
        assert.match(provider.id, /^[a-z0-9-]+$/, 'a folder name from the closed table');
    }
});

test('on a keyed turn a host error text keeps no part of the key: the whole key, its ends, the masked form OpenAI prints, any key-shaped token', () => {
    const key = 'sk-proj-PLANTEDkeyMiddle0123456789Wq7Z';
    const masked = `Incorrect API key provided: sk-proj-${'*'.repeat(40)}Wq7Z. You can find your API key at the dashboard.`;
    assert.equal(withoutKey(masked, key), `Incorrect API key provided: ${keyWord}. You can find your API key at the dashboard.`);
    assert.equal(withoutKey(`unexpected status 401 Unauthorized: {"message":"bad key ${key}"}`, key), `unexpected status 401 Unauthorized: {"message":"bad key ${keyWord}"}`);
    assert.equal(withoutKey('the key ****Wq7Z was refused', key), `the key ${keyWord} was refused`);
    assert.equal(withoutKey('the key Wq7Z was refused', key), `the key ${keyWord} was refused`);
    assert.equal(withoutKey('the key sk-pro... was refused', key), `the key ${keyWord} was refused`);
    // A key of another shape: a compatible endpoint's, by its own first six and last four characters.
    const zai = 'a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6.Zk9QvPlantedSecret';
    assert.equal(withoutKey('authentication failed for a1b2c3...cret', zai), `authentication failed for ${keyWord}`);
    // A token shaped as a provider's key is replaced whoever's it is; a token as long as one, too.
    assert.equal(withoutKey('header sk-ant-api03-OTHERKEYxx was refused', key), `header ${keyWord} was refused`);
    assert.equal(withoutKey('bearer eyJhbGciOiJIUzI1NiJ9.e30.abc rejected', key), `bearer ${keyWord} rejected`);
    assert.equal(withoutKey('token AbCdEfGhIjKlMnOpQrStUvWxYz012345 rejected', key), `token ${keyWord} rejected`);
    // Words that carry none of it stay as they are.
    assert.equal(withoutKey('Codex ended the turn: the model is overloaded, status 529.', key), 'Codex ended the turn: the model is overloaded, status 529.');
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

test('the login shell’s answer keeps its absolute folders in order and drops the rest; with none left, the inherited PATH stands', () => {
    const between = (path: string) => `__JASON_PATH__${path}__JASON_PATH_END__`;
    assert.equal(pathFromOutput(between('/usr/local/bin:/usr/bin:/home/a/.npm-global/bin')), '/usr/local/bin:/usr/bin:/home/a/.npm-global/bin');
    assert.equal(
        pathFromOutput(between('/usr/local/bin:.:/usr/bin:./node_modules/.bin:/home/a/bin')),
        '/usr/local/bin:/usr/bin:/home/a/bin',
        'a relative entry would be read against the folder a child starts in, the chat’s among them',
    );
    assert.equal(pathFromOutput(between('/usr/bin:/bin\u0007x:/sbin')), '/usr/bin:/sbin', 'an entry with a control character is dropped');
    assert.equal(pathFromOutput(between('.:./node_modules/.bin')), null, 'no absolute entry left');
    assert.equal(pathFromOutput(between('$PATH')), null, 'a shell that does not expand "$PATH" in double quotes printed it as it is');
    assert.equal(pathFromOutput(between('[/usr/bin /bin]')), null, 'a shell that prints its PATH as a list');
});
