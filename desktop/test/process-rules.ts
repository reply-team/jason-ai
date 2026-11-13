// The rules the process guard holds the desktop's sources to, as one function over a file's path and text, so a test
// can show each rule refusing a planted line as well as passing the tree.

/**
 * The modules allowed to start a process, each with the one thing it starts. One start is not here: `quitAndInstall`
 * starts the installer from inside `node_modules` (electron-updater's own spawn), the one start the guard cannot see.
 */
export const processModules: Readonly<Record<string, string>> = {
    'src/cli.ts': 'the bundled jason executable',
    'src/programs.ts': "the programs a Connect card runs: reply, npm, and the version of an agent host or of node; and an agent host's one naming run of a set-up chat, which src/chat-naming.ts gives its command line",
    'src/chat-process.ts': "an agent host for one turn of the chat, streamed, or an agent host's model listing (`codex app-server`, `startListing`), and on Windows taskkill from the system folder to end that turn's process tree, or a program the programs module ran past its time limit",
    'src/open-in-start.ts': "an editor or the file manager the page's Open in names, started on its own with the folder",
    'src/shell-path.ts': "the login shell, on macOS and Linux only, for the PATH a program started from the Finder or a session's menu lacks",
    'scripts/prepare.mjs': 'dotnet publish of Jason.App, to bundle the jason executable',
    'scripts/sign-windows.mjs': 'signtool, to sign each file of the Windows package through the Trusted Signing client',
};

/**
 * The two lines that may signal a process, each in its one module: the look whether the process a run file names is
 * running, with signal 0, which delivers nothing, and the end of a chat turn's process group or of a program run past
 * its limit. Anything else that reaches `process.kill` could stop whatever program holds a pid read from a file.
 */
export const signalLines: Readonly<Record<string, string>> = {
    'src/own-runtime.ts': 'process.kill(pid, 0);',
    'src/chat-process.ts': '(target, signal) => process.kill(target, signal),',
};

/**
 * The one module that may start an agent host's model listing (`startListing` in src/chat-process.ts, which starts
 * `codex app-server` and nothing else), with what it starts it for.
 */
export const listingCallers: Readonly<Record<string, string>> = {
    'src/model-lists.ts': "the models Codex offers, asked at most once a day per host id, one ask at a time",
};

const listingStart = /\bstartListing\b/;

/**
 * The one module that may call `startDetached` (src/open-in-start.ts), which starts any file with any arguments: main.ts,
 * where the Open in wiring hands it only a line the Open in module made from a place the desktop detected.
 */
export const startDetachedCallers: Readonly<Record<string, string>> = {
    'src/main.ts': 'the editor or file manager line the Open in module made from a detected place',
};

const detachedStart = /\bstartDetached\b/;
/** The starter's module named in any form (import, re-export, require, dynamic import), so no alias or computed access passes. */
const detachedModule = /['"`][^'"`]*\/open-in-start(?:\.ts|\.js)?['"`]/;

/**
 * The modules that call the program runner, `runFile`, with a command line of their own, and how many calls each makes:
 * the runner's own run of a program the PATH finds, a set-up chat's one naming run, and on Windows the check of Codex's
 * sandbox before a chat's first Codex turn, which runs cmd outside any sandbox and Codex. Anything else starts a program
 * through `Programs.run`, by a name the desktop knows.
 */
export const runFileCalls: Readonly<Record<string, number>> = { 'src/programs.ts': 1, 'src/chat-naming.ts': 1, 'src/codex-sandbox.ts': 2 };

/**
 * The one module that may name a shell other than the login-shell lookup, and only cmd.exe, in this one expression: cmd
 * by its full path under the system folder, given to `cmd`. The check runs its line with it outside any sandbox, and
 * `codex sandbox` runs it inside.
 */
export const sandboxedShell: Readonly<Record<string, string>> = { 'src/codex-sandbox.ts': "const cmd = win32.join(systemRoot, 'System32', 'cmd.exe');" };

/**
 * What each runner call of the sandbox check's module begins with, each at most once: cmd by the path the check made,
 * with the check's own arguments, and Codex as a turn's host is started.
 */
export const sandboxRuns: readonly string[] = ['runFile(check.cmd, check.inner, ', 'runFile(launch.file, [...launch.prefix, ...args], '];

const runFileCall = /\brunFile\s*\(/g;
const runFileDefinition = /function\s+runFile\s*\(/g;
/** The program runner's module, by its specifier: `./programs.ts` or `./programs`. */
const programsModule = String.raw`['"]\.\/programs(?:\.ts)?['"]`;
/** An import or re-export that names `runFile`, under any name it is given. */
const runFileNamed = new RegExp(String.raw`(?:import|export)\s*(?:type\s+)?\{[^}]*\brunFile\b[^}]*\}\s*from\s*${programsModule}`);
/** The runner's module taken whole, so what it holds is reached by a name the guard cannot read. */
const programsWhole = new RegExp(String.raw`import\s*\*\s*as\s+\w+\s*from\s*${programsModule}|\b(?:require|import)\s*\(\s*${programsModule}`);

/** The one module allowed to register an IPC handler, because it checks every sender's origin first. */
export const ipcModule = 'src/ipc.ts';

/**
 * The modules that may listen on a port, each with what it serves. Both listen on the loopback address alone, on a port
 * the system picks, and check every request's Host before anything else of it is read.
 */
export const listeningModules: Readonly<Record<string, string>> = {
    'src/chat-permission-endpoint.ts': "a chat turn's permission prompt tool and its Full access hook, for that turn's host alone, behind the turn's secret",
    'src/browser-bridge.ts': "the chat's view for a browser tab the desktop opened, behind the runtime's origin and a code the tab traded once",
};

/** A server module of Node imported or required by any form: what a listening socket is made with. */
const serverModule = /(?:from\s*|require\s*\(\s*|import\s*\(\s*)['"](?:node:)?(?:http|https|http2|net|tls|dgram)['"]/;
/** A server made or a socket listened on, by any name: the calls a module must not make outside the named ones. */
const listening = /\b(?:createServer|createSecureServer|createSocket)\s*\(|\.\s*listen\s*\(|\bWebSocketServer\b/;

/** The child_process functions a named module may import: both take a file and arguments and start no shell. */
const allowedImports = new Set(['execFile', 'spawn']);

const childProcess = /(?:from\s*|require\s*\(\s*|import\s*\(\s*)['"](?:node:)?child_process['"]/;
const childProcessImport = /import\s*(\{[^}]*\}|\*\s*as\s+\w+|\w+)\s*from\s*['"](?:node:)?child_process['"]/g;
const shellOption = /\bshell\s*:/;
const shellName = /['"`](?:cmd|cmd\.exe|command\.com|powershell|powershell\.exe|pwsh|pwsh\.exe|sh|bash|zsh|fish|\/bin\/sh|\/bin\/bash|\/bin\/zsh|\/usr\/bin\/env)['"`]/;
const shellVariable = /process\.env\.(?:SHELL|ComSpec|COMSPEC)\b|env\[\s*['"](?:SHELL|ComSpec|COMSPEC)['"]\s*\]/;
const childProcessRequire = /\brequire\s*\(\s*['"](?:node:)?child_process['"]/;
const childProcessDynamicImport = /\bimport\s*\(\s*['"](?:node:)?child_process['"]/;
const utilityProcess = /\butilityProcess\b/;
const registration = '(?:handle|handleOnce|on|once|addListener|prependListener)';
const ipcRegistration = new RegExp(`\\bipcMain\\s*\\.\\s*${registration}\\s*\\(`);
/** `webContents.ipc`, Electron's second registration surface, which answers a frame without the origin check. */
const contentsIpcRegistration = new RegExp(`\\.\\s*ipc\\s*\\.\\s*${registration}\\s*\\(`);
const processKill = /\bprocess\s*(?:\.\s*kill\b|\[\s*['"`]kill['"`]\s*\])/g;
const killTaken = /\{[^}]*\bkill\b[^}]*\}\s*=\s*process\b/;
const openPath = /\bshell\s*\.\s*openPath\b/;
const openExternal = /\bshell\s*\.\s*openExternal\b/g;

/**
 * The two openers inside main.ts's `lockNavigation` that hand a URL to the system browser, each by the name of the
 * function it is written in, each calling it once, with what it opens.
 */
export const navigationOpeners: Readonly<Record<string, string>> = {
    leave: 'an https link, and nothing else',
    ownPage: "a page of the runtime's own UI at its origin, under /ui/: the chat in a browser tab",
};

/**
 * Who may call the opener of the runtime's own page: the browser bridge's opening act alone. In main.ts, `ownPage` is
 * named outside the navigation lock only where the lock hands it out and where `openTab` is given it as its opener;
 * `openOwnPage` is named only by its module and, in main.ts, by its import and inside the lock.
 */
export const ownPageCaller = "src/main.ts: openTab's open, in the Open in browser place, the bridge's opening act";

const ownPageName = /\bownPage\b/g;
const ownPageHandedOut = /=\s*lockNavigation\(/;
const ownPageToTab = /\bopenTab\(\{[^\n]*\bopen:\s*ownPage\b/;
const openOwnPageName = /\bopenOwnPage\b/g;

/**
 * The spellings that would reach what the rules above name by another name, each refused. This guard reads text: it
 * holds these spellings and no computed name beyond them (a property reached through a value built at run time, a
 * module loaded by a name made at run time other than as below), as the C# guard reads compiled IL and not reflection.
 * - main.ts calls its navigation lock once, taking its two openers apart by name, so no other part of main.ts holds them
 *   under another name or reaches one by a computed key.
 * - `shell` is not reached by an index, aliased, taken apart or imported under another name outside the lock, so
 *   `openExternal` and `openPath` are never called by a name the rules do not read.
 * - Under src/, `listen` is not reached by an index, and a dynamic import names its module by a plain string.
 */
const lockCall = /\blockNavigation\s*\(/g;
const lockDefinition = /\bfunction\s+lockNavigation\s*\(/g;
const lockTakenApart = 'const { leave: toBrowser, ownPage } = lockNavigation(';
const shellIndexed = /\bshell\s*\[/g;
const shellAliased = /=\s*shell\b(?!\s*\.)/g;
const shellRenamed = /import\s*\{[^}]*\bshell\s+as\b/g;
const listenIndexed = /\[\s*['"`]listen['"`]\s*\]/;
const dynamicImport = /\bimport\s*\(/g;
const plainImport = /^import\s*\(\s*(?:'[^'`$\\]*'|"[^"`$\\]*")\s*\)/;

/** The line of `text` that holds `index`. */
function lineAt(text: string, index: number): string {
    const end = text.indexOf('\n', index);
    return text.slice(text.lastIndexOf('\n', index) + 1, end < 0 ? text.length : end);
}

/** The opener an `openExternal` call at `index` is written in: the nearest `const <name> = (` before it, or null. */
function openerAt(text: string, from: number, index: number): string | null {
    let name: string | null = null;
    for (const match of text.slice(from, index).matchAll(/\bconst\s+(\w+)\s*=\s*\(/g)) name = match[1] ?? null;
    return name;
}

/** The body of main.ts's `lockNavigation`, the one place a URL is handed to the system browser. */
function lockNavigationBody(text: string): { start: number; end: number } | null {
    const start = text.indexOf('function lockNavigation(');
    if (start < 0) return null;
    const end = text.indexOf('\n}\n', start);
    return end < 0 ? null : { start, end };
}

/** What is wrong with one source file, one sentence per rule it breaks; empty when nothing is. */
export function processViolations(path: string, text: string): string[] {
    const found: string[] = [];
    const named = path in processModules;

    if (childProcess.test(text) && !named) found.push(`${path} uses child_process, which only the named modules may`);
    if (utilityProcess.test(text)) found.push(`${path} uses utilityProcess, which no module of the desktop may`);
    for (const match of text.matchAll(childProcessImport)) {
        const clause = match[1] ?? '';
        if (!clause.startsWith('{')) {
            found.push(`${path} imports child_process whole, which hides what it starts`);
            continue;
        }
        for (const part of clause.slice(1, -1).split(',')) {
            const name = part.trim().split(/\s+as\s+/)[0]?.trim();
            if (name && !allowedImports.has(name)) found.push(`${path} imports ${name} from child_process; only execFile and spawn start no shell`);
        }
    }
    if (shellOption.test(text)) found.push(`${path} sets a shell option, which nothing in the desktop may`);
    const sandboxed = sandboxedShell[path];
    const shellText = sandboxed === undefined ? text : text.split(sandboxed).join("''");
    if (sandboxed !== undefined && text.split(sandboxed).length > 2) found.push(`${path} names cmd.exe more than once`);
    if (sandboxed !== undefined) {
        const seen = new Set<string>();
        for (const match of text.matchAll(runFileCall)) {
            const begins = sandboxRuns.find((each) => text.startsWith(each, match.index));
            if (begins === undefined || seen.has(begins)) found.push(`${path} calls the program runner other than to start cmd by the check's path and Codex as a turn's host, each once`);
            else seen.add(begins);
        }
    }
    if (path !== 'src/shell-path.ts' && (shellName.test(shellText) || shellVariable.test(text))) {
        found.push(`${path} names a shell, which only the login-shell lookup of macOS and Linux in src/shell-path.ts may`);
    }
    if (path === 'src/shell-path.ts' && !/process\.platform\s*!==\s*'darwin'\s*&&\s*process\.platform\s*!==\s*'linux'\)\s*return/.test(text)) {
        found.push(`${path} must return before the login shell anywhere but macOS and Linux`);
    }
    if (named && (childProcessRequire.test(text) || childProcessDynamicImport.test(text))) {
        found.push(`${path} requires child_process; a named module imports it by name, so what it starts is read`);
    }
    if ((ipcRegistration.test(text) || contentsIpcRegistration.test(text)) && path !== ipcModule) {
        found.push(`${path} registers an IPC handler; only ${ipcModule} may, since it checks the sender's origin`);
    }
    for (const match of text.matchAll(processKill)) {
        const end = text.indexOf('\n', match.index);
        const line = text.slice(text.lastIndexOf('\n', match.index) + 1, end < 0 ? text.length : end).trim();
        if (signalLines[path] !== line) found.push(`${path} signals a process with ${line}; only the two lines the guard names may`);
    }
    if (listingStart.test(text) && path !== 'src/chat-process.ts' && !(path in listingCallers)) {
        found.push(`${path} starts an agent host's model listing; only ${Object.keys(listingCallers).join(', ')} may`);
    }
    if (detachedStart.test(text) && path !== 'src/open-in-start.ts' && !(path in startDetachedCallers)) {
        found.push(`${path} starts a detached program; only ${Object.keys(startDetachedCallers).join(', ')} may`);
    }
    if (detachedModule.test(text) && path !== 'src/open-in-start.ts' && !(path in startDetachedCallers)) {
        found.push(`${path} imports the detached starter's module; only ${Object.keys(startDetachedCallers).join(', ')} may`);
    }
    const runs = [...text.matchAll(runFileCall)].length - [...text.matchAll(runFileDefinition)].length;
    if (runs > (runFileCalls[path] ?? 0)) found.push(`${path} calls the program runner ${runs} times; only the runner itself and a set-up chat's one naming run may`);
    if (!(path in runFileCalls) && (runFileNamed.test(text) || programsWhole.test(text))) {
        found.push(`${path} imports the program runner; only the runner itself and a set-up chat's naming run may`);
    }
    if (!(path in listeningModules) && (serverModule.test(text) || listening.test(text))) {
        found.push(`${path} may listen on a port; only ${Object.keys(listeningModules).join(', ')} may`);
    }
    if (killTaken.test(text)) found.push(`${path} takes kill from process, which hides what it signals`);
    if (openPath.test(text)) found.push(`${path} uses shell.openPath, which opens a file with whatever the system runs it with`);
    const lock = path === 'src/main.ts' ? lockNavigationBody(text) : null;
    const opened = new Map<string, number>();
    for (const match of text.matchAll(openExternal)) {
        if (lock === null || match.index < lock.start || match.index > lock.end) {
            found.push(`${path} hands a URL to the system browser outside main.ts's navigation lock`);
            continue;
        }
        const opener = openerAt(text, lock.start, match.index);
        if (opener === null || !(opener in navigationOpeners)) {
            found.push(`${path} hands a URL to the system browser in the navigation lock outside its named openers, ${Object.keys(navigationOpeners).join(' and ')}`);
            continue;
        }
        opened.set(opener, (opened.get(opener) ?? 0) + 1);
        if (opened.get(opener) === 2) found.push(`${path} hands a URL to the system browser more than once in ${opener}`);
    }
    const outsideLock = (index: number) => lock === null || index < lock.start || index > lock.end;
    if (path !== 'src/browser-bridge.ts') {
        for (const match of text.matchAll(openOwnPageName)) {
            const line = lineAt(text, match.index);
            if (path !== 'src/main.ts' || (outsideLock(match.index) && !/^import\s/.test(line))) {
                found.push(`${path} names the opener of the runtime's own page outside main.ts's navigation lock`);
            }
        }
    }
    if (path === 'src/main.ts') {
        const seen = { handedOut: 0, toTab: 0 };
        for (const match of text.matchAll(ownPageName)) {
            if (!outsideLock(match.index)) continue;
            const line = lineAt(text, match.index);
            // A line of a comment names it and calls nothing.
            if (/^\s*(?:\*|\/\/|\/\*)/.test(line)) continue;
            if (ownPageHandedOut.test(line)) seen.handedOut++;
            else if (ownPageToTab.test(line)) seen.toTab++;
            else found.push(`${path} uses the opener of the runtime's own page other than as ${ownPageCaller}`);
        }
        if (seen.handedOut > 1 || seen.toTab > 1) found.push(`${path} hands the opener of the runtime's own page to more than ${ownPageCaller}`);
    }
    const lockDefined = [...text.matchAll(lockDefinition)].length;
    const lockCalls = [...text.matchAll(lockCall)].length - lockDefined;
    // main.ts defines the lock and calls it once, taken apart as above; every other module names it nowhere.
    if (lockDefined > 0 ? path !== 'src/main.ts' || lockCalls !== 1 || text.split(lockTakenApart).length !== 2 : lockCalls > 0) {
        found.push(`${path} calls the navigation lock other than once, in main.ts, as ${lockTakenApart}`);
    }
    for (const pattern of [shellIndexed, shellAliased, shellRenamed]) {
        for (const match of text.matchAll(pattern)) {
            if (outsideLock(match.index)) found.push(`${path} reaches shell by another name outside main.ts's navigation lock: ${match[0].trim()}`);
        }
    }
    if (path.startsWith('src/')) {
        if (listenIndexed.test(text)) found.push(`${path} reaches listen by an index`);
        for (const match of text.matchAll(dynamicImport)) {
            if (!plainImport.test(text.slice(match.index))) found.push(`${path} imports a module by a name that is not a plain string`);
        }
    }
    return found;
}
