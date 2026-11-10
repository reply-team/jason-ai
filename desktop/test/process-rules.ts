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
const utilityProcess = /\butilityProcess\b/;
const ipcRegistration = /\bipcMain\s*\.\s*(?:handle|handleOnce|on|once|addListener|prependListener)\s*\(/;

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
    if (ipcRegistration.test(text) && path !== ipcModule) {
        found.push(`${path} registers an IPC handler; only ${ipcModule} may, since it checks the sender's origin`);
    }
    return found;
}
