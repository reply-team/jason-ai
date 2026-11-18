// Run by the Electron binary as Node (ELECTRON_RUN_AS_NODE=1), where the file system reads an `.asar` as an archive, as
// it does in the desktop's main process: keeps the application aside with the desktop's own `keepPrevious`, then reads
// the kept copy's archive the way the application would after it was put back. One JSON line on stdout says what came of
// it. Arguments: the rollback module's path, the rollback folder, the application's folder, the file the archive holds.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const [module, rollback, application, inside] = process.argv.slice(2);
const said = { electron: process.versions.electron ?? null, archiveRead: null, kept: null, error: null, keptRead: null };
try {
    // The fixture is a real archive: this file system reads a file inside it.
    said.archiveRead = readFileSync(join(application, 'resources', 'app.asar', inside), 'utf8');
    const { keepPrevious } = await import(pathToFileURL(module).href);
    said.kept = await keepPrevious(rollback, application, '0.34.0', '0.35.0');
    said.keptRead = readFileSync(join(said.kept, 'resources', 'app.asar', inside), 'utf8');
} catch (error) {
    said.error = error instanceof Error ? error.message : String(error);
}
process.stdout.write(`${JSON.stringify(said)}\n`);
