import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { packageUi } from './package-lib.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
// 'v1' here and API_VERSION in src/api/version.ts are the same constant twice, because a .mjs script does not import
// TypeScript; the layout guard holds them together by checking the manifest it writes against API_VERSION.
const out = await packageUi(join(root, 'dist'), join(root, 'artifacts', 'jason-ui.zip'), {
    version: process.env.JASON_VERSION ?? '0.0.0-local',
    apiVersion: 'v1',
});
console.log(out);
