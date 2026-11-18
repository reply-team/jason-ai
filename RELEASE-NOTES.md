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

# Jason 0.49.0-rc.1

This is a release candidate, published for a walk before 0.49.0 and not for general use: it is never the latest release,
so no install line and no update reaches it, and it is installed only by naming its version.

On macOS and Windows, Jason is now one installation: Jason Desktop and the `jason` it carries, the command line your
terminal and your agents use. One line installs it, one update moves it on, and one removal takes it off. On Linux,
Jason is installed, updated and removed as it was.

## What is new since 0.48.5

- **The install line installs Jason Desktop.** On macOS and Windows the one line installs the application, puts `jason`
  in the terminal and deploys Jason's skills for Claude Code and Codex, where each is found (`~/.claude/skills`, and
  `~/.agents/skills`, which Copilot, Cursor and Gemini CLI read too); it opens no window. Another agent host takes the
  skills with `jason skills install --host <your host>`. On a Mac the line installs on Apple silicon; on an Intel Mac
  the application is installed from its
  [disk image](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/INSTALL.md#jason-desktop) and opened from
  Applications. [INSTALL.md](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/INSTALL.md) §2 says what
  each script does.
- **The scripts take fewer options on macOS and Windows.** `--install-dir` and `--no-modify-path` (`-InstallDir` and
  `-NoModifyPath` in PowerShell) chose a folder for the command line and left the PATH alone. The application has its
  own place and puts `jason` in the terminal itself, so on macOS and Windows the scripts no longer take them; on Linux
  they are taken as before.
- **The install runs as you.** On macOS and Windows, Jason Desktop and everything it sets up are your account's own,
  so `install.sh` run as root, with `sudo`, and `install.ps1` run in an elevated PowerShell — an administrator's, or
  another account's than the one signed in — stop before anything is downloaded, and say to run the line again as
  yourself; `jason install` run so refuses with `install_elevated`. On Linux a run as root installs as any run does.
- **One update.** `jason update apply` updates Jason Desktop and the `jason` it carries together, through the
  application's own updater, one update at a time. Check for Updates… in Jason Desktop's menu updates the same way. An
  installer that finds Jason Desktop running waits for it to quit or refuses, and never closes it.
- **A terminal's update is refused while Jason Desktop shows the Demo.** `jason update apply` is refused as `demo_open`
  before anything is checked, so nothing is stopped, kept or downloaded. Leaving the Demo, or quitting Jason Desktop, and
  running it again takes it.
- **A command-line install moves into Jason Desktop.** On macOS and Windows, `jason update apply` in a command-line
  install of an earlier version updates it and then moves it into Jason Desktop, so that the terminal's `jason` is the
  one the application carries. The update never fails because of the move: where the move cannot go on, the install is
  left as it was and `jason status` says where the move stands. How to go back to 0.48.5 afterwards, with your data
  kept, is in
  [Going back from 0.49](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back-from-049).
- **One removal.** `jason uninstall` takes off Jason Desktop, the terminal command, the skills it deployed, the start at
  logon and an older command-line install; Installed apps on Windows and **Uninstall Jason…** in the Jason menu on macOS
  do the same. It keeps the data directory unless you add `--purge-data`.
- **Jason Desktop finishes each version at its first start.** It puts `jason` in the terminal and the skills in place,
  finishes an update a terminal started, and starts the runtime an install or an update stopped. An application opened
  from its disk image, or from where it was downloaded, sets nothing up: move it into Applications and open it there.
  [desktop.md](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md) says what the first start does.
- **`jason status` says which `jason` a terminal finds.** On macOS and Windows, where `jason` by name is another file
  than the `jason` answering, its `path` check fails, naming both files, and `ready` is false until that file is removed
  or its folder taken off the PATH. On Linux the check passes there and says that two installations are on this
  machine.
- **`jason runtime drain` and `jason runtime resume`.** A drained runtime answers every question and claims no new work
  until it is resumed or restarted, and `jason status` says when the runtime is draining.
- **A runtime never starts on data a newer Jason changed.** It refuses with `data_newer`, changes nothing in the data
  directory, and says to update Jason.
- **Programs are found only through full entries of the search path.** Jason starts every program by its full path,
  and only the entries of the search path that are full paths are searched: an empty entry, `.` or a relative folder
  names wherever the runtime happens to stand, and a program found only there is `executable_missing`.
- **Approval cards for a paid people search show its definition** and arguments, in Jason Desktop's chat as on
  Attention.

## Upgrading from 0.48.5

- **Installing this candidate.** It is installed by naming its version. On macOS:
  ```sh
  curl -fsSL https://get.jasonharness.ai/releases/download/v{{version}}/install.sh | sh -s -- --version {{version}}
  ```
  On Windows (PowerShell):
  ```powershell
  & ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v{{version}}/install.ps1))) -Version {{version}}
  ```
  A command-line install of 0.48 takes it with `jason update apply --version {{version}}`, and moves into Jason Desktop
  as above.
- **Going back.** `jason update rollback` does not undo the move into Jason Desktop. Going back is taking Jason off with
  `jason uninstall`, keeping the data directory, and installing 0.48.5 by its own pinned line: 0.49 adds no migration to
  the database, so 0.48.5 opens the same data as it is.
  [Going back from 0.49](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back-from-049)
  lists the steps.
- **Homebrew.** The Homebrew formula carries the command line alone and stays at 0.48.5; on a Mac and on Linux the one
  line is the way to 0.49, and
  [the update page](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#6-installing)
  says how to come from a Homebrew install.
