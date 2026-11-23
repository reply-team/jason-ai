/**
 * The main process's own stdout and stderr, which whoever started Jason Desktop may stop reading while it runs: outside
 * GNOME and KDE, xdg-open runs it with the pipes `jason ui open` gave xdg-open, and that command ends after ten seconds;
 * a terminal it was started from may close. A write then fails (EPIPE, EIO), and Node raises the failure as the stream's
 * `'error'` event, which with no listener is an uncaught exception: Electron's "A JavaScript error occurred in the main
 * process". Node's console already ignores such failures; a module that writes to the stream itself does not, as the
 * `debug` package electron-updater's requests log through does where `DEBUG` names it. The desktop says what it has to
 * say in its log, never on these streams, so their failures are dropped. Imported first by `main.ts`, so this holds before
 * anything the main process loads or runs.
 */
for (const stream of [process.stdout, process.stderr]) stream.on('error', () => undefined);
