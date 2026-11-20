<!--
What one release says of itself. The release workflow puts everything below its title into the release's body,
after the template's opening paragraph, with {{version}} filled in as the template's is; this comment is dropped,
so it is the only comment the file holds.

The shape: the title is `# Jason <version>`, then only these sections, each at most once:
`## What is new since <version>`, `## Fixed since <version>`, `## Upgrading from <version>` and `## Known issues`.
What is below this comment is the body of a public release, read on the release page and in the mail that announces
it: a link in it is an address into the public repository or the product's own, never a relative one.

After a release this file still names it: the version pull request leaves it as it is, and the next release rewrites
it. That is safe because a tag is checked before anything is built: a tag whose file is missing, untitled or names
another version stops the release there, so notes written for one release can never ship with another. The
button's run carries the template alone in that case, and says so. A candidate's tag needs the file to name the
candidate: v1.0.0-rc.1 reads `# Jason 1.0.0-rc.1`.

While a release's notes are not yet written, the file names its version with `-unwritten`, as
`# Jason <version>-unwritten`, which no tag carries, so a placeholder can never ship; whoever writes the notes
replaces the title and the body together.
-->

# Jason 0.49.1-rc.2

On Linux, Jason is now one installation, as on macOS and Windows: Jason Desktop and the `jason` it carries, the command
line your terminal and your agents use. The one line installs it as a deb where there is apt, updated with the system's
packages, and everywhere else as an AppImage that updates itself; `jason uninstall` takes either off. On macOS and
Windows, Jason is installed, updated and removed as in 0.49.0.

## What is new since 0.49.0

- **The install line installs Jason Desktop on Linux.** As on macOS and Windows, the one line checks the application's
  download against `checksums.txt`, puts it in its place and opens no window; then the `jason` the application carries
  puts `jason` in the terminal, deploys Jason's skills for Claude Code and Codex where each is found and retires an
  older command-line install. The system decides the form, as the next two items say.
  [INSTALL.md](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/INSTALL.md) §2 says what the script does,
  step by step.
- **Where there is apt (Ubuntu, Debian, Mint): the deb.** apt installs it with an administrator's password, asked once:
  by `sudo` where the line has a terminal, and otherwise, as when an agent runs the line in your desktop session, by
  your desktop's own password dialog. It puts Jason Desktop in `/opt/Jason` for every account of the computer, `jason`
  in every terminal as `/usr/bin/jason`, and Jason's apt repository, `https://get.jasonharness.ai/apt/`, signed by the
  key the package carries. While Ubuntu's own updates hold the package system, apt waits up to ten minutes for it. A
  password not given, no session to ask in and the package system still held are each refused in their own words, and
  nothing is installed.
- **Everywhere else (Arch, Fedora and the rest): the AppImage.** It is placed for your account alone, with no password,
  at `~/Applications/jason-desktop-linux-x64.AppImage`, with a menu entry and icons, and the terminal's `jason` is
  `~/.local/bin/jason`. The line stops before anything is downloaded where the system does not let the application make
  the user namespaces its sandbox needs, which Jason never runs without (on Arch, the `linux-hardened` kernel or a
  setting that turns them off), or has no setuid `fusermount` to open the file (install `fuse3`).
- **The line runs as you, with fewer options, on Linux too.** `install.sh` run as root, with `sudo`, stops before
  anything is downloaded and says to run the line again as yourself: it asks for the password itself where the deb needs
  one. `--install-dir` and `--no-modify-path` are refused before anything is read, since the application has its own
  place and every install puts `jason` in the terminal. Run by an agent confined to its sandbox, where neither `sudo`,
  `pkexec` nor `fusermount` can act, the line stops before anything is read and says to run it in your own terminal, or
  outside the sandbox. Where the install would change a Jason Desktop that is running, it changes nothing and says to
  quit Jason first.
- **Jason Desktop sets Linux up at its first start of each version**, as on macOS and Windows: `jason` in the terminal,
  the skills in place, an older command-line install retired, and the runtime started again where an install or an
  update stopped it. A deb or an AppImage installed by hand is set up the same way.
  [desktop.md](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md) says what the first start does.
- **The deb's updates are offered, and Ubuntu's automatic updates leave them alone.** They come with the system's, from
  Jason's apt repository: on Ubuntu, Software Updater offers a new version and installs it when you say so, as
  `sudo apt update && sudo apt upgrade` does in a terminal, and `sudo apt-mark hold jason-desktop` stops them. The deb's
  `jason update apply` and `jason update rollback` install nothing and print apt's commands instead. Going back from the
  deb to 0.49.0 or earlier starts with `jason uninstall`, or with `sudo rm /etc/apt/sources.list.d/jason-desktop.sources`:
  those releases' packages carry no key for Jason's apt source, and without it every `apt update` reports the source. Where the system
  replaced Jason under its open window, the update item reads
  `Restart Jason and Its Runtime to Finish the Update to <version>` and one notice says so; the item, or a click on the
  notice, stops the runtime the old version runs, which ends a running turn and the work in progress, and starts Jason
  again, whose start finishes the update. Where the system updated Jason while no window was open and the runtime still
  runs the earlier version, the next start of Jason Desktop offers the same item and notice, and restarts nothing by
  itself; `jason runtime restart` in a terminal brings the runtime up too, and so the install line says when it is run
  over an older package. On the deb no sentence names Check for Updates, which it has no item for: where the runtime is
  newer, or a newer release is out, Jason Desktop and its System screen name Software Updater or
  `sudo apt update && sudo apt upgrade`.
- **`jason status` says when the deb's updates cannot arrive, or are not finished.** A release upgrade of Ubuntu turns
  Jason's apt source off, as it turns off every third-party source; `jason status`'s `update` check says when the source
  is off or gone, and Software & Updates → Other Software turns it on again. The same check says when the system
  installed a version this installation is not set up for yet: opening Jason Desktop sets it up, and where the runtime
  still runs the earlier version, its restart item, or `jason runtime restart`, brings the runtime up.
- **An AppImage updates itself.** Its update item, or `jason update apply` in a terminal, updates Jason Desktop and the
  `jason` it carries together, as on macOS and Windows. The update replaces the file where it is, so you need to be able
  to write to its folder, and keeps the previous file under `~/.jason/desktop/rollback/<version>/`. Jason Desktop needs
  a display even with no window, so `jason update apply` from a terminal without one, an ssh session say, is refused
  before anything is read.
- **One removal on Linux.** `jason uninstall` in a terminal, or **Uninstall Jason…** in the Jason menu, which the tray
  carries on Linux, takes off Jason Desktop, the terminal command, the skills it deployed, Open Jason at Login, the
  runtime's start at logon and an older command-line install, as on macOS and Windows; Remove What Jason Placed… is
  gone. For the deb, your system asks for an administrator's password to take the package off before anything of yours
  is removed, and where that is refused nothing of yours goes; an AppImage goes with no password, with its menu entry,
  its icons and its copies of `jason`. It keeps the data directory unless you add `--purge-data`, and the harness root
  always. Removed the system's own way instead (`sudo apt purge jason-desktop`, App Center, or deleting the AppImage),
  the application goes and your account's own files stay. For the deb, the runtime's start at logon is skipped once its
  `jason` is gone; an AppImage's copy of `jason` under `~/.jason` stays, so `jason` still answers and the runtime still
  starts at sign-in until `jason uninstall`. The login and menu entries are hidden either way. `jason uninstall` is the
  whole removal.
  [desktop.md](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#taking-it-off) says what each
  takes off.
- **A command-line install moves into Jason Desktop on Linux too.** `jason update apply` in a command-line install of an
  earlier version updates it, then installs Jason Desktop in the form the install line takes, and moves the command line
  into it, so that the terminal's `jason` is the one the application carries. The update never fails because of the
  move: where the move cannot go on yet, the install is left as it was and `jason status` says where the move stands.
- **Linux's one installation is the `jason` a terminal should find.** As on macOS and Windows, where another `jason`
  answers first by name in a new terminal, the install line ends with an error that names that file, and the `path`
  check of `jason status`, run by the `jason` Jason Desktop carries, fails, naming both files. A `jason` the application
  does not carry, a build of your own say, keeps the check it had.
- **An agent installing Jason on Linux has nothing to choose.** The install page's section for an agent, and the skill
  that operates the installation, take it through the one installation on all three systems. Where the agent's harness
  refuses the script, it downloads the file for the system, checks it against `checksums.txt` and hands it to you to
  install or open.

## Fixed since 0.49.0

- **A runtime started from Jason Desktop on Linux no longer holds the application's files open.** The runtime Jason
  Desktop starts no longer inherits the application's open files, so after an AppImage's window closes, its runtime no
  longer keeps the AppImage mounted.
- **On Windows, a browser or a terminal the CLI opens no longer holds what `jason` was handed.** A link the CLI opens and
  the terminal `jason session open` starts no longer inherit the handles the CLI holds as inheritable, its standard
  streams among them, as the runtime it starts already did not: a script or an agent that runs `jason` with its output
  captured no longer waits for that browser or terminal to close before its read ends.

## Upgrading from 0.49.0

- **A command-line install on Linux.** `jason update apply` updates it to this version and moves it into Jason Desktop,
  as above: where there is apt it installs the deb, asking for an administrator's password once, and elsewhere it places
  the AppImage. Then the terminal's `jason` is the application's, `/usr/bin/jason` for the deb and `~/.local/bin/jason`
  for the AppImage, the command-line install is retired, and a runtime the move stopped runs again from the
  application's `jason`. Where the move waits — no password given, Jason Desktop open, a system that cannot run the
  AppImage, or an AppImage older than this version already at `~/Applications/jason-desktop-linux-x64.AppImage` —
  `jason status` says why and what moves it on; once that is done, `jason update apply` carries the move on.
  [Moving a command-line install into Jason Desktop](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#moving-a-command-line-install-into-jason-desktop)
  says each step. The one line, run instead, installs Jason Desktop and retires the command-line install the same way.
- **A Jason Desktop of 0.49.0 on Linux.** The deb and the AppImage take this version by their own update item, the deb
  asking for an administrator's password as before; the one line, too, installs this version's deb over an earlier one.
  The first start of this version then sets Jason up as above, and from then on the deb's updates come with the
  system's.
- **Going back to a command-line install.** `jason update rollback` does not undo the move into Jason Desktop. On Linux,
  going back is taking Jason off with `jason uninstall`, keeping the data directory, and installing 0.49.0 or 0.48.7 by
  its own pinned line, which installs the command line on Linux, as it did: this version adds no migration to the
  database, so either opens the same data as it is.
  [Going back from 0.49](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back-from-049)
  lists the steps.

## Known issues

- **GNOME without an extension for a tray icon shows none**, as Arch's GNOME ships; Ubuntu's GNOME shows the tray.
  There, Uninstall Jason…, Open Jason at Login, Start the Runtime at Logon and Command Line cannot be reached from the
  window: `jason uninstall` in a terminal takes Jason off. The update item is at the foot of Settings' list, and on
  Ctrl+Shift+D.
- **After an AppImage updates itself, the previous version stays mounted** until the updated window quits.
- **GNOME lists no menu entry for an AppImage whose path holds a per-cent sign**, and neither do other launchers built
  on GLib.
