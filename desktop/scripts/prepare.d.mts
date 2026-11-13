/** The repository's VersionPrefix, as dotnet/Directory.Build.props under `repository` names it. */
export function versionPrefix(repository?: string): string;
/** What a run prepares, from its arguments. */
export function plan(argv: string[], prefix?: string): { output: string; version: string; rid: string; executable: string | null; name: string; demo: string | null };
/** Completes a folder that holds the executable with the licence, the notices, the skills and version.json. */
export function complete(folder: string, version: string, repository?: string): void;
/** Puts the demo's sample archive into the folder at demo/jason-demo.zip, byte for byte; throws when there is none to put. */
export function carryDemo(folder: string, archive: string): void;
