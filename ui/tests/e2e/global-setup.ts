import { existsSync } from 'node:fs';
import { dist, jasonExe } from './harness';

export default function globalSetup(): void {
    if (!existsSync(`${dist}/index.html`)) throw new Error(`No build at ${dist}: run npm run build first.`);
    if (!existsSync(jasonExe)) {
        throw new Error(
            `No runtime at ${jasonExe}: build it with dotnet build runtime/src/Jason.App -c Release, or set JASON_EXE.`,
        );
    }
}
