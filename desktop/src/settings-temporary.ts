/**
 * The name of the temporary file a write of the runtime's settings (the settings file, and the secrets file where the
 * layout keeps it apart) goes through before it is renamed over the file: `<file>.<process id>.tmp`. A write that stops
 * in between leaves it there with the settings' secrets in it, and the runtime's start sweeps such a file and
 * `jason export` never carries one, by the shapes `desktop/settings-temporary-files.json` holds, which a test writes from
 * the runtime's own rule. Every writer of those files names its temporary file here, and a test holds the name to that
 * file's desktop shape; a writer of any other file names its own.
 */
export const settingsTemporary = (file: string, pid: number = process.pid): string => `${file}.${pid}.tmp`;
