// The rules the process guard holds the desktop's sources to, as one function over a file's path and text, so a test
// can show each rule refusing a planted line as well as passing the tree.

/** The modules allowed to start a process, each with the one thing it starts. */
export const processModules: Readonly<Record<string, string>> = {
    'src/cli.ts': 'the bundled jason executable',
    'src/programs.ts': 'the programs a Connect card runs: reply, npm, and the version of an agent host or of node',
    'src/shell-path.ts': "the login shell, on macOS only, for the PATH a program started from the Finder lacks",
    'scripts/prepare.mjs': 'dotnet publish of Jason.App, to bundle the jason executable',
};

/** The one module allowed to register an IPC handler, because it checks every sender's origin first. */
export const ipcModule = 'src/ipc.ts';

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
const openPath = /\bshell\s*\.\s*openPath\b/;
const openExternal = /\bshell\s*\.\s*openExternal\b/g;

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
    if (path !== 'src/shell-path.ts' && (shellName.test(text) || shellVariable.test(text))) {
        found.push(`${path} names a shell, which only the macOS login-shell lookup in src/shell-path.ts may`);
    }
    if (path === 'src/shell-path.ts' && !/process\.platform\s*!==\s*'darwin'/.test(text)) {
        found.push(`${path} must return before the login shell anywhere but macOS`);
    }
    if (named && (childProcessRequire.test(text) || childProcessDynamicImport.test(text))) {
        found.push(`${path} requires child_process; a named module imports it by name, so what it starts is read`);
    }
    if ((ipcRegistration.test(text) || contentsIpcRegistration.test(text)) && path !== ipcModule) {
        found.push(`${path} registers an IPC handler; only ${ipcModule} may, since it checks the sender's origin`);
    }
    if (openPath.test(text)) found.push(`${path} uses shell.openPath, which opens a file with whatever the system runs it with`);
    const lock = path === 'src/main.ts' ? lockNavigationBody(text) : null;
    for (const match of text.matchAll(openExternal)) {
        if (lock === null || match.index < lock.start || match.index > lock.end) {
            found.push(`${path} hands a URL to the system browser outside main.ts's navigation lock`);
        }
    }
    return found;
}
